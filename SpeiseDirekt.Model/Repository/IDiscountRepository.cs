using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public interface IDiscountRepository
{
    Task<List<Discount>> GetAllAsync();
    Task<Discount?> GetByIdAsync(Guid id);
    Task<Discount?> GetByCodeAsync(string code);
    Task<Discount> CreateAsync(Discount discount);
    Task<Discount?> UpdateAsync(Guid id, Action<Discount> updateAction);
    Task<bool> DeleteAsync(Guid id);
}
