using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt.Api.Dtos;

public record MenuItemDto
{
    [Required]
    public string Name { get; init; } = string.Empty;

    [Required]
    public string Description { get; init; } = string.Empty;

    public string Allergens { get; init; } = string.Empty;

    public decimal Price { get; init; }

    [Required]
    public Guid CategoryId { get; init; }
}
