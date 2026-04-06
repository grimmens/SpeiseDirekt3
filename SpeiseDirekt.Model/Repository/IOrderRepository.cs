using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public interface IOrderRepository
{
    Task<List<Order>> GetAllAsync(int page = 1, int pageSize = 20);
    Task<List<Order>> GetByStatusAsync(OrderStatus status);
    Task<List<Order>> GetActiveOrdersAsync();
    Task<Order?> GetByIdAsync(Guid id);
    Task<Order?> GetByOrderNumberAsync(string orderNumber);
    Task<Order> CreateAsync(Order order);
    Task<Order?> UpdateAsync(Guid id, Action<Order> updateAction);
    Task<bool> DeleteAsync(Guid id);
    Task<string> GenerateOrderNumberAsync();
}
