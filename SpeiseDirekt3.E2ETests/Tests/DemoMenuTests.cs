using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt3.Components.Demo;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// The live demo (AP13, <c>/demo</c>, <c>Components/Demo</c>): the real guest menu of the made-up "Trattoria Lago" built in
/// memory — no menu row in the database, nothing recorded, <c>noindex</c>, nothing from another host — with categories,
/// item sheet, search, filter, favourites, the restaurant profile, the English version at <c>/demo/en</c>, <c>?item=</c>
/// and <c>?theme=</c>.
/// </summary>
public class DemoMenuTests : E2ETestBase
{
    public DemoMenuTests(E2EFixture fixture) : base(fixture) { }

    private static readonly DemoMenuVersion German = DemoMenuContent.German;

    private ILocator SearchSheet => Page.GetByTestId("search-sheet");
    private ILocator FilterSheet => Page.GetByTestId("filter-sheet");
    private ILocator VisibleEntries => Page.Locator("main").Locator("[data-testid='item-card']:visible, [data-testid='list-row']:visible");

    private async Task<(int Menus, int Views, int Clicks)> CountsAsync()
    {
        await using var db = App.CreateDbContext(Guid.Empty);
        return (await db.Menus.IgnoreQueryFilters().CountAsync(), await db.MenuViews.CountAsync(), await db.MenuItemClicks.CountAsync());
    }

    [Fact]
    public async Task Demo_RendersTheMenuFromCode_WithoutADatabaseMenu_AndRecordsNothing()
    {
        var before = await CountsAsync();
        var itemClicks = new List<string>();
        var external = new List<string>();
        var origin = new Uri(App.ServerAddress).GetLeftPart(UriPartial.Authority);
        Page.Request += (_, r) =>
        {
            if (r.Url.Contains("/guest/api/item-click", StringComparison.OrdinalIgnoreCase)) lock (itemClicks) itemClicks.Add(r.Url);
            if (!r.Url.StartsWith(origin, StringComparison.OrdinalIgnoreCase) && !r.Url.StartsWith("data:")) lock (external) external.Add(r.Url);
        };

        var response = await Page.GotoAsync("/demo");
        response!.Status.Should().Be(200);
        response.Headers["x-robots-tag"].Should().Be("noindex");
        await WaitForGuestReadyAsync(Page);

        // The guest document of the menu's language, the restaurant profile, every category and dish
        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "de");
        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("data-guest-page", new Regex(".*"));
        await Assertions.Expect(Page.Locator("meta[name='robots']")).ToHaveAttributeAsync("content", "noindex");
        await Assertions.Expect(Page.Locator("link[rel='alternate'][hreflang]")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("link[rel='canonical']")).ToHaveAttributeAsync("href", App.ServerAddress + "/demo");
        await Assertions.Expect(Page.Locator("h1")).ToHaveTextAsync(German.MenuName);
        await Assertions.Expect(Page.GetByTestId("hero-cover")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("hero-cover")).ToHaveJSPropertyAsync("complete", true);
        (await Page.GetByTestId("hero-cover").EvaluateAsync<int>("i => i.naturalWidth")).Should().Be(1200);
        await Assertions.Expect(Page.GetByTestId("category-section")).ToHaveCountAsync(German.Categories.Count);
        await Assertions.Expect(Page.Locator("[data-testid='item-card'], [data-testid='list-row']")).ToHaveCountAsync(German.Dishes.Count);
        await Assertions.Expect(Page.GetByTestId("demo-note")).ToContainTextAsync("erfundenes Restaurant");
        await Assertions.Expect(Page.GetByTestId("demo-home-link")).ToHaveAttributeAsync("href", "/");
        (await Page.GetByTestId("demo-home-link").EvaluateAsync<double>("e => e.getBoundingClientRect().height")).Should().BeGreaterThanOrEqualTo(40, "a touch target");

        // Every dish photo is a static file that loads
        var photos = Page.Locator("[data-testid='item-card'] img");
        await Assertions.Expect(photos).ToHaveCountAsync(German.Dishes.Count(d => d.Photo is not null));
        foreach (var src in await photos.EvaluateAllAsync<string[]>("imgs => imgs.map(i => i.getAttribute('src'))"))
        {
            src.Should().StartWith("/images/demo/");
            (await Page.APIRequest.GetAsync(src)).Status.Should().Be(200);
        }

        // The item sheet opens (a variant dish and the combo), and no click is sent
        await ClickMenuItemAsync("Margherita");
        await Assertions.Expect(ItemSheet.GetByTestId("item-extras")).ToContainTextAsync("Büffelmozzarella");
        await Assertions.Expect(ItemSheet.GetByTestId("combo-offers")).ToContainTextAsync("Pranzo");
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(@"/demo\?item=[0-9a-f-]{36}$"));
        await CloseItemSheetAsync(Page);
        await ClickMenuItemAsync("Grüner Veltliner");
        await Assertions.Expect(ItemSheet.GetByTestId("item-variant")).ToHaveCountAsync(3);
        await CloseItemSheetAsync(Page);

        // A deep link renders the sheet from the server
        var tiramisu = DemoMenu.Id("de:item:Tiramisu");
        await OpenGuestPageAsync(Page, $"/demo?item={tiramisu}");
        await Assertions.Expect(ItemSheet).ToBeVisibleAsync();
        await Assertions.Expect(ItemSheet.GetByRole(AriaRole.Heading, new() { Name = "Tiramisu", Exact = true })).ToBeVisibleAsync();

        await Page.WaitForTimeoutAsync(300);
        itemClicks.Should().BeEmpty("the demo never tracks");
        external.Should().BeEmpty("the demo loads nothing from another host");
        (await CountsAsync()).Should().Be(before, "the demo has no menu row and records no view or click");
    }

    [Fact]
    public async Task Demo_SearchFilterAndFavourites_Work()
    {
        await OpenGuestPageAsync(Page, "/demo");

        // Search: by description, the result opens the dish
        await Page.GetByTestId("search-button").ClickAsync();
        await Assertions.Expect(SearchSheet).ToBeVisibleAsync();
        await SearchSheet.GetByRole(AriaRole.Searchbox).FillAsync("steinpilz");
        var results = SearchSheet.Locator("[data-search-row]:visible");
        await Assertions.Expect(results).ToHaveCountAsync(1);
        await Assertions.Expect(results.First).ToContainTextAsync("Risotto ai Funghi");
        await results.First.Locator("a").ClickAsync();
        await Assertions.Expect(ItemSheet.GetByRole(AriaRole.Heading, new() { Name = "Risotto ai Funghi", Exact = true })).ToBeVisibleAsync();
        await CloseItemSheetAsync(Page);

        // Filter: vegan (the demo's vegan dishes and drinks), the notice counts them
        await Page.GetByTestId("filter-button").ClickAsync();
        await Assertions.Expect(FilterSheet).ToBeVisibleAsync();
        await FilterSheet.GetByTestId("filter-label-vegan").ClickAsync();
        await FilterSheet.GetByTestId("filter-apply").ClickAsync();
        await Assertions.Expect(FilterSheet).ToBeHiddenAsync();
        var vegan = German.Dishes.Where(d => d.Vegan).Select(d => d.Name).ToArray();
        await Assertions.Expect(VisibleEntries).ToHaveCountAsync(vegan.Length);
        (await VisibleEntries.Locator("h3").AllInnerTextsAsync()).Select(t => t.Trim()).Should().BeEquivalentTo(vegan);
        await Assertions.Expect(Page.GetByTestId("filter-notice")).ToBeVisibleAsync();
        await Page.GetByTestId("filter-notice").GetByRole(AriaRole.Button).ClickAsync();
        await Assertions.Expect(VisibleEntries).ToHaveCountAsync(German.Dishes.Count);

        // Favourites: kept on the device for the demo's menu id
        await ItemEntry("Burrata").GetByTestId("favorite-button").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("favorites-count")).ToHaveTextAsync("1");
        await Page.ReloadAsync();
        await WaitForGuestReadyAsync(Page);
        await Assertions.Expect(Page.GetByTestId("favorites-count")).ToHaveTextAsync("1");
        var stored = await Page.EvaluateAsync<string>($"localStorage.getItem('sd-merkliste:{DemoMenu.MenuIdOf(German)}')");
        stored.Should().Contain(DemoMenu.Id("de:item:Burrata").ToString());
    }

    [Fact]
    public async Task Demo_ShowsTheRestaurantProfile_WithoutPhoneOrSocialLinks()
    {
        await OpenGuestPageAsync(Page, "/demo");
        await Page.GetByTestId("info-button").ClickAsync();
        var sheet = Page.GetByTestId("info-sheet");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await Assertions.Expect(sheet.GetByTestId("info-hours")).ToContainTextAsync("Dienstag Ruhetag");
        await Assertions.Expect(sheet.GetByTestId("info-address")).ToContainTextAsync("Mondsee");
        await Assertions.Expect(sheet.GetByTestId("info-website")).ToHaveAttributeAsync("href", "https://trattoria-lago.example");
        await Assertions.Expect(sheet.GetByTestId("info-phone")).ToHaveCountAsync(0);
        await Assertions.Expect(sheet.GetByTestId("info-instagram")).ToHaveCountAsync(0);
        await Assertions.Expect(sheet.GetByTestId("info-review")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Demo_English_IsALanguageVersion_AndSharingUsesTheDemoLink()
    {
        await OpenGuestPageAsync(Page, "/demo");

        // The language switch goes to /demo/en (a plain link, nothing recorded)
        await Page.GetByTestId("language-button").ClickAsync();
        var sheet = Page.GetByTestId("language-sheet");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await sheet.GetByTestId("language-option").Filter(new() { HasTextString = "English" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/demo/en$"));
        await WaitForGuestReadyAsync(Page);
        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "en");
        await Assertions.Expect(Page.GetByTestId("demo-note")).ToContainTextAsync("made-up restaurant");
        await Assertions.Expect(ItemEntry("Homemade lemonade").GetByTestId("item-price")).ToHaveTextAsync("from €4.20");
        await Assertions.Expect(Page.Locator("[data-testid='item-card'], [data-testid='list-row']")).ToHaveCountAsync(DemoMenuContent.English.Dishes.Count);

        // Share: the demo's own link (there is no /menu/{id} for it)
        await Page.EvaluateAsync("""
            () => {
                window.__copied = [];
                Object.defineProperty(navigator, 'share', { value: undefined, configurable: true });
                Object.defineProperty(navigator, 'clipboard', { value: { writeText: t => { window.__copied.push(t); return Promise.resolve(); } }, configurable: true });
            }
            """);
        await ClickMenuItemAsync("Beef carpaccio");
        await ItemSheet.GetByTestId("share-button").ClickAsync();
        await Assertions.Expect(ItemSheet.GetByTestId("share-status")).ToHaveTextAsync("Link copied");
        var copied = await Page.EvaluateAsync<string>("window.__copied[0]");
        copied.Should().Be($"{App.ServerAddress}/demo/en?item={DemoMenu.Id("en:item:Beef carpaccio")}");
    }

    [Theory]
    [InlineData("classic")]
    [InlineData("dark")]
    public async Task Demo_ThemeQuery_ShowsThatDesign(string theme)
    {
        await OpenGuestPageAsync(Page, $"/demo?theme={theme}");
        await Assertions.Expect(Page.GetByTestId("menu-display")).ToHaveAttributeAsync("data-theme", theme);
        await OpenGuestPageAsync(Page, "/demo?theme=unknown");
        await Assertions.Expect(Page.GetByTestId("menu-display")).ToHaveAttributeAsync("data-theme", "modern");
    }

    [Fact]
    public async Task Demo_WorksWithoutJavaScript()
    {
        var context = await Fixture.NewBrowserContextAsync(new() { JavaScriptEnabled = false });
        await using var _ = context;
        var page = await context.NewPageAsync();
        await page.GotoAsync(App.ServerAddress + "/demo");
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync(German.MenuName);
        await ItemEntry(page, "Burrata").Locator("a[data-item-link]").ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex(@"/demo\?item="));
        await Assertions.Expect(ItemSheetOf(page).GetByRole(AriaRole.Heading, new() { Name = "Burrata", Exact = true })).ToBeVisibleAsync();
    }
}
