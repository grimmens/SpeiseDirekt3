using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// The two roles of AP14 from the team member's side: the owner invites a manager (<c>/users/invite</c>, no role picker);
/// the manager signs in, sees no team entry, is denied the team pages and works on the owner's menus, items, QR codes,
/// restaurant profile and analytics. A deactivated member is denied every page of the tenant.
/// </summary>
public class PermissionTests : BackOfficeTestBase
{
    public PermissionTests(E2EFixture fixture) : base(fixture) { }

    private readonly List<IBrowserContext> _contexts = [];

    public new async Task DisposeAsync()
    {
        foreach (var context in _contexts)
            await context.DisposeAsync();
        await base.DisposeAsync();
    }

    private async Task<IPage> NewMemberPageAsync(string? locale = null)
    {
        var (context, page) = await NewContextAsync(locale);
        _contexts.Add(context);
        return page;
    }

    /// <summary>Invites a team member through the UI (as the owner in <see cref="E2ETestBase.Page"/>).</summary>
    private async Task InviteAsync(string email, string displayName)
    {
        await GotoInteractiveAsync("/users/invite");
        await Assertions.Expect(Page.Locator("select")).ToHaveCountAsync(0); // no role picker any more
        await Page.GetByPlaceholder("employee@example.com").FillAsync(email);
        await Page.GetByPlaceholder("John Doe").FillAsync(displayName);
        await Page.GetByPlaceholder("Min. 8 characters").FillAsync(Password);
        await Button("Create Account").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/users$"));
    }

    private static ILocator NavEntry(IPage page, string area, string key) =>
        page.GetByTestId(area).Locator($"a[data-nav='{key}']");

    private static async Task ExpectAccessDeniedAsync(IPage page, string url)
    {
        await page.GotoAsync(url);
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/Account/AccessDenied$"), new() { Timeout = 15_000 });
    }

    [Fact]
    public async Task Manager_SeesNoTeam_IsDeniedTheTeamPages_AndEditsAMenuItem()
    {
        var ownerEmail = await RegisterNewOwnerAsync();
        var ownerId = await OwnerIdAsync(ownerEmail);
        var menuId = await CreateMenuViaUiAsync("Team Menü");
        await CreateCategoryViaUiAsync(menuId, "Vorspeisen");
        await CreateMenuItemViaUiAsync(menuId, "Bruschetta", "Tomate, Basilikum", 5.50m);

        var managerEmail = UniqueEmail("manager");
        await InviteAsync(managerEmail, "Erika Managerin");
        var row = Page.GetByTestId("user-row").Filter(new() { HasText = "Erika Managerin" });
        await Assertions.Expect(row.GetByTestId("role-badge")).ToHaveTextAsync("Manager");
        Guid memberId;
        await using (var db = App.CreateDbContext(ownerId))
        {
            var member = await db.TenantUsers.SingleAsync(tu => tu.TenantOwnerId == ownerId.ToString());
            member.Role.Should().Be(TenantRole.Manager);
            memberId = member.Id;
        }

        var manager = await NewMemberPageAsync();
        await LoginAsync(manager, managerEmail);

        // The chrome: no "Team" in the top bar, the role is "Manager"; the dashboard has no team tile
        await GotoInteractiveAsync(manager, "/dashboard");
        await Assertions.Expect(NavEntry(manager, "top-nav", "menus")).ToBeVisibleAsync();
        await Assertions.Expect(NavEntry(manager, "top-nav", "qrcodes")).ToBeVisibleAsync();
        await Assertions.Expect(NavEntry(manager, "top-nav", "analytics")).ToBeVisibleAsync();
        await Assertions.Expect(NavEntry(manager, "top-nav", "restaurant")).ToBeVisibleAsync();
        await Assertions.Expect(NavEntry(manager, "top-nav", "team")).ToHaveCountAsync(0);
        await Assertions.Expect(manager.GetByTestId("user-role")).ToHaveTextAsync("Manager");
        await Assertions.Expect(manager.GetByTestId("stat-menus")).ToBeVisibleAsync();
        await Assertions.Expect(manager.GetByTestId("stat-team")).ToHaveCountAsync(0);

        // Nor in the phone drawer
        await manager.SetViewportSizeAsync(390, 844);
        await manager.GetByTestId("nav-drawer-button").ClickAsync();
        await Assertions.Expect(NavEntry(manager, "drawer-nav", "menus")).ToBeVisibleAsync();
        await Assertions.Expect(NavEntry(manager, "drawer-nav", "team")).ToHaveCountAsync(0);
        await manager.GetByTestId("nav-drawer-close").ClickAsync();
        await manager.SetViewportSizeAsync(1280, 900);

        // The team pages are the owner's
        await ExpectAccessDeniedAsync(manager, "/users");
        await Assertions.Expect(manager.GetByText("Erika Managerin")).ToHaveCountAsync(0);
        await ExpectAccessDeniedAsync(manager, "/users/invite");
        await ExpectAccessDeniedAsync(manager, $"/users/edit/{memberId}");

        // The menu work is the manager's: create buttons, and an item edited and saved
        await GotoInteractiveAsync(manager, "/menus");
        await Assertions.Expect(MenuCardLink(manager, "Team Menü")).ToBeVisibleAsync();
        await Assertions.Expect(manager.GetByRole(AriaRole.Button, new() { Name = "Create New Menu" })).ToBeVisibleAsync();

        await GotoInteractiveAsync(manager, $"/MenuItems/{menuId}");
        await manager.GetByRole(AriaRole.Link, new() { Name = "Edit Bruschetta", Exact = true }).ClickAsync();
        var editor = manager.GetByTestId("item-edit-form");
        await Assertions.Expect(editor).ToBeVisibleAsync();
        await Assertions.Expect(manager.GetByTestId("item-read-only")).ToHaveCountAsync(0);
        await editor.Locator("#MenuItem\\.Name").FillAsync("Bruschetta della casa");
        await editor.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await Assertions.Expect(manager).ToHaveURLAsync(new Regex("/MenuItems/Category/.+\\?highlight=", RegexOptions.IgnoreCase));
        await Assertions.Expect(manager.Locator("td", new() { HasTextString = "Bruschetta della casa" }).First).ToBeVisibleAsync();
        await using (var db = App.CreateDbContext(ownerId))
            (await db.MenuItems.SingleAsync()).Name.Should().Be("Bruschetta della casa");
    }

    [Fact]
    public async Task Manager_WorksWithQrCodes_TheRestaurantProfile_AndAnalytics()
    {
        var ownerEmail = await RegisterNewOwnerAsync();
        var ownerId = await OwnerIdAsync(ownerEmail);
        var menuId = await CreateMenuViaUiAsync("QR Menü");
        await CreateDirectQrViaUiAsync("Tisch 1", "QR Menü");
        var member = await SeedTeamMemberAsync(ownerId, "Paul Manager");
        var manager = await NewMemberPageAsync();
        await LoginAsync(manager, member.Email);

        // QR codes: create and edit (no read-only "View")
        await GotoInteractiveAsync(manager, "/qrcodes");
        var card = manager.GetByTestId("qr-card").Filter(new() { HasText = "Tisch 1" });
        await Assertions.Expect(card.GetByRole(AriaRole.Button, new() { Name = "View" })).ToHaveCountAsync(0);
        await card.GetByRole(AriaRole.Button, new() { Name = "Edit" }).ClickAsync();
        await Assertions.Expect(manager.Locator("#qrTitle")).ToBeEnabledAsync();
        await Assertions.Expect(manager.GetByTestId("qr-readonly-notice")).ToHaveCountAsync(0);
        await manager.Locator("#qrTitle").FillAsync("Tisch 1 (Terrasse)");
        await manager.GetByRole(AriaRole.Button, new() { Name = "Save Changes" }).ClickAsync();
        await Assertions.Expect(manager).ToHaveURLAsync(new Regex("/qrcodes$"));
        await Assertions.Expect(manager.GetByTestId("qr-card").Filter(new() { HasText = "Tisch 1 (Terrasse)" })).ToBeVisibleAsync();

        // The restaurant profile is the tenant's: the manager creates it, the owner's data has it
        await GotoInteractiveAsync(manager, "/restaurant");
        await Assertions.Expect(manager.GetByTestId("profile-read-only")).ToHaveCountAsync(0);
        await manager.Locator("#Restaurant\\.Name").FillAsync("Gasthaus des Teams");
        await manager.GetByRole(AriaRole.Button, new() { Name = "Save profile", Exact = true }).ClickAsync();
        await Assertions.Expect(manager.GetByTestId("profile-saved")).ToBeVisibleAsync();
        await using (var db = App.CreateDbContext(ownerId))
            (await db.RestaurantProfiles.SingleAsync()).Name.Should().Be("Gasthaus des Teams");

        // Analytics
        await GotoInteractiveAsync(manager, "/analytics");
        await Assertions.Expect(manager).ToHaveURLAsync(new Regex("/analytics$"));
        await Assertions.Expect(manager.GetByTestId("summary-views")).ToBeVisibleAsync();

        // The menu settings (language versions included) open for editing
        await GotoInteractiveAsync(manager, $"/menu/edit/{menuId}");
        await Assertions.Expect(manager.Locator("#menuName")).ToBeEnabledAsync();
    }

    /// <summary>
    /// "Deactivate" takes every permission at once: an open session is turned away on its next page, a new sign-in lands
    /// on the access-denied page, and "Activate" gives the access back.
    /// </summary>
    [Fact]
    public async Task DeactivatedMember_IsDeniedEverything_UntilActivatedAgain()
    {
        var ownerEmail = await RegisterNewOwnerAsync();
        var ownerId = await OwnerIdAsync(ownerEmail);
        var menuId = await CreateMenuViaUiAsync("Gesperrtes Menü");
        await CreateCategoryViaUiAsync(menuId, "Suppen");
        await CreateMenuItemViaUiAsync(menuId, "Gulaschsuppe", "Scharf", 6.90m);
        var qrId = await CreateDirectQrViaUiAsync("Tisch 9", "Gesperrtes Menü");
        Guid categoryId, itemId;
        await using (var db = App.CreateDbContext(ownerId))
        {
            categoryId = (await db.Categories.SingleAsync()).Id;
            itemId = (await db.MenuItems.SingleAsync()).Id;
        }
        var member = await SeedTeamMemberAsync(ownerId, "Lena Später");

        var memberPage = await NewMemberPageAsync();
        await LoginAsync(memberPage, member.Email);
        await GotoInteractiveAsync(memberPage, "/menus");
        await Assertions.Expect(MenuCardLink(memberPage, "Gesperrtes Menü")).ToBeVisibleAsync();

        // The owner deactivates the member on the edit page
        await GotoInteractiveAsync($"/users/edit/{member.TenantUserId}");
        await Page.GetByTestId("member-toggle-active").ClickAsync();
        await Page.Locator("#confirmBtn").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("member-access").GetByTestId("status-badge")).ToHaveTextAsync("Inactive");

        // Every page of the tenant is denied now (the session is still signed in)
        foreach (var url in new[]
                 {
                     "/dashboard", "/menus", $"/menudetail/{menuId}", $"/Categories/{menuId}", $"/MenuItems/{menuId}",
                     $"/MenuItems/Category/{categoryId}", $"/MenuItems/item/{itemId}/edit", $"/MenuItems/Category/{categoryId}/new",
                     $"/allergens/{menuId}", $"/combos/{menuId}", $"/menu/edit/{menuId}", "/menu/edit", $"/menu/preview/{menuId}",
                     "/qrcodes", $"/qrcode/edit/{qrId}", "/qrcode/edit", "/analytics", "/restaurant", "/users", "/users/invite"
                 })
        {
            await ExpectAccessDeniedAsync(memberPage, url);
        }
        await Assertions.Expect(memberPage.GetByText("Gesperrtes Menü")).ToHaveCountAsync(0);

        // A new sign-in ends on the access-denied page as well (the dashboard behind the login)
        await memberPage.Context.ClearCookiesAsync();
        await memberPage.GotoAsync("/Account/Login");
        await memberPage.Locator("#Input\\.Email").FillAsync(member.Email);
        await memberPage.Locator("#Input\\.Password").FillAsync(Password);
        await memberPage.GetByTestId("login-submit").ClickAsync();
        await Assertions.Expect(memberPage).ToHaveURLAsync(new Regex("/Account/AccessDenied$"));

        // Activated again, the member works as before
        await Page.GetByTestId("member-toggle-active").ClickAsync();
        await Page.Locator("#confirmBtn").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("member-access").GetByTestId("status-badge")).ToHaveTextAsync("Active");
        await GotoInteractiveAsync(memberPage, "/menus");
        await Assertions.Expect(MenuCardLink(memberPage, "Gesperrtes Menü")).ToBeVisibleAsync();
    }
}
