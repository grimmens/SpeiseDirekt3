using Microsoft.EntityFrameworkCore;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt3.Components.Demo;

namespace SpeiseDirekt3.E2ETests.Infrastructure;

/// <summary>
/// A realistic guest menu with dish photos for layout tests and screenshots: four photo categories (grid of
/// cards, one dish without a photo), three text-only categories (drinks, rendered as a list), category subtitles,
/// allergens, every item label, price variants on two drinks, extras on a pizza and a combo — plus, when the tenant
/// has none yet, the restaurant profile "Trattoria Lago" with a cover and a logo. The photos are generated (plate
/// on a table, lake view, emblem; no external files) and stored as <see cref="Image"/> rows like an upload
/// (<c>image/{id}</c>).
/// The German content is the app's live demo (<see cref="DemoMenuContent.German"/>, <c>/demo</c>), so the landing page's
/// screenshots and the demo show the same menu; this class adds the photos' palettes and, for the tests, a phone number
/// and social links on the profile (the live demo has none). <see cref="DemoPhotos"/> renders the same pictures for
/// <c>wwwroot/images/demo</c> (<c>LandingScreenshotTour.Capture_demo_images</c>).
/// </summary>
public static class GuestDemoData
{
    public const string MenuName = "Trattoria Lago";
    public static readonly string MenuDescription = DemoMenuContent.German.MenuDescription;

    /// <summary>Name of the restaurant profile (= <see cref="MenuName"/>: the hero shows the name once).</summary>
    public const string RestaurantName = "Trattoria Lago";
    public static readonly string RestaurantDescription = DemoMenuContent.German.Profile.Description;
    public const string RestaurantPhone = "+43 6232 1234";
    public static readonly string RestaurantEmail = DemoMenuContent.German.Profile.Email;
    public static readonly string RestaurantAddress = DemoMenuContent.German.Profile.Address;
    public static readonly string RestaurantOpeningHours = DemoMenuContent.German.Profile.OpeningHours;

    /// <summary>Categories in display order; <c>true</c> = the category has dish photos.</summary>
    public static readonly (string Name, bool Photos)[] Categories = DemoMenuContent.German.Categories
        .Select(c => (c.Name, DemoMenuContent.German.Dishes.Any(d => d.Category == c.Name && d.Photo is not null)))
        .ToArray();

    /// <summary>Category subtitles (<c>Category.Description</c>); categories without an entry have none.</summary>
    public static readonly IReadOnlyDictionary<string, string> Subtitles = DemoMenuContent.German.Categories
        .Where(c => c.Subtitle is not null)
        .ToDictionary(c => c.Name, c => c.Subtitle!);

    /// <summary>Drinks with price variants (the card shows "ab € …").</summary>
    public const string LemonadeName = "Hausgemachte Limonade";
    public const string WineWithVariantsName = "Grüner Veltliner";

    /// <summary>The pizza with extras (also the combo trigger, with a photo).</summary>
    public const string DishWithExtras = "Margherita";

    /// <summary>A dish with four labels (beliebt, vegan, halal, scharf): the card shows two chips and "+2".</summary>
    public const string DishWithManyLabels = "Penne all'Arrabbiata";

    /// <summary>The dish in a photo category that has no photo (rendered as a text card in the grid).</summary>
    public const string DishWithoutPhoto = "Risotto ai Funghi";

    public const string ComboName = "Pranzo";

    public sealed record Seeded(Guid MenuId, IReadOnlyDictionary<string, Guid> Items, IReadOnlyDictionary<string, Guid> CategoryIds);

    private sealed record Palette(Rgba32 TableA, Rgba32 TableB, Rgba32 Food, Rgba32 Garnish);

    private static Rgba32 C(string hex) => Rgba32.ParseHex(hex);

    private static Palette P(string tableA, string tableB, string food, string garnish) => new(C(tableA), C(tableB), C(food), C(garnish));

    /// <summary>The colours of each generated dish photo, by <see cref="DemoDish.Photo"/> (the demo's file stem).</summary>
    private static readonly IReadOnlyDictionary<string, Palette> Palettes = new Dictionary<string, Palette>
    {
        ["bruschetta-classica"] = P("#e9dccb", "#cdb89c", "#c8452f", "#4e8a3a"),
        ["vitello-tonnato"] = P("#d9d4cc", "#b9b0a3", "#e8dcc0", "#6b7d3a"),
        ["burrata"] = P("#dfe6e9", "#b8c4c9", "#f4f1e8", "#c93a2e"),
        ["carpaccio"] = P("#e7d9c9", "#c4ad94", "#b4313b", "#7aa13f"),
        ["spaghetti-carbonara"] = P("#efe3d1", "#d1bb9c", "#f0c96a", "#8a6a3a"),
        ["penne-arrabbiata"] = P("#e5ddd3", "#c6b7a6", "#d2472d", "#3f7d3a"),
        ["tagliatelle-ragu"] = P("#e8e0d5", "#c9bba8", "#9b3b23", "#d9c36a"),
        ["margherita"] = P("#d8cfc4", "#a9998a", "#e0a548", "#c53a2a"),
        ["diavola"] = P("#dcd2c6", "#ab9b8a", "#d9862f", "#9d1f1f"),
        ["quattro-formaggi"] = P("#e2dbd2", "#b3a596", "#ecd38c", "#c9a24a"),
        ["tiramisu"] = P("#e6ddd4", "#c3b3a3", "#7a4b2e", "#e8d7b9"),
        ["panna-cotta"] = P("#e9e4ea", "#c4b9c6", "#f5efe2", "#a3243b")
    };

    /// <summary>The German demo dishes in menu order (<see cref="DemoMenuContent.German"/>).</summary>
    private static IReadOnlyList<DemoDish> Dishes => DemoMenuContent.German.Dishes;

    private static IReadOnlyList<(string Code, string Name)> Allergens => DemoMenuContent.German.Allergens;

    /// <summary>
    /// The seed of every dish photo: 1, 2, 3, … in menu order of the dishes with a photo — the same pictures for the
    /// database seed (<see cref="SeedAsync"/>) and the demo's static files (<see cref="DemoPhotos"/>).
    /// </summary>
    private static IReadOnlyDictionary<string, int> PhotoSeeds => Dishes
        .Where(d => d.Photo is not null)
        .Select((d, i) => (d.Photo!, Seed: i + 1))
        .ToDictionary(p => p.Item1, p => p.Seed);

    /// <summary>
    /// The demo's pictures as images (not encoded): every dish photo by its file stem, the cover and the logo — what
    /// <c>LandingScreenshotTour.Capture_demo_images</c> writes to <c>wwwroot/images/demo/{stem}.webp</c>.
    /// </summary>
    public static IEnumerable<(string Stem, Image<Rgba32> Image)> DemoPhotos()
    {
        foreach (var (stem, seed) in PhotoSeeds)
        {
            var palette = Palettes[stem];
            yield return (stem, PhotoImage(palette.TableA, palette.TableB, palette.Food, palette.Garnish, seed));
        }
        yield return (DemoMenuContent.CoverPhoto, CoverImage());
        yield return (DemoMenuContent.LogoPhoto, LogoImage());
    }

    /// <summary>
    /// Seeds the demo menu into the tenant <paramref name="ownerId"/> and returns its ids. With
    /// <paramref name="withProfile"/> the tenant also gets the restaurant profile "Trattoria Lago" (cover, logo,
    /// contact, links) — unless it already has one (one profile per tenant; e.g. <see cref="E2ESeedData.OwnerId"/>).
    /// </summary>
    public static async Task<Seeded> SeedAsync(E2EAppFactory app, Guid ownerId, DesignTheme theme, string name = MenuName, bool withProfile = true)
    {
        var menuId = Guid.NewGuid();
        var items = new Dictionary<string, Guid>();
        var categoryIds = new Dictionary<string, Guid>();

        await app.SeedAsync(ownerId, async db =>
        {
            if (withProfile && !await db.RestaurantProfiles.IgnoreQueryFilters().AnyAsync(p => p.ApplicationUserId == ownerId))
                db.RestaurantProfiles.Add(Profile(db));

            var allergens = Allergens.ToDictionary(a => a.Code, a => new Allergen { Id = Guid.NewGuid(), Code = a.Code, Name = a.Name, MenuId = menuId });
            db.Allergens.AddRange(allergens.Values);

            var categories = Categories.Select((c, i) => new Category
            {
                Id = Guid.NewGuid(), Name = c.Name, Description = Subtitles.GetValueOrDefault(c.Name), SortOrder = i, MenuId = menuId
            }).ToList();
            foreach (var c in categories)
                categoryIds[c.Name] = c.Id;

            db.Menus.Add(new Menu
            {
                Id = menuId, Name = name, Description = MenuDescription, Theme = theme,
                Language = MenuLanguage.German, Categories = categories
            });

            foreach (var (dish, index) in Dishes.Select((d, i) => (d, i)))
            {
                var item = new MenuItem
                {
                    Id = Guid.NewGuid(), Name = dish.Name, Description = dish.Description, Price = dish.Price, SortOrder = index,
                    CategoryId = categoryIds[dish.Category], IsVegan = dish.Vegan, IsVegetarian = dish.Vegetarian, IsSpicy = dish.Spicy,
                    IsPopular = dish.Popular, IsNew = dish.New, IsHalal = dish.Halal, IsAlcoholFree = dish.AlcoholFree
                };
                foreach (var (option, i) in (dish.Variants ?? []).Select((o, i) => (o, i)))
                    item.PriceOptions.Add(new MenuItemPriceOption { Id = Guid.NewGuid(), Kind = PriceOptionKind.Variant, Name = option.Name, Price = option.Price, SortOrder = i });
                foreach (var (option, i) in (dish.Extras ?? []).Select((o, i) => (o, i)))
                    item.PriceOptions.Add(new MenuItemPriceOption { Id = Guid.NewGuid(), Kind = PriceOptionKind.Extra, Name = option.Name, Price = option.Price, SortOrder = i });
                item.ApplyLowestVariantPrice();
                foreach (var code in dish.Allergens)
                    item.Allergens.Add(allergens[code]);
                if (dish.Photo is { } stem)
                {
                    var palette = Palettes[stem];
                    var image = new SpeiseDirekt.Model.Image { Id = Guid.NewGuid(), MimeType = "image/jpeg", Content = Photo(palette, PhotoSeeds[stem]) };
                    db.Images.Add(image);
                    item.ImagePath = $"image/{image.Id}";
                }
                db.MenuItems.Add(item);
                items[dish.Name] = item.Id;
            }

            var combo = DemoMenuContent.German.Combo;
            db.MenuCombos.Add(new MenuCombo
            {
                Id = Guid.NewGuid(), Name = combo.Name, Description = combo.Description, ComboPrice = combo.Price,
                MenuId = menuId, TriggerMenuItemId = items[combo.Trigger],
                Items = combo.Includes.Select(name => new MenuComboItem { MenuItemId = items[name] }).ToList()
            });
        });

        return new Seeded(menuId, items, categoryIds);
    }

    // --- AP5: an English language version of "Trattoria Lago" -------------------------------------------------------

    /// <summary>Name of the English version (= the restaurant's name, like the German one: the hero shows it once).</summary>
    public const string EnglishMenuName = "Trattoria Lago";
    public const string EnglishMenuDescription = "Italian cooking by the lake · since 1987";
    public const string EnglishLemonadeName = "Homemade lemonade";

    private sealed record EnglishDish(string Category, string Name, string Description, decimal Price, string[] Codes,
        string? PhotoOf = null, bool Vegan = false, bool Vegetarian = false, bool Spicy = false, bool Popular = false,
        bool New = false, bool Halal = false, bool AlcoholFree = false, (string Name, decimal Price)[]? Variants = null);

    /// <summary>English categories (photos come from the German dishes named in <see cref="EnglishDish.PhotoOf"/>).</summary>
    public static readonly string[] EnglishCategories = { "Antipasti", "Pasta & Risotto", "Soft drinks", "Coffee" };

    private static readonly IReadOnlyDictionary<string, string> EnglishSubtitles = new Dictionary<string, string>
    {
        ["Antipasti"] = "To share with the whole table",
        ["Pasta & Risotto"] = "Fresh homemade pasta every day"
    };

    private static readonly EnglishDish[] EnglishDishes =
    {
        new("Antipasti", "Bruschetta Classica", "Toasted country bread with tomatoes, basil and garlic", 7.90m, new[] { "A" }, "Bruschetta Classica", Vegan: true),
        new("Antipasti", "Vitello Tonnato", "Tender veal with tuna cream and capers", 13.50m, new[] { "C", "D" }, "Vitello Tonnato"),
        new("Antipasti", "Burrata", "Creamy burrata on cherry tomatoes with basil oil", 12.90m, new[] { "G" }, "Burrata", Vegetarian: true),
        new("Pasta & Risotto", "Spaghetti Carbonara", "Guanciale, egg yolk, pecorino, black pepper", 14.90m, new[] { "A", "C", "G" }, "Spaghetti Carbonara", Popular: true),
        new("Pasta & Risotto", DishWithManyLabels, "Spicy tomato sauce with chilli and parsley", 12.50m, new[] { "A" }, DishWithManyLabels, Vegan: true, Spicy: true, Popular: true, Halal: true),
        new("Pasta & Risotto", DishWithoutPhoto, "Creamy risotto with porcini and parmesan", 16.90m, new[] { "G", "L" }, Vegetarian: true),
        new("Soft drinks", "Mineral water 0.5 l", "Sparkling or still", 4.20m, Array.Empty<string>(), Vegan: true, AlcoholFree: true),
        new("Soft drinks", EnglishLemonadeName, "Lemon and mint or elderflower", 4.20m, Array.Empty<string>(), Vegan: true, New: true, AlcoholFree: true,
            Variants: new[] { ("0.3 l", 4.20m), ("0.5 l", 5.90m) }),
        new("Coffee", "Espresso", "", 2.90m, Array.Empty<string>(), Vegan: true),
        new("Coffee", "Cappuccino", "With frothed milk", 4.20m, new[] { "G" }, Vegetarian: true)
    };

    private static readonly (string Code, string Name)[] EnglishAllergens =
    {
        ("A", "Cereals containing gluten"), ("C", "Eggs"), ("D", "Fish"), ("G", "Milk"), ("L", "Celery")
    };

    /// <summary>
    /// Seeds an English version of the German demo menu <paramref name="german"/> (four categories, ten dishes translated by
    /// hand, the German dishes' photos, English allergen names, labels and variants on the lemonade) and links both as
    /// language versions (<see cref="Menu.TranslationGroupId"/>). The German menu keeps its ids.
    /// </summary>
    public static async Task<Seeded> SeedEnglishVersionAsync(E2EAppFactory app, Guid ownerId, Seeded german, DesignTheme theme)
    {
        var menuId = Guid.NewGuid();
        var items = new Dictionary<string, Guid>();
        var categoryIds = new Dictionary<string, Guid>();

        await app.SeedAsync(ownerId, async db =>
        {
            var germanMenu = await db.Menus.SingleAsync(m => m.Id == german.MenuId);
            germanMenu.TranslationGroupId ??= Guid.NewGuid();
            var photos = await db.MenuItems
                .Where(i => i.Category!.MenuId == german.MenuId && i.ImagePath != null)
                .ToDictionaryAsync(i => i.Name, i => i.ImagePath);

            var allergens = EnglishAllergens.ToDictionary(a => a.Code, a => new Allergen { Id = Guid.NewGuid(), Code = a.Code, Name = a.Name, MenuId = menuId });
            db.Allergens.AddRange(allergens.Values);

            var categories = EnglishCategories.Select((c, i) => new Category
            {
                Id = Guid.NewGuid(), Name = c, Description = EnglishSubtitles.GetValueOrDefault(c), SortOrder = i, MenuId = menuId
            }).ToList();
            foreach (var c in categories)
                categoryIds[c.Name] = c.Id;

            db.Menus.Add(new Menu
            {
                Id = menuId, Name = EnglishMenuName, Description = EnglishMenuDescription, Theme = theme,
                Language = MenuLanguage.English, TranslationGroupId = germanMenu.TranslationGroupId, Categories = categories
            });

            foreach (var (dish, index) in EnglishDishes.Select((d, i) => (d, i)))
            {
                var item = new MenuItem
                {
                    Id = Guid.NewGuid(), Name = dish.Name, Description = dish.Description, Price = dish.Price, SortOrder = index,
                    CategoryId = categoryIds[dish.Category], IsVegan = dish.Vegan, IsVegetarian = dish.Vegetarian, IsSpicy = dish.Spicy,
                    IsPopular = dish.Popular, IsNew = dish.New, IsHalal = dish.Halal, IsAlcoholFree = dish.AlcoholFree,
                    ImagePath = dish.PhotoOf is { } source ? photos.GetValueOrDefault(source) : null
                };
                foreach (var (option, i) in (dish.Variants ?? []).Select((o, i) => (o, i)))
                    item.PriceOptions.Add(new MenuItemPriceOption { Id = Guid.NewGuid(), Kind = PriceOptionKind.Variant, Name = option.Name, Price = option.Price, SortOrder = i });
                item.ApplyLowestVariantPrice();
                foreach (var code in dish.Codes)
                    item.Allergens.Add(allergens[code]);
                db.MenuItems.Add(item);
                items[dish.Name] = item.Id;
            }
        });

        return new Seeded(menuId, items, categoryIds);
    }

    /// <summary>
    /// A small menu in <paramref name="language"/> (one category "Menu", one dish) linked into the translation group of
    /// <paramref name="groupMenuId"/> — e.g. a French version for the language sheet, or (without a group) an Italian menu.
    /// </summary>
    public static async Task<Guid> SeedSmallMenuAsync(E2EAppFactory app, Guid ownerId, MenuLanguage language, string name,
        string dishName, decimal price, Guid? groupMenuId = null, DesignTheme theme = DesignTheme.Modern)
    {
        var menuId = Guid.NewGuid();
        await app.SeedAsync(ownerId, async db =>
        {
            Guid? groupId = null;
            if (groupMenuId is { } other)
            {
                var otherMenu = await db.Menus.SingleAsync(m => m.Id == other);
                otherMenu.TranslationGroupId ??= Guid.NewGuid();
                groupId = otherMenu.TranslationGroupId;
            }
            var category = new Category { Id = Guid.NewGuid(), Name = "Menu", SortOrder = 0, MenuId = menuId };
            db.Menus.Add(new Menu
            {
                Id = menuId, Name = name, Description = "", Theme = theme, Language = language, TranslationGroupId = groupId,
                Categories = new List<Category> { category }
            });
            db.MenuItems.Add(new MenuItem
            {
                Id = Guid.NewGuid(), Name = dishName, Description = "", Price = price, CategoryId = category.Id, IsVegetarian = true, IsPopular = true
            });
        });
        return menuId;
    }

    /// <summary>The restaurant profile "Trattoria Lago" with a generated cover (lake view) and logo, both stored as <see cref="Image"/> rows.</summary>
    private static RestaurantProfile Profile(SpeiseDirekt.Data.ApplicationDbContext db)
    {
        var cover = new SpeiseDirekt.Model.Image { Id = Guid.NewGuid(), MimeType = "image/jpeg", Content = Cover() };
        var logo = new SpeiseDirekt.Model.Image { Id = Guid.NewGuid(), MimeType = "image/jpeg", Content = Logo() };
        db.Images.AddRange(cover, logo);
        return new RestaurantProfile
        {
            Id = Guid.NewGuid(),
            Name = RestaurantName,
            Description = RestaurantDescription,
            CoverImagePath = $"image/{cover.Id}",
            LogoImagePath = $"image/{logo.Id}",
            Phone = RestaurantPhone,
            Email = RestaurantEmail,
            Website = DemoMenuContent.German.Profile.Website,
            InstagramUrl = "https://instagram.com/trattoria.lago.example",
            FacebookUrl = "https://facebook.com/trattoria.lago.example",
            GoogleReviewUrl = "https://g.page/r/trattoria-lago-example/review",
            Address = RestaurantAddress,
            OpeningHours = RestaurantOpeningHours
        };
    }

    /// <summary>
    /// A 600 × 750 JPEG that reads as a food photo at card size: a plate with a dish on a table, soft shadow,
    /// a few garnish dots. Deterministic per <paramref name="seed"/>.
    /// </summary>
    private static byte[] Photo(Palette palette, int seed) => Photo(palette.TableA, palette.TableB, palette.Food, palette.Garnish, seed);

    public static byte[] Photo(Rgba32 tableA, Rgba32 tableB, Rgba32 food, Rgba32 garnish, int seed)
    {
        using var img = PhotoImage(tableA, tableB, food, garnish, seed);
        using var stream = new MemoryStream();
        img.SaveAsJpeg(stream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 82 });
        return stream.ToArray();
    }

    /// <summary>The dish photo of <see cref="Photo(Rgba32, Rgba32, Rgba32, Rgba32, int)"/> as an image (the caller disposes it).</summary>
    public static Image<Rgba32> PhotoImage(Rgba32 tableA, Rgba32 tableB, Rgba32 food, Rgba32 garnish, int seed)
    {
        const int w = 600, h = 750;
        var rnd = new Random(seed);
        double cx = w * (0.48 + rnd.NextDouble() * 0.04), cy = h * (0.52 + rnd.NextDouble() * 0.04);
        var plateR = w * 0.40;
        var foodR = plateR * (0.52 + rnd.NextDouble() * 0.08);
        var lobes = 4 + rnd.Next(4);
        var phase = rnd.NextDouble() * Math.PI * 2;
        var dots = Enumerable.Range(0, 9 + rnd.Next(6)).Select(_ =>
        {
            var a = rnd.NextDouble() * Math.PI * 2;
            var r = Math.Sqrt(rnd.NextDouble()) * foodR * 0.8;
            return (X: cx + Math.Cos(a) * r, Y: cy + Math.Sin(a) * r, R: 7 + rnd.NextDouble() * 12);
        }).ToArray();
        var plate = new Rgba32(246, 243, 236);

        var img = new Image<Rgba32>(w, h);
        img.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < h; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < w; x++)
                {
                    // Table: diagonal gradient with a little grain
                    var t = (x * 0.6 + y) / (w * 0.6 + h);
                    var grain = (Hash(x, y, seed) - 0.5) * 10 + Math.Sin(y * 0.05 + Math.Sin(x * 0.01) * 3) * 3;
                    var (r, g, b) = Mix(tableA, tableB, t);
                    r += grain; g += grain; b += grain;

                    // Plate shadow (offset down-right, soft edge)
                    var ds = Dist(x, y, cx + 14, cy + 22);
                    var shadow = SmoothStep(plateR + 40, plateR - 10, ds) * 0.28;
                    (r, g, b) = (r * (1 - shadow), g * (1 - shadow), b * (1 - shadow));

                    var d = Dist(x, y, cx, cy);
                    if (d < plateR)
                    {
                        // Plate: rim lit from the top left, a slightly darker well, a thin edge
                        var light = 1 - 0.07 * ((y - cy) + (x - cx) * 0.5) / plateR;
                        var well = d < plateR * 0.74 ? 0.95 - 0.04 * SmoothStep(plateR * 0.6, plateR * 0.74, d) : 1.0;
                        var edge = SmoothStep(plateR - 6, plateR, d) * 0.25;
                        var k = light * well * (1 - edge);
                        (r, g, b) = (plate.R * k, plate.G * k, plate.B * k);

                        // Dish: an irregular blob with texture and a highlight
                        var angle = Math.Atan2(y - cy, x - cx);
                        var wobble = 1 + 0.10 * Math.Sin(angle * lobes + phase) + 0.05 * Math.Sin(angle * (lobes + 3) - phase);
                        var fd = d / (foodR * wobble);
                        if (fd < 1)
                        {
                            var tex = 0.80 + 0.14 * Noise(x / 9.0, y / 9.0, seed + 7) + 0.08 * Noise(x / 3.0, y / 3.0, seed + 11);
                            var hl = 0.18 * SmoothStep(0.9, 0.0, Dist(x, y, cx - foodR * 0.3, cy - foodR * 0.35) / foodR);
                            var rim = SmoothStep(0.85, 1.0, fd) * 0.25;
                            var fk = tex * (1 - rim);
                            (r, g, b) = (food.R * fk + 255 * hl, food.G * fk + 255 * hl, food.B * fk + 255 * hl);
                            foreach (var dot in dots)
                            {
                                var dd = Dist(x, y, dot.X, dot.Y);
                                if (dd < dot.R)
                                {
                                    var gk = 0.8 + 0.2 * (1 - dd / dot.R);
                                    (r, g, b) = (garnish.R * gk, garnish.G * gk, garnish.B * gk);
                                }
                            }
                        }
                    }

                    row[x] = new Rgba32(Clamp(r), Clamp(g), Clamp(b));
                }
            }
        });
        return img;
    }

    /// <summary>
    /// A 1600 × 800 JPEG for the restaurant's cover: a lake at golden hour — sky, sun, two mountain ridges, the
    /// lake with the sun's reflection and a dark shoreline. The interesting part sits in the middle band, which the
    /// guest view keeps when it crops the picture (2:1 on phones, 3:1 from 640 px).
    /// </summary>
    public static byte[] Cover(int seed = 3)
    {
        using var img = CoverImage(seed);
        using var stream = new MemoryStream();
        img.SaveAsJpeg(stream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 80 });
        return stream.ToArray();
    }

    /// <summary>The cover of <see cref="Cover"/> as an image (the caller disposes it).</summary>
    public static Image<Rgba32> CoverImage(int seed = 3)
    {
        const int w = 1600, h = 800;
        const double horizon = h * 0.56;
        var sunX = w * 0.64; var sunY = h * 0.31; const double sunR = 44;
        Rgba32 skyTop = C("#6f9cc4"), skyLow = C("#f6c58f"), farRidge = C("#7d89a6"), nearRidge = C("#3f5368"),
            waterTop = C("#c99877"), waterDeep = C("#2f4f66"), shore = C("#24372d");

        double Ridge(double x, double baseY, double amp, int s) =>
            baseY - amp * (0.55 * Noise(x / 260.0, 0.5, s) + 0.3 * Noise(x / 90.0, 1.5, s + 1) + 0.15 * Noise(x / 30.0, 2.5, s + 2));

        var img = new Image<Rgba32>(w, h);
        img.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < h; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < w; x++)
                {
                    double r, g, b;
                    if (y < horizon)
                    {
                        // Sky: blue on top, warm near the horizon, glow around the sun
                        (r, g, b) = Mix(skyTop, skyLow, Math.Pow(y / horizon, 1.6));
                        var glow = SmoothStep(380, 0, Dist(x, y, sunX, sunY)) * 0.55;
                        (r, g, b) = (r + (255 - r) * glow, g + (236 - g) * glow, b + (200 - b) * glow);
                        if (Dist(x, y, sunX, sunY) < sunR)
                            (r, g, b) = (255, 222, 160);
                        // Ridges: far (hazy) and near (darker), the near one with a tree line at the shore
                        if (y > Ridge(x, horizon - 60, 150, seed))
                            (r, g, b) = Mix(farRidge, skyLow, 0.25 * (1 - (y - (horizon - 210)) / 210.0));
                        if (y > Ridge(x + 4000, horizon - 8, 150, seed + 5))
                            (r, g, b) = Mix(nearRidge, farRidge, 0.18 * Noise(x / 12.0, y / 12.0, seed + 9));
                        if (y > horizon - 10 - 14 * Noise(x / 18.0, 0.3, seed + 4))
                            (r, g, b) = (shore.R, shore.G, shore.B);
                    }
                    else
                    {
                        // Lake: warm reflection under the sun, ripples as horizontal streaks
                        var depth = (y - horizon) / (h - horizon);
                        (r, g, b) = Mix(waterTop, waterDeep, Math.Pow(depth, 0.7));
                        var ripple = Noise(x / 70.0, y / 3.2, seed + 13);
                        var shimmer = SmoothStep(0.62, 0.9, ripple) * SmoothStep(170 + depth * 260, 0, Math.Abs(x - sunX)) * (1 - depth * 0.6);
                        (r, g, b) = (r + (255 - r) * shimmer * 0.8, g + (230 - g) * shimmer * 0.8, b + (190 - b) * shimmer * 0.8);
                        var streak = (ripple - 0.5) * 18 * (1 - depth);
                        (r, g, b) = (r + streak, g + streak, b + streak);
                    }
                    var grain = (Hash(x, y, seed) - 0.5) * 6;
                    row[x] = new Rgba32(Clamp(r + grain), Clamp(g + grain), Clamp(b + grain));
                }
            }
        });
        return img;
    }

    /// <summary>
    /// A 400 × 400 JPEG logo: a round deep-green emblem with a cream ring, a rising sun and three waves (the lake),
    /// on a cream tile — reads well in a rounded square or a circle.
    /// </summary>
    public static byte[] Logo()
    {
        using var img = LogoImage();
        using var stream = new MemoryStream();
        img.SaveAsJpeg(stream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 88 });
        return stream.ToArray();
    }

    /// <summary>The logo of <see cref="Logo"/> as an image (the caller disposes it).</summary>
    public static Image<Rgba32> LogoImage()
    {
        const int size = 400;
        const double c = size / 2.0;
        Rgba32 tile = C("#f6efe3"), badge = C("#1f4e46"), cream = C("#f3e6c9"), sun = C("#e7a83e");

        var img = new Image<Rgba32>(size, size);
        img.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < size; y++)
            {
                var row = rows.GetRowSpan(y);
                for (var x = 0; x < size; x++)
                {
                    var (r, g, b) = ((double)tile.R, (double)tile.G, (double)tile.B);
                    var d = Dist(x + 0.5, y + 0.5, c, c);
                    // Emblem with an anti-aliased edge
                    var inBadge = SmoothStep(171, 169, d);
                    (r, g, b) = Blend((r, g, b), badge, inBadge);
                    // Thin ring inside the edge
                    var ring = SmoothStep(2.6, 1.2, Math.Abs(d - 152)) * inBadge;
                    (r, g, b) = Blend((r, g, b), cream, ring);
                    if (d < 146)
                    {
                        // Sun: half disc on the waterline
                        var waterline = c + 18;
                        var sd = Dist(x + 0.5, y + 0.5, c, waterline);
                        if (y + 0.5 < waterline)
                            (r, g, b) = Blend((r, g, b), sun, SmoothStep(58, 56, sd));
                        // Three waves below it
                        for (var i = 0; i < 3; i++)
                        {
                            var wy = waterline + 20 + i * 24 + Math.Sin((x - c) / 17.0 + i * 1.3) * 5;
                            var half = 84 - i * 18;
                            var onWave = SmoothStep(4.2, 2.6, Math.Abs(y + 0.5 - wy)) * SmoothStep(half + 1, half - 1, Math.Abs(x + 0.5 - c));
                            (r, g, b) = Blend((r, g, b), cream, onWave);
                        }
                    }
                    row[x] = new Rgba32(Clamp(r), Clamp(g), Clamp(b));
                }
            }
        });
        return img;
    }

    private static (double R, double G, double B) Blend((double R, double G, double B) a, Rgba32 b, double t) =>
        (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    private static (double R, double G, double B) Mix(Rgba32 a, Rgba32 b, double t) =>
        (a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

    private static double Dist(double x, double y, double cx, double cy) => Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));

    private static double SmoothStep(double edge0, double edge1, double x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0, 1);
        return t * t * (3 - 2 * t);
    }

    private static double Hash(int x, int y, int seed)
    {
        unchecked
        {
            var n = x * 374761393 + y * 668265263 + seed * 2147483647;
            n = (n ^ (n >> 13)) * 1274126177;
            return ((n ^ (n >> 16)) & 0xffff) / 65535.0;
        }
    }

    /// <summary>Smooth value noise in [0, 1] (bilinear interpolation of hashed grid points).</summary>
    private static double Noise(double x, double y, int seed)
    {
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        var top = Hash(x0, y0, seed) + (Hash(x0 + 1, y0, seed) - Hash(x0, y0, seed)) * fx;
        var bottom = Hash(x0, y0 + 1, seed) + (Hash(x0 + 1, y0 + 1, seed) - Hash(x0, y0 + 1, seed)) * fx;
        return top + (bottom - top) * fy;
    }

    private static byte Clamp(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);
}
