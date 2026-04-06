using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public class MenuItemRepository : IMenuItemRepository
{
    private readonly ApplicationDbContext _db;

    public MenuItemRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<MenuItem>> GetAllAsync(Guid? categoryId = null)
    {
        var query = _db.MenuItems.AsQueryable();

        if (categoryId.HasValue)
            query = query.Where(mi => mi.CategoryId == categoryId.Value);

        return await query.ToListAsync();
    }

    public async Task<MenuItem?> GetByIdAsync(Guid id)
    {
        return await _db.MenuItems
            .Include(mi => mi.Category)
            .FirstOrDefaultAsync(mi => mi.Id == id);
    }

    public async Task<MenuItem> CreateAsync(MenuItem menuItem)
    {
        _db.MenuItems.Add(menuItem);
        await _db.SaveChangesAsync();
        return menuItem;
    }

    public async Task<MenuItem?> UpdateAsync(Guid id, Action<MenuItem> updateAction)
    {
        var menuItem = await _db.MenuItems.FindAsync(id);
        if (menuItem is null)
            return null;

        updateAction(menuItem);
        await _db.SaveChangesAsync();
        return menuItem;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var menuItem = await _db.MenuItems.FindAsync(id);
        if (menuItem is null)
            return false;

        // Remove tracking records that reference this menu item (configured with NoAction FK)
        var clicks = await _db.MenuItemClicks.Where(c => c.MenuItemId == id).ToListAsync();
        _db.MenuItemClicks.RemoveRange(clicks);

        _db.MenuItems.Remove(menuItem);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> CategoryExistsAsync(Guid categoryId)
    {
        return await _db.Categories.AnyAsync(c => c.Id == categoryId);
    }
}
