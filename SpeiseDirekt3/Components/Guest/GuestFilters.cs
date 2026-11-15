using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;

namespace SpeiseDirekt3.Components.Guest;

// Inside the namespace: Components/Menu and Components/MenuItem are namespaces too
using MenuItem = SpeiseDirekt.Model.MenuItem;

/// <summary>An allergen guests can exclude in the filter sheet (distinct by code within a menu).</summary>
public sealed record GuestFilterAllergen(string Code, string Name);

/// <summary>
/// Server-side data for the guest search, the filter sheet and the favourites list (<c>js/guest-search-filter.js</c>,
/// <c>js/guest-favorites.js</c>). Which labels exist and how they match comes from <see cref="ItemLabels"/>; the
/// guest texts and icons from <see cref="GuestLabels"/>. Only what occurs in the menu is offered.
/// </summary>
public static class GuestFilters
{
    /// <summary>
    /// Space separated keys of every label the item <b>matches</b> for filtering (<see cref="ItemLabels.Matches"/>:
    /// a vegan item is vegetarian too), for <c>data-filter-labels</c>. <c>data-labels</c> lists only the shown
    /// labels (vegan hides "vegetarian").
    /// </summary>
    public static string LabelData(MenuItem item) =>
        string.Join(' ', ItemLabels.All.Where(l => ItemLabels.Matches(item, l)).Select(l => l.Key));

    /// <summary>The labels at least one item of the menu matches, in display order.</summary>
    public static IReadOnlyList<ItemLabel> LabelsIn(GuestMenuModel model) =>
        ItemLabels.All.Where(l => model.AllItems.Any(i => ItemLabels.Matches(i, l))).ToList();

    /// <summary>
    /// The allergens used by at least one item of the menu, by code (ordinal), one per code. The filter compares
    /// codes with the items' <c>data-allergens</c> (<see cref="GuestFormat.AllergenData"/>), so codes containing
    /// whitespace cannot be offered.
    /// </summary>
    public static IReadOnlyList<GuestFilterAllergen> AllergensIn(GuestMenuModel model) =>
        model.AllItems
            .SelectMany(i => i.Allergens)
            .Where(a => !string.IsNullOrWhiteSpace(a.Code) && !a.Code.Any(char.IsWhiteSpace))
            .GroupBy(a => a.Code, StringComparer.Ordinal)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => new GuestFilterAllergen(g.Key, g.First().Name))
            .ToList();

    /// <summary>Whether the filter sheet has anything to offer (otherwise neither button nor sheet is rendered).</summary>
    public static bool HasFilters(GuestMenuModel model) => LabelsIn(model).Count > 0 || AllergensIn(model).Count > 0;

    /// <summary>
    /// Text the search matches (<c>data-search-text</c>): name, description, category and the German texts of every
    /// label the item matches (so "vegetarisch" finds vegan dishes too, like the filter).
    /// </summary>
    public static string SearchText(MenuItem item, string categoryName) =>
        string.Join(' ', new[] { item.Name, item.Description, categoryName }
            .Concat(ItemLabels.All.Where(l => ItemLabels.Matches(item, l)).Select(l => l.GuestText))
            .Where(s => !string.IsNullOrWhiteSpace(s)));

    /// <summary>The shown label texts of an item for the meta line of a search result (<c>Vegan, Scharf</c>).</summary>
    public static string LabelTexts(MenuItem item) => string.Join(", ", GuestLabels.For(item).Select(l => l.Text));

    /// <summary>
    /// The item's price variants for the favourites list (display order; the first is the default). Empty when the
    /// item has a single price.
    /// </summary>
    public static IReadOnlyList<MenuItemPriceOption> Variants(MenuItem item) => item.Variants();
}
