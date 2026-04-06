using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public interface IMenuComboRepository
{
    Task<List<MenuCombo>> GetAllByMenuAsync(Guid menuId);
    Task<MenuCombo?> GetByIdAsync(Guid id);
    Task<MenuCombo> CreateAsync(MenuCombo combo);
    Task<MenuCombo?> UpdateAsync(Guid id, Action<MenuCombo> updateAction);
    Task<bool> DeleteAsync(Guid id);
}
