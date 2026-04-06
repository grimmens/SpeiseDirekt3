using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;
using SpeiseDirekt.Repository;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation;

/// <summary>
/// Handles POS payment processing (restaurant transactions).
/// Uses IgnoreQueryFilters for anonymous kiosk access.
/// Not related to TenantSubscription (app feature billing).
/// </summary>
public class PosPaymentService : IPosPaymentService
{
    private readonly ApplicationDbContext _db;
    private readonly IPosPaymentRepository _paymentRepo;
    private readonly IPosStripeGateway _stripe;
    private readonly PosStripeSettings _settings;

    public PosPaymentService(
        ApplicationDbContext db,
        IPosPaymentRepository paymentRepo,
        IPosStripeGateway stripe,
        IOptions<PosStripeSettings> settings)
    {
        _db = db;
        _paymentRepo = paymentRepo;
        _stripe = stripe;
        _settings = settings.Value;
    }

    private async Task<Order> GetOrderAsync(Guid orderId)
    {
        return await _db.Orders.IgnoreQueryFilters()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == orderId)
            ?? throw new InvalidOperationException($"Order '{orderId}' not found.");
    }

    public async Task<PosPayment> CreateCashPaymentAsync(Guid orderId)
    {
        var order = await GetOrderAsync(orderId);

        var payment = new PosPayment
        {
            OrderId = orderId,
            Amount = order.GrandTotal,
            Currency = _settings.Currency.ToUpperInvariant(),
            Status = PosPaymentStatus.Succeeded,
            PaymentMethod = PosPaymentMethod.Cash,
            IdempotencyKey = $"{orderId}:{Guid.NewGuid()}",
            CompletedAt = DateTime.UtcNow,
            ApplicationUserId = order.ApplicationUserId
        };

        _db.PosPayments.Add(payment);

        // Advance order to Confirmed
        if (order.Status == OrderStatus.Draft)
        {
            order.Status = OrderStatus.Confirmed;
            order.UpdatedAt = DateTime.UtcNow;
        }
        order.PaymentMethod = PosPaymentMethod.Cash;

        await _db.SaveChangesAsync();
        return payment;
    }

    public async Task<(PosPayment Payment, string CheckoutUrl)> CreateStripeCheckoutAsync(
        Guid orderId, string successUrl, string cancelUrl)
    {
        var order = await GetOrderAsync(orderId);
        var idempotencyKey = $"{orderId}:{Guid.NewGuid()}";

        var payment = new PosPayment
        {
            OrderId = orderId,
            Amount = order.GrandTotal,
            Currency = _settings.Currency.ToUpperInvariant(),
            Status = PosPaymentStatus.Pending,
            PaymentMethod = PosPaymentMethod.Card,
            IdempotencyKey = idempotencyKey,
            ApplicationUserId = order.ApplicationUserId
        };

        _db.PosPayments.Add(payment);
        order.PaymentMethod = PosPaymentMethod.Card;
        await _db.SaveChangesAsync();

        var amountInCents = (long)(order.GrandTotal * 100);
        var metadata = new Dictionary<string, string>
        {
            ["orderId"] = orderId.ToString(),
            ["paymentId"] = payment.Id.ToString()
        };

        var (sessionId, url) = await _stripe.CreateCheckoutSessionAsync(
            _settings.Currency, amountInCents, $"Order {order.OrderNumber}",
            successUrl, cancelUrl, metadata, idempotencyKey);

        payment.StripeSessionId = sessionId;
        await _db.SaveChangesAsync();

        return (payment, url);
    }

    public async Task<PosPayment> HandleWebhookAsync(string json, string stripeSignature)
    {
        var (eventType, data) = _stripe.ConstructWebhookEvent(
            json, stripeSignature, _settings.WebhookSecret);

        if (eventType == "checkout.session.completed")
        {
            var sessionId = data.GetValueOrDefault("SessionId")
                ?? throw new InvalidOperationException("Missing SessionId in webhook.");

            var payment = await _paymentRepo.GetByStripeSessionIdAsync(sessionId)
                ?? throw new InvalidOperationException($"Payment with session '{sessionId}' not found.");

            if (payment.Status == PosPaymentStatus.Succeeded)
                return payment;

            payment.Status = PosPaymentStatus.Succeeded;
            payment.StripePaymentIntentId = data.GetValueOrDefault("PaymentIntentId");
            payment.CompletedAt = DateTime.UtcNow;

            if (payment.Order?.Status == OrderStatus.Draft)
            {
                payment.Order.Status = OrderStatus.Confirmed;
                payment.Order.UpdatedAt = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            return payment;
        }

        if (eventType == "checkout.session.expired")
        {
            var sessionId = data.GetValueOrDefault("SessionId") ?? "";
            var payment = await _paymentRepo.GetByStripeSessionIdAsync(sessionId);

            if (payment != null && payment.Status == PosPaymentStatus.Pending)
            {
                payment.Status = PosPaymentStatus.Failed;
                payment.FailureReason = "Checkout session expired.";
                await _db.SaveChangesAsync();
            }

            return payment!;
        }

        throw new InvalidOperationException($"Unhandled webhook event type: {eventType}");
    }

    public async Task<PosPayment> RefundAsync(Guid paymentId, decimal? amount = null, string? reason = null)
    {
        var payment = await _db.PosPayments.IgnoreQueryFilters()
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.Id == paymentId)
            ?? throw new InvalidOperationException($"Payment '{paymentId}' not found.");

        if (payment.Status != PosPaymentStatus.Succeeded)
            throw new InvalidOperationException($"Cannot refund payment with status {payment.Status}.");

        var refundAmount = amount ?? payment.Amount;

        if (payment.PaymentMethod == PosPaymentMethod.Card && !string.IsNullOrEmpty(payment.StripePaymentIntentId))
        {
            var refundAmountInCents = (long)(refundAmount * 100);
            var refundId = await _stripe.CreateRefundAsync(
                payment.StripePaymentIntentId, refundAmountInCents, reason);
            payment.StripeRefundId = refundId;
        }

        payment.RefundAmount = refundAmount;
        payment.RefundReason = reason;
        payment.Status = refundAmount >= payment.Amount
            ? PosPaymentStatus.Refunded
            : PosPaymentStatus.PartiallyRefunded;

        if (refundAmount >= payment.Amount && payment.Order != null)
        {
            payment.Order.Status = OrderStatus.Cancelled;
            payment.Order.CancelledAt = DateTime.UtcNow;
            payment.Order.CancellationReason = reason ?? "Refunded";
            payment.Order.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
        return payment;
    }

    public async Task<PosPayment?> GetByOrderIdAsync(Guid orderId)
    {
        return await _db.PosPayments.IgnoreQueryFilters()
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.OrderId == orderId);
    }

    public async Task<PosPayment?> GetByIdAsync(Guid paymentId)
    {
        return await _db.PosPayments.IgnoreQueryFilters()
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.Id == paymentId);
    }
}
