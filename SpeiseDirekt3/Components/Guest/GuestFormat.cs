using System.Globalization;

namespace SpeiseDirekt3.Components.Guest;

/// <summary>
/// Formatting for the guest view, in one place: prices in German notation (<c>€ 9,90</c>), image URLs,
/// anchors and item links. Independent of the server culture (the back office uses
/// <see cref="SpeiseDirekt.Infrastructure.PriceFormat"/> with <c>€ 9.90</c>).
/// </summary>
public static class GuestFormat
{
    private static readonly NumberFormatInfo GermanNumbers = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NumberGroupSizes = new[] { 3 }
    };

    /// <summary>Guest price: <c>€ 9,90</c>, <c>€ 1.234,50</c>. The space is a normal one; the price elements
    /// are <c>white-space: nowrap</c> in <c>guest-menu.css</c>.</summary>
    public static string Price(decimal price) => "€ " + Amount(price);

    /// <summary>Just the amount in German notation: <c>9,90</c>.</summary>
    public static string Amount(decimal price) => price.ToString("#,##0.00", GermanNumbers);

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
}
