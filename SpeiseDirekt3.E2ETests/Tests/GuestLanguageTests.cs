using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// AP5, guest side: language versions of a menu (<c>Menu.TranslationGroupId</c>) — the language button and sheet, the
/// switch from <c>/menu/…</c>, <c>/qr/…</c> and the owner preview (no tracking), the list without JS, hreflang and
/// og:locale — and the guest UI in the menu's language: German menus German, every other language English (texts,
/// aria-labels, label chips, the scripts' strings with singular/plural, prices <c>€9.90</c> / "from"), <c>lang</c>.
/// </summary>
public class GuestLanguageTests : BackOfficeTestBase
{
    public GuestLanguageTests(E2EFixture fixture) : base(fixture) { }

    private sealed record Group(Guid OwnerId, GuestDemoData.Seeded German, GuestDemoData.Seeded English, Guid FrenchId);

    /// <summary>German demo menu + its English version + a small French version, in a fresh tenant.</summary>
    private async Task<Group> SeedGroupAsync(Guid? ownerId = null, bool withFrench = true)
    {
        var owner = ownerId ?? Guid.NewGuid();
        var german = await GuestDemoData.SeedAsync(App, owner, DesignTheme.Modern);
        var english = await GuestDemoData.SeedEnglishVersionAsync(App, owner, german, DesignTheme.Modern);
        var french = withFrench
            ? await GuestDemoData.SeedSmallMenuAsync(App, owner, MenuLanguage.French, "Trattoria Lago (Français)", "Tiramisu maison", 7.50m, german.MenuId)
            : Guid.Empty;
        return new Group(owner, german, english, french);
    }

    private async Task<(int Views, int Clicks)> CountTrackingAsync(params Guid[] menuIds)
    {
        await using var db = App.CreateDbContext(Guid.Empty);
        return (await db.MenuViews.CountAsync(v => menuIds.Contains(v.MenuId)),
                await db.MenuItemClicks.CountAsync(c => menuIds.Contains(c.MenuId)));
    }

    private ILocator LanguageButton(IPage page) => page.GetByTestId("language-button");

    // --- Language button and sheet ---------------------------------------------------------------------------------

    [Fact]
    public async Task MenuWithoutVersions_HasNoLanguageButton_ListOrAlternates()
    {
        var menu = await GuestDemoData.SeedAsync(App, Guid.NewGuid(), DesignTheme.Modern);

        await OpenMenuAsync(menu.MenuId);

        await Assertions.Expect(LanguageButton(Page)).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("language-footer")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("language-sheet")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("link[rel='alternate'][hreflang]")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "de");
        await Assertions.Expect(Page.Locator("meta[property='og:locale']")).ToHaveAttributeAsync("content", "de_DE");
    }

    [Fact]
    public async Task LanguageSheet_ListsEndonyms_MarksTheCurrent_AndBehavesLikeASheet()
    {
        var group = await SeedGroupAsync();
        await OpenMenuAsync(group.German.MenuId);

        var button = LanguageButton(Page);
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-label", "Sprache wählen");
        await Assertions.Expect(button).ToHaveTextAsync("DE");
        await button.ClickAsync();

        var sheet = Page.GetByTestId("language-sheet");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await Assertions.Expect(sheet).ToHaveAttributeAsync("aria-modal", "true");
        await Assertions.Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = "Sprache wählen" })).ToBeVisibleAsync();
        var options = sheet.GetByTestId("language-option");
        await Assertions.Expect(options.Locator(".gm-langlist-name")).ToHaveTextAsync(new[] { "Deutsch", "English", "Français" });
        await Assertions.Expect(options.Locator(".gm-langlist-hint")).ToHaveTextAsync(new[] { "Englisch", "Französisch" });
        await Assertions.Expect(options.Nth(0)).ToHaveAttributeAsync("aria-current", "true");
        await Assertions.Expect(options.Nth(1)).Not.ToHaveAttributeAsync("aria-current", "true");
        await Assertions.Expect(options.Nth(2)).ToHaveAttributeAsync("lang", "fr");
        await Assertions.Expect(options.Nth(2)).ToHaveAttributeAsync("hreflang", "fr");
        (await options.Nth(1).BoundingBoxAsync())!.Height.Should().BeGreaterThanOrEqualTo(40);

        // Escape closes it and the focus returns to the button
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(sheet).ToBeHiddenAsync();
        await Assertions.Expect(button).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Switch_FromMenuPage_GoesToThePublicVersion_WithoutTracking()
    {
        var group = await SeedGroupAsync();
        var requests = new List<string>();
        Page.Request += (_, r) => requests.Add(r.Url);
        await OpenMenuAsync(group.German.MenuId);

        await LanguageButton(Page).ClickAsync();
        await Page.GetByTestId("language-sheet").GetByRole(AriaRole.Link, new() { Name = "English" }).ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menu/{group.English.MenuId}$"));
        await WaitForGuestReadyAsync(Page);
        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "en");
        await Assertions.Expect(LanguageButton(Page)).ToHaveTextAsync("EN");
        await Assertions.Expect(LanguageButton(Page)).ToHaveAttributeAsync("aria-label", "Choose language");
        requests.Should().NotContain(u => u.Contains("/guest/api/"), "switching the language records nothing");
        (await CountTrackingAsync(group.German.MenuId, group.English.MenuId)).Should().Be((0, 0));

        // And back: the German version, its button says DE again
        await LanguageButton(Page).ClickAsync();
        await Page.GetByTestId("language-sheet").GetByRole(AriaRole.Link, new() { Name = "Deutsch" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menu/{group.German.MenuId}$"));
        await WaitForGuestReadyAsync(Page);
        await Assertions.Expect(LanguageButton(Page)).ToHaveTextAsync("DE");
    }

    [Fact]
    public async Task Switch_FromQrCode_GoesToThePublicMenu_WithoutItem_AndRecordsNoView()
    {
        var group = await SeedGroupAsync();
        var qrId = Guid.NewGuid();
        await App.SeedAsync(group.OwnerId, db =>
        {
            db.QRCodes.Add(new QRCode { Id = qrId, Title = "Tisch 7", MenuId = group.German.MenuId });
            return Task.CompletedTask;
        });

        await OpenGuestPageAsync(Page, $"/qr/{qrId}");
        (await CountTrackingAsync(group.German.MenuId)).Views.Should().Be(1);
        await Assertions.Expect(Page.GetByTestId("language-sheet").Locator("a[data-language='en']"))
            .ToHaveAttributeAsync("href", $"/menu/{group.English.MenuId}");
        var english = Page.GetByTestId("language-sheet").GetByRole(AriaRole.Link, new() { Name = "English" });

        // An item opened on the QR page: the language links still go to the plain public page (no ?item=)
        await ClickMenuItemAsync("Burrata");
        await CloseItemSheetAsync(Page);
        await LanguageButton(Page).ClickAsync();
        await english.ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menu/{group.English.MenuId}$"));
        await WaitForGuestReadyAsync(Page);

        var (views, clicks) = await CountTrackingAsync(group.German.MenuId, group.English.MenuId);
        views.Should().Be(1, "only the QR scan counts, not the switch");
        clicks.Should().Be(1, "only the opened item");

        // A ?item= request on /qr renders the links without the item as well
        await OpenGuestPageAsync(Page, $"/qr/{qrId}?item={group.German.Items["Burrata"]}");
        await Assertions.Expect(Page.GetByTestId("language-footer").GetByRole(AriaRole.Link, new() { Name = "English" }))
            .ToHaveAttributeAsync("href", $"/menu/{group.English.MenuId}");
    }

    [Fact]
    public async Task Switch_InOwnerPreview_GoesToThePreviewOfTheVersion_NoAlternates()
    {
        var email = await RegisterNewOwnerAsync();
        var group = await SeedGroupAsync(await OwnerIdAsync(email));
        var requests = new List<string>();
        Page.Request += (_, r) => requests.Add(r.Url);

        await OpenGuestPageAsync(Page, $"/menu/preview/{group.German.MenuId}?theme=dark");
        await Assertions.Expect(Page.Locator("meta[name='robots']")).ToHaveAttributeAsync("content", "noindex");
        await Assertions.Expect(Page.Locator("link[rel='alternate'][hreflang]")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("meta[property='og:locale:alternate']")).ToHaveCountAsync(0);

        await LanguageButton(Page).ClickAsync();
        var english = Page.GetByTestId("language-sheet").GetByRole(AriaRole.Link, new() { Name = "English" });
        await Assertions.Expect(english).ToHaveAttributeAsync("href", $"/menu/preview/{group.English.MenuId}");
        await english.ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menu/preview/{group.English.MenuId}$"));
        await WaitForGuestReadyAsync(Page);
        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "en");
        await Assertions.Expect(Page.GetByTestId("menu-display")).ToHaveAttributeAsync("data-preview", "true");
        requests.Should().NotContain(u => u.Contains("/guest/api/"));
        (await CountTrackingAsync(group.German.MenuId, group.English.MenuId)).Should().Be((0, 0));
    }

    [Fact]
    public async Task WithoutJavaScript_TheButtonLinksToTheLanguageList()
    {
        var group = await SeedGroupAsync();
        await using var noJs = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, JavaScriptEnabled = false, ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        var page = await noJs.NewPageAsync();
        await page.GotoAsync($"/menu/{group.German.MenuId}");

        var button = page.GetByTestId("language-button");
        await Assertions.Expect(button).ToHaveAttributeAsync("href", "#gm-languages");
        await button.ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("#gm-languages$"));
        var list = page.GetByTestId("language-footer");
        await Assertions.Expect(list).ToBeInViewportAsync();
        await Assertions.Expect(list.GetByRole(AriaRole.Heading, new() { Name = "Sprachen" })).ToBeVisibleAsync();
        await Assertions.Expect(list.Locator("[aria-current='true']")).ToHaveTextAsync(new Regex("^\\s*Deutsch"));

        await list.GetByRole(AriaRole.Link, new() { Name = "English" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex($"/menu/{group.English.MenuId}$"));
        await Assertions.Expect(page.Locator("h1")).ToHaveTextAsync(GuestDemoData.EnglishMenuName);
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "en");
    }

    [Fact]
    public async Task PublicPages_LinkEveryVersionWithHreflang_AndOgLocale()
    {
        var group = await SeedGroupAsync();
        var origin = new Uri(App.ServerAddress).GetLeftPart(UriPartial.Authority);

        await OpenMenuAsync(group.English.MenuId);

        var alternates = Page.Locator("link[rel='alternate'][hreflang]");
        await Assertions.Expect(alternates).ToHaveCountAsync(3);
        await Assertions.Expect(Page.Locator("link[rel='alternate'][hreflang='de']")).ToHaveAttributeAsync("href", $"{origin}/menu/{group.German.MenuId}");
        await Assertions.Expect(Page.Locator("link[rel='alternate'][hreflang='en']")).ToHaveAttributeAsync("href", $"{origin}/menu/{group.English.MenuId}");
        await Assertions.Expect(Page.Locator("link[rel='alternate'][hreflang='fr']")).ToHaveAttributeAsync("href", $"{origin}/menu/{group.FrenchId}");
        await Assertions.Expect(Page.Locator("meta[property='og:locale']")).ToHaveAttributeAsync("content", "en_GB");
        var localeAlternates = await Page.Locator("meta[property='og:locale:alternate']").EvaluateAllAsync<string[]>("els => els.map(e => e.content)");
        localeAlternates.Should().BeEquivalentTo("de_DE", "fr_FR");

        // The QR page of a version is public too
        var qrId = Guid.NewGuid();
        await App.SeedAsync(group.OwnerId, db =>
        {
            db.QRCodes.Add(new QRCode { Id = qrId, Title = "Terrasse", MenuId = group.German.MenuId });
            return Task.CompletedTask;
        });
        await OpenGuestPageAsync(Page, $"/qr/{qrId}");
        await Assertions.Expect(alternates).ToHaveCountAsync(3);
        await Assertions.Expect(Page.Locator("meta[property='og:locale']")).ToHaveAttributeAsync("content", "de_DE");
    }

    // --- The guest UI in the menu's language ----------------------------------------------------------------------------

    [Fact]
    public async Task EnglishMenu_ShowsTheGuestUiInEnglish()
    {
        var group = await SeedGroupAsync(withFrench: false);
        await OpenMenuAsync(group.English.MenuId);

        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "en");
        await Assertions.Expect(Page).ToHaveTitleAsync($"{GuestDemoData.EnglishMenuName} – Menu");
        await Assertions.Expect(Page.Locator(".gm-skip")).ToHaveTextAsync("Skip to the menu");
        await Assertions.Expect(Page.GetByTestId("search-button")).ToHaveAttributeAsync("aria-label", "Search the menu");
        await Assertions.Expect(Page.GetByTestId("info-button")).ToHaveAttributeAsync("aria-label", "About the restaurant");
        await Assertions.Expect(Page.GetByTestId("category-nav")).ToHaveAttributeAsync("aria-label", "Categories");
        await Assertions.Expect(Page.GetByTestId("category-all")).ToHaveAttributeAsync("aria-label", "Show all categories");
        await Assertions.Expect(Page.GetByTestId("filter-button")).ToHaveAttributeAsync("aria-label", "Filter");

        // Label chips, prices ("€9.90", "from"), allergen title, heart labels
        var penne = ItemEntry(GuestDemoData.DishWithManyLabels);
        await Assertions.Expect(penne.GetByTestId("label-popular")).ToHaveTextAsync("Popular");
        await Assertions.Expect(penne.GetByTestId("label-vegan")).ToHaveTextAsync("Vegan");
        await Assertions.Expect(penne.GetByTestId("label-spicy")).ToHaveTextAsync("Spicy");
        await Assertions.Expect(penne.GetByTestId("item-price")).ToHaveTextAsync("€12.50");
        await Assertions.Expect(penne.GetByTestId("item-allergens")).ToHaveAttributeAsync("title", "Allergens");
        await Assertions.Expect(penne.GetByTestId("favorite-button")).ToHaveAttributeAsync("aria-label", $"Add {GuestDemoData.DishWithManyLabels} to favourites");
        await Assertions.Expect(ItemEntry(GuestDemoData.EnglishLemonadeName).GetByTestId("item-price")).ToHaveTextAsync("from €4.20");
        await Assertions.Expect(ItemEntry("Mineral water 0.5 l").GetByTestId("label-alcohol-free")).ToHaveTextAsync("Alcohol-free");
        await Assertions.Expect(ItemEntry("Burrata").GetByTestId("label-vegetarian")).ToHaveTextAsync("Vegetarian");

        // Item sheet
        await ClickMenuItemAsync(GuestDemoData.EnglishLemonadeName);
        await Assertions.Expect(ItemSheet.GetByTestId("item-variant").First).ToContainTextAsync("€4.20");
        await Assertions.Expect(ItemSheet.GetByTestId("share-button")).ToHaveTextAsync("Share");
        await Assertions.Expect(ItemSheet.Locator("[data-sheet-close]")).ToHaveAttributeAsync("aria-label", "Close");
        await Assertions.Expect(ItemSheet.GetByTestId("item-favorite-button")).ToHaveAttributeAsync("aria-label", $"Add {GuestDemoData.EnglishLemonadeName} to favourites");
        await CloseItemSheetAsync(Page);
        await ClickMenuItemAsync("Burrata");
        await Assertions.Expect(ItemSheet.GetByTestId("allergen-legend").GetByRole(AriaRole.Heading)).ToHaveTextAsync("Allergens");
        await Assertions.Expect(ItemSheet.GetByTestId("allergen-legend")).ToContainTextAsync("Milk");
        await CloseItemSheetAsync(Page);

        // Info sheet
        await Page.GetByTestId("info-button").ClickAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = "Information" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("info-sheet")).ToContainTextAsync("Opening hours");
        await Assertions.Expect(Page.GetByTestId("info-sheet").GetByTestId("info-review")).ToHaveTextAsync("Write a review");
        await Page.Keyboard.PressAsync("Escape");
    }

    [Fact]
    public async Task EnglishMenu_ScriptStrings_SearchFilterFavouritesShare_InEnglish()
    {
        var group = await SeedGroupAsync(withFrench: false);
        await OpenMenuAsync(group.English.MenuId);

        // Search: result count (plural/singular), empty state, label text in English
        await Page.GetByTestId("search-button").ClickAsync();
        var search = Page.GetByTestId("search-sheet");
        await Assertions.Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = "Search" })).ToBeVisibleAsync();
        await Assertions.Expect(search.Locator("input")).ToHaveAttributeAsync("placeholder", "Dish, ingredient or label");
        await search.Locator("input").FillAsync("vegetarian");
        await Assertions.Expect(search.Locator("[data-search-status]")).ToHaveTextAsync(new Regex("^\\d+ results$"));
        await search.Locator("input").FillAsync("halal");
        await Assertions.Expect(search.Locator("[data-search-status]")).ToHaveTextAsync("1 result");
        await search.Locator("input").FillAsync("zzzq");
        await Assertions.Expect(search.GetByTestId("search-empty")).ToContainTextAsync("No results for “zzzq”");
        await Assertions.Expect(search.GetByTestId("search-empty")).ToContainTextAsync("Try another word");
        await search.GetByRole(AriaRole.Button, new() { Name = "Close" }).ClickAsync();
        await Assertions.Expect(search).ToBeHiddenAsync();

        // Filter: apply button with the live count, singular and plural; notice; badge label
        await Page.GetByTestId("filter-button").ClickAsync();
        var filter = Page.GetByTestId("filter-sheet");
        await Assertions.Expect(filter.GetByRole(AriaRole.Heading, new() { Name = "Diet" })).ToBeVisibleAsync();
        await Assertions.Expect(filter.Locator("legend")).ToHaveTextAsync("Without allergens");
        await Assertions.Expect(filter.GetByTestId("filter-label-halal")).ToHaveTextAsync("Halal");
        await filter.GetByTestId("filter-label-halal").ClickAsync();
        await Assertions.Expect(filter.GetByTestId("filter-apply")).ToHaveTextAsync("Show 1 dish");
        await filter.GetByTestId("filter-label-halal").ClickAsync();
        await filter.GetByTestId("filter-label-vegan").ClickAsync();
        await Assertions.Expect(filter.GetByTestId("filter-apply")).ToHaveTextAsync("Show 5 dishes");
        await Assertions.Expect(filter.GetByTestId("filter-reset")).ToHaveTextAsync("Reset");
        await filter.GetByTestId("filter-apply").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("filter-notice")).ToContainTextAsync("Filter on · 5 dishes");
        await Assertions.Expect(Page.GetByTestId("filter-button")).ToHaveAttributeAsync("aria-label", "Filter (1 active)");
        await Page.GetByTestId("filter-notice").GetByRole(AriaRole.Button, new() { Name = "Reset" }).ClickAsync();
        await Assertions.Expect(Page.GetByTestId("filter-notice")).ToBeHiddenAsync();

        // Favourites: unit line and sum in the English notation
        await ItemEntry("Espresso").GetByTestId("favorite-button").ClickAsync();
        await ItemEntry(GuestDemoData.EnglishLemonadeName).GetByTestId("favorite-button").ClickAsync();
        await Assertions.Expect(ItemEntry("Espresso").GetByTestId("favorite-button")).ToHaveAttributeAsync("aria-label", "Remove Espresso from favourites");
        await Page.GetByTestId("favorites-button").ClickAsync();
        var favorites = Page.GetByTestId("favorites-sheet");
        await Assertions.Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = "Favourites" })).ToBeVisibleAsync();
        await Assertions.Expect(favorites.GetByTestId("favorites-sum")).ToHaveTextAsync("€7.10");
        var espresso = favorites.GetByTestId("favorite-row").Filter(new() { HasText = "Espresso" });
        await espresso.GetByRole(AriaRole.Button, new() { Name = "One portion more" }).ClickAsync();
        await Assertions.Expect(espresso.GetByTestId("favorite-unit")).ToHaveTextAsync("2 × €2.90");
        await Assertions.Expect(favorites.GetByTestId("favorites-sum")).ToHaveTextAsync("€10.00");
        await Assertions.Expect(favorites.GetByTestId("favorite-variant")).ToContainTextAsync("0.5 l · €5.90");
        await favorites.GetByTestId("favorites-clear").ClickAsync();
        await Assertions.Expect(favorites.GetByTestId("favorites-confirm")).ToContainTextAsync("Clear your favourites list?");
        await Assertions.Expect(favorites.GetByTestId("favorites-confirm").GetByRole(AriaRole.Button, new() { Name = "Yes, clear" })).ToBeVisibleAsync();
        await favorites.GetByTestId("favorites-confirm").GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Page.Keyboard.PressAsync("Escape");

        // Share: "Link copied"
        await Page.EvaluateAsync("() => { Object.defineProperty(navigator, 'share', { value: undefined, configurable: true }); Object.defineProperty(navigator, 'clipboard', { value: { writeText: () => Promise.resolve() }, configurable: true }); }");
        await ClickMenuItemAsync("Burrata");
        await ItemSheet.GetByTestId("share-button").ClickAsync();
        await Assertions.Expect(ItemSheet.GetByTestId("share-status")).ToHaveTextAsync("Link copied");
    }

    [Fact]
    public async Task GermanMenu_KeepsTheGermanUi()
    {
        var group = await SeedGroupAsync(withFrench: false);
        await OpenMenuAsync(group.German.MenuId);

        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "de");
        await Assertions.Expect(Page.GetByTestId("search-button")).ToHaveAttributeAsync("aria-label", "Speisekarte durchsuchen");
        var penne = ItemEntry(GuestDemoData.DishWithManyLabels);
        await Assertions.Expect(penne.GetByTestId("item-price")).ToHaveTextAsync("€ 12,50");
        await Assertions.Expect(penne.GetByTestId("label-popular")).ToHaveTextAsync("Beliebt");
        await Assertions.Expect(ItemEntry(GuestDemoData.LemonadeName).GetByTestId("item-price")).ToHaveTextAsync("ab € 4,20");
        await Page.GetByTestId("filter-button").ClickAsync();
        await Page.GetByTestId("filter-sheet").GetByTestId("filter-label-halal").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("filter-sheet").GetByTestId("filter-apply")).ToHaveTextAsync("1 Gericht anzeigen");
    }

    [Fact]
    public async Task ItalianMenu_FallsBackToTheEnglishUi()
    {
        var owner = Guid.NewGuid();
        var menuId = await GuestDemoData.SeedSmallMenuAsync(App, owner, MenuLanguage.Italian, "Menù del giorno", "Lasagne al forno", 11.50m);

        await OpenMenuAsync(menuId);

        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", "it");
        await Assertions.Expect(Page.GetByTestId("search-button")).ToHaveAttributeAsync("aria-label", "Search the menu");
        await Assertions.Expect(ItemEntry("Lasagne al forno").GetByTestId("item-price")).ToHaveTextAsync("€11.50");
        await Assertions.Expect(ItemEntry("Lasagne al forno").GetByTestId("label-popular")).ToHaveTextAsync("Popular");
        await Assertions.Expect(Page.Locator("meta[property='og:locale']")).ToHaveAttributeAsync("content", "it_IT");
        await Assertions.Expect(LanguageButton(Page)).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Favourites_StayPerVersion_WhenSwitchingBackAndForth()
    {
        var group = await SeedGroupAsync(withFrench: false);
        await OpenMenuAsync(group.German.MenuId);
        await ItemEntry("Burrata").GetByTestId("favorite-button").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("favorites-count")).ToHaveTextAsync("1");

        await LanguageButton(Page).ClickAsync();
        await Page.GetByTestId("language-sheet").GetByRole(AriaRole.Link, new() { Name = "English" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menu/{group.English.MenuId}$"));
        await WaitForGuestReadyAsync(Page);
        await Assertions.Expect(Page.GetByTestId("favorites-button")).ToBeHiddenAsync();
        await ItemEntry("Espresso").GetByTestId("favorite-button").ClickAsync();
        await ItemEntry("Cappuccino").GetByTestId("favorite-button").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("favorites-count")).ToHaveTextAsync("2");

        // Back returns to the German menu itself (the sheet was closed before leaving), one step
        await Page.GoBackAsync();
        await WaitForGuestReadyAsync(Page);
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menu/{group.German.MenuId}$"));
        await Assertions.Expect(Page.GetByTestId("language-sheet")).ToBeHiddenAsync();
        (await Page.EvaluateAsync<bool>("() => history.state?.gm == null")).Should().BeTrue("no sheet entry is left in the history");
        await Assertions.Expect(Page.GetByTestId("favorites-count")).ToHaveTextAsync("1");
        await Assertions.Expect(ItemEntry("Burrata").GetByTestId("favorite-button")).ToHaveAttributeAsync("aria-pressed", "true");
        await Page.GoForwardAsync();
        await WaitForGuestReadyAsync(Page);
        await Assertions.Expect(Page.GetByTestId("favorites-count")).ToHaveTextAsync("2");
    }

    [Fact]
    public async Task TopBar_At360px_FitsInfoLanguageFilterAndSearch()
    {
        var group = await SeedGroupAsync(withFrench: false);
        await Page.SetViewportSizeAsync(360, 780);
        await OpenMenuAsync(group.German.MenuId);
        await Page.Mouse.WheelAsync(0, 900);
        await Page.WaitForTimeoutAsync(500);

        var boxes = new List<LocatorBoundingBoxResult>();
        foreach (var id in new[] { "info-button", "language-button", "filter-button", "search-button" })
        {
            var box = await Page.GetByTestId(id).BoundingBoxAsync();
            box.Should().NotBeNull(id);
            box!.Height.Should().BeGreaterThanOrEqualTo(40, id);
            box.Width.Should().BeGreaterThanOrEqualTo(40, id);
            boxes.Add(box);
        }
        var title = (await Page.GetByTestId("guest-topbar").Locator(".gm-topbar-center").BoundingBoxAsync())!;
        // Left to right without overlaps, all inside the viewport
        boxes[0].X.Should().BeGreaterThanOrEqualTo(0);
        (boxes[0].X + boxes[0].Width).Should().BeLessThanOrEqualTo(boxes[1].X);
        (boxes[1].X + boxes[1].Width).Should().BeLessThanOrEqualTo(title.X + 0.5f);
        (title.X + title.Width).Should().BeLessThanOrEqualTo(boxes[2].X + 0.5f);
        (boxes[2].X + boxes[2].Width).Should().BeLessThanOrEqualTo(boxes[3].X);
        (boxes[3].X + boxes[3].Width).Should().BeLessThanOrEqualTo(360);
        var overflow = await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
        overflow.Should().BeFalse();
    }
}
