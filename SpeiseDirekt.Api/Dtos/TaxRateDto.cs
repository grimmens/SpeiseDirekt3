using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt.Api.Dtos;

public record TaxRateDto
{
    [Required]
    [StringLength(100)]
    public string Name { get; init; } = string.Empty;

    [Range(0, 1)]
    public decimal Rate { get; init; }

    public bool IsDefault { get; init; }
}
