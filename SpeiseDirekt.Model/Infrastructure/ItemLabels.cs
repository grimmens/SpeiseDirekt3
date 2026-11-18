using SpeiseDirekt.Model;

namespace SpeiseDirekt.Infrastructure;

/// <summary>What a label says about an item: a dietary fact (filterable) or a highlight set by the restaurant.</summary>
public enum ItemLabelKind
{
    /// <summary>Vegan, vegetarian, halal, alcohol-free, spicy: facts guests filter by.</summary>
    Dietary,

    /// <summary>New, popular: set by the restaurant to draw attention.</summary>
    Highlight
}

/// <summary>
/// One label of a menu item (stored as a <c>bool</c> on <see cref="MenuItem"/>). The back office names it from its
/// resources (<c>ItemsText.Label_{PropertyName without "Is"}</c>, AP12).
/// </summary>
/// <param name="Key">Stable key for markup, CSS and JS (<c>data-label="vegan"</c>); never changes.</param>
/// <param name="PropertyName">The <see cref="MenuItem"/> property (also the editor checkbox id <c>MenuItem.{PropertyName}</c>).</param>
/// <param name="GuestText">German text of the guest view's label chip (capitalised: "Vegetarisch").</param>
/// <param name="GuestTextEnglish">English text of the guest view's label chip (menus in any language but German).</param>
/// <param name="Order">Display order (ascending) everywhere.</param>
public sealed record ItemLabel(
    string Key,
    string PropertyName,
    string GuestText,
    string GuestTextEnglish,
    ItemLabelKind Kind,
    int Order,
    Func<MenuItem, bool> Get,
    Action<MenuItem, bool> Set);

/// <summary>
/// The single list of item labels used by the back office (editor, item table), the guest view and the guest
/// filter. Add a label here (plus its <see cref="MenuItem"/> property and a migration) and every place picks it up.
/// </summary>
public static class ItemLabels
{
    public static readonly ItemLabel Popular = new("popular", nameof(MenuItem.IsPopular), "Beliebt", "Popular",
        ItemLabelKind.Highlight, 10, i => i.IsPopular, (i, v) => i.IsPopular = v);

    public static readonly ItemLabel New = new("new", nameof(MenuItem.IsNew), "Neu", "New",
        ItemLabelKind.Highlight, 20, i => i.IsNew, (i, v) => i.IsNew = v);

    public static readonly ItemLabel Vegan = new("vegan", nameof(MenuItem.IsVegan), "Vegan", "Vegan",
        ItemLabelKind.Dietary, 30, i => i.IsVegan, (i, v) => i.IsVegan = v);

    public static readonly ItemLabel Vegetarian = new("vegetarian", nameof(MenuItem.IsVegetarian), "Vegetarisch", "Vegetarian",
        ItemLabelKind.Dietary, 40, i => i.IsVegetarian, (i, v) => i.IsVegetarian = v);

    public static readonly ItemLabel Halal = new("halal", nameof(MenuItem.IsHalal), "Halal", "Halal",
        ItemLabelKind.Dietary, 50, i => i.IsHalal, (i, v) => i.IsHalal = v);

    public static readonly ItemLabel AlcoholFree = new("alcohol-free", nameof(MenuItem.IsAlcoholFree), "Alkoholfrei", "Alcohol-free",
        ItemLabelKind.Dietary, 60, i => i.IsAlcoholFree, (i, v) => i.IsAlcoholFree = v);

    public static readonly ItemLabel Spicy = new("spicy", nameof(MenuItem.IsSpicy), "Scharf", "Spicy",
        ItemLabelKind.Dietary, 70, i => i.IsSpicy, (i, v) => i.IsSpicy = v);

    /// <summary>The guest chip text: German on German menus, English on every other menu (<c>GuestText</c> in the app).</summary>
    public static string GuestTextFor(this ItemLabel label, bool german) => german ? label.GuestText : label.GuestTextEnglish;

    /// <summary>All labels in display order.</summary>
    public static readonly IReadOnlyList<ItemLabel> All =
        new[] { Popular, New, Vegan, Vegetarian, Halal, AlcoholFree, Spicy }.OrderBy(l => l.Order).ToArray();

    /// <summary>Labels of one kind in display order (e.g. the dietary ones for the guest filter).</summary>
    public static IReadOnlyList<ItemLabel> OfKind(ItemLabelKind kind) => All.Where(l => l.Kind == kind).ToArray();

    public static ItemLabel? ByKey(string key) => All.FirstOrDefault(l => l.Key == key);

    /// <summary>
    /// The labels to <b>show</b> for <paramref name="item"/>, in display order. "vegan" implies "vegetarian", so a
    /// vegan item does not show "vegetarian" as well (the stored flag is untouched).
    /// </summary>
    public static IReadOnlyList<ItemLabel> ActiveFor(MenuItem item) =>
        All.Where(l => l.Get(item) && !(ReferenceEquals(l, Vegetarian) && item.IsVegan)).ToArray();

    /// <summary>
    /// Whether <paramref name="item"/> matches <paramref name="label"/> for a filter: a vegan item counts as vegetarian.
    /// </summary>
    public static bool Matches(MenuItem item, ItemLabel label) =>
        label.Get(item) || (ReferenceEquals(label, Vegetarian) && item.IsVegan);
}
