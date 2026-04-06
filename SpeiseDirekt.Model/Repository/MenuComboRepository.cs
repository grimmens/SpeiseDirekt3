using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public class MenuComboRepository : IMenuComboRepository
{
    private readonly ApplicationDbContext _db;

    public MenuComboRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<MenuCombo>> GetAllByMenuAsync(Guid menuId)
    {
        return await _db.MenuCombos
            .Include(c => c.TriggerMenuItem)
            .Include(c => c.Items)
                .ThenInclude(i => i.MenuItem)
            .Where(c => c.MenuId == menuId)
            .ToListAsync();
    }

    public async Task<MenuCombo?> GetByIdAsync(Guid id)
    {
        return await _db.MenuCombos
            .Include(c => c.TriggerMenuItem)
            .Include(c => c.Items)
                .ThenInclude(i => i.MenuItem)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<MenuCombo> CreateAsync(MenuCombo combo)
    {
        _db.MenuCombos.Add(combo);
        await _db.SaveChangesAsync();
        return combo;
    }

    public async Task<MenuCombo?> UpdateAsync(Guid id, Action<MenuCombo> updateAction)
    {
        var combo = await _db.MenuCombos
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (combo is null)
            return null;

        updateAction(combo);
        await _db.SaveChangesAsync();
        return combo;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var combo = await _db.MenuCombos
            .Include(c => c.Items)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (combo is null)
            return false;

        _db.MenuCombos.Remove(combo);
        await _db.SaveChangesAsync();
        return true;
    }
}
