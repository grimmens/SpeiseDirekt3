using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;
using SpeiseDirekt.Repository;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation;

public class OrderService : IOrderService
{
    private readonly ApplicationDbContext _db;
    private readonly IOrderRepository _orderRepo;
    private readonly ITaxRateRepository _taxRateRepo;
    private readonly IDiscountService _discountService;

    public OrderService(
        ApplicationDbContext db,
        IOrderRepository orderRepo,
        ITaxRateRepository taxRateRepo,
        IDiscountService discountService)
    {
        _db = db;
        _orderRepo = orderRepo;
        _taxRateRepo = taxRateRepo;
        _discountService = discountService;
    }

    public async Task<Order> CreateOrderAsync(Guid menuId, List<CreateOrderItemDto> items, string? notes = null)
    {
        var defaultTaxRate = await _taxRateRepo.GetDefaultAsync();
        var defaultRate = defaultTaxRate?.Rate ?? 0m;

        var order = new Order
        {
            OrderNumber = await _orderRepo.GenerateOrderNumberAsync(),
            MenuId = menuId,
            Status = OrderStatus.Draft,
            Notes = notes
        };

        foreach (var dto in items)
        {
            var orderItem = await BuildOrderItemAsync(dto, defaultRate);
            order.Items.Add(orderItem);
        }

        RecalculateTotals(order);
        return await _orderRepo.CreateAsync(order);
    }

    public async Task<Order> AddItemAsync(Guid orderId, CreateOrderItemDto item)
    {
        var order = await _orderRepo.GetByIdAsync(orderId)
            ?? throw new InvalidOperationException($"Order '{orderId}' not found.");
        EnsureDraft(order);

        var defaultTaxRate = await _taxRateRepo.GetDefaultAsync();
        var orderItem = await BuildOrderItemAsync(item, defaultTaxRate?.Rate ?? 0m);
        orderItem.OrderId = orderId;

        _db.OrderItems.Add(orderItem);
        RecalculateTotals(order);
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return order;
    }

    public async Task<Order> RemoveItemAsync(Guid orderId, Guid orderItemId)
    {
        var order = await _orderRepo.GetByIdAsync(orderId)
            ?? throw new InvalidOperationException($"Order '{orderId}' not found.");
        EnsureDraft(order);

        var item = order.Items.FirstOrDefault(i => i.Id == orderItemId)
            ?? throw new InvalidOperationException($"OrderItem '{orderItemId}' not found.");

        _db.OrderItems.Remove(item);
        order.Items.Remove(item);
        RecalculateTotals(order);
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return order;
    }

    public async Task<Order> UpdateItemQuantityAsync(Guid orderId, Guid orderItemId, int quantity)
    {
        if (quantity < 1)
            throw new ArgumentException("Quantity must be at least 1.");

        var order = await _orderRepo.GetByIdAsync(orderId)
            ?? throw new InvalidOperationException($"Order '{orderId}' not found.");
        EnsureDraft(order);

        var item = order.Items.FirstOrDefault(i => i.Id == orderItemId)
            ?? throw new InvalidOperationException($"OrderItem '{orderItemId}' not found.");

        item.Quantity = quantity;
        item.LineTotal = Math.Round(item.UnitPrice * quantity, 2, MidpointRounding.AwayFromZero);
        item.TaxAmount = Math.Round(item.LineTotal * item.TaxRate, 2, MidpointRounding.AwayFromZero);

        RecalculateTotals(order);
        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return order;
    }

    public async Task<Order> ApplyDiscountAsync(Guid orderId, string discountCode)
    {
        var order = await _orderRepo.GetByIdAsync(orderId)
            ?? throw new InvalidOperationException($"Order '{orderId}' not found.");
        EnsureDraft(order);

        var (isValid, error) = await _discountService.ValidateDiscountAsync(discountCode, order.SubTotal);
        if (!isValid)
            throw new InvalidOperationException(error ?? "Invalid discount code.");

        var discount = await _discountService.GetByCodeAsync(discountCode)
            ?? throw new InvalidOperationException("Discount not found.");

        order.DiscountId = discount.Id;

        if (discount.Type == DiscountType.Percentage)
            order.DiscountAmount = Math.Round(order.SubTotal * discount.Value / 100m, 2, MidpointRounding.AwayFromZero);
        else
            order.DiscountAmount = Math.Min(discount.Value, order.SubTotal + order.TaxAmount);

        order.GrandTotal = order.SubTotal + order.TaxAmount - order.DiscountAmount;
        order.UpdatedAt = DateTime.UtcNow;

        // Increment usage atomically
        discount.CurrentUses++;

        await _db.SaveChangesAsync();
        return order;
    }

    public async Task<Order> RemoveDiscountAsync(Guid orderId)
    {
        var order = await _orderRepo.GetByIdAsync(orderId)
            ?? throw new InvalidOperationException($"Order '{orderId}' not found.");
        EnsureDraft(order);

        if (order.DiscountId.HasValue)
        {
            var discount = await _discountService.GetByIdAsync(order.DiscountId.Value);
            if (discount != null && discount.CurrentUses > 0)
                discount.CurrentUses--;

            order.DiscountId = null;
            order.DiscountAmount = 0;
            RecalculateTotals(order);
        }

        order.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return order;
    }

    public async Task<Order> ConfirmOrderAsync(Guid orderId)
    {
        return await UpdateStatusAsync(orderId, OrderStatus.Confirmed);
    }

    public async Task<Order> UpdateStatusAsync(Guid orderId, OrderStatus newStatus)
    {
        var order = await _orderRepo.GetByIdAsync(orderId)
            ?? throw new InvalidOperationException($"Order '{orderId}' not found.");

        ValidateStatusTransition(order.Status, newStatus);

        order.Status = newStatus;
        order.UpdatedAt = DateTime.UtcNow;

        if (newStatus == OrderStatus.Completed)
            order.CompletedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return order;
    }

    public async Task<Order> CancelOrderAsync(Guid orderId, string? reason = null)
    {
        var order = await _orderRepo.GetByIdAsync(orderId)
            ?? throw new InvalidOperationException($"Order '{orderId}' not found.");

        ValidateStatusTransition(order.Status, OrderStatus.Cancelled);

        order.Status = OrderStatus.Cancelled;
        order.CancelledAt = DateTime.UtcNow;
        order.CancellationReason = reason;
        order.UpdatedAt = DateTime.UtcNow;

        // Reverse discount usage if one was applied
        if (order.DiscountId.HasValue)
        {
            var discount = await _discountService.GetByIdAsync(order.DiscountId.Value);
            if (discount != null && discount.CurrentUses > 0)
                discount.CurrentUses--;
        }

        await _db.SaveChangesAsync();
        return order;
    }

    public void RecalculateTotals(Order order)
    {
        foreach (var item in order.Items)
        {
            item.LineTotal = Math.Round(item.UnitPrice * item.Quantity, 2, MidpointRounding.AwayFromZero);
            item.TaxAmount = Math.Round(item.LineTotal * item.TaxRate, 2, MidpointRounding.AwayFromZero);
        }

        order.SubTotal = order.Items.Sum(i => i.LineTotal);
        order.TaxAmount = order.Items.Sum(i => i.TaxAmount);
        order.GrandTotal = order.SubTotal + order.TaxAmount - order.DiscountAmount;
    }

    public async Task<Order> CreatePosOrderAsync(Guid menuId, Guid menuOwnerId, List<CreateOrderItemDto> items, string? notes = null)
    {
        // Get default tax rate for this tenant (bypassing query filters)
        var defaultTax = await _db.TaxRates.IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.ApplicationUserId == menuOwnerId && t.IsDefault);
        var defaultRate = defaultTax?.Rate ?? 0m;

        // Generate order number (bypassing query filters)
        var lastOrderNumber = await _db.Orders.IgnoreQueryFilters()
            .Where(o => o.ApplicationUserId == menuOwnerId)
            .OrderByDescending(o => o.OrderNumber)
            .Select(o => o.OrderNumber)
            .FirstOrDefaultAsync();

        string orderNumber;
        if (lastOrderNumber != null && lastOrderNumber.StartsWith("ORD-")
            && int.TryParse(lastOrderNumber[4..], out var lastNum))
            orderNumber = $"ORD-{lastNum + 1:D6}";
        else
            orderNumber = "ORD-000001";

        var order = new Order
        {
            OrderNumber = orderNumber,
            MenuId = menuId,
            Status = OrderStatus.Draft,
            Notes = notes,
            ApplicationUserId = menuOwnerId // Assign to menu owner
        };

        foreach (var dto in items)
        {
            var orderItem = await BuildPosOrderItemAsync(dto, defaultRate, menuOwnerId);
            orderItem.ApplicationUserId = menuOwnerId;
            order.Items.Add(orderItem);
        }

        RecalculateTotals(order);
        order.TrackingCode = GenerateTrackingCode();
        _db.Orders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    public Task<List<Order>> GetActiveOrdersAsync() => _orderRepo.GetActiveOrdersAsync();
    public Task<List<Order>> GetOrderHistoryAsync(int page = 1, int pageSize = 20) => _orderRepo.GetAllAsync(page, pageSize);
    public Task<Order?> GetByIdAsync(Guid id) => _orderRepo.GetByIdAsync(id);

    public async Task<Order?> GetByTrackingCodeAsync(string trackingCode)
    {
        return await _db.Orders.IgnoreQueryFilters()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.TrackingCode == trackingCode);
    }

    private async Task<OrderItem> BuildPosOrderItemAsync(CreateOrderItemDto dto, decimal defaultTaxRate, Guid ownerId)
    {
        var menuItem = await _db.MenuItems.IgnoreQueryFilters()
            .Include(mi => mi.TaxRate)
            .FirstOrDefaultAsync(mi => mi.Id == dto.MenuItemId)
            ?? throw new InvalidOperationException($"MenuItem '{dto.MenuItemId}' not found.");

        var taxRate = menuItem.TaxRate?.Rate ?? defaultTaxRate;
        var unitPrice = menuItem.Price;
        var isComboItem = false;

        if (dto.MenuComboId.HasValue)
        {
            var combo = await _db.MenuCombos.IgnoreQueryFilters()
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.Id == dto.MenuComboId.Value);

            if (combo != null)
            {
                if (menuItem.Id == combo.TriggerMenuItemId)
                    unitPrice = combo.ComboPrice;
                else
                    unitPrice = 0;
                isComboItem = true;
            }
        }

        var lineTotal = Math.Round(unitPrice * dto.Quantity, 2, MidpointRounding.AwayFromZero);
        var taxAmount = Math.Round(lineTotal * taxRate, 2, MidpointRounding.AwayFromZero);

        return new OrderItem
        {
            MenuItemId = dto.MenuItemId,
            MenuComboId = dto.MenuComboId,
            Quantity = dto.Quantity,
            UnitPrice = unitPrice,
            ItemName = menuItem.Name,
            TaxRate = taxRate,
            TaxAmount = taxAmount,
            LineTotal = lineTotal,
            IsComboItem = isComboItem
        };
    }

    private async Task<OrderItem> BuildOrderItemAsync(CreateOrderItemDto dto, decimal defaultTaxRate)
    {
        var menuItem = await _db.MenuItems
            .Include(mi => mi.TaxRate)
            .FirstOrDefaultAsync(mi => mi.Id == dto.MenuItemId)
            ?? throw new InvalidOperationException($"MenuItem '{dto.MenuItemId}' not found.");

        var taxRate = menuItem.TaxRate?.Rate ?? defaultTaxRate;
        var unitPrice = menuItem.Price;
        var isComboItem = false;

        // If this is part of a combo, use combo pricing
        if (dto.MenuComboId.HasValue)
        {
            var combo = await _db.MenuCombos
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.Id == dto.MenuComboId.Value);

            if (combo != null)
            {
                if (menuItem.Id == combo.TriggerMenuItemId)
                {
                    // Trigger item gets the combo price
                    unitPrice = combo.ComboPrice;
                }
                else
                {
                    // Included items are free (part of combo)
                    unitPrice = 0;
                }
                isComboItem = true;
            }
        }

        var lineTotal = Math.Round(unitPrice * dto.Quantity, 2, MidpointRounding.AwayFromZero);
        var taxAmount = Math.Round(lineTotal * taxRate, 2, MidpointRounding.AwayFromZero);

        return new OrderItem
        {
            MenuItemId = dto.MenuItemId,
            MenuComboId = dto.MenuComboId,
            Quantity = dto.Quantity,
            UnitPrice = unitPrice,
            ItemName = menuItem.Name,
            TaxRate = taxRate,
            TaxAmount = taxAmount,
            LineTotal = lineTotal,
            IsComboItem = isComboItem
        };
    }

    private static void EnsureDraft(Order order)
    {
        if (order.Status != OrderStatus.Draft)
            throw new InvalidOperationException($"Order must be in Draft status to modify items. Current status: {order.Status}.");
    }

    private static string GenerateTrackingCode()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var random = new Random();
        return new string(Enumerable.Range(0, 6).Select(_ => chars[random.Next(chars.Length)]).ToArray());
    }

    private static void ValidateStatusTransition(OrderStatus current, OrderStatus target)
    {
        var valid = (current, target) switch
        {
            (OrderStatus.Draft, OrderStatus.Confirmed) => true,
            (OrderStatus.Confirmed, OrderStatus.Preparing) => true,
            (OrderStatus.Preparing, OrderStatus.Ready) => true,
            (OrderStatus.Ready, OrderStatus.Completed) => true,
            // Cancellation from any non-terminal state
            (OrderStatus.Draft, OrderStatus.Cancelled) => true,
            (OrderStatus.Confirmed, OrderStatus.Cancelled) => true,
            (OrderStatus.Preparing, OrderStatus.Cancelled) => true,
            (OrderStatus.Ready, OrderStatus.Cancelled) => true,
            _ => false
        };

        if (!valid)
            throw new InvalidOperationException($"Cannot transition from {current} to {target}.");
    }
}
