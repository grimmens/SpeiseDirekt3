using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;
using SpeiseDirekt3.Preview;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// The demo data the preview app adds to one tenant at startup (<see cref="PreviewSeeder"/>, environment
/// <c>Preview</c>). Runs the seeder directly against the test database, like the hosted service does.
/// </summary>
public class PreviewSeedTests : E2ETestBase
{
    private const string OwnerEmail = "owner@e2e.test";

    public PreviewSeedTests(E2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task UnknownEmail_SeedsNothing()
    {
        int menusBefore, qrCodesBefore;
        await using (var db = App.CreateDbContext(Guid.Empty))
        {
            menusBefore = await db.Menus.IgnoreQueryFilters().CountAsync();
            qrCodesBefore = await db.QRCodes.IgnoreQueryFilters().CountAsync();
        }

        var result = await PreviewSeeder.SeedAsync(App.CreateDbContext, "niemand@preview.invalid", App.Clock);

        Assert.False(result.UserFound);
        await using (var db = App.CreateDbContext(Guid.Empty))
        {
            Assert.Equal(menusBefore, await db.Menus.IgnoreQueryFilters().CountAsync());
            Assert.Equal(qrCodesBefore, await db.QRCodes.IgnoreQueryFilters().CountAsync());
        }
    }

    [Fact]
    public async Task Owner_GetsThreeMenusAndFiveQrCodes_OnlyOnce()
    {
        var first = await PreviewSeeder.SeedAsync(App.CreateDbContext, OwnerEmail, App.Clock);
        // Second run (e-mail in another case, with whitespace): nothing is added twice
        var second = await PreviewSeeder.SeedAsync(App.CreateDbContext, " OWNER@E2E.test ", App.Clock);

        Assert.True(first.UserFound);
        Assert.Equal(E2ESeedData.OwnerId, first.OwnerId);
        Assert.Equal(0, second.MenusAdded);
        Assert.Equal(0, second.QrCodesAdded);
        Assert.Equal(first.MenusTotal, second.MenusTotal);
        Assert.Equal(first.QrCodesTotal, second.QrCodesTotal);

        await using var db = App.CreateDbContext(E2ESeedData.OwnerId);
        var menus = await db.Menus
            .Include(m => m.Categories!).ThenInclude(c => c.MenuItems)
            .Where(m => m.Name.StartsWith(PreviewSeeder.Prefix))
            .ToListAsync();
        Assert.Equal(PreviewSeeder.MenuNames.OrderBy(n => n), menus.Select(m => m.Name).OrderBy(n => n));
        Assert.All(menus, m => Assert.NotEmpty(m.Categories!));
        Assert.All(menus, m => Assert.All(m.Categories!, c => Assert.NotEmpty(c.MenuItems!)));
        Assert.Equal(new[] { DesignTheme.Classic, DesignTheme.Modern, DesignTheme.Dark },
            PreviewSeeder.MenuNames.Select(n => menus.Single(m => m.Name == n).Theme));

        var gasthaus = menus.Single(m => m.Name == PreviewSeeder.MenuNames[0]);
        Assert.Equal(new[] { "Vorspeisen", "Hauptspeisen", "Desserts", "Getränke" },
            gasthaus.Categories!.OrderBy(c => c.SortOrder).Select(c => c.Name));
        Assert.Equal(13, gasthaus.Categories!.Sum(c => c.MenuItems!.Count));
        Assert.Equal(6, await db.Allergens.CountAsync(a => a.MenuId == gasthaus.Id));

        // Guest-menu data model: subtitles, labels, variants on drinks (price = lowest variant), extras on dishes
        Assert.Equal(new[] { "Suppen und kleine Speisen", "Warme Küche bis 21:30 Uhr", "Aus der eigenen Patisserie", null },
            gasthaus.Categories!.OrderBy(c => c.SortOrder).Select(c => c.Description));
        var items = await db.MenuItems.Include(i => i.PriceOptions)
            .Where(i => i.Category!.MenuId == gasthaus.Id).ToDictionaryAsync(i => i.Name);
        Assert.Equal(new[] { ("0,25 l", 4.10m), ("0,5 l", 7.40m) }, items["Sommerspritzer"].Variants().Select(o => (o.Name, o.Price)));
        Assert.Equal(4.10m, items["Sommerspritzer"].Price);
        Assert.Equal(3.40m, items["Melange"].Price); // follows its cheaper variant "Klein"
        Assert.Equal(new[] { "+ Speck", "+ Blattsalat" }, items["Käsespätzle"].Extras().Select(o => o.Name));
        Assert.True(items["Wiener Schnitzel vom Kalb"].IsPopular);
        Assert.True(items["Kürbiscremesuppe"].IsNew);
        Assert.True(items["Chili sin Carne"].IsHalal);
        Assert.True(items["Almdudler"].IsAlcoholFree);
        Assert.All(items.Values.SelectMany(i => i.PriceOptions), o => Assert.Equal(E2ESeedData.OwnerId, o.ApplicationUserId));

        // The owner already has a restaurant profile (E2E seed): it is kept as it is
        Assert.False(first.RestaurantProfileAdded);
        Assert.False(second.RestaurantProfileAdded);
        Assert.Equal(E2ESeedData.RestaurantName, (await db.RestaurantProfiles.SingleAsync()).Name);
        Assert.Equal(1, await db.MenuCombos.CountAsync(c => c.MenuId == gasthaus.Id));
        Assert.True(await db.MenuItems.AnyAsync(i => i.Category!.MenuId == gasthaus.Id && i.Allergens.Count > 0), "items carry allergens");

        var qrCodes = await db.QRCodes
            .Include(q => q.TimeTableEntries).Include(q => q.CalendarEntries)
            .Where(q => q.Title.StartsWith(PreviewSeeder.Prefix))
            .ToListAsync();
        Assert.Equal(PreviewSeeder.QrCodeTitles.OrderBy(t => t), qrCodes.Select(q => q.Title).OrderBy(t => t));
        Assert.Equal(3, qrCodes.Count(q => !q.IsTimeTableBased && !q.IsCalendarBased && q.MenuId != null));
        Assert.Equal(2, qrCodes.Single(q => q.IsTimeTableBased).TimeTableEntries!.Count);
        Assert.Equal(2, qrCodes.Single(q => q.IsCalendarBased).CalendarEntries!.Count);
        // Every QR code and entry points at one of the seeded menus of this tenant
        var menuIds = menus.Select(m => m.Id).ToHashSet();
        Assert.All(qrCodes.Where(q => q.MenuId != null), q => Assert.Contains(q.MenuId!.Value, menuIds));
        Assert.All(qrCodes.SelectMany(q => q.TimeTableEntries!), e => Assert.Contains(e.MenuId, menuIds));
        Assert.All(qrCodes.SelectMany(q => q.CalendarEntries!), e => Assert.Contains(e.MenuId, menuIds));
    }

    [Fact]
    public async Task TenantWithoutProfile_GetsTheDemoProfile_Once()
    {
        var ownerId = Guid.NewGuid();
        var email = UniqueEmail("preview");
        await using (var db = App.CreateDbContext(Guid.Empty))
        {
            db.Users.Add(new SpeiseDirekt.Data.ApplicationUser
            {
                Id = ownerId.ToString(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                Email = email, NormalizedEmail = email.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString()
            });
            await db.SaveChangesAsync();
        }

        var first = await PreviewSeeder.SeedAsync(App.CreateDbContext, email, App.Clock);
        var second = await PreviewSeeder.SeedAsync(App.CreateDbContext, email, App.Clock);

        Assert.True(first.RestaurantProfileAdded);
        Assert.False(second.RestaurantProfileAdded);
        await using var tenant = App.CreateDbContext(ownerId);
        var profile = await tenant.RestaurantProfiles.SingleAsync();
        Assert.Equal(PreviewSeeder.RestaurantName, profile.Name);
        Assert.Equal(ownerId, profile.ApplicationUserId);
        Assert.Equal("https://example.com", profile.Website);
        Assert.Contains('\n', profile.OpeningHours);
        Assert.False(string.IsNullOrWhiteSpace(profile.Description));
    }

    [Fact]
    public async Task SeededDirectQrCode_ShowsItsMenuToGuests_InItsTheme()
    {
        await PreviewSeeder.SeedAsync(App.CreateDbContext, OwnerEmail, App.Clock);
        Guid qrId;
        await using (var db = App.CreateDbContext(E2ESeedData.OwnerId))
            qrId = (await db.QRCodes.SingleAsync(q => q.Title == PreviewSeeder.QrCodeTitles[2])).Id; // "Bar" → Dark theme

        await OpenPublicMenuPageAsync(Page, $"/qr/{qrId}");

        await Assertions.Expect(Page.Locator("h1").First).ToHaveTextAsync(PreviewSeeder.MenuNames[2]);
        await Assertions.Expect(Page.GetByTestId("menu-display")).ToHaveAttributeAsync("data-theme", "dark");
        await Assertions.Expect(Page.GetByText("Espresso Martini").First).ToBeVisibleAsync();
    }
}
