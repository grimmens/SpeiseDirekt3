using SpeiseDirekt.Model;
using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt.Api.Dtos;

public record MenuDto
{
    [Required]
    public string Name { get; init; } = string.Empty;

    [Required]
    public string Description { get; init; } = string.Empty;

    public DesignTheme Theme { get; init; } = DesignTheme.Modern;

    public MenuLanguage Language { get; init; } = MenuLanguage.German;
}
