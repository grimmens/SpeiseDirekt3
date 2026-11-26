# Spec: global admin and paid tenants (stage 1)

- **Status:** approved by the operator on 2026-11-26. This file is the brief for the implementation.
- **Branch:** `feature/global-admin`, based on `master` `1dccf28` (PR #25 merged).
- **Stage 1:** only the operator, as global admin, decides per customer between free and paid, with a toggle.
- **Stage 2:** PayPal sets the same role. Not part of this work.

## 1. Goal

- **Two new Identity roles,** assigned through ASP.NET Core Identity user roles (`AspNetUserRoles`):
  - `GlobalAdmin` administers all customers.
  - `PaidTenant` marks a paying customer.
- **Free tenant** (without `PaidTenant`): at most **1 menu** and **1 QR code**.
- **Paid tenant:** any number of menus and QR codes.
- **Admin page** `/admin/kunden`: lists all customers, each with a "paid" toggle.

## 2. Current state (read these first)

- **`SpeiseDirekt.Model/Model/Model/TenantSubscription.cs`:**
  - Fields: `IsPaid`, `SubscriptionStart`, `SubscriptionEnd`, `MaxUsers` and `PlanName`, keyed by the owner's user id.
  - The table is empty in production (preview copy of 2026-11-25: 0 rows).
- **`SpeiseDirekt.Model/Infrastructure/PaidTenantHandler.cs` + `PolicyNames.PaidTenant`:**
  - Despite the name, this policy means "may create a QR code": paid (`IsPaid`), or fewer than **100** QR codes.
  - Menus have no limit at all.
  - Used by:
    - `Components/QrCodes/QrCodeEdit.razor`, in `CanCreateQrCodeAsync`, which also guards a directly opened `/qrcode/edit`;
    - `Components/QrCodes/QRCodesPage.razor`, through `Components/Payments/PaidTenantComponentBase.cs` (`CanCreateQrCode`);
    - `Components/Pages/DashboardPage.razor` (`canCreateQrCode`);
    - `AnalyticsPage.razor`, which inherits the base but does not use it.
  - The E2E tests `QrCodeLimitTests` (99/100) cover it.
- **Identity:**
  - `SpeiseDirekt3/Program.cs` uses `AddIdentityCore<ApplicationUser>()` **without** `AddRoles`.
  - `SpeiseDirekt.Api/Program.cs` uses `AddIdentityApiEndpoints<ApplicationUser>()`.
  - `ApplicationDbContext : IdentityDbContext<ApplicationUser>`, so the tables `AspNetRoles` and `AspNetUserRoles` already
    exist.
  - Production holds six legacy roles assigned to nobody: `Manager`, `Cashier`, `Driver`, `Employee`, `Customer` and
    `GeneralManager`. No code references them.
- **Tenants:**
  - The tenant is the owner's account: `ApplicationUser.TenantOwnerId == null`.
  - Members have `TenantOwnerId` set and a `TenantUser` row (`Role`, `Permissions`, `IsActive`).
  - `IUserIdProvider.GetUserId()` already returns the owner id for the current user (the `TenantOwnerId` claim of a
    member, otherwise `NameIdentifier`), and `Guid.Empty` when the user is anonymous. Inside an authorization handler,
    read the same claims (see `PaidTenantHandler`).
- **Tenant query filters:**
  - `ApplicationDbContext` filters `Menu`, `QRCode`, `RestaurantProfile` and others by the current user's tenant
    (`IUserIdProvider`).
  - Cross-tenant queries need `IgnoreQueryFilters()`.
  - Compare owner ids as **Guid**, not as strings. A GUID column reads back upper case and the Identity id is lower
    case; that is the old bug noted in `PaidTenantHandler`.
- **Where menus are created:**
  - `Components/Menu/Components/MenuEditForm.razor` (`/menu/edit/{Id:guid?}`, `Context.Menus.Add`);
  - `IMenuTranslationService.CreateTranslationAsync` (`MenuTranslationService`), the "Add translation" action in
    `MenuLanguageVersions.razor`;
  - `SpeiseDirekt.Api/Controllers/MenusController.Create`;
  - links to `menu/edit` on `/menus` (`MenuPage.razor`) and on the dashboard (the action, the empty state and checklist
    step 1).
- **Where QR codes are created:**
  - `QrCodeEdit.razor` (new code);
  - `SpeiseDirekt.Api/Controllers/QrCodesController.Create`, which has no limit at all today;
  - buttons on `/qrcodes` and the dashboard.
- **Back-office navigation:**
  - `Components/Layout/ShellNavigation.cs` holds the permission-based entries.
  - The top bar is `NavMenu.razor` and the phone drawer is `NavDrawer.razor`.
- **`Routes.razor`:** `NotAuthorized` always redirects to the login page, also for signed-in users.
- **Bypasses:** the preview seed (`Preview/PreviewSeeder.cs`) and the dev menu generator (`MenuItemGenerator`, off in
  production) insert directly. They may keep bypassing the limits.

## 3. Decisions (operator, 2026-11-26)

1. **The role names are exactly `GlobalAdmin` and `PaidTenant`.** No other roles.
2. **`PaidTenant` is assigned to the owner account.** Members inherit it through their tenant. A `PaidTenant` role on
   a member account has no effect.
3. **Free limits:**
   - 1 menu and 1 QR code; a **language version counts as a menu**.
   - Paid tenants have no limit.
   - Configurable as `Plans:Free:MaxMenus` / `Plans:Free:MaxQrCodes`, defaults 1 / 1, in `appsettings.json`.
4. **Existing data stays:**
   - Nothing is deleted, hidden or disabled.
   - A tenant above the limit keeps all its menus and QR codes. Editing still works, and guest pages and printed QR
     codes keep working.
   - Only **creating** is blocked.
5. **The paid status is read from the database at the moment it matters,** never from the sign-in cookie. Toggling
   takes effect immediately, without a new sign-in, also for members who are already signed in.
6. **`GlobalAdmin` is not assignable in the UI.**
   - The app syncs it at every start from `Admin:GlobalAdminEmails` in `appsettings.json`.
   - The value is already set on this branch: `["bernhard_grimmy@hotmail.com"]`. The operator chose `appsettings.json`
     although the repository is public.
   - The sync is a full sync: listed accounts get the role, and every other account loses it.
   - An address without an account is logged and skipped.
   - The preview runs on a copy of production, so the operator is global admin there too.
7. **`TenantSubscription` stays** for stage 2, but **`IsPaid` is no longer read anywhere**.
   - `MaxUsers` (team limit) keeps working as today: without a row there is no limit.
   - Do not drop columns now.
8. **The six legacy roles are deleted** by a data-only migration, and only where no user and no role claim references
   them.
9. **Privacy policy:** no change.
   - `/datenschutz` point 3 already says "Angaben zu Ihrem Tarif, soweit vorhanden".
   - Log lines carry internal ids only, never e-mail addresses.

## 4. Implementation

### 4.1 Roles and setup
- **Role names:** add `SpeiseDirekt.Model/Infrastructure/AppRoles.cs` with `GlobalAdmin = "GlobalAdmin"` and
  `PaidTenant = "PaidTenant"`.
- **Role stores, in both `Program.cs`:**
  - Web: `AddIdentityCore<ApplicationUser>(…).AddRoles<IdentityRole>().AddEntityFrameworkStores<…>()`. `AddRoles` must
    come **before** `AddEntityFrameworkStores`, otherwise the role store is not registered.
  - API: `.AddRoles<IdentityRole>()` on `AddIdentityApiEndpoints<ApplicationUser>()`.
- **Startup step `RoleSetup`** (a hosted service or an awaited startup step; in the E2E environment after
  `E2EDatabaseInitializer`, which uses `EnsureCreated` and no migrations):
  - make sure both roles exist (`RoleManager`, idempotent);
  - sync `GlobalAdmin` from `Admin:GlobalAdminEmails`: match by `NormalizedEmail`, add the missing, remove the others;
  - log `GlobalAdmin sync: {Added} added, {Removed} removed, {Unknown} address(es) without an account`, without
    addresses;
  - catch errors and log them. A failing sync must not stop the app.
- **Options class** `AdminOptions` (`Admin:GlobalAdminEmails`, `string[]`, default empty). Bind it in the web app only;
  the API needs no admin.

### 4.2 Plan service (single source of truth)
- **`ITenantPlanService` / `TenantPlanService` in `SpeiseDirekt.Model`,** registered in the web app and the API. It
  works with an explicit owner id and must not depend on the tenant query filter:
  - `Task<bool> IsPaidAsync(string ownerId)` checks whether the owner has the role `PaidTenant`.
    - Query `UserRoles` joined with `Roles` (`NormalizedName == "PAIDTENANT"`) through `ApplicationDbContext`, so it
      also works where no `UserManager` is available.
  - `Task<PlanUsage> GetUsageAsync(string ownerId)` returns `IsPaid`, `MenuCount`, `QrCodeCount`, `MaxMenus` and
    `MaxQrCodes` (null = unlimited).
    - Counts use `IgnoreQueryFilters()` and a Guid comparison of `ApplicationUserId`.
  - `Task<bool> CanCreateMenuAsync(string ownerId)` and `Task<bool> CanCreateQrCodeAsync(string ownerId)`.
- **Owner id:** callers in the UI, the API and the services pass `IUserIdProvider.GetUserId()`. No new helper is needed.
  `Guid.Empty` (anonymous) never passes.
- **`PaidTenantHandler` / `PolicyNames.PaidTenant`:**
  - From now on they mean exactly "the tenant has the role `PaidTenant`".
  - Rewrite the handler on the plan service: no `TenantSubscriptions`, no QR count.
  - Creation checks use the plan service, not this policy.
- **`PaidTenantComponentBase`:** let it expose `CanCreateQrCode` from the plan service, or remove it and call the service
  in `QRCodesPage`. Remove the unused inheritance from `AnalyticsPage`.

### 4.3 Enforcement (server side, at save time; the UI only mirrors it)
- **Menus:**
  - `MenuEditForm`: a new menu, with the id null on save and on open.
    - Opening `/menu/edit` without an id at the limit redirects to `/menus`, as `QrCodeEdit` does for QR codes.
    - The save path checks again and shows the hint instead of saving.
  - `MenuTranslationService.CreateTranslationAsync`: at the limit it returns `Succeeded = false` with the hint text.
    The service gets the owner id from `IUserIdProvider`, the same way its query filters get the tenant.
  - API `MenusController.Create`: return `BadRequest("Menu limit reached (1). Upgrade your plan to add more menus.")`,
    in the same style as `UsersController`'s user limit. If the API has a translation endpoint, give it the same check.
- **QR codes:**
  - `QrCodeEdit`, for new codes: replace the policy call by the plan service, keep the redirect, and check again on save.
  - API `QrCodesController.Create`: return `BadRequest("QR code limit reached (1). Upgrade your plan to add more QR codes.")`.
- **The check is count-then-insert.** A race between two simultaneous creates is acceptable and needs no locking.

### 4.4 UI for the free tier
- **At the limit,** the create action is disabled and does not disappear, so the reason stays visible. A short hint
  is shown next to it.
  - Places: `/menus` "New menu", the dashboard action and checklist step 1, the "Add translation" panel, `/qrcodes`
    (both buttons) and the dashboard QR action.
  - Texts in the existing `.resx` files (`MenusText`, `QrCodesText`, `DashboardText`; de + en):
    - Menu:
      - de „Im kostenlosen Zugang ist eine Speisekarte enthalten. Für mehr wenden Sie sich an uns.“
      - en "The free plan includes one menu. For more, please contact us."
    - QR code:
      - de „Im kostenlosen Zugang ist ein QR-Code enthalten. Für mehr wenden Sie sich an uns.“
      - en "The free plan includes one QR code. For more, please contact us."
    - Language version: the menu text.
  - If `Landing:ContactEmail` (`LandingSettings`) is set, "wenden Sie sich an uns" / "contact us" becomes a `mailto:`
    link. It is empty today, and then the plain text stays.
  - The texts assume the limit 1. If the config is ever raised, the texts must be changed with it (note this in
    `CLAUDE.md`).
- **Paid tenants and tenants below the limit** see no hint.
- **No prices** anywhere: there is no pricing decision yet.

### 4.5 Admin page `/admin/kunden`
- **Access:**
  - An interactive-server page in the back-office layout with `[Authorize]` (sign-in required).
  - In `OnInitializedAsync` it checks `GlobalAdmin` **in the database** (`UserManager.IsInRoleAsync`, or a
    `GlobalAdminRequirement` handler that does that).
  - Non-admins get a full load of `/Account/AccessDenied`, exactly like the area pages do without their view permission
    (`Shared/PageAccess.RequireAsync`). No customer data is rendered before the check.
  - Do not use `Routes.razor`'s `NotAuthorized`: it would send a signed-in user to the login page.
  - Add the policy `PolicyNames.GlobalAdmin` for reuse.
- **Navigation:**
  - An extra entry "Admin" (`ShellText.Nav_Admin`, de/en "Admin") in the top bar and the drawer, shown only to global
    admins.
  - Keep it outside the permission-based `ShellNavigation.Items`, for example as `ShellNavigation.AdminItem` plus a
    flag.
  - `SectionOf("admin/…")` returns `"admin"`.
- **Service:** `ITenantAdminService` in `SpeiseDirekt.Model`. Cross-tenant, with `IgnoreQueryFilters()` everywhere.
  - `ListTenantsAsync(string? search)` returns one row per owner account (`TenantOwnerId == null`):
    - the owner user id;
    - the e-mail;
    - the restaurant name (`RestaurantProfile.Name`, or empty);
    - the number of menus, QR codes and team members (`TenantUsers` of the owner; active/inactive);
    - the last guest view (max `MenuView.ViewedAt` over the tenant's menus, or null);
    - `IsPaid`;
    - `IsGlobalAdmin`, shown as a badge.
  - The search matches the e-mail or the restaurant name, case-insensitively. Sort by restaurant name, then by e-mail.
  - `ListMembersAsync(string ownerId)` returns the e-mail, the role (`TenantRole`) and whether the member is active.
    The admin can only look at them.
  - `SetPaidAsync(string ownerId, bool paid, string adminUserId)`:
    - uses `UserManager.AddToRoleAsync` / `RemoveFromRoleAsync` and is idempotent;
    - accepts only owner accounts and rejects a member id;
    - logs `GlobalAdmin {AdminId} set PaidTenant for tenant {OwnerId}: {Old} -> {New}` with ids only.
  - Write nothing else. The admin never touches tenant content. Note that `ApplicationDbContext` stamps
    `ApplicationUserId` on tracked entities.
- **Page:**
  - A title and a search field.
  - A table on wide screens and cards at 390 px. Keep the phone gutter rule from the repo `CLAUDE.md`: the gutter comes
    from `MainLayout` only.
  - Columns: Restaurant, E-Mail, Speisekarten, QR-Codes, Team, Letzter Gästeaufruf (date in the request culture),
    Bezahlt (toggle).
  - Rows can be expanded to show the members.
  - The toggle saves immediately, with no dialog, and shows a short "Gespeichert"/"Saved". On an error it switches back
    and shows a message.
  - Paging is not needed yet (few tenants). Put a hard cap of 500 rows on the query.
  - Texts in a new `Resources/AdminText.resx` + `AdminText.de.resx`, following the existing pattern.
- **Not in stage 1:**
  - locking or deleting accounts;
  - signing in as a customer;
  - editing e-mails;
  - an admin API;
  - a registration date: no column stores it, and do not add one now.

### 4.6 Migration
- **One data-only migration,** `RemoveUnusedLegacyRoles` (`migrationBuilder.Sql`; the model snapshot stays the same).
- **It deletes the six roles above,** and only those with no `AspNetUserRoles` and no `AspNetRoleClaims` rows. The SQL
  must run on SQL Server; SQLite is not needed, because E2E and the API tests use `EnsureCreated`.
- **`Down`:** empty, with a comment saying the roles were unused.
- **The new roles are created by `RoleSetup` at startup,** not by the migration, so the SQLite environments get them
  too.
- **Deploy:** `deploy.yml` takes the backup before the bundle as usual.

### 4.7 Tests
- **API integration tests** (`SpeiseDirekt.Api.IntegrationTests`):
  - Give the seeded test user (`TestSeedData`) `PaidTenant` by default, so existing tests that create several menus or
    QR codes keep passing.
  - New `TenantPlanServiceTests`:
    - free vs paid;
    - counts across query filters;
    - Guid comparison with an upper-case stored id;
    - a language version counts.
  - New `PlanLimitApiTests`:
    - for a free tenant, use a second seeded owner through `TestAuthHandler.UserHeader`, or remove the role from the
      test user;
    - a free tenant at the limit gets 400 on `POST /api/menus` and `POST /api/qrcodes`;
    - below the limit it gets 201/200;
    - a paid tenant is unlimited.
  - `RoleSetup` tests:
    - creates both roles;
    - adds a listed address;
    - removes an unlisted admin;
    - skips an unknown address;
    - is idempotent.
- **E2E** (`SpeiseDirekt3.E2ETests`):
  - The seed owner `owner@e2e.test` gets `PaidTenant` by default.
  - The E2E app config sets `Admin:GlobalAdminEmails` to a dedicated `admin@e2e.test`, an owner account of its own in
    the seed.
  - `QrCodeLimitTests`: rewrite them from 100 to the free limit 1 with a free owner. Keep "editing an existing QR code
    is still allowed".
  - New `PlanLimitTests` (free owner):
    - the first menu can be created and the second is blocked: the button is disabled, the hint is shown, and
      `/menu/edit` redirects;
    - "Add translation" is blocked at the limit;
    - the first QR code works and the second is blocked;
    - a tenant seeded with 3 menus and 2 QR codes can still edit them, and its guest pages answer 200;
    - de and en texts.
  - New `GlobalAdminTests`:
    - the admin sees "Admin" and the list, and the search works;
    - toggling a tenant to paid lets that tenant's already signed-in owner create a second menu without signing in
      again;
    - toggling back blocks it again, and the content stays;
    - a non-admin owner and a member see no "Admin" entry, and `/admin/kunden` sends them to `/Account/AccessDenied`
      without showing any customer data;
    - an anonymous user goes to the login page;
    - at 390 px there is no horizontal overflow.
- **Run everything at the end:**
  - the full API suite;
  - the full E2E suite (on this host, `QrRedirectTests.MalformedQrId_IsNotFound_NoServerError` is a known Chromium
    artefact);
  - `tools/tailwind/build.ps1` / `build.sh`, then commit `wwwroot/css/tailwind.css` with the markup and make sure
    `--check` passes.

### 4.8 Docs
- **Repository `CLAUDE.md`:** add a section "Plans and roles" covering:
  - the roles, and where `PaidTenant` lives (the owner);
  - `ITenantPlanService` as the only check;
  - the free limits and their config, and that the texts assume 1;
  - `Admin:GlobalAdminEmails` with its full sync at startup;
  - the admin page;
  - `IsPaid` is unused, with stage 2 to follow;
  - the bypasses (preview seed, dev generator).
- **Replace** the old text about `PaidTenant` meaning "less than 100 QR codes" wherever it appears.

## 5. Acceptance

- **Signed-in operator in the preview / production:**
  - sees "Admin" and `/admin/kunden` listing all customers;
  - can switch their own account to paid with the toggle, and can then create any number of menus and QR codes.
- **A free customer:**
  - can create exactly one menu and one QR code;
  - sees the hint at the limit;
  - cannot get around it via a direct URL, a language version or the API.
- **Existing content above the limit** stays editable, and guest pages and QR redirects are unchanged.
- **Non-admins** see no admin entry and no customer data.
- **The migration** deletes the six unused roles and nothing else.
- **Everything is green:** API, E2E and the Tailwind check.

## 6. After deploying (operator)

- **Production:** sign in, open `/admin/kunden` and switch your own account to "Bezahlt". It has more than one menu,
  so creating would be blocked until then.
- **Already signed in before the deploy?** The admin entry appears without a new sign-in, because the role is read from
  the database.
- **Stage 2 (PayPal):** the webhook sets and removes `PaidTenant` through `ITenantAdminService.SetPaidAsync` or a
  sibling, and the `TenantSubscription` schema gets designed then. The page counter's planned event "Premium gebucht"
  belongs to stage 2 as well; stage 1 sends no new event.

## 7. Process

- **Commits:** small and logical (roles + setup, plan service + enforcement, UI hints, admin page, migration, tests,
  docs). English messages in the style of the history.
- **Never commit** secrets or local settings.
- **Push the branch** and open a PR against `master`. Every open PR gets the preview `https://preview.speisedirekt.de`
  automatically. The migration runs there on a copy of production.
- **Merge:** done by the operator.
