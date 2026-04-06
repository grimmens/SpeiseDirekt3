using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public interface IMenuRepository
{
    Task<List<Menu>> GetAllAsync();
    Task<Menu?> GetByIdAsync(Guid id);
    Task<Menu> CreateAsync(Menu menu);
    Task<Menu?> UpdateAsync(Guid id, Action<Menu> updateAction);
    Task<bool> DeleteAsync(Guid id);
}
