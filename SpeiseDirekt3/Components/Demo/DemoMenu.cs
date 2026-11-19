using System.Security.Cryptography;
using System.Text;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt3.Components.Guest;

namespace SpeiseDirekt3.Components.Demo;

// Inside the namespace: Components/Menu and Components/MenuItem are namespaces too
using Menu = SpeiseDirekt.Model.Menu;
using MenuItem = SpeiseDirekt.Model.MenuItem;

/// <summary>
/// Builds the live demo's <see cref="GuestMenuModel"/> from <see cref="DemoMenuContent"/> in memory — no database, no
/// tenant, no QR code — so the real guest components render it exactly like a menu (<see cref="DemoMenuPage"/>).
/// Every id is derived from the content (<see cref="Id"/>), so item links, the favourites and the filter of a visitor
/// stay valid across requests and deployments. Pictures are static files (<c>wwwroot/images/demo</c>): their URLs go
/// straight into <c>ImagePath</c>/<c>CoverImagePath</c>/<c>LogoImagePath</c>, which the guest components accept as
/// absolute paths (<see cref="GuestFormat.ImageUrl"/>).
/// </summary>
public static class DemoMenu
{
    /// <summary>A stable id for a piece of the demo (<c>menu:de</c>, <c>de:item:Burrata</c>, …): an MD5-based GUID.</summary>
    public static Guid Id(string key) =>
        new(MD5.HashData(Encoding.UTF8.GetBytes("speisedirekt-demo:" + key)));

    /// <summary>The menu id of a version (the guest root's <c>data-menu-id</c>: favourites and filter are kept per id).</summary>
    public static Guid MenuIdOf(DemoMenuVersion version) => Id($"menu:{MenuLanguages.Of(version.Language).Code}");

    /// <summary>The version served at <paramref name="path"/> (<c>/demo</c>, <c>/demo/en</c>), or null.</summary>
    public static DemoMenuVersion? ForPath(string path) =>
        DemoMenuContent.Versions.FirstOrDefault(v => string.Equals(v.Path, path.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));

    /// <param name="version">The language version to show.</param>
    /// <param name="imageUrl">The URL of a photo below <c>wwwroot</c> (the page passes its fingerprinted asset URL).</param>
    /// <param name="selectedItem"><c>?item=</c>: the item whose sheet the server renders open.</param>
    /// <param name="theme">The design to show (<c>?theme=</c>), default Modern like the landing page's hero.</param>
    public static GuestMenuModel Build(DemoMenuVersion version, Func<string, string> imageUrl, string? selectedItem = null,
        DesignTheme theme = DesignTheme.Modern)
    {
        var code = MenuLanguages.Of(version.Language).Code;
        var menuId = MenuIdOf(version);
        string? Photo(string? stem) => stem is null ? null : imageUrl(DemoMenuContent.ImagePath(stem));

        var menu = new Menu
        {
            Id = menuId,
            Name = version.MenuName,
            Description = version.MenuDescription,
            Theme = theme,
            Language = version.Language
        };

        var allergens = version.Allergens.ToDictionary(
            a => a.Code,
            a => new Allergen { Id = Id($"{code}:allergen:{a.Code}"), Code = a.Code, Name = a.Name, MenuId = menuId });

        var categories = version.Categories.Select((c, index) => new Category
        {
            Id = Id($"{code}:category:{c.Name}"),
            Name = c.Name,
            Description = c.Subtitle,
            SortOrder = index,
            MenuId = menuId,
            MenuItems = new List<MenuItem>()
        }).ToList();
        var categoryByName = categories.ToDictionary(c => c.Name);

        var items = new Dictionary<string, MenuItem>();
        foreach (var (dish, index) in version.Dishes.Select((d, i) => (d, i)))
        {
            var category = categoryByName[dish.Category];
            var itemId = Id($"{code}:item:{dish.Name}");
            var item = new MenuItem
            {
                Id = itemId,
                Name = dish.Name,
                Description = dish.Description,
                Price = dish.Price,
                SortOrder = index,
                CategoryId = category.Id,
                ImagePath = Photo(dish.Photo),
                IsVegan = dish.Vegan,
                IsVegetarian = dish.Vegetarian,
                IsSpicy = dish.Spicy,
                IsHalal = dish.Halal,
                IsAlcoholFree = dish.AlcoholFree,
                IsNew = dish.New,
                IsPopular = dish.Popular
            };
            foreach (var (option, i) in (dish.Variants ?? []).Select((o, i) => (o, i)))
                item.PriceOptions.Add(new MenuItemPriceOption
                {
                    Id = Id($"{code}:variant:{dish.Name}:{i}"), MenuItemId = itemId, Kind = PriceOptionKind.Variant,
                    Name = option.Name, Price = option.Price, SortOrder = i
                });
            foreach (var (option, i) in (dish.Extras ?? []).Select((o, i) => (o, i)))
                item.PriceOptions.Add(new MenuItemPriceOption
                {
                    Id = Id($"{code}:extra:{dish.Name}:{i}"), MenuItemId = itemId, Kind = PriceOptionKind.Extra,
                    Name = option.Name, Price = option.Price, SortOrder = i
                });
            item.ApplyLowestVariantPrice();
            foreach (var allergen in dish.Allergens)
                item.Allergens.Add(allergens[allergen]);
            category.MenuItems!.Add(item);
            items[dish.Name] = item;
        }
        menu.Categories = categories;

        var combo = new MenuCombo
        {
            Id = Id($"{code}:combo:{version.Combo.Name}"),
            Name = version.Combo.Name,
            Description = version.Combo.Description,
            ComboPrice = version.Combo.Price,
            MenuId = menuId,
            TriggerMenuItemId = items[version.Combo.Trigger].Id,
            Items = version.Combo.Includes
                .Select(name => new MenuComboItem { MenuItemId = items[name].Id, MenuItem = items[name] })
                .ToList()
        };

        var profile = new RestaurantProfile
        {
            Id = Id($"{code}:profile"),
            Name = version.Profile.Name,
            Description = version.Profile.Description,
            Address = version.Profile.Address,
            OpeningHours = version.Profile.OpeningHours,
            Email = version.Profile.Email,
            Website = version.Profile.Website,
            CoverImagePath = Photo(DemoMenuContent.CoverPhoto),
            LogoImagePath = Photo(DemoMenuContent.LogoPhoto)
        };

        var guestCategories = categories
            .Where(c => c.MenuItems!.Count > 0)
            .InDisplayOrder()
            .Select(c => new GuestCategory(c, c.MenuItems!.InDisplayOrder().ToList()))
            .ToList();

        var versions = DemoMenuContent.Versions
            .Select(v => new GuestLanguageVersion(MenuIdOf(v), MenuLanguages.Of(v.Language), v.Path, v.Path, IsCurrent: v == version))
            .InLanguageOrder(v => v.Language.Language)
            .ToList();

        MenuItem? selected = null;
        if (Guid.TryParse(selectedItem, out var selectedId))
            selected = guestCategories.SelectMany(c => c.Items).FirstOrDefault(i => i.Id == selectedId);

        return new GuestMenuModel
        {
            Menu = menu,
            Categories = guestCategories,
            Combos = [combo],
            Profile = profile,
            Language = MenuLanguages.Of(version.Language),
            Text = GuestText.For(version.Language),
            Versions = versions,
            BasePath = version.Path,
            PublicPath = version.Path,
            ThemeKey = GuestMenuModel.ThemeKeyOf(theme),
            TrackClicks = false,
            IsDemo = true,
            SelectedItem = selected
        };
    }
}
