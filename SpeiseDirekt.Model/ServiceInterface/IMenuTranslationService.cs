using SpeiseDirekt.Model;

namespace SpeiseDirekt.ServiceInterface;

/// <summary>One menu of a translation group (<see cref="Menu.TranslationGroupId"/>).</summary>
public sealed record MenuVersion(Guid Id, string Name, MenuLanguage Language);

/// <summary>Outcome of a change to a translation group: the affected menu, or a message for the owner.</summary>
public sealed record MenuTranslationResult(Guid? MenuId, string? Error)
{
    public bool Succeeded => Error is null;
    public static MenuTranslationResult Ok(Guid menuId) => new(menuId, null);
    public static MenuTranslationResult Fail(string error) => new(null, error);
}

/// <summary>
/// Language versions of a menu (back office and API, current tenant only): menus with the same
/// <see cref="Menu.TranslationGroupId"/> are the same menu in different languages, at most one per language.
/// Owners translate by hand: "Add translation" copies a menu into another language, the copy is then edited.
/// </summary>
public interface IMenuTranslationService
{
    /// <summary>
    /// The versions of <paramref name="menuId"/>'s group in language order, including the menu itself; just the menu
    /// when it is not linked; empty when the menu does not exist in the tenant.
    /// </summary>
    Task<IReadOnlyList<MenuVersion>> GetVersionsAsync(Guid menuId);

    /// <summary>
    /// Menus of the tenant that "Link existing menu" offers for <paramref name="menuId"/>: not linked to other menus
    /// yet and in a language the group does not have (by name).
    /// </summary>
    Task<IReadOnlyList<MenuVersion>> GetLinkCandidatesAsync(Guid menuId);

    /// <summary>
    /// "Add translation": copies <paramref name="menuId"/> into <paramref name="language"/> — name (unique, with the
    /// language in brackets), description, theme, categories, items with every field, label, price option, photo
    /// reference and allergen link, the menu's allergens (EU allergens named in the target language) and combos — and
    /// links both menus (creating the group when needed). One transaction; the copy shares no rows with the original
    /// except the read-only <see cref="Image"/> rows of the photos. No QR code. Fails when the group already has that
    /// language.
    /// </summary>
    Task<MenuTranslationResult> CreateTranslationAsync(Guid menuId, MenuLanguage language);

    /// <summary>
    /// "Link existing menu": adds <paramref name="otherMenuId"/> to <paramref name="menuId"/>'s group (creating it).
    /// Fails when the other menu is linked to other menus already or its language is taken in the group.
    /// </summary>
    Task<MenuTranslationResult> LinkAsync(Guid menuId, Guid otherMenuId);

    /// <summary>"Unlink": takes <paramref name="menuId"/> out of its group; a group left with a single menu is dissolved.</summary>
    Task<MenuTranslationResult> UnlinkAsync(Guid menuId);

    /// <summary>
    /// The message to show when <paramref name="menuId"/> may not switch to <paramref name="language"/> because another
    /// version of its group already has it; <c>null</c> when the change is fine.
    /// </summary>
    Task<string?> ValidateLanguageAsync(Guid menuId, MenuLanguage language);
}
