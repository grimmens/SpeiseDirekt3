using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Infrastructure;

/// <summary>
/// The order in which categories (within a menu) and menu items (within a category) are shown in the
/// back office, on the guest menu and in the API: ascending <c>SortOrder</c>, ties by name. Rows created
/// before the column existed all have 0, so they stay alphabetical until someone reorders them.
/// </summary>
public static class DisplayOrder
{
    public static IOrderedQueryable<Category> InDisplayOrder(this IQueryable<Category> source) =>
        source.OrderBy(c => c.SortOrder).ThenBy(c => c.Name);

    public static IOrderedEnumerable<Category> InDisplayOrder(this IEnumerable<Category> source) =>
        source.OrderBy(c => c.SortOrder).ThenBy(c => c.Name);

    public static IOrderedQueryable<MenuItem> InDisplayOrder(this IQueryable<MenuItem> source) =>
        source.OrderBy(i => i.SortOrder).ThenBy(i => i.Name);

    public static IOrderedEnumerable<MenuItem> InDisplayOrder(this IEnumerable<MenuItem> source) =>
        source.OrderBy(i => i.SortOrder).ThenBy(i => i.Name);

    /// <summary>SortOrder for a new category: after the last category of the menu.</summary>
    public static async Task<int> NextCategorySortOrderAsync(this ApplicationDbContext db, Guid menuId) =>
        (await db.Categories.Where(c => c.MenuId == menuId).MaxAsync(c => (int?)c.SortOrder) ?? -1) + 1;

    /// <summary>SortOrder for a new menu item: after the last item of the category.</summary>
    public static async Task<int> NextMenuItemSortOrderAsync(this ApplicationDbContext db, Guid categoryId) =>
        (await db.MenuItems.Where(i => i.CategoryId == categoryId).MaxAsync(i => (int?)i.SortOrder) ?? -1) + 1;

    /// <summary>
    /// Moves the element at <paramref name="index"/> of <paramref name="siblings"/> (all siblings, in display
    /// order) by <paramref name="offset"/> positions and renumbers the siblings 0..n-1, so rows that all had
    /// the same SortOrder get distinct values. Returns false (and changes nothing) if the move would leave the list.
    /// </summary>
    public static bool Move<T>(IList<T> siblings, int index, int offset, Action<T, int> setSortOrder)
    {
        var target = index + offset;
        if (index < 0 || index >= siblings.Count || target < 0 || target >= siblings.Count || offset == 0)
            return false;

        var moved = siblings[index];
        siblings.RemoveAt(index);
        siblings.Insert(target, moved);
        for (var i = 0; i < siblings.Count; i++)
            setSortOrder(siblings[i], i);
        return true;
    }
}
