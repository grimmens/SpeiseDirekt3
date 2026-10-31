# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Solution Overview

`SpeiseDirekt3.sln` contains four .NET 9 projects:

| Project | Type | Purpose |
|---|---|---|
| `SpeiseDirekt3/` | Blazor Server web app | Restaurant back office (menus, categories, items, allergens, combos, QR codes, analytics, user management) and public menu display |
| `SpeiseDirekt.Api/` | ASP.NET Core Web API | REST API (Swagger, Identity API endpoints) over the same data |
| `SpeiseDirekt.Model/` | Class library (root namespace `SpeiseDirekt`) | Shared core: entities, DbContext, migrations, repositories, services, authorization, DI setup |
| `SpeiseDirekt.Api.IntegrationTests/` | xUnit test project | Integration tests for every API controller |

Both `SpeiseDirekt3` and `SpeiseDirekt.Api` reference `SpeiseDirekt.Model` and share one SQL Server database (connection string `server`).

## Development Commands

Run from the repository root.

### Building and Running
- **Build everything**: `dotnet build`
- **Run Blazor app**: `dotnet run --project SpeiseDirekt3` (https://localhost:7051, http://localhost:5010)
- **Run Web API**: `dotnet run --project SpeiseDirekt.Api` (https://localhost:7254, http://localhost:5289; Swagger UI at `/swagger` in Development)
- **Watch mode**: `dotnet watch run --project SpeiseDirekt3`
- **Manual API requests**: `SpeiseDirekt.Api/SpeiseDirekt.Api.http`

### Testing
- **Run all tests**: `dotnet test`
- **Run a single test class**: `dotnet test --filter "FullyQualifiedName~MenusControllerTests"`
- **Run a single test**: `dotnet test --filter "FullyQualifiedName~MenusControllerTests.GetAll_ReturnsSeededMenus"`

### Database Operations
Migrations live in `SpeiseDirekt.Model/Data/Migrations`, so the EF commands need both project flags:
- **Create migration**: `dotnet ef migrations add <MigrationName> --project SpeiseDirekt.Model --startup-project SpeiseDirekt3`
- **Update database**: `dotnet ef database update --project SpeiseDirekt.Model --startup-project SpeiseDirekt3`
- **Remove last migration**: `dotnet ef migrations remove --project SpeiseDirekt.Model --startup-project SpeiseDirekt3`

### Deployment
- `SpeiseDirekt3/SendToProd.ps1` publishes the Blazor app (Release) and copies it via `ssh`/`scp` to a systemd service on the production server. Run it from inside `SpeiseDirekt3/`. There is no CI pipeline.

## Architecture

### Technology Stack
- **Framework**: ASP.NET Core 9.0 (Blazor Server with Interactive Server render mode; Web API with controllers)
- **Database**: SQL Server via Entity Framework Core 9 (split queries enabled)
- **Authentication**: ASP.NET Core Identity. Cookies in the Blazor app; `AddIdentityApiEndpoints` / `MapIdentityApi` in the API
- **UI**: Tailwind CSS via CDN (config inline in `SpeiseDirekt3/Components/App.razor`), plus Bootstrap, Bootstrap Icons, Chart.js
- **AI**: Microsoft.Extensions.AI with Ollama (`llama3.2:latest` at `http://localhost:11434`, hard-coded in `SpeiseDirekt3/Program.cs`)
- **Images**: SixLabors.ImageSharp for resizing; images are stored as bytes in the database (`Image` entity)
- **QR codes**: QRCoder (PNG generated in `SpeiseDirekt3/Controller/ImageController.cs`)
- **Tests**: xUnit, FluentAssertions, `WebApplicationFactory`, SQLite in-memory

### SpeiseDirekt.Model (shared core)
- `Model/Model/`: one file per entity
  - `Menu` (with `DesignTheme`: Elegant, Modern, Classic, Minimal, Fancy, Dark; and `MenuLanguage`) → `Category` → `MenuItem`
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
- `Infrastructure/`: authorization (see below), `TenantClaimsTransformation`, `EditFormExtensions`
- `Repository/`: repositories for Menu, Category, MenuItem, QrCode, Image and MenuCombo (used by the API)
- `ServiceInterface/` and `ServiceImplementation/`: services (see below)

### Multi-Tenant Architecture
- A **tenant** is the owner account. Sub-accounts (`ApplicationUser.TenantOwnerId` set, plus a `TenantUser` row with role and permissions) work on the owner's data.
- Tenant-owned entities implement `IAppUserEntity` (`ApplicationUserId` = tenant owner ID).
- `ApplicationDbContext.OnModelCreating` applies global query filters on `ApplicationUserId` for MenuItem, Category, Menu, QRCode, TimeTableEntry, CalendarEntry, Allergen, MenuCombo and MenuComboItem.
- `ChangeTracker` events set `ApplicationUserId` on Added/Modified entities from `IUserIdProvider`. Saving an `IAppUserEntity` **throws** if no authenticated user is present.
- `TenantClaimsTransformation` adds `TenantOwnerId` and `TenantRole` claims for sub-accounts.
- `IUserIdProvider.GetUserId()` returns the **tenant owner ID** (the `TenantOwnerId` claim for sub-accounts, otherwise `NameIdentifier`) and is what the query filters use. `GetActualUserId()` returns the logged-in user. Both return `Guid.Empty` when unauthenticated.
- Public/anonymous paths (QR code resolution, public menu display) use `IgnoreQueryFilters()`. See `MenuService`, `MenuDisplayPage.razor`, `MenuPreviewPage.razor`, `QrRedirect.razor`.

### Authorization
- **Permissions**: `Permission` is a `[Flags] long` enum (`MenusView`, `MenusCreate`, …, `UsersDelete`, `DeliveryView`, …). When adding a value, pick an unused bit; the bits are not in order.
- **Policies**: `AuthorizationOptions.AddPermissionPolicies()` generates one policy per permission, named `Can{Action}{Area}` (e.g. `MenusView` → `CanViewMenus`). `PolicyNames` holds constants for them; they must match `GetPolicyName` (e.g. `CanViewMenuCombos`).
- **Resolution** (`PermissionService`): the owner has all permissions. A sub-account uses `TenantUser.Permissions` if set, otherwise the role defaults from `PermissionDefaults` (GeneralManager, Manager, Cashier, Employee, Driver). Inactive or missing `TenantUser` → no permissions.
- **API**: controllers use `[Authorize(Policy = PolicyNames.Can…)]` per action. `ImageUploadController` and `MenuDisplayController` have no permission policies.
- **Blazor**: pages use `@attribute [Authorize]` and check `IPermissionService.HasPermissionAsync(Permission.…)` to show/hide actions.
- **PaidTenant** policy (`PaidTenantHandler`): passes for tenants with an active paid `TenantSubscription`; free tenants pass while they have fewer than 100 QR codes.
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
- `Program.cs`: registers Identity (cookies), permission and `PaidTenant` policies, claims transformation, cookie consent (BytexDigital, GDPR), Ollama chat client, image services
- **DbContext lifetime**: `ApplicationDbContext` is registered as `Transient` (plus a scoped `IDbContextFactory`) so each component gets its own context and avoids concurrent-access errors in Blazor Server. Components inject `ApplicationDbContext` directly, not the repositories.
- `Components/`, organized by feature:
  - `Layout/`: main layout, menu layout, navigation, `MenuTreeNav`
  - `Pages/`: home, auth, public `MenuDisplayPage`
  - `Menu/`: menu list (`/menus`), menu detail (`/menudetail/{MenuId}`), edit form, preview, public display components (`MenuDisplayComponents/`: simple/complex display, search, item overlay). `ComplexMenuDisplayComp` loads the menu's combos once (separate query, `IgnoreQueryFilters`); the item overlay shows the combos the opened item triggers (also in `Preview`).
  - `Categories/` (`/Categories/{MenuId}`), `MenuItem/` (`/MenuItems/{MenuId}`), `Allergens/` (`/allergens/{MenuId}`), `MenuCombos/` (`/combos/{MenuId}`)
  - `QrCodes/` (`/qrcodes`, edit, time table entries, `QrRedirect` at `/qr/{QrId}`), `Analytics/` (`/analytics`)
  - `UserManagement/`: users list, invite, edit role/permissions (`/users`, `/users/invite`, `/users/edit/{UserId}`)
  - `Payments/PaidTenantComponentBase.cs`: base component for premium features
  - `Shared/`, `HelperComp/`: modal, confirmation modal, pagination, time input, `MultiSelectComboBox`, popovers
  - `Account/`: Identity pages (static rendering under `/Account`)
- `Controller/ImageController.cs`: serves `/Image/{id}` from the DB and `/Image/qr/{qrId}` as QR PNG (encodes `/qr/{qrId}`)
- `wwwroot/`: static assets, `app.css`, `css/tailwind-overrides.css`, JS helpers (`speise.js`, `js/speisedirekt.js`)

### SpeiseDirekt.Api (Web API)
- Controllers in `Controllers/`, routes under `/api/...`:
  - `MenusController`, `CategoriesController`, `MenuItemsController`: CRUD via repositories
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
