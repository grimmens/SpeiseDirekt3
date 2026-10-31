using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt3.E2ETests.Infrastructure;

/// <summary>Fixed test data for the public menu pages. Seeded once per factory (= per test class).</summary>
public static class E2ESeedData
{
    public static readonly Guid OwnerId = Guid.Parse("e2e00000-0000-0000-0000-000000000001");
    public static readonly Guid MenuId = Guid.Parse("e2e00000-0000-0000-0000-0000000000a1");
    public static readonly Guid MenuWithoutCombosId = Guid.Parse("e2e00000-0000-0000-0000-0000000000a2");

    public static readonly Guid BurgerId = Guid.Parse("e2e00000-0000-0000-0000-0000000000c1");
    public static readonly Guid FriesId = Guid.Parse("e2e00000-0000-0000-0000-0000000000c2");
    public static readonly Guid ColaId = Guid.Parse("e2e00000-0000-0000-0000-0000000000c3");
    public static readonly Guid SaladId = Guid.Parse("e2e00000-0000-0000-0000-0000000000c4");

    public const string MenuName = "E2E Bistro";
    public const string MenuWithoutCombosName = "E2E Cafe";
    public const string ComboName = "Burger Menü";
    public const string ComboDescription = "Burger mit Pommes und Getränk";

    public static async Task SeedAsync(ApplicationDbContext db)
    {
        db.Users.Add(new ApplicationUser
        {
            Id = OwnerId.ToString(),
            UserName = "owner@e2e.test",
            NormalizedUserName = "OWNER@E2E.TEST",
            Email = "owner@e2e.test",
            NormalizedEmail = "OWNER@E2E.TEST",
            EmailConfirmed = true,
            SecurityStamp = Guid.NewGuid().ToString()
        });

        var mains = new Category { Id = Guid.NewGuid(), Name = "Hauptspeisen", MenuId = MenuId };
        var sides = new Category { Id = Guid.NewGuid(), Name = "Beilagen", MenuId = MenuId };
        var drinks = new Category { Id = Guid.NewGuid(), Name = "Getränke", MenuId = MenuId };

        db.Menus.Add(new Menu
        {
            Id = MenuId,
            Name = MenuName,
            Description = "Menü für E2E-Tests",
            Theme = DesignTheme.Modern,
            Language = MenuLanguage.German,
            Categories = new List<Category> { mains, sides, drinks }
        });

        db.MenuItems.AddRange(
            new MenuItem { Id = BurgerId, Name = "Cheeseburger", Description = "Rind, Cheddar", Price = 9.50m, CategoryId = mains.Id },
            new MenuItem { Id = SaladId, Name = "Gartensalat", Description = "Saisonal", Price = 7.00m, CategoryId = mains.Id },
            new MenuItem { Id = FriesId, Name = "Pommes", Description = "Hausgemacht", Price = 4.00m, CategoryId = sides.Id },
            new MenuItem { Id = ColaId, Name = "Cola", Description = "0,33 l", Price = 3.50m, CategoryId = drinks.Id });

        // Trigger: Cheeseburger; includes Pommes + Cola (7.50 single) for 6.00 → saves 1.50
        db.MenuCombos.Add(new MenuCombo
        {
            Id = Guid.NewGuid(),
            Name = ComboName,
            Description = ComboDescription,
            ComboPrice = 6.00m,
            MenuId = MenuId,
            TriggerMenuItemId = BurgerId,
            Items =
            {
                new MenuComboItem { MenuItemId = FriesId },
                new MenuComboItem { MenuItemId = ColaId }
            }
        });

        var cafeCategory = new Category { Id = Guid.NewGuid(), Name = "Kaffee", MenuId = MenuWithoutCombosId };
        db.Menus.Add(new Menu
        {
            Id = MenuWithoutCombosId,
            Name = MenuWithoutCombosName,
            Description = "Ohne Combos",
            Theme = DesignTheme.Minimal,
            Language = MenuLanguage.German,
            Categories = new List<Category> { cafeCategory }
        });
        db.MenuItems.Add(new MenuItem
        {
            Id = Guid.NewGuid(), Name = "Espresso", Description = "Doppelt", Price = 2.80m, CategoryId = cafeCategory.Id
        });

        await db.SaveChangesAsync();
    }
}
