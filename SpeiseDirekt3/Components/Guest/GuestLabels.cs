using SpeiseDirekt.Infrastructure;
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
/// The labels of a menu item for the guest view. Which labels exist, their German text and their order come from
/// the shared list <see cref="ItemLabels"/> (also used by the back office); this class only adds the guest icon.
/// A new label therefore needs a line in <see cref="ItemLabels"/>, optionally an icon here (+ <c>GuestIcon.razor</c>)
/// and a color in <c>guest-menu.css</c>.
/// </summary>
public static class GuestLabels
{
    private static readonly Dictionary<string, string> Icons = new()
    {
        [ItemLabels.Vegan.Key] = "leaf",
        [ItemLabels.Vegetarian.Key] = "leaf",
        [ItemLabels.Spicy.Key] = "flame",
    };

    /// <summary>The chip of one shared label.</summary>
    public static GuestLabel Of(ItemLabel label) =>
        new(label.Key, label.GuestText, Icons.GetValueOrDefault(label.Key));

    /// <summary>All labels that apply to <paramref name="item"/>, in display order ("vegan" hides "Vegetarisch").</summary>
    public static IReadOnlyList<GuestLabel> For(MenuItem item) =>
        ItemLabels.ActiveFor(item).Select(Of).ToList();

    /// <summary>Space separated label keys for <c>data-labels</c> on cards (<c>vegan spicy</c>).</summary>
    public static string Data(MenuItem item) => string.Join(' ', For(item).Select(l => l.Key));
}
