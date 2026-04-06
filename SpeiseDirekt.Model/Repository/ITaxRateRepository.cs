using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public interface ITaxRateRepository
{
    Task<List<TaxRate>> GetAllAsync();
    Task<TaxRate?> GetByIdAsync(Guid id);
    Task<TaxRate?> GetDefaultAsync();
    Task<TaxRate> CreateAsync(TaxRate taxRate);
    Task<TaxRate?> UpdateAsync(Guid id, Action<TaxRate> updateAction);
    Task<bool> DeleteAsync(Guid id);
}
