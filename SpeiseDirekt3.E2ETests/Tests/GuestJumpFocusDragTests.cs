using FluentAssertions;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// AP6, from the operator's iPhone test: category jumps (chip bar and categories sheet) land right under the sticky bars
/// for the first, middle and last category of a long menu, also right after the sheet has closed; focus rings only for
/// keyboard use (no ring on the jumped-to heading, none on the active chip or a sheet's first control after a tap; keyboard
/// users keep them); bottom sheets can be dragged down by the handle and the header band (<c>js/guest-menu.js</c>).
/// </summary>
public class GuestJumpFocusDragTests : E2ETestBase
{
    public GuestJumpFocusDragTests(E2EFixture fixture) : base(fixture) { }

    private static readonly string[] LongMenuCategories =
        Enumerable.Range(1, 12).Select(i => $"Rubrik {i:00}").ToArray();

    /// <summary>A long text-only menu: twelve categories with five dishes each (list rows).</summary>
    private async Task<Guid> SeedLongMenuAsync()
    {
        var menuId = Guid.NewGuid();
        await App.SeedAsync(E2ESeedData.OwnerId, db =>
        {
            var categories = LongMenuCategories
                .Select((name, i) => new Category { Id = Guid.NewGuid(), Name = name, SortOrder = i, MenuId = menuId, Description = i % 3 == 0 ? "Untertitel der Rubrik" : null })
                .ToList();
            db.Menus.Add(new Menu { Id = menuId, Name = "Lange Karte", Description = "", Theme = DesignTheme.Classic, Language = MenuLanguage.German, Categories = categories });
            foreach (var c in categories)
                for (var k = 0; k < 5; k++)
                    db.MenuItems.Add(new MenuItem
                    {
                        Id = Guid.NewGuid(), Name = $"{c.Name} Gericht {k + 1}", Description = "Mit einer Beschreibung, die eine Zeile füllt",
                        Price = 9.5m + k, CategoryId = c.Id, SortOrder = k
                    });
            return Task.CompletedTask;
        });
        return menuId;
    }

    private Task<GuestDemoData.Seeded> SeedDemoAsync() => GuestDemoData.SeedAsync(App, E2ESeedData.OwnerId, DesignTheme.Modern);

    private async Task UsePhoneAsync() => await Page.SetViewportSizeAsync(390, 844);

    private static ILocator BarChip(IPage page, string name) =>
        page.GetByTestId("category-chip").Filter(new() { HasTextRegex = ExactText(name) });

    private static ILocator SheetChip(IPage page, string name) =>
        page.GetByTestId("category-sheet").Locator("[data-category-link]").Filter(new() { HasTextRegex = ExactText(name) });

    private static ILocator Heading(IPage page, string category) =>
        page.Locator($"[data-testid='category-section'][data-category='{category}'] h2");

    private static async Task EventuallyAsync(Func<Task<bool>> condition, string because)
    {
        for (var i = 0; i < 60 && !await condition(); i++)
            await Task.Delay(100);
        (await condition()).Should().BeTrue(because);
    }

    /// <summary>
    /// The section's top sits 8 px under the sticky bars (top bar + chip bar), so its heading is right below them —
    /// or the page has ended before the section could get there and the heading is visible below the bars.
    /// </summary>
    private static Task<bool> LandedAsync(IPage page, string category) =>
        page.EvaluateAsync<bool>(@"name => {
            const section = document.querySelector(`[data-testid='category-section'][data-category='${name}']`);
            const bars = document.querySelector('[data-catnav]').getBoundingClientRect().bottom;
            const top = section.getBoundingClientRect().top;
            const heading = section.querySelector('h2').getBoundingClientRect().top;
            const atEnd = scrollY + innerHeight >= document.documentElement.scrollHeight - 2;
            return Math.abs(top - (bars - 8)) <= 2 || (atEnd && top > bars - 8 && heading < innerHeight - 40);
        }", category);

    private static async Task ExpectLandedAsync(IPage page, string category)
    {
        await EventuallyAsync(async () => await LandedAsync(page, category) && await StillAsync(page),
            $"the heading of \"{category}\" ends up right under the sticky bars");
        await Assertions.Expect(BarChip(page, category)).ToHaveAttributeAsync("aria-current", "true");
    }

    /// <summary>The page does not scroll any more (two reads 150 ms apart).</summary>
    private static async Task<bool> StillAsync(IPage page)
    {
        var before = await page.EvaluateAsync<double>("() => scrollY");
        await Task.Delay(150);
        return Math.Abs(await page.EvaluateAsync<double>("() => scrollY") - before) < 0.5;
    }

    /// <summary>Computed outline style of the focused element ("none" = no ring).</summary>
    private static Task<string> FocusedOutlineAsync(IPage page) =>
        page.EvaluateAsync<string>("() => getComputedStyle(document.activeElement).outlineStyle");

    private static Task ScrollToAsync(IPage page, string where) =>
        page.EvaluateAsync($"() => window.scrollTo(0, {where})");

    private async Task<(IBrowserContext Context, IPage Page)> TouchPhoneAsync(bool reducedMotion = false)
    {
        var context = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, ViewportSize = new ViewportSize { Width = 390, Height = 844 }, IsMobile = true, HasTouch = true,
            ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference
        });
        context.SetDefaultTimeout(15_000);
        return (context, await context.NewPageAsync());
    }

    // --- Category jumps ---

    [Fact]
    public async Task ChipBar_JumpsToFirstMiddleAndLastCategory_RightUnderTheStickyBars_HeadingFocusedWithoutRing()
    {
        var menuId = await SeedLongMenuAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(menuId);

        foreach (var (category, start) in new[]
                 {
                     (LongMenuCategories[^1], "0"), (LongMenuCategories[0], "document.documentElement.scrollHeight"),
                     (LongMenuCategories[5], "1200"), (LongMenuCategories[6], "3000")
                 })
        {
            await ScrollToAsync(Page, start);
            await Task.Delay(300);
            await BarChip(Page, category).ClickAsync();

            await ExpectLandedAsync(Page, category);
            await Assertions.Expect(Heading(Page, category)).ToBeFocusedAsync();
            (await FocusedOutlineAsync(Page)).Should().Be("none", "the heading is a focus target for screen readers, not a control");
        }
        Page.Url.Should().NotContain("#", "with JS the jump does not add history entries");
    }

    [Fact]
    public async Task CategorySheet_JumpsOnceTheSheetHasClosed_FirstMiddleLast_FocusOnTheHeadingNotTheOpener()
    {
        var menuId = await SeedLongMenuAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(menuId);

        foreach (var (category, start) in new[]
                 {
                     (LongMenuCategories[^1], "0"), (LongMenuCategories[0], "document.documentElement.scrollHeight"),
                     (LongMenuCategories[6], "900")
                 })
        {
            await ScrollToAsync(Page, start);
            await Task.Delay(300);
            await Page.GetByTestId("category-all").ClickAsync();
            var sheet = Page.GetByTestId("category-sheet");
            await Assertions.Expect(sheet).ToBeVisibleAsync();

            await SheetChip(Page, category).ClickAsync();
            // The tapped chip turns active right away, the sheet closes, then the page jumps
            await Assertions.Expect(SheetChip(Page, category)).ToHaveAttributeAsync("aria-current", "true");
            await Assertions.Expect(sheet).ToBeHiddenAsync();
            await ExpectLandedAsync(Page, category);
            await Assertions.Expect(Heading(Page, category)).ToBeFocusedAsync();
            (await FocusedOutlineAsync(Page)).Should().Be("none");
            (await Page.EvaluateAsync<bool>("() => !history.state?.gm")).Should().BeTrue("the sheet's history entry was taken back");
        }
    }

    [Theory]
    [InlineData("dropped")]
    [InlineData("stopped short")]
    public async Task Jump_WhoseSmoothScrollFails_IsCorrectedBySafetyNet(string failure)
    {
        var menuId = await SeedLongMenuAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(menuId);
        // The first smooth page scroll does not move the page at all, or stops half way
        await Page.EvaluateAsync(@"failure => {
            const original = window.scrollTo.bind(window);
            let broken = 0;
            window.scrollTo = (...args) => {
                const o = args[0];
                if (o && typeof o === 'object' && o.behavior === 'smooth' && broken++ === 0) {
                    if (failure === 'stopped short') original({ top: scrollY + (o.top - scrollY) / 2, behavior: 'auto' });
                    return;
                }
                return original(...args);
            };
            window.__broken = () => broken;
        }", failure);

        await BarChip(Page, LongMenuCategories[7]).ClickAsync();

        await ExpectLandedAsync(Page, LongMenuCategories[7]);
        (await Page.EvaluateAsync<int>("() => window.__broken()")).Should().Be(1);
        await Assertions.Expect(Heading(Page, LongMenuCategories[7])).ToBeFocusedAsync();
        await Assertions.Expect(BarChip(Page, LongMenuCategories[7])).ToBeInViewportAsync(new() { Ratio = 1 });
    }

    [Fact]
    public async Task Jump_WithReducedMotion_IsInstant_AndLandsUnderTheBars()
    {
        var menuId = await SeedLongMenuAsync();
        var (context, page) = await TouchPhoneAsync(reducedMotion: true);
        await using var _ = context;
        await OpenGuestPageAsync(page, $"/menu/{menuId}");

        await page.GetByTestId("category-all").TapAsync();
        await SheetChip(page, LongMenuCategories[8]).TapAsync();
        await ExpectLandedAsync(page, LongMenuCategories[8]);

        await BarChip(page, LongMenuCategories[2]).TapAsync();
        await ExpectLandedAsync(page, LongMenuCategories[2]);
    }

    // --- Focus rings: only for keyboard use ---

    [Fact]
    public async Task CategorySheet_OpenedByTap_ActiveChipHasFocusButNoRing_KeyboardBringsTheRingBack()
    {
        var menuId = await SeedLongMenuAsync();
        var (context, page) = await TouchPhoneAsync();
        await using var _ = context;
        await OpenGuestPageAsync(page, $"/menu/{menuId}");

        await page.GetByTestId("category-all").TapAsync();
        var active = SheetChip(page, LongMenuCategories[0]);
        await Assertions.Expect(active).ToBeFocusedAsync();
        (await FocusedOutlineAsync(page)).Should().Be("none", "after a tap the active chip looks selected, not focused");

        // A keyboard user (e.g. an external keyboard) sees where the focus is from the first key press on
        await page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(SheetChip(page, LongMenuCategories[1])).ToBeFocusedAsync();
        (await FocusedOutlineAsync(page)).Should().NotBe("none");
    }

    [Fact]
    public async Task CategorySheet_OpenedByKeyboard_ShowsTheRingOnTheActiveChip_AndJumpsToTheChosenOne()
    {
        var menuId = await SeedLongMenuAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(menuId);

        await Page.GetByTestId("category-all").FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(SheetChip(Page, LongMenuCategories[0])).ToBeFocusedAsync();
        (await FocusedOutlineAsync(Page)).Should().NotBe("none", "keyboard users see where the focus is");

        await Page.Keyboard.PressAsync("Tab");
        await Page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(SheetChip(Page, LongMenuCategories[2])).ToBeFocusedAsync();
        (await FocusedOutlineAsync(Page)).Should().NotBe("none");
        await Page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(Page.GetByTestId("category-sheet")).ToBeHiddenAsync();
        await ExpectLandedAsync(Page, LongMenuCategories[2]);
        await Assertions.Expect(Heading(Page, LongMenuCategories[2])).ToBeFocusedAsync();
        (await FocusedOutlineAsync(Page)).Should().Be("none", "the heading never shows a ring, the next Tab continues in its section");
    }

    [Fact]
    public async Task Sheets_OpenedAndClosedByTap_ShowNoFocusRing_EscapeRestoresTheRingForKeyboardUsers()
    {
        var demo = await SeedDemoAsync();
        await GuestDemoData.SeedEnglishVersionAsync(App, E2ESeedData.OwnerId, demo, DesignTheme.Modern);
        var (context, page) = await TouchPhoneAsync();
        await using var _ = context;
        await OpenGuestPageAsync(page, $"/menu/{demo.MenuId}");
        await ItemEntry(page, "Burrata").GetByTestId("favorite-button").TapAsync();

        var sheets = new (string Opener, string Sheet)[]
        {
            ("info-button", "info-sheet"), ("language-button", "language-sheet"), ("filter-button", "filter-sheet"),
            ("search-button", "search-sheet"), ("favorites-button", "favorites-sheet"), ("category-all", "category-sheet")
        };
        foreach (var (opener, sheetId) in sheets)
        {
            await page.GetByTestId(opener).TapAsync();
            var sheet = page.GetByTestId(sheetId);
            await Assertions.Expect(sheet).ToBeVisibleAsync();
            await EventuallyAsync(async () => await page.EvaluateAsync<bool>("id => document.querySelector(`[data-testid='${id}']`).contains(document.activeElement)", sheetId),
                $"the focus moves into {sheetId}");
            (await FocusedOutlineAsync(page)).Should().Be("none", $"{sheetId} was opened by a tap");

            await sheet.Locator("[data-sheet-close]").First.TapAsync();
            await Assertions.Expect(sheet).ToBeHiddenAsync();
            await Assertions.Expect(page.GetByTestId(opener)).ToBeFocusedAsync();
            (await FocusedOutlineAsync(page)).Should().Be("none", $"the opener of {sheetId} gets the focus back without a ring");
        }

        // Item sheet by tap, closed by tap: no ring on the card link either
        await ItemEntry(page, "Tiramisu").Locator("a[data-item-link]").TapAsync();
        await Assertions.Expect(ItemSheetOf(page)).ToBeVisibleAsync();
        (await FocusedOutlineAsync(page)).Should().Be("none");
        await ItemSheetOf(page).Locator("[data-sheet-close]").TapAsync();
        await Assertions.Expect(ItemSheetOf(page)).ToBeHiddenAsync();
        (await FocusedOutlineAsync(page)).Should().Be("none");

        // Keyboard: Escape closes, the opener shows the ring
        await page.GetByTestId("search-button").TapAsync();
        await Assertions.Expect(page.GetByTestId("search-sheet")).ToBeVisibleAsync();
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(page.GetByTestId("search-sheet")).ToBeHiddenAsync();
        await Assertions.Expect(page.GetByTestId("search-button")).ToBeFocusedAsync();
        (await FocusedOutlineAsync(page)).Should().NotBe("none");
    }

    // --- Drag down to close ---

    /// <summary>Center of an element's box in viewport coordinates.</summary>
    private static async Task<(float X, float Y)> CenterAsync(ILocator locator)
    {
        var box = (await locator.BoundingBoxAsync())!;
        return (box.X + box.Width / 2, box.Y + box.Height / 2);
    }

    /// <summary>Drags with the mouse (pointer events like a finger), slowly: pauses between the moves.</summary>
    private static async Task SlowDragAsync(IPage page, (float X, float Y) from, float dy, int steps = 8, bool release = true)
    {
        await page.Mouse.MoveAsync(from.X, from.Y);
        await page.Mouse.DownAsync();
        for (var i = 1; i <= steps; i++)
        {
            await page.Mouse.MoveAsync(from.X, from.Y + dy * i / steps);
            await Task.Delay(40);
        }
        if (!release) return;
        await Task.Delay(150);   // the finger rests: no flick
        await page.Mouse.UpAsync();
    }

    private static Task<double> PanelTopAsync(ILocator sheet) =>
        sheet.EvaluateAsync<double>("d => d.querySelector('.gm-sheet-panel').getBoundingClientRect().top");

    private static Task<double> PanelHeightAsync(ILocator sheet) =>
        sheet.EvaluateAsync<double>("d => d.querySelector('.gm-sheet-panel').getBoundingClientRect().height");

    [Fact]
    public async Task ItemSheet_DraggedDownByItsHandle_Closes_TakesBackItsHistoryEntry_AndRestoresTheFocus()
    {
        var demo = await SeedDemoAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(demo.MenuId);
        var menuUrl = Page.Url;

        await ClickMenuItemAsync("Burrata");
        await Task.Delay(400);   // slide-up animation
        var sheet = ItemSheet;
        var handle = sheet.Locator(".gm-sheet-handle");
        // The pill stays 40 × 5, its hit area is at least 44 px tall
        var hitArea = await handle.EvaluateAsync<double>("h => parseFloat(getComputedStyle(h, '::before').height)");
        hitArea.Should().BeGreaterThanOrEqualTo(44);
        (await handle.EvaluateAsync<string>("h => getComputedStyle(h).touchAction")).Should().Be("none");

        var height = await PanelHeightAsync(sheet);
        var start = await CenterAsync(handle);
        await SlowDragAsync(Page, start, (float)(height * 0.45), release: false);
        // Mid-drag: the panel follows the finger, the backdrop fades
        (await PanelTopAsync(sheet)).Should().BeApproximately(844 - height + height * 0.45, 3);
        (await sheet.EvaluateAsync<double>("d => parseFloat(getComputedStyle(d).getPropertyValue('--gm-drag-fade'))")).Should().BeApproximately(0.45, 0.02);
        await Page.Mouse.UpAsync();

        await Assertions.Expect(sheet).ToBeHiddenAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(menuUrl);
        (await Page.EvaluateAsync<bool>("() => !history.state?.gm")).Should().BeTrue();
        await Assertions.Expect(ItemEntry("Burrata").Locator("a[data-item-link]")).ToBeFocusedAsync();
        (await sheet.EvaluateAsync<string>("d => d.style.translate")).Should().BeEmpty("the drag offset is reset for the next opening");

        // The entry was consumed: back leaves the menu instead of reopening the sheet
        await Page.GoBackAsync();
        await Assertions.Expect(Page).ToHaveURLAsync("about:blank");
    }

    [Fact]
    public async Task SmallDrag_SpringsBack_AndTheSheetStaysOpen()
    {
        var demo = await SeedDemoAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(demo.MenuId);
        await Page.GetByTestId("category-all").ClickAsync();
        var sheet = Page.GetByTestId("category-sheet");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await Task.Delay(400);
        var restingTop = await PanelTopAsync(sheet);
        var height = await PanelHeightAsync(sheet);

        await SlowDragAsync(Page, await CenterAsync(sheet.Locator(".gm-sheet-title")), (float)(height * 0.15));

        await Task.Delay(500);
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        (await PanelTopAsync(sheet)).Should().BeApproximately(restingTop, 1);
        (await Page.EvaluateAsync<string?>("() => history.state?.gm?.sheet")).Should().Be("categories");

        // Pulling up only stretches a little (rubber band), then it snaps back too
        await SlowDragAsync(Page, await CenterAsync(sheet.Locator(".gm-sheet-title")), -120, release: false);
        (await PanelTopAsync(sheet)).Should().BeInRange(restingTop - 16.5, restingTop);
        await Page.Mouse.UpAsync();
        await Task.Delay(500);
        (await PanelTopAsync(sheet)).Should().BeApproximately(restingTop, 1);
        await Assertions.Expect(sheet).ToBeVisibleAsync();
    }

    [Fact]
    public async Task FastFlick_Closes_EvenAShortOne()
    {
        var demo = await SeedDemoAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(demo.MenuId);
        await Page.GetByTestId("search-button").ClickAsync();
        var sheet = Page.GetByTestId("search-sheet");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await Task.Delay(400);
        var height = await PanelHeightAsync(sheet);

        // 60 px in ~30 ms by touch: far below a quarter of the panel, but fast. Dispatched in the page, so the
        // timing does not depend on the test runner's round trips (a mouse flick from here is not reliably fast)
        (60 / height).Should().BeLessThan(0.2);
        await sheet.Locator(".gm-sheet-title").EvaluateAsync(@"title => {
            const zone = title.closest('.gm-sheet-head');
            const r = title.getBoundingClientRect();
            const x = r.left + r.width / 2, y0 = r.top + r.height / 2;
            const fire = (type, y) => zone.dispatchEvent(new PointerEvent(type, {
                bubbles: true, cancelable: true, pointerId: 7, pointerType: 'touch', isPrimary: true, clientX: x, clientY: y, button: 0 }));
            const wait = ms => { const end = performance.now() + ms; while (performance.now() < end) { } };
            fire('pointerdown', y0);
            for (let i = 1; i <= 4; i++) { wait(8); fire('pointermove', y0 + 15 * i); }
            fire('pointerup', y0 + 60);
        }");

        await Assertions.Expect(sheet).ToBeHiddenAsync();
        (await Page.EvaluateAsync<bool>("() => !history.state?.gm")).Should().BeTrue();
        await Assertions.Expect(Page.GetByTestId("search-button")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task HeaderBand_DragsTheSheet_ButItsCloseButtonStaysATap()
    {
        var demo = await SeedDemoAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(demo.MenuId);
        var sheet = Page.GetByTestId("filter-sheet");

        // A tap on the close button in the header band closes as before
        await Page.GetByTestId("filter-button").ClickAsync();
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await sheet.Locator(".gm-sheet-head [data-sheet-close]").ClickAsync();
        await Assertions.Expect(sheet).ToBeHiddenAsync();
        await Assertions.Expect(Page.GetByTestId("filter-button")).ToBeFocusedAsync();

        // A slow drag from the title beyond a quarter of the panel closes it
        await Page.GetByTestId("filter-button").ClickAsync();
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await Task.Delay(400);
        var height = await PanelHeightAsync(sheet);
        await SlowDragAsync(Page, await CenterAsync(sheet.Locator(".gm-sheet-title")), (float)(height * 0.35));
        await Assertions.Expect(sheet).ToBeHiddenAsync();
        (await Page.EvaluateAsync<bool>("() => !history.state?.gm")).Should().BeTrue();
        await Assertions.Expect(Page.GetByTestId("filter-button")).ToBeFocusedAsync();

        // A press on the close button that slides a little is no drag: the sheet does not move, the tap closes it
        await Page.GetByTestId("filter-button").ClickAsync();
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await Task.Delay(400);
        var closeButton = sheet.Locator(".gm-sheet-head [data-sheet-close]");
        var restingTop = await PanelTopAsync(sheet);
        await SlowDragAsync(Page, await CenterAsync(closeButton), 12, steps: 3, release: false);
        (await PanelTopAsync(sheet)).Should().BeApproximately(restingTop, 0.5, "a press on a button does not move the sheet");
        await Page.Mouse.UpAsync();
        await Assertions.Expect(sheet).ToBeHiddenAsync();
    }

    [Fact]
    public async Task EscapeDuringADrag_ClosesTheSheet_AndTheNextDragWorks()
    {
        var demo = await SeedDemoAsync();
        await UsePhoneAsync();
        await OpenMenuAsync(demo.MenuId);
        var sheet = Page.GetByTestId("category-sheet");
        await Page.GetByTestId("category-all").ClickAsync();
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await Task.Delay(400);

        await SlowDragAsync(Page, await CenterAsync(sheet.Locator(".gm-sheet-handle")), 40, steps: 4, release: false);
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(sheet).ToBeHiddenAsync();
        await Page.Mouse.UpAsync();
        (await sheet.EvaluateAsync<string>("d => d.style.translate")).Should().BeEmpty();

        await Page.GetByTestId("category-all").ClickAsync();
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await Task.Delay(400);
        (await sheet.EvaluateAsync<string>("d => d.style.translate")).Should().BeEmpty("the sheet opens at its place");
        var height = await PanelHeightAsync(sheet);
        await SlowDragAsync(Page, await CenterAsync(sheet.Locator(".gm-sheet-handle")), (float)(height * 0.5));
        await Assertions.Expect(sheet).ToBeHiddenAsync();
    }

    [Fact]
    public async Task Drag_WithReducedMotion_ClosesWithoutAnimation()
    {
        var demo = await SeedDemoAsync();
        await using var context = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, ViewportSize = new ViewportSize { Width = 390, Height = 844 }, ReducedMotion = ReducedMotion.Reduce
        });
        var page = await context.NewPageAsync();
        await OpenGuestPageAsync(page, $"/menu/{demo.MenuId}");
        await page.GetByTestId("category-all").ClickAsync();
        var sheet = page.GetByTestId("category-sheet");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        var height = await PanelHeightAsync(sheet);

        await SlowDragAsync(page, await CenterAsync(sheet.Locator(".gm-sheet-handle")), (float)(height * 0.4));
        (await sheet.EvaluateAsync<bool>("d => d.open")).Should().BeFalse("with reduced motion the sheet closes at once");
        await Assertions.Expect(page.GetByTestId("category-all")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Desktop_CenteredSheet_CannotBeDragged()
    {
        var demo = await SeedDemoAsync();
        await OpenMenuAsync(demo.MenuId);   // 1280 × 900
        await Page.GetByTestId("category-all").ClickAsync();
        var sheet = Page.GetByTestId("category-sheet");
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await Assertions.Expect(sheet.Locator(".gm-sheet-handle")).ToBeHiddenAsync();
        await Task.Delay(400);
        var restingTop = await PanelTopAsync(sheet);

        await SlowDragAsync(Page, await CenterAsync(sheet.Locator(".gm-sheet-title")), 300);

        await Task.Delay(300);
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        (await PanelTopAsync(sheet)).Should().BeApproximately(restingTop, 1);
        (await sheet.EvaluateAsync<string>("d => d.style.translate")).Should().BeEmpty();
    }
}
