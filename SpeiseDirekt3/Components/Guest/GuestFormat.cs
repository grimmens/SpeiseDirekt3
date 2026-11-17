using System.Globalization;
using SpeiseDirekt.Infrastructure;

namespace SpeiseDirekt3.Components.Guest;

/// <summary>
/// Formatting for the guest view, in one place: image URLs, anchors, item links, public paths and origin. Prices are
/// formatted by <see cref="GuestText"/> (<c>€ 9,90</c> on German menus, <c>€9.90</c> on all others), independent of the
/// server culture (the back office uses <see cref="SpeiseDirekt.Infrastructure.PriceFormat"/> with <c>€ 9.90</c>).
/// </summary>
public static class GuestFormat
{
    /// <summary>Whether the item's variants have more than one price (then its price is a "from" price, <see cref="GuestText.ItemPrice"/>).</summary>
    public static bool HasPriceRange(SpeiseDirekt.Model.MenuItem item) =>
        item.Variants().Select(v => v.Price).Distinct().Skip(1).Any();

    /// <summary>Machine-readable price for <c>data-price</c> (invariant, <c>9.90</c>), e.g. for sorting or filters in JS.</summary>
    public static string PriceData(decimal price) => price.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>
    /// Absolute URL of an item image. Uploads are stored as <c>image/{id}</c> (relative); guest pages have no
    /// <c>&lt;base href&gt;</c>, so every URL there must start with <c>/</c> or a scheme.
    /// </summary>
    public static string? ImageUrl(string? imagePath)
    {
        if (string.IsNullOrWhiteSpace(imagePath))
            return null;
        if (imagePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || imagePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || imagePath.StartsWith('/'))
            return imagePath;
        return "/" + imagePath;
    }

    /// <summary>Id of a category section, target of the chips (<c>#cat-…</c>).</summary>
    public static string CategoryAnchor(Guid categoryId) => $"cat-{categoryId:N}";

    /// <summary>Id of an item card/row (<c>#item-…</c>), target of the no-JS close link of the item sheet.</summary>
    public static string ItemAnchor(Guid itemId) => $"item-{itemId:N}";

    /// <summary>Id of the item's name heading in its detail sheet (the sheet's accessible name).</summary>
    public static string ItemTitleId(Guid itemId) => $"gm-item-title-{itemId:N}";

    /// <summary>Link that opens an item: same page with <c>?item=&lt;id&gt;</c> (deep link, works without JS).</summary>
    public static string ItemUrl(string basePath, Guid itemId) => $"{basePath}?item={itemId}";

    /// <summary>Allergen codes of an item for cards: sorted by code, comma separated (<c>A, C, G</c>).</summary>
    public static string AllergenCodes(SpeiseDirekt.Model.MenuItem item) =>
        string.Join(", ", item.Allergens.OrderBy(a => a.Code, StringComparer.Ordinal).Select(a => a.Code));

    /// <summary>Space separated allergen codes for <c>data-allergens</c> (<c>A C G</c>).</summary>
    public static string AllergenData(SpeiseDirekt.Model.MenuItem item) =>
        string.Join(' ', item.Allergens.OrderBy(a => a.Code, StringComparer.Ordinal).Select(a => a.Code));

    // --- Price options (variants, extras) ---------------------------------------------------------------

    /// <summary>
    /// Name of an extra without a leading "+" (owners often type "+ Halloumi"; the price column carries the plus).
    /// </summary>
    public static string ExtraName(string name)
    {
        var trimmed = name.TrimStart('+', ' ', ' ');
        return trimmed.Length > 0 ? trimmed : name;
    }

    // --- Restaurant profile -------------------------------------------------------------------------------

    /// <summary>Public, canonical path of a menu: <c>/menu/{id}</c> (also for links shared from <c>/qr/…</c> or the preview).</summary>
    public static string PublicMenuPath(Guid menuId) => $"/menu/{menuId}";

    /// <summary>Owner preview of a menu: <c>/menu/preview/{id}</c>.</summary>
    public static string PreviewMenuPath(Guid menuId) => $"/menu/preview/{menuId}";

    /// <summary><c>tel:</c> link of a phone number as typed (<c>+43 1 234 56 78</c> → <c>tel:+4312345678</c>).</summary>
    public static string TelHref(string phone)
    {
        var digits = new string(phone.Where(c => char.IsDigit(c) || c == '+').ToArray());
        return "tel:" + (digits.Length > 0 ? digits : phone.Trim());
    }

    /// <summary>
    /// Outbound link to a map search for the restaurant (a plain link, no embedded map, nothing loaded with the page):
    /// <c>https://www.google.com/maps/search/?api=1&amp;query=Name, Street 1, 1010 Wien</c>.
    /// </summary>
    public static string MapsUrl(string? name, string address)
    {
        var lines = address.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var query = string.Join(", ", string.IsNullOrWhiteSpace(name) ? lines : lines.Prepend(name.Trim()));
        return "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString(query);
    }

    /// <summary>A web address as people read it: <c>https://www.example.com/team/</c> → <c>example.com/team</c>.</summary>
    public static string DisplayUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var path = uri.AbsolutePath.TrimEnd('/');
        return host + path;
    }

    /// <summary>
    /// Scheme and host of the page as the guest's browser sees it, for absolute URLs in meta tags (<c>og:url</c>,
    /// <c>og:image</c>, canonical link): behind the reverse proxy the forwarded headers win over Kestrel's own
    /// <c>http://localhost:…</c>. <c>null</c> without a request.
    /// </summary>
    public static string? PublicOrigin(Microsoft.AspNetCore.Http.HttpRequest? request)
    {
        if (request is null)
            return null;
        static string? First(string? value) =>
            value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
        var scheme = First(request.Headers["X-Forwarded-Proto"]) ?? request.Scheme;
        var host = First(request.Headers["X-Forwarded-Host"]) ?? request.Host.Value;
        if (string.IsNullOrEmpty(host) || (scheme != "http" && scheme != "https"))
            return null;
        return $"{scheme}://{host}";
    }

    /// <summary>Absolute URL of <paramref name="path"/> (<c>/menu/…</c>, an image URL) on <paramref name="origin"/>.</summary>
    public static string? Absolute(string? origin, string? path)
    {
        if (string.IsNullOrEmpty(path))
            return null;
        if (path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || path.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return path;
        return origin is null ? null : origin + (path.StartsWith('/') ? path : "/" + path);
    }

    /// <summary>One line of plain text for meta descriptions: line breaks become spaces, cut at a word to ≤ <paramref name="max"/> characters.</summary>
    public static string Excerpt(string text, int max = 200)
    {
        var flat = string.Join(' ', text.Split((char[])['\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        if (flat.Length <= max)
            return flat;
        var cut = flat.LastIndexOf(' ', max - 1);
        return flat[..(cut > max / 2 ? cut : max - 1)].TrimEnd(' ', ',', '.', ';', ':', '·', '–') + "…";
    }
}
