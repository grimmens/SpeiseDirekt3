using SpeiseDirekt.Model;

namespace SpeiseDirekt3.Components.Guest;

// Inside the namespace: Components/Menu and Components/MenuItem are namespaces too
using Menu = SpeiseDirekt.Model.Menu;
using MenuItem = SpeiseDirekt.Model.MenuItem;

/// <summary>A label chip of a menu item in the guest view.</summary>
/// <param name="Key">Stable key: CSS modifier <c>gm-label--{Key}</c>, <c>data-label</c>, <c>data-labels</c> on cards, test id <c>label-{Key}</c>.</param>
/// <param name="Text">German text of the chip.</param>
/// <param name="Icon">Name of the <see cref="GuestIcon"/> shown in front of the text, or null.</param>
public sealed record GuestLabel(string Key, string Text, string? Icon);

/// <summary>
/// The labels of a menu item, in a fixed order. Add a label with one line in <see cref="Rules"/>
/// (and, if it needs one, an icon in <c>GuestIcon.razor</c> and a color in <c>guest-menu.css</c>).
/// </summary>
public static class GuestLabels
{
    public static readonly GuestLabel Vegan = new("vegan", "Vegan", "leaf");
    public static readonly GuestLabel Vegetarian = new("vegetarian", "Vegetarisch", "leaf");
    public static readonly GuestLabel Spicy = new("spicy", "Scharf", "flame");

    /// <summary>Order = order of the chips. "Vegan" implies vegetarian, so vegan items do not show "Vegetarisch".</summary>
    private static readonly (GuestLabel Label, Func<MenuItem, bool> Applies)[] Rules =
    {
        (Vegan, i => i.IsVegan),
        (Vegetarian, i => i.IsVegetarian && !i.IsVegan),
        (Spicy, i => i.IsSpicy),
    };

    /// <summary>All labels that apply to <paramref name="item"/>, in display order.</summary>
    public static IReadOnlyList<GuestLabel> For(MenuItem item) =>
        Rules.Where(r => r.Applies(item)).Select(r => r.Label).ToList();

    /// <summary>Space separated label keys for <c>data-labels</c> on cards (<c>vegan spicy</c>).</summary>
    public static string Data(MenuItem item) => string.Join(' ', For(item).Select(l => l.Key));
}
