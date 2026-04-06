using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public class OrderRepository : IOrderRepository
{
    private readonly ApplicationDbContext _db;

    public OrderRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<Order>> GetAllAsync(int page = 1, int pageSize = 20)
    {
        return await _db.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.MenuItem)
            .OrderByDescending(o => o.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();
    }

    public async Task<List<Order>> GetByStatusAsync(OrderStatus status)
    {
        return await _db.Orders
            .Include(o => o.Items)
            .Where(o => o.Status == status)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<List<Order>> GetActiveOrdersAsync()
    {
        return await _db.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.MenuItem)
            .Where(o => o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled)
            .OrderByDescending(o => o.CreatedAt)
            .ToListAsync();
    }

    public async Task<Order?> GetByIdAsync(Guid id)
    {
        return await _db.Orders
            .Include(o => o.Items)
                .ThenInclude(i => i.MenuItem)
            .Include(o => o.Menu)
            .Include(o => o.Discount)
            .FirstOrDefaultAsync(o => o.Id == id);
    }

    public async Task<Order?> GetByOrderNumberAsync(string orderNumber)
    {
        return await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.OrderNumber == orderNumber);
    }

    public async Task<Order> CreateAsync(Order order)
    {
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    public async Task<Order?> UpdateAsync(Guid id, Action<Order> updateAction)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
            return null;

        updateAction(order);
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return order;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var order = await _db.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
            return false;

        // Remove related POS payments (NoAction FK)
        var payments = await _db.PosPayments.Where(p => p.OrderId == id).ToListAsync();
        _db.PosPayments.RemoveRange(payments);

        _db.Orders.Remove(order);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<string> GenerateOrderNumberAsync()
    {
        var lastOrder = await _db.Orders
            .OrderByDescending(o => o.OrderNumber)
            .Select(o => o.OrderNumber)
            .FirstOrDefaultAsync();

        if (lastOrder != null && lastOrder.StartsWith("ORD-")
            && int.TryParse(lastOrder[4..], out var lastNum))
        {
            return $"ORD-{lastNum + 1:D6}";
        }

        return "ORD-000001";
    }
}
