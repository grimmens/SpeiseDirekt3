namespace SpeiseDirekt.ServiceInterface;

/// <summary>
/// Abstraction over Stripe SDK for POS payments.
/// Allows mocking in integration tests. Not related to app subscription billing.
/// </summary>
public interface IPosStripeGateway
{
    Task<(string SessionId, string Url)> CreateCheckoutSessionAsync(
        string currency,
        long amountInCents,
        string orderDescription,
        string successUrl,
        string cancelUrl,
        Dictionary<string, string> metadata,
        string idempotencyKey);

    Task<string> CreateRefundAsync(string paymentIntentId, long amountInCents, string? reason);

    (string Type, Dictionary<string, string> Data) ConstructWebhookEvent(string json, string signature, string secret);
}
