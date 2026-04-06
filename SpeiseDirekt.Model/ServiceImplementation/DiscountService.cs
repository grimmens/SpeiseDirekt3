using SpeiseDirekt.Model;
using SpeiseDirekt.Repository;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation;

public class DiscountService : IDiscountService
{
    private readonly IDiscountRepository _repo;

    public DiscountService(IDiscountRepository repo)
    {
        _repo = repo;
    }

    public Task<List<Discount>> GetAllAsync() => _repo.GetAllAsync();
    public Task<Discount?> GetByIdAsync(Guid id) => _repo.GetByIdAsync(id);
    public Task<Discount?> GetByCodeAsync(string code) => _repo.GetByCodeAsync(code);
    public Task<Discount> CreateAsync(Discount discount) => _repo.CreateAsync(discount);
    public Task<Discount?> UpdateAsync(Guid id, Action<Discount> updateAction) => _repo.UpdateAsync(id, updateAction);
    public Task<bool> DeleteAsync(Guid id) => _repo.DeleteAsync(id);

    public async Task<(bool IsValid, string? ErrorMessage)> ValidateDiscountAsync(string code, decimal orderSubTotal)
    {
        var discount = await _repo.GetByCodeAsync(code);

        if (discount == null)
            return (false, "Discount code not found.");

        if (!discount.IsActive)
            return (false, "This discount code is no longer active.");

        var now = DateTime.UtcNow;

        if (discount.ValidFrom.HasValue && now < discount.ValidFrom.Value)
            return (false, "This discount code is not yet valid.");

        if (discount.ValidTo.HasValue && now > discount.ValidTo.Value)
            return (false, "This discount code has expired.");

        if (discount.MaxUses.HasValue && discount.CurrentUses >= discount.MaxUses.Value)
            return (false, "This discount code has reached its maximum number of uses.");

        if (discount.MinOrderAmount.HasValue && orderSubTotal < discount.MinOrderAmount.Value)
            return (false, $"Minimum order amount of {discount.MinOrderAmount.Value:C} required.");

        return (true, null);
    }
}
