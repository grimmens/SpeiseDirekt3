using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt.Api.Dtos;

public record MenuItemDto
{
    [Required]
    public string Name { get; init; } = string.Empty;

    /// <summary>Optional (e.g. drinks); stored as an empty string when missing.</summary>
    public string? Description { get; init; }

    public List<Guid> AllergenIds { get; init; } = new();

    public decimal Price { get; init; }

    [Required]
    public Guid CategoryId { get; init; }

    // Dietary labels (optional, default false)
    public bool IsVegan { get; init; }
    public bool IsVegetarian { get; init; }
    public bool IsSpicy { get; init; }

    /// <summary>
    /// Position within the category (ascending, ties by name). Optional: on create the item goes after the
    /// last one of the category, on update the current position is kept.
    /// </summary>
    public int? SortOrder { get; init; }
}
