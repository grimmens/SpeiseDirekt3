using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class OrdersController : ControllerBase
{
    private readonly IOrderService _orderService;

    public OrdersController(IOrderService orderService)
    {
        _orderService = orderService;
    }

    [HttpGet]
    [Authorize(Policy = PolicyNames.CanViewOrders)]
    public async Task<ActionResult<List<Order>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null)
    {
        if (status != null && Enum.TryParse<OrderStatus>(status, true, out var parsed))
        {
            var filtered = await _orderService.GetOrderHistoryAsync(page, pageSize);
            return Ok(filtered.Where(o => o.Status == parsed));
        }

        var orders = await _orderService.GetOrderHistoryAsync(page, pageSize);
        return Ok(orders);
    }

    [HttpGet("active")]
    [Authorize(Policy = PolicyNames.CanViewOrders)]
    public async Task<ActionResult<List<Order>>> GetActive()
    {
        var orders = await _orderService.GetActiveOrdersAsync();
        return Ok(orders);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanViewOrders)]
    public async Task<ActionResult<Order>> Get(Guid id)
    {
        var order = await _orderService.GetByIdAsync(id);
        if (order is null)
            return NotFound();
        return Ok(order);
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.CanCreateOrders)]
    public async Task<ActionResult<Order>> Create(CreateOrderDto dto)
    {
        try
        {
            var items = dto.Items.Select(i => new CreateOrderItemDto
            {
                MenuItemId = i.MenuItemId,
                Quantity = i.Quantity,
                MenuComboId = i.MenuComboId
            }).ToList();

            var order = await _orderService.CreateOrderAsync(dto.MenuId, items, dto.Notes);
            return CreatedAtAction(nameof(Get), new { id = order.Id }, order);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id:guid}/items")]
    [Authorize(Policy = PolicyNames.CanEditOrders)]
    public async Task<ActionResult<Order>> AddItem(Guid id, OrderItemInputDto dto)
    {
        try
        {
            var item = new CreateOrderItemDto
            {
                MenuItemId = dto.MenuItemId,
                Quantity = dto.Quantity,
                MenuComboId = dto.MenuComboId
            };
            var order = await _orderService.AddItemAsync(id, item);
            return Ok(order);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id:guid}/items/{itemId:guid}")]
    [Authorize(Policy = PolicyNames.CanEditOrders)]
    public async Task<ActionResult<Order>> RemoveItem(Guid id, Guid itemId)
    {
        try
        {
            var order = await _orderService.RemoveItemAsync(id, itemId);
            return Ok(order);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id:guid}/items/{itemId:guid}/quantity")]
    [Authorize(Policy = PolicyNames.CanEditOrders)]
    public async Task<ActionResult<Order>> UpdateQuantity(Guid id, Guid itemId, UpdateQuantityDto dto)
    {
        try
        {
            var order = await _orderService.UpdateItemQuantityAsync(id, itemId, dto.Quantity);
            return Ok(order);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id:guid}/confirm")]
    [Authorize(Policy = PolicyNames.CanEditOrders)]
    public async Task<ActionResult<Order>> Confirm(Guid id)
    {
        try
        {
            var order = await _orderService.ConfirmOrderAsync(id);
            return Ok(order);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPut("{id:guid}/status")]
    [Authorize(Policy = PolicyNames.CanEditOrders)]
    public async Task<ActionResult<Order>> UpdateStatus(Guid id, UpdateOrderStatusDto dto)
    {
        if (!Enum.TryParse<OrderStatus>(dto.Status, true, out var newStatus))
            return BadRequest($"Invalid status: {dto.Status}");

        try
        {
            var order = await _orderService.UpdateStatusAsync(id, newStatus);
            return Ok(order);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id:guid}/cancel")]
    [Authorize(Policy = PolicyNames.CanEditOrders)]
    public async Task<ActionResult<Order>> Cancel(Guid id, CancelOrderDto? dto = null)
    {
        try
        {
            var order = await _orderService.CancelOrderAsync(id, dto?.Reason);
            return Ok(order);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("{id:guid}/discount")]
    [Authorize(Policy = PolicyNames.CanEditOrders)]
    public async Task<ActionResult<Order>> ApplyDiscount(Guid id, ApplyDiscountDto dto)
    {
        try
        {
            var order = await _orderService.ApplyDiscountAsync(id, dto.Code);
            return Ok(order);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpDelete("{id:guid}/discount")]
    [Authorize(Policy = PolicyNames.CanEditOrders)]
    public async Task<ActionResult<Order>> RemoveDiscount(Guid id)
    {
        try
        {
            var order = await _orderService.RemoveDiscountAsync(id);
            return Ok(order);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}
