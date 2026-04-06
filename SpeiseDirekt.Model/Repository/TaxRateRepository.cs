using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public class TaxRateRepository : ITaxRateRepository
{
    private readonly ApplicationDbContext _db;

    public TaxRateRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<TaxRate>> GetAllAsync()
    {
        return await _db.TaxRates.OrderBy(t => t.Name).ToListAsync();
    }

    public async Task<TaxRate?> GetByIdAsync(Guid id)
    {
        return await _db.TaxRates.FindAsync(id);
    }

    public async Task<TaxRate?> GetDefaultAsync()
    {
        return await _db.TaxRates.FirstOrDefaultAsync(t => t.IsDefault);
    }

    public async Task<TaxRate> CreateAsync(TaxRate taxRate)
    {
        _db.TaxRates.Add(taxRate);
        await _db.SaveChangesAsync();
        return taxRate;
    }

    public async Task<TaxRate?> UpdateAsync(Guid id, Action<TaxRate> updateAction)
    {
        var taxRate = await _db.TaxRates.FindAsync(id);
        if (taxRate is null)
            return null;

        updateAction(taxRate);
        await _db.SaveChangesAsync();
        return taxRate;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var taxRate = await _db.TaxRates.FindAsync(id);
        if (taxRate is null)
            return false;

        _db.TaxRates.Remove(taxRate);
        await _db.SaveChangesAsync();
        return true;
    }
}
