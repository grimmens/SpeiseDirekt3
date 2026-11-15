using System.ComponentModel.DataAnnotations;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.Dtos;

/// <summary>
/// A price variant ("0,5 l" € 5.90) or an extra ("+ Halloumi" € 6.00) of a menu item, part of <see cref="MenuItemDto"/>.
/// </summary>
public record MenuItemPriceOptionDto
{
    /// <summary><c>"Variant"</c> or <c>"Extra"</c> (0 / 1 are accepted too).</summary>
    [EnumDataType(typeof(PriceOptionKind), ErrorMessage = "Kind must be Variant or Extra.")]
    public PriceOptionKind Kind { get; init; }

    [Required(AllowEmptyStrings = false, ErrorMessage = "Name is required.")]
    [StringLength(MenuItemPriceOption.NameMaxLength, ErrorMessage = "The name can have at most 60 characters.")]
    public string Name { get; init; } = string.Empty;

    [Range(0d, 1_000_000d, ErrorMessage = "The price must be 0 or more.")]
    public decimal Price { get; init; }

    /// <summary>Position among the options of the same kind. Optional: defaults to the position in the list.</summary>
    public int? SortOrder { get; init; }
}
