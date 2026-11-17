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

    // Labels (optional, default false; see ItemLabels for texts and order)
    public bool IsVegan { get; init; }
    public bool IsVegetarian { get; init; }
    public bool IsSpicy { get; init; }
    public bool IsHalal { get; init; }
    public bool IsAlcoholFree { get; init; }
    public bool IsNew { get; init; }
    public bool IsPopular { get; init; }

    /// <summary>
    /// Optional price variants and extras. Create: the options of the new item (none when omitted). Update: replaces
    /// all options of the item; omitted (<c>null</c>) keeps them, an empty list removes them. When the item has at
    /// least one variant, <see cref="Price"/> is ignored and set to the lowest variant price.
    /// </summary>
    [MaxLength(MaxPriceOptions, ErrorMessage = "An item can have at most 50 price options.")]
    public List<MenuItemPriceOptionDto>? PriceOptions { get; init; }

    public const int MaxPriceOptions = 50;

    /// <summary>
    /// Position within the category (ascending, ties by name). Optional: on create the item goes after the
    /// last one of the category, on update the current position is kept.
    /// </summary>
    public int? SortOrder { get; init; }
}
