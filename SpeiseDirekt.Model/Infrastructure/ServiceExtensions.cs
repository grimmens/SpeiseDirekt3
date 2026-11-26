using Microsoft.Extensions.DependencyInjection.Extensions;
using SpeiseDirekt.Repository;
using SpeiseDirekt.ServiceInterface;
using SpeiseDirekt.ServiceImplementation;

namespace SpeiseDirekt.Infrastructure
{
    public static class ServiceExtensions
    {
        public static void ConfigureServices(this IServiceCollection services)
        {
            services.AddScoped<IUserIdProvider, UserIdProvider>();
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<IImageResizeService, ImageResizeService>();
            services.AddHttpClient();

            // System clock (TryAdd, so tests can register a fake TimeProvider)
            services.TryAddSingleton(TimeProvider.System);

            // The guests' daily visitor keys: one random salt in memory, replaced at local midnight (AP15a, no cookie)
            services.AddSingleton<IVisitorKeyService, VisitorKeyService>();

            // Transient: services that depend on ApplicationDbContext
            // (each component gets a fresh DbContext to avoid concurrent access issues in Blazor Server)
            services.AddTransient<IMenuService, MenuService>();
            services.AddTransient<ITrackingService, TrackingService>();
            services.AddTransient<IAnalyticsService, AnalyticsService>();
            services.AddTransient<IPermissionService, PermissionService>();
            services.AddTransient<IRestaurantProfileService, RestaurantProfileService>();
            services.AddTransient<IMenuTranslationService, MenuTranslationService>();

            // Plans (free / paid): the only check of the free limits, Plans:Free:MaxMenus / MaxQrCodes (defaults 1 / 1)
            services.AddOptions<FreePlanOptions>().BindConfiguration(FreePlanOptions.SectionName);
            services.AddTransient<ITenantPlanService, TenantPlanService>();

            // Identity roles (both hosts call AddRoles<IdentityRole>, so RoleManager and the role-aware UserManager exist):
            // the startup sync of the web app, resolvable in tests
            services.AddTransient<RoleSetup>();

            // Repositories (all depend on ApplicationDbContext)
            services.AddTransient<IMenuRepository, MenuRepository>();
            services.AddTransient<ICategoryRepository, CategoryRepository>();
            services.AddTransient<IMenuItemRepository, MenuItemRepository>();
            services.AddTransient<IQrCodeRepository, QrCodeRepository>();
            services.AddTransient<IImageRepository, ImageRepository>();
            services.AddTransient<IMenuComboRepository, MenuComboRepository>();
        }
    }
}
