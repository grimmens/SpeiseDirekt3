using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// The global admin (spec "global admin and paid tenants", stage 1): <c>admin@e2e.test</c> — the E2E app's only entry of
/// <c>Admin:GlobalAdminEmails</c> — sees "Admin" in the navigation and <c>/admin/kunden</c> with every customer, searches
/// them, opens a team (read-only) and switches "Paid", which counts at once for the customer's signed-in owner and
/// members; a refused switch turns back with a message. Nobody else sees the entry or any customer data: a non-admin
/// owner and a member get <c>/Account/AccessDenied</c>, an anonymous visitor the login page. Phone: cards, nothing wider
/// than the screen.
/// </summary>
public class GlobalAdminTests : QrCalendarTestBase
{
    public GlobalAdminTests(E2EFixture fixture) : base(fixture) { }

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

    private const string MenuHint = "The free plan includes one menu. For more, please contact us.";

    private static ILocator Table(IPage page) => page.GetByTestId("admin-table");

    /// <summary>The customer's row in the table (1024 px and wider).</summary>
    private static ILocator Row(IPage page, Guid ownerId) =>
        Table(page).Locator($"[data-testid='admin-tenant-row'][data-owner-id='{ownerId}']");

    /// <summary>The customer's card (below 1024 px).</summary>
    private static ILocator Card(IPage page, Guid ownerId) =>
        page.GetByTestId("admin-cards").Locator($"[data-testid='admin-tenant-card'][data-owner-id='{ownerId}']");

    private static ILocator AdminEntry(IPage page, string nav) => page.GetByTestId(nav).GetByTestId("nav-admin");

    /// <summary>Signs the global admin in (in <paramref name="page"/>) and opens the customer list through "Admin".</summary>
    private static async Task OpenCustomersAsAdminAsync(IPage page)
    {
        await LoginAsync(page, E2ESeedData.AdminEmail);
        await GotoInteractiveAsync(page, "/admin/kunden");
        await Assertions.Expect(page.GetByTestId("admin-page")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("admin-count")).ToBeVisibleAsync();
    }

    /// <summary>Types into the search field (debounced) and waits until exactly <paramref name="count"/> customers show.</summary>
    private static async Task SearchAsync(IPage page, string term, int count)
    {
        await page.GetByTestId("admin-search").FillAsync(term);
        await Assertions.Expect(Table(page).GetByTestId("admin-tenant-row")).ToHaveCountAsync(count);
    }

    /// <summary>
    /// A customer straight in the database: an owner account (free unless <paramref name="paid"/>), optionally with a
    /// restaurant profile, <paramref name="menus"/> menus (each with a QR code while <paramref name="qrCodes"/> lasts) and
    /// one guest view on <paramref name="viewedAt"/> (UTC).
    /// </summary>
    private async Task<SeededOwner> SeedCustomerAsync(string? email = null, string? restaurant = null, int menus = 0, int qrCodes = 0,
        DateTime? viewedAt = null, bool paid = false)
    {
        var owner = await SeedOwnerAsync(email, paid);
        if (restaurant is not null)
            await App.SeedAsync(owner.OwnerId, db =>
            {
                db.RestaurantProfiles.Add(new RestaurantProfile { Id = Guid.NewGuid(), Name = restaurant });
                return Task.CompletedTask;
            });
        for (var i = 0; i < menus; i++)
        {
            var menuId = await SeedMenuAsync(owner.OwnerId, $"Karte {i + 1}");
            if (i < qrCodes)
                await SeedQrAsync(owner.OwnerId, new QRCode { Title = $"Tisch {i + 1}", MenuId = menuId });
            if (i == 0 && viewedAt is { } at)
                await App.SeedAsync(owner.OwnerId, db =>
                {
                    db.MenuViews.Add(new MenuView { Id = Guid.NewGuid(), SessionId = "0123456789abcdef", MenuId = menuId, ViewedAt = at });
                    return Task.CompletedTask;
                });
        }
        return owner;
    }

    /// <summary>Elements (and the document) wider than the viewport.</summary>
    private static Task<string[]> WideElementsAsync(IPage page) => page.EvaluateAsync<string[]>("""
        () => {
            const width = document.documentElement.clientWidth;
            const wide = [...document.querySelectorAll('body *')]
                .filter(e => e.checkVisibility() && e.getBoundingClientRect().right > width + 1)
                .map(e => e.tagName + '.' + e.className + ' ' + Math.round(e.getBoundingClientRect().right));
            if (document.documentElement.scrollWidth > width) wide.push('document ' + document.documentElement.scrollWidth);
            return wide;
        }
        """);

    [Fact]
    public void TheE2EApp_ListsOnlyTheSeededAdmin()
    {
        // The operator's address of appsettings.json is replaced, not merged
        App.Services.GetRequiredService<IOptions<AdminOptions>>().Value.GlobalAdminEmails
            .Should().Equal(E2ESeedData.AdminEmail);
        App.ServerServices.GetRequiredService<IOptions<AdminOptions>>().Value.GlobalAdminEmails
            .Should().Equal(E2ESeedData.AdminEmail);
    }

    [Fact]
    public async Task Admin_SeesTheEntry_AndEveryCustomerWithItsNumbers()
    {
        var customer = await SeedCustomerAsync(restaurant: $"Gasthaus Liste {Guid.NewGuid():N}"[..24], menus: 2, qrCodes: 1,
            viewedAt: new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc));
        await LoginAsync(Page, E2ESeedData.AdminEmail);

        // "Admin" comes last in the top bar and leads to the customer list
        await GotoInteractiveAsync("/dashboard");
        var entry = AdminEntry(Page, "top-nav");
        await Assertions.Expect(entry).ToBeVisibleAsync();
        await Assertions.Expect(entry).ToHaveTextAsync("Admin");
        await Assertions.Expect(Page.GetByTestId("top-nav").Locator("a").Last).ToHaveAttributeAsync("data-testid", "nav-admin");
        await entry.ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/admin/kunden$"));
        await Assertions.Expect(Page.GetByTestId("admin-page")).ToBeVisibleAsync();
        await Assertions.Expect(entry).ToHaveAttributeAsync("aria-current", "page");
        await Assertions.Expect(Page).ToHaveTitleAsync("Customers - Speise-Direkt");
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1, Name = "Customers" })).ToBeVisibleAsync();

        // A table at 1280 px (the cards are for phones); headers as written (CSS shows them upper case)
        await Assertions.Expect(Table(Page)).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("admin-cards")).ToBeHiddenAsync();
        await Assertions.Expect(Table(Page).Locator("thead th"))
            .ToHaveTextAsync(["Restaurant", "E-mail", "Menus", "QR codes", "Team", "Last guest view", "Paid"]);
        await Assertions.Expect(Page.GetByTestId("admin-count")).ToHaveTextAsync(new Regex(@"^\d+ customers$"));

        // The customer's numbers
        var row = Row(Page, customer.OwnerId);
        await Assertions.Expect(row.GetByTestId("tenant-restaurant")).ToHaveTextAsync(new Regex("^Gasthaus Liste "));
        await Assertions.Expect(row.GetByTestId("tenant-email")).ToHaveTextAsync(customer.Email);
        await Assertions.Expect(row.GetByTestId("tenant-menus")).ToHaveTextAsync("2");
        await Assertions.Expect(row.GetByTestId("tenant-qrcodes")).ToHaveTextAsync("1");
        await Assertions.Expect(row.GetByTestId("tenant-team")).ToHaveTextAsync("No team");
        await Assertions.Expect(row.GetByTestId("tenant-last-view")).ToHaveTextAsync("11/20/2026");
        await Assertions.Expect(row).ToHaveAttributeAsync("data-paid", "false");
        await Assertions.Expect(row.GetByTestId("paid-toggle")).ToHaveAttributeAsync("aria-checked", "false");
        await Assertions.Expect(row.GetByTestId("admin-badge")).ToHaveCountAsync(0);

        // The paid seed owner and the admin's own account (badge, no profile, no guest view yet)
        var seedOwner = Row(Page, E2ESeedData.OwnerId);
        await Assertions.Expect(seedOwner.GetByTestId("tenant-restaurant")).ToHaveTextAsync(E2ESeedData.RestaurantName);
        await Assertions.Expect(seedOwner).ToHaveAttributeAsync("data-paid", "true");
        await Assertions.Expect(seedOwner.GetByTestId("paid-toggle")).ToHaveAttributeAsync("aria-checked", "true");
        var admin = Row(Page, E2ESeedData.AdminId);
        await Assertions.Expect(admin.GetByTestId("admin-badge")).ToHaveTextAsync("Global admin");
        await Assertions.Expect(admin.GetByTestId("tenant-restaurant")).ToHaveTextAsync("No restaurant profile");
        await Assertions.Expect(admin.GetByTestId("tenant-last-view")).ToHaveTextAsync("None yet");
    }

    [Fact]
    public async Task Admin_InGerman_ColumnsDatesAndTheSwitch()
    {
        var customer = await SeedCustomerAsync(restaurant: "Wirtshaus Deutschtest", menus: 1,
            viewedAt: new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc));
        var page = await NewPageAsync(E2ESettings.GermanLocale);
        await OpenCustomersAsAdminAsync(page);

        await Assertions.Expect(page).ToHaveTitleAsync("Kunden - Speise-Direkt");
        await Assertions.Expect(AdminEntry(page, "top-nav")).ToHaveTextAsync("Admin");
        await Assertions.Expect(Table(page).Locator("thead th"))
            .ToHaveTextAsync(["Restaurant", "E-Mail", "Speisekarten", "QR-Codes", "Team", "Letzter Gästeaufruf", "Bezahlt"]);
        await Assertions.Expect(page.GetByTestId("admin-search")).ToHaveAttributeAsync("placeholder", "E-Mail oder Restaurant");
        await SearchAsync(page, customer.Email, 1);
        await Assertions.Expect(page.GetByTestId("admin-count")).ToHaveTextAsync("1 Kunde");

        var row = Row(page, customer.OwnerId);
        await Assertions.Expect(row.GetByTestId("tenant-last-view")).ToHaveTextAsync("20.11.2026");
        await Assertions.Expect(row.GetByTestId("tenant-team")).ToHaveTextAsync("Kein Team");
        await Assertions.Expect(row.GetByTestId("paid-toggle")).ToHaveAttributeAsync("aria-label", "Bezahlt: Wirtshaus Deutschtest");
        await row.GetByTestId("paid-toggle").ClickAsync();
        await Assertions.Expect(row.GetByTestId("paid-saved")).ToHaveTextAsync("Gespeichert");
        await Assertions.Expect(row.GetByTestId("paid-toggle")).ToHaveAttributeAsync("aria-checked", "true");
        (await App.IsPaidAsync(customer.OwnerId)).Should().BeTrue();

        await SearchAsync(page, "gibt-es-nicht", 0);
        await Assertions.Expect(page.GetByTestId("admin-empty")).ToContainTextAsync("Kein Kunde passt zu „gibt-es-nicht“.");
    }

    [Fact]
    public async Task Search_MatchesTheEmailOrTheRestaurant_IgnoringCase()
    {
        var tag = Guid.NewGuid().ToString("N")[..10];
        var byEmail = await SeedCustomerAsync(email: $"suche-{tag}@e2e.test");
        var byRestaurant = await SeedCustomerAsync(restaurant: $"Gasthof Zum Suchtest {tag}");
        await OpenCustomersAsAdminAsync(Page);
        await Assertions.Expect(Row(Page, byEmail.OwnerId)).ToBeVisibleAsync();
        await Assertions.Expect(Row(Page, byRestaurant.OwnerId)).ToBeVisibleAsync();

        // Part of the e-mail, in upper case
        await SearchAsync(Page, $"SUCHE-{tag.ToUpperInvariant()}", 1);
        await Assertions.Expect(Row(Page, byEmail.OwnerId)).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("admin-count")).ToHaveTextAsync("1 customer");

        // Part of the restaurant name, in lower case
        await SearchAsync(Page, $"zum suchtest {tag}", 1);
        await Assertions.Expect(Row(Page, byRestaurant.OwnerId)).ToBeVisibleAsync();

        // Both share the tag
        await SearchAsync(Page, tag, 2);

        // Nothing matches: the empty state names the search
        await Page.GetByTestId("admin-search").FillAsync($"niemand-{tag}");
        await Assertions.Expect(Page.GetByTestId("admin-empty")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("admin-empty")).ToContainTextAsync($"No customer matches “niemand-{tag}”.");
        await Assertions.Expect(Page.GetByTestId("admin-tenant-row")).ToHaveCountAsync(0);

        // Cleared: everyone again
        await Page.GetByTestId("admin-search").FillAsync("");
        await Assertions.Expect(Row(Page, byEmail.OwnerId)).ToBeVisibleAsync();
        await Assertions.Expect(Row(Page, byRestaurant.OwnerId)).ToBeVisibleAsync();
        await Assertions.Expect(Row(Page, E2ESeedData.AdminId)).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Members_OpenBelowTheCustomer_ReadOnly()
    {
        var customer = await SeedCustomerAsync(restaurant: $"Teamlokal {Guid.NewGuid():N}"[..20]);
        var active = await SeedTeamMemberAsync(customer.OwnerId, "Max Kellner");
        var inactive = await SeedTeamMemberAsync(customer.OwnerId, "Anna Berger", active: false);
        await OpenCustomersAsAdminAsync(Page);
        await SearchAsync(Page, customer.Email, 1);

        var row = Row(Page, customer.OwnerId);
        var toggle = row.GetByTestId("admin-members-toggle");
        await Assertions.Expect(toggle).ToHaveTextAsync(new Regex("1 active · 1 inactive"));
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(Table(Page).GetByTestId("admin-members-row")).ToHaveCountAsync(0);

        await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        var members = Table(Page).GetByTestId("admin-members-row").GetByTestId("admin-member");
        await Assertions.Expect(members).ToHaveCountAsync(2);
        var max = members.Filter(new() { HasText = "Max Kellner" });
        await Assertions.Expect(max).ToContainTextAsync(active.Email);
        await Assertions.Expect(max.GetByTestId("admin-member-role")).ToHaveTextAsync("Manager");
        await Assertions.Expect(max.GetByTestId("admin-member-status")).ToHaveTextAsync("Active");
        var anna = members.Filter(new() { HasText = "Anna Berger" });
        await Assertions.Expect(anna).ToContainTextAsync(inactive.Email);
        await Assertions.Expect(anna.GetByTestId("admin-member-status")).ToHaveTextAsync("Inactive");
        // The admin only looks: nothing to press or type in the team
        await Assertions.Expect(Table(Page).GetByTestId("admin-members").Locator("button, input, select, a")).ToHaveCountAsync(0);

        await toggle.ClickAsync();
        await Assertions.Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(Table(Page).GetByTestId("admin-members-row")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// The paid status is read from the database at every check, never from the sign-in cookie: the switch counts at once
    /// for the customer's owner and member who are signed in already, in both directions, and the content stays.
    /// </summary>
    [Fact]
    public async Task SwitchingPaid_CountsAtOnce_ForTheSignedInOwnerAndMember_AndTheContentStays()
    {
        var owner = await NewPageAsync();
        var ownerEmail = UniqueEmail();
        await RegisterAsync(owner, ownerEmail, paid: false);
        var ownerId = await OwnerIdAsync(ownerEmail);
        await SeedMenuAsync(ownerId, "Erste Karte");
        var member = await SeedTeamMemberAsync(ownerId, "Lena Service");
        var memberPage = await NewPageAsync();
        await LoginAsync(memberPage, member.Email);

        // Free and at the limit: owner and member are both blocked
        foreach (var page in new[] { owner, memberPage })
        {
            await GotoInteractiveAsync(page, "/menus");
            await Assertions.Expect(page.GetByTestId("menu-create-button")).ToBeDisabledAsync();
            await Assertions.Expect(page.GetByTestId("menu-limit-hint")).ToHaveTextAsync(MenuHint);
        }

        // The admin switches the customer to paid
        await OpenCustomersAsAdminAsync(Page);
        await SearchAsync(Page, ownerEmail, 1);
        var row = Row(Page, ownerId);
        var paid = row.GetByTestId("paid-toggle");
        await Assertions.Expect(paid).ToHaveAttributeAsync("role", "switch");
        await Assertions.Expect(paid).ToHaveAttributeAsync("aria-checked", "false");
        await paid.ClickAsync();
        await Assertions.Expect(row.GetByTestId("paid-saved")).ToHaveTextAsync("Saved");
        await Assertions.Expect(paid).ToHaveAttributeAsync("aria-checked", "true");
        await Assertions.Expect(row).ToHaveAttributeAsync("data-paid", "true");
        await Assertions.Expect(paid).Not.ToHaveAttributeAsync("aria-busy", "true");
        (await App.IsPaidAsync(ownerId)).Should().BeTrue();

        // Same sessions, no new sign-in: the owner creates a second menu, the member may too
        await GotoInteractiveAsync(owner, "/menus");
        await Assertions.Expect(owner.GetByTestId("menu-create-button")).ToBeEnabledAsync();
        await Assertions.Expect(owner.GetByTestId("menu-limit-hint")).ToHaveCountAsync(0);
        await owner.GetByTestId("menu-create-button").ClickAsync();
        await Assertions.Expect(owner).ToHaveURLAsync(new Regex("/menu/edit$"));
        await owner.Locator("#menuName").FillAsync("Zweite Karte");
        await owner.Locator("#menuDesc").FillAsync("Nach dem Umschalten");
        await owner.GetByTestId("menu-save-button").ClickAsync();
        await Assertions.Expect(owner).ToHaveURLAsync(new Regex("/menus$"));
        await Assertions.Expect(MenuCardLink(owner, "Zweite Karte")).ToBeVisibleAsync();
        await GotoInteractiveAsync(memberPage, "/menus");
        await Assertions.Expect(memberPage.GetByTestId("menu-create-button")).ToBeEnabledAsync();
        await GotoInteractiveAsync(memberPage, "/qrcodes");
        await Assertions.Expect(memberPage.GetByTestId("qr-create-button")).ToBeEnabledAsync();

        // Back to free: blocked again at once, both menus stay (and stay editable)
        await paid.ClickAsync();
        await Assertions.Expect(paid).ToHaveAttributeAsync("aria-checked", "false");
        await Assertions.Expect(row.GetByTestId("paid-saved")).ToHaveTextAsync("Saved");
        await Assertions.Expect(row).ToHaveAttributeAsync("data-paid", "false");
        (await App.IsPaidAsync(ownerId)).Should().BeFalse();

        await GotoInteractiveAsync(owner, "/menus");
        await Assertions.Expect(owner.GetByTestId("menu-create-button")).ToBeDisabledAsync();
        await Assertions.Expect(owner.GetByTestId("menu-limit-hint")).ToHaveTextAsync(MenuHint);
        await Assertions.Expect(MenuCardLink(owner, "Erste Karte")).ToBeVisibleAsync();
        await Assertions.Expect(MenuCardLink(owner, "Zweite Karte")).ToBeVisibleAsync();
        await GotoInteractiveAsync(owner, "/menu/edit");
        await Assertions.Expect(owner).ToHaveURLAsync(new Regex("/menus$"));
        await GotoInteractiveAsync(memberPage, "/menus");
        await Assertions.Expect(memberPage.GetByTestId("menu-create-button")).ToBeDisabledAsync();
        await GotoInteractiveAsync(memberPage, "/qrcodes");
        // No QR code yet: the free plan's one is still open
        await Assertions.Expect(memberPage.GetByTestId("qr-create-button")).ToBeEnabledAsync();

        // The list shows the new number once it loads again
        await GotoInteractiveAsync("/admin/kunden");
        await SearchAsync(Page, ownerEmail, 1);
        await Assertions.Expect(Row(Page, ownerId).GetByTestId("tenant-menus")).ToHaveTextAsync("2");
        await Assertions.Expect(Row(Page, ownerId)).ToHaveAttributeAsync("data-paid", "false");
    }

    /// <summary>
    /// <c>SetPaidAsync</c> checks the acting admin in the database: an admin who lost the role while the page was open is
    /// refused, and the switch turns back with the message.
    /// </summary>
    [Fact]
    public async Task ARefusedSwitch_TurnsBack_WithAMessage()
    {
        var customer = await SeedCustomerAsync(restaurant: $"Abgelehnt {Guid.NewGuid():N}"[..20]);
        // A second global admin for this test only, straight in the database (the startup sync never sees it)
        var temporaryAdmin = await SeedOwnerAsync(paid: false);
        await App.SetRoleAsync(temporaryAdmin.OwnerId.ToString(), AppRoles.GlobalAdmin, true);
        try
        {
            await LoginAsync(Page, temporaryAdmin.Email);
            await GotoInteractiveAsync("/admin/kunden");
            await SearchAsync(Page, customer.Email, 1);
            var row = Row(Page, customer.OwnerId);

            await App.SetRoleAsync(temporaryAdmin.OwnerId.ToString(), AppRoles.GlobalAdmin, false);
            await row.GetByTestId("paid-toggle").ClickAsync();

            await Assertions.Expect(row.GetByTestId("paid-error")).ToHaveTextAsync("Not saved: only a global admin may change this.");
            await Assertions.Expect(row.GetByTestId("paid-toggle")).ToHaveAttributeAsync("aria-checked", "false");
            await Assertions.Expect(row).ToHaveAttributeAsync("data-paid", "false");
            await Assertions.Expect(row.GetByTestId("paid-saved")).ToHaveCountAsync(0);
            (await App.IsPaidAsync(customer.OwnerId)).Should().BeFalse("nothing was saved");

            // The page itself is gone for the former admin at the next load
            await Page.GotoAsync("/admin/kunden");
            await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/Account/AccessDenied$"));
        }
        finally
        {
            await App.SetRoleAsync(temporaryAdmin.OwnerId.ToString(), AppRoles.GlobalAdmin, false);
        }
    }

    /// <summary>
    /// Asserts that <paramref name="page"/> has no admin entry and is denied <c>/admin/kunden</c> — on a full load (the
    /// server answers with a redirect, so the page never renders) and on a client-side navigation from its circuit — and
    /// that nothing of the customers reached the browser (documents and the circuit's messages).
    /// </summary>
    private async Task ExpectNoAdminAsync(IPage page, params string[] secrets)
    {
        var received = new StringBuilder();
        page.WebSocket += (_, ws) => ws.FrameReceived += (_, frame) =>
        {
            lock (received)
                received.Append(frame.Text).Append(frame.Binary is { } bytes ? Encoding.UTF8.GetString(bytes) : "");
        };

        await GotoInteractiveAsync(page, "/dashboard");
        await Assertions.Expect(page.GetByTestId("top-nav")).ToBeVisibleAsync();
        await Assertions.Expect(AdminEntry(page, "top-nav")).ToHaveCountAsync(0);
        await Assertions.Expect(page.Locator("[data-nav='admin']")).ToHaveCountAsync(0);

        // Full load: the prerender redirects before anything renders
        var response = await page.GotoAsync("/admin/kunden");
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/Account/AccessDenied$"));
        var first = response!.Request.RedirectedFrom;
        first.Should().NotBeNull("the server redirected /admin/kunden");
        first!.Url.Should().EndWith("/admin/kunden");
        (await first.ResponseAsync())!.Status.Should().Be(302);
        await Assertions.Expect(page.GetByTestId("admin-page")).ToHaveCountAsync(0);

        // From a running circuit: the page loads /Account/AccessDenied before it reads anything
        await GotoInteractiveAsync(page, "/dashboard");
        await page.EvaluateAsync("() => Blazor.navigateTo('/admin/kunden')");
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/Account/AccessDenied$"));
        await Assertions.Expect(page.GetByTestId("admin-page")).ToHaveCountAsync(0);

        var html = await page.ContentAsync();
        string circuit;
        lock (received)
            circuit = received.ToString();
        circuit.Should().Contain("enableNavigationInterception", "the circuit's messages were recorded");
        foreach (var secret in secrets)
        {
            html.Should().NotContain(secret);
            circuit.Should().NotContain(secret, "no customer data goes through the circuit");
        }

        // Nor in the phone drawer
        await page.SetViewportSizeAsync(390, 844);
        await GotoInteractiveAsync(page, "/dashboard");
        await page.GetByTestId("nav-drawer-button").ClickAsync();
        await Assertions.Expect(page.GetByTestId("drawer-nav").Locator("a").First).ToBeVisibleAsync();
        await Assertions.Expect(AdminEntry(page, "drawer-nav")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task NonAdminOwner_SeesNoEntry_AndIsDenied_WithoutCustomerData()
    {
        var restaurant = $"Geheimlokal {Guid.NewGuid():N}"[..22];
        var customer = await SeedCustomerAsync(restaurant: restaurant, menus: 1);
        await RegisterNewOwnerAsync();

        await ExpectNoAdminAsync(Page, customer.Email, restaurant, E2ESeedData.AdminEmail, E2ESeedData.OwnerEmail, E2ESeedData.RestaurantName);
    }

    [Fact]
    public async Task Member_SeesNoEntry_AndIsDenied_WithoutCustomerData()
    {
        var restaurant = $"Fremdlokal {Guid.NewGuid():N}"[..21];
        var customer = await SeedCustomerAsync(restaurant: restaurant);
        var employer = await SeedOwnerAsync();
        var member = await SeedTeamMemberAsync(employer.OwnerId, "Tom Theke");
        await LoginAsync(Page, member.Email);

        await ExpectNoAdminAsync(Page, customer.Email, restaurant, E2ESeedData.AdminEmail, E2ESeedData.OwnerEmail);
    }

    [Fact]
    public async Task Anonymous_GoesToTheLoginPage()
    {
        await Page.GotoAsync("/admin/kunden");
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/Account/Login\\?ReturnUrl=%2Fadmin%2Fkunden$", RegexOptions.IgnoreCase));
        await Assertions.Expect(Page.GetByTestId("admin-page")).ToHaveCountAsync(0);
        (await Page.ContentAsync()).Should().NotContain(E2ESeedData.OwnerEmail).And.NotContain(E2ESeedData.AdminEmail);

        // Signed in from there, the admin lands on the list
        await Page.Locator("#Input\\.Email").FillAsync(E2ESeedData.AdminEmail);
        await Page.Locator("#Input\\.Password").FillAsync(Password);
        await WaitForInteractiveAsync(Page, () => Page.GetByTestId("login-submit").ClickAsync());
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/admin/kunden$"));
        await Assertions.Expect(Page.GetByTestId("admin-page")).ToBeVisibleAsync();
    }

    /// <summary>The role is read from the database when a page loads: granted or taken, it counts without a new sign-in.</summary>
    [Fact]
    public async Task TheAdminEntry_FollowsTheRole_WithoutANewSignIn()
    {
        var owner = await SeedOwnerAsync();
        await LoginAsync(Page, owner.Email);
        await GotoInteractiveAsync("/dashboard");
        await Assertions.Expect(AdminEntry(Page, "top-nav")).ToHaveCountAsync(0);

        await App.SetRoleAsync(owner.OwnerId.ToString(), AppRoles.GlobalAdmin, true);
        try
        {
            await GotoInteractiveAsync("/dashboard");
            await Assertions.Expect(AdminEntry(Page, "top-nav")).ToBeVisibleAsync();
            await GotoInteractiveAsync("/admin/kunden");
            await Assertions.Expect(Page.GetByTestId("admin-page")).ToBeVisibleAsync();
            await Assertions.Expect(Row(Page, owner.OwnerId).GetByTestId("admin-badge")).ToHaveTextAsync("Global admin");
        }
        finally
        {
            await App.SetRoleAsync(owner.OwnerId.ToString(), AppRoles.GlobalAdmin, false);
        }

        await GotoInteractiveAsync("/dashboard");
        await Assertions.Expect(AdminEntry(Page, "top-nav")).ToHaveCountAsync(0);
        await Page.GotoAsync("/admin/kunden");
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/Account/AccessDenied$"));
    }

    [Fact]
    public async Task OnAPhone_CardsAndTheDrawerEntry_WithoutSidewaysScrolling()
    {
        var customer = await SeedCustomerAsync(email: $"ein-sehr-langer-adressname-{Guid.NewGuid():N}@e2e.test",
            restaurant: "Gasthaus Zur Langen Speisekarte am Marktplatz", menus: 1, qrCodes: 1,
            viewedAt: new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc));
        await SeedTeamMemberAsync(customer.OwnerId, "Maximilian Langername-Mitbindestrich");
        await LoginAsync(Page, E2ESeedData.AdminEmail);
        await Page.SetViewportSizeAsync(390, 844);

        // "Admin" in the drawer, current on the page
        await GotoInteractiveAsync("/dashboard");
        await Page.GetByTestId("nav-drawer-button").ClickAsync();
        var entry = AdminEntry(Page, "drawer-nav");
        await Assertions.Expect(entry).ToBeVisibleAsync();
        await entry.ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/admin/kunden$"));
        await Assertions.Expect(Page.GetByTestId("admin-page")).ToBeVisibleAsync();

        // Cards instead of the table
        await Assertions.Expect(Page.GetByTestId("admin-cards")).ToBeVisibleAsync();
        await Assertions.Expect(Table(Page)).ToBeHiddenAsync();
        await SearchCardsAsync(customer.Email);
        var card = Card(Page, customer.OwnerId);
        await Assertions.Expect(card.GetByTestId("tenant-restaurant")).ToHaveTextAsync("Gasthaus Zur Langen Speisekarte am Marktplatz");
        await Assertions.Expect(card.GetByTestId("tenant-email")).ToHaveTextAsync(customer.Email);
        await Assertions.Expect(card.GetByTestId("tenant-menus")).ToHaveTextAsync("1");
        await Assertions.Expect(card.GetByTestId("tenant-qrcodes")).ToHaveTextAsync("1");
        await Assertions.Expect(card.GetByTestId("tenant-last-view")).ToHaveTextAsync("11/20/2026");

        // The team opens inside the card, the switch works there
        await card.GetByTestId("admin-members-toggle").ClickAsync();
        await Assertions.Expect(card.GetByTestId("admin-member")).ToContainTextAsync("Maximilian Langername-Mitbindestrich");
        await card.GetByTestId("paid-toggle").ClickAsync();
        await Assertions.Expect(card.GetByTestId("paid-saved")).ToHaveTextAsync("Saved");
        await Assertions.Expect(card.GetByTestId("paid-toggle")).ToHaveAttributeAsync("aria-checked", "true");
        (await App.IsPaidAsync(customer.OwnerId)).Should().BeTrue();

        // Nothing wider than the phone, with the team open, and with the whole list
        await Page.EvaluateAsync("() => document.fonts.ready");
        (await WideElementsAsync(Page)).Should().BeEmpty("nothing is wider than a 390 px phone");
        await Page.GetByTestId("admin-search").FillAsync("");
        await Assertions.Expect(Card(Page, E2ESeedData.AdminId)).ToBeVisibleAsync();
        (await WideElementsAsync(Page)).Should().BeEmpty("nothing is wider than a 390 px phone");
        // The page sits in MainLayout's 16 px phone gutter, with none of its own
        var edges = await Page.GetByTestId("admin-page").EvaluateAsync<double[]>(
            "e => { const r = e.getBoundingClientRect(); return [r.left, document.documentElement.clientWidth - r.right]; }");
        edges.Should().Equal(16, 16);
    }

    private async Task SearchCardsAsync(string term)
    {
        await Page.GetByTestId("admin-search").FillAsync(term);
        await Assertions.Expect(Page.GetByTestId("admin-cards").GetByTestId("admin-tenant-card")).ToHaveCountAsync(1);
    }
}
