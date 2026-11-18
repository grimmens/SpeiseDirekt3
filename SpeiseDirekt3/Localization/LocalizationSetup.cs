using System.Globalization;
using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Localization;
using Microsoft.Net.Http.Headers;
using SpeiseDirekt3.Components.Guest;

namespace SpeiseDirekt3.Localization;

/// <summary>
/// Request localization of the Blazor app (AP12): services (<see cref="AddAppLocalization"/>) and middleware
/// (<see cref="UseAppRequestLocalization"/>).
/// <list type="bullet">
/// <item>Back office, account pages, landing page: <see cref="RequestLocalizationMiddleware"/> with one provider,
/// <see cref="BrowserLanguageCultureProvider"/> (<see cref="AppCultures.Select"/>); the default QueryString and Cookie
/// providers are gone. The Interactive Server circuit keeps the culture of the request that opened it (the
/// <c>/_blazor</c> connection carries the browser's <c>Accept-Language</c> too).</item>
/// <item>Their documents send <c>Vary: Accept-Language</c> and <c>Content-Language</c>, and <c>Cache-Control: private</c>
/// when nothing else set a policy, so no shared cache hands one language to everybody.</item>
/// <item><b>Guest pages</b> (<see cref="GuestPageAttribute"/>: <c>/qr</c>, <c>/menu</c>, <c>/menu/preview</c>) are excluded:
/// their culture is pinned to the invariant culture, so nothing on them can follow the browser. Their language is the
/// menu's (<c>GuestText</c>, which formats prices with its own number format) — unchanged by AP12.</item>
/// </list>
/// </summary>
public static class LocalizationSetup
{
    public static IServiceCollection AddAppLocalization(this IServiceCollection services)
    {
        services.AddLocalization(options => options.ResourcesPath = "Resources");
        services.Configure<RequestLocalizationOptions>(options =>
        {
            options.DefaultRequestCulture = new RequestCulture(AppCultures.Default);
            options.SupportedCultures = AppCultures.Supported.ToList();
            options.SupportedUICultures = AppCultures.Supported.ToList();
            // Only the browser decides: no ?culture=, no cookie, not the built-in Accept-Language provider (it would fall
            // back to the default for an unsupported language, and it tries only the first three entries)
            options.RequestCultureProviders = [new BrowserLanguageCultureProvider()];
        });
        return services;
    }

    public static IApplicationBuilder UseAppRequestLocalization(this IApplicationBuilder app)
    {
        app.UseWhen(IsGuestPage, guest => guest.Use(async (context, next) =>
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;
            await next(context);
        }));
        app.UseWhen(context => !IsGuestPage(context), localized =>
        {
            localized.Use(async (context, next) =>
            {
                if (context.GetEndpoint()?.Metadata.GetMetadata<ComponentTypeMetadata>() is not null)
                    context.Response.OnStarting(() =>
                    {
                        MarkLanguageDependent(context.Response);
                        return Task.CompletedTask;
                    });
                await next(context);
            });
            localized.UseRequestLocalization();
        });
        return app;
    }

    /// <summary>A page with [GuestPage] (endpoint metadata: routing runs first in the WebApplication pipeline).</summary>
    public static bool IsGuestPage(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<GuestPageAttribute>() is not null;

    /// <summary>The response's content depends on <c>Accept-Language</c>.</summary>
    private static void MarkLanguageDependent(HttpResponse response)
    {
        response.Headers.Append(HeaderNames.Vary, "Accept-Language");
        var culture = response.HttpContext.Features.Get<IRequestCultureFeature>()?.RequestCulture.UICulture;
        if (culture is not null)
            response.Headers.ContentLanguage = culture.TwoLetterISOLanguageName;
        if (string.IsNullOrEmpty(response.Headers.CacheControl))
            response.Headers.CacheControl = "private";
    }
}

/// <summary>The one culture provider: <see cref="AppCultures.Select"/> on the request's <c>Accept-Language</c>.</summary>
public sealed class BrowserLanguageCultureProvider : RequestCultureProvider
{
    public override Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        var culture = AppCultures.Select(httpContext.Request.Headers.AcceptLanguage.ToString());
        return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(culture.Name));
    }
}
