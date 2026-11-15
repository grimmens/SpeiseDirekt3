using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;

namespace SpeiseDirekt3.Components.Guest;

// Inside the namespace: Components/Menu and Components/MenuItem are namespaces too
using Menu = SpeiseDirekt.Model.Menu;
using MenuItem = SpeiseDirekt.Model.MenuItem;

/// <summary>A category as guests see it: only categories with items, items in display order.</summary>
public sealed record GuestCategory(Category Category, IReadOnlyList<MenuItem> Items)
{
    /// <summary>At least one item has a photo → grid of cards; otherwise (e.g. drinks) a compact list.</summary>
    public bool HasPhotos => Items.Any(i => !string.IsNullOrWhiteSpace(i.ImagePath));

    /// <summary>Id of the section (<c>cat-…</c>), target of the category chips.</summary>
    public string Anchor => GuestFormat.CategoryAnchor(Category.Id);
}

/// <summary>
/// Everything the guest menu view (<see cref="GuestMenuView"/>) renders, loaded once per request by
/// <see cref="GuestMenuLoader"/>. The three guest pages differ only in how they find the menu and in
/// <see cref="TrackClicks"/>/<see cref="IsPreview"/>.
/// </summary>
public sealed class GuestMenuModel
{
    public required Menu Menu { get; init; }

    /// <summary>Categories with at least one item, in display order (<see cref="DisplayOrder"/>).</summary>
    public required IReadOnlyList<GuestCategory> Categories { get; init; }

    /// <summary>Combos of the menu (included items with their <c>MenuItem</c> loaded).</summary>
    public IReadOnlyList<MenuCombo> Combos { get; init; } = Array.Empty<MenuCombo>();

    /// <summary>Path of the current page without query (<c>/menu/{id}</c>, <c>/qr/{id}</c>, <c>/menu/preview/{id}</c>);
    /// item links are <c>{BasePath}?item={id}</c>.</summary>
    public required string BasePath { get; init; }

    /// <summary>Design theme key for <c>data-theme</c> (<c>css/guest-themes.css</c>), e.g. <c>modern</c>.</summary>
    public string ThemeKey { get; init; } = DesignTheme.Modern.ToString().ToLowerInvariant();

    /// <summary>Item clicks are recorded (<c>POST /guest/api/item-click</c>); false in the owner preview.</summary>
    public bool TrackClicks { get; init; } = true;

    /// <summary>Owner preview (<c>/menu/preview/{id}</c>).</summary>
    public bool IsPreview { get; init; }

    /// <summary>Item from <c>?item=</c>: the server renders its sheet open (deep link, no-JS click).</summary>
    public MenuItem? SelectedItem { get; init; }

    public IEnumerable<MenuItem> AllItems => Categories.SelectMany(c => c.Items);

    public IReadOnlyList<MenuCombo> CombosFor(MenuItem item) =>
        Combos.Where(c => c.TriggerMenuItemId == item.Id).ToList();

    public GuestCategory? CategoryOf(MenuItem item) =>
        Categories.FirstOrDefault(c => c.Category.Id == item.CategoryId);

    public string ItemUrl(MenuItem item) => GuestFormat.ItemUrl(BasePath, item.Id);

    public static string ThemeKeyOf(DesignTheme theme) => theme.ToString().ToLowerInvariant();
}

/// <summary>Loads the data of the guest menu view. Public pages ignore the tenant filter, the owner preview keeps it.</summary>
public static class GuestMenuLoader
{
    /// <summary>The menu with categories, items, their allergens and price options (<c>IncludeGuestContent</c>), or null.</summary>
    /// <param name="publicAccess">true: anonymous guest page (<c>IgnoreQueryFilters</c>); false: owner preview (own tenant only).</param>
    public static async Task<Menu?> LoadMenuAsync(ApplicationDbContext db, Guid menuId, bool publicAccess)
    {
        IQueryable<Menu> menus = db.Menus.AsNoTracking();
        if (publicAccess)
            menus = menus.IgnoreQueryFilters();

        return await menus
            .IncludeGuestContent()
            .FirstOrDefaultAsync(m => m.Id == menuId);
    }

    /// <summary>
    /// Builds the view model for a loaded menu (from <see cref="LoadMenuAsync"/> or <c>IMenuService</c>): orders
    /// categories and items, loads the combos and resolves <paramref name="selectedItem"/> (<c>?item=</c>).
    /// </summary>
    public static async Task<GuestMenuModel> BuildAsync(
        ApplicationDbContext db, Menu menu, string basePath, string? selectedItem,
        bool trackClicks = true, bool isPreview = false, DesignTheme? themeOverride = null)
    {
        var categories = (menu.Categories ?? Enumerable.Empty<Category>())
            .Where(c => c.MenuItems?.Any() == true)
            .InDisplayOrder()
            .Select(c => new GuestCategory(c, c.MenuItems!.InDisplayOrder().ToList()))
            .ToList();

        var combos = await db.MenuCombos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(c => c.Items)
                .ThenInclude(i => i.MenuItem)
            .Where(c => c.MenuId == menu.Id)
            .OrderBy(c => c.Name)
            .ToListAsync();

        MenuItem? selected = null;
        if (Guid.TryParse(selectedItem, out var selectedId))
            selected = categories.SelectMany(c => c.Items).FirstOrDefault(i => i.Id == selectedId);

        return new GuestMenuModel
        {
            Menu = menu,
            Categories = categories,
            Combos = combos,
            BasePath = basePath,
            ThemeKey = GuestMenuModel.ThemeKeyOf(themeOverride ?? menu.Theme),
            TrackClicks = trackClicks,
            IsPreview = isPreview,
            SelectedItem = selected
        };
    }
}
