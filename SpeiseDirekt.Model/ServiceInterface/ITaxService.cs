using SpeiseDirekt.Model;

namespace SpeiseDirekt.ServiceInterface;

public interface ITaxService
{
    Task<List<TaxRate>> GetAllAsync();
    Task<TaxRate?> GetByIdAsync(Guid id);
    Task<TaxRate?> GetDefaultAsync();
    Task<TaxRate> CreateAsync(string name, decimal rate, bool isDefault = false);
    Task<TaxRate?> UpdateAsync(Guid id, string name, decimal rate);
    Task SetDefaultAsync(Guid taxRateId);
    Task<bool> DeleteAsync(Guid id);
}
