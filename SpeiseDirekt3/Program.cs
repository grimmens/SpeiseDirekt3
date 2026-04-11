using BytexDigital.Blazor.Components.CookieConsent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using SpeiseDirekt3.Components;
using SpeiseDirekt3.Components.Account;
using SpeiseDirekt.Data;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceImplementation;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt3
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            builder.Services.AddLocalization(o => o.ResourcesPath = "Resources");

            builder.Services.AddCascadingAuthenticationState();
            builder.Services.AddScoped<IdentityUserAccessor>();
            builder.Services.AddScoped<IdentityRedirectManager>();
            builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
            builder.Services.AddScoped<IImageResizeService, ImageResizeService>();
            builder.Services.AddHttpContextAccessor();
            builder.Services.ConfigureServices();

            // POS Stripe configuration (restaurant payments, not app subscription billing)
            builder.Services.Configure<SpeiseDirekt.Model.PosStripeSettings>(
                builder.Configuration.GetSection("PosStripe"));

            builder.Services.AddCookieConsent(o =>
            {
                o.Revision = 1;
                o.PolicyUrl = "/cookie-policy";
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

            builder.Services.AddAuthentication(options =>
                {
                    options.DefaultScheme = IdentityConstants.ApplicationScheme;
                    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
                })
                .AddIdentityCookies();
            builder.Services.AddAuthorization(options =>
            {
                options.AddPolicy(PolicyNames.PaidTenant, policy =>
                    policy.Requirements.Add(new PaidTenantRequirement()));
                options.AddPermissionPolicies();
            });

            var connectionString = builder.Configuration.GetConnectionString("server") ?? throw new InvalidOperationException("Connection string 'server' not found.");
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlServer(connectionString, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
            }, contextLifetime: ServiceLifetime.Transient);
            builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            {
                options.UseSqlServer(connectionString, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
            }, lifetime: ServiceLifetime.Scoped);

            

            builder.Services.AddDatabaseDeveloperPageExceptionFilter();

            builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
                .AddRoles<IdentityRole>()
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddSignInManager()
                .AddDefaultTokenProviders();

            builder.Services.AddControllers();

            //Add required Services

            builder.Services.AddSingleton<IChatClient>(sp =>
            {
                return new OllamaChatClient(new Uri("http://localhost:11434"), "llama3.2:latest")
                    .AsBuilder()
                    .UseFunctionInvocation()
                    .Build();
            });
            builder.Services.AddTransient<IMenuItemGenerator, AiMenuItemGenerator>();
            builder.Services.AddTransient<IImageUploadService, ImageDatabaseUploadService>();
            builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
            builder.Services.AddTransient<IAuthorizationHandler, PaidTenantHandler>();
            builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
            builder.Services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation, TenantClaimsTransformation>();


            var app = builder.Build();

            SeedRolesAsync(app.Services).GetAwaiter().GetResult();

            // Configure the HTTP request pipeline.
            if (app.Environment.IsDevelopment())
            {
                app.UseMigrationsEndPoint();
            }
            else
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseHttpsRedirection();

            var supportedCultures = new[] { "de", "en" };
            app.UseRequestLocalization(new Microsoft.AspNetCore.Builder.RequestLocalizationOptions()
                .SetDefaultCulture("de")
                .AddSupportedCultures(supportedCultures)
                .AddSupportedUICultures(supportedCultures));

            app.UseAntiforgery();

            app.MapControllers();
            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();
            // Add additional endpoints required by the Identity /Account Razor components.
            app.MapAdditionalIdentityEndpoints();

            app.Run();
        }

        private static async Task SeedRolesAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            foreach (var role in Enum.GetValues<TenantRole>())
            {
                var name = role.ToString();
                if (!await roleManager.RoleExistsAsync(name))
                    await roleManager.CreateAsync(new IdentityRole(name));
            }
        }
    }
}
