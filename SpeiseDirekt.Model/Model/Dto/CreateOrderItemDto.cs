namespace SpeiseDirekt.Model
{
    public record CreateOrderItemDto
    {
        public Guid MenuItemId { get; init; }
        public int Quantity { get; init; } = 1;
        public Guid? MenuComboId { get; init; }
    }
}
