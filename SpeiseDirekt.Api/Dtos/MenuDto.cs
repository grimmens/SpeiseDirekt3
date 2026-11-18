using SpeiseDirekt.Model;
using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt.Api.Dtos;

public record MenuDto
{
    [Required]
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional; null or omitted is stored as an empty description.</summary>
    public string? Description { get; init; }

    public DesignTheme Theme { get; init; } = DesignTheme.Modern;

    public MenuLanguage Language { get; init; } = MenuLanguage.German;
}
