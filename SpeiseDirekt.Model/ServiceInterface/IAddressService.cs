using SpeiseDirekt.Model;

namespace SpeiseDirekt.ServiceInterface;

public interface IAddressService
{
    Task<List<Address>> GetForUserAsync(string userId);
    Task<Address?> GetByIdAsync(Guid id, string userId);
    Task<Address> CreateAsync(string userId, Address address);
    Task<Address?> UpdateAsync(Guid id, string userId, Action<Address> apply);
    Task<bool> DeleteAsync(Guid id, string userId);
    Task<bool> SetDefaultAsync(Guid id, string userId);
}
