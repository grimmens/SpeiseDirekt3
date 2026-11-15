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

    // --- Mezze menu: restaurant profile, category subtitles, labels, price variants and extras ---

    public static readonly Guid MezzeMenuId = Guid.Parse("e2e00000-0000-0000-0000-0000000000a4");
    public const string MezzeMenuName = "E2E Mezze";

    public static readonly Guid HummusId = Guid.Parse("e2e00000-0000-0000-0000-0000000000d1");
    public static readonly Guid FattoushId = Guid.Parse("e2e00000-0000-0000-0000-0000000000d2");
    public static readonly Guid FalafelId = Guid.Parse("e2e00000-0000-0000-0000-0000000000d3");
    public static readonly Guid LemonadeId = Guid.Parse("e2e00000-0000-0000-0000-0000000000d4");
    public static readonly Guid BeerId = Guid.Parse("e2e00000-0000-0000-0000-0000000000d5");

    /// <summary>Restaurant profile of <see cref="OwnerId"/> (shown on all of the owner's menus).</summary>
    public const string RestaurantName = "E2E Restaurant am Markt";
    public const string RestaurantOpeningHours = "Mo–Fr 11:00–22:00\nSa 10:00–23:00\nSo Ruhetag";

    /// <summary>Category names and subtitles of the mezze menu, in display order.</summary>
    public static readonly IReadOnlyList<(string Name, string? Description)> MezzeCategories = new (string, string?)[]
    {
        ("Mezze", "Kalt und warm, zum Teilen"),
        ("Salate", "Bis 22:00 Uhr"),
        ("Getränke", null)
    };

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
    /// icons on the card (stored as <c>MenuItem.IsVegan</c>/<c>IsVegetarian</c>/<c>IsSpicy</c>).
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
        SeedMezzeMenuAndRestaurantProfile(db);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Additive: the owner's restaurant profile and a menu that uses everything of the guest-menu data model
    /// (category subtitles, all labels, a drink and a beer with variants, a salad with extras).
    /// </summary>
    private static void SeedMezzeMenuAndRestaurantProfile(ApplicationDbContext db)
    {
        db.RestaurantProfiles.Add(new RestaurantProfile
        {
            Id = Guid.NewGuid(),
            Name = RestaurantName,
            Description = "Levantinische Küche und Wiener Klassiker, alles frisch zubereitet.",
            Phone = "+43 1 234 56 78",
            Email = "hallo@example.com",
            Website = "https://example.com",
            InstagramUrl = "https://example.com/instagram",
            FacebookUrl = "https://example.com/facebook",
            GoogleReviewUrl = "https://example.com/review",
            Address = "Marktplatz 1\n1010 Wien",
            OpeningHours = RestaurantOpeningHours
        });

        var categories = MezzeCategories
            .Select((c, i) => new Category { Id = Guid.NewGuid(), Name = c.Name, Description = c.Description, MenuId = MezzeMenuId, SortOrder = i })
            .ToList();
        db.Menus.Add(new Menu
        {
            Id = MezzeMenuId,
            Name = MezzeMenuName,
            Description = "Mezze, Salate und Getränke",
            Theme = DesignTheme.Classic,
            Language = MenuLanguage.German,
            Categories = categories
        });
        var (mezze, salads, drinks) = (categories[0], categories[1], categories[2]);

        static MenuItemPriceOption Option(PriceOptionKind kind, string name, decimal price, int sortOrder) =>
            new() { Id = Guid.NewGuid(), Kind = kind, Name = name, Price = price, SortOrder = sortOrder };

        db.MenuItems.AddRange(
            new MenuItem
            {
                Id = HummusId, Name = "Hummus", Description = "Kichererbsen, Tahini, Olivenöl", Price = 7.50m, CategoryId = mezze.Id,
                SortOrder = 0, IsVegan = true, IsVegetarian = true, IsHalal = true, IsPopular = true
            },
            new MenuItem
            {
                Id = FalafelId, Name = "Falafel-Teller", Description = "Mit Tahini-Sauce", Price = 12.50m, CategoryId = mezze.Id,
                SortOrder = 1, IsVegan = true, IsHalal = true, IsSpicy = true, IsNew = true,
                PriceOptions =
                {
                    Option(PriceOptionKind.Variant, "Standard", 12.50m, 0),
                    Option(PriceOptionKind.Variant, "Groß", 15.50m, 1)
                }
            },
            new MenuItem
            {
                Id = FattoushId, Name = "Fattoush", Description = "Brotsalat mit Sumach", Price = 11.90m, CategoryId = salads.Id,
                SortOrder = 0, IsVegan = true, IsHalal = true,
                PriceOptions =
                {
                    Option(PriceOptionKind.Extra, "+ Halloumi", 6.00m, 0),
                    Option(PriceOptionKind.Extra, "+ Hähnchen", 5.00m, 1)
                }
            },
            new MenuItem
            {
                Id = LemonadeId, Name = "Hausgemachte Limonade", Description = "Minze und Limette", Price = 3.90m, CategoryId = drinks.Id,
                SortOrder = 0, IsVegan = true, IsAlcoholFree = true, IsNew = true,
                PriceOptions =
                {
                    Option(PriceOptionKind.Variant, "0,3 l", 3.90m, 0),
                    Option(PriceOptionKind.Variant, "0,5 l", 5.40m, 1)
                }
            },
            new MenuItem
            {
                Id = BeerId, Name = "Hausbier", Description = "", Price = 3.40m, CategoryId = drinks.Id,
                SortOrder = 1, IsPopular = true,
                PriceOptions =
                {
                    Option(PriceOptionKind.Variant, "0,25 l", 3.40m, 0),
                    Option(PriceOptionKind.Variant, "0,5 l", 5.90m, 1)
                }
            });
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
                CategoryId = category.Id,
                IsVegan = dish.Vegan,
                IsVegetarian = dish.Vegetarian,
                IsSpicy = dish.Spicy
            };
            foreach (var code in dish.AllergenCodes)
                item.Allergens.Add(allergens[code]);
            db.MenuItems.Add(item);
        }
    }
}
