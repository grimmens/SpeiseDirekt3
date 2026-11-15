using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt.Api.Dtos;

public record CategoryDto
{
    [Required]
    public string Name { get; init; } = string.Empty;

    [Required]
    public Guid MenuId { get; init; }

    /// <summary>
    /// Position within the menu (ascending, ties by name). Optional: on create the category goes after the
    /// last one of the menu, on update the current position is kept.
    /// </summary>
    public int? SortOrder { get; init; }
}
