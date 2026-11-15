using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
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
