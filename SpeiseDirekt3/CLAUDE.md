# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Development Commands

### Building and Running
- **Run the application**: `dotnet run` (runs on https://localhost:7051 and http://localhost:5010)
- **Build the project**: `dotnet build`
- **Restore packages**: `dotnet restore`
- **Watch mode**: `dotnet watch run` (for live reloading during development)

### Database Operations
- **Create migration**: `dotnet ef migrations add <MigrationName>`
- **Update database**: `dotnet ef database update`
- **Remove last migration**: `dotnet ef migrations remove`

### Testing
- **Run tests**: `dotnet test` (if test projects exist)

## Project Architecture

### Technology Stack
- **Framework**: ASP.NET Core 9.0 with Blazor Server
- **Database**: SQL Server with Entity Framework Core 9.0.3
- **Authentication**: ASP.NET Core Identity
- **UI Framework**: Tailwind CSS with Bootstrap Icons
- **AI Integration**: Microsoft.Extensions.AI with Ollama (llama3.2:latest model)
- **QR Code Generation**: QRCoder library

### Core Domain Models
Located in `Model/Model.cs`:
- **MenuItem**: Restaurant menu items with categories, pricing, allergens, and images
- **Category**: Groups menu items within menus
- **Menu**: Top-level container with design themes (Elegant, Modern, Classic, Minimal, Fancy, Dark)
- **QRCode**: Can be simple (direct menu link) or time-table based (different menus at different times)
- **TimeTableEntry**: Links QR codes to specific menus during time windows
- **TenantSubscription**: Handles paid vs free tier functionality

### Multi-Tenant Architecture
- All entities implement `IAppUserEntity` interface with `ApplicationUserId` property
- Global query filters automatically filter data by current user in `ApplicationDbContext:28-32`
- User context provided by `IUserIdProvider` service
- Authorization policy "PaidTenant" restricts premium features

### Key Services
- **IMenuService**: Handles time-based menu resolution for QR codes, including cross-midnight time handling
- **IImageUploadService**: Manages menu item image uploads to `wwwroot/uploads/menu-items/`
- **IMenuItemGenerator** (AI-powered): Generates menu items using Ollama AI
- **INotificationService**: Application notifications
- **IUserIdProvider**: Current user context

### Data Access Patterns
- DbContext is registered as `Transient` with query splitting enabled
- Automatic `ApplicationUserId` assignment on entity add/modify in `ApplicationDbContext:48-73`
- Global query filters ensure tenant isolation
- Uses `IgnoreQueryFilters()` for cross-tenant operations (like QR code access)

### UI Architecture
- **Blazor Server** with Interactive Server render mode
- **Component Structure**:
  - `Components/Layout/`: Main layout, navigation
  - `Components/Pages/`: Main application pages
  - `Components/Menu/`: Menu management components
  - `Components/MenuItem/`: Menu item CRUD
  - `Components/Categories/`: Category management
  - `Components/QrCodes/`: QR code generation and management
  - `Components/Account/`: Identity pages

### Design System
- **Tailwind CSS** with custom configuration
- Custom color palette: primary (blue tones), secondary (green tones)
- Typography: Inter and Manrope fonts
- Multiple menu display themes supported
- Modern card-based layouts replacing traditional tables
- Mobile-first responsive design

### Authentication & Authorization
- ASP.NET Core Identity with custom `ApplicationUser`
- Cookie-based authentication
- "PaidTenant" authorization policy for premium features
- Account pages under `/Account` path use static rendering

### External Integrations
- **Ollama AI**: Local AI model at `http://localhost:11434` for menu item generation
- **Cookie Consent**: GDPR compliance with BytexDigital component
- **Bootstrap Icons**: Icon library

### File Organization
- `Data/`: DbContext and migrations
- `Infrastructure/`: Service registrations and cross-cutting concerns
- `ServiceInterface/` & `ServiceImplementation/`: Clean architecture service pattern
- `Components/`: All Blazor components organized by feature
- `Model/`: Domain models and DTOs
- `wwwroot/`: Static assets, including uploaded images and CSS themes

### Database Considerations
- Uses SQL Server LocalDB for development
- Entity relationships properly configured with foreign keys
- Automatic tenant isolation through global query filters
- Migration history shows evolution from initial setup to current multi-tenant structure