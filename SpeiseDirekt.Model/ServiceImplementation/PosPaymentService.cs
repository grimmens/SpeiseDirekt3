using Microsoft.Extensions.Options;
using SpeiseDirekt.Model;
using SpeiseDirekt.Repository;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation;

/// <summary>
/// Handles POS payment processing (restaurant transactions).
/// Not related to TenantSubscription (app feature billing).
/// </summary>
public class PosPaymentService : IPosPaymentService
{
    private readonly IPosPaymentRepository _paymentRepo;
    private readonly IOrderRepository _orderRepo;
    private readonly IOrderService _orderService;
    private readonly IPosStripeGateway _stripe;
    private readonly PosStripeSettings _settings;

    public PosPaymentService(
        IPosPaymentRepository paymentRepo,
        IOrderRepository orderRepo,
        IOrderService orderService,
        IPosStripeGateway stripe,
        IOptions<PosStripeSettings> settings)
    {
        _paymentRepo = paymentRepo;
        _orderRepo = orderRepo;
        _orderService = orderService;
        _stripe = stripe;
        _settings = settings.Value;
    }

    public async Task<PosPayment> CreateCashPaymentAsync(Guid orderId)
    {
        var order = await _orderRepo.GetByIdAsync(orderId)
            ?? throw new InvalidOperationException($"Order '{orderId}' not found.");

        var payment = new PosPayment
        {
            OrderId = orderId,
            Amount = order.GrandTotal,
            Currency = _settings.Currency.ToUpperInvariant(),
            Status = PosPaymentStatus.Succeeded,
            PaymentMethod = PosPaymentMethod.Cash,
            IdempotencyKey = $"{orderId}:{Guid.NewGuid()}",
            CompletedAt = DateTime.UtcNow
        };

        await _paymentRepo.CreateAsync(payment);

        // Advance order to Confirmed
        if (order.Status == OrderStatus.Draft)
            await _orderService.ConfirmOrderAsync(orderId);

        order.PaymentMethod = PosPaymentMethod.Cash;
        await _orderRepo.UpdateAsync(orderId, _ => { });

        return payment;
    }

    public async Task<(PosPayment Payment, string CheckoutUrl)> CreateStripeCheckoutAsync(
        Guid orderId, string successUrl, string cancelUrl)
    {
        var order = await _orderRepo.GetByIdAsync(orderId)
            ?? throw new InvalidOperationException($"Order '{orderId}' not found.");

        var idempotencyKey = $"{orderId}:{Guid.NewGuid()}";

        var payment = new PosPayment
        {
            OrderId = orderId,
            Amount = order.GrandTotal,
            Currency = _settings.Currency.ToUpperInvariant(),
            Status = PosPaymentStatus.Pending,
            PaymentMethod = PosPaymentMethod.Card,
            IdempotencyKey = idempotencyKey
        };

        await _paymentRepo.CreateAsync(payment);

        var amountInCents = (long)(order.GrandTotal * 100);
        var metadata = new Dictionary<string, string>
        {
            ["orderId"] = orderId.ToString(),
            ["paymentId"] = payment.Id.ToString()
        };

        var (sessionId, url) = await _stripe.CreateCheckoutSessionAsync(
            _settings.Currency,
            amountInCents,
            $"Order {order.OrderNumber}",
            successUrl,
            cancelUrl,
            metadata,
            idempotencyKey);

        await _paymentRepo.UpdateAsync(payment.Id, p => p.StripeSessionId = sessionId);

        order.PaymentMethod = PosPaymentMethod.Card;
        await _orderRepo.UpdateAsync(orderId, _ => { });

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

            // Idempotency: skip if already succeeded
            if (payment.Status == PosPaymentStatus.Succeeded)
                return payment;

            await _paymentRepo.UpdateAsync(payment.Id, p =>
            {
                p.Status = PosPaymentStatus.Succeeded;
                p.StripePaymentIntentId = data.GetValueOrDefault("PaymentIntentId");
                p.CompletedAt = DateTime.UtcNow;
            });

            // Advance order status
            if (payment.Order?.Status == OrderStatus.Draft)
                await _orderService.ConfirmOrderAsync(payment.OrderId);

            return payment;
        }

        if (eventType == "checkout.session.expired")
        {
            var sessionId = data.GetValueOrDefault("SessionId") ?? "";
            var payment = await _paymentRepo.GetByStripeSessionIdAsync(sessionId);

            if (payment != null && payment.Status == PosPaymentStatus.Pending)
            {
                await _paymentRepo.UpdateAsync(payment.Id, p =>
                {
                    p.Status = PosPaymentStatus.Failed;
                    p.FailureReason = "Checkout session expired.";
                });
            }

            return payment!;
        }

        throw new InvalidOperationException($"Unhandled webhook event type: {eventType}");
    }

    public async Task<PosPayment> RefundAsync(Guid paymentId, decimal? amount = null, string? reason = null)
    {
        var payment = await _paymentRepo.GetByIdAsync(paymentId)
            ?? throw new InvalidOperationException($"Payment '{paymentId}' not found.");

        if (payment.Status != PosPaymentStatus.Succeeded)
            throw new InvalidOperationException($"Cannot refund payment with status {payment.Status}.");

        var refundAmount = amount ?? payment.Amount;

        if (payment.PaymentMethod == PosPaymentMethod.Card && !string.IsNullOrEmpty(payment.StripePaymentIntentId))
        {
            var refundAmountInCents = (long)(refundAmount * 100);
            var refundId = await _stripe.CreateRefundAsync(
                payment.StripePaymentIntentId, refundAmountInCents, reason);

            await _paymentRepo.UpdateAsync(paymentId, p =>
            {
                p.StripeRefundId = refundId;
                p.RefundAmount = refundAmount;
                p.RefundReason = reason;
                p.Status = refundAmount >= payment.Amount
                    ? PosPaymentStatus.Refunded
                    : PosPaymentStatus.PartiallyRefunded;
            });
        }
        else
        {
            // Cash refund — just update the record
            await _paymentRepo.UpdateAsync(paymentId, p =>
            {
                p.RefundAmount = refundAmount;
                p.RefundReason = reason;
                p.Status = refundAmount >= payment.Amount
                    ? PosPaymentStatus.Refunded
                    : PosPaymentStatus.PartiallyRefunded;
            });
        }

        // Cancel the order if full refund
        if (refundAmount >= payment.Amount)
            await _orderService.CancelOrderAsync(payment.OrderId, reason ?? "Refunded");

        return (await _paymentRepo.GetByIdAsync(paymentId))!;
    }

    public Task<PosPayment?> GetByOrderIdAsync(Guid orderId) => _paymentRepo.GetByOrderIdAsync(orderId);
    public Task<PosPayment?> GetByIdAsync(Guid paymentId) => _paymentRepo.GetByIdAsync(paymentId);
}
