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

            builder.Services.AddCascadingAuthenticationState();
            builder.Services.AddScoped<IdentityUserAccessor>();
            builder.Services.AddScoped<IdentityRedirectManager>();
            builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
            builder.Services.AddScoped<IImageResizeService, ImageResizeService>();
            builder.Services.AddHttpContextAccessor();
            builder.Services.ConfigureServices();

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
                options.AddPolicy("PaidTenant", policy =>
                    policy.Requirements.Add(new PaidTenantRequirement()));
                options.AddPermissionPolicies();
            });

            var connectionString = builder.Configuration.GetConnectionString("server") ?? throw new InvalidOperationException("Connection string 'server' not found.");
            builder.Services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlServer(connectionString, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
            });
            builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
            {
                options.UseSqlServer(connectionString, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
            }, lifetime: ServiceLifetime.Scoped);

            

            builder.Services.AddDatabaseDeveloperPageExceptionFilter();

            builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
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
            builder.Services.AddScoped<IImageUploadService, ImageDatabaseUploadService>();
            builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
            builder.Services.AddScoped<IAuthorizationHandler, PaidTenantHandler>();
            builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
            builder.Services.AddScoped<Microsoft.AspNetCore.Authentication.IClaimsTransformation, TenantClaimsTransformation>();


            var app = builder.Build();

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

            app.UseAntiforgery();

            app.MapControllers();
            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();
            // Add additional endpoints required by the Identity /Account Razor components.
            app.MapAdditionalIdentityEndpoints();

            app.Run();
        }
    }
}
