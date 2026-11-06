using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Back-office shell (package P1): the dashboard on <c>/</c> with its getting-started checklist, the menu
/// cards on <c>/menus</c>, the menu delete confirmation, the QR code tile on <c>/menudetail</c> and the sidebar.
/// </summary>
public class ShellDashboardMenuListTests : BackOfficeTestBase
{
    public ShellDashboardMenuListTests(E2EFixture fixture) : base(fixture) { }

    // --- Helpers ---

    private async Task<(string Email, Guid OwnerId)> RegisterOwnerAsync()
    {
        var email = await RegisterNewOwnerAsync();
        await using var db = App.CreateDbContext(Guid.Empty);
        return (email, Guid.Parse((await db.Users.SingleAsync(u => u.Email == email)).Id));
    }

    private ILocator StatValue(string tile) => Page.GetByTestId(tile).GetByTestId("stat-value");

    private ILocator Steps => Page.GetByTestId("checklist-step");

    private ILocator MenuCard(string menuName) =>
        Page.GetByTestId("menu-card").Filter(new() { Has = Page.Locator("h3", new() { HasTextString = menuName }) });

    private ILocator ConfirmationModal => Page.Locator("#confirmationModal");

    private static Menu NewMenu(string name, string description = "Seeded by E2E test",
        DesignTheme theme = DesignTheme.Modern, MenuLanguage language = MenuLanguage.German) =>
        new() { Id = Guid.NewGuid(), Name = name, Description = description, Theme = theme, Language = language };

    private static Category NewCategory(Guid menuId, string name) => new() { Id = Guid.NewGuid(), Name = name, MenuId = menuId };

    private static MenuItem NewItem(Guid categoryId, string name, decimal price = 9.50m) =>
        new() { Id = Guid.NewGuid(), Name = name, Description = "", Price = price, CategoryId = categoryId };

    private static QRCode DirectQr(Guid menuId, string title) => new() { Id = Guid.NewGuid(), Title = title, MenuId = menuId };

    private static QRCode TimeTableQr(string title, params Guid[] menuIds) => new()
    {
        Id = Guid.NewGuid(), Title = title, IsTimeTableBased = true,
        TimeTableEntries = menuIds.Select((menuId, i) => new TimeTableEntry
        {
            Id = Guid.NewGuid(), StartTime = new TimeOnly(8 + 4 * i, 0), EndTime = new TimeOnly(11 + 4 * i, 0), MenuId = menuId
        }).ToList()
    };

    // --- Dashboard ---

    [Fact]
    public async Task NewOwner_SeesDashboard_WithAllChecklistStepsOpen()
    {
        var (email, _) = await RegisterOwnerAsync();
        await GotoInteractiveAsync("/");

        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Welcome back" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("dashboard-user")).ToHaveTextAsync(email);
        await Assertions.Expect(Page.GetByText("Powerful Features")).ToHaveCountAsync(0);

        await Assertions.Expect(Steps).ToHaveCountAsync(4);
        for (var i = 0; i < 4; i++)
            await Assertions.Expect(Steps.Nth(i)).ToHaveAttributeAsync("data-state", "open");
        await Assertions.Expect(Page.GetByTestId("getting-started-progress")).ToHaveTextAsync("0 of 4 done");
        await Assertions.Expect(Steps.Nth(0).GetByTestId("checklist-action")).ToHaveAttributeAsync("href", "menu/edit");
        await Assertions.Expect(Steps.Nth(1)).ToContainTextAsync("Create a menu first.");
        await Assertions.Expect(Steps.Nth(3)).ToContainTextAsync("Create a QR code first.");

        await Assertions.Expect(StatValue("stat-menus")).ToHaveTextAsync("0");
        await Assertions.Expect(StatValue("stat-qrcodes")).ToHaveTextAsync("0");
        await Assertions.Expect(StatValue("stat-views")).ToHaveTextAsync("0");
        await Assertions.Expect(StatValue("stat-team")).ToHaveTextAsync("0");

        var actions = Page.GetByTestId("quick-actions");
        await Assertions.Expect(actions.GetByRole(AriaRole.Link, new() { Name = "Create menu" })).ToHaveAttributeAsync("href", "menu/edit");
        await Assertions.Expect(actions.GetByRole(AriaRole.Link, new() { Name = "Create QR code" })).ToHaveAttributeAsync("href", "qrcode/edit");
        await Assertions.Expect(actions.GetByRole(AriaRole.Link, new() { Name = "Analytics" })).ToHaveAttributeAsync("href", "analytics");
        await Assertions.Expect(Page.GetByTestId("dashboard-menus")).ToContainTextAsync("No menus yet.");

        // The checklist's first action leads to the menu editor
        await Steps.Nth(0).GetByTestId("checklist-action").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/menu/edit$"));
    }

    [Fact]
    public async Task Checklist_FollowsTheData_AndDisappearsAfterTheFirstScan()
    {
        var (_, ownerId) = await RegisterOwnerAsync();
        var menu = NewMenu("Bistro Start");
        var category = NewCategory(menu.Id, "Suppen");
        var qr = DirectQr(menu.Id, "Tisch 1");
        await App.SeedAsync(ownerId, db =>
        {
            db.Menus.Add(menu);
            db.Categories.Add(category);
            db.MenuItems.Add(NewItem(category.Id, "Gulaschsuppe"));
            db.QRCodes.Add(qr);

            // One active and one deactivated sub-account
            foreach (var (name, active) in new[] { ("Aktiv", true), ("Inaktiv", false) })
            {
                var userId = Guid.NewGuid().ToString();
                var email = $"{name.ToLowerInvariant()}-{Guid.NewGuid():N}@e2e.test";
                db.Users.Add(new ApplicationUser
                {
                    Id = userId, UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email,
                    NormalizedEmail = email.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString(), TenantOwnerId = ownerId.ToString()
                });
                db.TenantUsers.Add(new TenantUser
                {
                    Id = Guid.NewGuid(), ApplicationUserId = userId, TenantOwnerId = ownerId.ToString(),
                    Role = TenantRole.Employee, DisplayName = name, IsActive = active
                });
            }
            return Task.CompletedTask;
        });

        await GotoInteractiveAsync("/");

        for (var i = 0; i < 3; i++)
            await Assertions.Expect(Steps.Nth(i)).ToHaveAttributeAsync("data-state", "done");
        await Assertions.Expect(Steps.Nth(3)).ToHaveAttributeAsync("data-state", "open");
        await Assertions.Expect(Steps.Nth(3).GetByTestId("checklist-action")).ToHaveAttributeAsync("href", "qrcodes");
        await Assertions.Expect(Page.GetByTestId("getting-started-progress")).ToHaveTextAsync("3 of 4 done");

        await Assertions.Expect(StatValue("stat-menus")).ToHaveTextAsync("1");
        await Assertions.Expect(StatValue("stat-qrcodes")).ToHaveTextAsync("1");
        await Assertions.Expect(StatValue("stat-views")).ToHaveTextAsync("0");
        await Assertions.Expect(StatValue("stat-team")).ToHaveTextAsync("1");

        var menuLink = Page.GetByTestId("dashboard-menu");
        await Assertions.Expect(menuLink).ToHaveCountAsync(1);
        await Assertions.Expect(menuLink).ToHaveAttributeAsync("href", $"menudetail/{menu.Id}");
        await Assertions.Expect(menuLink).ToContainTextAsync("Bistro Start");
        await Assertions.Expect(menuLink).ToContainTextAsync("1 category · 1 item");

        // The first guest scans the code (plus an old view outside the 7-day window)
        await App.SeedAsync(ownerId, db =>
        {
            db.MenuViews.Add(new MenuView { Id = Guid.NewGuid(), SessionId = "scan", MenuId = menu.Id, QRCodeId = qr.Id, ViewedAt = DateTime.UtcNow.AddHours(-1) });
            db.MenuViews.Add(new MenuView { Id = Guid.NewGuid(), SessionId = "old", MenuId = menu.Id, ViewedAt = DateTime.UtcNow.AddDays(-10) });
            return Task.CompletedTask;
        });
        await GotoInteractiveAsync("/");

        await Assertions.Expect(StatValue("stat-views")).ToHaveTextAsync("1");
        await Assertions.Expect(Page.GetByTestId("getting-started")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("dashboard-menu")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task Anonymous_VisitorStillSeesTheLandingPage()
    {
        await Page.GotoAsync("/");

        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Powerful Features" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Welcome back" })).ToHaveCountAsync(0);
    }

    // --- Menu list ---

    [Fact]
    public async Task MenuList_CardsShowCountsBadges_AndActionsOutsideTheLink()
    {
        var (_, ownerId) = await RegisterOwnerAsync();
        const string longName = "Gasthaus Zur Alten Post am Marktplatz";
        var full = NewMenu(longName, "Hauptkarte, ganztägig", DesignTheme.Classic, MenuLanguage.English);
        var empty = NewMenu("Leere Karte", "", DesignTheme.Dark);
        await App.SeedAsync(ownerId, db =>
        {
            db.Menus.AddRange(full, empty);
            var starters = NewCategory(full.Id, "Vorspeisen");
            var mains = NewCategory(full.Id, "Hauptspeisen");
            db.Categories.AddRange(starters, mains);
            db.MenuItems.AddRange(NewItem(starters.Id, "Suppe"), NewItem(mains.Id, "Schnitzel"), NewItem(mains.Id, "Gulasch"));
            db.QRCodes.AddRange(DirectQr(full.Id, "Tisch 1"), DirectQr(full.Id, "Tisch 2"), TimeTableQr("Eingang", full.Id));
            return Task.CompletedTask;
        });

        await GotoInteractiveAsync("/menus");

        var card = MenuCard(longName);
        await Assertions.Expect(card.GetByTestId("menu-card-counts")).ToHaveTextAsync(new Regex(@"^\s*2 categories\s+3 items\s+3 QR codes\s*$"));
        await Assertions.Expect(card).ToContainTextAsync("Classic");
        await Assertions.Expect(card).ToContainTextAsync("English");
        await Assertions.Expect(card).ToContainTextAsync("Hauptkarte, ganztägig");
        await Assertions.Expect(card.Locator("h3")).Not.ToHaveClassAsync(new Regex(@"\btruncate\b"));

        var emptyCard = MenuCard("Leere Karte");
        await Assertions.Expect(emptyCard).ToContainTextAsync("No description");
        await Assertions.Expect(emptyCard.GetByTestId("menu-card-counts")).ToHaveTextAsync(new Regex(@"^\s*0 categories\s+0 items\s+0 QR codes\s*$"));
        await Assertions.Expect(emptyCard).ToContainTextAsync("Dark");

        // No button or link nested in a link (the card body is the link, the actions sit below it)
        (await Page.Locator("a button, a a, a input, a select").CountAsync()).Should().Be(0);

        await Assertions.Expect(card.GetByRole(AriaRole.Link, new() { Name = "Preview" })).ToHaveAttributeAsync("href", $"menu/preview/{full.Id}");
        await Assertions.Expect(card.GetByRole(AriaRole.Link, new() { Name = "Preview" })).ToHaveAttributeAsync("target", "_blank");
        await Assertions.Expect(card.GetByRole(AriaRole.Button, new() { Name = "Edit" })).ToBeVisibleAsync();
        await Assertions.Expect(card.GetByRole(AriaRole.Button, new() { Name = $"Delete menu {longName}" })).ToBeVisibleAsync();

        var open = card.GetByRole(AriaRole.Link, new() { Name = "Open", Exact = true });
        await Assertions.Expect(open).ToHaveAttributeAsync("href", $"menudetail/{full.Id}");
        await open.ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menudetail/{full.Id}$"));
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = longName })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task DeleteMenu_FromList_NamesTheQrCodes_AndRemovesTheMenuWithItsData()
    {
        var (_, ownerId) = await RegisterOwnerAsync();
        var doomed = NewMenu("Zu löschen");
        var kept = NewMenu("Bleibt");
        var category = NewCategory(doomed.Id, "Hauptspeisen");
        var schnitzel = NewItem(category.Id, "Schnitzel");
        var salad = NewItem(category.Id, "Salat");
        var table = DirectQr(doomed.Id, "Tisch 1");
        // Calendar code: falls back to the doomed menu, shows the kept one on its dates
        var calendar = new QRCode
        {
            Id = Guid.NewGuid(), Title = "Aktion", IsCalendarBased = true, MenuId = doomed.Id,
            CalendarEntries = new List<CalendarEntry> { new() { Id = Guid.NewGuid(), Date = DateOnly.FromDateTime(DateTime.Today), MenuId = kept.Id } }
        };
        var schedule = TimeTableQr("Eingang", kept.Id, doomed.Id);
        var keptView = new MenuView { Id = Guid.NewGuid(), SessionId = "kept", MenuId = kept.Id, QRCodeId = calendar.Id, ViewedAt = DateTime.UtcNow };
        await App.SeedAsync(ownerId, db =>
        {
            db.Menus.AddRange(doomed, kept);
            db.Categories.Add(category);
            var allergen = new Allergen { Id = Guid.NewGuid(), Code = "A", Name = "Gluten", MenuId = doomed.Id };
            db.Allergens.Add(allergen);
            schnitzel.Allergens.Add(allergen);
            db.MenuItems.AddRange(schnitzel, salad);
            db.MenuCombos.Add(new MenuCombo
            {
                Id = Guid.NewGuid(), Name = "Mittag", ComboPrice = 12m, MenuId = doomed.Id, TriggerMenuItemId = schnitzel.Id,
                Items = { new MenuComboItem { MenuItemId = salad.Id } }
            });
            db.QRCodes.AddRange(table, calendar, schedule);
            db.MenuViews.AddRange(
                new MenuView { Id = Guid.NewGuid(), SessionId = "doomed", MenuId = doomed.Id, QRCodeId = table.Id, ViewedAt = DateTime.UtcNow },
                keptView);
            db.MenuItemClicks.Add(new MenuItemClick { Id = Guid.NewGuid(), SessionId = "doomed", MenuId = doomed.Id, MenuItemId = schnitzel.Id });
            return Task.CompletedTask;
        });

        await GotoInteractiveAsync("/menus");
        await MenuCard("Zu löschen").GetByRole(AriaRole.Button, new() { Name = "Delete menu Zu löschen" }).ClickAsync();

        await Assertions.Expect(ConfirmationModal).ToBeVisibleAsync();
        await Assertions.Expect(ConfirmationModal).ToContainTextAsync("Delete \"Zu löschen\"");
        await Assertions.Expect(ConfirmationModal).ToContainTextAsync("This also deletes 2 QR codes pointing at this menu.");
        await Assertions.Expect(ConfirmationModal).ToContainTextAsync("It is also removed from the schedule of 1 other QR code.");
        await ConfirmationModal.Locator("#confirmBtn").ClickAsync();

        await Assertions.Expect(MenuCard("Zu löschen")).ToHaveCountAsync(0);
        await Assertions.Expect(MenuCard("Bleibt")).ToBeVisibleAsync();

        await using var db = App.CreateDbContext(ownerId);
        (await db.Menus.AnyAsync(m => m.Id == doomed.Id)).Should().BeFalse();
        (await db.Categories.AnyAsync(c => c.MenuId == doomed.Id)).Should().BeFalse();
        (await db.Allergens.AnyAsync(a => a.MenuId == doomed.Id)).Should().BeFalse();
        (await db.MenuCombos.AnyAsync(c => c.MenuId == doomed.Id)).Should().BeFalse();
        (await db.QRCodes.Select(q => q.Title).ToListAsync()).Should().BeEquivalentTo("Eingang");
        (await db.TimeTableEntries.Where(t => t.QRCodeId == schedule.Id).Select(t => t.MenuId).ToListAsync()).Should().Equal(kept.Id);
        (await db.MenuViews.AnyAsync(v => v.MenuId == doomed.Id)).Should().BeFalse();
        (await db.MenuItemClicks.AnyAsync(c => c.MenuId == doomed.Id)).Should().BeFalse();
        // The kept menu's view survives; the deleted QR code is no longer referenced
        (await db.MenuViews.SingleAsync(v => v.Id == keptView.Id)).QRCodeId.Should().BeNull();
    }

    [Fact]
    public async Task DeleteMenu_FromDetail_WithoutQrCodes_SaysNothingExtra()
    {
        var (_, ownerId) = await RegisterOwnerAsync();
        var menu = NewMenu("Solo");
        await App.SeedAsync(ownerId, db => { db.Menus.Add(menu); return Task.CompletedTask; });

        await GotoInteractiveAsync($"/menudetail/{menu.Id}");
        await Page.Locator("main").GetByRole(AriaRole.Button, new() { Name = "Delete menu" }).ClickAsync();

        await Assertions.Expect(ConfirmationModal.Locator(".modal-body")).ToHaveTextAsync("Delete \"Solo\" with its categories and items?");
        await ConfirmationModal.Locator("#confirmBtn").ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/menus$"));
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "No menus yet" })).ToBeVisibleAsync();
        await using var db = App.CreateDbContext(ownerId);
        (await db.Menus.AnyAsync(m => m.Id == menu.Id)).Should().BeFalse();
    }

    // --- Menu detail ---

    [Fact]
    public async Task MenuDetail_QrTileCountsDirectAndScheduledCodes_AndPreviewIsOfferedOnce()
    {
        var (_, ownerId) = await RegisterOwnerAsync();
        var menu = NewMenu("Detailkarte");
        var other = NewMenu("Andere Karte");
        await App.SeedAsync(ownerId, db =>
        {
            db.Menus.AddRange(menu, other);
            db.QRCodes.AddRange(DirectQr(menu.Id, "Tisch 1"), TimeTableQr("Eingang", other.Id, menu.Id), DirectQr(other.Id, "Bar"));
            return Task.CompletedTask;
        });

        await GotoInteractiveAsync($"/menudetail/{menu.Id}");

        var tile = Page.GetByTestId("menu-qr-tile");
        await Assertions.Expect(tile).ToHaveAttributeAsync("href", "qrcodes");
        await Assertions.Expect(tile).ToContainTextAsync("2");
        await Assertions.Expect(tile).ToContainTextAsync("1 direct, 1 via schedule");

        var main = Page.Locator("main");
        await Assertions.Expect(main.GetByRole(AriaRole.Link, new() { Name = "Preview" })).ToHaveCountAsync(1);
        await Assertions.Expect(main.GetByRole(AriaRole.Link, new() { Name = "Preview" })).ToHaveAttributeAsync("href", $"menu/preview/{menu.Id}");

        await tile.ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
    }

    [Fact]
    public async Task MenuDetail_UnknownMenu_SaysNotFound()
    {
        await RegisterOwnerAsync();
        await GotoInteractiveAsync($"/menudetail/{Guid.NewGuid()}");

        await Assertions.Expect(Page.GetByTestId("menu-not-found")).ToContainTextAsync("Menu not found");
        await Assertions.Expect(Page.GetByText("Loading...")).ToHaveCountAsync(0);
    }

    // --- Sidebar ---

    [Fact]
    public async Task Sidebar_HasNoDebugLink_AndShowsTheOwnerWithTitle()
    {
        var (email, _) = await RegisterOwnerAsync();
        await GotoInteractiveAsync("/menus");

        var user = Page.GetByTestId("sidebar-user").First;
        await Assertions.Expect(user).ToHaveAttributeAsync("title", email);
        await Assertions.Expect(user).ToContainTextAsync(email);
        await Assertions.Expect(user).ToContainTextAsync("Owner");
        await Assertions.Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Auth Required" })).ToHaveCountAsync(0);

        // The debug page is gone (an empty 404, which Chromium refuses to navigate to, so ask over HTTP)
        var response = await Page.APIRequest.GetAsync("/auth");
        response.Status.Should().Be(404);
    }

    [Fact]
    public async Task Sidebar_AndDashboard_ShowTheDisplayNameOfASubAccount()
    {
        var (_, ownerId) = await RegisterOwnerAsync();
        var subEmail = UniqueEmail("kellner");
        await App.SeedAsync(ownerId, db =>
        {
            var sub = new ApplicationUser
            {
                Id = Guid.NewGuid().ToString(), UserName = subEmail, NormalizedUserName = subEmail.ToUpperInvariant(),
                Email = subEmail, NormalizedEmail = subEmail.ToUpperInvariant(), EmailConfirmed = true,
                SecurityStamp = Guid.NewGuid().ToString(), TenantOwnerId = ownerId.ToString()
            };
            sub.PasswordHash = new PasswordHasher<ApplicationUser>().HashPassword(sub, Password);
            db.Users.Add(sub);
            db.TenantUsers.Add(new TenantUser
            {
                Id = Guid.NewGuid(), ApplicationUserId = sub.Id, TenantOwnerId = ownerId.ToString(),
                Role = TenantRole.Manager, DisplayName = "Max Kellner", IsActive = true
            });
            return Task.CompletedTask;
        });

        await LogoutAsync(Page);
        await LoginAsync(Page, subEmail);

        var user = Page.GetByTestId("sidebar-user").First;
        await Assertions.Expect(user).ToContainTextAsync("Max Kellner");
        await Assertions.Expect(user).ToContainTextAsync("Manager");
        await Assertions.Expect(user).ToHaveAttributeAsync("title", $"Max Kellner ({subEmail})");
        await Assertions.Expect(Page.GetByTestId("dashboard-user")).ToHaveTextAsync("Max Kellner");
    }

    [Fact]
    public async Task PhoneSidebar_StaysOpenWhileBrowsingTheTree_AndClosesWhenALinkIsFollowed()
    {
        var (_, ownerId) = await RegisterOwnerAsync();
        var menu = NewMenu("Handykarte");
        await App.SeedAsync(ownerId, db => { db.Menus.Add(menu); return Task.CompletedTask; });

        await Page.SetViewportSizeAsync(390, 844);
        await GotoInteractiveAsync("/menus");

        var overlay = Page.Locator("#mobile-sidebar");
        await Assertions.Expect(overlay).ToBeHiddenAsync();
        await Page.GetByRole(AriaRole.Button, new() { Name = "Open sidebar" }).ClickAsync();
        await Assertions.Expect(overlay).ToBeVisibleAsync();

        // Expanding a menu in the tree is not a navigation: the overlay stays
        var node = overlay.Locator("div.flex.items-center", new() { Has = Page.Locator($"a[href='menudetail/{menu.Id}']") });
        await node.Locator("button").ClickAsync();
        await Assertions.Expect(overlay.Locator($"a[href='categories/{menu.Id}']")).ToBeVisibleAsync();
        await Assertions.Expect(overlay).ToBeVisibleAsync();

        await overlay.GetByRole(AriaRole.Link, new() { Name = "QR Codes", Exact = true }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
        await Assertions.Expect(overlay).ToBeHiddenAsync();
    }
}
