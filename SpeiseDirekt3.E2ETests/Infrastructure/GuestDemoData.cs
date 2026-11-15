using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SpeiseDirekt.Model;

namespace SpeiseDirekt3.E2ETests.Infrastructure;

/// <summary>
/// A realistic guest menu with dish photos for layout tests and screenshots: four photo categories (grid of
/// cards, one dish without a photo), three text-only categories (drinks, rendered as a list), allergens,
/// dietary labels and a combo. The photos are generated (plate on a table, no external files) and stored as
/// <see cref="Image"/> rows like an upload (<c>MenuItem.ImagePath = "image/{id}"</c>).
/// </summary>
public static class GuestDemoData
{
    public const string MenuName = "Trattoria Lago";
    public const string MenuDescription = "Italienische Küche am See · täglich 11:30–22:00";

    /// <summary>Categories in display order; <c>true</c> = the category has dish photos.</summary>
    public static readonly (string Name, bool Photos)[] Categories =
    {
        ("Antipasti", true), ("Pasta & Risotto", true), ("Pizza", true), ("Dolci", true),
        ("Alkoholfreie Getränke", false), ("Weine", false), ("Kaffee", false)
    };

    /// <summary>The dish in a photo category that has no photo (rendered as a text card in the grid).</summary>
    public const string DishWithoutPhoto = "Risotto ai Funghi";

    public const string ComboName = "Pranzo";

    public sealed record Seeded(Guid MenuId, IReadOnlyDictionary<string, Guid> Items, IReadOnlyDictionary<string, Guid> CategoryIds);

    private sealed record Dish(string Category, string Name, string Description, decimal Price, string[] Codes,
        bool Vegan = false, bool Vegetarian = false, bool Spicy = false, Palette? Photo = null);

    private sealed record Palette(Rgba32 TableA, Rgba32 TableB, Rgba32 Food, Rgba32 Garnish);

    private static Rgba32 C(string hex) => Rgba32.ParseHex(hex);

    private static Palette P(string tableA, string tableB, string food, string garnish) => new(C(tableA), C(tableB), C(food), C(garnish));

    private static readonly Dish[] Dishes =
    {
        new("Antipasti", "Bruschetta Classica", "Geröstetes Landbrot mit Tomaten, Basilikum und Knoblauch", 7.90m, new[] { "A" }, Vegan: true, Photo: P("#e9dccb", "#cdb89c", "#c8452f", "#4e8a3a")),
        new("Antipasti", "Vitello Tonnato", "Zartes Kalbfleisch mit Thunfischcreme und Kapern", 13.50m, new[] { "C", "D" }, Photo: P("#d9d4cc", "#b9b0a3", "#e8dcc0", "#6b7d3a")),
        new("Antipasti", "Burrata", "Cremige Burrata auf Kirschtomaten mit Basilikumöl", 12.90m, new[] { "G" }, Vegetarian: true, Photo: P("#dfe6e9", "#b8c4c9", "#f4f1e8", "#c93a2e")),
        new("Antipasti", "Carpaccio vom Rind", "Hauchdünnes Rinderfilet, Rucola, Parmesan, Zitrone", 14.50m, new[] { "G" }, Photo: P("#e7d9c9", "#c4ad94", "#b4313b", "#7aa13f")),
        new("Pasta & Risotto", "Spaghetti Carbonara", "Guanciale, Eigelb, Pecorino, schwarzer Pfeffer", 14.90m, new[] { "A", "C", "G" }, Photo: P("#efe3d1", "#d1bb9c", "#f0c96a", "#8a6a3a")),
        new("Pasta & Risotto", "Penne all'Arrabbiata", "Scharfe Tomatensauce mit Chili und Petersilie", 12.50m, new[] { "A" }, Vegan: true, Spicy: true, Photo: P("#e5ddd3", "#c6b7a6", "#d2472d", "#3f7d3a")),
        new("Pasta & Risotto", DishWithoutPhoto, "Cremiges Risotto mit Steinpilzen und Parmesan", 16.90m, new[] { "G", "L" }, Vegetarian: true),
        new("Pasta & Risotto", "Tagliatelle al Ragù", "Hausgemachte Bandnudeln mit Rinderragout", 15.40m, new[] { "A", "C", "L" }, Photo: P("#e8e0d5", "#c9bba8", "#9b3b23", "#d9c36a")),
        new("Pizza", "Margherita", "Tomate, Fior di Latte, Basilikum", 10.50m, new[] { "A", "G" }, Vegetarian: true, Photo: P("#d8cfc4", "#a9998a", "#e0a548", "#c53a2a")),
        new("Pizza", "Diavola", "Tomate, Mozzarella, scharfe Salami, Chili", 13.90m, new[] { "A", "G" }, Spicy: true, Photo: P("#dcd2c6", "#ab9b8a", "#d9862f", "#9d1f1f")),
        new("Pizza", "Quattro Formaggi", "Mozzarella, Gorgonzola, Parmesan, Taleggio", 14.50m, new[] { "A", "G" }, Vegetarian: true, Photo: P("#e2dbd2", "#b3a596", "#ecd38c", "#c9a24a")),
        new("Dolci", "Tiramisu", "Mascarpone, Espresso, Savoiardi, Kakao", 7.50m, new[] { "A", "C", "G" }, Vegetarian: true, Photo: P("#e6ddd4", "#c3b3a3", "#7a4b2e", "#e8d7b9")),
        new("Dolci", "Panna Cotta", "Mit Beerenragout und frischer Minze", 6.90m, new[] { "G" }, Vegetarian: true, Photo: P("#e9e4ea", "#c4b9c6", "#f5efe2", "#a3243b")),
        new("Alkoholfreie Getränke", "Mineralwasser 0,5 l", "Prickelnd oder still", 4.20m, Array.Empty<string>(), Vegan: true),
        new("Alkoholfreie Getränke", "Hausgemachte Limonade 0,4 l", "Zitrone-Minze oder Holunder", 5.20m, Array.Empty<string>(), Vegan: true),
        new("Alkoholfreie Getränke", "Apfelsaft gespritzt 0,5 l", "", 4.50m, Array.Empty<string>(), Vegan: true),
        new("Weine", "Grüner Veltliner 1/8 l", "Weingut am Hang, Wachau", 4.80m, new[] { "O" }),
        new("Weine", "Chianti Classico 1/8 l", "Toskana, DOCG", 6.20m, new[] { "O" }),
        new("Weine", "Prosecco 0,1 l", "Extra Dry", 5.90m, new[] { "O" }),
        new("Kaffee", "Espresso", "", 2.90m, Array.Empty<string>(), Vegan: true),
        new("Kaffee", "Cappuccino", "Mit aufgeschäumter Milch", 4.20m, new[] { "G" }, Vegetarian: true)
    };

    private static readonly (string Code, string Name)[] Allergens =
    {
        ("A", "Glutenhaltiges Getreide"), ("C", "Eier"), ("D", "Fisch"), ("G", "Milch / Laktose"),
        ("L", "Sellerie"), ("O", "Sulfite")
    };

    /// <summary>Seeds the demo menu into the tenant <paramref name="ownerId"/> and returns its ids.</summary>
    public static async Task<Seeded> SeedAsync(E2EAppFactory app, Guid ownerId, DesignTheme theme, string name = MenuName)
    {
        var menuId = Guid.NewGuid();
        var items = new Dictionary<string, Guid>();
        var categoryIds = new Dictionary<string, Guid>();

        await app.SeedAsync(ownerId, db =>
        {
            var allergens = Allergens.ToDictionary(a => a.Code, a => new Allergen { Id = Guid.NewGuid(), Code = a.Code, Name = a.Name, MenuId = menuId });
            db.Allergens.AddRange(allergens.Values);

            var categories = Categories.Select((c, i) => new Category { Id = Guid.NewGuid(), Name = c.Name, SortOrder = i, MenuId = menuId }).ToList();
            foreach (var c in categories)
                categoryIds[c.Name] = c.Id;

            db.Menus.Add(new Menu
            {
                Id = menuId, Name = name, Description = MenuDescription, Theme = theme,
                Language = MenuLanguage.German, Categories = categories
            });

            var seed = 1;
            foreach (var (dish, index) in Dishes.Select((d, i) => (d, i)))
            {
                var item = new MenuItem
                {
                    Id = Guid.NewGuid(), Name = dish.Name, Description = dish.Description, Price = dish.Price, SortOrder = index,
                    CategoryId = categoryIds[dish.Category], IsVegan = dish.Vegan, IsVegetarian = dish.Vegetarian, IsSpicy = dish.Spicy
                };
                foreach (var code in dish.Codes)
                    item.Allergens.Add(allergens[code]);
                if (dish.Photo is { } palette)
                {
                    var image = new SpeiseDirekt.Model.Image { Id = Guid.NewGuid(), MimeType = "image/jpeg", Content = Photo(palette, seed++) };
                    db.Images.Add(image);
                    item.ImagePath = $"image/{image.Id}";
                }
                db.MenuItems.Add(item);
                items[dish.Name] = item.Id;
            }

            db.MenuCombos.Add(new MenuCombo
            {
                Id = Guid.NewGuid(), Name = ComboName, Description = "Pizza Margherita, Tiramisu und Mineralwasser", ComboPrice = 19.90m,
                MenuId = menuId, TriggerMenuItemId = items["Margherita"],
                Items = { new MenuComboItem { MenuItemId = items["Tiramisu"] }, new MenuComboItem { MenuItemId = items["Mineralwasser 0,5 l"] } }
            });
            return Task.CompletedTask;
        });

        return new Seeded(menuId, items, categoryIds);
    }

    /// <summary>
    /// A 600 × 750 JPEG that reads as a food photo at card size: a plate with a dish on a table, soft shadow,
    /// a few garnish dots. Deterministic per <paramref name="seed"/>.
    /// </summary>
    private static byte[] Photo(Palette palette, int seed) => Photo(palette.TableA, palette.TableB, palette.Food, palette.Garnish, seed);

    public static byte[] Photo(Rgba32 tableA, Rgba32 tableB, Rgba32 food, Rgba32 garnish, int seed)
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

        using var img = new Image<Rgba32>(w, h);
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

        using var stream = new MemoryStream();
        img.SaveAsJpeg(stream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 82 });
        return stream.ToArray();
    }

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
