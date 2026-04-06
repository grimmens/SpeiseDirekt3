using SpeiseDirekt.Model;
using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt.Api.Dtos;

public record DiscountDto
{
    [Required]
    [StringLength(50)]
    public string Code { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }

    public DiscountType Type { get; init; }

    [Range(0, double.MaxValue)]
    public decimal Value { get; init; }

    public decimal? MinOrderAmount { get; init; }

    public DateTime? ValidFrom { get; init; }
    public DateTime? ValidTo { get; init; }

    public int? MaxUses { get; init; }

    public bool IsActive { get; init; } = true;
}

public record ValidateDiscountDto
{
    [Required]
    public string Code { get; init; } = string.Empty;

    [Range(0, double.MaxValue)]
    public decimal OrderSubTotal { get; init; }
}
