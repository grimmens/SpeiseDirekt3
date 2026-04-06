using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public interface IPosPaymentRepository
{
    Task<PosPayment?> GetByIdAsync(Guid id);
    Task<PosPayment?> GetByOrderIdAsync(Guid orderId);
    Task<PosPayment?> GetByStripeSessionIdAsync(string sessionId);
    Task<PosPayment> CreateAsync(PosPayment payment);
    Task<PosPayment?> UpdateAsync(Guid id, Action<PosPayment> updateAction);
}
