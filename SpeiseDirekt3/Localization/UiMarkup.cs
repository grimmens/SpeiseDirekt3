using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Components;

namespace SpeiseDirekt3.Localization;

/// <summary>
/// A localized sentence with emphasised parts (AP12): one resource string with <c>{0}</c>, <c>{1}</c>, … whose arguments
/// are shown in an element, e.g. <c>"The sections of {0}."</c> → "The sections of <strong>Lunch</strong>.". The sentence
/// and the arguments are HTML-encoded, so user content (a menu name) is never markup.
/// </summary>
public static class UiMarkup
{
    /// <summary>The sentence with each argument in <c>&lt;strong&gt;</c> (with <paramref name="cssClass"/> when given).</summary>
    public static MarkupString Strong(string format, object[] args, string? cssClass = null)
    {
        var open = cssClass is null ? "<strong>" : $"<strong class=\"{WebUtility.HtmlEncode(cssClass)}\">";
        var parts = args.Select(a => (object)(open + WebUtility.HtmlEncode(Convert.ToString(a, CultureInfo.CurrentCulture)) + "</strong>")).ToArray();
        return new MarkupString(string.Format(CultureInfo.CurrentCulture, WebUtility.HtmlEncode(format), parts));
    }

    /// <summary>The sentence with its one argument in <c>&lt;strong&gt;</c>.</summary>
    public static MarkupString Strong(string format, object arg) => Strong(format, [arg]);
}
