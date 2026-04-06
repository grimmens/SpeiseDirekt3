using SpeiseDirekt.Data;
using SpeiseDirekt.Model;
using SpeiseDirekt.Repository;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation;

public class TaxService : ITaxService
{
    private readonly ApplicationDbContext _db;
    private readonly ITaxRateRepository _repo;

    public TaxService(ApplicationDbContext db, ITaxRateRepository repo)
    {
        _db = db;
        _repo = repo;
    }

    public Task<List<TaxRate>> GetAllAsync() => _repo.GetAllAsync();
    public Task<TaxRate?> GetByIdAsync(Guid id) => _repo.GetByIdAsync(id);
    public Task<TaxRate?> GetDefaultAsync() => _repo.GetDefaultAsync();

    public async Task<TaxRate> CreateAsync(string name, decimal rate, bool isDefault = false)
    {
        var taxRate = new TaxRate
        {
            Name = name,
            Rate = rate,
            IsDefault = isDefault
        };

        if (isDefault)
            await ClearDefaultsAsync();

        return await _repo.CreateAsync(taxRate);
    }

    public async Task<TaxRate?> UpdateAsync(Guid id, string name, decimal rate)
    {
        return await _repo.UpdateAsync(id, t =>
        {
            t.Name = name;
            t.Rate = rate;
        });
    }

    public async Task SetDefaultAsync(Guid taxRateId)
    {
        await ClearDefaultsAsync();
        await _repo.UpdateAsync(taxRateId, t => t.IsDefault = true);
    }

    public Task<bool> DeleteAsync(Guid id) => _repo.DeleteAsync(id);

    private async Task ClearDefaultsAsync()
    {
        var all = await _repo.GetAllAsync();
        foreach (var t in all.Where(t => t.IsDefault))
            t.IsDefault = false;
        await _db.SaveChangesAsync();
    }
}
