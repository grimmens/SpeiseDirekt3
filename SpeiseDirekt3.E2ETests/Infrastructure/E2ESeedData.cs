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

    // --- Ramen menu: transcribed from a real menu card (page "RAMEN") ---

    public static readonly Guid RamenMenuId = Guid.Parse("e2e00000-0000-0000-0000-0000000000a3");
    public const string RamenMenuName = "Ramen";
    public const string RamenCategoryName = "Ramen";

    /// <summary>
    /// Allergen codes of the card (Austrian allergen labelling, letters A–R). Only the codes used on the card.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> RamenAllergens = new Dictionary<string, string>
    {
        ["A"] = "Glutenhaltiges Getreide",
        ["B"] = "Krebstiere",
        ["C"] = "Eier",
        ["D"] = "Fisch",
        ["E"] = "Erdnüsse",
        ["F"] = "Soja",
        ["G"] = "Milch / Laktose",
        ["L"] = "Sellerie",
        ["N"] = "Sesam"
    };

    /// <summary>
    /// A dish of the ramen card. <see cref="Vegan"/>, <see cref="Vegetarian"/> and <see cref="Spicy"/> are the
    /// icons on the card; the app has no fields for them (no EF model change), so they are not stored.
    /// </summary>
    public sealed record RamenDish(
        string Name, string Description, decimal Price, string[] AllergenCodes,
        bool Vegan = false, bool Vegetarian = false, bool Spicy = false);

    public static readonly IReadOnlyList<RamenDish> RamenDishes = new[]
    {
        new RamenDish("Tokyo Ramen",
            "Shoyu Ramen, Chashu Pork, Ei, Menma, Sojasprossen, Jungzwiebeln",
            17.40m, new[] { "A", "B", "C", "D", "F", "N" }),
        new RamenDish("Hokkaido Ramen",
            "Miso Ramen, Chashu Pork, Zuckermais, Ei, Jungzwiebeln, Menma, Chili-Knoblauch-Sardellen-Butter",
            17.40m, new[] { "A", "C", "F", "N", "D", "G" }),
        new RamenDish("Karma Dragon",
            "Chili-Yuzu Ramen mit glasiertem Hühnerkeulenfleisch, Ei, Zuckererbsen, Lotuswurzel, Jungzwiebeln",
            17.40m, new[] { "A", "C", "F", "N" }, Spicy: true),
        new RamenDish("Smoky Men",
            "Rauchiges Miso-Sesam Ramen, Tan Tan Rind, Ei, Pak-Choi, Menma, schwarze Morcheln, Whiskey",
            17.40m, new[] { "A", "C", "E", "F", "N" }),
        new RamenDish("Veggie Dragon",
            "Chili-Yuzu Ramen mit veganer Dashi-Brühe, pikantem Tofu, Zuckererbsen, Lotuswurzel, Ei, Jungzwiebeln",
            17.40m, new[] { "A", "C", "F", "N" }, Vegetarian: true, Spicy: true),
        new RamenDish("Veggie Smoky Men",
            "Rauchiges Miso-Sesam Ramen mit veganer Dashi-Brühe, Tan Tan Pilz, Ei, Pak-Choi, Menma, schwarze Morcheln, Whiskey",
            17.40m, new[] { "A", "C", "E", "F", "N" }, Vegetarian: true),
        new RamenDish("Kinoko Ramen",
            "Shoyu Ramen, Pilzfond, veganes Tan Tan aus Shiitake und Tofu, Shimeji Pilze, Babyspinat, Sojasprossen, Röstzwiebeln",
            17.40m, new[] { "A", "F", "L", "N" }, Vegan: true),
        new RamenDish("Yasai Ramen",
            "Miso Ramen mit veganer Dashi-Brühe, Jungzwiebel, Mais, pikantem Tofu, Sojasprossen, Zuckererbsen, frischem Marktgemüse",
            17.40m, new[] { "A", "F", "L", "N" }, Vegan: true, Spicy: true)
    };

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

        SeedRamenMenu(db);

        await db.SaveChangesAsync();
    }

    private static void SeedRamenMenu(ApplicationDbContext db)
    {
        var category = new Category { Id = Guid.NewGuid(), Name = RamenCategoryName, MenuId = RamenMenuId };
        db.Menus.Add(new Menu
        {
            Id = RamenMenuId,
            Name = RamenMenuName,
            Description = "ラーメン",
            Theme = DesignTheme.Modern,
            Language = MenuLanguage.German,
            Categories = new List<Category> { category }
        });

        var allergens = RamenAllergens.ToDictionary(
            a => a.Key,
            a => new Allergen { Id = Guid.NewGuid(), Code = a.Key, Name = a.Value, MenuId = RamenMenuId });
        db.Allergens.AddRange(allergens.Values);

        foreach (var dish in RamenDishes)
        {
            var item = new MenuItem
            {
                Id = Guid.NewGuid(),
                Name = dish.Name,
                Description = dish.Description,
                Price = dish.Price,
                CategoryId = category.Id
            };
            foreach (var code in dish.AllergenCodes)
                item.Allergens.Add(allergens[code]);
            db.MenuItems.Add(item);
        }
    }
}
