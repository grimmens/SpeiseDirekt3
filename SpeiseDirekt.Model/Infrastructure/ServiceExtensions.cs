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

            // POS session (server-side cart in IMemoryCache)
            services.AddMemoryCache();
            services.AddSingleton<IPosSessionService, PosSessionService>();
            services.AddSingleton<IPosCustomerService, PosCustomerService>();

            // POS services (restaurant transactions, not app subscription billing)
            services.AddTransient<IOrderService, OrderService>();
            services.AddTransient<ITaxService, TaxService>();
            services.AddTransient<IDiscountService, DiscountService>();
            services.AddTransient<IPosPaymentService, PosPaymentService>();
            services.AddTransient<IPosStripeGateway, PosStripeGateway>();

            // POS location (geocoding via OpenStreetMap Nominatim)
            services.AddHttpClient<IPosLocationService, PosLocationService>();

            // POS repositories (restaurant transactions, not app subscription billing)
            services.AddTransient<IOrderRepository, OrderRepository>();
            services.AddTransient<ITaxRateRepository, TaxRateRepository>();
            services.AddTransient<IDiscountRepository, DiscountRepository>();
            services.AddTransient<IPosPaymentRepository, PosPaymentRepository>();
        }
    }
}
