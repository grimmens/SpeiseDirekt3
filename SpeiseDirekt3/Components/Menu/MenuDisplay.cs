using Microsoft.Extensions.Localization;
using SpeiseDirekt.Model;

namespace SpeiseDirekt3.Components.Menu;

/// <summary>Display names of menu settings in the back office's language (AP12).</summary>
public static class MenuDisplay
{
    /// <summary>A design theme's name: <c>MenusText.Theme_{value}</c> ("Classic" / "Klassisch").</summary>
    public static string ThemeName(this IStringLocalizer<MenusText> texts, DesignTheme theme) => texts[$"Theme_{theme}"];

    /// <summary>A menu language's name in the back office's language ("German" / "Deutsch").</summary>
    public static string LanguageName(MenuLanguage language) => SpeiseDirekt.Infrastructure.MenuLanguages.Of(language).DisplayName;
}
