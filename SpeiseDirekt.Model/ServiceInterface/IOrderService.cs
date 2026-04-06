using SpeiseDirekt.Model;

namespace SpeiseDirekt.ServiceInterface;

public interface IOrderService
{
    Task<Order> CreateOrderAsync(Guid menuId, List<CreateOrderItemDto> items, string? notes = null);
    Task<Order> AddItemAsync(Guid orderId, CreateOrderItemDto item);
    Task<Order> RemoveItemAsync(Guid orderId, Guid orderItemId);
    Task<Order> UpdateItemQuantityAsync(Guid orderId, Guid orderItemId, int quantity);
    Task<Order> ApplyDiscountAsync(Guid orderId, string discountCode);
    Task<Order> RemoveDiscountAsync(Guid orderId);
    Task<Order> ConfirmOrderAsync(Guid orderId);
    Task<Order> UpdateStatusAsync(Guid orderId, OrderStatus newStatus);
    Task<Order> CancelOrderAsync(Guid orderId, string? reason = null);
    void RecalculateTotals(Order order);
    Task<List<Order>> GetActiveOrdersAsync();
    Task<List<Order>> GetOrderHistoryAsync(int page = 1, int pageSize = 20);
    Task<Order?> GetByIdAsync(Guid id);
}
