using Microsoft.Extensions.Options;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;
using Stripe;
using Stripe.Checkout;

namespace SpeiseDirekt.ServiceImplementation;

/// <summary>
/// Real Stripe SDK wrapper for POS payments.
/// Not related to app subscription billing.
/// </summary>
public class PosStripeGateway : IPosStripeGateway
{
    private readonly PosStripeSettings _settings;

    public PosStripeGateway(IOptions<PosStripeSettings> settings)
    {
        _settings = settings.Value;
        StripeConfiguration.ApiKey = _settings.SecretKey;
    }

    public async Task<(string SessionId, string Url)> CreateCheckoutSessionAsync(
        string currency,
        long amountInCents,
        string orderDescription,
        string successUrl,
        string cancelUrl,
        Dictionary<string, string> metadata,
        string idempotencyKey)
    {
        var options = new SessionCreateOptions
        {
            PaymentMethodTypes = new List<string> { "card" },
            LineItems = new List<SessionLineItemOptions>
            {
                new()
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        Currency = currency,
                        UnitAmount = amountInCents,
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = orderDescription
                        }
                    },
                    Quantity = 1
                }
            },
            Mode = "payment",
            SuccessUrl = successUrl,
            CancelUrl = cancelUrl,
            Metadata = metadata
        };

        var requestOptions = new RequestOptions
        {
            IdempotencyKey = idempotencyKey
        };

        var service = new SessionService();
        var session = await service.CreateAsync(options, requestOptions);
        return (session.Id, session.Url);
    }

    public async Task<string> CreateRefundAsync(string paymentIntentId, long amountInCents, string? reason)
    {
        var options = new RefundCreateOptions
        {
            PaymentIntent = paymentIntentId,
            Amount = amountInCents,
            Reason = reason
        };

        var service = new RefundService();
        var refund = await service.CreateAsync(options);
        return refund.Id;
    }

    public (string Type, Dictionary<string, string> Data) ConstructWebhookEvent(
        string json, string signature, string secret)
    {
        var stripeEvent = EventUtility.ConstructEvent(json, signature, secret);
        var data = new Dictionary<string, string>();

        if (stripeEvent.Data.Object is Session session)
        {
            data["SessionId"] = session.Id;
            data["PaymentIntentId"] = session.PaymentIntentId ?? "";
            if (session.Metadata != null)
            {
                foreach (var kvp in session.Metadata)
                    data[$"meta_{kvp.Key}"] = kvp.Value;
            }
        }

        return (stripeEvent.Type, data);
    }
}
