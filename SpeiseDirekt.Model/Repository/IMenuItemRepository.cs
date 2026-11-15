using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public interface IMenuItemRepository
{
    Task<List<MenuItem>> GetAllAsync(Guid? categoryId = null);
    Task<MenuItem?> GetByIdAsync(Guid id);
    Task<MenuItem> CreateAsync(MenuItem menuItem);
    Task<MenuItem?> UpdateAsync(Guid id, Action<MenuItem> updateAction);
    Task<bool> DeleteAsync(Guid id);
    Task<bool> CategoryExistsAsync(Guid categoryId);
    /// <summary>SortOrder for a new item of the category (after the last one).</summary>
    Task<int> NextSortOrderAsync(Guid categoryId);
}
