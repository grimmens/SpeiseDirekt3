using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// The free plan in the back office (spec "global admin and paid tenants", stage 1): an owner without the role
/// <c>PaidTenant</c> creates one menu and one QR code; at the limit every create action stays visible but disabled with
/// the hint (<c>Shared/PlanLimitHint</c>, its contact phrase a <c>mailto:</c> link once <c>Landing:ContactEmail</c> is
/// set), <c>/menu/edit</c> and <c>/qrcode/edit</c> send it back to the list, saving a new menu checks again, and "Add
/// translation" is refused (a language version is a menu). Content above the limit stays editable and its guest pages
/// answer. Paid tenants see no hint. The QR code list alone: <see cref="QrCodeLimitTests"/>.
/// </summary>
public class PlanLimitTests : QrCalendarTestBase
{
    public PlanLimitTests(E2EFixture fixture) : base(fixture) { }

    private const string MenuHint = "The free plan includes one menu. For more, please contact us.";
    private const string QrHint = "The free plan includes one QR code. For more, please contact us.";

    private IConfiguration Configuration => App.ServerServices.GetRequiredService<IConfiguration>();

    private async Task<(int Menus, int QrCodes)> CountsAsync(Guid ownerId)
    {
        await using var db = App.CreateDbContext(ownerId);
        return (await db.Menus.CountAsync(), await db.QRCodes.CountAsync());
    }

    /// <summary>A free owner at both limits: one menu (with an item) and one QR code for it.</summary>
    private async Task<(Guid OwnerId, Guid MenuId)> SetUpFreeOwnerAtTheLimitsAsync(IPage page, string menuName = "Einzige Karte")
    {
        var email = UniqueEmail();
        await RegisterAsync(page, email, paid: false);
        var ownerId = await OwnerIdAsync(email);
        var menuId = await SeedMenuAsync(ownerId, menuName);
        await SeedQrAsync(ownerId, new QRCode { Title = "Tisch 1", MenuId = menuId });
        return (ownerId, menuId);
    }

    [Fact]
    public async Task FreeOwner_CreatesTheFirstMenuAndQrCode_ThenEveryCreateActionIsBlocked()
    {
        var ownerId = await RegisterOwnerAsync(paid: false);
        (await App.IsPaidAsync(ownerId)).Should().BeFalse("a self-registered owner starts on the free plan");

        // Below the limit: no hint anywhere, the actions are links and buttons as before
        await GotoInteractiveAsync("/menus");
        await Assertions.Expect(Page.GetByTestId("menu-create-button")).ToBeEnabledAsync();
        await Assertions.Expect(Page.GetByTestId("menu-create-first-button")).ToBeEnabledAsync();
        await Assertions.Expect(Page.GetByTestId("menu-limit-hint")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("menu-limit-hint-empty")).ToHaveCountAsync(0);
        await GotoInteractiveAsync("/dashboard");
        await Assertions.Expect(Page.Locator("a[data-testid='dashboard-create-menu']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("a[data-testid='dashboard-create-qr']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".sd-plan-hint")).ToHaveCountAsync(0);

        // The first menu: created; "New menu" stays, disabled, with the reason next to it
        await CreateMenuViaUiAsync("Erste Karte");
        var create = Page.GetByTestId("menu-create-button");
        await Assertions.Expect(create).ToBeVisibleAsync();
        await Assertions.Expect(create).ToBeDisabledAsync();
        await Assertions.Expect(create).ToHaveAttributeAsync("aria-describedby", "menu-limit-hint");
        await Assertions.Expect(Page.GetByTestId("menu-limit-hint")).ToHaveTextAsync(MenuHint);

        // The editor's URL does not get around it
        await GotoInteractiveAsync("/menu/edit");
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/menus$"));
        await Assertions.Expect(Page.Locator("#menuName")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("menu-create-button")).ToBeDisabledAsync();

        // The first QR code: created; then blocked, also through the URL
        await CreateDirectQrViaUiAsync("Tisch 1", "Erste Karte");
        await Assertions.Expect(Page.GetByTestId("qr-create-button")).ToBeDisabledAsync();
        await Assertions.Expect(Page.GetByTestId("qr-limit-hint")).ToHaveTextAsync(QrHint);
        await GotoInteractiveAsync("/qrcode/edit");
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
        await Assertions.Expect(Page.Locator("#qrTitle")).ToHaveCountAsync(0);

        // The dashboard: both actions disabled buttons (never followable links) with their hints
        await GotoInteractiveAsync("/dashboard");
        var dashboardMenu = Page.GetByTestId("dashboard-create-menu");
        var dashboardQr = Page.GetByTestId("dashboard-create-qr");
        await Assertions.Expect(dashboardMenu).ToBeDisabledAsync();
        await Assertions.Expect(dashboardQr).ToBeDisabledAsync();
        (await dashboardMenu.EvaluateAsync<string>("e => e.tagName")).Should().Be("BUTTON");
        (await dashboardQr.EvaluateAsync<string>("e => e.tagName")).Should().Be("BUTTON");
        await Assertions.Expect(Page.GetByTestId("dashboard-menu-limit-hint")).ToHaveTextAsync(MenuHint);
        await Assertions.Expect(Page.GetByTestId("dashboard-qr-limit-hint")).ToHaveTextAsync(QrHint);
        // Step 1 of the checklist is done (a menu exists), so it shows no action and no hint
        await Assertions.Expect(Page.GetByTestId("checklist-step").Nth(0)).ToHaveAttributeAsync("data-state", "done");
        await Assertions.Expect(Page.GetByTestId("checklist-limit-hint")).ToHaveCountAsync(0);

        (await CountsAsync(ownerId)).Should().Be((1, 1));
    }

    [Fact]
    public async Task SavingANewMenu_ChecksTheLimitAgain_AndShowsTheHintInsteadOfSaving()
    {
        var ownerId = await RegisterOwnerAsync(paid: false);
        await GotoInteractiveAsync("/menu/edit");
        await Assertions.Expect(Page.Locator("#menuName")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("menu-editor-limit")).ToHaveCountAsync(0);

        // Meanwhile the tenant's one menu was created elsewhere (another tab, a team member)
        await SeedMenuAsync(ownerId, "Aus dem anderen Tab");

        await Page.Locator("#menuName").FillAsync("Noch eine Karte");
        await Page.Locator("#menuDesc").FillAsync("Sollte nicht gespeichert werden");
        await Page.GetByTestId("menu-save-button").ClickAsync();

        await Assertions.Expect(Page.GetByTestId("menu-editor-limit")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("menu-editor-limit-hint")).ToHaveTextAsync(MenuHint);
        await Assertions.Expect(Page.GetByTestId("menu-save-button")).ToBeDisabledAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/menu/edit$"));
        await using var db = App.CreateDbContext(ownerId);
        (await db.Menus.Select(m => m.Name).ToListAsync()).Should().Equal("Aus dem anderen Tab");
    }

    [Fact]
    public async Task AddTranslation_IsBlockedAtTheLimit_AlsoWhenThePanelWasOpenedBefore()
    {
        var ownerId = await RegisterOwnerAsync(paid: false);
        var menuId = await SeedMenuAsync(ownerId, "Hauskarte");

        await GotoInteractiveAsync($"/menu/edit/{menuId}");
        var add = Page.GetByTestId("add-translation-button");
        await Assertions.Expect(add).ToBeVisibleAsync();
        await Assertions.Expect(add).ToBeDisabledAsync();
        await Assertions.Expect(add).ToHaveAttributeAsync("aria-describedby", "translation-limit-hint");
        await Assertions.Expect(Page.GetByTestId("translation-limit-hint")).ToHaveTextAsync(MenuHint);
        // Editing the menu itself stays allowed
        await Assertions.Expect(Page.GetByTestId("menu-save-button")).ToBeEnabledAsync();
        await Assertions.Expect(Page.GetByTestId("menu-editor-limit")).ToHaveCountAsync(0);

        // Paid for a moment: the panel opens; free again before "Create copy": the service refuses the copy
        await SetPaidAsync(ownerId, true);
        await GotoInteractiveAsync($"/menu/edit/{menuId}");
        await Assertions.Expect(Page.GetByTestId("translation-limit-hint")).ToHaveCountAsync(0);
        await add.ClickAsync();
        await Assertions.Expect(Page.GetByTestId("add-translation-panel")).ToBeVisibleAsync();
        await SetPaidAsync(ownerId, false);
        await Page.GetByTestId("add-translation-panel").GetByRole(AriaRole.Button, new() { Name = "Create copy" }).ClickAsync();

        await Assertions.Expect(Page.GetByTestId("language-versions-error")).ToHaveTextAsync(MenuHint);
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menu/edit/{menuId}$"));
        (await CountsAsync(ownerId)).Menus.Should().Be(1, "the language version was not created");
    }

    [Fact]
    public async Task TenantAboveTheLimit_KeepsEditingEverything_AndItsGuestPagesAnswer()
    {
        var ownerId = await RegisterOwnerAsync(paid: false);
        var menus = new[] { "Mittag", "Abend", "Getränke" };
        var menuIds = new List<Guid>();
        foreach (var name in menus)
            menuIds.Add(await SeedMenuAsync(ownerId, name));
        var qrIds = new[]
        {
            await SeedQrAsync(ownerId, new QRCode { Title = "Tisch 1", MenuId = menuIds[0] }),
            await SeedQrAsync(ownerId, new QRCode { Title = "Tisch 2", MenuId = menuIds[1] })
        };

        // All three menus are listed; creating stays blocked
        await GotoInteractiveAsync("/menus");
        foreach (var name in menus)
            await Assertions.Expect(MenuCardLink(Page, name)).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("menu-create-button")).ToBeDisabledAsync();
        await Assertions.Expect(Page.GetByTestId("menu-limit-hint")).ToHaveTextAsync(MenuHint);

        // A menu's settings are saved
        await GotoInteractiveAsync($"/menu/edit/{menuIds[1]}");
        await Assertions.Expect(Page.GetByTestId("menu-editor-limit")).ToHaveCountAsync(0);
        await Page.Locator("#menuName").FillAsync("Abendkarte");
        await Page.GetByTestId("menu-save-button").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menudetail/{menuIds[1]}$"));

        // Content inside a menu is not limited
        await CreateCategoryViaUiAsync(menuIds[2], "Weine");

        // A QR code is saved
        await GotoInteractiveAsync($"/qrcode/edit/{qrIds[1]}");
        await Page.Locator("#qrTitle").FillAsync("Tisch 2 (Garten)");
        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
        await Assertions.Expect(Page.GetByTestId("qr-card").Filter(new() { HasText = "Tisch 2 (Garten)" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("qr-create-button")).ToBeDisabledAsync();

        await using (var db = App.CreateDbContext(ownerId))
        {
            (await db.Menus.SingleAsync(m => m.Id == menuIds[1])).Name.Should().Be("Abendkarte");
            (await db.Categories.CountAsync(c => c.MenuId == menuIds[2])).Should().Be(2);
            (await db.QRCodes.SingleAsync(q => q.Id == qrIds[1])).Title.Should().Be("Tisch 2 (Garten)");
            (await db.Menus.CountAsync()).Should().Be(3);
            (await db.QRCodes.CountAsync()).Should().Be(2);
        }

        // Guests: every menu page and every printed QR code answer 200 with their menu, as before
        var (guestContext, guest) = await NewContextAsync();
        await using var _ = guestContext;
        var guestPages = menuIds.Select((id, i) => ($"/menu/{id}", $"{menus[i]} Gericht"))
            .Append(($"/qr/{qrIds[0]}", "Mittag Gericht"))
            .Append(($"/qr/{qrIds[1]}", "Abend Gericht"));
        foreach (var (url, dish) in guestPages)
        {
            var response = await guest.GotoAsync(url);
            response!.Status.Should().Be(200, url);
            await WaitForGuestReadyAsync(guest);
            await Assertions.Expect(ItemEntry(guest, dish)).ToBeVisibleAsync();
        }

        // Creating stays blocked through the URLs as well
        await GotoInteractiveAsync("/menu/edit");
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/menus$"));
        await GotoInteractiveAsync("/qrcode/edit");
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
    }

    [Fact]
    public async Task PaidOwner_SeesNoHint_AndCreatesMore()
    {
        var ownerId = await RegisterOwnerAsync(); // paid, the tests' default
        (await App.IsPaidAsync(ownerId)).Should().BeTrue();
        var menuId = await SeedMenuAsync(ownerId, "Erste Karte");
        await SeedQrAsync(ownerId, new QRCode { Title = "Tisch 1", MenuId = menuId });

        await GotoInteractiveAsync("/dashboard");
        await Assertions.Expect(Page.Locator("a[data-testid='dashboard-create-menu']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("a[data-testid='dashboard-create-qr']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator(".sd-plan-hint")).ToHaveCountAsync(0);

        await GotoInteractiveAsync($"/menu/edit/{menuId}");
        await Assertions.Expect(Page.GetByTestId("add-translation-button")).ToBeEnabledAsync();
        await Assertions.Expect(Page.GetByTestId("translation-limit-hint")).ToHaveCountAsync(0);

        await GotoInteractiveAsync("/qrcodes");
        await Assertions.Expect(Page.GetByTestId("qr-create-button")).ToBeEnabledAsync();
        await Assertions.Expect(Page.GetByTestId("qr-limit-hint")).ToHaveCountAsync(0);

        await CreateMenuViaUiAsync("Zweite Karte");
        await Assertions.Expect(Page.GetByTestId("menu-create-button")).ToBeEnabledAsync();
        await Assertions.Expect(Page.GetByTestId("menu-limit-hint")).ToHaveCountAsync(0);
        await CreateDirectQrViaUiAsync("Tisch 2", "Zweite Karte");
        (await CountsAsync(ownerId)).Should().Be((2, 2));
    }

    public sealed record Texts(string Locale, string MenuHint, string QrHint, string Contact, string NewMenu, string NewQr);

    private static readonly Texts German = new("de-DE",
        "Im kostenlosen Zugang ist eine Speisekarte enthalten. Für mehr wenden Sie sich an uns.",
        "Im kostenlosen Zugang ist ein QR-Code enthalten. Für mehr wenden Sie sich an uns.",
        "wenden Sie sich an uns", "Neue Speisekarte", "Neuer QR-Code");

    private static readonly Texts English = new("en-US", MenuHint, QrHint, "contact us", "Create New Menu", "Create New QR Code");

    public static TheoryData<string> Languages => new() { "de", "en" };

    /// <summary>
    /// The hints in both languages, first with the contact phrase as plain text (no <c>Landing:ContactEmail</c>, today's
    /// setting), then — once an address is set — as a <c>mailto:</c> link inside the same sentence.
    /// </summary>
    [Theory]
    [MemberData(nameof(Languages))]
    public async Task Hints_InTheBrowsersLanguage_WithTheContactPhraseAsAMailtoLinkOnceAnAddressIsSet(string language)
    {
        var texts = language == "de" ? German : English;
        var (context, page) = await NewContextAsync(texts.Locale);
        await using var _ = context;
        var (_, menuId) = await SetUpFreeOwnerAtTheLimitsAsync(page);

        // Where each hint is, and the sentence it says
        var places = new (string Url, string TestId, string Text)[]
        {
            ("/menus", "menu-limit-hint", texts.MenuHint),
            ("/qrcodes", "qr-limit-hint", texts.QrHint),
            ("/dashboard", "dashboard-menu-limit-hint", texts.MenuHint),
            ("/dashboard", "dashboard-qr-limit-hint", texts.QrHint),
            ($"/menu/edit/{menuId}", "translation-limit-hint", texts.MenuHint)
        };

        foreach (var (url, testId, text) in places)
        {
            await GotoInteractiveAsync(page, url);
            await Assertions.Expect(page.GetByTestId(testId)).ToHaveTextAsync(text);
            await Assertions.Expect(page.GetByTestId(testId).GetByTestId("plan-limit-contact")).ToHaveCountAsync(0);
        }
        await GotoInteractiveAsync(page, "/menus");
        await Assertions.Expect(page.GetByTestId("menu-create-button")).ToHaveTextAsync(texts.NewMenu);
        await GotoInteractiveAsync(page, "/qrcodes");
        await Assertions.Expect(page.GetByTestId("qr-create-button")).ToHaveTextAsync(texts.NewQr);

        try
        {
            Configuration["Landing:ContactEmail"] = "kontakt@speisedirekt.example";
            foreach (var (url, testId, text) in places)
            {
                await GotoInteractiveAsync(page, url);
                var hint = page.GetByTestId(testId);
                await Assertions.Expect(hint).ToHaveTextAsync(text);
                var link = hint.GetByTestId("plan-limit-contact");
                await Assertions.Expect(link).ToHaveAttributeAsync("href", "mailto:kontakt@speisedirekt.example");
                await Assertions.Expect(link).ToHaveTextAsync(texts.Contact);
            }
        }
        finally
        {
            Configuration["Landing:ContactEmail"] = "";
        }
    }
}
