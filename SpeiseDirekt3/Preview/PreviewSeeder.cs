using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt3.Preview;

public sealed record PreviewSeedResult(bool UserFound, Guid OwnerId, int MenusAdded, int QrCodesAdded, int MenusTotal, int QrCodesTotal)
{
    public static readonly PreviewSeedResult NotFound = new(false, Guid.Empty, 0, 0, 0, 0);
}

/// <summary>
/// Adds three demo menus (Classic, Modern and Dark theme; categories, items, allergens, one combo) and five
/// QR codes (three direct, one time table, one calendar) to the tenant of one user. Idempotent: a menu or QR
/// code whose name already exists in the tenant is left alone, so the seed can run on every start of the
/// preview app and survives edits the operator makes in between.
/// </summary>
public static class PreviewSeeder
{
    public const string Prefix = "Preview: ";

    public static readonly IReadOnlyList<string> MenuNames = new[]
    {
        Prefix + "Gasthaus Zur Post",
        Prefix + "Mittagskarte",
        Prefix + "Bar & Lounge"
    };

    public static readonly IReadOnlyList<string> QrCodeTitles = new[]
    {
        Prefix + "Tisch 1",
        Prefix + "Mittag",
        Prefix + "Bar",
        Prefix + "Eingang (Zeitplan)",
        Prefix + "Tagesaktion (Kalender)"
    };

    /// <param name="createContext">
    /// Creates a context acting as the given tenant owner (query filters and <c>ApplicationUserId</c> stamping as
    /// in the app). It is called with <see cref="Guid.Empty"/> for the user lookup only.
    /// </param>
    /// <param name="email">User whose tenant is seeded (case-insensitive); unknown → nothing happens.</param>
    public static async Task<PreviewSeedResult> SeedAsync(Func<Guid, ApplicationDbContext> createContext, string email, TimeProvider clock, CancellationToken ct = default)
    {
        Guid ownerId;
        await using (var db = createContext(Guid.Empty))
        {
            var normalized = email.Trim().ToUpperInvariant();
            var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.NormalizedEmail == normalized, ct);
            if (user is null)
                return PreviewSeedResult.NotFound;
            ownerId = Guid.Parse(user.TenantOwnerId ?? user.Id);
        }

        await using var tenant = createContext(ownerId);

        var menusAdded = 0;
        if (!await tenant.Menus.AnyAsync(m => m.Name == MenuNames[0], ct)) { AddGasthaus(tenant); menusAdded++; }
        if (!await tenant.Menus.AnyAsync(m => m.Name == MenuNames[1], ct)) { AddMittagskarte(tenant); menusAdded++; }
        if (!await tenant.Menus.AnyAsync(m => m.Name == MenuNames[2], ct)) { AddBar(tenant); menusAdded++; }
        await tenant.SaveChangesAsync(ct);

        // The QR codes point at the menus by name, whether those were created just now or in an earlier run
        var menuIds = (await tenant.Menus
                .Where(m => MenuNames.Contains(m.Name))
                .Select(m => new { m.Name, m.Id })
                .ToListAsync(ct))
            .GroupBy(m => m.Name)
            .ToDictionary(g => g.Key, g => g.First().Id);
        var gasthaus = menuIds[MenuNames[0]];
        var mittag = menuIds[MenuNames[1]];
        var bar = menuIds[MenuNames[2]];
        var today = DateOnly.FromDateTime(clock.GetLocalNow().Date);

        var qrCodesAdded = 0;
        async Task AddQrCodeAsync(string title, Func<QRCode> create)
        {
            if (await tenant.QRCodes.AnyAsync(q => q.Title == title, ct))
                return;
            tenant.QRCodes.Add(create());
            qrCodesAdded++;
        }

        await AddQrCodeAsync(QrCodeTitles[0], () => new QRCode { Id = Guid.NewGuid(), Title = QrCodeTitles[0], MenuId = gasthaus });
        await AddQrCodeAsync(QrCodeTitles[1], () => new QRCode { Id = Guid.NewGuid(), Title = QrCodeTitles[1], MenuId = mittag });
        await AddQrCodeAsync(QrCodeTitles[2], () => new QRCode { Id = Guid.NewGuid(), Title = QrCodeTitles[2], MenuId = bar });
        await AddQrCodeAsync(QrCodeTitles[3], () => new QRCode
        {
            Id = Guid.NewGuid(), Title = QrCodeTitles[3], IsTimeTableBased = true,
            TimeTableEntries = new List<TimeTableEntry>
            {
                new() { Id = Guid.NewGuid(), StartTime = new TimeOnly(11, 0), EndTime = new TimeOnly(14, 0), MenuId = mittag },
                new() { Id = Guid.NewGuid(), StartTime = new TimeOnly(17, 0), EndTime = new TimeOnly(23, 0), MenuId = gasthaus }
            }
        });
        await AddQrCodeAsync(QrCodeTitles[4], () => new QRCode
        {
            Id = Guid.NewGuid(), Title = QrCodeTitles[4], IsCalendarBased = true, MenuId = gasthaus,
            CalendarEntries = new List<CalendarEntry>
            {
                new() { Id = Guid.NewGuid(), Date = today, MenuId = mittag },
                new() { Id = Guid.NewGuid(), Date = today, RecurringDayOfWeek = DayOfWeek.Friday, EndDate = today.AddMonths(2), MenuId = bar }
            }
        });
        await tenant.SaveChangesAsync(ct);

        return new PreviewSeedResult(true, ownerId, menusAdded, qrCodesAdded,
            await tenant.Menus.CountAsync(ct), await tenant.QRCodes.CountAsync(ct));
    }

    // --- Demo content ------------------------------------------------------------------------------------

    private static Category NewCategory(Guid menuId, string name, int sortOrder) =>
        new() { Id = Guid.NewGuid(), Name = name, MenuId = menuId, SortOrder = sortOrder, MenuItems = new List<MenuItem>() };

    /// <param name="codes">Allergen letter codes of the item, e.g. <c>"ACG"</c>; each must exist in <paramref name="allergens"/>.</param>
    private static MenuItem AddItem(Category category, IReadOnlyDictionary<string, Allergen> allergens, string name, string description, decimal price,
        string codes = "", bool vegan = false, bool vegetarian = false, bool spicy = false)
    {
        var item = new MenuItem
        {
            Id = Guid.NewGuid(), Name = name, Description = description, Price = price, CategoryId = category.Id,
            SortOrder = category.MenuItems!.Count, IsVegan = vegan, IsVegetarian = vegetarian, IsSpicy = spicy
        };
        foreach (var code in codes)
            item.Allergens.Add(allergens[code.ToString()]);
        category.MenuItems.Add(item);
        return item;
    }

    private static Dictionary<string, Allergen> AddAllergens(ApplicationDbContext db, Guid menuId, params (string Code, string Name)[] entries)
    {
        var allergens = entries.ToDictionary(e => e.Code, e => new Allergen { Id = Guid.NewGuid(), Code = e.Code, Name = e.Name, MenuId = menuId });
        db.Allergens.AddRange(allergens.Values);
        return allergens;
    }

    private static void AddGasthaus(ApplicationDbContext db)
    {
        var menuId = Guid.NewGuid();
        var allergens = AddAllergens(db, menuId,
            ("A", "Glutenhaltiges Getreide"), ("C", "Eier"), ("G", "Milch / Laktose"), ("L", "Sellerie"), ("H", "Schalenfrüchte"), ("O", "Sulfite"));

        var vorspeisen = NewCategory(menuId, "Vorspeisen", 0);
        var haupt = NewCategory(menuId, "Hauptspeisen", 1);
        var desserts = NewCategory(menuId, "Desserts", 2);
        var getraenke = NewCategory(menuId, "Getränke", 3);

        AddItem(vorspeisen, allergens, "Frittatensuppe", "Klare Rindsuppe mit Frittaten", 5.20m, "ACL");
        AddItem(vorspeisen, allergens, "Kürbiscremesuppe", "Mit Kernöl und Kürbiskernen", 5.90m, "GL", vegetarian: true);
        AddItem(vorspeisen, allergens, "Bruschetta", "Tomaten, Basilikum, Knoblauch", 7.40m, "A", vegan: true);
        var schnitzel = AddItem(haupt, allergens, "Wiener Schnitzel vom Kalb", "Mit Petersilerdäpfeln und Preiselbeeren", 24.90m, "ACG");
        AddItem(haupt, allergens, "Zwiebelrostbraten", "Mit Bratkartoffeln und Röstzwiebeln", 26.50m, "AO");
        AddItem(haupt, allergens, "Käsespätzle", "Mit Bergkäse und Röstzwiebeln", 15.80m, "ACG", vegetarian: true);
        AddItem(haupt, allergens, "Chili sin Carne", "Bohnen, Mais, Paprika, Reis", 14.20m, vegan: true, spicy: true);
        AddItem(haupt, allergens, "Tagesfisch", "Nach Marktangebot, Preis nach Tagespreis", 0m, "G");
        var strudel = AddItem(desserts, allergens, "Apfelstrudel", "Hausgemacht, mit Vanillesauce", 6.80m, "ACG", vegetarian: true);
        AddItem(desserts, allergens, "Kaiserschmarrn", "Mit Zwetschkenröster, für 2 Personen", 13.90m, "ACG", vegetarian: true);
        var spritzer = AddItem(getraenke, allergens, "Sommerspritzer 0,25 l", "Grüner Veltliner mit Soda", 4.10m, "O", vegan: true);
        AddItem(getraenke, allergens, "Almdudler 0,35 l", "", 3.60m, vegan: true);
        AddItem(getraenke, allergens, "Melange", "Wiener Kaffeehaus-Klassiker", 4.20m, "G", vegetarian: true);

        db.Menus.Add(new Menu
        {
            Id = menuId, Name = MenuNames[0], Description = "Hauptkarte, ganztägig",
            Theme = DesignTheme.Classic, Language = MenuLanguage.German,
            Categories = new List<Category> { vorspeisen, haupt, desserts, getraenke }
        });
        db.MenuCombos.Add(new MenuCombo
        {
            Id = Guid.NewGuid(), Name = "Mittagsmenü", Description = "Schnitzel mit Strudel und Spritzer",
            ComboPrice = 29.90m, MenuId = menuId, TriggerMenuItemId = schnitzel.Id,
            Items = { new MenuComboItem { Id = Guid.NewGuid(), MenuItemId = strudel.Id }, new MenuComboItem { Id = Guid.NewGuid(), MenuItemId = spritzer.Id } }
        });
    }

    private static void AddMittagskarte(ApplicationDbContext db)
    {
        var menuId = Guid.NewGuid();
        var allergens = AddAllergens(db, menuId, ("A", "Glutenhaltiges Getreide"), ("C", "Eier"), ("G", "Milch / Laktose"));
        var teller = NewCategory(menuId, "Mittagsteller", 0);
        AddItem(teller, allergens, "Menü 1: Rindsgulasch", "Mit Semmelknödel", 11.90m, "AC");
        AddItem(teller, allergens, "Menü 2: Gemüselasagne", "Mit Blattsalat", 10.90m, "AG", vegetarian: true);
        AddItem(teller, allergens, "Menü 3: Linsen-Dal", "Mit Basmatireis und Koriander", 10.50m, vegan: true, spicy: true);
        var suppen = NewCategory(menuId, "Dazu", 1);
        AddItem(suppen, allergens, "Tagessuppe", "Wechselt täglich", 3.90m, "A");
        AddItem(suppen, allergens, "Kleiner Salat", "Blattsalat mit Kernöl", 3.50m, vegan: true);

        db.Menus.Add(new Menu
        {
            Id = menuId, Name = MenuNames[1], Description = "Mo bis Fr 11:30 bis 14:00",
            Theme = DesignTheme.Modern, Language = MenuLanguage.German,
            Categories = new List<Category> { teller, suppen }
        });
    }

    private static void AddBar(ApplicationDbContext db)
    {
        var menuId = Guid.NewGuid();
        var allergens = AddAllergens(db, menuId, ("A", "Glutenhaltiges Getreide"), ("G", "Milch / Laktose"), ("H", "Schalenfrüchte"), ("O", "Sulfite"));
        var cocktails = NewCategory(menuId, "Signature Cocktails", 0);
        AddItem(cocktails, allergens, "Negroni Sbagliato", "Campari, Vermouth, Prosecco", 9.50m, "O", vegan: true);
        AddItem(cocktails, allergens, "Espresso Martini", "Vodka, Kaffeelikör, Espresso", 11.00m, vegan: true);
        AddItem(cocktails, allergens, "Whiskey Sour", "Bourbon, Zitrone, Eiweiß", 10.50m);
        AddItem(cocktails, allergens, "Virgin Mojito", "Limette, Minze, Soda", 6.50m, vegan: true);
        var snacks = NewCategory(menuId, "Snacks", 1);
        AddItem(snacks, allergens, "Oliven und Mandeln", "Geröstet, mit Rosmarin", 5.50m, "H", vegan: true);
        AddItem(snacks, allergens, "Käseplatte", "Drei Sorten aus der Region, Feigensenf", 12.90m, "AG", vegetarian: true);
        AddItem(snacks, allergens, "Chili-Nüsse", "Scharf geröstet", 4.50m, "H", vegan: true, spicy: true);

        db.Menus.Add(new Menu
        {
            Id = menuId, Name = MenuNames[2], Description = "Ab 18 Uhr",
            Theme = DesignTheme.Dark, Language = MenuLanguage.German,
            Categories = new List<Category> { cocktails, snacks }
        });
    }
}
