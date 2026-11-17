using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.Dtos;

/// <summary>
/// One language version of a menu (<c>GET /api/menus/{id}/language-versions</c>): menus with the same
/// <c>translationGroupId</c>. <see cref="IsCurrent"/> marks the menu the request asked for.
/// </summary>
public sealed record MenuLanguageVersionDto(Guid Id, string Name, MenuLanguage Language, bool IsCurrent);
