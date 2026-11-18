using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Not a test in the usual sense: screenshots of the guest menu for design reviews, with the demo menu of
/// <see cref="GuestDemoData"/> (photos, text-only drinks, restaurant profile with cover and logo) in every design
/// theme: phone (390 px) and desktop (1280 px), open item sheet, category and search sheet, the drinks list, the
/// page without JavaScript, a notice page and the menu editor's phone frame; for modern, classic and dark also the
/// info sheet, the footer, an item with price variants, the card grid with label chips and the top bar with the
/// logo; plus the share fallbacks, a menu without a restaurant profile, one whose name differs from the
/// restaurant's and the seeded mezze menu (profile without pictures).
/// Runs only when <c>E2E_SCREENSHOT_DIR</c> is set; guest pages need no
/// external requests, <c>E2E_ALLOW_EXTERNAL_REQUESTS=1</c> only matters for the editor shots (Tailwind CDN).
/// <code>
/// E2E_SCREENSHOT_DIR=/tmp/shots E2E_ALLOW_EXTERNAL_REQUESTS=1 dotnet test SpeiseDirekt3.E2ETests --filter FullyQualifiedName~GuestScreenshotTour
/// </code>
/// </summary>
public class GuestScreenshotTour : BackOfficeTestBase
{
    public GuestScreenshotTour(E2EFixture fixture) : base(fixture) { }

    private static string? Directory => Environment.GetEnvironmentVariable(AdminScreenshotTour.DirectoryEnvVar) is { Length: > 0 } d ? d : null;

    [Fact]
    public async Task Capture_guest_views()
    {
        var dir = Directory;
        if (dir is null) return; // opt-in only
        System.IO.Directory.CreateDirectory(dir);

        var email = await RegisterNewOwnerAsync();
        Guid ownerId;
        await using (var db = App.CreateDbContext(Guid.Empty))
            ownerId = Guid.Parse((await db.Users.SingleAsync(u => u.Email == email)).Id);

        var menus = new Dictionary<DesignTheme, GuestDemoData.Seeded>();
        foreach (var theme in Enum.GetValues<DesignTheme>())
            menus[theme] = await GuestDemoData.SeedAsync(App, ownerId, theme);

        var state = await Context.StorageStateAsync();
        await using var phoneContext = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, StorageState = state,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }, DeviceScaleFactor = 2, IsMobile = true, HasTouch = true
        });
        phoneContext.SetDefaultTimeout(15_000);
        var phone = await phoneContext.NewPageAsync();

        async Task Shot(IPage page, string name, bool fullPage = false)
        {
            await page.EvaluateAsync("() => document.fonts.ready");
            await page.WaitForTimeoutAsync(450); // sheet animations
            await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{name}.png"), FullPage = fullPage });
        }

        foreach (var (theme, seeded) in menus)
        {
            var key = theme.ToString().ToLowerInvariant();
            await OpenGuestPageAsync(phone, $"/menu/{seeded.MenuId}");
            await Shot(phone, $"phone-{key}-top");
            await Shot(phone, $"phone-{key}-full", fullPage: true);
            await OpenGuestPageAsync(Page, $"/menu/{seeded.MenuId}");
            await Shot(Page, $"desktop-{key}-top");

            if (theme is DesignTheme.Modern or DesignTheme.Classic or DesignTheme.Dark)
            {
                await Shot(Page, $"desktop-{key}-full", fullPage: true);
                await ClickMenuItemAsync(phone, "Margherita");
                await Shot(phone, $"phone-{key}-item");
                await CloseItemSheetAsync(phone);
                await ClickMenuItemAsync(phone, GuestDemoData.DishWithoutPhoto);
                await Shot(phone, $"phone-{key}-item-text");
                await CloseItemSheetAsync(phone);
                await ClickMenuItemAsync(Page, "Margherita");
                await Shot(Page, $"desktop-{key}-item");
                await CloseItemSheetAsync(Page);

                // Text-only category (list) after a jump via its chip
                await phone.GetByTestId("category-nav").GetByRole(AriaRole.Link, new() { Name = "Weine" }).ClickAsync();
                await phone.WaitForTimeoutAsync(900);
                await Shot(phone, $"phone-{key}-drinks");

                // --- AP3: restaurant profile, labels, variants ---
                await ClickMenuItemAsync(phone, GuestDemoData.WineWithVariantsName);
                await Shot(phone, $"phone-{key}-item-variants");
                await CloseItemSheetAsync(phone);
                await phone.GetByTestId("category-nav").GetByRole(AriaRole.Link, new() { Name = "Pasta & Risotto" }).ClickAsync();
                await phone.WaitForTimeoutAsync(900);
                await Shot(phone, $"phone-{key}-labels");
                await phone.GetByTestId("info-button").ClickAsync();
                await Assertions.Expect(phone.GetByTestId("info-sheet")).ToBeVisibleAsync();
                await Shot(phone, $"phone-{key}-info");
                await phone.Keyboard.PressAsync("Escape");
                await Assertions.Expect(phone.GetByTestId("info-sheet")).ToBeHiddenAsync();
                await phone.EvaluateAsync("() => window.scrollTo(0, document.documentElement.scrollHeight)");
                await phone.WaitForTimeoutAsync(400);
                await Shot(phone, $"phone-{key}-footer");

                await Page.GetByTestId("info-button").ClickAsync();
                await Assertions.Expect(Page.GetByTestId("info-sheet")).ToBeVisibleAsync();
                await Shot(Page, $"desktop-{key}-info");
                await Page.Keyboard.PressAsync("Escape");
                await ClickMenuItemAsync(Page, GuestDemoData.WineWithVariantsName);
                await Shot(Page, $"desktop-{key}-item-variants");
                await CloseItemSheetAsync(Page);
                await Page.EvaluateAsync("() => window.scrollTo(0, document.documentElement.scrollHeight)");
                await Page.WaitForTimeoutAsync(400);
                await Shot(Page, $"desktop-{key}-footer");
            }
        }

        // Sheets and states (modern)
        var modern = menus[DesignTheme.Modern];
        await OpenGuestPageAsync(phone, $"/menu/{modern.MenuId}");
        await phone.Mouse.WheelAsync(0, 900);
        await phone.WaitForTimeoutAsync(600);
        await Shot(phone, "phone-modern-scrolled");
        await phone.GetByTestId("category-all").ClickAsync();
        await Shot(phone, "phone-modern-categories");
        await phone.Keyboard.PressAsync("Escape");
        await phone.GetByTestId("search-button").ClickAsync();
        await phone.GetByTestId("search-sheet").Locator("input").FillAsync("pizza");
        await Shot(phone, "phone-modern-search");
        await phone.GetByTestId("search-sheet").GetByRole(AriaRole.Button, new() { Name = "Schließen" }).ClickAsync();
        await Assertions.Expect(phone.GetByTestId("search-sheet")).ToBeHiddenAsync();
        await ClickMenuItemAsync(phone, "Margherita");
        await phone.GetByTestId("item-sheet").Locator("[data-sheet-body]").EvaluateAsync("e => e.scrollTop = e.scrollHeight");
        await Shot(phone, "phone-modern-item-combo");

        var dark = menus[DesignTheme.Dark];
        await OpenGuestPageAsync(phone, $"/menu/{dark.MenuId}");
        await phone.GetByTestId("category-all").ClickAsync();
        await Shot(phone, "phone-dark-categories");
        await phone.Keyboard.PressAsync("Escape");
        await phone.GetByTestId("search-button").ClickAsync();
        await phone.GetByTestId("search-sheet").Locator("input").FillAsync("wein");
        await Shot(phone, "phone-dark-search");

        // Top bar with logo once the hero scrolled away; share: copied, and the field when nothing else works
        await OpenGuestPageAsync(phone, $"/menu/{modern.MenuId}");
        await phone.Mouse.WheelAsync(0, 500);
        await phone.WaitForTimeoutAsync(600);
        await Shot(phone, "phone-modern-topbar-logo");
        await phone.SetViewportSizeAsync(360, 780);
        await OpenGuestPageAsync(phone, $"/menu/{modern.MenuId}");
        await Shot(phone, "phone360-modern-top");
        await phone.Mouse.WheelAsync(0, 900);
        await phone.WaitForTimeoutAsync(600);
        await Shot(phone, "phone360-modern-topbar-logo");
        await phone.SetViewportSizeAsync(390, 844);
        await OpenGuestPageAsync(phone, $"/menu/{modern.MenuId}");
        await phone.EvaluateAsync("() => { Object.defineProperty(navigator, 'share', { value: undefined, configurable: true }); Object.defineProperty(navigator, 'clipboard', { value: { writeText: () => Promise.resolve() }, configurable: true }); }");
        await ClickMenuItemAsync(phone, GuestDemoData.LemonadeName);
        await phone.GetByTestId("item-sheet").GetByTestId("share-button").ClickAsync();
        await Shot(phone, "phone-modern-share-copied");
        await CloseItemSheetAsync(phone);
        await phone.EvaluateAsync("() => Object.defineProperty(navigator, 'clipboard', { value: { writeText: () => Promise.reject(new Error('denied')) }, configurable: true })");
        await ClickMenuItemAsync(phone, GuestDemoData.LemonadeName);
        await phone.GetByTestId("item-sheet").GetByTestId("share-button").ClickAsync();
        await Assertions.Expect(phone.GetByTestId("item-sheet").GetByTestId("share-url")).ToBeVisibleAsync();
        await Shot(phone, "phone-modern-share-fallback");

        // Menu named differently from the restaurant (name above the h1), the mezze menu (profile without pictures),
        // and a menu whose tenant has no profile at all
        var lunch = await GuestDemoData.SeedAsync(App, ownerId, DesignTheme.Modern, name: "Mittagskarte");
        await OpenGuestPageAsync(phone, $"/menu/{lunch.MenuId}");
        await Shot(phone, "phone-modern-other-name-top");
        await OpenGuestPageAsync(Page, $"/menu/{lunch.MenuId}");
        await Shot(Page, "desktop-modern-other-name-top");
        await OpenGuestPageAsync(phone, $"/menu/{E2ESeedData.MezzeMenuId}");
        await Shot(phone, "phone-mezze-top");
        await Shot(phone, "phone-mezze-full", fullPage: true);
        var noProfile = await GuestDemoData.SeedAsync(App, Guid.NewGuid(), DesignTheme.Classic, withProfile: false);
        await OpenGuestPageAsync(phone, $"/menu/{noProfile.MenuId}");
        await Shot(phone, "phone-noprofile-top");
        await OpenGuestPageAsync(Page, $"/menu/{noProfile.MenuId}");
        await Shot(Page, "desktop-noprofile-top");

        // Without JavaScript
        await using (var noJs = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, JavaScriptEnabled = false,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }, DeviceScaleFactor = 2, IsMobile = true, HasTouch = true
        }))
        {
            var page = await noJs.NewPageAsync();
            await page.GotoAsync($"/menu/{modern.MenuId}");
            await Shot(page, "phone-nojs-top");
            await page.GotoAsync($"/menu/{modern.MenuId}?item={modern.Items["Tiramisu"]}");
            await Shot(page, "phone-nojs-item");
            await page.GotoAsync($"/menu/{modern.MenuId}?item={modern.Items[GuestDemoData.WineWithVariantsName]}");
            await Shot(page, "phone-nojs-item-variants");
            await page.GotoAsync($"/menu/{modern.MenuId}");
            await page.GetByTestId("info-button").ClickAsync();
            await Shot(page, "phone-nojs-info");
        }

        // Notice page
        await phone.GotoAsync($"/menu/{Guid.NewGuid()}");
        await Shot(phone, "phone-notice-notfound");

        await CaptureSearchFilterFavoritesAsync(menus, phone, Shot);

        // Menu editor: phone frame with the guest page, theme follows the select
        await GotoInteractiveAsync($"/menu/edit/{modern.MenuId}");
        var frame = Page.FrameLocator("[data-testid='menu-preview-iframe']");
        await frame.Locator("html[data-guest-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await Shot(Page, "editor-modern");
        await Page.Locator("#themeSelect").SelectOptionAsync("Dark");
        await Assertions.Expect(frame.GetByTestId("menu-display")).ToHaveAttributeAsync("data-theme", "dark");
        await frame.Locator("html[data-guest-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await Shot(Page, "editor-dark");
        await GotoInteractiveAsync(phone, $"/menu/edit/{modern.MenuId}");
        await phone.Mouse.WheelAsync(0, 1400);
        await Shot(phone, "phone-editor", fullPage: true);
    }

    /// <summary>
    /// AP5 (its own run: <c>--filter FullyQualifiedName~Capture_language_versions</c>): the demo menu with its English
    /// version in modern, classic and dark — language sheet, the English menu (top, item sheet, filter sheet, favourites
    /// sheet), the language list at the end of the page, the top bar at 360 px with info, language, filter and search
    /// buttons — plus the back office: the "Language versions" card and the editor's phone frame with unsaved changes
    /// (modern, dark). The back office needs <c>E2E_ALLOW_EXTERNAL_REQUESTS=1</c> for Tailwind.
    /// </summary>
    [Fact]
    public async Task Capture_language_versions()
    {
        var dir = Directory;
        if (dir is null) return; // opt-in only
        System.IO.Directory.CreateDirectory(dir);

        var email = await RegisterNewOwnerAsync();
        var ownerId = await OwnerIdAsync(email);

        var state = await Context.StorageStateAsync();
        await using var phoneContext = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, StorageState = state,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }, DeviceScaleFactor = 2, IsMobile = true, HasTouch = true
        });
        phoneContext.SetDefaultTimeout(15_000);
        var phone = await phoneContext.NewPageAsync();

        async Task Shot(IPage page, string name, bool fullPage = false)
        {
            await page.EvaluateAsync("() => document.fonts.ready");
            await page.WaitForTimeoutAsync(450);
            await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{name}.png"), FullPage = fullPage });
        }

        GuestDemoData.Seeded? modernGerman = null;
        foreach (var theme in new[] { DesignTheme.Modern, DesignTheme.Classic, DesignTheme.Dark })
        {
            var key = theme.ToString().ToLowerInvariant();
            var german = await GuestDemoData.SeedAsync(App, ownerId, theme);
            modernGerman ??= german;
            var english = await GuestDemoData.SeedEnglishVersionAsync(App, ownerId, german, theme);
            await GuestDemoData.SeedSmallMenuAsync(App, ownerId, MenuLanguage.French, $"Trattoria Lago FR {key}", "Tiramisu maison", 7.50m, german.MenuId, theme);

            foreach (var (page, device) in new[] { (phone, "phone"), (Page, "desktop") })
            {
                var name = $"lang-{device}-{key}";
                await OpenGuestPageAsync(page, $"/menu/{german.MenuId}");
                await Shot(page, $"{name}-de-top");
                await page.GetByTestId("language-button").ClickAsync();
                await Assertions.Expect(page.GetByTestId("language-sheet")).ToBeVisibleAsync();
                await Shot(page, $"{name}-sheet");
                await page.GetByTestId("language-sheet").GetByRole(AriaRole.Link, new() { Name = "English" }).ClickAsync();
                await WaitForGuestReadyAsync(page);
                await Shot(page, $"{name}-en-top");
                await ClickMenuItemAsync(page, GuestDemoData.EnglishLemonadeName);
                await Shot(page, $"{name}-en-item");
                await CloseItemSheetAsync(page);
                await ClickMenuItemAsync(page, "Burrata");
                await page.GetByTestId("item-sheet").Locator("[data-sheet-body]").EvaluateAsync("e => e.scrollTop = e.scrollHeight");
                await Shot(page, $"{name}-en-item-allergens");
                await CloseItemSheetAsync(page);
                await page.GetByTestId("filter-button").ClickAsync();
                await page.GetByTestId("filter-sheet").GetByTestId("filter-label-vegan").ClickAsync();
                await Shot(page, $"{name}-en-filter");
                await page.Keyboard.PressAsync("Escape");
                await Assertions.Expect(page.GetByTestId("filter-sheet")).ToBeHiddenAsync();
                foreach (var item in new[] { "Burrata", "Espresso", GuestDemoData.EnglishLemonadeName })
                    await ItemEntry(page, item).GetByTestId("favorite-button").ClickAsync();
                await page.GetByTestId("favorites-button").ClickAsync();
                var favorites = page.GetByTestId("favorites-sheet");
                await favorites.GetByTestId("favorite-row").Filter(new() { HasText = "Espresso" })
                    .GetByRole(AriaRole.Button, new() { Name = "One portion more" }).ClickAsync();
                await Shot(page, $"{name}-en-favorites");
                await page.Keyboard.PressAsync("Escape");
                await Assertions.Expect(favorites).ToBeHiddenAsync();
                await page.EvaluateAsync("() => window.scrollTo(0, document.documentElement.scrollHeight)");
                await Shot(page, $"{name}-en-footer");
            }

            // Top bar at 360 px with info, language, filter and search (title scrolled in)
            await phone.SetViewportSizeAsync(360, 780);
            await OpenGuestPageAsync(phone, $"/menu/{german.MenuId}");
            await Shot(phone, $"lang-phone360-{key}-top");
            await phone.Mouse.WheelAsync(0, 900);
            await phone.WaitForTimeoutAsync(600);
            await Shot(phone, $"lang-phone360-{key}-topbar");
            await phone.SetViewportSizeAsync(390, 844);
        }

        // Without JS: the button jumps to the list at the end of the page
        await using (var noJs = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, JavaScriptEnabled = false,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 }, DeviceScaleFactor = 2, IsMobile = true, HasTouch = true
        }))
        {
            var page = await noJs.NewPageAsync();
            await page.GotoAsync($"/menu/{modernGerman!.MenuId}");
            await page.GetByTestId("language-button").ClickAsync();
            await Shot(page, "lang-phone-nojs-list");
        }

        // Back office: the "Language versions" card and the phone frame with unsaved changes (modern, dark)
        await GotoInteractiveAsync($"/menu/edit/{modernGerman!.MenuId}");
        var frame = Page.FrameLocator("[data-testid='menu-preview-iframe']");
        await frame.Locator("html[data-guest-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await Shot(Page, "lang-editor-versions", fullPage: true);
        await Page.GetByTestId("language-versions").GetByRole(AriaRole.Button, new() { Name = "Add translation" }).ClickAsync();
        await Shot(Page, "lang-editor-add-translation", fullPage: true);
        await Page.GetByTestId("language-versions").GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Page.Locator("#menuName").FillAsync("Trattoria Lago – Herbstkarte");
        await Page.Locator("#menuDesc").FillAsync("Kürbis, Steinpilze und Wild aus dem Salzkammergut");
        await Assertions.Expect(frame.Locator("h1")).ToHaveTextAsync("Trattoria Lago – Herbstkarte");
        await Shot(Page, "lang-editor-draft-modern");
        await Page.Locator("#themeSelect").SelectOptionAsync("Dark");
        await Assertions.Expect(frame.GetByTestId("menu-display")).ToHaveAttributeAsync("data-theme", "dark");
        await Shot(Page, "lang-editor-draft-dark");
        await Page.Locator("#languageSelect").SelectOptionAsync("Italian");
        await Assertions.Expect(frame.Locator("html")).ToHaveAttributeAsync("lang", "it");
        await Assertions.Expect(frame.Locator("h1")).ToHaveTextAsync("Trattoria Lago – Herbstkarte");
        await Shot(Page, "lang-editor-draft-dark-italian");
        await GotoInteractiveAsync(phone, $"/menu/edit/{modernGerman.MenuId}");
        await Shot(phone, "lang-phone-editor", fullPage: true);
    }

    /// <summary>
    /// AP4: search (AP8: also the empty field and no results) with highlighted results, the filter sheet, the filtered menu with its notice, hearts on cards and
    /// rows, the floating button and the favourites list with a variant select and the sum — modern, classic and dark,
    /// phone and desktop. Leaves favourites in the desktop context of the modern menu, so the editor shots show the
    /// hearts and the floating button inside the phone frame.
    /// </summary>
    private async Task CaptureSearchFilterFavoritesAsync(
        IReadOnlyDictionary<DesignTheme, GuestDemoData.Seeded> menus, IPage phone, Func<IPage, string, bool, Task> shot)
    {
        foreach (var theme in new[] { DesignTheme.Dark, DesignTheme.Classic, DesignTheme.Modern })
        {
            var key = theme.ToString().ToLowerInvariant();
            var seeded = menus[theme];
            await EnsureVariantsAsync(seeded);
            foreach (var (page, device) in new[] { (phone, "phone"), (Page, "desktop") })
            {
                var name = $"{device}-{key}";
                await OpenGuestPageAsync(page, $"/menu/{seeded.MenuId}");

                await page.GetByTestId("search-button").ClickAsync();
                await shot(page, $"{name}-search-empty", false);         // AP8: full-screen on the phone, the field on top
                await page.GetByTestId("search-sheet").Locator("input").FillAsync("tomate");
                await shot(page, $"{name}-search-highlight", false);
                await page.GetByTestId("search-sheet").Locator("input").FillAsync("xyz");
                await shot(page, $"{name}-search-none", false);
                await page.GetByTestId("search-sheet").GetByRole(AriaRole.Button, new() { Name = "Schließen" }).ClickAsync();
                await Assertions.Expect(page.GetByTestId("search-sheet")).ToBeHiddenAsync();

                await page.GetByTestId("filter-button").ClickAsync();
                var filter = page.GetByTestId("filter-sheet");
                await filter.GetByTestId("filter-label-vegetarian").ClickAsync();
                await filter.Locator("input[data-filter-allergen='A']").CheckAsync();
                await shot(page, $"{name}-filter-sheet", false);
                await filter.GetByTestId("filter-apply").ClickAsync();
                await Assertions.Expect(filter).ToBeHiddenAsync();
                await shot(page, $"{name}-filtered", false);
                await page.GetByTestId("filter-notice").GetByRole(AriaRole.Button, new() { Name = "Zurücksetzen" }).ClickAsync();

                foreach (var item in new[] { "Burrata", "Margherita", "Espresso", "Tiramisu" })
                {
                    var entry = page.Locator("[data-testid='item-card'], [data-testid='list-row']",
                        new() { Has = page.Locator("h3", new() { HasTextRegex = ExactText(item) }) });
                    await entry.GetByTestId("favorite-button").ClickAsync();
                }
                await page.EvaluateAsync("() => window.scrollTo(0, 0)");
                await shot(page, $"{name}-hearts", false);
                await page.EvaluateAsync("() => window.scrollTo(0, document.documentElement.scrollHeight)");
                await shot(page, $"{name}-fab-bottom", false);

                await page.GetByTestId("favorites-button").ClickAsync();
                var favorites = page.GetByTestId("favorites-sheet");
                var espresso = favorites.GetByTestId("favorite-row").Filter(new() { HasText = "Espresso" });
                var variant = espresso.GetByTestId("favorite-variant");
                if (await variant.CountAsync() > 0)
                    await variant.SelectOptionAsync(new SelectOptionValue { Index = 1 });
                await favorites.GetByTestId("favorite-row").Filter(new() { HasText = "Margherita" })
                    .GetByRole(AriaRole.Button, new() { Name = "Eine Portion mehr" }).ClickAsync();
                await shot(page, $"{name}-favorites", false);
                await favorites.GetByTestId("favorites-clear").ClickAsync();
                await shot(page, $"{name}-favorites-confirm", false);
                await favorites.GetByTestId("favorites-confirm").GetByRole(AriaRole.Button, new() { Name = "Abbrechen" }).ClickAsync();
                await page.Keyboard.PressAsync("Escape");
                await Assertions.Expect(favorites).ToBeHiddenAsync();

                if (device == "phone")
                {
                    await ClickMenuItemAsync(page, "Margherita");
                    await shot(page, $"{name}-item-heart", false);
                    await CloseItemSheetAsync(page);
                    await ClickMenuItemAsync(page, GuestDemoData.DishWithoutPhoto);
                    await page.GetByTestId("item-sheet").GetByTestId("item-favorite-button").ClickAsync();
                    await shot(page, $"{name}-item-heart-text", false);
                    await CloseItemSheetAsync(page);
                }
            }
        }
    }

    /// <summary>Gives the demo menu's espresso two variants when the menu has none yet (for the variant select).</summary>
    private async Task EnsureVariantsAsync(GuestDemoData.Seeded seeded)
    {
        Guid ownerId;
        await using (var db = App.CreateDbContext(Guid.Empty))
        {
            if (await db.MenuItemPriceOptions.IgnoreQueryFilters()
                    .AnyAsync(o => o.Kind == PriceOptionKind.Variant && o.MenuItem!.Category!.MenuId == seeded.MenuId))
                return;
            ownerId = (await db.Menus.IgnoreQueryFilters().SingleAsync(m => m.Id == seeded.MenuId)).ApplicationUserId;
        }
        await App.SeedAsync(ownerId, db =>
        {
            db.MenuItemPriceOptions.AddRange(
                new MenuItemPriceOption { Id = Guid.NewGuid(), MenuItemId = seeded.Items["Espresso"], Kind = PriceOptionKind.Variant, Name = "Einfach", Price = 2.90m, SortOrder = 0 },
                new MenuItemPriceOption { Id = Guid.NewGuid(), MenuItemId = seeded.Items["Espresso"], Kind = PriceOptionKind.Variant, Name = "Doppelt", Price = 4.20m, SortOrder = 1 });
            return Task.CompletedTask;
        });
    }
}
