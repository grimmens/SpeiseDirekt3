using SpeiseDirekt.Model;

namespace SpeiseDirekt.Infrastructure;

/// <summary>
/// Rules and ordered views for <see cref="MenuItemPriceOption"/>s (price variants and extras of a menu item).
/// Used by the back office, the API and the guest view.
/// </summary>
public static class PriceOptions
{
    /// <summary>The item's variants in display order (<c>SortOrder</c>, then name).</summary>
    public static IReadOnlyList<MenuItemPriceOption> Variants(this MenuItem item) => OfKind(item, PriceOptionKind.Variant);

    /// <summary>The item's extras in display order (<c>SortOrder</c>, then name).</summary>
    public static IReadOnlyList<MenuItemPriceOption> Extras(this MenuItem item) => OfKind(item, PriceOptionKind.Extra);

    public static IReadOnlyList<MenuItemPriceOption> OfKind(this MenuItem item, PriceOptionKind kind) =>
        (item.PriceOptions ?? Enumerable.Empty<MenuItemPriceOption>())
            .Where(o => o.Kind == kind)
            .OrderBy(o => o.SortOrder)
            .ThenBy(o => o.Name)
            .ToArray();

    public static bool HasVariants(this MenuItem item) =>
        item.PriceOptions?.Any(o => o.Kind == PriceOptionKind.Variant) == true;

    /// <summary>
    /// The rule for items with variants: <see cref="MenuItem.Price"/> is the lowest variant price (guests read it as
    /// "from € x"; ordering, combos and old clients keep a meaningful price). No variants: the price is left alone.
    /// Call before every save of an item whose options may have changed.
    /// </summary>
    public static void ApplyLowestVariantPrice(this MenuItem item)
    {
        if (item.HasVariants())
            item.Price = item.PriceOptions.Where(o => o.Kind == PriceOptionKind.Variant).Min(o => o.Price);
    }

    /// <summary>Renumbers the options of one kind 0..n-1 in their current display order.</summary>
    public static void Renumber(this MenuItem item, PriceOptionKind kind)
    {
        var index = 0;
        foreach (var option in item.OfKind(kind))
            option.SortOrder = index++;
    }
}
