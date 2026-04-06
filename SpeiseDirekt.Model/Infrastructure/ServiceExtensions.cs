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

            // Transient: services that depend on ApplicationDbContext
            // (each component gets a fresh DbContext to avoid concurrent access issues in Blazor Server)
            services.AddTransient<IMenuService, MenuService>();
            services.AddTransient<ITrackingService, TrackingService>();
            services.AddTransient<IAnalyticsService, AnalyticsService>();
            services.AddTransient<IPermissionService, PermissionService>();

            // Repositories (all depend on ApplicationDbContext)
            services.AddTransient<IMenuRepository, MenuRepository>();
            services.AddTransient<ICategoryRepository, CategoryRepository>();
            services.AddTransient<IMenuItemRepository, MenuItemRepository>();
            services.AddTransient<IQrCodeRepository, QrCodeRepository>();
            services.AddTransient<IImageRepository, ImageRepository>();
            services.AddTransient<IMenuComboRepository, MenuComboRepository>();

            // POS repositories (restaurant transactions, not app subscription billing)
            services.AddTransient<IOrderRepository, OrderRepository>();
            services.AddTransient<ITaxRateRepository, TaxRateRepository>();
            services.AddTransient<IDiscountRepository, DiscountRepository>();
            services.AddTransient<IPosPaymentRepository, PosPaymentRepository>();
        }
    }
}
