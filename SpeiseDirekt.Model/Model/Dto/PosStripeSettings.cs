namespace SpeiseDirekt.Model
{
    /// <summary>
    /// Stripe configuration for POS payments (restaurant transactions).
    /// Not related to TenantSubscription (app feature billing).
    /// </summary>
    public class PosStripeSettings
    {
        public string SecretKey { get; set; } = string.Empty;
        public string PublishableKey { get; set; } = string.Empty;
        public string WebhookSecret { get; set; } = string.Empty;
        public string Currency { get; set; } = "eur";
    }
}
