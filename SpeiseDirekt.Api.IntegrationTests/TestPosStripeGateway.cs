using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Api.IntegrationTests;

/// <summary>
/// Test double for POS Stripe gateway. Returns predictable canned responses.
/// </summary>
public class TestPosStripeGateway : IPosStripeGateway
{
    public const string TestSessionId = "cs_test_session_123";
    public const string TestCheckoutUrl = "https://checkout.stripe.com/test";
    public const string TestPaymentIntentId = "pi_test_intent_456";
    public const string TestRefundId = "re_test_refund_789";

    public Task<(string SessionId, string Url)> CreateCheckoutSessionAsync(
        string currency,
        long amountInCents,
        string orderDescription,
        string successUrl,
        string cancelUrl,
        Dictionary<string, string> metadata,
        string idempotencyKey)
    {
        return Task.FromResult((TestSessionId, TestCheckoutUrl));
    }

    public Task<string> CreateRefundAsync(string paymentIntentId, long amountInCents, string? reason)
    {
        return Task.FromResult(TestRefundId);
    }

    public (string Type, Dictionary<string, string> Data) ConstructWebhookEvent(
        string json, string signature, string secret)
    {
        // Parse the test payload to determine event type
        if (json.Contains("checkout.session.completed"))
        {
            return ("checkout.session.completed", new Dictionary<string, string>
            {
                ["SessionId"] = TestSessionId,
                ["PaymentIntentId"] = TestPaymentIntentId
            });
        }

        if (json.Contains("checkout.session.expired"))
        {
            return ("checkout.session.expired", new Dictionary<string, string>
            {
                ["SessionId"] = TestSessionId
            });
        }

        return ("unknown", new Dictionary<string, string>());
    }
}
