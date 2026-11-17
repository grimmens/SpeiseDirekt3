using System.Text.Json;
using FluentAssertions;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// AP8, from the operator's iPhone: the search sheet "does not open properly" — keyboard up, the search field off-screen,
/// the end of the page showing above the keyboard. On phones the search sheet is now a full-screen panel anchored to the
/// top (<c>gm-sheet--full</c>, <c>js/guest-menu.js</c>): the field is at its final place when it takes the focus inside
/// the tap (the panel fades in, no slide), the panel follows the visual viewport (the area above the keyboard), the page
/// behind cannot scroll and is back where it was after the close. Headless browsers have no virtual keyboard: these tests
/// check the structure the fix relies on, and a stand-in for <c>window.visualViewport</c> simulates the keyboard.
/// From 768 px the search stays the centered dialog.
/// </summary>
public class GuestSearchPhoneTests : E2ETestBase
{
    public GuestSearchPhoneTests(E2EFixture fixture) : base(fixture) { }

    private const int Width = 390;
    private const int Height = 844;

    private Task<GuestDemoData.Seeded> SeedDemoAsync() => GuestDemoData.SeedAsync(App, E2ESeedData.OwnerId, DesignTheme.Modern);

    private async Task<(IBrowserContext Context, IPage Page)> TouchPhoneAsync(bool reducedMotion = false)
    {
        var context = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, ViewportSize = new ViewportSize { Width = Width, Height = Height }, IsMobile = true, HasTouch = true,
            ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference
        });
        context.SetDefaultTimeout(15_000);
        return (context, await context.NewPageAsync());
    }

    private static ILocator Sheet(IPage page) => page.GetByTestId("search-sheet");
    private static ILocator Input(IPage page) => Sheet(page).GetByRole(AriaRole.Searchbox);

    private sealed record Box(double Top, double Left, double Width, double Height)
    {
        public double Bottom => Top + Height;
    }

    private static async Task<Box> BoxAsync(IPage page, string selector)
    {
        var r = await page.EvaluateAsync<double[]>(
            "s => { const r = document.querySelector(s).getBoundingClientRect(); return [r.top, r.left, r.width, r.height]; }", selector);
        return new Box(r[0], r[1], r[2], r[3]);
    }

    private const string Dialog = "dialog[data-sheet='search']";
    private const string Panel = "dialog[data-sheet='search'] .gm-sheet-panel";
    private const string Field = "dialog[data-sheet='search'] .gm-search-field";
    private const string Body = "dialog[data-sheet='search'] [data-sheet-body]";

    private static Task<double> ScrollYAsync(IPage page) => page.EvaluateAsync<double>("() => scrollY");

    private static async Task ScrollPageToAsync(IPage page, int y)
    {
        await page.EvaluateAsync($"() => window.scrollTo(0, {y})");
        (await ScrollYAsync(page)).Should().BeApproximately(y, 1, "the demo menu is long enough");
    }

    /// <summary>
    /// Records, at the moment the search field gets the focus: the field's and the dialog's position and the dialog's
    /// transform and animation. That is the moment iOS opens the keyboard and scrolls to reveal the field.
    /// </summary>
    private static Task RecordFocusMomentAsync(IPage page) => page.EvaluateAsync(@"() => {
        window.__searchFocus = null;
        document.addEventListener('focusin', e => {
            if (e.target.id !== 'gm-search-input' || window.__searchFocus) return;
            const dialog = e.target.closest('dialog');
            const field = e.target.closest('.gm-search-field').getBoundingClientRect();
            const box = dialog.getBoundingClientRect();
            const style = getComputedStyle(dialog);
            window.__searchFocus = { fieldTop: field.top, fieldBottom: field.bottom, dialogTop: box.top, dialogHeight: box.height,
                                     transform: style.transform, animation: style.animationName };
        }, true);
    }");

    private sealed record FocusMoment(double FieldTop, double FieldBottom, double DialogTop, double DialogHeight, string Transform, string Animation);

    private static async Task<FocusMoment> FocusMomentAsync(IPage page)
    {
        var json = await page.EvaluateAsync<string?>("() => window.__searchFocus && JSON.stringify(window.__searchFocus)");
        json.Should().NotBeNull("the search field got the focus");
        return JsonSerializer.Deserialize<FocusMoment>(json!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
    }

    /// <summary>
    /// Replaces window.visualViewport with a stand-in (js/guest-menu.js reads it when the sheet opens); window.__keyboard(h, top)
    /// then simulates what iOS reports when the keyboard opens: a smaller visual viewport, possibly panned.
    /// </summary>
    private static Task FakeVisualViewportAsync(IPage page) => page.EvaluateAsync(@"() => {
        const vv = new EventTarget();
        Object.assign(vv, { width: innerWidth, height: innerHeight, offsetLeft: 0, offsetTop: 0, pageLeft: 0, pageTop: 0, scale: 1 });
        Object.defineProperty(window, 'visualViewport', { configurable: true, get: () => vv });
        window.__vv = vv;
        window.__keyboard = (height, offsetTop = 0, scale = 1) => {
            Object.assign(vv, { height, offsetTop, scale });
            vv.dispatchEvent(new Event('resize'));
            vv.dispatchEvent(new Event('scroll'));
        };
    }");

    private static Task KeyboardAsync(IPage page, double height, double offsetTop = 0, double scale = 1) =>
        page.EvaluateAsync($"() => window.__keyboard({height}, {offsetTop}, {scale})");

    [Fact]
    public async Task Phone_OpensFullScreenFromTheTop_FieldFocusedInsideTheTapAtItsFinalPlace_PageStays()
    {
        var demo = await SeedDemoAsync();
        var (context, page) = await TouchPhoneAsync();
        await using var _ = context;
        await OpenGuestPageAsync(page, $"/menu/{demo.MenuId}");
        await ScrollPageToAsync(page, 1200);
        await RecordFocusMomentAsync(page);

        await page.GetByTestId("search-button").TapAsync();

        // Inside the tap (no waiting): iOS opens the keyboard only for a focus during the gesture
        (await page.EvaluateAsync<string>("() => document.activeElement?.id")).Should().Be("gm-search-input");
        var moment = await FocusMomentAsync(page);
        moment.Transform.Should().Be("none", "nothing moves the panel while the field takes the focus");
        moment.Animation.Should().Be("gm-fade-in", "the full-screen panel fades in instead of sliding up");
        moment.DialogTop.Should().BeApproximately(0, 0.5);
        moment.FieldTop.Should().BeInRange(0, Height * 0.2, "the field is at the top of the screen, far above any keyboard");

        await page.WaitForTimeoutAsync(400);
        var dialog = await BoxAsync(page, Dialog);
        dialog.Top.Should().BeApproximately(0, 0.5);
        dialog.Left.Should().BeApproximately(0, 0.5);
        dialog.Width.Should().BeApproximately(Width, 0.5);
        dialog.Height.Should().BeApproximately(Height, 0.5);
        (await BoxAsync(page, Field)).Top.Should().BeApproximately(moment.FieldTop, 0.5, "the field was already at its final place when it got the focus");
        (await page.EvaluateAsync<string>($"() => getComputedStyle(document.querySelector(\"{Panel}\")).borderTopLeftRadius")).Should().Be("0px");
        await Assertions.Expect(Sheet(page).Locator(".gm-sheet-handle")).ToBeHiddenAsync();

        // Nothing of the page shows: every corner and the middle belong to the panel
        var covered = await page.EvaluateAsync<bool>(@"() => {
            const dialog = document.querySelector(""dialog[data-sheet='search']"");
            const points = [[4, 4], [innerWidth - 4, 4], [innerWidth / 2, innerHeight / 2], [4, innerHeight - 4], [innerWidth - 4, innerHeight - 4]];
            return points.every(([x, y]) => dialog.contains(document.elementFromPoint(x, y)));
        }");
        covered.Should().BeTrue();
        (await ScrollYAsync(page)).Should().BeApproximately(1200, 1, "the page behind did not move");
        (await page.EvaluateAsync<bool>("() => document.documentElement.classList.contains('gm-locked')")).Should().BeTrue();

        // No drag to close: the header band does not move the full-screen panel
        var title = (await Sheet(page).Locator(".gm-sheet-title").BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(title.X + title.Width / 2, title.Y + title.Height / 2);
        await page.Mouse.DownAsync();
        for (var i = 1; i <= 6; i++)
            await page.Mouse.MoveAsync(title.X + title.Width / 2, title.Y + title.Height / 2 + 50 * i);
        await page.Mouse.UpAsync();
        await page.WaitForTimeoutAsync(400);
        await Assertions.Expect(Sheet(page)).ToBeVisibleAsync();
        (await BoxAsync(page, Dialog)).Top.Should().BeApproximately(0, 0.5);
        (await Sheet(page).EvaluateAsync<string>("d => d.style.translate")).Should().BeEmpty();

        // Close button: back on the page where it was, the focus on the search button
        await Sheet(page).Locator(".gm-sheet-head [data-sheet-close]").TapAsync();
        await Assertions.Expect(Sheet(page)).ToBeHiddenAsync();
        (await ScrollYAsync(page)).Should().BeApproximately(1200, 1);
        await Assertions.Expect(page.GetByTestId("search-button")).ToBeFocusedAsync();
    }

    [Fact]
    public async Task Phone_SimulatedKeyboard_PanelFillsTheAreaAboveIt_ResultsScrollThere_PageComesBackOnClose()
    {
        var demo = await SeedDemoAsync();
        var (context, page) = await TouchPhoneAsync();
        await using var _ = context;
        await OpenGuestPageAsync(page, $"/menu/{demo.MenuId}");
        await ScrollPageToAsync(page, 600);
        await FakeVisualViewportAsync(page);

        await page.GetByTestId("search-button").TapAsync();
        await Assertions.Expect(Sheet(page)).ToBeVisibleAsync();
        await Input(page).FillAsync("a");
        var results = Sheet(page).Locator("[data-search-row]:visible");
        (await results.CountAsync()).Should().BeGreaterThan(10);

        // Keyboard up: the visual viewport is 460 px high
        await KeyboardAsync(page, 460);
        var dialog = await BoxAsync(page, Dialog);
        dialog.Top.Should().BeApproximately(0, 0.5);
        dialog.Height.Should().BeApproximately(460, 0.5);
        (await BoxAsync(page, Panel)).Bottom.Should().BeLessThanOrEqualTo(460.5);
        var field = await BoxAsync(page, Field);
        field.Top.Should().BeInRange(0, 120);
        var body = await BoxAsync(page, Body);
        body.Bottom.Should().BeLessThanOrEqualTo(460.5, "the results end at the keyboard");

        // The results scroll inside the panel, under the field; the page does not move
        var sizes = await page.EvaluateAsync<double[]>($"() => {{ const b = document.querySelector(\"{Body}\"); return [b.scrollHeight, b.clientHeight]; }}");
        sizes[0].Should().BeGreaterThan(sizes[1], "more results than fit above the keyboard");
        await page.EvaluateAsync($"() => {{ const b = document.querySelector(\"{Body}\"); b.scrollTop = b.scrollHeight; }}");
        var last = await results.Last.BoundingBoxAsync();
        ((double)last!.Y + last.Height).Should().BeLessThanOrEqualTo(460.5, "the last result can be scrolled into the area above the keyboard");
        (await BoxAsync(page, Field)).Top.Should().BeApproximately(field.Top, 0.5, "the field stays at the top while the results scroll");
        (await ScrollYAsync(page)).Should().BeApproximately(600, 1);

        // iOS panned the visual viewport: the panel follows it
        await KeyboardAsync(page, 460, 150);
        dialog = await BoxAsync(page, Dialog);
        dialog.Top.Should().BeApproximately(150, 0.5);
        dialog.Height.Should().BeApproximately(460, 0.5);

        // Pinch zoom: the zoomed visual viewport is no free area, the panel keeps the full height
        await KeyboardAsync(page, 422, 0, 2);
        dialog = await BoxAsync(page, Dialog);
        dialog.Top.Should().BeApproximately(0, 0.5);
        dialog.Height.Should().BeApproximately(Height, 0.5);
        await KeyboardAsync(page, 460);

        // Something scrolled the page behind the panel (as iOS does to reveal a field): Escape puts it back (on an empty
        // field — in a filled type=search field the first Escape only clears it)
        await Input(page).FillAsync("");
        await page.EvaluateAsync("() => window.scrollTo(0, document.documentElement.scrollHeight)");
        (await ScrollYAsync(page)).Should().BeGreaterThan(600);
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Sheet(page)).ToBeHiddenAsync();
        (await ScrollYAsync(page)).Should().BeApproximately(600, 1);

        // Closed: the panel no longer follows the viewport
        await KeyboardAsync(page, 300);
        (await Sheet(page).EvaluateAsync<string>("d => d.style.getPropertyValue('--gm-vv-height')")).Should().BeEmpty();

        // The back button closes it too and the page stays where it was
        await KeyboardAsync(page, Height);
        await page.GetByTestId("search-button").TapAsync();
        await Assertions.Expect(Sheet(page)).ToBeVisibleAsync();
        (await BoxAsync(page, Dialog)).Height.Should().BeApproximately(Height, 0.5);
        await page.EvaluateAsync("() => window.scrollTo(0, 0)");
        await page.GoBackAsync();
        await Assertions.Expect(Sheet(page)).ToBeHiddenAsync();
        (await ScrollYAsync(page)).Should().BeApproximately(600, 1);
        await Assertions.Expect(page).ToHaveURLAsync(new System.Text.RegularExpressions.Regex($@"/menu/{demo.MenuId}$"));
    }

    [Fact]
    public async Task Phone_TypingClearingAndOpeningAResult_Work_AndBackReturnsToThePagePosition()
    {
        var demo = await SeedDemoAsync();
        var (context, page) = await TouchPhoneAsync();
        await using var _ = context;
        await OpenGuestPageAsync(page, $"/menu/{demo.MenuId}");
        await ScrollPageToAsync(page, 900);

        await page.GetByTestId("search-button").TapAsync();
        await Input(page).FillAsync("pizza");
        await Assertions.Expect(Sheet(page).Locator("[data-search-row]:visible")).ToHaveCountAsync(3);
        await Sheet(page).Locator("[data-search-clear]").TapAsync();
        await Assertions.Expect(Input(page)).ToHaveValueAsync("");
        await Assertions.Expect(Input(page)).ToBeFocusedAsync();
        await Assertions.Expect(Sheet(page).Locator("[data-search-row]:visible")).ToHaveCountAsync(0);

        await Input(page).FillAsync("xyz");
        await Assertions.Expect(Sheet(page).GetByTestId("search-empty")).ToBeVisibleAsync();
        await Input(page).FillAsync("tonnato");
        await page.EvaluateAsync("() => window.scrollTo(0, 0)");       // as if iOS had moved the page behind the panel
        await Sheet(page).Locator("[data-search-row]:visible a").TapAsync();
        await Assertions.Expect(Sheet(page)).ToBeHiddenAsync();
        await Assertions.Expect(ItemSheetOf(page).GetByRole(AriaRole.Heading, new() { Name = "Vitello Tonnato" })).ToBeVisibleAsync();
        (await ScrollYAsync(page)).Should().BeApproximately(900, 1, "the page is back where it was before the search");

        // Closing the item continues at its card in the menu (the search result that opened it is gone), as before
        await page.GoBackAsync();
        await Assertions.Expect(ItemSheetOf(page)).ToBeHiddenAsync();
        await Assertions.Expect(ItemEntry(page, "Vitello Tonnato").Locator("a[data-item-link]")).ToBeFocusedAsync();
        await Assertions.Expect(ItemEntry(page, "Vitello Tonnato")).ToBeInViewportAsync();
    }

    [Fact]
    public async Task Phone_ATouchThatCouldOnlyScrollThePage_IsCancelled_ResultsFieldAndPinchAreNot()
    {
        var demo = await SeedDemoAsync();
        var (context, page) = await TouchPhoneAsync();
        await using var _ = context;
        await OpenGuestPageAsync(page, $"/menu/{demo.MenuId}");
        await ScrollPageToAsync(page, 500);
        await page.GetByTestId("search-button").TapAsync();
        await Assertions.Expect(Sheet(page)).ToBeVisibleAsync();

        // Whether a synthetic one- or two-finger touchmove at an element is cancelled (= the page behind cannot move)
        Task<bool> CancelledAsync(string selector, int fingers = 1) => page.EvaluateAsync<bool>(@"([selector, fingers]) => {
            const target = document.querySelector(selector);
            const r = target.getBoundingClientRect();
            const touches = Array.from({ length: fingers }, (_, i) => new Touch({ identifier: i + 1, target, clientX: r.left + 10 + 20 * i, clientY: r.top + 5 }));
            const event = new TouchEvent('touchmove', { bubbles: true, cancelable: true, touches, targetTouches: touches, changedTouches: touches });
            target.dispatchEvent(event);
            return event.defaultPrevented;
        }", new object[] { selector, fingers });

        // No results: the body has nothing to scroll, a drag there would move the page
        (await CancelledAsync($"{Body} [data-search-status]")).Should().BeTrue();
        (await CancelledAsync($"{Dialog} .gm-sheet-title")).Should().BeTrue();
        (await CancelledAsync($"{Dialog} #gm-search-input")).Should().BeFalse("the field keeps its caret and selection gestures");
        (await CancelledAsync($"{Body} [data-search-status]", fingers: 2)).Should().BeFalse("pinch zoom stays possible");

        // Many results: the body scrolls, its touches are left alone; the wheel scrolls the results, not the page
        await Input(page).FillAsync("a");
        (await CancelledAsync($"{Body} [data-search-row]:not([hidden])")).Should().BeFalse();
        var row = (await Sheet(page).Locator("[data-search-row]:visible").Nth(2).BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(row.X + row.Width / 2, row.Y + row.Height / 2);
        await page.Mouse.WheelAsync(0, 400);
        await page.WaitForTimeoutAsync(300);
        (await page.EvaluateAsync<double>($"() => document.querySelector(\"{Body}\").scrollTop")).Should().BeGreaterThan(0);
        (await ScrollYAsync(page)).Should().BeApproximately(500, 1);
    }

    [Fact]
    public async Task Phone_ReducedMotion_OpensAndClosesWithoutAnimation()
    {
        var demo = await SeedDemoAsync();
        var (context, page) = await TouchPhoneAsync(reducedMotion: true);
        await using var _ = context;
        await OpenGuestPageAsync(page, $"/menu/{demo.MenuId}");
        await RecordFocusMomentAsync(page);

        await page.GetByTestId("search-button").TapAsync();
        var moment = await FocusMomentAsync(page);
        moment.Animation.Should().Be("none");
        moment.FieldTop.Should().BeInRange(0, Height * 0.2);
        await Sheet(page).Locator(".gm-sheet-head [data-sheet-close]").TapAsync();
        (await Sheet(page).EvaluateAsync<bool>("d => d.open")).Should().BeFalse("with reduced motion it closes at once");
    }

    [Fact]
    public async Task Desktop_SearchStaysTheCenteredDialog()
    {
        var demo = await SeedDemoAsync();
        await OpenMenuAsync(demo.MenuId);
        await FakeVisualViewportAsync(Page);
        await RecordFocusMomentAsync(Page);

        await Page.GetByTestId("search-button").ClickAsync();
        await Assertions.Expect(Sheet(Page)).ToBeVisibleAsync();
        await Assertions.Expect(Input(Page)).ToBeFocusedAsync();
        (await FocusMomentAsync(Page)).Animation.Should().Be("gm-pop-in");
        await Page.WaitForTimeoutAsync(300);

        var dialog = await BoxAsync(Page, Dialog);
        dialog.Width.Should().BeApproximately(520, 0.5);
        // Centered on the page (whose width leaves out the scrollbar gutter)
        var page = await BoxAsync(Page, "[data-guest-root]");
        (dialog.Left + dialog.Width / 2).Should().BeApproximately(page.Left + page.Width / 2, 1);
        dialog.Top.Should().BeGreaterThan(20);
        dialog.Height.Should().BeLessThan(900 * 0.87);
        (await Page.EvaluateAsync<string>($"() => getComputedStyle(document.querySelector(\"{Panel}\")).borderTopLeftRadius")).Should().NotBe("0px");
        (await Page.EvaluateAsync<string>("() => getComputedStyle(document.querySelector('.gm-search-bar')).position")).Should().Be("static");

        // A smaller visual viewport (the variables are set) changes nothing at this width
        await KeyboardAsync(Page, 400, 100);
        (await BoxAsync(Page, Dialog)).Should().BeEquivalentTo(dialog);

        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Sheet(Page)).ToBeHiddenAsync();
        await Assertions.Expect(Page.GetByTestId("search-button")).ToBeFocusedAsync();
    }
}
