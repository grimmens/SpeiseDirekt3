using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

public class OrdersControllerTests : BaseIntegrationTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public OrdersControllerTests(CustomWebApplicationFactory factory) : base(factory) { }

    private StringContent ToJson(object obj) =>
        new(JsonSerializer.Serialize(obj, JsonOptions), Encoding.UTF8, "application/json");

    private async Task<Order> CreateTestOrder()
    {
        var dto = new CreateOrderDto
        {
            MenuId = TestSeedData.Menu1Id,
            Items = new List<OrderItemInputDto>
            {
                new() { MenuItemId = TestSeedData.MenuItem1Id, Quantity = 2 },
                new() { MenuItemId = TestSeedData.MenuItem2Id, Quantity = 1 }
            },
            Notes = "Test order"
        };
        var response = await Client.PostAsync("/api/orders", ToJson(dto));
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<Order>(JsonOptions))!;
    }

    [Fact]
    public async Task CreateOrder_ValidItems_Returns201WithCorrectTotals()
    {
        var order = await CreateTestOrder();

        order.Should().NotBeNull();
        order.OrderNumber.Should().StartWith("ORD-");
        order.Status.Should().Be(OrderStatus.Draft);
        order.Items.Should().HaveCount(2);
        order.Notes.Should().Be("Test order");

        // Caesar Salad x2 = 17.00, Tomato Soup x1 = 6.00 → SubTotal = 23.00
        order.SubTotal.Should().Be(23.00m);
        // Tax at 20% default: 23.00 * 0.20 = 4.60
        order.TaxAmount.Should().Be(4.60m);
        order.GrandTotal.Should().Be(27.60m);
    }

    [Fact]
    public async Task CreateOrder_EmptyItems_Returns400()
    {
        var dto = new CreateOrderDto
        {
            MenuId = TestSeedData.Menu1Id,
            Items = new List<OrderItemInputDto>()
        };
        var response = await Client.PostAsync("/api/orders", ToJson(dto));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetOrder_ReturnsWithItems()
    {
        var created = await CreateTestOrder();

        var response = await Client.GetAsync($"/api/orders/{created.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await response.Content.ReadFromJsonAsync<Order>(JsonOptions);
        order.Should().NotBeNull();
        order!.Items.Should().HaveCount(2);
        order.Items.Should().Contain(i => i.ItemName == "Caesar Salad" && i.Quantity == 2);
        order.Items.Should().Contain(i => i.ItemName == "Tomato Soup" && i.Quantity == 1);
    }

    [Fact]
    public async Task GetOrder_NonExistent_Returns404()
    {
        var response = await Client.GetAsync($"/api/orders/{Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ConfirmOrder_DraftOrder_Returns200()
    {
        var created = await CreateTestOrder();

        var response = await Client.PostAsync($"/api/orders/{created.Id}/confirm", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await response.Content.ReadFromJsonAsync<Order>(JsonOptions);
        order!.Status.Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public async Task StatusTransition_FullLifecycle()
    {
        var created = await CreateTestOrder();

        // Draft → Confirmed
        var r1 = await Client.PostAsync($"/api/orders/{created.Id}/confirm", null);
        r1.StatusCode.Should().Be(HttpStatusCode.OK);

        // Confirmed → Preparing
        var r2 = await Client.PutAsync($"/api/orders/{created.Id}/status",
            ToJson(new UpdateOrderStatusDto { Status = "Preparing" }));
        r2.StatusCode.Should().Be(HttpStatusCode.OK);

        // Preparing → Ready
        var r3 = await Client.PutAsync($"/api/orders/{created.Id}/status",
            ToJson(new UpdateOrderStatusDto { Status = "Ready" }));
        r3.StatusCode.Should().Be(HttpStatusCode.OK);

        // Ready → Completed
        var r4 = await Client.PutAsync($"/api/orders/{created.Id}/status",
            ToJson(new UpdateOrderStatusDto { Status = "Completed" }));
        r4.StatusCode.Should().Be(HttpStatusCode.OK);

        var final = await r4.Content.ReadFromJsonAsync<Order>(JsonOptions);
        final!.Status.Should().Be(OrderStatus.Completed);
        final.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task StatusTransition_InvalidTransition_Returns400()
    {
        var created = await CreateTestOrder();
        // Confirm first
        await Client.PostAsync($"/api/orders/{created.Id}/confirm", null);
        // Complete first
        await Client.PutAsync($"/api/orders/{created.Id}/status",
            ToJson(new UpdateOrderStatusDto { Status = "Preparing" }));
        await Client.PutAsync($"/api/orders/{created.Id}/status",
            ToJson(new UpdateOrderStatusDto { Status = "Ready" }));
        await Client.PutAsync($"/api/orders/{created.Id}/status",
            ToJson(new UpdateOrderStatusDto { Status = "Completed" }));

        // Completed → Preparing should fail
        var response = await Client.PutAsync($"/api/orders/{created.Id}/status",
            ToJson(new UpdateOrderStatusDto { Status = "Preparing" }));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CancelOrder_SetsStatusAndReason()
    {
        var created = await CreateTestOrder();
        var dto = new CancelOrderDto { Reason = "Customer changed mind" };

        var response = await Client.PostAsync($"/api/orders/{created.Id}/cancel", ToJson(dto));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await response.Content.ReadFromJsonAsync<Order>(JsonOptions);
        order!.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("Customer changed mind");
        order.CancelledAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateItemQuantity_RecalculatesTotals()
    {
        var created = await CreateTestOrder();
        var saladItem = created.Items.First(i => i.ItemName == "Caesar Salad");

        // Change Caesar Salad from 2 to 3
        var response = await Client.PutAsync(
            $"/api/orders/{created.Id}/items/{saladItem.Id}/quantity",
            ToJson(new UpdateQuantityDto { Quantity = 3 }));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await response.Content.ReadFromJsonAsync<Order>(JsonOptions);
        // 8.50 * 3 = 25.50 + 6.00 = 31.50
        order!.SubTotal.Should().Be(31.50m);
        // 31.50 * 0.20 = 6.30
        order.TaxAmount.Should().Be(6.30m);
        order.GrandTotal.Should().Be(37.80m);
    }

    [Fact]
    public async Task ApplyDiscount_ValidCode_ReducesTotal()
    {
        var created = await CreateTestOrder();
        var dto = new ApplyDiscountDto { Code = "SAVE10" };

        var response = await Client.PostAsync($"/api/orders/{created.Id}/discount", ToJson(dto));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await response.Content.ReadFromJsonAsync<Order>(JsonOptions);
        order!.DiscountId.Should().NotBeNull();
        // 10% of SubTotal 23.00 = 2.30
        order.DiscountAmount.Should().Be(2.30m);
        // 23.00 + 4.60 - 2.30 = 25.30
        order.GrandTotal.Should().Be(25.30m);
    }

    [Fact]
    public async Task ApplyDiscount_InvalidCode_Returns400()
    {
        var created = await CreateTestOrder();
        var dto = new ApplyDiscountDto { Code = "BOGUS" };

        var response = await Client.PostAsync($"/api/orders/{created.Id}/discount", ToJson(dto));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RemoveDiscount_RestoresTotal()
    {
        var created = await CreateTestOrder();
        // Apply discount first
        await Client.PostAsync($"/api/orders/{created.Id}/discount",
            ToJson(new ApplyDiscountDto { Code = "SAVE10" }));

        // Remove it
        var response = await Client.DeleteAsync($"/api/orders/{created.Id}/discount");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await response.Content.ReadFromJsonAsync<Order>(JsonOptions);
        order!.DiscountId.Should().BeNull();
        order.DiscountAmount.Should().Be(0);
        order.GrandTotal.Should().Be(27.60m);
    }

    [Fact]
    public async Task AddItem_ToDraftOrder_RecalculatesTotals()
    {
        var created = await CreateTestOrder();
        var dto = new OrderItemInputDto { MenuItemId = TestSeedData.MenuItem2Id, Quantity = 2 };

        var response = await Client.PostAsync($"/api/orders/{created.Id}/items", ToJson(dto));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await response.Content.ReadFromJsonAsync<Order>(JsonOptions);
        order!.Items.Should().HaveCount(3);
        // Original 23.00 + 6.00*2 = 35.00
        order.SubTotal.Should().Be(35.00m);
    }

    [Fact]
    public async Task RemoveItem_FromDraftOrder_RecalculatesTotals()
    {
        var created = await CreateTestOrder();
        var soupItem = created.Items.First(i => i.ItemName == "Tomato Soup");

        var response = await Client.DeleteAsync($"/api/orders/{created.Id}/items/{soupItem.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var order = await response.Content.ReadFromJsonAsync<Order>(JsonOptions);
        order!.Items.Should().HaveCount(1);
        // Only Caesar Salad x2 = 17.00
        order.SubTotal.Should().Be(17.00m);
    }

    [Fact]
    public async Task GetActiveOrders_ReturnsNonTerminalOrders()
    {
        await CreateTestOrder(); // Draft
        var order2 = await CreateTestOrder();
        await Client.PostAsync($"/api/orders/{order2.Id}/confirm", null); // Confirmed

        var response = await Client.GetAsync("/api/orders/active");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var orders = await response.Content.ReadFromJsonAsync<List<Order>>(JsonOptions);
        orders!.Count.Should().BeGreaterThanOrEqualTo(2);
        orders!.All(o => o.Status != OrderStatus.Completed && o.Status != OrderStatus.Cancelled)
            .Should().BeTrue();
    }

    [Fact]
    public async Task ComboOrder_CalculatesComboPrice()
    {
        var dto = new CreateOrderDto
        {
            MenuId = TestSeedData.Menu1Id,
            Items = new List<OrderItemInputDto>
            {
                // Trigger item (Caesar Salad) at combo price
                new() { MenuItemId = TestSeedData.MenuItem1Id, Quantity = 1, MenuComboId = TestSeedData.ComboId },
                // Included item (Tomato Soup) at 0
                new() { MenuItemId = TestSeedData.MenuItem2Id, Quantity = 1, MenuComboId = TestSeedData.ComboId }
            }
        };

        var response = await Client.PostAsync("/api/orders", ToJson(dto));
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var order = await response.Content.ReadFromJsonAsync<Order>(JsonOptions);
        order.Should().NotBeNull();

        var triggerItem = order!.Items.First(i => i.MenuItemId == TestSeedData.MenuItem1Id);
        var includedItem = order.Items.First(i => i.MenuItemId == TestSeedData.MenuItem2Id);

        // Trigger gets combo price 12.00, included gets 0
        triggerItem.UnitPrice.Should().Be(12.00m);
        triggerItem.IsComboItem.Should().BeTrue();
        includedItem.UnitPrice.Should().Be(0m);
        includedItem.IsComboItem.Should().BeTrue();

        // SubTotal = 12.00 + 0 = 12.00
        order.SubTotal.Should().Be(12.00m);
    }
}
