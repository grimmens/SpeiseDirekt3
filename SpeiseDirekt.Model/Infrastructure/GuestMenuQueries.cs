using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Infrastructure;

/// <summary>
/// What a guest page needs of a menu: categories, their items and per item the allergens and the price variants /
/// extras (in display order). Combine with <c>IgnoreQueryFilters()</c> on anonymous paths and
/// <c>AsNoTracking()</c>; the restaurant profile comes from <see cref="ServiceInterface.IRestaurantProfileService"/>.
/// </summary>
public static class GuestMenuQueries
{
    public static IQueryable<Menu> IncludeGuestContent(this IQueryable<Menu> menus) =>
        menus
            .Include(m => m.Categories!)
                .ThenInclude(c => c.MenuItems!)
                    .ThenInclude(i => i.Allergens)
            .Include(m => m.Categories!)
                .ThenInclude(c => c.MenuItems!)
                    .ThenInclude(i => i.PriceOptions.OrderBy(o => o.Kind).ThenBy(o => o.SortOrder).ThenBy(o => o.Name));
}
