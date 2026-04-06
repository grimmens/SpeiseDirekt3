using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public class DiscountRepository : IDiscountRepository
{
    private readonly ApplicationDbContext _db;

    public DiscountRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<Discount>> GetAllAsync()
    {
        return await _db.Discounts.OrderBy(d => d.Code).ToListAsync();
    }

    public async Task<Discount?> GetByIdAsync(Guid id)
    {
        return await _db.Discounts.FindAsync(id);
    }

    public async Task<Discount?> GetByCodeAsync(string code)
    {
        return await _db.Discounts.FirstOrDefaultAsync(d => d.Code == code);
    }

    public async Task<Discount> CreateAsync(Discount discount)
    {
        _db.Discounts.Add(discount);
        await _db.SaveChangesAsync();
        return discount;
    }

    public async Task<Discount?> UpdateAsync(Guid id, Action<Discount> updateAction)
    {
        var discount = await _db.Discounts.FindAsync(id);
        if (discount is null)
            return null;

        updateAction(discount);
        await _db.SaveChangesAsync();
        return discount;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var discount = await _db.Discounts.FindAsync(id);
        if (discount is null)
            return false;

        _db.Discounts.Remove(discount);
        await _db.SaveChangesAsync();
        return true;
    }
}
