using SpeiseDirekt3.ServiceInterface;
using SpeiseDirekt3.ServiceImplementation;
using BytexDigital.Blazor.Components.CookieConsent;

namespace SpeiseDirekt3.Infrastructure
{
    public static class ServiceExtensions
    {
        public static void ConfigureServices(this IServiceCollection services)
        {
            services.AddScoped<IUserIdProvider, UserIdProvider>();
            services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
            services.AddScoped<INotificationService, NotificationService>();
            services.AddScoped<IMenuService, MenuService>();

            services.AddCookieConsent(o =>
            {
                o.Revision = 1;
                o.PolicyUrl = "/cookie-policy";

                // Call optional
                o.UseDefaultConsentPrompt(prompt =>
                {
                    prompt.Position = ConsentModalPosition.BottomRight;
                    prompt.Layout = ConsentModalLayout.Cloud;
                    prompt.SecondaryActionOpensSettings = false;
                    prompt.AcceptAllButtonDisplaysFirst = false;
                });

                o.Categories.Add(new CookieCategory
                {
                    TitleText = new()
                    {
                        ["en"] = "Google Services",
                        ["de"] = "Google Dienste"
                    },
                    DescriptionText = new()
                    {
                        ["en"] = "Allows the integration and usage of Google services.",
                        ["de"] = "Erlaubt die Verwendung von Google Diensten."
                    },
                    Identifier = "google",
                    IsPreselected = true,

                    Services = new()
        {
            new CookieCategoryService
            {
                Identifier = "google-maps",
                PolicyUrl = "https://policies.google.com/privacy",
                TitleText = new()
                {
                    ["en"] = "Google Maps",
                    ["de"] = "Google Maps"
                },
                ShowPolicyText = new()
                {
                    ["en"] = "Display policies",
                    ["de"] = "Richtlinien anzeigen"
                }
            },
            new CookieCategoryService
            {
                Identifier = "google-analytics",
                PolicyUrl = "https://policies.google.com/privacy",
                TitleText = new()
                {
                    ["en"] = "Google Analytics",
                    ["de"] = "Google Analytics"
                },
                ShowPolicyText = new()
                {
                    ["en"] = "Display policies",
                    ["de"] = "Richtlinien anzeigen"
                }
            }
        }
                });
            });
        }
    }
}
