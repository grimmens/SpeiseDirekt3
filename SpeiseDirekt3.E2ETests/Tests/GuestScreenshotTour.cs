using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Not a test in the usual sense: screenshots of the guest menu for design reviews, with the demo menu of
/// <see cref="GuestDemoData"/> (photos, text-only drinks) in every design theme: phone (390 px) and desktop
/// (1280 px), open item sheet, category and search sheet, the drinks list, the page without JavaScript, a notice
/// page and the menu editor's phone frame. Runs only when <c>E2E_SCREENSHOT_DIR</c> is set; guest pages need no
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
        await using var phoneContext = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
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

        // Without JavaScript
        await using (var noJs = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
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
        }

        // Notice page
        await phone.GotoAsync($"/menu/{Guid.NewGuid()}");
        await Shot(phone, "phone-notice-notfound");

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
}
