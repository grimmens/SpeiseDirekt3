using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace SpeiseDirekt3.Components.Landing;

/// <summary>Headlines whose part in [brackets] is highlighted (the landing page's hero, the account pages' brand panel).</summary>
public static class HeadlineMarkup
{
    /// <summary>
    /// "Ihre Speisekarte, [ein Scan entfernt.]" → the text with the bracketed part in a <c>span</c> of
    /// <paramref name="cssClass"/>: one resource string per sentence, the brackets only mark the styled part.
    /// </summary>
    public static RenderFragment Highlight(string text, string cssClass) => builder => Render(builder, text, cssClass);

    private static void Render(RenderTreeBuilder builder, string text, string cssClass)
    {
        var open = text.IndexOf('[');
        var close = open < 0 ? -1 : text.IndexOf(']', open + 1);
        if (close < 0)
        {
            builder.AddContent(0, text);
            return;
        }
        builder.AddContent(1, text[..open]);
        builder.OpenElement(2, "span");
        builder.AddAttribute(3, "class", cssClass);
        builder.AddContent(4, text[(open + 1)..close]);
        builder.CloseElement();
        builder.AddContent(5, text[(close + 1)..]);
    }
}
