using System.Text;
using SpeiseDirekt3.Components.Guest;

namespace SpeiseDirekt3.Components.Landing;

/// <summary>
/// <c>/robots.txt</c> and <c>/sitemap.xml</c> (AP13). Search engines may crawl the landing page and the guest pages
/// (<c>/menu/{id}</c>, <c>/qr/{id}</c> — crawlable as before, an owner's public menu is meant to be found), but not
/// the back office or the account pages. The sitemap lists the indexable pages of our own, <see cref="SitemapPaths"/>:
/// <c>/</c>, the imprint <c>/impressum</c> and the privacy policy <c>/datenschutz</c> (the guest pages belong to the restaurants, the demo and the preview are
/// <c>noindex</c>). Absolute URLs use the origin the browser sees (<see cref="GuestFormat.PublicOrigin"/>, after
/// <c>UseForwardedHeaders</c>).
/// </summary>
public static class SeoEndpoints
{
    /// <summary>
    /// Path prefixes crawlers must not visit: the back office (its routes in the casings the app links them with), the
    /// account pages, the owner preview and the app's own endpoints. Guest pages and the landing page stay allowed.
    /// </summary>
    public static readonly IReadOnlyList<string> Disallowed =
    [
        "/Account/", "/dashboard", "/menus", "/menudetail/", "/menu/edit", "/menu/preview/", "/Categories/", "/categories/",
        "/MenuItems/", "/menuitems/", "/allergens/", "/combos/", "/qrcodes", "/qrcode/", "/analytics", "/Analytics",
        "/restaurant", "/users", "/guest/api/", "/api/", "/_blazor", "/_framework/"
    ];

    /// <summary>The pages of <c>/sitemap.xml</c>: the landing page, the imprint (AP15a) and the privacy policy (AP16b).</summary>
    public static readonly IReadOnlyList<string> SitemapPaths = ["/", Legal.ImprintPage.Path, Legal.PrivacyPage.Path];

    public static IEndpointRouteBuilder MapSeoEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/robots.txt", (HttpContext http) =>
        {
            var text = new StringBuilder();
            text.Append("User-agent: *\n");
            text.Append("Allow: /\n");
            foreach (var path in Disallowed)
                text.Append("Disallow: ").Append(path).Append('\n');
            if (GuestFormat.PublicOrigin(http.Request) is { } origin)
                text.Append('\n').Append("Sitemap: ").Append(origin).Append("/sitemap.xml\n");
            http.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.Text(text.ToString(), "text/plain; charset=utf-8");
        });

        endpoints.MapGet("/sitemap.xml", (HttpContext http) =>
        {
            var origin = GuestFormat.PublicOrigin(http.Request);
            var urls = string.Concat(SitemapPaths.Select(path =>
                $"  <url><loc>{System.Security.SecurityElement.Escape(GuestFormat.Absolute(origin, path))}</loc></url>\n"));
            var xml = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                {urls}</urlset>

                """;
            http.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.Text(xml, "application/xml; charset=utf-8");
        });
        return endpoints;
    }
}
