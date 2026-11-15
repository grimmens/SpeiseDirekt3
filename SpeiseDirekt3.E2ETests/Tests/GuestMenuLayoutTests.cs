using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// The guest menu as static SSR page with its own CSS/JS (<c>Components/Guest</c>, <c>js/guest-menu.js</c>):
/// no circuit, readable without JavaScript, sticky category chips with scroll-spy and category sheet, grid of
/// cards vs. list rows, item sheet (Escape/backdrop/back button, deep link), search sheet, click tracking via
/// <c>POST /guest/api/item-click</c>, German prices. Uses the photo menu of <see cref="GuestDemoData"/>.
/// </summary>
public class GuestMenuLayoutTests : BackOfficeTestBase
{
    public GuestMenuLayoutTests(E2EFixture fixture) : base(fixture) { }

    private Task<GuestDemoData.Seeded> SeedDemoAsync(DesignTheme theme = DesignTheme.Modern) =>
        GuestDemoData.SeedAsync(App, E2ESeedData.OwnerId, theme);

    private async Task UsePhoneAsync() => await Page.SetViewportSizeAsync(390, 844);

    private ILocator Chip(string name) => Page.GetByTestId("category-chip").Filter(new() { HasTextRegex = ExactText(name) });

    private ILocator Section(string name) => Page.Locator($"[data-testid='category-section'][data-category='{name}']");

    private async Task<int> CountClicksAsync(Guid menuId)
    {
        await using var db = App.CreateDbContext(Guid.Empty);
        return await db.MenuItemClicks.CountAsync(c => c.MenuId == menuId);
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition, string because)
    {
        for (var i = 0; i < 50 && !await condition(); i++)
            await Task.Delay(100);
        (await condition()).Should().BeTrue(because);
    }

    /// <summary>Top of an element relative to the viewport.</summary>
    private static Task<double> TopAsync(ILocator locator) => locator.EvaluateAsync<double>("e => e.getBoundingClientRect().top");

    // --- Rendering and assets ---

    [Fact]
    public async Task GuestPage_IsStatic_NoCircuitNoBlazorScript()
    {
        var demo = await SeedDemoAsync();
        var sockets = new List<string>();
        var scripts = new List<string>();
        Page.WebSocket += (_, ws) => sockets.Add(ws.Url);
        Page.Request += (_, r) => { if (r.ResourceType == "script") scripts.Add(new Uri(r.Url).AbsolutePath); };

        await OpenMenuAsync(demo.MenuId);
        await ClickMenuItemAsync("Margherita");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        sockets.Should().BeEmpty("guest pages render statically, there is no SignalR circuit");
        (await Page.EvaluateAsync<bool>("() => typeof window.Blazor === 'undefined'")).Should().BeTrue();
        // Only the guest module and the behaviour modules it imports (js/guest-*.js)
        scripts.Should().Contain(s => Regex.IsMatch(s, @"^/js/guest-menu(\.[a-z0-9]+)?\.js$"))
            .And.OnlyContain(s => Regex.IsMatch(s, @"^/js/guest-[a-z-]+(\.[a-z0-9]+)?\.js$"));
        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "de");
    }

    [Fact]
    public async Task InteractiveRouter_LoadsGuestPageAsFullPage()
    {
        await RegisterNewOwnerAsync();
        var demo = await SeedDemoAsync();
        await GotoInteractiveAsync("/menus");

        // Navigation inside the circuit (like a link without target=_blank): the interactive router does not know
        // the static guest pages; its NotFound (ServerPageLoad) loads them from the server with their own head
        await Page.EvaluateAsync($"() => Blazor.navigateTo('/menu/{demo.MenuId}')");
        await WaitForGuestReadyAsync(Page);

        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("data-guest-page", "");
        await Assertions.Expect(Page.Locator("script[src*='tailwindcss']")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("guest-topbar")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task WithoutJavaScript_MenuIsReadable_AndItemLinksOpenTheSheetFromTheServer()
    {
        var demo = await SeedDemoAsync();
        // Reduced motion: no slide-in animation (with JS disabled Playwright's stability check stalls on CSS animations)
        await using var context = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, JavaScriptEnabled = false, ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync($"/menu/{demo.MenuId}");
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync(GuestDemoData.MenuName);
        await Assertions.Expect(page.GetByTestId("item-card").First).ToBeVisibleAsync();
        await Assertions.Expect(ItemEntry(page, "Espresso").GetByTestId("item-price")).ToHaveTextAsync("€ 2,90");
        await Assertions.Expect(page.GetByTestId("category-chip").First).ToHaveAttributeAsync("href", new Regex("^#cat-"));
        // Controls that need JS stay hidden
        await Assertions.Expect(page.GetByTestId("search-button")).ToBeHiddenAsync();
        await Assertions.Expect(ItemSheetOf(page)).ToBeHiddenAsync();

        // A card is a real link: the server answers ?item= with the sheet open
        await ItemEntry(page, "Tiramisu").Locator("a").ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex($@"/menu/{demo.MenuId}\?item={demo.Items["Tiramisu"]}$"));
        var sheet = ItemSheetOf(page);
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await Assertions.Expect(sheet.GetByRole(AriaRole.Heading, new() { Name = "Tiramisu" })).ToBeVisibleAsync();
        await Assertions.Expect(sheet.GetByTestId("allergen-legend")).ToBeVisibleAsync();

        // Closing leads back to the plain page at the item's card
        await sheet.GetByRole(AriaRole.Link, new() { Name = "Schließen" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex($@"/menu/{demo.MenuId}#item-{demo.Items["Tiramisu"]:N}$"));
        await Assertions.Expect(ItemSheetOf(page)).ToBeHiddenAsync();
    }

    // --- Category bar ---

    [Fact]
    public async Task CategoryBar_IsVisibleAtTheTop_AndStaysStickyWhileScrolling()
    {
        var demo = await SeedDemoAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(demo.MenuId);

        var nav = Page.GetByTestId("category-nav");
        await Assertions.Expect(nav).ToBeInViewportAsync();
        (await TopAsync(nav)).Should().BeLessThan(400, "the chips are visible without scrolling");
        await Assertions.Expect(Page.GetByTestId("category-chip")).ToHaveCountAsync(GuestDemoData.Categories.Length);
        await Assertions.Expect(Chip("Antipasti")).ToHaveAttributeAsync("aria-current", "true");

        await Page.Mouse.WheelAsync(0, 2500);
        await EventuallyAsync(async () => await TopAsync(nav) is >= 50 and <= 60, "the bar sticks right under the 56 px top bar");
        await Assertions.Expect(Page.GetByTestId("guest-topbar")).ToBeInViewportAsync();
    }

    [Fact]
    public async Task ActiveChip_FollowsTheScrollPosition()
    {
        var demo = await SeedDemoAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(demo.MenuId);

        // Scroll by hand until "Pizza" sits right under the sticky bars
        var top = await Section("Pizza").EvaluateAsync<double>("e => e.getBoundingClientRect().top + window.scrollY");
        await Page.Mouse.WheelAsync(0, (float)(top - 100));
        await Assertions.Expect(Chip("Pizza")).ToHaveClassAsync(new Regex(@"\bis-active\b"));
        await Assertions.Expect(Chip("Pizza")).ToHaveAttributeAsync("aria-current", "true");
        await Assertions.Expect(Chip("Antipasti")).Not.ToHaveAttributeAsync("aria-current", "true");
        // The active chip was scrolled into the visible part of the bar
        await Assertions.Expect(Chip("Pizza")).ToBeInViewportAsync(new() { Ratio = 1 });

        await Page.Mouse.WheelAsync(0, -(float)top);
        await Assertions.Expect(Chip("Antipasti")).ToHaveAttributeAsync("aria-current", "true");
    }

    [Fact]
    public async Task Chip_JumpsToItsSection_BelowTheStickyBars()
    {
        var demo = await SeedDemoAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(demo.MenuId);

        await Chip("Dolci").ClickAsync();

        var heading = Section("Dolci").Locator("h2");
        await EventuallyAsync(async () => await TopAsync(heading) is > 100 and < 200, "the heading sits right below the sticky bars");
        await Assertions.Expect(Chip("Dolci")).ToHaveAttributeAsync("aria-current", "true");
        Page.Url.Should().NotContain("#", "with JS the jump does not add history entries");
    }

    [Fact]
    public async Task CategorySheet_ListsAllCategories_AndJumpsToTheChosenOne()
    {
        var demo = await SeedDemoAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(demo.MenuId);

        await Page.GetByTestId("category-all").ClickAsync();
        var sheet = Page.GetByTestId("category-sheet");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await Assertions.Expect(sheet).ToHaveAttributeAsync("aria-modal", "true");
        await Assertions.Expect(sheet.GetByRole(AriaRole.Heading, new() { Name = "Kategorien" })).ToBeVisibleAsync();
        await Assertions.Expect(sheet.Locator("[data-category-link]")).ToHaveTextAsync(GuestDemoData.Categories.Select(c => c.Name).ToArray());

        await sheet.GetByRole(AriaRole.Link, new() { Name = "Weine" }).ClickAsync();

        await Assertions.Expect(sheet).ToBeHiddenAsync();
        await Assertions.Expect(Section("Weine").Locator("h2")).ToBeInViewportAsync();
        await Assertions.Expect(Chip("Weine")).ToHaveAttributeAsync("aria-current", "true");
    }

    [Fact]
    public async Task TwentyFiveCategories_AllRender_AndTheLastOneCanBeReached()
    {
        var menuId = Guid.NewGuid();
        await App.SeedAsync(E2ESeedData.OwnerId, db =>
        {
            var categories = Enumerable.Range(1, 25)
                .Select(i => new Category { Id = Guid.NewGuid(), Name = $"Kategorie {i:00}", SortOrder = i, MenuId = menuId }).ToList();
            db.Menus.Add(new Menu { Id = menuId, Name = "Große Karte", Description = "", Language = MenuLanguage.German, Categories = categories });
            foreach (var c in categories)
                db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = $"Gericht {c.Name[^2..]}", Description = "", Price = 5m, CategoryId = c.Id });
            return Task.CompletedTask;
        });
        await UsePhoneAsync();
        await OpenMenuAsync(menuId);

        await Assertions.Expect(Page.GetByTestId("category-section")).ToHaveCountAsync(25);
        await Assertions.Expect(Page.GetByTestId("category-chip")).ToHaveCountAsync(25);

        await Page.GetByTestId("category-all").ClickAsync();
        await Page.GetByTestId("category-sheet").GetByRole(AriaRole.Link, new() { Name = "Kategorie 25" }).ClickAsync();
        await Assertions.Expect(Section("Kategorie 25").Locator("h3")).ToBeInViewportAsync();
        await Assertions.Expect(Chip("Kategorie 25")).ToHaveAttributeAsync("aria-current", "true");
        await Assertions.Expect(Chip("Kategorie 25")).ToBeInViewportAsync(new() { Ratio = 1 });
    }

    // --- Cards and list rows ---

    [Fact]
    public async Task PhotoCategories_AreCardGrids_TextOnlyCategories_AreListRows()
    {
        var demo = await SeedDemoAsync();
        await OpenMenuAsync(demo.MenuId);

        foreach (var (name, photos) in GuestDemoData.Categories)
        {
            var section = Section(name);
            await Assertions.Expect(section).ToHaveAttributeAsync("data-layout", photos ? "grid" : "list");
            await Assertions.Expect(section.GetByTestId(photos ? "item-card" : "list-row").First).ToBeVisibleAsync();
            await Assertions.Expect(section.GetByTestId(photos ? "list-row" : "item-card")).ToHaveCountAsync(0);
        }

        // Photo card: lazy image with its size reserved, data for scripts, an empty slot for the heart button
        var card = ItemEntry("Burrata");
        var img = card.Locator("img");
        await Assertions.Expect(img).ToHaveAttributeAsync("loading", "lazy");
        await Assertions.Expect(img).ToHaveAttributeAsync("width", "600");
        await Assertions.Expect(card).ToHaveAttributeAsync("data-item-id", demo.Items["Burrata"].ToString());
        await Assertions.Expect(card).ToHaveAttributeAsync("data-labels", "vegetarian");
        await Assertions.Expect(card).ToHaveAttributeAsync("data-allergens", "G");
        await Assertions.Expect(card).ToHaveAttributeAsync("data-price", "12.90");
        await Assertions.Expect(card.Locator("[data-slot='favorite']")).ToHaveCountAsync(1);

        // Dish without a photo in a photo grid: a clean text card, no emoji placeholder
        var textCard = ItemEntry(GuestDemoData.DishWithoutPhoto);
        await Assertions.Expect(textCard).ToHaveAttributeAsync("data-testid", "item-card");
        await Assertions.Expect(textCard).ToHaveAttributeAsync("data-has-image", "false");
        await Assertions.Expect(textCard.Locator("img")).ToHaveCountAsync(0);
        (await Page.Locator("main").InnerTextAsync()).Should().NotContain("🥙");

        // List row: name left, price right
        var row = ItemEntry("Chianti Classico 1/8 l");
        var title = await row.Locator("h3").BoundingBoxAsync();
        var price = await row.GetByTestId("item-price").BoundingBoxAsync();
        price!.X.Should().BeGreaterThan(title!.X + title.Width);
    }

    [Theory]
    [InlineData(390, 2)]
    [InlineData(800, 3)]
    [InlineData(1280, 4)]
    public async Task CardGrid_HasColumnsByViewportWidth(int width, int columns)
    {
        var demo = await SeedDemoAsync();
        await Page.SetViewportSizeAsync(width, 900);
        await OpenMenuAsync(demo.MenuId);

        var lefts = await Section("Antipasti").GetByTestId("item-card")
            .EvaluateAllAsync<double[]>("cards => cards.map(c => Math.round(c.getBoundingClientRect().left))");
        lefts.Distinct().Should().HaveCount(columns);
        // No horizontal scrolling of the page
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth")).Should().BeTrue();
    }

    [Fact]
    public async Task Prices_AreGermanFormatted()
    {
        var menuId = Guid.NewGuid();
        await App.SeedAsync(E2ESeedData.OwnerId, db =>
        {
            var category = new Category { Id = Guid.NewGuid(), Name = "Spezial", MenuId = menuId };
            db.Menus.Add(new Menu { Id = menuId, Name = "Preise", Description = "", Language = MenuLanguage.German, Categories = new List<Category> { category } });
            db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = "Kaviar", Description = "", Price = 1234.5m, CategoryId = category.Id, SortOrder = 0 });
            db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = "Brot", Description = "", Price = 9.9m, CategoryId = category.Id, SortOrder = 1 });
            db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = "Wasser", Description = "", Price = 0m, CategoryId = category.Id, SortOrder = 2 });
            return Task.CompletedTask;
        });
        await OpenMenuAsync(menuId);

        await Assertions.Expect(ItemEntry("Kaviar").GetByTestId("item-price")).ToHaveTextAsync("€ 1.234,50");
        await Assertions.Expect(ItemEntry("Brot").GetByTestId("item-price")).ToHaveTextAsync("€ 9,90");
        await Assertions.Expect(ItemEntry("Wasser").GetByTestId("item-price")).ToHaveTextAsync("€ 0,00");
        await Assertions.Expect(ItemEntry("Brot")).ToHaveAttributeAsync("data-price", "9.90");
    }

    // --- Item sheet ---

    [Fact]
    public async Task ItemSheet_IsAnAccessibleDialog_WithUrlAndFocusHandling()
    {
        var demo = await SeedDemoAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(demo.MenuId);
        var link = ItemEntry("Margherita").Locator("a[data-item-link]");

        await ClickMenuItemAsync("Margherita");

        await Assertions.Expect(ItemSheet).ToHaveAttributeAsync("aria-modal", "true");
        var labelledBy = await ItemSheet.GetAttributeAsync("aria-labelledby");
        await Assertions.Expect(Page.Locator($"#{labelledBy}")).ToHaveTextAsync("Margherita");
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($@"\?item={demo.Items["Margherita"]}$"));
        (await ItemSheet.EvaluateAsync<bool>("d => d.contains(document.activeElement)")).Should().BeTrue("focus moves into the sheet");
        await Assertions.Expect(ItemSheet.GetByRole(AriaRole.Img, new() { Name = "Margherita" })).ToBeVisibleAsync();
        await Assertions.Expect(ItemSheet.GetByTestId("item-price")).ToHaveTextAsync("€ 10,50");
        await Assertions.Expect(ItemSheet.GetByTestId("combo-offers")).ToContainTextAsync(GuestDemoData.ComboName);
        (await Page.EvaluateAsync<bool>("() => document.documentElement.classList.contains('gm-locked')")).Should().BeTrue("the page behind does not scroll");

        await Page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(ItemSheet).ToBeHiddenAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($@"/menu/{demo.MenuId}$"));
        await Assertions.Expect(link).ToBeFocusedAsync();
    }

    [Fact]
    public async Task ItemSheet_ClosesWithBackdropTap_AndWithTheBackButton()
    {
        var demo = await SeedDemoAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(demo.MenuId);

        await ClickMenuItemAsync("Diavola");
        await Page.Mouse.ClickAsync(195, 30); // above the bottom sheet = backdrop
        await Assertions.Expect(ItemSheet).ToBeHiddenAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($@"/menu/{demo.MenuId}$"));

        await ClickMenuItemAsync("Tiramisu");
        await Page.GoBackAsync();
        await Assertions.Expect(ItemSheet).ToBeHiddenAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($@"/menu/{demo.MenuId}$"));
        await Assertions.Expect(Page.Locator("h1")).ToHaveTextAsync(GuestDemoData.MenuName); // still on the menu

        // Forward brings the item back
        await Page.GoForwardAsync();
        await Assertions.Expect(ItemSheet.GetByRole(AriaRole.Heading, new() { Name = "Tiramisu" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task DeepLink_OpensTheItemSheet_AndClosingKeepsTheGuestOnTheMenu()
    {
        var demo = await SeedDemoAsync();
        await UsePhoneAsync();

        await OpenGuestPageAsync(Page, $"/menu/{demo.MenuId}?item={demo.Items["Panna Cotta"]}");

        await Assertions.Expect(ItemSheet).ToBeVisibleAsync();
        await Assertions.Expect(ItemSheet.GetByRole(AriaRole.Heading, new() { Name = "Panna Cotta" })).ToBeVisibleAsync();
        (await ItemSheet.EvaluateAsync<bool>("d => d.matches(':modal')")).Should().BeTrue("JS turns the server-rendered sheet into a modal");
        await Assertions.Expect(Page.Locator("[data-scrim]")).ToHaveCountAsync(0);

        await ItemSheet.GetByRole(AriaRole.Link, new() { Name = "Schließen" }).ClickAsync();
        await Assertions.Expect(ItemSheet).ToBeHiddenAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($@"/menu/{demo.MenuId}$"));
        await Assertions.Expect(ItemEntry("Panna Cotta").Locator("a")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task UnknownItemParameter_IsIgnored()
    {
        var demo = await SeedDemoAsync();

        await OpenGuestPageAsync(Page, $"/menu/{demo.MenuId}?item=not-a-guid");
        await Assertions.Expect(ItemSheet).ToBeHiddenAsync();
        await OpenGuestPageAsync(Page, $"/menu/{demo.MenuId}?item={E2ESeedData.BurgerId}"); // item of another menu
        await Assertions.Expect(ItemSheet).ToBeHiddenAsync();
        await Assertions.Expect(Page.Locator("h1")).ToHaveTextAsync(GuestDemoData.MenuName);
    }

    // --- Search ---

    [Fact]
    public async Task SearchSheet_FindsItems_AndOpensTheTappedOne()
    {
        var demo = await SeedDemoAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(demo.MenuId);

        await Page.GetByTestId("search-button").ClickAsync();
        var sheet = Page.GetByTestId("search-sheet");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        var input = sheet.GetByRole(AriaRole.Searchbox);
        await Assertions.Expect(input).ToBeFocusedAsync();

        await input.FillAsync("pizza");
        await Assertions.Expect(sheet.Locator("[data-search-row]:visible")).ToHaveCountAsync(3); // by category
        await Assertions.Expect(sheet.GetByRole(AriaRole.Status)).ToHaveTextAsync("3 Ergebnisse");

        await input.FillAsync("kalbfleisch"); // in the description
        await Assertions.Expect(sheet.Locator("[data-search-row]:visible")).ToHaveTextAsync(new[] { new Regex("Vitello Tonnato") });
        await input.FillAsync("tiramisù"); // accents and case ignored
        await Assertions.Expect(sheet.Locator("[data-search-row]:visible")).ToHaveCountAsync(1);
        await input.FillAsync("xyz");
        await Assertions.Expect(sheet.GetByRole(AriaRole.Status)).ToHaveTextAsync("Keine Treffer für „xyz“");

        await input.FillAsync("tonnato");
        await sheet.Locator("[data-search-row]:visible a").ClickAsync();
        await Assertions.Expect(sheet).ToBeHiddenAsync();
        await Assertions.Expect(ItemSheet.GetByRole(AriaRole.Heading, new() { Name = "Vitello Tonnato" })).ToBeVisibleAsync();

        // One back press closes the item and returns to the plain menu (the search entry was replaced)
        await Page.GoBackAsync();
        await Assertions.Expect(ItemSheet).ToBeHiddenAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($@"/menu/{demo.MenuId}$"));
    }

    // --- Click tracking ---

    [Fact]
    public async Task ItemClick_IsRecordedExactlyOnce()
    {
        var demo = await SeedDemoAsync();
        await OpenMenuAsync(demo.MenuId);

        await ClickMenuItemAsync("Burrata");
        await EventuallyAsync(async () => await CountClicksAsync(demo.MenuId) == 1, "the open is recorded");
        await Page.WaitForTimeoutAsync(500);
        (await CountClicksAsync(demo.MenuId)).Should().Be(1, "exactly once");
        await using (var db = App.CreateDbContext(Guid.Empty))
            (await db.MenuItemClicks.SingleAsync(c => c.MenuId == demo.MenuId)).MenuItemId.Should().Be(demo.Items["Burrata"]);

        // Back and forward do not count again; a deep link (page request) counts once
        await Page.GoBackAsync();
        await Page.GoForwardAsync();
        await OpenGuestPageAsync(Page, $"/menu/{demo.MenuId}?item={demo.Items["Diavola"]}");
        await Page.WaitForTimeoutAsync(500);
        (await CountClicksAsync(demo.MenuId)).Should().Be(2);
    }

    [Fact]
    public async Task ItemClickEndpoint_ValidatesInput()
    {
        var demo = await SeedDemoAsync();
        var api = Page.APIRequest;

        (await api.PostAsync("/guest/api/item-click", new() { DataString = "{not json", Headers = Json })).Status.Should().Be(400);
        (await api.PostAsync("/guest/api/item-click", new() { DataString = "{}", Headers = Json })).Status.Should().Be(400);
        (await api.PostAsync("/guest/api/item-click", new() { DataObject = new { menuId = demo.MenuId, itemId = E2ESeedData.BurgerId } }))
            .Status.Should().Be(404, "the item belongs to another menu");
        (await api.PostAsync("/guest/api/item-click", new() { DataObject = new { menuId = demo.MenuId, itemId = Guid.NewGuid() } }))
            .Status.Should().Be(404);

        // The owner preview never counts, even if a request comes from there
        var fromPreview = await api.PostAsync("/guest/api/item-click", new()
        {
            DataObject = new { menuId = demo.MenuId, itemId = demo.Items["Margherita"] },
            Headers = new Dictionary<string, string> { ["Referer"] = $"{App.ServerAddress}/menu/preview/{demo.MenuId}" }
        });
        fromPreview.Status.Should().Be(204);
        (await CountClicksAsync(demo.MenuId)).Should().Be(0);

        var ok = await api.PostAsync("/guest/api/item-click", new() { DataObject = new { menuId = demo.MenuId, itemId = demo.Items["Margherita"] } });
        ok.Status.Should().Be(204);
        (await CountClicksAsync(demo.MenuId)).Should().Be(1);
    }

    private static readonly Dictionary<string, string> Json = new() { ["Content-Type"] = "application/json" };

    // --- Editor preview ---

    [Fact]
    public async Task MenuEditor_NewMenu_ShowsHintInsteadOfPreview()
    {
        await RegisterNewOwnerAsync();
        await GotoInteractiveAsync("/menu/edit");

        await Assertions.Expect(Page.GetByTestId("menu-preview-hint")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("menu-preview-iframe")).ToHaveCountAsync(0);
    }
}
