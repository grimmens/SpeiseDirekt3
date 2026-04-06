using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public class PosPaymentRepository : IPosPaymentRepository
{
    private readonly ApplicationDbContext _db;

    public PosPaymentRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<PosPayment?> GetByIdAsync(Guid id)
    {
        return await _db.PosPayments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.Id == id);
    }

    public async Task<PosPayment?> GetByOrderIdAsync(Guid orderId)
    {
        return await _db.PosPayments
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.OrderId == orderId);
    }

    public async Task<PosPayment?> GetByStripeSessionIdAsync(string sessionId)
    {
        return await _db.PosPayments
            .IgnoreQueryFilters()
            .Include(p => p.Order)
            .FirstOrDefaultAsync(p => p.StripeSessionId == sessionId);
    }

    public async Task<PosPayment> CreateAsync(PosPayment payment)
    {
        _db.PosPayments.Add(payment);
        await _db.SaveChangesAsync();
        return payment;
    }

    public async Task<PosPayment?> UpdateAsync(Guid id, Action<PosPayment> updateAction)
    {
        var payment = await _db.PosPayments.FindAsync(id);
        if (payment is null)
            return null;

        updateAction(payment);
        await _db.SaveChangesAsync();
        return payment;
    }
}
