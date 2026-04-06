using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt.Api.Dtos;

public record CreateOrderDto
{
    [Required]
    public Guid MenuId { get; init; }

    [Required]
    [MinLength(1)]
    public List<OrderItemInputDto> Items { get; init; } = new();

    public string? Notes { get; init; }
}

public record OrderItemInputDto
{
    [Required]
    public Guid MenuItemId { get; init; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; } = 1;

    public Guid? MenuComboId { get; init; }
}

public record UpdateOrderStatusDto
{
    [Required]
    public string Status { get; init; } = string.Empty;
}

public record ApplyDiscountDto
{
    [Required]
    public string Code { get; init; } = string.Empty;
}

public record UpdateQuantityDto
{
    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }
}

public record CancelOrderDto
{
    public string? Reason { get; init; }
}
