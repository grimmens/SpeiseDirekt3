using System.Globalization;
using Microsoft.Net.Http.Headers;

namespace SpeiseDirekt3.Localization;

/// <summary>
/// The languages of the back office (incl. the account pages under <c>/Account</c>) and the landing page, and the one
/// rule that picks one: <b>only the browser decides</b>, through <c>Accept-Language</c> (no switcher, cookie or query
/// string). Supported are German and English; a regional variant counts as its language (<c>de-AT</c> → German,
/// <c>en-GB</c> → English). The guest pages are not part of this: their language is the menu's (<c>GuestText</c>).
/// </summary>
public static class AppCultures
{
    /// <summary>German (formal "Sie"). Neutral culture: formats like de-DE ("5,90 €", "18.11.2026").</summary>
    public static readonly CultureInfo German = CultureInfo.GetCultureInfo("de");

    /// <summary>English, the neutral language of the resources. Formats like en-US ("€5.90", "11/18/2026").</summary>
    public static readonly CultureInfo English = CultureInfo.GetCultureInfo("en");

    public static readonly IReadOnlyList<CultureInfo> Supported = [German, English];

    /// <summary>No (usable) <c>Accept-Language</c> at all — crawlers, curl: German, the product's market.</summary>
    public static CultureInfo Default => German;

    /// <summary>A header that names no supported language (e.g. only <c>fr</c>): English.</summary>
    public static CultureInfo Fallback => English;

    /// <summary>
    /// Picks the culture for an <c>Accept-Language</c> header: its entries by quality (highest first, ties in header
    /// order, <c>q=0</c> = not acceptable), the first whose language is German or English wins; <c>*</c> accepts any
    /// language and gets the <see cref="Default"/>. No header or an unreadable one → <see cref="Default"/>; a header
    /// without a supported language → <see cref="Fallback"/>.
    /// Examples: <c>fr-FR, en;q=0.8</c> → English; <c>fr-CH, de;q=0.9, en;q=0.8</c> → German; <c>de-AT</c> → German.
    /// </summary>
    public static CultureInfo Select(string? acceptLanguage)
    {
        if (string.IsNullOrWhiteSpace(acceptLanguage)
            || !StringWithQualityHeaderValue.TryParseList(acceptLanguage.Split(','), out var entries)
            || entries.Count == 0)
            return Default;

        foreach (var entry in entries.OrderByDescending(e => e.Quality ?? 1.0))
        {
            if (entry.Quality is <= 0)
                continue;
            var tag = entry.Value.Value ?? "";
            if (tag == "*")
                return Default;
            var dash = tag.IndexOf('-');
            var language = (dash < 0 ? tag : tag[..dash]).ToLowerInvariant();
            if (language == "de") return German;
            if (language == "en") return English;
        }
        return Fallback;
    }

    /// <summary>True while the current request (or circuit) shows German.</summary>
    public static bool IsGerman => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de";

    /// <summary>The <c>lang</c> of the back-office and landing documents: <c>de</c> or <c>en</c>.</summary>
    public static string HtmlLang => IsGerman ? "de" : "en";

    /// <summary>The landing page's <c>og:locale</c>: <c>de_DE</c> or <c>en_US</c>.</summary>
    public static string OpenGraphLocale => IsGerman ? "de_DE" : "en_US";
}
