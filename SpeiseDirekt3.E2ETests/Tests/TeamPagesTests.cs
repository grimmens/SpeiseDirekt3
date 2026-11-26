using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// The team pages of AP14 (two roles): <c>/users</c> lists the owner first ("Owner"/"Besitzer", "You"/"Sie") and then the
/// managers, with one sentence on the roles instead of the old role legend; <c>/users/invite</c> creates a manager
/// without a role picker and keeps the free tier's member limit; <c>/users/edit/{id}</c> changes the display name,
/// deactivates/activates and removes a member — no permission matrix. English and German.
/// </summary>
public class TeamPagesTests : BackOfficeTestBase
{
    public TeamPagesTests(E2EFixture fixture) : base(fixture) { }

    private readonly List<IBrowserContext> _contexts = [];

    public new async Task DisposeAsync()
    {
        foreach (var context in _contexts)
            await context.DisposeAsync();
        await base.DisposeAsync();
    }

    private async Task<IPage> NewPageAsync(string? locale = null)
    {
        var (context, page) = await NewContextAsync(locale);
        _contexts.Add(context);
        return page;
    }

    private static ILocator OwnerRow(IPage page) => page.GetByTestId("owner-row");
    private static ILocator MemberRow(IPage page, string name) => page.GetByTestId("user-row").Filter(new() { HasText = name });
    private ILocator Confirmation => Page.GetByTestId("confirmation");

    [Fact]
    public async Task TeamPage_ListsTheOwnerFirst_ThenTheManagers_WithOneSentenceOnTheRoles()
    {
        var ownerEmail = await RegisterNewOwnerAsync();
        var ownerId = await OwnerIdAsync(ownerEmail);
        var max = await SeedTeamMemberAsync(ownerId, "Max Kellner");
        await SeedTeamMemberAsync(ownerId, "Anna Berger", active: false);

        await GotoInteractiveAsync("/users");
        await Assertions.Expect(Page.GetByTestId("role-legend")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("team-roles-note")).ToHaveTextAsync(
            "Managers can edit menus, dishes, QR codes and the restaurant profile and see the analytics. Only the owner manages the team.");

        // The owner first: e-mail, "You", "Owner", active, nothing to edit
        var rows = Page.GetByTestId("users-table").Locator("tbody tr");
        await Assertions.Expect(rows).ToHaveCountAsync(3);
        await Assertions.Expect(rows.First).ToHaveAttributeAsync("data-testid", "owner-row");
        var owner = OwnerRow(Page);
        await Assertions.Expect(owner).ToContainTextAsync(ownerEmail);
        await Assertions.Expect(owner.GetByTestId("owner-you")).ToHaveTextAsync("You");
        await Assertions.Expect(owner.GetByTestId("role-badge")).ToHaveTextAsync("Owner");
        await Assertions.Expect(owner.GetByTestId("status-badge")).ToHaveTextAsync("Active");
        await Assertions.Expect(owner.GetByRole(AriaRole.Link)).ToHaveCountAsync(0);
        await Assertions.Expect(owner.GetByRole(AriaRole.Button)).ToHaveCountAsync(0);

        // Then the managers (by name), each with "Manager", the status and "Edit"
        await Assertions.Expect(rows.Nth(1)).ToContainTextAsync("Anna Berger");
        await Assertions.Expect(rows.Nth(2)).ToContainTextAsync("Max Kellner");
        await Assertions.Expect(MemberRow(Page, "Anna Berger").GetByTestId("role-badge")).ToHaveTextAsync("Manager");
        await Assertions.Expect(MemberRow(Page, "Anna Berger").GetByTestId("status-badge")).ToHaveTextAsync("Inactive");
        await Assertions.Expect(MemberRow(Page, "Max Kellner").GetByTestId("role-badge")).ToHaveTextAsync("Manager");
        await Assertions.Expect(MemberRow(Page, "Max Kellner").GetByTestId("status-badge")).ToHaveTextAsync("Active");
        await Assertions.Expect(MemberRow(Page, "Max Kellner")).ToContainTextAsync(max.Email);
        await Assertions.Expect(MemberRow(Page, "Max Kellner").GetByRole(AriaRole.Link, new() { Name = "Edit Max Kellner" }))
            .ToHaveAttributeAsync("href", $"/users/edit/{max.TenantUserId}");
        await Assertions.Expect(Page.GetByTestId("team-empty")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task TeamPage_WithoutManagers_ShowsTheOwner_AndInvitesTheFirstOne()
    {
        var ownerEmail = await RegisterNewOwnerAsync();

        await GotoInteractiveAsync("/users");
        await Assertions.Expect(OwnerRow(Page)).ToContainTextAsync(ownerEmail);
        await Assertions.Expect(Page.GetByTestId("user-row")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("team-empty")).ToContainTextAsync("No managers yet");
        await Page.GetByTestId("invite-link").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/users/invite$"));
    }

    [Fact]
    public async Task Invite_CreatesAManager_WithoutARolePicker()
    {
        var ownerId = await OwnerIdAsync(await RegisterNewOwnerAsync());
        var email = UniqueEmail("neu");

        await GotoInteractiveAsync("/users/invite");
        await Assertions.Expect(Page.Locator("select")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("invite-role")).ToContainTextAsync("Role: Manager");
        await Assertions.Expect(Page.GetByTestId("invite-role")).ToContainTextAsync("Only the owner manages the team.");
        await Page.GetByPlaceholder("employee@example.com").FillAsync(email);
        await Page.GetByPlaceholder("John Doe").FillAsync("  Nina Neu  ");
        await Page.GetByPlaceholder("Min. 8 characters").FillAsync(Password);
        await Button("Create Account").ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/users$"));
        await Assertions.Expect(MemberRow(Page, "Nina Neu").GetByTestId("role-badge")).ToHaveTextAsync("Manager");
        await using var db = App.CreateDbContext(ownerId);
        var member = await db.TenantUsers.Include(tu => tu.ApplicationUser).SingleAsync(tu => tu.TenantOwnerId == ownerId.ToString());
        member.Role.Should().Be(TenantRole.Manager);
        member.Permissions.Should().Be(Permission.None);
        member.IsActive.Should().BeTrue();
        member.DisplayName.Should().Be("Nina Neu");
        member.ApplicationUser!.Email.Should().Be(email);
        member.ApplicationUser.TenantOwnerId.Should().Be(ownerId.ToString());
    }

    [Fact]
    public async Task Invite_KeepsTheSubscriptionsMemberLimit_AlsoForAPaidTenant()
    {
        // The registered owner is paid (PaidTenant); TenantSubscription.MaxUsers limits the team all the same
        var ownerId = await OwnerIdAsync(await RegisterNewOwnerAsync());
        await App.SeedAsync(ownerId, db =>
        {
            db.TenantSubscriptions.Add(new TenantSubscription { TenantId = ownerId.ToString(), MaxUsers = 1 });
            return Task.CompletedTask;
        });
        await SeedTeamMemberAsync(ownerId, "Max Kellner");

        await GotoInteractiveAsync("/users/invite");
        await Page.GetByPlaceholder("employee@example.com").FillAsync(UniqueEmail("zuviel"));
        await Page.GetByPlaceholder("John Doe").FillAsync("Zu Viel");
        await Page.GetByPlaceholder("Min. 8 characters").FillAsync(Password);
        await Button("Create Account").ClickAsync();

        await Assertions.Expect(Page.GetByText("User limit reached (1). Upgrade your plan to add more users.")).ToBeVisibleAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/users/invite$"));
        await using var db = App.CreateDbContext(ownerId);
        (await db.TenantUsers.CountAsync(tu => tu.TenantOwnerId == ownerId.ToString())).Should().Be(1);
    }

    [Fact]
    public async Task EditMember_ChangesTheDisplayName_AndDeactivatesAndActivates()
    {
        var ownerId = await OwnerIdAsync(await RegisterNewOwnerAsync());
        var max = await SeedTeamMemberAsync(ownerId, "Max Kellner");

        await GotoInteractiveAsync("/users");
        await MemberRow(Page, "Max Kellner").GetByRole(AriaRole.Link, new() { Name = "Edit Max Kellner" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/users/edit/{max.TenantUserId}$"));
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Edit Max Kellner", Level = 1 })).ToBeVisibleAsync();

        // No permission matrix and no role picker: the role is shown, not chosen
        await Assertions.Expect(Page.Locator("input[type=checkbox]")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("button[aria-pressed]")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("member-form").GetByTestId("role-badge")).ToHaveTextAsync("Manager");
        await Assertions.Expect(Page.GetByTestId("member-email")).ToHaveTextAsync(max.Email);

        // The display name: required, then saved (trimmed)
        var name = Page.Locator("#Member\\.DisplayName");
        await Assertions.Expect(name).ToHaveValueAsync("Max Kellner");
        await name.FillAsync("   ");
        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page.GetByText("Please enter a display name.")).ToBeVisibleAsync();
        await name.FillAsync("  Max K.  ");
        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/users$"));
        await Assertions.Expect(MemberRow(Page, "Max K.")).ToBeVisibleAsync();

        // Deactivate (asks first), then activate again
        await GotoInteractiveAsync($"/users/edit/{max.TenantUserId}");
        var access = Page.GetByTestId("member-access");
        await Assertions.Expect(access.GetByTestId("status-badge")).ToHaveTextAsync("Active");
        await access.GetByTestId("member-toggle-active").ClickAsync();
        await Assertions.Expect(Confirmation).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("confirmation-message")).ToHaveTextAsync("Deactivate Max K.?");
        await Page.Locator("#confirmBtn").ClickAsync();
        await Assertions.Expect(access.GetByTestId("status-badge")).ToHaveTextAsync("Inactive");
        await Assertions.Expect(access).ToContainTextAsync("has no access any more");
        await using (var db = App.CreateDbContext(ownerId))
            (await db.TenantUsers.SingleAsync(tu => tu.Id == max.TenantUserId)).IsActive.Should().BeFalse();

        await GotoInteractiveAsync("/users");
        await Assertions.Expect(MemberRow(Page, "Max K.").GetByTestId("status-badge")).ToHaveTextAsync("Inactive");
        await GotoInteractiveAsync($"/users/edit/{max.TenantUserId}");
        await access.GetByTestId("member-toggle-active").ClickAsync();
        await Page.Locator("#confirmBtn").ClickAsync();
        await Assertions.Expect(access.GetByTestId("status-badge")).ToHaveTextAsync("Active");
    }

    [Fact]
    public async Task EditMember_Remove_DeletesTheMember_AndTheirLogin()
    {
        var ownerId = await OwnerIdAsync(await RegisterNewOwnerAsync());
        await CreateMenuViaUiAsync("Karte des Teams");
        var max = await SeedTeamMemberAsync(ownerId, "Max Kellner");
        var memberPage = await NewPageAsync();
        await LoginAsync(memberPage, max.Email);
        await GotoInteractiveAsync(memberPage, "/menus");
        await Assertions.Expect(MenuCardLink(memberPage, "Karte des Teams")).ToBeVisibleAsync();

        await GotoInteractiveAsync($"/users/edit/{max.TenantUserId}");
        await Page.GetByTestId("member-remove").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("confirmation-message"))
            .ToHaveTextAsync("Remove Max Kellner from the team? The login is deleted. This cannot be undone.");
        // Cancel keeps the member
        await Page.Locator("#cancelBtn").ClickAsync();
        await Assertions.Expect(Confirmation).ToBeHiddenAsync();
        await Page.GetByTestId("member-remove").ClickAsync();
        await Page.Locator("#confirmBtn").ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/users$"));
        await Assertions.Expect(Page.GetByTestId("user-row")).ToHaveCountAsync(0);
        await using (var db = App.CreateDbContext(ownerId))
        {
            (await db.TenantUsers.AnyAsync(tu => tu.Id == max.TenantUserId)).Should().BeFalse();
            (await db.Users.AnyAsync(u => u.Id == max.AccountId)).Should().BeFalse();
        }

        // The open session of the removed member sees nothing any more, and the login is gone
        await memberPage.GotoAsync("/menus");
        await Assertions.Expect(memberPage).ToHaveURLAsync(new Regex("/Account/AccessDenied$"));
        await memberPage.Context.ClearCookiesAsync();
        await memberPage.GotoAsync("/Account/Login");
        await memberPage.Locator("#Input\\.Email").FillAsync(max.Email);
        await memberPage.Locator("#Input\\.Password").FillAsync(Password);
        await memberPage.GetByTestId("login-submit").ClickAsync();
        await Assertions.Expect(memberPage.GetByText("Invalid login attempt. Please check your e-mail address and password.")).ToBeVisibleAsync();
        await Assertions.Expect(memberPage).ToHaveURLAsync(new Regex("/Account/Login"));
    }

    [Fact]
    public async Task EditMember_OfAnotherTenant_OrUnknown_IsNotFound()
    {
        var otherOwner = Guid.NewGuid();
        await App.SeedAsync(otherOwner, db =>
        {
            db.Users.Add(new SpeiseDirekt.Data.ApplicationUser { Id = otherOwner.ToString(), UserName = UniqueEmail("other"), SecurityStamp = Guid.NewGuid().ToString() });
            return Task.CompletedTask;
        });
        var foreign = await SeedTeamMemberAsync(otherOwner, "Fremde Person");
        await RegisterNewOwnerAsync();

        await GotoInteractiveAsync($"/users/edit/{foreign.TenantUserId}");
        await Assertions.Expect(Page.GetByTestId("member-not-found")).ToContainTextAsync("Team member not found.");
        await Assertions.Expect(Page.GetByText("Fremde Person")).ToHaveCountAsync(0);
        await GotoInteractiveAsync($"/users/edit/{Guid.NewGuid()}");
        await Assertions.Expect(Page.GetByTestId("member-not-found")).ToBeVisibleAsync();
    }

    /// <summary>The team pages and a manager's chrome in German (formal "Sie").</summary>
    [Fact]
    public async Task TeamPages_AndTheManagersNavigation_InGerman()
    {
        var page = await NewPageAsync(E2ESettings.GermanLocale);
        var ownerEmail = UniqueEmail("besitzer");
        await RegisterAsync(page, ownerEmail);
        var ownerId = await OwnerIdAsync(ownerEmail);
        var max = await SeedTeamMemberAsync(ownerId, "Max Kellner");

        await GotoInteractiveAsync(page, "/users");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Teamverwaltung", Level = 1 })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("team-roles-note")).ToHaveTextAsync(
            "Manager können Speisekarten, Gerichte, QR-Codes und das Restaurantprofil bearbeiten und die Statistik sehen. Das Team verwaltet nur der Besitzer.");
        await Assertions.Expect(OwnerRow(page).GetByTestId("role-badge")).ToHaveTextAsync("Besitzer");
        await Assertions.Expect(OwnerRow(page).GetByTestId("owner-you")).ToHaveTextAsync("Sie");
        await Assertions.Expect(MemberRow(page, "Max Kellner").GetByTestId("role-badge")).ToHaveTextAsync("Manager");
        await Assertions.Expect(MemberRow(page, "Max Kellner").GetByTestId("status-badge")).ToHaveTextAsync("Aktiv");
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Teammitglied einladen" })).ToBeVisibleAsync();

        await GotoInteractiveAsync(page, "/users/invite");
        await Assertions.Expect(page.GetByTestId("invite-role")).ToContainTextAsync("Rolle: Manager");

        await GotoInteractiveAsync(page, $"/users/edit/{max.TenantUserId}");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Max Kellner bearbeiten", Level = 1 })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Zugang" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("member-toggle-active")).ToHaveTextAsync("Deaktivieren");
        await Assertions.Expect(page.GetByTestId("member-remove")).ToHaveTextAsync("Entfernen");
        await page.GetByTestId("member-remove").ClickAsync();
        await Assertions.Expect(page.GetByTestId("confirmation-message"))
            .ToHaveTextAsync("Max Kellner aus dem Team entfernen? Der Zugang wird gelöscht. Das lässt sich nicht rückgängig machen.");
        await page.Locator("#cancelBtn").ClickAsync();

        // The manager's chrome: no "Team", the role "Manager"; the team page is denied
        var manager = await NewPageAsync(E2ESettings.GermanLocale);
        await LoginAsync(manager, max.Email);
        await GotoInteractiveAsync(manager, "/dashboard");
        await Assertions.Expect(manager.GetByTestId("top-nav").Locator("a"))
            .ToHaveTextAsync(["Dashboard", "Speisekarten", "QR-Codes", "Statistik", "Restaurant"]);
        await Assertions.Expect(manager.GetByTestId("user-role")).ToHaveTextAsync("Manager");
        await manager.GotoAsync("/users");
        await Assertions.Expect(manager).ToHaveURLAsync(new Regex("/Account/AccessDenied$"));
        await Assertions.Expect(manager.GetByRole(AriaRole.Heading, new() { Name = "Kein Zugriff", Level = 1 })).ToBeVisibleAsync();
    }
}
