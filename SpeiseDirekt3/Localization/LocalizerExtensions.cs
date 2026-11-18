using Microsoft.Extensions.Localization;

namespace SpeiseDirekt3.Localization;

/// <summary>Helpers on <see cref="IStringLocalizer"/> for the conventions of the resource files (see the repo's CLAUDE.md).</summary>
public static class LocalizerExtensions
{
    /// <summary>
    /// A count with its noun from an explicit one/other key pair: <c>{key}_One</c> for exactly 1, else <c>{key}_Other</c>
    /// (German and English both use "other" for 0). <c>{0}</c> is the count, <paramref name="args"/> follow as
    /// <c>{1}</c>, <c>{2}</c>, … Example: <c>T.Plural(3, "Items")</c> with <c>Items_One</c> = "1 item" / "1 Gericht" and
    /// <c>Items_Other</c> = "{0} items" / "{0} Gerichte" → "3 items" / "3 Gerichte".
    /// </summary>
    public static string Plural(this IStringLocalizer localizer, int count, string key, params object[] args) =>
        localizer[count == 1 ? key + "_One" : key + "_Other", [count, .. args]];
}
