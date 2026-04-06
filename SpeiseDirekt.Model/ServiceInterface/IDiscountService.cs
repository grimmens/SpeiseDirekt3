using SpeiseDirekt.Model;

namespace SpeiseDirekt.ServiceInterface;

public interface IDiscountService
{
    Task<List<Discount>> GetAllAsync();
    Task<Discount?> GetByIdAsync(Guid id);
    Task<Discount?> GetByCodeAsync(string code);
    Task<Discount> CreateAsync(Discount discount);
    Task<Discount?> UpdateAsync(Guid id, Action<Discount> updateAction);
    Task<bool> DeleteAsync(Guid id);
    Task<(bool IsValid, string? ErrorMessage)> ValidateDiscountAsync(string code, decimal orderSubTotal);
}
