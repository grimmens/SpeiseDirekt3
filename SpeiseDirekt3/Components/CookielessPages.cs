using Microsoft.AspNetCore.Antiforgery;
using Microsoft.Extensions.Options;

namespace SpeiseDirekt3.Components;

/// <summary>
/// AP15a: the public pages without a form set <b>no cookie at all</b> — the guest pages (<c>/qr/{id}</c>, <c>/menu/{id}</c>,
/// <c>/menu/preview/{id}</c>, <c>/demo</c>; <see cref="PageDocument.Guest"/>) and the landing document (<c>/</c>, the
/// imprint <c>/impressum</c>, the privacy policy <c>/datenschutz</c> — its opt-out switch is a script button, not a form;
/// <see cref="PageDocument.Landing"/>). The privacy policy says so (point 5): keep both true together. Blazor's static SSR generates the antiforgery tokens on
/// every page response before it starts (whether the page has a form or not), which stores the antiforgery cookie on a
/// first visit. These pages have no form that posts (the guests' item clicks are a JSON <c>fetch</c>, which the
/// antiforgery middleware does not check), so that cookie is removed from their responses again; nothing else changes
/// (cache headers, <c>X-Frame-Options</c>). The account pages and the back office keep it: their forms need it.
/// </summary>
public static class CookielessPages
{
    /// <summary>True for an endpoint whose document has no form: guest and landing pages.</summary>
    public static bool IsCookieless(Endpoint? endpoint) =>
        endpoint?.Metadata.GetMetadata<OwnDocumentAttribute>()?.Document is PageDocument.Guest or PageDocument.Landing;

    /// <summary>
    /// Registers, before the endpoint runs, a response-start callback that drops the antiforgery cookie of
    /// <see cref="IsCookieless"/> pages. Response-start callbacks run in reverse order of registration, so it runs after
    /// the one of Blazor's endpoint that stored the cookie.
    /// </summary>
    public static IApplicationBuilder UseCookielessPublicPages(this IApplicationBuilder app)
    {
        var cookieName = app.ApplicationServices.GetRequiredService<IOptions<AntiforgeryOptions>>().Value.Cookie.Name;
        return app.Use((context, next) =>
        {
            context.Response.OnStarting(() =>
            {
                if (cookieName is { Length: > 0 } && IsCookieless(context.GetEndpoint()))
                {
                    var cookies = context.Response.Headers.SetCookie;
                    var kept = cookies.Where(c => c is null || !c.StartsWith(cookieName + "=", StringComparison.Ordinal)).ToArray();
                    if (kept.Length != cookies.Count)
                        context.Response.Headers.SetCookie = kept;
                }
                return Task.CompletedTask;
            });
            return next(context);
        });
    }
}
