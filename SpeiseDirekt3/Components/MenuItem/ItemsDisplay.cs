using Microsoft.Extensions.Localization;
using SpeiseDirekt.Infrastructure;

namespace SpeiseDirekt3.Components.MenuItem;

/// <summary>Display texts of menu items in the back office's language (AP12).</summary>
public static class ItemsDisplay
{
    /// <summary>The resource key of a label's name: <c>Label_{PropertyName without "Is"}</c> ("Label_AlcoholFree").</summary>
    public static string LabelKey(ItemLabel label) =>
        "Label_" + (label.PropertyName.StartsWith("Is", StringComparison.Ordinal) ? label.PropertyName[2..] : label.PropertyName);

    /// <summary>A label's name: "Vegetarian" / "Vegetarisch" (the German one equals the guest chip's).</summary>
    public static string LabelName(this IStringLocalizer<ItemsText> texts, ItemLabel label) => texts[LabelKey(label)];
}
