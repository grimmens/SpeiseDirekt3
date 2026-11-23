using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using OpenAI;
using SpeiseDirekt3.Components;
using SpeiseDirekt3.Components.Account;
using SpeiseDirekt3.Components.Guest;
using SpeiseDirekt3.Components.Landing;
using SpeiseDirekt3.Localization;
using SpeiseDirekt.Data;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.ServiceImplementation;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt3
{
    public class Program
    {
        /// <summary>
        /// Environment for end-to-end tests (<c>ASPNETCORE_ENVIRONMENT=E2E</c>): SQLite instead of SQL Server
        /// (connection string <c>e2e</c>, default <c>speisedirekt-e2e.db</c>) and the schema is created with
        /// <c>EnsureCreated()</c> at startup instead of migrations.
        /// </summary>
        public const string E2EEnvironment = "E2E";

        /// <summary>
        /// Environment of the PR preview on server2 (<c>ASPNETCORE_ENVIRONMENT=Preview</c>): production code paths plus
        /// <c>appsettings.Preview.json</c>; <see cref="Preview.PreviewSeedHostedService"/> adds demo data at startup.
        /// </summary>
        public const string PreviewEnvironment = "Preview";

        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            // English and German from Resources/*.resx, chosen only by the browser's Accept-Language (AP12; guest pages
            // excluded, see Localization/LocalizationSetup.cs)
            builder.Services.AddAppLocalization();

            // Behind Apache (server2: app.speisedirekt.de / the PR preview → http://localhost:<port>): the scheme, host and
            // client address the browser used come from X-Forwarded-Proto/-Host/-For, trusted only from the loopback
            // proxy. Without it the login redirect of a protected page pointed to http:// (one extra hop through Apache).
            builder.Services.Configure<ForwardedHeadersOptions>(options =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost;
                options.KnownNetworks.Clear();
                options.KnownProxies.Clear();
                options.KnownNetworks.Add(new Microsoft.AspNetCore.HttpOverrides.IPNetwork(System.Net.IPAddress.Loopback, 8));
                options.KnownProxies.Add(System.Net.IPAddress.IPv6Loopback);
            });

            builder.Services.AddCascadingAuthenticationState();
            builder.Services.AddScoped<IdentityUserAccessor>();
            builder.Services.AddScoped<IdentityRedirectManager>();
            builder.Services.AddScoped<AuthenticationStateProvider, IdentityRevalidatingAuthenticationStateProvider>();
            builder.Services.AddScoped<IImageResizeService, ImageResizeService>();
            // <html lang> of the guest pages (App.razor, set by GuestMenuView)
            builder.Services.AddScoped<SpeiseDirekt3.Components.Guest.GuestDocument>();
            builder.Services.AddHttpContextAccessor();
            builder.Services.ConfigureServices();

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

            var isE2E = builder.Environment.IsEnvironment(E2EEnvironment);
            if (isE2E)
            {
                var e2eConnectionString = builder.Configuration.GetConnectionString("e2e") ?? "Data Source=speisedirekt-e2e.db";
                builder.Services.AddDbContext<ApplicationDbContext>(options =>
                {
                    options.UseSqlite(e2eConnectionString, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
                }, contextLifetime: ServiceLifetime.Transient);
                builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
                {
                    options.UseSqlite(e2eConnectionString, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
                }, lifetime: ServiceLifetime.Scoped);
            }
            else
            {
                var connectionString = builder.Configuration.GetConnectionString("server") ?? throw new InvalidOperationException("Connection string 'server' not found.");
                builder.Services.AddDbContext<ApplicationDbContext>(options =>
                {
                    options.UseSqlServer(connectionString, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
                }, contextLifetime: ServiceLifetime.Transient);
                builder.Services.AddDbContextFactory<ApplicationDbContext>(options =>
                {
                    options.UseSqlServer(connectionString, o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
                }, lifetime: ServiceLifetime.Scoped);
            }

            

            builder.Services.AddDatabaseDeveloperPageExceptionFilter();

            builder.Services.AddIdentityCore<ApplicationUser>(options => options.SignIn.RequireConfirmedAccount = false)
                .AddEntityFrameworkStores<ApplicationDbContext>()
                .AddSignInManager()
                .AddDefaultTokenProviders()
                // Identity's error messages in the request's language (Resources/IdentityText.resx)
                .AddErrorDescriber<LocalizedIdentityErrorDescriber>();

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
            builder.Services.Configure<Components.MenuItem.MenuItemGeneratorOptions>(
                builder.Configuration.GetSection(Components.MenuItem.MenuItemGeneratorOptions.SectionName));
            builder.Services.AddTransient<IImageUploadService, ImageDatabaseUploadService>();
            builder.Services.AddSingleton<IEmailSender<ApplicationUser>, IdentityNoOpEmailSender>();
            builder.Services.AddTransient<IAuthorizationHandler, PaidTenantHandler>();
            builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
            builder.Services.AddTransient<Microsoft.AspNetCore.Authentication.IClaimsTransformation, TenantClaimsTransformation>();


            if (isE2E)
            {
                // Runs (and is awaited) during host start, before requests are served
                builder.Services.AddHostedService<E2EDatabaseInitializer>();
                // Static web assets (_content/*, scoped CSS) are only enabled automatically in Development
                builder.WebHost.UseStaticWebAssets();
            }

            if (builder.Environment.IsEnvironment(PreviewEnvironment))
            {
                // Demo data for the tenant named in appsettings.Preview.json, see Preview/PreviewSeeder.cs
                builder.Services.Configure<Preview.PreviewSeedOptions>(
                    builder.Configuration.GetSection(Preview.PreviewSeedOptions.SectionName));
                builder.Services.AddHostedService<Preview.PreviewSeedHostedService>();
            }

            var app = builder.Build();

            // First: everything after it (HTTPS redirection, HSTS, cookies' Secure flag, redirects, absolute URLs in meta
            // tags via GuestFormat.PublicOrigin, the tracking's client address) sees the browser's scheme, host and address
            app.UseForwardedHeaders();

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

            // Explicit, so they run after UseForwardedHeaders: WebApplication would otherwise add them implicitly at the
            // very start of the pipeline, and the login challenge of a protected page would build its redirect from
            // Kestrel's http://localhost instead of the browser's https origin
            app.UseAuthentication();
            app.UseAuthorization();

            // Culture of the request from Accept-Language (back office, account pages, landing page); the guest pages are
            // pinned to the invariant culture. Needs the endpoint, which the implicit UseRouting has already chosen.
            app.UseAppRequestLocalization();

            app.UseAntiforgery();

            app.MapControllers();
            // Item click tracking of the static guest pages (js/guest-menu.js)
            app.MapGuestEndpoints();
            // robots.txt and sitemap.xml (Components/Landing/SeoEndpoints.cs)
            app.MapSeoEndpoints();
            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();
            // Add additional endpoints required by the Identity /Account Razor components.
            app.MapAdditionalIdentityEndpoints();

            app.Run();
        }
    }
}
