using System.Globalization;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Infrastructure;

/// <summary>What the app knows about a <see cref="MenuLanguage"/>.</summary>
/// <param name="Code">ISO 639-1 code for <c>lang</c> and <c>hreflang</c> (<c>de</c>, <c>en</c>, …).</param>
/// <param name="NativeName">The language's own name (endonym) as guests pick it: "Deutsch", "Français", "日本語".</param>
/// <param name="EnglishName">English name (back office, English guest UI).</param>
/// <param name="GermanName">German name (German guest UI).</param>
/// <param name="OgLocale">Open Graph locale (<c>og:locale</c>, language_TERRITORY).</param>
public sealed record MenuLanguageInfo(
    MenuLanguage Language, string Code, string NativeName, string EnglishName, string GermanName, string OgLocale)
{
    /// <summary>Upper-case code for compact buttons ("DE", "EN").</summary>
    public string ShortCode => Code.ToUpperInvariant();

    /// <summary>
    /// The name in the back office's language (AP12, <see cref="CultureInfo.CurrentUICulture"/>): <see cref="GermanName"/>
    /// in German, else <see cref="EnglishName"/>. Not for guest pages: their language is the menu's (<c>GuestText</c>).
    /// </summary>
    public string DisplayName => CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "de" ? GermanName : EnglishName;
}

/// <summary>
/// The one table of menu languages: ISO code, endonym, English and German name, Open Graph locale. Every
/// <see cref="MenuLanguage"/> value has an entry (the guest language switch, <c>lang</c>/<c>hreflang</c> and the back
/// office's "Language versions" read it).
/// </summary>
public static class MenuLanguages
{
    public static readonly IReadOnlyList<MenuLanguageInfo> All = new MenuLanguageInfo[]
    {
        new(MenuLanguage.German, "de", "Deutsch", "German", "Deutsch", "de_DE"),
        new(MenuLanguage.English, "en", "English", "English", "Englisch", "en_GB"),
        new(MenuLanguage.French, "fr", "Français", "French", "Französisch", "fr_FR"),
        new(MenuLanguage.Spanish, "es", "Español", "Spanish", "Spanisch", "es_ES"),
        new(MenuLanguage.Italian, "it", "Italiano", "Italian", "Italienisch", "it_IT"),
        new(MenuLanguage.Dutch, "nl", "Nederlands", "Dutch", "Niederländisch", "nl_NL"),
        new(MenuLanguage.Portuguese, "pt", "Português", "Portuguese", "Portugiesisch", "pt_PT"),
        new(MenuLanguage.Polish, "pl", "Polski", "Polish", "Polnisch", "pl_PL"),
        new(MenuLanguage.Czech, "cs", "Čeština", "Czech", "Tschechisch", "cs_CZ"),
        new(MenuLanguage.Hungarian, "hu", "Magyar", "Hungarian", "Ungarisch", "hu_HU"),
        new(MenuLanguage.Croatian, "hr", "Hrvatski", "Croatian", "Kroatisch", "hr_HR"),
        new(MenuLanguage.Slovenian, "sl", "Slovenščina", "Slovenian", "Slowenisch", "sl_SI"),
        new(MenuLanguage.Romanian, "ro", "Română", "Romanian", "Rumänisch", "ro_RO"),
        new(MenuLanguage.Bulgarian, "bg", "Български", "Bulgarian", "Bulgarisch", "bg_BG"),
        new(MenuLanguage.Greek, "el", "Ελληνικά", "Greek", "Griechisch", "el_GR"),
        new(MenuLanguage.Russian, "ru", "Русский", "Russian", "Russisch", "ru_RU"),
        new(MenuLanguage.Turkish, "tr", "Türkçe", "Turkish", "Türkisch", "tr_TR"),
        new(MenuLanguage.Arabic, "ar", "العربية", "Arabic", "Arabisch", "ar_AR"),
        new(MenuLanguage.Chinese, "zh", "中文", "Chinese", "Chinesisch", "zh_CN"),
        new(MenuLanguage.Japanese, "ja", "日本語", "Japanese", "Japanisch", "ja_JP"),
        new(MenuLanguage.Korean, "ko", "한국어", "Korean", "Koreanisch", "ko_KR"),
    };

    private static readonly IReadOnlyDictionary<MenuLanguage, MenuLanguageInfo> ByLanguage =
        All.ToDictionary(l => l.Language);

    /// <summary>The entry of <paramref name="language"/> (German for an unknown value).</summary>
    public static MenuLanguageInfo Of(MenuLanguage language) =>
        ByLanguage.TryGetValue(language, out var info) ? info : ByLanguage[MenuLanguage.German];

    /// <summary>Sorts versions of a menu for display: in the order of <see cref="MenuLanguage"/> (German, English, …).</summary>
    public static IEnumerable<T> InLanguageOrder<T>(this IEnumerable<T> source, Func<T, MenuLanguage> language) =>
        source.OrderBy(x => (int)language(x));
}
