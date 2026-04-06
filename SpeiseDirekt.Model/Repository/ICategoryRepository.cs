using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public interface ICategoryRepository
{
    Task<List<Category>> GetAllAsync(Guid? menuId = null);
    Task<Category?> GetByIdAsync(Guid id);
    Task<Category> CreateAsync(Category category);
    Task<Category?> UpdateAsync(Guid id, Action<Category> updateAction);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> MenuExistsAsync(Guid menuId);
}
