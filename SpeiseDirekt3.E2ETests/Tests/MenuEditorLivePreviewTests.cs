using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// AP5 addendum: the menu editor's phone frame shows unsaved changes right away — name and description by postMessage
/// (js/menu-preview-draft.js → js/guest-preview.js, owner preview only), the theme in place, the language by a reload
/// with ?lang= that keeps the draft. Public pages never listen; foreign messages and markup in the text are ignored.
/// </summary>
public class MenuEditorLivePreviewTests : BackOfficeTestBase
{
    public MenuEditorLivePreviewTests(E2EFixture fixture) : base(fixture) { }

    private IFrameLocator Frame => Page.FrameLocator("[data-testid='menu-preview-iframe']");

    private async Task<GuestDemoData.Seeded> SeedForNewOwnerAsync(DesignTheme theme = DesignTheme.Modern)
    {
        var owner = await OwnerIdAsync(await RegisterNewOwnerAsync());
        return await GuestDemoData.SeedAsync(App, owner, theme, name: "Sommerkarte");
    }

    private async Task OpenEditorAsync(Guid menuId)
    {
        await GotoInteractiveAsync($"/menu/edit/{menuId}");
        await Frame.Locator("html[data-guest-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
    }

    private async Task<Menu> MenuInDbAsync(Guid menuId)
    {
        await using var db = App.CreateDbContext(Guid.Empty);
        return await db.Menus.IgnoreQueryFilters().AsNoTracking().SingleAsync(m => m.Id == menuId);
    }

    private Task<string> FrameTitleAsync() =>
        Page.Locator("[data-testid='menu-preview-iframe']").EvaluateAsync<string>("f => f.contentDocument.title");

    [Fact]
    public async Task TypedNameAndDescription_ShowInThePreview_BeforeSaving()
    {
        var menu = await SeedForNewOwnerAsync();
        await OpenEditorAsync(menu.MenuId);
        await Assertions.Expect(Page.GetByTestId("menu-preview-note")).ToHaveTextAsync("Shows your unsaved changes. Guests see them after you save.");
        await Assertions.Expect(Frame.Locator("h1")).ToHaveTextAsync("Sommerkarte");

        await Page.Locator("#menuName").FillAsync("Herbstkarte");
        await Page.Locator("#menuDesc").FillAsync("Kürbis, Pilze und Wild");

        await Assertions.Expect(Frame.Locator("h1")).ToHaveTextAsync("Herbstkarte");
        await Assertions.Expect(Frame.Locator(".gm-hero-desc")).ToHaveTextAsync("Kürbis, Pilze und Wild");
        await Assertions.Expect(Frame.GetByTestId("guest-topbar").Locator("[data-menu-name]")).ToHaveTextAsync("Herbstkarte");
        // The restaurant ("Trattoria Lago") differs from the menu now: its name above the h1 and in the title
        await Assertions.Expect(Frame.GetByTestId("hero-restaurant")).ToBeVisibleAsync();
        (await FrameTitleAsync()).Should().Be($"Herbstkarte – {GuestDemoData.RestaurantName}");

        // Not saved: the database and the guests still have the old values
        (await MenuInDbAsync(menu.MenuId)).Name.Should().Be("Sommerkarte");
        var (guestContext, guest) = await NewContextAsync();
        await using (guestContext)
        {
            await OpenGuestPageAsync(guest, $"/menu/{menu.MenuId}");
            await Assertions.Expect(guest.Locator("h1")).ToHaveTextAsync("Sommerkarte");
        }

        // An empty name shows the saved one; the restaurant's own name hides the line above the h1 again
        await Page.Locator("#menuName").FillAsync("");
        await Assertions.Expect(Frame.Locator("h1")).ToHaveTextAsync("Sommerkarte");
        await Page.Locator("#menuName").FillAsync(GuestDemoData.RestaurantName);
        await Assertions.Expect(Frame.Locator("h1")).ToHaveTextAsync(GuestDemoData.RestaurantName);
        await Assertions.Expect(Frame.GetByTestId("hero-restaurant")).ToBeHiddenAsync();
        (await FrameTitleAsync()).Should().Be($"{GuestDemoData.RestaurantName} – Speisekarte");
        await Page.Locator("#menuDesc").FillAsync("");
        await Assertions.Expect(Frame.Locator(".gm-hero-desc")).ToBeHiddenAsync();

        // Cancel: nothing saved, the preview shows the saved menu again
        await Button("Cancel").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menudetail/{menu.MenuId}$"));
        (await MenuInDbAsync(menu.MenuId)).Name.Should().Be("Sommerkarte");
        await OpenEditorAsync(menu.MenuId);
        await Assertions.Expect(Frame.Locator("h1")).ToHaveTextAsync("Sommerkarte");
        await Assertions.Expect(Frame.Locator(".gm-hero-desc")).ToHaveTextAsync(GuestDemoData.MenuDescription);

        // Save: the guests get it
        await Page.Locator("#menuName").FillAsync("Herbstkarte");
        await Assertions.Expect(Frame.Locator("h1")).ToHaveTextAsync("Herbstkarte");
        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menudetail/{menu.MenuId}$"));
        (await MenuInDbAsync(menu.MenuId)).Name.Should().Be("Herbstkarte");
        await OpenEditorAsync(menu.MenuId);
        await Assertions.Expect(Frame.Locator("h1")).ToHaveTextAsync("Herbstkarte");
    }

    /// <summary>
    /// The description is optional (AP13; it was [Required] without a message in the form, so clearing it silently
    /// blocked saving): a new menu without one and an existing one cleared both save, and the guests see no intro line.
    /// </summary>
    [Fact]
    public async Task Description_IsOptional_SavesEmpty_GuestsSeeNoIntro()
    {
        var menu = await SeedForNewOwnerAsync();
        await OpenEditorAsync(menu.MenuId);
        await Assertions.Expect(Page.Locator("label[for='menuDesc']")).ToContainTextAsync("(optional)");

        await Page.Locator("#menuDesc").FillAsync("");
        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menudetail/{menu.MenuId}$"));
        (await MenuInDbAsync(menu.MenuId)).Description.Should().BeEmpty();

        var newId = await CreateMenuViaUiAsync("Ohne Beschreibung", description: "");
        (await MenuInDbAsync(newId)).Description.Should().BeEmpty();

        var (guestContext, guest) = await NewContextAsync();
        await using (guestContext)
        {
            await OpenGuestPageAsync(guest, $"/menu/{menu.MenuId}");
            await Assertions.Expect(guest.Locator("h1")).ToHaveTextAsync("Sommerkarte");
            await Assertions.Expect(guest.Locator(".gm-hero-desc")).ToHaveCountAsync(0);
            // The meta description falls back to the restaurant's description
            (await guest.Locator("meta[name='description']").GetAttributeAsync("content")).Should().StartWith("Familienbetrieb");
        }
    }

    [Fact]
    public async Task ThemeSwitchesInPlace_LanguageReloads_BothKeepTheDraft()
    {
        var menu = await SeedForNewOwnerAsync();
        await OpenEditorAsync(menu.MenuId);
        var iframe = Page.Locator("[data-testid='menu-preview-iframe']");
        await iframe.EvaluateAsync("f => f.contentWindow.__sameDocument = true");

        await Page.Locator("#menuName").FillAsync("Herbstkarte");
        await Assertions.Expect(Frame.Locator("h1")).ToHaveTextAsync("Herbstkarte");

        // Theme: in place (same document), the draft stays
        await Page.Locator("#themeSelect").SelectOptionAsync("Dark");
        await Assertions.Expect(Frame.GetByTestId("menu-display")).ToHaveAttributeAsync("data-theme", "dark");
        (await iframe.EvaluateAsync<bool>("f => f.contentWindow.__sameDocument === true")).Should().BeTrue();
        await Assertions.Expect(Frame.Locator("h1")).ToHaveTextAsync("Herbstkarte");
        // The arrow buttons change the theme through Blazor: the preview follows too
        await Page.Locator("#themeSelect").Locator("xpath=following-sibling::button").ClickAsync();
        await Assertions.Expect(Frame.GetByTestId("menu-display")).ToHaveAttributeAsync("data-theme", "elegant");
        await Page.Locator("#themeSelect").Locator("xpath=preceding-sibling::button").ClickAsync();
        await Assertions.Expect(Frame.GetByTestId("menu-display")).ToHaveAttributeAsync("data-theme", "dark");

        // Language: the frame reloads with ?lang=en (English UI, € notation) and gets the draft again
        await Page.Locator("#languageSelect").SelectOptionAsync("English");
        await Assertions.Expect(iframe).ToHaveAttributeAsync("src", new Regex("theme=dark&lang=en$"));
        await Assertions.Expect(Frame.Locator("html")).ToHaveAttributeAsync("lang", "en");
        await Assertions.Expect(Frame.GetByTestId("search-button")).ToHaveAttributeAsync("aria-label", "Search the menu");
        await Assertions.Expect(Frame.GetByTestId("menu-display")).ToHaveAttributeAsync("data-theme", "dark");
        await Assertions.Expect(Frame.Locator("h1")).ToHaveTextAsync("Herbstkarte");
        await Assertions.Expect(Frame.Locator("[data-testid='list-row']", new() { Has = Frame.Locator("h3", new() { HasTextRegex = ExactText("Espresso") }) })
            .GetByTestId("item-price")).ToHaveTextAsync("€2.90");
        (await MenuInDbAsync(menu.MenuId)).Language.Should().Be(MenuLanguage.German);

        // ?lang= is a preview-only override: the public page stays German
        var (guestContext, guest) = await NewContextAsync();
        await using (guestContext)
        {
            await OpenGuestPageAsync(guest, $"/menu/{menu.MenuId}?lang=en");
            await Assertions.Expect(guest.Locator("html")).ToHaveAttributeAsync("lang", "de");
            await Assertions.Expect(guest.GetByTestId("search-button")).ToHaveAttributeAsync("aria-label", "Speisekarte durchsuchen");
        }
    }

    [Fact]
    public async Task TextChanges_KeepTheScrollPosition()
    {
        var menu = await SeedForNewOwnerAsync();
        await OpenEditorAsync(menu.MenuId);
        var iframe = Page.Locator("[data-testid='menu-preview-iframe']");
        await iframe.EvaluateAsync("f => f.contentWindow.scrollTo(0, 900)");
        await Page.WaitForTimeoutAsync(300);
        var before = await iframe.EvaluateAsync<double>("f => f.contentWindow.scrollY");
        before.Should().BeGreaterThan(500);

        await Page.Locator("#menuDesc").FillAsync("Neue Beschreibung");
        await Assertions.Expect(Frame.Locator(".gm-hero-desc")).ToHaveTextAsync("Neue Beschreibung");
        await Page.Locator("#menuName").FillAsync("Sommerkarte plus");
        await Assertions.Expect(Frame.GetByTestId("guest-topbar").Locator("[data-menu-name]")).ToHaveTextAsync("Sommerkarte plus");

        (await iframe.EvaluateAsync<double>("f => f.contentWindow.scrollY")).Should().BeApproximately(before, 2);
    }

    [Fact]
    public async Task Markup_IsShownAsText_ForeignMessagesAndPublicPagesAreIgnored()
    {
        var menu = await SeedForNewOwnerAsync();
        await OpenEditorAsync(menu.MenuId);
        var iframe = Page.Locator("[data-testid='menu-preview-iframe']");

        // HTML typed into the name stays text
        await Page.Locator("#menuName").FillAsync("<img src=x onerror=\"window.__pwned=1\"><b>Fett</b>");
        await Assertions.Expect(Frame.Locator("h1")).ToHaveTextAsync("<img src=x onerror=\"window.__pwned=1\"><b>Fett</b>");
        await Assertions.Expect(Frame.Locator("h1 img, h1 b")).ToHaveCountAsync(0);
        (await iframe.EvaluateAsync<bool>("f => f.contentWindow.__pwned === 1")).Should().BeFalse();

        // A message in another shape (extra key, unknown theme, wrong version) changes nothing
        await Page.Locator("#menuName").FillAsync("Sommerkarte");
        await Assertions.Expect(Frame.Locator("h1")).ToHaveTextAsync("Sommerkarte");
        await iframe.EvaluateAsync(@"f => {
            const w = f.contentWindow;
            w.postMessage({ type: 'sd-preview-draft', v: 1, name: 'Extra', description: '', theme: 'dark', html: '<b>x</b>' }, location.origin);
            w.postMessage({ type: 'sd-preview-draft', v: 1, name: 'Theme', description: '', theme: 'neon' }, location.origin);
            w.postMessage({ type: 'sd-preview-draft', v: 2, name: 'Version', description: '', theme: 'dark' }, location.origin);
            w.postMessage('sd-preview-draft', location.origin);
        }");
        await Page.WaitForTimeoutAsync(500);
        await Assertions.Expect(Frame.Locator("h1")).ToHaveTextAsync("Sommerkarte");
        await Assertions.Expect(Frame.GetByTestId("menu-display")).ToHaveAttributeAsync("data-theme", "modern");

        // The frame does not listen to anyone but its parent (here: a message the frame posts to itself)
        await iframe.EvaluateAsync("f => f.contentWindow.eval(\"window.postMessage({ type: 'sd-preview-draft', v: 1, name: 'Selbst', description: '', theme: 'dark' }, location.origin)\")");
        await Page.WaitForTimeoutAsync(500);
        await Assertions.Expect(Frame.Locator("h1")).ToHaveTextAsync("Sommerkarte");

        // The public page never listens, even inside the editor's frame
        await iframe.EvaluateAsync($"f => f.src = '/menu/{menu.MenuId}'");
        await Frame.Locator($"[data-guest-root][data-menu-id='{menu.MenuId}']:not([data-preview])").WaitForAsync();
        await Frame.Locator("html[data-guest-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await iframe.EvaluateAsync("f => f.contentWindow.postMessage({ type: 'sd-preview-draft', v: 1, name: 'Öffentlich', description: 'x', theme: 'dark' }, location.origin)");
        await Page.Locator("#menuName").FillAsync("Auch nicht");
        await Page.WaitForTimeoutAsync(600);
        await Assertions.Expect(Frame.Locator("h1")).ToHaveTextAsync("Sommerkarte");
        await Assertions.Expect(Frame.GetByTestId("menu-display")).ToHaveAttributeAsync("data-theme", "modern");
    }
}
