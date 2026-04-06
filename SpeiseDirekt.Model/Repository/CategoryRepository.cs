using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public class CategoryRepository : ICategoryRepository
{
    private readonly ApplicationDbContext _db;

    public CategoryRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<Category>> GetAllAsync(Guid? menuId = null)
    {
        var query = _db.Categories.AsQueryable();

        if (menuId.HasValue)
            query = query.Where(c => c.MenuId == menuId.Value);

        return await query.ToListAsync();
    }

    public async Task<Category?> GetByIdAsync(Guid id)
    {
        return await _db.Categories
            .Include(c => c.MenuItems!)
            .FirstOrDefaultAsync(c => c.Id == id);
    }

    public async Task<Category> CreateAsync(Category category)
    {
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();
        return category;
    }

    public async Task<Category?> UpdateAsync(Guid id, Action<Category> updateAction)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category is null)
            return null;

        updateAction(category);
        await _db.SaveChangesAsync();
        return category;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var category = await _db.Categories
            .Include(c => c.MenuItems!)
            .FirstOrDefaultAsync(c => c.Id == id);
        if (category is null)
            return false;

        // Remove tracking records for child menu items (configured with NoAction FK)
        if (category.MenuItems?.Count > 0)
        {
            var menuItemIds = category.MenuItems.Select(mi => mi.Id).ToList();
            var clicks = await _db.MenuItemClicks
                .Where(c => menuItemIds.Contains(c.MenuItemId))
                .ToListAsync();
            _db.MenuItemClicks.RemoveRange(clicks);
        }

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MenuExistsAsync(Guid menuId)
    {
        return await _db.Menus.AnyAsync(m => m.Id == menuId);
    }
}
