using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Not a test in the usual sense: walks a seeded tenant through every back-office page and stores
/// full-page screenshots (desktop 1280 px and phone 390 px) for design reviews. Runs only when
/// <c>E2E_SCREENSHOT_DIR</c> points at a directory; set <c>E2E_ALLOW_EXTERNAL_REQUESTS=1</c> as well so
/// Tailwind and the icon font come from their CDNs and the pages look like production.
/// <code>
/// E2E_SCREENSHOT_DIR=/tmp/shots E2E_ALLOW_EXTERNAL_REQUESTS=1 dotnet test SpeiseDirekt3.E2ETests --filter FullyQualifiedName~AdminScreenshotTour
/// </code>
/// </summary>
public class AdminScreenshotTour : BackOfficeTestBase
{
    public const string DirectoryEnvVar = "E2E_SCREENSHOT_DIR";

    public AdminScreenshotTour(E2EFixture fixture) : base(fixture) { }

    private static string? Directory => Environment.GetEnvironmentVariable(DirectoryEnvVar) is { Length: > 0 } d ? d : null;

    private sealed record Seeded(Guid MenuId, Guid CategoryId, Guid EmptyMenuId, Guid TimeTableQrId, Guid CalendarQrId, Guid TenantUserId);

    [Fact]
    public async Task Capture_every_back_office_page()
    {
        var dir = Directory;
        if (dir is null) return; // opt-in only
        System.IO.Directory.CreateDirectory(dir);

        var email = await RegisterNewOwnerAsync();
        Guid ownerId;
        await using (var db = App.CreateDbContext(Guid.Empty))
            ownerId = Guid.Parse((await db.Users.SingleAsync(u => u.Email == email)).Id);
        var seeded = await SeedTenantAsync(ownerId);

        // Desktop
        await ShootAsync(Page, dir, "desktop", seeded);

        // Phone: same cookies, narrow viewport
        var state = await Context.StorageStateAsync();
        await using var phoneContext = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress,
            StorageState = state,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            DeviceScaleFactor = 2,
            IsMobile = true,
            HasTouch = true
        });
        phoneContext.SetDefaultTimeout(15_000);
        var phone = await phoneContext.NewPageAsync();
        await ShootAsync(phone, dir, "phone", seeded, phoneSubset: true);

        // Empty tenant (first-run experience)
        var (emptyContext, emptyPage) = await NewContextAsync();
        await using (emptyContext)
        {
            await RegisterAsync(emptyPage, UniqueEmail("empty"));
            foreach (var (name, url) in new[] { ("menus", "/menus"), ("qrcodes", "/qrcodes"), ("analytics", "/analytics"), ("users", "/users"), ("home", "/") })
            {
                await GotoInteractiveAsync(emptyPage, url);
                await SettleAsync(emptyPage);
                await emptyPage.ScreenshotAsync(new() { Path = Path.Combine(dir, $"desktop-empty-{name}.png"), FullPage = true });
            }
        }
    }

    private async Task ShootAsync(IPage page, string dir, string prefix, Seeded s, bool phoneSubset = false)
    {
        async Task Shot(string name, string url, Func<IPage, Task>? after = null)
        {
            await GotoInteractiveAsync(page, url);
            if (after is not null) await after(page);
            await SettleAsync(page);
            await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{prefix}-{name}.png"), FullPage = true });
        }

        await Shot("home", "/");
        await Shot("menus", "/menus");
        await Shot("menudetail", $"/menudetail/{s.MenuId}");
        await Shot("categories", $"/Categories/{s.MenuId}");
        await Shot("menuitems", $"/MenuItems/{s.MenuId}", async p =>
            await Assertions.Expect(p).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/MenuItems/Category/", System.Text.RegularExpressions.RegexOptions.IgnoreCase)));
        await Shot("qrcodes", "/qrcodes");
        await Shot("qrcode-edit-timetable", $"/qrcode/edit/{s.TimeTableQrId}");
        await Shot("analytics", "/analytics");

        if (phoneSubset)
        {
            // Open the hamburger sidebar once
            await GotoInteractiveAsync(page, "/menus");
            await page.Locator("button:has(span.sr-only:text('Open sidebar'))").ClickAsync();
            await SettleAsync(page);
            await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{prefix}-sidebar-open.png"), FullPage = false });
            return;
        }

        await Shot("menuitems-editor", $"/MenuItems/{s.MenuId}", async p =>
        {
            await Assertions.Expect(p).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/MenuItems/Category/", System.Text.RegularExpressions.RegexOptions.IgnoreCase));
            await p.GetByRole(AriaRole.Button, new() { Name = "New Menu Entry" }).ClickAsync();
            await Assertions.Expect(p.Locator("#menuItemEditModal")).ToBeVisibleAsync();
        });
        await Shot("menu-edit", $"/menu/edit/{s.MenuId}");
        await Shot("menu-new", "/menu/edit");
        await Shot("menudetail-empty-menu", $"/menudetail/{s.EmptyMenuId}");
        await Shot("categories-empty-menu", $"/Categories/{s.EmptyMenuId}");
        await Shot("menuitems-empty-menu", $"/MenuItems/{s.EmptyMenuId}");
        await Shot("allergens", $"/allergens/{s.MenuId}");
        await Shot("combos", $"/combos/{s.MenuId}");
        await Shot("qrcode-new", "/qrcode/edit");
        await Shot("qrcode-edit-calendar", $"/qrcode/edit/{s.CalendarQrId}");
        await Shot("users", "/users");
        await Shot("users-invite", "/users/invite");
        await Shot("users-edit", $"/users/edit/{s.TenantUserId}");

        // Identity pages render statically (no circuit)
        await page.GotoAsync("/Account/Manage");
        await SettleAsync(page);
        await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{prefix}-account-manage.png"), FullPage = true });
    }

    /// <summary>Tailwind's CDN build compiles classes after load; give it a moment and wait for fonts.</summary>
    private static async Task SettleAsync(IPage page)
    {
        try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5_000 }); } catch (TimeoutException) { }
        await page.WaitForTimeoutAsync(700);
    }

    private async Task<Seeded> SeedTenantAsync(Guid ownerId)
    {
        var menuId = Guid.NewGuid();
        var emptyMenuId = Guid.NewGuid();
        var lunchMenuId = Guid.NewGuid();
        var timeTableQrId = Guid.NewGuid();
        var calendarQrId = Guid.NewGuid();
        var tenantUserId = Guid.NewGuid();
        Guid firstCategoryId = Guid.Empty;

        await App.SeedAsync(ownerId, async db =>
        {
            // --- Main menu with four categories, allergens, dietary flags and a combo
            var allergens = new Dictionary<string, Allergen>();
            foreach (var (code, name) in new[] { ("A", "Glutenhaltiges Getreide"), ("C", "Eier"), ("G", "Milch / Laktose"), ("L", "Sellerie"), ("H", "Schalenfrüchte"), ("O", "Sulfite") })
                allergens[code] = new Allergen { Id = Guid.NewGuid(), Code = code, Name = name, MenuId = menuId };
            db.Allergens.AddRange(allergens.Values);

            var vorspeisen = new Category { Id = Guid.NewGuid(), Name = "Vorspeisen", MenuId = menuId };
            var haupt = new Category { Id = Guid.NewGuid(), Name = "Hauptspeisen", MenuId = menuId };
            var desserts = new Category { Id = Guid.NewGuid(), Name = "Desserts", MenuId = menuId };
            var getraenke = new Category { Id = Guid.NewGuid(), Name = "Getränke", MenuId = menuId };
            firstCategoryId = vorspeisen.Id;

            db.Menus.Add(new Menu
            {
                Id = menuId, Name = "Gasthaus Zur Post", Description = "Hauptkarte, ganztägig",
                Theme = DesignTheme.Classic, Language = MenuLanguage.German,
                Categories = new List<Category> { vorspeisen, haupt, desserts, getraenke }
            });

            MenuItem Item(Category c, string name, string desc, decimal price, bool vegan = false, bool veg = false, bool spicy = false, params string[] codes)
            {
                var item = new MenuItem { Id = Guid.NewGuid(), Name = name, Description = desc, Price = price, CategoryId = c.Id, IsVegan = vegan, IsVegetarian = veg, IsSpicy = spicy };
                foreach (var code in codes) item.Allergens.Add(allergens[code]);
                db.MenuItems.Add(item);
                return item;
            }

            Item(vorspeisen, "Frittatensuppe", "Klare Rindsuppe mit Frittaten", 5.20m, codes: new[] { "A", "C", "L" });
            Item(vorspeisen, "Kürbiscremesuppe", "Mit Kernöl und Kürbiskernen", 5.90m, veg: true, codes: new[] { "G", "L" });
            Item(vorspeisen, "Bruschetta", "Tomaten, Basilikum, Knoblauch", 7.40m, vegan: true, codes: new[] { "A" });
            var schnitzel = Item(haupt, "Wiener Schnitzel vom Kalb", "Mit Petersilerdäpfeln und Preiselbeeren", 24.90m, codes: new[] { "A", "C", "G" });
            Item(haupt, "Zwiebelrostbraten", "Mit Bratkartoffeln und Röstzwiebeln", 26.50m, codes: new[] { "A", "O" });
            Item(haupt, "Käsespätzle", "Mit Bergkäse und Röstzwiebeln", 15.80m, veg: true, codes: new[] { "A", "C", "G" });
            Item(haupt, "Chili sin Carne", "Bohnen, Mais, Paprika, Reis", 14.20m, vegan: true, spicy: true);
            Item(haupt, "Tagesfisch", "Nach Marktangebot, Preis nach Tagespreis", 0m, codes: new[] { "G" });
            var strudel = Item(desserts, "Apfelstrudel", "Hausgemacht, mit Vanillesauce", 6.80m, veg: true, codes: new[] { "A", "C", "G" });
            Item(desserts, "Kaiserschmarrn", "Mit Zwetschkenröster, für 2 Personen", 13.90m, veg: true, codes: new[] { "A", "C", "G" });
            var spritzer = Item(getraenke, "Sommerspritzer 0,25 l", "Grüner Veltliner mit Soda", 4.10m, vegan: true, codes: new[] { "O" });
            Item(getraenke, "Almdudler 0,35 l", "", 3.60m, vegan: true);
            Item(getraenke, "Melange", "Wiener Kaffeehaus-Klassiker", 4.20m, veg: true, codes: new[] { "G" });

            db.MenuCombos.Add(new MenuCombo
            {
                Id = Guid.NewGuid(), Name = "Mittagsmenü", Description = "Schnitzel mit Strudel und Spritzer",
                ComboPrice = 29.90m, MenuId = menuId, TriggerMenuItemId = schnitzel.Id,
                Items = { new MenuComboItem { MenuItemId = strudel.Id }, new MenuComboItem { MenuItemId = spritzer.Id } }
            });

            // --- Lunch menu (small) and an empty menu (freshly created, nothing in it yet)
            var lunchCat = new Category { Id = Guid.NewGuid(), Name = "Mittagsteller", MenuId = lunchMenuId };
            db.Menus.Add(new Menu
            {
                Id = lunchMenuId, Name = "Mittagskarte", Description = "Mo bis Fr 11:30 bis 14:00",
                Theme = DesignTheme.Modern, Language = MenuLanguage.German, Categories = new List<Category> { lunchCat }
            });
            db.MenuItems.AddRange(
                new MenuItem { Id = Guid.NewGuid(), Name = "Menü 1: Gulasch", Description = "Mit Semmelknödel", Price = 11.90m, CategoryId = lunchCat.Id },
                new MenuItem { Id = Guid.NewGuid(), Name = "Menü 2: Gemüselasagne", Description = "Mit Blattsalat", Price = 10.90m, CategoryId = lunchCat.Id, IsVegetarian = true });

            db.Menus.Add(new Menu { Id = emptyMenuId, Name = "Abendkarte", Description = "", Theme = DesignTheme.Elegant, Language = MenuLanguage.German });

            // --- QR codes: direct, time table, calendar
            var directQrId = Guid.NewGuid();
            db.QRCodes.Add(new QRCode { Id = directQrId, Title = "Tisch 1", MenuId = menuId });
            db.QRCodes.Add(new QRCode { Id = Guid.NewGuid(), Title = "Tisch 2", MenuId = menuId });
            db.QRCodes.Add(new QRCode
            {
                Id = timeTableQrId, Title = "Eingang (Zeitplan)", IsTimeTableBased = true,
                TimeTableEntries = new List<TimeTableEntry>
                {
                    new() { Id = Guid.NewGuid(), StartTime = new TimeOnly(11, 0), EndTime = new TimeOnly(14, 0), MenuId = lunchMenuId },
                    new() { Id = Guid.NewGuid(), StartTime = new TimeOnly(17, 0), EndTime = new TimeOnly(22, 0), MenuId = menuId }
                }
            });
            db.QRCodes.Add(new QRCode
            {
                Id = calendarQrId, Title = "Tagesaktion (Kalender)", IsCalendarBased = true, MenuId = menuId,
                CalendarEntries = new List<CalendarEntry>
                {
                    new() { Id = Guid.NewGuid(), Date = DateOnly.FromDateTime(DateTime.Today), MenuId = lunchMenuId },
                    new() { Id = Guid.NewGuid(), Date = DateOnly.FromDateTime(DateTime.Today), RecurringDayOfWeek = DayOfWeek.Friday, EndDate = DateOnly.FromDateTime(DateTime.Today.AddMonths(2)), MenuId = menuId }
                }
            });

            // --- A sub-account
            var subId = Guid.NewGuid().ToString();
            db.Users.Add(new ApplicationUser
            {
                Id = subId, UserName = "kellner@gasthaus.test", NormalizedUserName = "KELLNER@GASTHAUS.TEST",
                Email = "kellner@gasthaus.test", NormalizedEmail = "KELLNER@GASTHAUS.TEST", EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString(), TenantOwnerId = ownerId.ToString()
            });
            db.TenantUsers.Add(new TenantUser
            {
                Id = tenantUserId, ApplicationUserId = subId, TenantOwnerId = ownerId.ToString(),
                Role = TenantRole.Employee, DisplayName = "Max Kellner", IsActive = true
            });

            // --- Two weeks of views and clicks for the analytics page
            var rnd = new Random(42);
            for (var day = 0; day < 14; day++)
            {
                var date = DateTime.UtcNow.Date.AddDays(-day);
                for (var i = 0; i < 5 + rnd.Next(12); i++)
                {
                    var session = Guid.NewGuid().ToString("N");
                    db.MenuViews.Add(new MenuView { Id = Guid.NewGuid(), SessionId = session, MenuId = menuId, QRCodeId = directQrId, ViewedAt = date.AddHours(11 + rnd.Next(10)) });
                    if (rnd.Next(3) == 0)
                        db.MenuItemClicks.Add(new MenuItemClick { Id = Guid.NewGuid(), SessionId = session, MenuId = menuId, MenuItemId = schnitzel.Id, ClickedAt = date.AddHours(12) });
                }
                for (var i = 0; i < rnd.Next(5); i++)
                    db.MenuViews.Add(new MenuView { Id = Guid.NewGuid(), SessionId = Guid.NewGuid().ToString("N"), MenuId = lunchMenuId, QRCodeId = timeTableQrId, ViewedAt = date.AddHours(12) });
            }
            await Task.CompletedTask;
        });

        return new Seeded(menuId, firstCategoryId, emptyMenuId, timeTableQrId, calendarQrId, tenantUserId);
    }
}
