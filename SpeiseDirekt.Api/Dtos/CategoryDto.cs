using System.ComponentModel.DataAnnotations;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.Dtos;

public record CategoryDto
{
    [Required]
    public string Name { get; init; } = string.Empty;

    [Required]
    public Guid MenuId { get; init; }

    /// <summary>
    /// Optional subtitle shown under the category name (e.g. "Mezze" or "served until 22:00"). On update, omitted
    /// (<c>null</c>) keeps the current subtitle and an empty string removes it.
    /// </summary>
    [StringLength(Category.DescriptionMaxLength, ErrorMessage = "The subtitle can have at most 200 characters.")]
    public string? Description { get; init; }

    /// <summary>
    /// Position within the menu (ascending, ties by name). Optional: on create the category goes after the
    /// last one of the menu, on update the current position is kept.
    /// </summary>
    public int? SortOrder { get; init; }
}
