using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

public class PosPaymentsControllerTests : BaseIntegrationTest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public PosPaymentsControllerTests(CustomWebApplicationFactory factory) : base(factory) { }

    private StringContent ToJson(object obj) =>
        new(JsonSerializer.Serialize(obj, JsonOptions), Encoding.UTF8, "application/json");

    private async Task<Order> CreateTestOrder()
    {
        var dto = new CreateOrderDto
        {
            MenuId = TestSeedData.Menu1Id,
            Items = new List<OrderItemInputDto>
            {
                new() { MenuItemId = TestSeedData.MenuItem1Id, Quantity = 1 },
                new() { MenuItemId = TestSeedData.MenuItem2Id, Quantity = 1 }
            }
        };
        var response = await Client.PostAsync("/api/orders", ToJson(dto));
        return (await response.Content.ReadFromJsonAsync<Order>(JsonOptions))!;
    }

    [Fact]
    public async Task CreateCashPayment_ValidOrder_ReturnsSucceeded()
    {
        var order = await CreateTestOrder();

        var response = await Client.PostAsync($"/api/pos-payments/cash/{order.Id}", null);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payment = await response.Content.ReadFromJsonAsync<PosPayment>(JsonOptions);
        payment.Should().NotBeNull();
        payment!.Status.Should().Be(PosPaymentStatus.Succeeded);
        payment.PaymentMethod.Should().Be(PosPaymentMethod.Cash);
        payment.Amount.Should().Be(order.GrandTotal);
        payment.CompletedAt.Should().NotBeNull();

        // Verify order was confirmed
        var orderResponse = await Client.GetAsync($"/api/orders/{order.Id}");
        var updatedOrder = await orderResponse.Content.ReadFromJsonAsync<Order>(JsonOptions);
        updatedOrder!.Status.Should().Be(OrderStatus.Confirmed);
    }

    [Fact]
    public async Task CreateCashPayment_NonExistentOrder_Returns400()
    {
        var response = await Client.PostAsync($"/api/pos-payments/cash/{Guid.NewGuid()}", null);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateStripeSession_ValidOrder_ReturnsCheckoutUrl()
    {
        var order = await CreateTestOrder();
        var dto = new CreateStripePaymentDto
        {
            SuccessUrl = "https://example.com/success",
            CancelUrl = "https://example.com/cancel"
        };

        var response = await Client.PostAsync($"/api/pos-payments/stripe/{order.Id}", ToJson(dto));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
        result.GetProperty("checkoutUrl").GetString().Should().Be(TestPosStripeGateway.TestCheckoutUrl);
        result.GetProperty("id").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Webhook_CompletedSession_UpdatesPayment()
    {
        var order = await CreateTestOrder();
        // Create a Stripe session first
        var stripeDto = new CreateStripePaymentDto
        {
            SuccessUrl = "https://example.com/success",
            CancelUrl = "https://example.com/cancel"
        };
        await Client.PostAsync($"/api/pos-payments/stripe/{order.Id}", ToJson(stripeDto));

        // Simulate webhook
        var webhookBody = "{\"type\":\"checkout.session.completed\"}";
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/pos-payments/webhook")
        {
            Content = new StringContent(webhookBody, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Stripe-Signature", "test_signature");

        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify payment is now succeeded
        var paymentResponse = await Client.GetAsync($"/api/pos-payments/order/{order.Id}");
        var payment = await paymentResponse.Content.ReadFromJsonAsync<PosPayment>(JsonOptions);
        payment!.Status.Should().Be(PosPaymentStatus.Succeeded);
        payment.StripePaymentIntentId.Should().Be(TestPosStripeGateway.TestPaymentIntentId);
    }

    [Fact]
    public async Task Webhook_MissingSignature_Returns400()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/pos-payments/webhook")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        // No Stripe-Signature header

        var response = await Client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetByOrder_ReturnsPayment()
    {
        var order = await CreateTestOrder();
        await Client.PostAsync($"/api/pos-payments/cash/{order.Id}", null);

        var response = await Client.GetAsync($"/api/pos-payments/order/{order.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var payment = await response.Content.ReadFromJsonAsync<PosPayment>(JsonOptions);
        payment!.OrderId.Should().Be(order.Id);
    }

    [Fact]
    public async Task GetByOrder_NoPayment_Returns404()
    {
        var order = await CreateTestOrder();
        var response = await Client.GetAsync($"/api/pos-payments/order/{order.Id}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Refund_CashPayment_ReturnsRefunded()
    {
        var order = await CreateTestOrder();
        var payResponse = await Client.PostAsync($"/api/pos-payments/cash/{order.Id}", null);
        var payment = await payResponse.Content.ReadFromJsonAsync<PosPayment>(JsonOptions);

        var refundDto = new RefundPaymentDto { Reason = "Customer request" };
        var response = await Client.PostAsync($"/api/pos-payments/{payment!.Id}/refund", ToJson(refundDto));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var refunded = await response.Content.ReadFromJsonAsync<PosPayment>(JsonOptions);
        refunded!.Status.Should().Be(PosPaymentStatus.Refunded);
        refunded.RefundAmount.Should().Be(payment.Amount);
        refunded.RefundReason.Should().Be("Customer request");
    }

    [Fact]
    public async Task Refund_PendingPayment_Returns400()
    {
        var order = await CreateTestOrder();
        // Create Stripe (pending) payment
        var stripeDto = new CreateStripePaymentDto
        {
            SuccessUrl = "https://example.com/success",
            CancelUrl = "https://example.com/cancel"
        };
        var payResponse = await Client.PostAsync($"/api/pos-payments/stripe/{order.Id}", ToJson(stripeDto));
        var json = await payResponse.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
        var paymentId = result.GetProperty("id").GetString();

        // Try to refund pending payment
        var response = await Client.PostAsync($"/api/pos-payments/{paymentId}/refund", ToJson(new RefundPaymentDto()));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
