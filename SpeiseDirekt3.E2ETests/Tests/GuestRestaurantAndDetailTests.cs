using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Guest view, package AP3: the restaurant profile on the guest pages (hero with cover, logo and name, info sheet,
/// footer reachable without JS, nothing of it without a profile), category subtitles, the item sheet with price
/// variants, extras and "Teilen" (Web Share, clipboard, read-only field; always the public link), the label chips
/// (highlights first, compact on cards) and the page's title, description and Open Graph tags.
/// Data: the seeded owner's profile and "E2E Mezze" menu (<see cref="E2ESeedData"/>), and the demo menu of
/// <see cref="GuestDemoData"/> in fresh tenants (profile with cover and logo, or none).
/// </summary>
public class GuestRestaurantAndDetailTests : BackOfficeTestBase
{
    public GuestRestaurantAndDetailTests(E2EFixture fixture) : base(fixture) { }

    private static readonly Guid MezzeMenuId = E2ESeedData.MezzeMenuId;

    /// <summary>The demo menu in a tenant of its own, with (default) or without the restaurant profile.</summary>
    private Task<GuestDemoData.Seeded> SeedDemoAsync(string name = GuestDemoData.MenuName, bool withProfile = true, Guid? ownerId = null) =>
        GuestDemoData.SeedAsync(App, ownerId ?? Guid.NewGuid(), DesignTheme.Modern, name, withProfile);

    private string PublicUrl(Guid menuId, Guid? itemId = null) =>
        $"{App.ServerAddress}/menu/{menuId}" + (itemId is { } id ? $"?item={id}" : "");

    private async Task UsePhoneAsync() => await Page.SetViewportSizeAsync(390, 844);

    private ILocator Section(string name) => Page.Locator($"[data-testid='category-section'][data-category='{name}']");

    private Task<string?> MetaAsync(string selector) => Page.Locator(selector).GetAttributeAsync("content");

    private async Task<string> ImagePathAsync(Func<SpeiseDirekt.Data.ApplicationDbContext, Task<string?>> query)
    {
        await using var db = App.CreateDbContext(Guid.Empty);
        return (await query(db))!;
    }

    // --- Hero ---

    [Fact]
    public async Task Hero_WithProfile_ShowsCoverLogoAndRestaurantName_AboveTheMenuName()
    {
        var demo = await SeedDemoAsync(name: "Abendkarte");
        await UsePhoneAsync();
        await OpenMenuAsync(demo.MenuId);

        var hero = Page.GetByTestId("guest-hero");
        var cover = hero.GetByTestId("hero-cover");
        var logo = hero.GetByTestId("hero-logo");
        await Assertions.Expect(cover).ToBeVisibleAsync();
        await Assertions.Expect(cover).ToHaveAttributeAsync("src", new Regex("^/image/"));
        // Size reserved before the picture arrives (no layout shift), and it does load
        await Assertions.Expect(cover).ToHaveAttributeAsync("width", "1200");
        await Assertions.Expect(cover).ToHaveAttributeAsync("height", "600");
        await Assertions.Expect(logo).ToBeVisibleAsync();
        (await cover.EvaluateAsync<int>("img => img.complete ? img.naturalWidth : new Promise(r => img.onload = () => r(img.naturalWidth))"))
            .Should().BeGreaterThan(0);
        (await ComputedAsync(cover, "objectFit")).Should().Be("cover");

        await Assertions.Expect(hero.GetByTestId("hero-restaurant")).ToHaveTextAsync(GuestDemoData.RestaurantName);
        await Assertions.Expect(Page.Locator("h1")).ToHaveTextAsync("Abendkarte");
        await Assertions.Expect(hero).ToContainTextAsync(GuestDemoData.MenuDescription);

        // The logo overlaps the cover's lower edge
        var coverBox = (await cover.BoundingBoxAsync())!;
        var logoBox = (await logo.BoundingBoxAsync())!;
        logoBox.Y.Should().BeLessThan(coverBox.Y + coverBox.Height);
        (logoBox.Y + logoBox.Height).Should().BeGreaterThan(coverBox.Y + coverBox.Height);
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth")).Should().BeTrue("nothing overflows at 390 px");

        // Top bar: logo + menu name appear once the hero has scrolled away
        var brand = Page.GetByTestId("guest-topbar").Locator(".gm-topbar-brand");
        await Assertions.Expect(brand).ToHaveCSSAsync("opacity", "0");
        await Page.Mouse.WheelAsync(0, 700);
        await Assertions.Expect(brand).ToHaveCSSAsync("opacity", "1");
        await Assertions.Expect(brand).ToContainTextAsync("Abendkarte");
    }

    [Fact]
    public async Task Hero_MenuNamedLikeTheRestaurant_ShowsTheNameOnce()
    {
        var demo = await SeedDemoAsync();
        await OpenMenuAsync(demo.MenuId);

        await Assertions.Expect(Page.Locator("h1")).ToHaveTextAsync(GuestDemoData.RestaurantName);
        await Assertions.Expect(Page.GetByTestId("hero-restaurant")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("hero-logo")).ToBeVisibleAsync();
        await Assertions.Expect(Page).ToHaveTitleAsync($"{GuestDemoData.MenuName} – Speisekarte");
    }

    [Fact]
    public async Task Hero_ProfileWithoutPictures_ShowsOnlyTheName()
    {
        await OpenMenuAsync(MezzeMenuId);

        await Assertions.Expect(Page.GetByTestId("hero-restaurant")).ToHaveTextAsync(E2ESeedData.RestaurantName);
        await Assertions.Expect(Page.Locator("h1")).ToHaveTextAsync(E2ESeedData.MezzeMenuName);
        await Assertions.Expect(Page.GetByTestId("hero-cover")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("hero-logo")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator(".gm-topbar-brand")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task MenuWithoutProfile_HasNoHeroMedia_NoInfoButton_AndNoFooter()
    {
        var demo = await SeedDemoAsync(withProfile: false);
        await OpenMenuAsync(demo.MenuId);

        await Assertions.Expect(Page.Locator("h1")).ToHaveTextAsync(GuestDemoData.MenuName);
        await Assertions.Expect(Page.GetByTestId("guest-hero").Locator("img")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("hero-restaurant")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("info-button")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("restaurant-footer")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("info-sheet")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator(".gm-topbar-brand, .gm-hero-brand")).ToHaveCountAsync(0);
        await Assertions.Expect(Page).ToHaveTitleAsync($"{GuestDemoData.MenuName} – Speisekarte");
    }

    // --- Info sheet and footer ---

    [Fact]
    public async Task InfoSheet_ShowsTheRestaurant_WithContactLinks()
    {
        var external = new List<string>();
        var origin = new Uri(App.ServerAddress).GetLeftPart(UriPartial.Authority);
        Page.Request += (_, r) => { if (!r.Url.StartsWith(origin, StringComparison.OrdinalIgnoreCase)) lock (external) external.Add(r.Url); };
        await UsePhoneAsync();
        await OpenMenuAsync(MezzeMenuId);

        var button = Page.GetByTestId("info-button");
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-label", "Informationen zum Restaurant");
        await button.ClickAsync();

        var sheet = Page.GetByTestId("info-sheet");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await Assertions.Expect(sheet).ToHaveAttributeAsync("aria-modal", "true");
        await Assertions.Expect(Page.GetByRole(AriaRole.Dialog, new() { Name = "Informationen" })).ToBeVisibleAsync();
        await Assertions.Expect(sheet.GetByRole(AriaRole.Heading, new() { Name = E2ESeedData.RestaurantName })).ToBeVisibleAsync();
        await Assertions.Expect(sheet.GetByTestId("info-description")).ToContainTextAsync("Levantinische Küche");
        // Opening hours keep their line breaks
        (await sheet.GetByTestId("info-hours").InnerTextAsync()).Should().Be(E2ESeedData.RestaurantOpeningHours);

        var maps = sheet.GetByTestId("info-address");
        await Assertions.Expect(maps).ToHaveAttributeAsync("href",
            "https://www.google.com/maps/search/?api=1&query=" + Uri.EscapeDataString($"{E2ESeedData.RestaurantName}, Marktplatz 1, 1010 Wien"));
        await Assertions.Expect(maps).ToContainTextAsync("Marktplatz 1");
        await Assertions.Expect(sheet.GetByTestId("info-phone")).ToHaveAttributeAsync("href", "tel:+4312345678");
        await Assertions.Expect(sheet.GetByTestId("info-email")).ToHaveAttributeAsync("href", "mailto:hallo@example.com");
        await Assertions.Expect(sheet.GetByTestId("info-website")).ToHaveAttributeAsync("href", "https://example.com");
        await Assertions.Expect(sheet.GetByTestId("info-instagram")).ToHaveAttributeAsync("href", "https://example.com/instagram");
        await Assertions.Expect(sheet.GetByTestId("info-facebook")).ToHaveAttributeAsync("href", "https://example.com/facebook");
        await Assertions.Expect(sheet.GetByTestId("info-review")).ToHaveAttributeAsync("href", "https://example.com/review");
        await Assertions.Expect(sheet.GetByTestId("info-review")).ToHaveTextAsync("Bewertung schreiben");
        foreach (var id in new[] { "info-address", "info-website", "info-instagram", "info-facebook", "info-review" })
        {
            await Assertions.Expect(sheet.GetByTestId(id)).ToHaveAttributeAsync("target", "_blank");
            await Assertions.Expect(sheet.GetByTestId(id)).ToHaveAttributeAsync("rel", new Regex(@"\bnoopener\b"));
        }
        // tel:/mailto: stay in the page
        await Assertions.Expect(sheet.GetByTestId("info-phone")).Not.ToHaveAttributeAsync("target", "_blank");
        // Icons are inline SVG
        await Assertions.Expect(sheet.GetByTestId("info-phone").Locator("svg").First).ToBeVisibleAsync();

        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(sheet).ToBeHiddenAsync();
        await Assertions.Expect(button).ToBeFocusedAsync();
        external.Should().BeEmpty("the address is a link: no map or anything else is loaded from other hosts");
    }

    [Fact]
    public async Task Footer_ShowsTheRestaurant_AtTheEndOfThePage()
    {
        await OpenMenuAsync(MezzeMenuId);

        var footer = Page.GetByTestId("restaurant-footer");
        await Assertions.Expect(footer).ToHaveAttributeAsync("id", "gm-info");
        await Assertions.Expect(footer.GetByRole(AriaRole.Heading, new() { Name = E2ESeedData.RestaurantName, Level = 2 })).ToBeVisibleAsync();
        await Assertions.Expect(footer.GetByTestId("info-phone")).ToHaveAttributeAsync("href", "tel:+4312345678");
        // After the menu
        (await Page.EvaluateAsync<bool>(
            "() => !!(document.getElementById('gm-main').compareDocumentPosition(document.getElementById('gm-info')) & Node.DOCUMENT_POSITION_FOLLOWING)"))
            .Should().BeTrue();
    }

    [Fact]
    public async Task Info_WithoutJavaScript_TheButtonJumpsToTheFooter()
    {
        await using var context = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, JavaScriptEnabled = false, ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync($"/menu/{MezzeMenuId}");

        var button = page.GetByTestId("info-button");
        await Assertions.Expect(button).ToBeVisibleAsync();
        await Assertions.Expect(button).ToHaveAttributeAsync("href", "#gm-info");
        await button.ClickAsync();

        await Assertions.Expect(page).ToHaveURLAsync(new Regex("#gm-info$"));
        var footer = page.GetByTestId("restaurant-footer");
        var heading = footer.GetByRole(AriaRole.Heading, new() { Name = E2ESeedData.RestaurantName });
        await Assertions.Expect(heading).ToBeInViewportAsync();
        (await heading.EvaluateAsync<double>("e => e.getBoundingClientRect().top")).Should().BeGreaterThan(112, "the heading is not hidden under the sticky bars");
        await Assertions.Expect(footer.GetByTestId("info-hours")).ToContainTextAsync("So Ruhetag");
        await Assertions.Expect(footer.GetByTestId("info-phone")).ToHaveAttributeAsync("href", "tel:+4312345678");
    }

    // --- Category subtitles ---

    [Fact]
    public async Task CategorySubtitles_AreShownUnderTheHeadings()
    {
        await OpenMenuAsync(MezzeMenuId);

        foreach (var (name, subtitle) in E2ESeedData.MezzeCategories)
        {
            var section = Section(name);
            await Assertions.Expect(section.Locator("h2")).ToHaveTextAsync(name);
            if (subtitle is null)
                await Assertions.Expect(section.Locator(".gm-section-subtitle")).ToHaveCountAsync(0);
            else
                await Assertions.Expect(section.Locator(".gm-section-subtitle")).ToHaveTextAsync(subtitle);
        }
        var heading = await ComputedAsync(Section("Mezze").Locator("h2"), "color");
        (await ComputedAsync(Section("Mezze").Locator(".gm-section-subtitle"), "color")).Should().NotBe(heading, "the subtitle is muted");
    }

    // --- Item detail: variants, extras ---

    [Fact]
    public async Task Variants_FromPriceOnTheCard_AndTheListInTheSheet()
    {
        await OpenMenuAsync(MezzeMenuId);

        await Assertions.Expect(ItemEntry("Hausgemachte Limonade").GetByTestId("item-price")).ToHaveTextAsync("ab € 3,90");
        await Assertions.Expect(ItemEntry("Hausbier").GetByTestId("item-price")).ToHaveTextAsync("ab € 3,40");
        await Assertions.Expect(ItemEntry("Falafel-Teller").GetByTestId("item-price")).ToHaveTextAsync("ab € 12,50");

        await ClickMenuItemAsync("Hausgemachte Limonade");
        var variants = ItemSheet.GetByTestId("item-variants");
        await Assertions.Expect(variants).ToBeVisibleAsync();
        await Assertions.Expect(variants.Locator(".gm-variant-name")).ToHaveTextAsync(new[] { "0,3 l", "0,5 l" });
        await Assertions.Expect(variants.Locator(".gm-price")).ToHaveTextAsync(new[] { "€ 3,90", "€ 5,40" });
        // The list replaces the single price: no price twice
        await Assertions.Expect(ItemSheet.GetByTestId("item-price")).ToHaveCountAsync(0);
        await Assertions.Expect(ItemSheet.GetByTestId("item-extras")).ToHaveCountAsync(0);
        await CloseItemSheetAsync(Page);

        // Without variants: the single price as before
        await ClickMenuItemAsync("Hummus");
        await Assertions.Expect(ItemSheet.GetByTestId("item-price")).ToHaveTextAsync("€ 7,50");
        await Assertions.Expect(ItemSheet.GetByTestId("item-variants")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Extras_AreListedWithPlusPrices_AndKeepTheItemPrice()
    {
        await OpenMenuAsync(MezzeMenuId);

        await ClickMenuItemAsync("Fattoush");

        await Assertions.Expect(ItemSheet.GetByTestId("item-price")).ToHaveTextAsync("€ 11,90");
        var extras = ItemSheet.GetByTestId("item-extras");
        await Assertions.Expect(extras.GetByRole(AriaRole.Heading, new() { Name = "Extras" })).ToBeVisibleAsync();
        // The leading "+" of the stored names moves to the price
        await Assertions.Expect(extras.Locator(".gm-extra-name")).ToHaveTextAsync(new[] { "Halloumi", "Hähnchen" });
        await Assertions.Expect(extras.Locator(".gm-extra-price")).ToHaveTextAsync(new[] { "+ € 6,00", "+ € 5,00" });
    }

    [Fact]
    public async Task DeepLink_RendersVariantsAndExtrasOnTheServer_WithoutJavaScript()
    {
        await using var context = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, JavaScriptEnabled = false, ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync($"/menu/{MezzeMenuId}?item={E2ESeedData.FalafelId}");
        var sheet = ItemSheetOf(page);
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await Assertions.Expect(sheet.GetByTestId("item-variants").Locator(".gm-variant-name")).ToHaveTextAsync(new[] { "Standard", "Groß" });
        await Assertions.Expect(sheet.GetByTestId("item-variants").Locator(".gm-price")).ToHaveTextAsync(new[] { "€ 12,50", "€ 15,50" });
        // Sharing needs JS: the button stays invisible
        await Assertions.Expect(sheet.GetByTestId("share-button")).ToBeHiddenAsync();

        await page.GotoAsync($"/menu/{MezzeMenuId}?item={E2ESeedData.FattoushId}");
        await Assertions.Expect(ItemSheetOf(page).GetByTestId("item-extras").Locator(".gm-extra-name")).ToHaveTextAsync(new[] { "Halloumi", "Hähnchen" });
    }

    // --- Share ---

    /// <summary>
    /// Replaces the Web Share and clipboard APIs before the page loads. <paramref name="share"/>: "resolve", "abort"
    /// or null (no Web Share); <paramref name="copy"/>: true = clipboard works, false = it refuses.
    /// Calls are recorded in <c>window.__share</c> (<c>shared</c>, <c>copied</c>).
    /// </summary>
    private static Task StubShareAsync(IBrowserContext context, string? share, bool copy = true) =>
        context.AddInitScriptAsync($$"""
            (() => {
                const mode = {{JsonSerializer.Serialize(share)}};
                window.__share = { shared: [], copied: [] };
                const share = mode === null ? undefined
                    : data => { window.__share.shared.push(data); return mode === 'abort' ? Promise.reject(new DOMException('cancelled', 'AbortError')) : Promise.resolve(); };
                Object.defineProperty(Navigator.prototype, 'share', { value: share, configurable: true, writable: true });
                Object.defineProperty(Navigator.prototype, 'canShare', { value: share ? () => true : undefined, configurable: true, writable: true });
                const writeText = {{(copy ? "true" : "false")}}
                    ? text => { window.__share.copied.push(text); return Promise.resolve(); }
                    : () => Promise.reject(new DOMException('denied', 'NotAllowedError'));
                Object.defineProperty(Navigator.prototype, 'clipboard', { get: () => ({ writeText }), configurable: true });
            })();
            """);

    private sealed record SharedData(string? Title, string? Text, string? Url);
    private sealed record ShareCalls(List<SharedData> Shared, List<string> Copied);

    private static async Task<ShareCalls> ShareCallsAsync(IPage page) =>
        JsonSerializer.Deserialize<ShareCalls>(await page.EvaluateAsync<string>("() => JSON.stringify(window.__share)"),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

    [Fact]
    public async Task Share_UsesWebShare_WithThePublicLink_FromAQrCode()
    {
        var qrId = Guid.NewGuid();
        await App.SeedAsync(E2ESeedData.OwnerId, db =>
        {
            db.QRCodes.Add(new QRCode { Id = qrId, Title = "Tisch Mezze", MenuId = MezzeMenuId });
            return Task.CompletedTask;
        });
        await StubShareAsync(Context, "resolve");
        var guestApiCalls = 0;
        Page.Request += (_, r) => { if (r.Url.Contains("/guest/api/")) Interlocked.Increment(ref guestApiCalls); };
        await OpenGuestPageAsync(Page, $"/qr/{qrId}");
        await ClickMenuItemAsync("Hausgemachte Limonade");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var callsBefore = guestApiCalls;   // the item click itself is recorded

        var button = ItemSheet.GetByTestId("share-button");
        await Assertions.Expect(button).ToHaveTextAsync("Teilen");
        await button.ClickAsync();
        await Page.WaitForFunctionAsync("() => window.__share.shared.length > 0");

        var calls = await ShareCallsAsync(Page);
        calls.Shared.Should().ContainSingle().Which.Should().Be(new SharedData(
            "Hausgemachte Limonade", $"Hausgemachte Limonade – {E2ESeedData.RestaurantName}", PublicUrl(MezzeMenuId, E2ESeedData.LemonadeId)));
        calls.Copied.Should().BeEmpty();
        await Assertions.Expect(ItemSheet.GetByTestId("share-status")).ToBeEmptyAsync();
        await Assertions.Expect(ItemSheet.GetByTestId("share-fallback")).ToBeHiddenAsync();
        await Page.WaitForTimeoutAsync(300);
        callsBefore.Should().Be(1, "opening the item is recorded");
        guestApiCalls.Should().Be(callsBefore, "sharing is not tracked");
    }

    [Fact]
    public async Task Share_FromTheOwnerPreview_SharesThePublicLink()
    {
        var email = await RegisterNewOwnerAsync();
        Guid ownerId;
        await using (var db = App.CreateDbContext(Guid.Empty))
            ownerId = Guid.Parse((await db.Users.SingleAsync(u => u.Email == email)).Id);
        var demo = await SeedDemoAsync(ownerId: ownerId);
        await StubShareAsync(Context, "resolve");

        await OpenGuestPageAsync(Page, $"/menu/preview/{demo.MenuId}");
        await ClickMenuItemAsync(GuestDemoData.DishWithExtras);
        await ItemSheet.GetByTestId("share-button").ClickAsync();
        await Page.WaitForFunctionAsync("() => window.__share.shared.length > 0");

        var shared = (await ShareCallsAsync(Page)).Shared.Single();
        shared.Url.Should().Be(PublicUrl(demo.MenuId, demo.Items[GuestDemoData.DishWithExtras]), "never the preview URL");
        shared.Text.Should().Be($"{GuestDemoData.DishWithExtras} – {GuestDemoData.RestaurantName}");
    }

    [Fact]
    public async Task Share_WithoutWebShare_CopiesTheLink_AndSaysSo()
    {
        await StubShareAsync(Context, share: null);
        await OpenMenuAsync(MezzeMenuId);
        await ClickMenuItemAsync("Fattoush");

        await ItemSheet.GetByTestId("share-button").ClickAsync();

        var status = ItemSheet.GetByRole(AriaRole.Status);
        await Assertions.Expect(status).ToHaveTextAsync("Link kopiert");
        await Assertions.Expect(status).ToBeVisibleAsync();
        (await ShareCallsAsync(Page)).Copied.Should().Equal(PublicUrl(MezzeMenuId, E2ESeedData.FattoushId));
        await Assertions.Expect(ItemSheet.GetByTestId("share-fallback")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Share_WhenCopyingFails_ShowsTheLinkInAReadOnlyField()
    {
        await StubShareAsync(Context, share: null, copy: false);
        await OpenMenuAsync(MezzeMenuId);
        await ClickMenuItemAsync("Hummus");

        await ItemSheet.GetByTestId("share-button").ClickAsync();

        var field = ItemSheet.GetByLabel("Link zum Kopieren");
        await Assertions.Expect(field).ToBeVisibleAsync();
        await Assertions.Expect(field).ToHaveValueAsync(PublicUrl(MezzeMenuId, E2ESeedData.HummusId));
        await Assertions.Expect(field).ToHaveAttributeAsync("readonly", "");
        await Assertions.Expect(field).ToBeFocusedAsync();
        await Assertions.Expect(ItemSheet.GetByTestId("share-status")).ToBeEmptyAsync();
    }

    [Fact]
    public async Task Share_CancelledByTheGuest_IsNotAnError()
    {
        await StubShareAsync(Context, "abort");
        await OpenMenuAsync(MezzeMenuId);
        await ClickMenuItemAsync("Hausbier");

        await ItemSheet.GetByTestId("share-button").ClickAsync();
        await Page.WaitForFunctionAsync("() => window.__share.shared.length > 0");
        await Page.WaitForTimeoutAsync(300);

        (await ShareCallsAsync(Page)).Copied.Should().BeEmpty("a cancelled share does not fall back to copying");
        await Assertions.Expect(ItemSheet.GetByTestId("share-status")).ToBeEmptyAsync();
        await Assertions.Expect(ItemSheet.GetByTestId("share-fallback")).ToBeHiddenAsync();
        await Assertions.Expect(ItemSheet).ToBeVisibleAsync();
    }

    // --- Labels ---

    [Fact]
    public async Task Labels_HighlightsFirst_VeganHidesVegetarisch_AllInTheSheet()
    {
        await OpenMenuAsync(MezzeMenuId);

        // Hummus is vegan and vegetarian: only "Vegan"; the highlight comes first
        await ClickMenuItemAsync("Hummus");
        await Assertions.Expect(ItemSheet.GetByTestId("item-labels").Locator("li")).ToHaveTextAsync(new[] { "Beliebt", "Vegan", "Halal" });
        await Assertions.Expect(ItemSheet.GetByTestId("label-vegetarian")).ToHaveCountAsync(0);
        await CloseItemSheetAsync(Page);

        await ClickMenuItemAsync("Falafel-Teller");
        var labels = ItemSheet.GetByTestId("item-labels");
        await Assertions.Expect(labels.Locator("li")).ToHaveTextAsync(new[] { "Neu", "Vegan", "Halal", "Scharf" });
        await Assertions.Expect(labels.GetByTestId("label-more")).ToHaveCountAsync(0);
        await CloseItemSheetAsync(Page);

        await ClickMenuItemAsync("Hausgemachte Limonade");
        await Assertions.Expect(ItemSheet.GetByTestId("item-labels").Locator("li")).ToHaveTextAsync(new[] { "Neu", "Vegan", "Alkoholfrei" });

        // Icons: neutral pictograms for beliebt, neu, alkoholfrei; halal is text only
        var sheetLabels = ItemSheet.GetByTestId("item-labels");
        await Assertions.Expect(sheetLabels.GetByTestId("label-new").Locator("svg")).ToHaveCountAsync(1);
        await Assertions.Expect(sheetLabels.GetByTestId("label-alcohol-free").Locator("svg")).ToHaveCountAsync(1);
        await CloseItemSheetAsync(Page);
        await Assertions.Expect(ItemEntry("Hummus").GetByTestId("label-popular").Locator("svg")).ToHaveCountAsync(1);
        await Assertions.Expect(ItemEntry("Hummus").GetByTestId("label-halal").Locator("svg")).ToHaveCountAsync(0);

        // Highlights look different from dietary labels (accent color from the theme)
        var popular = ItemEntry("Hummus").GetByTestId("label-popular");
        var vegan = ItemEntry("Hummus").GetByTestId("label-vegan");
        await Assertions.Expect(popular).ToHaveClassAsync(new Regex(@"\bgm-label--highlight\b"));
        (await ComputedAsync(popular, "backgroundColor")).Should().NotBe(await ComputedAsync(vegan, "backgroundColor"));
        (await ComputedAsync(popular, "color")).Should().NotBe(await ComputedAsync(vegan, "color"));
    }

    [Fact]
    public async Task Labels_OnCards_StayCompact_AndNeverOverflowThePhoneGrid()
    {
        var demo = await SeedDemoAsync();
        await Page.SetViewportSizeAsync(360, 780);
        await OpenMenuAsync(demo.MenuId);

        // Four labels: two chips and "+2"; the other two stay readable for screen readers
        var card = ItemEntry(GuestDemoData.DishWithManyLabels);
        await Assertions.Expect(card).ToHaveAttributeAsync("data-labels", "popular vegan halal spicy");
        await Assertions.Expect(card.GetByTestId("label-popular")).ToBeVisibleAsync();
        await Assertions.Expect(card.GetByTestId("label-vegan")).ToBeVisibleAsync();
        await Assertions.Expect(card.GetByTestId("label-more")).ToHaveTextAsync("+2");
        await Assertions.Expect(card.GetByTestId("label-more")).ToHaveAttributeAsync("aria-hidden", "true");
        await Assertions.Expect(card.GetByTestId("label-halal")).ToHaveClassAsync(new Regex(@"\bgm-visually-hidden\b"));
        await Assertions.Expect(card.GetByTestId("label-spicy")).ToHaveClassAsync(new Regex(@"\bgm-visually-hidden\b"));
        // Three labels are all shown
        await Assertions.Expect(ItemEntry(GuestDemoData.LemonadeName).GetByTestId("label-more")).ToHaveCountAsync(0);

        // In the 2-column grid nothing sticks out of its card
        foreach (var name in new[] { GuestDemoData.DishWithManyLabels, "Spaghetti Carbonara", "Carpaccio vom Rind", GuestDemoData.DishWithExtras })
        {
            var entry = ItemEntry(name);
            var box = (await entry.BoundingBoxAsync())!;
            var chips = (await entry.GetByTestId("item-labels").BoundingBoxAsync())!;
            (chips.X + chips.Width).Should().BeLessThanOrEqualTo(box.X + box.Width + 0.5f, $"the chips of {name} fit into the card");
            (await entry.EvaluateAsync<bool>("e => e.scrollWidth <= e.clientWidth + 1")).Should().BeTrue();
        }
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth")).Should().BeTrue();

        // All four in the sheet
        await ClickMenuItemAsync(GuestDemoData.DishWithManyLabels);
        await Assertions.Expect(ItemSheet.GetByTestId("item-labels").Locator("li")).ToHaveTextAsync(new[] { "Beliebt", "Vegan", "Halal", "Scharf" });
    }

    // --- Head: title, description, Open Graph ---

    [Fact]
    public async Task Meta_DescribesTheMenu_WithThePublicCanonicalUrl()
    {
        await OpenMenuAsync(MezzeMenuId);

        await Assertions.Expect(Page).ToHaveTitleAsync($"{E2ESeedData.MezzeMenuName} – {E2ESeedData.RestaurantName}");
        (await MetaAsync("meta[name='description']")).Should().Be("Mezze, Salate und Getränke");
        (await MetaAsync("meta[property='og:title']")).Should().Be($"{E2ESeedData.MezzeMenuName} – {E2ESeedData.RestaurantName}");
        (await MetaAsync("meta[property='og:description']")).Should().Be("Mezze, Salate und Getränke");
        (await MetaAsync("meta[property='og:site_name']")).Should().Be(E2ESeedData.RestaurantName);
        (await MetaAsync("meta[property='og:type']")).Should().Be("website");
        (await MetaAsync("meta[property='og:url']")).Should().Be(PublicUrl(MezzeMenuId));
        (await Page.Locator("link[rel='canonical']").GetAttributeAsync("href")).Should().Be(PublicUrl(MezzeMenuId));
        await Assertions.Expect(Page.Locator("meta[property='og:image']")).ToHaveCountAsync(0); // no cover, no dish photo
        await Assertions.Expect(Page.Locator("meta[name='robots']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Meta_OgImageIsTheCover_AndAnItemPageDescribesTheItem()
    {
        var owner = Guid.NewGuid();
        var demo = await SeedDemoAsync(name: "Abendkarte", ownerId: owner);
        var cover = await ImagePathAsync(db => db.RestaurantProfiles.IgnoreQueryFilters()
            .Where(p => p.ApplicationUserId == owner).Select(p => p.CoverImagePath).SingleAsync());
        await OpenMenuAsync(demo.MenuId);

        await Assertions.Expect(Page).ToHaveTitleAsync($"Abendkarte – {GuestDemoData.RestaurantName}");
        var image = await MetaAsync("meta[property='og:image']");
        image.Should().Be($"{App.ServerAddress}/{cover}");
        var response = await Page.APIRequest.GetAsync(image!);
        response.Status.Should().Be(200);
        response.Headers["content-type"].Should().StartWith("image/");
        (await MetaAsync("meta[name='twitter:card']")).Should().Be("summary_large_image");

        // ?item=: title, description, URL and picture of the item
        var margherita = demo.Items[GuestDemoData.DishWithExtras];
        var photo = await ImagePathAsync(db => db.MenuItems.IgnoreQueryFilters().Where(i => i.Id == margherita).Select(i => i.ImagePath).SingleAsync());
        await OpenGuestPageAsync(Page, $"/menu/{demo.MenuId}?item={margherita}");
        await Assertions.Expect(Page).ToHaveTitleAsync($"{GuestDemoData.DishWithExtras} – {GuestDemoData.RestaurantName}");
        (await MetaAsync("meta[property='og:title']")).Should().Be($"{GuestDemoData.DishWithExtras} – {GuestDemoData.RestaurantName}");
        (await MetaAsync("meta[name='description']")).Should().Be("Tomate, Fior di Latte, Basilikum · € 10,50");
        (await MetaAsync("meta[property='og:url']")).Should().Be(PublicUrl(demo.MenuId, margherita));
        (await MetaAsync("meta[property='og:image']")).Should().Be($"{App.ServerAddress}/{photo}");
    }

    [Fact]
    public async Task Meta_WithoutProfile_UsesTheMenu_AndTheFirstDishPhoto()
    {
        var demo = await SeedDemoAsync(withProfile: false);
        var firstPhoto = await ImagePathAsync(db => db.MenuItems.IgnoreQueryFilters()
            .Where(i => i.Id == demo.Items["Bruschetta Classica"]).Select(i => i.ImagePath).SingleAsync());
        await OpenMenuAsync(demo.MenuId);

        await Assertions.Expect(Page).ToHaveTitleAsync($"{GuestDemoData.MenuName} – Speisekarte");
        (await MetaAsync("meta[property='og:site_name']")).Should().Be(GuestDemoData.MenuName);
        (await MetaAsync("meta[property='og:image']")).Should().Be($"{App.ServerAddress}/{firstPhoto}");
    }

    [Fact]
    public async Task Meta_QrPageAndPreview_PointToThePublicPage_PreviewIsNoindex()
    {
        var qrId = Guid.NewGuid();
        await App.SeedAsync(E2ESeedData.OwnerId, db =>
        {
            db.QRCodes.Add(new QRCode { Id = qrId, Title = "Tisch Meta", MenuId = MezzeMenuId });
            return Task.CompletedTask;
        });
        await OpenGuestPageAsync(Page, $"/qr/{qrId}");
        (await MetaAsync("meta[property='og:url']")).Should().Be(PublicUrl(MezzeMenuId));
        (await Page.Locator("link[rel='canonical']").GetAttributeAsync("href")).Should().Be(PublicUrl(MezzeMenuId));
        await Assertions.Expect(Page.Locator("meta[name='robots']")).ToHaveCountAsync(0);

        var email = await RegisterNewOwnerAsync();
        Guid ownerId;
        await using (var db = App.CreateDbContext(Guid.Empty))
            ownerId = Guid.Parse((await db.Users.SingleAsync(u => u.Email == email)).Id);
        var demo = await SeedDemoAsync(ownerId: ownerId);
        await OpenGuestPageAsync(Page, $"/menu/preview/{demo.MenuId}");

        (await MetaAsync("meta[name='robots']")).Should().Be("noindex");
        (await MetaAsync("meta[property='og:url']")).Should().Be(PublicUrl(demo.MenuId));
        // The preview shows the restaurant too
        await Assertions.Expect(Page.GetByTestId("hero-logo")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("info-button")).ToBeVisibleAsync();
    }

    // --- Assets ---

    [Fact]
    public async Task RestaurantModule_IsServedWithRevalidation()
    {
        await OpenMenuAsync(MezzeMenuId);
        var modules = await Page.EvaluateAsync<string[]>(
            "() => performance.getEntriesByType('resource').map(e => new URL(e.name).pathname).filter(p => p.startsWith('/js/'))");
        modules.Should().Contain("/js/guest-restaurant.js", "guest-menu.js imports it");

        // Not fingerprinted, so phones must revalidate it: no-cache plus an ETag (MapStaticAssets)
        var response = await Page.APIRequest.GetAsync("/js/guest-restaurant.js");
        response.Status.Should().Be(200);
        response.Headers["cache-control"].Should().Contain("no-cache");
        response.Headers.Should().ContainKey("etag");
    }

    private static Task<string> ComputedAsync(ILocator locator, string property) =>
        locator.EvaluateAsync<string>($"e => getComputedStyle(e).{property}");
}
