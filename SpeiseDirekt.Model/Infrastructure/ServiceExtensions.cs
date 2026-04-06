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
            services.AddScoped<IMenuService, MenuService>();
            services.AddScoped<ITrackingService, TrackingService>();
            services.AddScoped<IAnalyticsService, AnalyticsService>();
            services.AddScoped<IImageResizeService, ImageResizeService>();
            services.AddHttpClient();

            // Repositories
            services.AddScoped<IMenuRepository, MenuRepository>();
            services.AddScoped<ICategoryRepository, CategoryRepository>();
            services.AddScoped<IMenuItemRepository, MenuItemRepository>();
            services.AddScoped<IQrCodeRepository, QrCodeRepository>();
            services.AddScoped<IImageRepository, ImageRepository>();
        }
    }
}
