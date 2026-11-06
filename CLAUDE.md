# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Solution Overview

`SpeiseDirekt3.sln` contains five .NET 9 projects:

| Project | Type | Purpose |
|---|---|---|
| `SpeiseDirekt3/` | Blazor Server web app | Restaurant back office (menus, categories, items, allergens, combos, QR codes, analytics, user management) and public menu display |
| `SpeiseDirekt.Api/` | ASP.NET Core Web API | REST API (Swagger, Identity API endpoints) over the same data |
| `SpeiseDirekt.Model/` | Class library (root namespace `SpeiseDirekt`) | Shared core: entities, DbContext, migrations, repositories, services, authorization, DI setup |
| `SpeiseDirekt.Api.IntegrationTests/` | xUnit test project | Integration tests for every API controller |
| `SpeiseDirekt3.E2ETests/` | xUnit + Playwright test project | Browser end-to-end tests for the Blazor app (public pages, QR resolution, auth, back office) |

Both `SpeiseDirekt3` and `SpeiseDirekt.Api` reference `SpeiseDirekt.Model` and share one SQL Server database (connection string `server`).

## Development Commands

Run from the repository root.

### Building and Running
- **Build everything**: `dotnet build`
- **Run Blazor app**: `dotnet run --project SpeiseDirekt3` (https://localhost:7051, http://localhost:5010)
- **Run Web API**: `dotnet run --project SpeiseDirekt.Api` (https://localhost:7254, http://localhost:5289; Swagger UI at `/swagger` in Development)
- **Watch mode**: `dotnet watch run --project SpeiseDirekt3`
- **Run Blazor app without SQL Server**: set `ASPNETCORE_ENVIRONMENT=E2E` (`Program.E2EEnvironment`): SQLite (connection string `e2e`, default `speisedirekt-e2e.db`), schema via `EnsureCreated()` (`E2EDatabaseInitializer`, no migrations), static web assets enabled. The database starts empty.
- **Manual API requests**: `SpeiseDirekt.Api/SpeiseDirekt.Api.http`

### Testing
- **Run all tests**: `dotnet test`
- **Run a single test class**: `dotnet test --filter "FullyQualifiedName~MenusControllerTests"`
- **Run a single test**: `dotnet test --filter "FullyQualifiedName~MenusControllerTests.GetAll_ReturnsSeededMenus"`
- **Run only the E2E tests**: `dotnet test SpeiseDirekt3.E2ETests` (no SQL Server needed; Chromium is installed by Playwright on first run, or set `PLAYWRIGHT_CHROMIUM_EXECUTABLE` to an existing binary)

### Database Operations
Migrations live in `SpeiseDirekt.Model/Data/Migrations`, so the EF commands need both project flags:
- **Create migration**: `dotnet ef migrations add <MigrationName> --project SpeiseDirekt.Model --startup-project SpeiseDirekt3`
- **Update database**: `dotnet ef database update --project SpeiseDirekt.Model --startup-project SpeiseDirekt3`
- **Remove last migration**: `dotnet ef migrations remove --project SpeiseDirekt.Model --startup-project SpeiseDirekt3`

### Deployment
CI/CD since 2026-10-31 (`.github/workflows/`), running on the self-hosted ARM64 runner of the CI host (`actions-runner-speisedirekt`) and deploying to server2 via SSH/rsync. No Docker. Only the Blazor app `SpeiseDirekt3` is deployed; the Web API is not hosted anywhere.
- **`deploy.yml`** — on every push to `master`: restore, build, `dotnet test` (a red test stops the deployment), `dotnet publish` of the Blazor app (portable, framework-dependent — server2 runs it with `dotnet SpeiseDirekt3.dll`), EF migrations bundle for linux-x64, rsync to `server2:/var/www/SpeiseDirekt.next`, **COPY_ONLY backup of the `speisedirekt` database** to `/var/opt/mssql/backup/deploy` (the last ten are kept), bundle against the connection string from `Speisedirekt.service`, directory swap, restart, smoke test of https://app.speisedirekt.de.
- **`ci.yml`** — PR preview at https://preview.speisedirekt.de: every open PR against `master` is deployed when opened and on every push (no label needed). One slot — the most recently pushed PR wins. Build of the merge commit (master + PR), tests, copy of the production database to `speisedirekt-pr-preview` (`deploy/preview/db-kopie.sql`, run with `sa` from the CI host), migrations against the copy, `speisedirekt-preview.service` (user `sdpreview`, port 5013) on server2, comment in the PR with the applied migrations. `workflow_dispatch` with a PR number redeploys a preview by hand.
- **`pr-preview-abbau.yml`** — stops the preview when its PR is closed; the database copy stays until the next run.
- `.config/dotnet-tools.json` pins `dotnet-ef` for the bundle (`dotnet tool restore`). Host-side prerequisites (runner, secrets, unit files, Apache vhosts, SQL login) are documented in the CI host's `CLAUDE.md`.
- `SpeiseDirekt3/SendToProd.ps1` (manual publish + `scp`) is superseded by the pipeline; the file is not tracked in the repository.

## Architecture

### Technology Stack
- **Framework**: ASP.NET Core 9.0 (Blazor Server with Interactive Server render mode; Web API with controllers)
- **Database**: SQL Server via Entity Framework Core 9 (split queries enabled)
- **Authentication**: ASP.NET Core Identity. Cookies in the Blazor app; `AddIdentityApiEndpoints` / `MapIdentityApi` in the API
- **UI**: Tailwind CSS via the Play CDN `cdn.tailwindcss.com` (config inline in `SpeiseDirekt3/Components/App.razor`) — the **only** remaining external asset. Everything else is in the repo: Bootstrap 5.3.3 (`wwwroot/lib/bootstrap`), Chart.js 4.6.1 + chartjs-adapter-date-fns 3.0.0 (`wwwroot/lib/chartjs`, analytics page), fonts (`wwwroot/fonts`, `css/fonts.css`: Inter, Manrope and the theme fonts, woff2, latin + latin-ext, SIL OFL licenses in `fonts/licenses`). Icons are inline SVGs (no icon font).
- **AI**: Microsoft.Extensions.AI with Ollama (`llama3.2:latest` at `http://localhost:11434`, hard-coded in `SpeiseDirekt3/Program.cs`)
- **Images**: SixLabors.ImageSharp for resizing; images are stored as bytes in the database (`Image` entity)
- **QR codes**: QRCoder (PNG generated in `SpeiseDirekt3/Controller/ImageController.cs`)
- **Tests**: xUnit, FluentAssertions, `WebApplicationFactory`, SQLite (in-memory for API tests, temp file for E2E), Microsoft.Playwright

### SpeiseDirekt.Model (shared core)
- `Model/Model/`: one file per entity
  - `Menu` (with `DesignTheme`: Elegant, Modern, Classic, Minimal, Fancy, Dark — styles the guest view, see below; and `MenuLanguage`) → `Category` → `MenuItem` (dietary labels `IsVegan`, `IsVegetarian`, `IsSpicy`; `Description` is optional). `Category` and `MenuItem` carry `SortOrder` (migration `AddCategoryAndMenuItemSortOrder`, default 0): every list, the guest display and the API order by `SortOrder` then name through `Infrastructure/DisplayOrder.cs`; new entries get last + 1 within their parent, the back office reorders with up/down arrows.
  - `Allergen`: belongs to a menu (`MenuId`, short `Code` + `Name`); many-to-many with `MenuItem` via join table `MenuItemAllergen`
  - `MenuCombo` / `MenuComboItem`: combo offers per menu, triggered by a `TriggerMenuItem`, with a `ComboPrice` and a list of included items
  - `QRCode`: points to a menu directly, or is time-table based (`TimeTableEntry`) or calendar based (`CalendarEntry`). `IValidatableObject` checks for overlapping time windows and date ranges.
  - `TenantSubscription`: paid vs. free tier, `MaxUsers`, `PlanName`
  - `Image`, `TranslationCache`
  - Analytics: `MenuView`, `MenuItemClick` (not tenant-scoped, FKs with `NoAction`)
- `Model/PermissionModels.cs`: `TenantRole`, `[Flags] Permission` enum, `TenantUser` (sub-account of a tenant)
- `Model/Dto/`: shared DTOs (`CreateMenuItemDto`, `BreadcrumbItem`)
- `Data/ApplicationDbContext.cs`: Identity DbContext, global query filters, relationship config, automatic `ApplicationUserId` assignment
- `Data/ApplicationUser.cs`: Identity user with `TenantOwnerId` (null = tenant owner, set = sub-account)
- `Infrastructure/ServiceExtensions.cs`: `ConfigureServices()` registers the shared services and repositories (DbContext-dependent ones as `Transient`). Both hosts call it.
- `Infrastructure/`: authorization (see below), `TenantClaimsTransformation`, `EditFormExtensions`, `DisplayOrder` (ordering helpers), `PriceFormat.Euro` (back-office prices as `€ 13.90`, invariant culture; the guest components format their own)
- `Repository/`: repositories for Menu, Category, MenuItem, QrCode, Image and MenuCombo (used by the API)
- `ServiceInterface/` and `ServiceImplementation/`: services (see below)

### Multi-Tenant Architecture
- A **tenant** is the owner account. Sub-accounts (`ApplicationUser.TenantOwnerId` set, plus a `TenantUser` row with role and permissions) work on the owner's data.
- Tenant-owned entities implement `IAppUserEntity` (`ApplicationUserId` = tenant owner ID).
- `ApplicationDbContext.OnModelCreating` applies global query filters on `ApplicationUserId` for MenuItem, Category, Menu, QRCode, TimeTableEntry, CalendarEntry, Allergen, MenuCombo and MenuComboItem.
- `ChangeTracker` events set `ApplicationUserId` on Added/Modified entities from `IUserIdProvider`. Adding, modifying or deleting an `IAppUserEntity` **throws** if no authenticated user is present; merely tracking queried entities (anonymous public pages) is allowed.
- `TenantClaimsTransformation` adds `TenantOwnerId` and `TenantRole` claims for sub-accounts.
- `IUserIdProvider.GetUserId()` returns the **tenant owner ID** (the `TenantOwnerId` claim for sub-accounts, otherwise `NameIdentifier`) and is what the query filters use. `GetActualUserId()` returns the logged-in user. Both return `Guid.Empty` when unauthenticated.
- Public/anonymous paths (QR code resolution, public menu display) use `IgnoreQueryFilters()`. See `MenuService`, `MenuDisplayPage.razor`, `QrRedirect.razor`. The owner preview `/menu/preview/{id}` (`MenuPreviewPage.razor`) is `[Authorize]` and uses the query filters (own tenant only); it passes `DisableTracking` so item clicks are not counted in the analytics.

### Authorization
- **Permissions**: `Permission` is a `[Flags] long` enum (`MenusView`, `MenusCreate`, …, `UsersDelete`, `DeliveryView`, …). When adding a value, pick an unused bit; the bits are not in order.
- **Policies**: `AuthorizationOptions.AddPermissionPolicies()` generates one policy per permission, named `Can{Action}{Area}` (e.g. `MenusView` → `CanViewMenus`). `PolicyNames` holds constants for them; they must match `GetPolicyName` (e.g. `CanViewMenuCombos`).
- **Resolution** (`PermissionService`): the owner has all permissions. A sub-account uses `TenantUser.Permissions` if set, otherwise the role defaults from `PermissionDefaults` (GeneralManager, Manager, Cashier, Employee, Driver). Inactive or missing `TenantUser` → no permissions.
- **API**: controllers use `[Authorize(Policy = PolicyNames.Can…)]` per action. `ImageUploadController` and `MenuDisplayController` have no permission policies.
- **Blazor**: pages use `@attribute [Authorize]` and check `IPermissionService.HasPermissionAsync(Permission.…)` to show/hide actions. `/qrcodes` without `QrCodesView` redirects to `/Account/AccessDenied`.
- **PaidTenant** policy (`PaidTenantHandler`): passes for tenants with an active paid `TenantSubscription`; free tenants pass while they have fewer than 100 QR codes. `/qrcodes` disables the "Create New QR Code" button, and `/qrcode/edit` checks it as well (on load for a new code and again on save; editing existing codes stays allowed).
- **User limit**: creating a sub-account fails once the active `TenantUser` count reaches `TenantSubscription.MaxUsers`.

### Key Services
- **IMenuService**: resolves the active menu for a QR code. Precedence: matching calendar entry for today (if `IsCalendarBased`), then the active time-table window (if `IsTimeTableBased`, also across midnight; no active window = null), otherwise the direct menu. Uses the injected `TimeProvider` (`GetLocalNow()`), registered as `TimeProvider.System` in `ServiceExtensions`.
- **IPermissionService**: current role and permissions (cached per instance)
- **ITrackingService** / **IAnalyticsService**: records menu views and item clicks (session cookie `SpeiseDirekt_SessionId`) and aggregates them for the analytics page and API.
- **IImageUploadService**: `ImageDatabaseUploadService` (registered in the Blazor app) stores images in the DB; `ImageUploadService` (file-based, `wwwroot/uploads/`) still exists but is not registered.
- **IImageResizeService**: resizes uploads (ImageSharp)
- **IMenuItemGenerator**: AI-generated menu items via Ollama (Blazor app only)
- **IAutomaticTranslator** / **ITranslationApiService**: Azure Translator with DB cache (`TranslationCache`), incl. allergen names. Implemented but not registered or used yet.
- **INotificationService**: in-app notifications

### SpeiseDirekt3 (Blazor app)
- `Program.cs`: registers Identity (cookies), permission and `PaidTenant` policies, claims transformation, cookie consent (BytexDigital, GDPR), Ollama chat client, image services. `MenuItemGenerator:Enabled` (true only in `appsettings.Development.json`) shows the "Generate Menu Items" button; production has no Ollama.
- **DbContext lifetime**: `ApplicationDbContext` is registered as `Transient` (plus a scoped `IDbContextFactory`) so each component gets its own context and avoids concurrent-access errors in Blazor Server. Components inject `ApplicationDbContext` directly, not the repositories.
- `Components/`, organized by feature:
  - `Layout/`: main layout, menu layout, navigation, `MenuTreeNav` (reloads on navigation). The sidebar shows the sub-account's `TenantUser.DisplayName` or the e-mail plus the role; the phone overlay closes on navigation.
  - `Pages/`: home (a dashboard for signed-in users: stat tiles, quick actions, a getting-started checklist that hides once a menu, items, a QR code and a first scan exist, the menus; the marketing hero only for anonymous visitors), public `MenuDisplayPage`
  - `Menu/`: menu list (`/menus`, cards with category/item/QR counts, actions outside the link, delete via `MenuPage.DeleteMenuAsync` which first removes clicks, views, QR codes, combos and allergens in one transaction because those foreign keys do not cascade; the confirmation names the QR codes that go with the menu), menu detail (`/menudetail/{MenuId}`, with a QR codes tile), edit form (the guest preview sits in a 380 × 640 px phone frame and follows the selected theme live), preview (`/menu/preview/{id}`: `[Authorize]`, own tenant only, `DisableTracking` so item clicks are not counted in the analytics), public display components (`MenuDisplayComponents/`: complex display, search, item overlay, bottom navigation). `ComplexMenuDisplayComp` loads the menu's combos once (separate query, `IgnoreQueryFilters`); the item overlay shows the combos the opened item triggers (also in `Preview`), an allergen legend (code + name) and the dietary badges (`DietaryBadges`: vegan / vegetarisch / scharf; vegan hides vegetarisch). Public pages must load `MenuItem.Allergens` (`ThenInclude`). Loading / not found / "no menu right now" states on the public pages use `Shared/GuestNotice` (German, `data-testid="guest-notice"`, `data-kind`).
  - **Design themes**: `ComplexMenuDisplayComp` renders `<div class="sd-menu" data-theme="{menu.Theme}">`; `wwwroot/css/menu-themes.css` defines a palette and fonts per theme (CSS variables) and recolors the Tailwind utilities used inside the guest view (`.bg-white`, `.text-gray-900`, `.bg-teal-600`, …) plus the hooks `.sd-header`, `.sd-header-shade` and `.sd-page` (`MenuLayout`). `modern` is the default look and has no overrides. The menu editor's preview follows the selected theme live.
  - **Tailwind vs. Bootstrap**: both are loaded and share class names; Bootstrap's `.bg-white`, `.text-white`, `.bg-black` are `!important`, and Tailwind-only modifiers such as `bg-opacity-20` do not exist in Bootstrap (→ solid black). Use the slash syntax (`bg-black/20`, `from-black/10`) and `!important` in overrides of those classes.
  - `Categories/` (`/Categories/{MenuId}`: reorder arrows, inline rename, item counts, link row to items/allergens/combos), `MenuItem/` (`/MenuItems/{MenuId}` redirects to the first category in display order; table with euro prices, dietary and allergen chips, icon actions, reorder arrows, no paging; "Delete All" as a red outline button at the right; empty states link to the categories page; deleting an item first removes its `MenuItemClick` rows, a combo still blocks the delete and shows a message), `Allergens/` (`/allergens/{MenuId}`: "Add the 14 EU allergens" fills the missing Austrian letter codes A–R from `EuAllergens.cs`, German names on German menus; a "Used by" column and a confirmation when deleting an allergen in use), `MenuCombos/` (`/combos/{MenuId}`)
  - `QrCodes/` (`/qrcodes`: requires `QrCodesView`, otherwise redirect to `/Account/AccessDenied`; compact cards with a Direct/Schedule/Calendar badge, what guests get, the guest URL, Download PNG, Copy link via `wwwroot/js/qr-codes.js`; deleting a code first detaches its `MenuView` rows; `/qrcode/edit` refuses a new code on load and again on save when the `PaidTenant` policy fails (redirect to `/qrcodes`; editing existing codes stays allowed) and has three modes: simple, schedule (`QrCodeTimeTableEntries`) and calendar (`QrCodeCalendarEntries`: date, optional weekly repeat, optional until, menu, plus the direct `MenuId` as fallback); `QRCode.Validate` lets a single date sit inside a weekly entry's period and only collides entries of the same kind; `QrRedirect` at `/qr/{QrId}`), `Analytics/` (`/analytics`: summary tiles first, a "Sessions" series computed on the page from `GetTrafficPerUserAsync`)
  - `UserManagement/`: users list, invite, edit role/permissions (`/users`, `/users/invite`, `/users/edit/{UserId}`)
  - `Payments/PaidTenantComponentBase.cs`: base component for premium features
  - `Shared/`, `HelperComp/`: modal, confirmation modal, pagination, time input, `MultiSelectComboBox`, popovers
  - `Account/`: Identity pages (static rendering under `/Account`)
- `Controller/ImageController.cs`: serves `/Image/{id}` from the DB and `/Image/qr/{qrId}` as QR PNG (encodes `/qr/{qrId}`; `?download=1&name=qr-tisch-1.png` sends it as an attachment, the name must be a plain lower-case `.png` name)
- `wwwroot/`: static assets, `app.css`, `css/tailwind-overrides.css`, `css/menu-themes.css`, `css/fonts.css` + `fonts/`, vendored `lib/bootstrap` and `lib/chartjs`, JS helpers (`speise.js`, `js/speisedirekt.js`, `js/qr-codes.js`, `js/analytics.js`)

### SpeiseDirekt.Api (Web API)
- Controllers in `Controllers/`, routes under `/api/...`:
  - `MenusController`, `CategoriesController`, `MenuItemsController`: CRUD via repositories (`MenuItemDto` has optional `IsVegan`/`IsVegetarian`/`IsSpicy`, default false; `CategoryDto` and `MenuItemDto` take an optional `SortOrder`: create without it appends at the end, update keeps the value; lists come back in display order)
  - `AllergensController`: CRUD, scoped to a menu (`GET` requires `?menuId=`; `Code` must be unique per menu)
  - `QrCodesController`: CRUD plus `POST/DELETE {id}/timetable-entries` and `{id}/calendar-entries`
  - `UsersController`: manage sub-accounts (creates Identity user + `TenantUser`, enforces `MaxUsers`)
  - `AnalyticsController`: `traffic-per-user`, `traffic-per-menu`, `traffic-per-menuitem`
  - `ImageUploadController` (`/api/images`): upload (max 5 MB, resized to 600px), delete; `GET /api/images/{id}` is anonymous
  - `MenuCombosController`: CRUD via `IMenuComboRepository`, scoped to a menu (`GET` requires `?menuId=`). Validates: trigger item required, at least one included item, `ComboPrice >= 0`, trigger and included items must belong to the combo's menu (via `Category.MenuId`). On `PUT` the combo keeps its menu (`MenuId` in the DTO is ignored).
  - `MenuDisplayController` (`/api/public`, anonymous): menu for a QR code (records a view), the menu's combos (`GET menu/{menuId}/combos`, returns `PublicMenuComboDto` with trigger item, included items and price) and item click tracking
- Request models in `Dtos/`. Responses currently return entities directly; JSON uses `ReferenceHandler.IgnoreCycles`.
- CORS policy `AllowAll`. `public partial class Program` exists so the tests can use `WebApplicationFactory<Program>`.

### SpeiseDirekt.Api.IntegrationTests
- `CustomWebApplicationFactory`: replaces SQL Server with SQLite in-memory, swaps `IUserIdProvider` for a fixed `TestUserId`, and adds the `TestScheme` auth handler (`TestAuthHandler`), which authenticates every request as that user (the tenant owner, so all permissions apply). It also replaces `TimeProvider` with `TestTimeProvider` (`Factory.Clock`): system clock by default, `SetLocalNow(...)` fixes the time for one test (reset before each test). Requests with the header `TestAuthHandler.AnonymousHeader` are unauthenticated (`BaseIntegrationTest.AnonymousGet`).
- `BaseIntegrationTest`: recreates the DB and seeds it (`TestSeedData`) before each test. Use the fixed IDs in `TestSeedData` in assertions. Combos are not seeded by default; tests opt in via `TestSeedData.SeedCombosAsync(Factory.Services)`.
- `Tests/`: one test class per controller.
- SQLite differs from SQL Server (e.g. FK enforcement, decimal handling). Keep that in mind when a test passes locally but the real DB behaves differently.

### SpeiseDirekt3.E2ETests
- `Infrastructure/E2EAppFactory`: `WebApplicationFactory<SpeiseDirekt3.Program>` that runs the app in the `E2E` environment with its own SQLite **file** (`%TEMP%/speisedirekt-e2e-{guid}.db` via `ConnectionStrings:e2e`; the app creates the schema, the factory then seeds `E2ESeedData`: bistro and cafe menus with a combo, plus the "Ramen" menu transcribed from a real menu card — 8 dishes at 17.40 with allergen codes A–N; including its vegan/vegetarian/spicy icons) and additionally starts the app on Kestrel at a free loopback port (`ServerAddress`), because Playwright needs a real socket. The file is deleted when the factory is disposed (connection pooling is off so it can be deleted). It replaces `TimeProvider` in both hosts with `E2ETestClock` (`App.Clock.SetLocalNow(...)`, reset before every test) and offers `CreateDbContext(ownerId)` / `SeedAsync(ownerId, ...)` to seed data directly.
- `Infrastructure/E2EFixture`: class fixture with one factory (= one server + one DB file) and one headless Chromium **per test class**. xUnit runs test classes in parallel, each with its own DB; tests within a class run sequentially and share the DB, so every test uses its own tenant (`UniqueEmail()`) or its own seeded ids.
- `Infrastructure/E2ETestBase`: new browser context per test (`NewContextAsync()` for a second one, e.g. an anonymous guest).
  - External requests (in practice only the Tailwind CDN) are blocked unless `E2E_ALLOW_EXTERNAL_REQUESTS=1` (e.g. for layout screenshots). While blocked, `E2ESettings` injects a minimal stylesheet for the Tailwind display utilities the app uses (`hidden`, `lg:flex`, `xl:block`, …, plus an SVG size cap) so hidden modals stay hidden (`whitespace-nowrap` too). Bootstrap JS is served locally, so popovers and modals use the real Bootstrap.
  - `GotoInteractiveAsync` / `WaitForInteractiveAsync`: wait until the Blazor circuit is interactive (the server's `enableNavigationInterception` call on the `/_blazor` WebSocket); clicks during prerendering are lost. `OpenMenuAsync` / `OpenPublicMenuPageAsync` wait for `js/complex-menu*.js` on the public menu display.
  - Accounts: `RegisterAsync`, `LoginAsync`, `LogoutAsync`, `RegisterNewOwnerAsync` via the Identity UI (static SSR forms). The cookie consent prompt is disabled in `Routes.razor`; `DismissCookieConsentAsync` accepts it should it come back.
- `Infrastructure/BackOfficeTestBase`: UI helpers for the back office (`CreateMenuViaUiAsync`, `CreateCategoryViaUiAsync`, `CreateAllergenViaUiAsync`, `CreateMenuItemViaUiAsync`, `CreateDirectQrViaUiAsync`).
- `Tests/`: `MenuDisplayTests`, `MenuComboDisplayTests` (public menu page), `QrRedirectTests` (`/qr/{id}`: direct, unknown, time table with fixed clock incl. across midnight, calendar precedence and fallbacks), `AuthTests`, `BackOfficeFlowTests` (menu → category → item → QR via UI, guest opens the QR), `PermissionTests` (invited Employee is read-only, `/qrcodes` denied), `AllergenTests`, `ComboEditorTests` (incl. menu preview), `QrCodeLimitTests` (free tier: 99 OK, 100 blocked, also via the direct edit URL; editing at the limit stays allowed), `RamenMenuTests` (ramen menu card: dishes, prices, allergen codes, overlay), `GuestUiTests` (dietary badges, allergen legend, editor preview with allergens, price on one line at 360px, local Bootstrap JS, notice pages), `ShellDashboardMenuListTests` (dashboard, checklist, menu cards, delete confirmations, sidebar), `CategoryAndItemUxTests` (reordering, euro prices, labels, empty states, optional description, deletes with guest clicks), `QrCodeAdminUxTests` (calendar editor, list cards, download, copy link, deletes with recorded scans), `AllergensSettingsAnalyticsTests` (EU quick-fill, usage counts, phone-frame preview, analytics tiles, role tooltips), `MenuPreviewTests` (login required, own tenant only, no click tracking), `MenuThemeTests` (colors/fonts per theme, dark cards despite Bootstrap, header shade, theme chosen in the editor), `LocalAssetsTests` (no external requests except Tailwind on guest page, back office and analytics; self-hosted fonts; local Chart.js with time scale).
- `AdminScreenshotTour` is not a test but a tool: with `E2E_SCREENSHOT_DIR=<dir>` (and `E2E_ALLOW_EXTERNAL_REQUESTS=1` so Tailwind comes from the CDN) it seeds a realistic tenant and stores full-page screenshots of every back-office page at 1280 px and 390 px for design reviews. Without the variable it does nothing.
- Known host artefact: `QrRedirectTests.MalformedQrId_IsNotFound_NoServerError` fails with `net::ERR_HTTP_RESPONSE_CODE_FAILURE` when Playwright drives a Chromium newer than its own build (the browser refuses to navigate to an empty 404 page); the app answers 404 correctly.
- Bootstrap's CSS loads next to Tailwind and its spacing utilities `p-0`…`p-5`, `m-*`, `gap-0`…`gap-5`, `pl-0` and `text-center` are `!important`, so they override Tailwind's (`p-4` renders as 1.5rem). Use non-colliding classes (`p-6`, `px-3.5`, `gap-x-*`, arbitrary values) in new markup.
- The Ollama menu item generator is not tested.
