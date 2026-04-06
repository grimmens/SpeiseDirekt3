using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt.Api.Dtos;

public record CategoryDto
{
    [Required]
    public string Name { get; init; } = string.Empty;

    [Required]
    public Guid MenuId { get; init; }
}
