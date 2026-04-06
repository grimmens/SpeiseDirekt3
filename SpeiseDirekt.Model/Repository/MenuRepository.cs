using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public class MenuRepository : IMenuRepository
{
    private readonly ApplicationDbContext _db;

    public MenuRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<Menu>> GetAllAsync()
    {
        return await _db.Menus.ToListAsync();
    }

    public async Task<Menu?> GetByIdAsync(Guid id)
    {
        return await _db.Menus
            .Include(m => m.Categories!)
                .ThenInclude(c => c.MenuItems!)
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<Menu> CreateAsync(Menu menu)
    {
        _db.Menus.Add(menu);
        await _db.SaveChangesAsync();
        return menu;
    }

    public async Task<Menu?> UpdateAsync(Guid id, Action<Menu> updateAction)
    {
        var menu = await _db.Menus.FindAsync(id);
        if (menu is null)
            return null;

        updateAction(menu);
        await _db.SaveChangesAsync();
        return menu;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var menu = await _db.Menus.FindAsync(id);
        if (menu is null)
            return false;

        // Remove tracking records that reference this menu (configured with NoAction FK)
        var menuViews = await _db.MenuViews.Where(v => v.MenuId == id).ToListAsync();
        _db.MenuViews.RemoveRange(menuViews);

        var menuItemClicks = await _db.MenuItemClicks.Where(c => c.MenuId == id).ToListAsync();
        _db.MenuItemClicks.RemoveRange(menuItemClicks);

        // Remove allergens (configured with NoAction FK)
        var allergens = await _db.Allergens.Where(a => a.MenuId == id).ToListAsync();
        _db.Allergens.RemoveRange(allergens);

        _db.Menus.Remove(menu);
        await _db.SaveChangesAsync();
        return true;
    }
}
