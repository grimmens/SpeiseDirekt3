using SpeiseDirekt.Model;

namespace SpeiseDirekt.ServiceInterface;

/// <summary>
/// Handles POS payment processing (restaurant transactions).
/// Not related to TenantSubscription (app feature billing).
/// </summary>
public interface IPosPaymentService
{
    Task<PosPayment> CreateCashPaymentAsync(Guid orderId);
    Task<(PosPayment Payment, string CheckoutUrl)> CreateStripeCheckoutAsync(
        Guid orderId, string successUrl, string cancelUrl);
    Task<PosPayment> HandleWebhookAsync(string json, string stripeSignature);
    Task<PosPayment> RefundAsync(Guid paymentId, decimal? amount = null, string? reason = null);
    Task<PosPayment?> GetByOrderIdAsync(Guid orderId);
    Task<PosPayment?> GetByIdAsync(Guid paymentId);
}
