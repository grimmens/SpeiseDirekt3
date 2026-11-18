using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// The back-office chrome (AP9): the top navigation with its active entry, the menu context bar (tabs, menu switcher,
/// preview), the user menu, keyboard use, the phone drawer, the context strip on phones, the anonymous header and the
/// logo/favicon files. Layout: Components/Layout, css/shell.css, js/shell.js.
/// </summary>
public class BackOfficeNavigationTests : QrCalendarTestBase
{
    public BackOfficeNavigationTests(E2EFixture fixture) : base(fixture) { }

    private ILocator TopNav => Page.GetByTestId("top-nav");
    private ILocator ActiveTopLink => TopNav.Locator("a[aria-current='page']");
    private ILocator Context => Page.GetByTestId("menu-context");
    private ILocator Tabs => Page.GetByTestId("menu-tabs");
    private ILocator ActiveTab => Tabs.Locator("a[aria-current='page']");
    private ILocator Tab(string name) => Tabs.GetByRole(AriaRole.Link, new() { Name = name, Exact = true });
    private ILocator UserButton => Page.GetByTestId("user-menu-button");
    private ILocator UserMenu => Page.GetByTestId("user-menu");
    private ILocator SwitcherButton => Page.GetByTestId("menu-switcher-button");
    private ILocator Switcher => Page.GetByTestId("menu-switcher");
    private ILocator DrawerButton => Page.GetByTestId("nav-drawer-button");
    private ILocator OpenDrawer => Page.Locator("dialog[data-testid='nav-drawer'][open]");

    private async Task<Guid> FirstCategoryAsync(Guid ownerId, Guid menuId)
    {
        await using var db = App.CreateDbContext(ownerId);
        return await db.Categories.Where(c => c.MenuId == menuId).Select(c => c.Id).FirstAsync();
    }

    private Task<string?> FocusedTestIdAsync() =>
        Page.EvaluateAsync<string?>("() => document.activeElement?.getAttribute('data-testid') ?? null");

    // --- Top navigation ---

    [Fact]
    public async Task TopNav_MarksTheSectionOfEveryPage()
    {
        var ownerId = await RegisterOwnerAsync();
        var menuId = await SeedMenuAsync(ownerId, "Abschnittskarte");
        var categoryId = await FirstCategoryAsync(ownerId, menuId);
        var qrId = await SeedQrAsync(ownerId, new QRCode { Title = "Tisch 1", MenuId = menuId });

        var expected = new (string Url, string Section)[]
        {
            ("/dashboard", "Dashboard"),
            ("/menus", "Menus"),
            ($"/menudetail/{menuId}", "Menus"),
            ($"/Categories/{menuId}", "Menus"),
            ($"/MenuItems/Category/{categoryId}", "Menus"),
            ($"/allergens/{menuId}", "Menus"),
            ($"/combos/{menuId}", "Menus"),
            ($"/menu/edit/{menuId}", "Menus"),
            ("/menu/edit", "Menus"),
            ("/qrcodes", "QR Codes"),
            ($"/qrcode/edit/{qrId}", "QR Codes"),
            ("/analytics", "Analytics"),
            ("/restaurant", "Restaurant"),
            ("/users", "Team"),
            ("/users/invite", "Team"),
        };
        foreach (var (url, section) in expected)
        {
            await GotoInteractiveAsync(url);
            await Assertions.Expect(ActiveTopLink).ToHaveCountAsync(1);
            await Assertions.Expect(ActiveTopLink).ToHaveTextAsync(section);
        }

        // Identity pages (static) have the bar too, without an active entry
        await Page.GotoAsync("/Account/Manage");
        await Assertions.Expect(TopNav).ToBeVisibleAsync();
        await Assertions.Expect(ActiveTopLink).ToHaveCountAsync(0);

        // Client-side navigation moves the mark
        await GotoInteractiveAsync("/menus");
        await TopNav.GetByRole(AriaRole.Link, new() { Name = "QR Codes", Exact = true }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
        await Assertions.Expect(ActiveTopLink).ToHaveTextAsync("QR Codes");
        await TopNav.GetByRole(AriaRole.Link, new() { Name = "Dashboard", Exact = true }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/dashboard$"));
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Welcome back" })).ToBeVisibleAsync();
        await Assertions.Expect(ActiveTopLink).ToHaveTextAsync("Dashboard");

        // Same entries as before the redesign, in the brief's order
        await Assertions.Expect(TopNav.Locator("a")).ToHaveTextAsync(new[] { "Dashboard", "Menus", "QR Codes", "Analytics", "Restaurant", "Team" });
    }

    // --- Menu context bar ---

    [Fact]
    public async Task MenuContextBar_TabsNavigate_AndMarkTheCurrentPage()
    {
        var ownerId = await RegisterOwnerAsync();
        var menuId = await SeedMenuAsync(ownerId, "Reiterkarte");
        var categoryId = await FirstCategoryAsync(ownerId, menuId);

        // Only on the pages of one menu
        foreach (var url in new[] { "/dashboard", "/menus", "/menu/edit", "/qrcodes" })
        {
            await GotoInteractiveAsync(url);
            await Assertions.Expect(Context).ToHaveCountAsync(0);
        }
        await GotoInteractiveAsync($"/menudetail/{Guid.NewGuid()}");
        await Assertions.Expect(Page.GetByTestId("menu-not-found")).ToBeVisibleAsync();
        await Assertions.Expect(Context).ToHaveCountAsync(0);

        await GotoInteractiveAsync($"/menudetail/{menuId}");
        await Assertions.Expect(Context.GetByTestId("menu-context-name")).ToHaveTextAsync("Reiterkarte");
        await Assertions.Expect(Context.GetByRole(AriaRole.Link, new() { Name = "Menus", Exact = true })).ToHaveAttributeAsync("href", "/menus");
        await Assertions.Expect(Tabs.Locator("a")).ToHaveTextAsync(new[] { "Overview", "Categories", "Items", "Allergens", "Combos", "Settings" });
        await Assertions.Expect(ActiveTab).ToHaveTextAsync("Overview");

        // Every tab navigates within the circuit and becomes the current one; the bar stays
        var tabs = new (string Name, string Url, string Heading)[]
        {
            ("Categories", $"/Categories/{menuId}", "Categories"),
            ("Items", $"/MenuItems/Category/{categoryId}", "Reiterkarte Kategorie"),
            ("Allergens", $"/allergens/{menuId}", "Allergens"),
            ("Combos", $"/combos/{menuId}", "Combos"),
            ("Settings", $"/menu/edit/{menuId}", "Menu Settings"),
            ("Overview", $"/menudetail/{menuId}", "Reiterkarte"),
        };
        foreach (var (name, url, heading) in tabs)
        {
            await Tab(name).ClickAsync();
            await Assertions.Expect(Page).ToHaveURLAsync(new Regex(Regex.Escape(url) + "$", RegexOptions.IgnoreCase));
            await Assertions.Expect(Page.Locator("main h1")).ToContainTextAsync(heading);
            await Assertions.Expect(ActiveTab).ToHaveTextAsync(name);
            await Assertions.Expect(Context.GetByTestId("menu-context-name")).ToHaveTextAsync("Reiterkarte");
        }

        // The tree's former targets are all here: the tabs' hrefs (Items through the redirect to the first category)
        await Assertions.Expect(Tab("Items")).ToHaveAttributeAsync("href", $"/MenuItems/{menuId}");
        await Assertions.Expect(Tab("Settings")).ToHaveAttributeAsync("href", $"/menu/edit/{menuId}");

        // No second breadcrumb on the pages
        foreach (var url in new[] { $"/menudetail/{menuId}", $"/Categories/{menuId}", $"/MenuItems/Category/{categoryId}", $"/allergens/{menuId}", $"/combos/{menuId}", $"/menu/edit/{menuId}" })
        {
            await GotoInteractiveAsync(url);
            await Assertions.Expect(Page.Locator("nav[aria-label='Breadcrumb' i]")).ToHaveCountAsync(1);
            await Assertions.Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Preview" })).ToHaveCountAsync(1);
        }
    }

    [Fact]
    public async Task MenuSwitcher_ListsTheMenus_AndKeepsTheTab()
    {
        var ownerId = await RegisterOwnerAsync();
        var first = await SeedMenuAsync(ownerId, "Alpha Karte");
        var second = await SeedMenuAsync(ownerId, "Beta Karte");
        var secondCategory = await FirstCategoryAsync(ownerId, second);

        await GotoInteractiveAsync($"/allergens/{first}");
        await Assertions.Expect(SwitcherButton).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(Switcher).ToBeHiddenAsync();
        await SwitcherButton.ClickAsync();
        await Assertions.Expect(SwitcherButton).ToHaveAttributeAsync("aria-expanded", "true");
        var items = Switcher.GetByTestId("menu-switcher-item");
        await Assertions.Expect(items).ToHaveTextAsync(new[] { "Alpha Karte", "Beta Karte" });
        await Assertions.Expect(items.Nth(0)).ToHaveAttributeAsync("aria-current", "page");
        await Assertions.Expect(items.Nth(1)).ToHaveAttributeAsync("href", $"/allergens/{second}");
        await Assertions.Expect(Switcher.GetByRole(AriaRole.Link, new() { Name = "All menus" })).ToHaveAttributeAsync("href", "/menus");

        // Escape closes it and gives the focus back to the button
        await Page.Keyboard.PressAsync("ArrowDown");
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(Switcher).ToBeHiddenAsync();
        await Assertions.Expect(SwitcherButton).ToHaveAttributeAsync("aria-expanded", "false");
        (await FocusedTestIdAsync()).Should().Be("menu-switcher-button");

        // A click outside closes it too (the page header's corner: the open list hangs over the title since AP11's icon tile)
        await SwitcherButton.ClickAsync();
        await Assertions.Expect(Switcher).ToBeVisibleAsync();
        await Page.GetByTestId("page-header").ClickAsync(new() { Position = new() { X = 6, Y = 6 } });
        await Assertions.Expect(Switcher).ToBeHiddenAsync();

        // Switching keeps the tab
        await SwitcherButton.ClickAsync();
        await items.Nth(1).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/allergens/{second}$"));
        await Assertions.Expect(Context.GetByTestId("menu-context-name")).ToHaveTextAsync("Beta Karte");
        await Assertions.Expect(ActiveTab).ToHaveTextAsync("Allergens");
        await Assertions.Expect(Switcher).ToBeHiddenAsync();

        // Items of one category → the other menu's items (its first category)
        await GotoInteractiveAsync($"/MenuItems/Category/{await FirstCategoryAsync(ownerId, first)}");
        await SwitcherButton.ClickAsync();
        await Switcher.GetByRole(AriaRole.Link, new() { Name = "Beta Karte" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/MenuItems/Category/{secondCategory}$", RegexOptions.IgnoreCase));
        await Assertions.Expect(ActiveTab).ToHaveTextAsync("Items");

        // A menu created meanwhile shows up after the next navigation
        await SeedMenuAsync(ownerId, "Gamma Karte");
        await Tab("Overview").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menudetail/{second}$"));
        await SwitcherButton.ClickAsync();
        await Assertions.Expect(items).ToHaveTextAsync(new[] { "Alpha Karte", "Beta Karte", "Gamma Karte" });
    }

    [Fact]
    public async Task PreviewLink_OpensTheGuestPreviewInANewTab()
    {
        var ownerId = await RegisterOwnerAsync();
        var menuId = await SeedMenuAsync(ownerId, "Vorschaukarte");

        await GotoInteractiveAsync($"/Categories/{menuId}");
        var preview = Context.GetByTestId("menu-preview-link");
        await Assertions.Expect(preview).ToHaveAttributeAsync("href", $"/menu/preview/{menuId}");
        await Assertions.Expect(preview).ToHaveAttributeAsync("target", "_blank");
        await Assertions.Expect(preview).ToHaveAttributeAsync("rel", new Regex("noopener"));

        var popup = await Page.RunAndWaitForPopupAsync(() => preview.ClickAsync());
        await WaitForGuestReadyAsync(popup);
        await Assertions.Expect(popup).ToHaveURLAsync(new Regex($"/menu/preview/{menuId}$"));
        await Assertions.Expect(popup.GetByTestId("menu-display")).ToBeVisibleAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/Categories/{menuId}$"));
    }

    // --- User menu ---

    [Fact]
    public async Task UserMenu_ShowsNameAndRole_LinksToTheSettings_AndLogsOut()
    {
        var email = await RegisterNewOwnerAsync();
        await GotoInteractiveAsync("/qrcodes");

        await Assertions.Expect(UserButton).ToContainTextAsync(email);
        await Assertions.Expect(UserButton).ToContainTextAsync("Owner");
        await Assertions.Expect(UserButton).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(UserMenu).ToBeHiddenAsync();

        await UserButton.ClickAsync();
        await Assertions.Expect(UserButton).ToHaveAttributeAsync("aria-expanded", "true");
        await Assertions.Expect(UserMenu).ToBeVisibleAsync();
        await Assertions.Expect(UserMenu).ToContainTextAsync(email);
        await Assertions.Expect(UserMenu).ToContainTextAsync("Owner");
        await Assertions.Expect(UserMenu.GetByRole(AriaRole.Link, new() { Name = "Account settings" })).ToHaveAttributeAsync("href", "Account/Manage");

        // Clicking the button again closes it
        await UserButton.ClickAsync();
        await Assertions.Expect(UserMenu).ToBeHiddenAsync();

        // Account settings (a static Identity page): the user menu works there as well
        await UserButton.ClickAsync();
        await UserMenu.GetByRole(AriaRole.Link, new() { Name = "Account settings" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/Account/Manage$"));
        await Assertions.Expect(UserMenu).ToBeHiddenAsync();
        await UserButton.ClickAsync();
        await Assertions.Expect(UserMenu).ToBeVisibleAsync();

        // Log out: the anti-forgery form, back on the page (which asks for a login)
        await UserMenu.GetByRole(AriaRole.Button, new() { Name = "Log out" }).ClickAsync();
        await Assertions.Expect(Page.GetByTestId("anonymous-nav")).ToBeVisibleAsync();
        await Assertions.Expect(UserButton).ToHaveCountAsync(0);
        await Page.GotoAsync("/menus");
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/Account/Login", RegexOptions.IgnoreCase));
    }

    // --- Keyboard ---

    [Fact]
    public async Task Keyboard_TabReachesTheNav_EscapeClosesTheUserMenu_SkipLinkFocusesTheContent()
    {
        await RegisterNewOwnerAsync();

        // Tab order of the chrome (on a static Identity page, so the user menu is shown to work without a circuit):
        // skip link first, then brand and navigation. Blazor's FocusOnNavigate puts the focus on the h1 after loading,
        // so start from the brand link and step back once.
        await Page.GotoAsync("/Account/Manage");
        await Page.GetByTestId("brand-link").FocusAsync();
        await Page.Keyboard.PressAsync("Shift+Tab");
        await Assertions.Expect(Page.Locator(".sd-skip-link")).ToBeFocusedAsync();
        await Assertions.Expect(Page.Locator(".sd-skip-link")).ToBeInViewportAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(Page.GetByTestId("brand-link")).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(TopNav.GetByRole(AriaRole.Link, new() { Name = "Dashboard" })).ToBeFocusedAsync();
        for (var i = 0; i < 6; i++) await Page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(UserButton).ToBeFocusedAsync();

        // Enter opens, the arrow keys walk the entries, Escape closes and returns the focus
        await Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(UserMenu).ToBeVisibleAsync();
        await Page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(UserMenu.GetByRole(AriaRole.Link, new() { Name = "Account settings" })).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(UserMenu.GetByRole(AriaRole.Button, new() { Name = "Log out" })).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(UserMenu).ToBeHiddenAsync();
        await Assertions.Expect(UserButton).ToBeFocusedAsync();
        await Assertions.Expect(UserButton).ToHaveAttributeAsync("aria-expanded", "false");

        // ArrowDown on the closed button opens it at the first entry; Tab out of it closes it
        await Page.Keyboard.PressAsync("ArrowDown");
        await Assertions.Expect(UserMenu).ToBeVisibleAsync();
        await Assertions.Expect(UserMenu.GetByRole(AriaRole.Link, new() { Name = "Account settings" })).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Tab");
        await Page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(UserMenu).ToBeHiddenAsync();

        // The skip link focuses <main> and stays on the page
        await Page.GotoAsync("/Account/Manage");
        await Page.Locator(".sd-skip-link").FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        (await Page.EvaluateAsync<string>("() => document.activeElement.id")).Should().Be("sd-main");
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/Account/Manage$"));

        // Interactive pages put the focus on the heading after navigating (FocusOnNavigate); the chrome is right before it
        await GotoInteractiveAsync("/menus");
        await Assertions.Expect(Page.Locator("main h1")).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Shift+Tab");
        await Assertions.Expect(UserButton).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Shift+Tab");
        await Assertions.Expect(TopNav.GetByRole(AriaRole.Link, new() { Name = "Team" })).ToBeFocusedAsync();

        // … and the skip link is not taken for a navigation by Blazor's router (<base href> would make it "/#sd-main")
        await Page.Locator(".sd-skip-link").FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        (await Page.EvaluateAsync<string>("() => document.activeElement.id")).Should().Be("sd-main");
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/menus$"));
    }

    // --- Phone ---

    [Fact]
    public async Task PhoneDrawer_OpensAndClosesOnNavigationEscapeAndBackdrop_WithTheFocus()
    {
        await RegisterNewOwnerAsync();
        await Page.SetViewportSizeAsync(390, 844);
        await GotoInteractiveAsync("/menus");

        await Assertions.Expect(TopNav).ToBeHiddenAsync();
        await Assertions.Expect(UserButton).ToBeHiddenAsync();
        await Assertions.Expect(DrawerButton).ToBeVisibleAsync();
        await Assertions.Expect(DrawerButton).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(DrawerButton).ToHaveAttributeAsync("aria-label", "Open navigation");

        // Open: focus on the current page's entry, the user section is there
        await DrawerButton.ClickAsync();
        await Assertions.Expect(OpenDrawer).ToHaveCountAsync(1);
        await Assertions.Expect(DrawerButton).ToHaveAttributeAsync("aria-expanded", "true");
        var drawer = Page.GetByTestId("nav-drawer");
        await Assertions.Expect(drawer.Locator("a[aria-current='page']")).ToBeFocusedAsync();
        await Assertions.Expect(drawer.Locator("a[aria-current='page']")).ToHaveTextAsync("Menus");
        await Assertions.Expect(drawer.GetByTestId("drawer-user")).ToContainTextAsync("Owner");
        await Assertions.Expect(drawer.GetByRole(AriaRole.Button, new() { Name = "Log out" })).ToBeVisibleAsync();

        // Escape
        await Page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(OpenDrawer).ToHaveCountAsync(0);
        await Assertions.Expect(DrawerButton).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(DrawerButton).ToBeFocusedAsync();

        // Close button
        await DrawerButton.ClickAsync();
        await Page.GetByTestId("nav-drawer-close").ClickAsync();
        await Assertions.Expect(OpenDrawer).ToHaveCountAsync(0);
        await Assertions.Expect(DrawerButton).ToBeFocusedAsync();

        // Backdrop (right of the 320 px panel)
        await DrawerButton.ClickAsync();
        await Assertions.Expect(OpenDrawer).ToHaveCountAsync(1);
        await Page.Mouse.ClickAsync(370, 500);
        await Assertions.Expect(OpenDrawer).ToHaveCountAsync(0);
        await Assertions.Expect(DrawerButton).ToHaveAttributeAsync("aria-expanded", "false");

        // A click inside the panel that is not a link keeps it open
        await DrawerButton.ClickAsync();
        await drawer.GetByTestId("drawer-user").Locator(".sd-drawer-user-id").ClickAsync();
        await Assertions.Expect(OpenDrawer).ToHaveCountAsync(1);

        // Following a link navigates and closes it
        await drawer.GetByRole(AriaRole.Link, new() { Name = "QR Codes", Exact = true }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
        await Assertions.Expect(OpenDrawer).ToHaveCountAsync(0);
        (await Page.EvaluateAsync<bool>("() => document.documentElement.classList.contains('sd-drawer-open')")).Should().BeFalse();

        // Widening the window closes an open drawer
        await DrawerButton.ClickAsync();
        await Assertions.Expect(OpenDrawer).ToHaveCountAsync(1);
        await Page.SetViewportSizeAsync(1280, 900);
        await Assertions.Expect(OpenDrawer).ToHaveCountAsync(0);
        await Assertions.Expect(TopNav).ToBeVisibleAsync();
    }

    [Theory]
    [InlineData(360)]
    [InlineData(390)]
    public async Task PhoneContextStrip_ScrollsSideways_WithoutPageOverflow(int width)
    {
        var ownerId = await RegisterOwnerAsync();
        var menuId = await SeedMenuAsync(ownerId, "Eine ziemlich lange Speisekarte für den Sommer");
        await Page.SetViewportSizeAsync(width, 800);

        await GotoInteractiveAsync($"/combos/{menuId}");
        await Assertions.Expect(ActiveTab).ToHaveTextAsync("Combos");

        var metrics = await Tabs.EvaluateAsync<double[]>("""
            strip => {
                const tab = strip.querySelector('[aria-current="page"]').getBoundingClientRect();
                return [strip.scrollWidth, strip.clientWidth, strip.scrollLeft, tab.left, tab.right,
                        document.documentElement.scrollWidth, window.innerWidth];
            }
            """);
        var (scrollWidth, clientWidth, scrollLeft, tabLeft, tabRight, pageWidth, viewport) =
            (metrics[0], metrics[1], metrics[2], metrics[3], metrics[4], metrics[5], metrics[6]);
        scrollWidth.Should().BeGreaterThan(clientWidth, "six tabs do not fit a phone: the strip scrolls");
        scrollLeft.Should().BeGreaterThan(0, "the active tab was scrolled into view");
        tabLeft.Should().BeGreaterThanOrEqualTo(0);
        tabRight.Should().BeLessThanOrEqualTo(viewport);
        pageWidth.Should().BeLessThanOrEqualTo(viewport, "the page itself never scrolls sideways");

        // The long name is cut, the preview stays reachable
        await Assertions.Expect(Context.GetByTestId("menu-preview-link")).ToBeInViewportAsync();
        var head = await Page.Locator(".sd-ctx-head").BoundingBoxAsync();
        ((double)head!.Width).Should().BeLessThanOrEqualTo(viewport);

        // The strip scrolls by itself, the page does not
        await Tabs.EvaluateAsync("strip => { strip.scrollLeft = 0; }");
        await Assertions.Expect(Tab("Overview")).ToBeInViewportAsync();
        (await Page.EvaluateAsync<double>("() => window.scrollX")).Should().Be(0);
    }

    // --- Anonymous ---

    [Fact]
    public async Task AnonymousHeader_OnTheLoginPage()
    {
        await Page.GotoAsync("/Account/Login");

        var nav = Page.GetByTestId("anonymous-nav");
        await Assertions.Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Log in" })).ToHaveAttributeAsync("href", "Account/Login");
        await Assertions.Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Register" })).ToHaveAttributeAsync("href", "Account/Register");
        await Assertions.Expect(Page.GetByTestId("brand-link")).ToBeVisibleAsync();
        // Anonymous: the brand leads to the landing page (logged in: to the dashboard)
        await Assertions.Expect(Page.GetByTestId("brand-link")).ToHaveAttributeAsync("href", "/");
        await Assertions.Expect(Page.GetByTestId("brand-link").Locator("img")).ToHaveAttributeAsync("src", "/images/brand/logo-inverse.svg");
        await Assertions.Expect(TopNav).ToHaveCountAsync(0);
        await Assertions.Expect(UserButton).ToHaveCountAsync(0);
        await Assertions.Expect(DrawerButton).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("dialog[data-testid='nav-drawer']")).ToHaveCountAsync(0);

        // Same on a phone: both links stay in the bar
        await Page.SetViewportSizeAsync(360, 740);
        await Assertions.Expect(nav.GetByRole(AriaRole.Link, new() { Name = "Register" })).ToBeInViewportAsync();
        (await Page.EvaluateAsync<bool>("() => document.documentElement.scrollWidth <= window.innerWidth")).Should().BeTrue();

        await nav.GetByRole(AriaRole.Link, new() { Name = "Register" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/Account/Register$"));
    }

    // --- Logo and favicons ---

    [Fact]
    public async Task FaviconsAndLogos_AreServed_AndAreValidImages()
    {
        // Both documents link the same favicon set
        await Page.GotoAsync("/Account/Login");
        await ExpectIconLinksAsync(Page);
        var (guestContext, guest) = await NewContextAsync();
        await using (guestContext)
        {
            await OpenMenuAsync(guest, E2ESeedData.RamenMenuId);
            await ExpectIconLinksAsync(guest);
        }

        foreach (var (url, size) in new[] { ("/favicon-32.png", 32), ("/apple-touch-icon.png", 180) })
        {
            var response = await Page.APIRequest.GetAsync(url);
            response.Status.Should().Be(200, url);
            response.Headers["content-type"].Should().Be("image/png");
            using var image = SixLabors.ImageSharp.Image.Load(await response.BodyAsync());
            image.Width.Should().Be(size, url);
            image.Height.Should().Be(size, url);
        }

        foreach (var url in new[] { "/favicon.svg", "/images/brand/logo.svg", "/images/brand/logo-inverse.svg", "/images/brand/logo-mark.svg", "/images/brand/logo-mark-inverse.svg" })
        {
            var response = await Page.APIRequest.GetAsync(url);
            response.Status.Should().Be(200, url);
            response.Headers["content-type"].Should().StartWith("image/svg+xml");
            var svg = XDocument.Parse(await response.TextAsync()).Root!;
            svg.Name.LocalName.Should().Be("svg", url);
            svg.Attribute("viewBox").Should().NotBeNull(url);

            // The browser decodes it
            var width = await Page.EvaluateAsync<int>("""
                async url => {
                    const img = new Image();
                    img.src = url;
                    await img.decode();
                    return img.naturalWidth;
                }
                """, url);
            width.Should().BeGreaterThan(0, url);
        }

        // The corrupt bitmaps are gone
        foreach (var url in new[] { "/favicon.png", "/image/speise-direkt.jpg", "/image/speise-direkt.png", "/image/speise-direkt2.png" })
            (await Page.APIRequest.GetAsync(url)).Status.Should().Be(404, url);
    }

    private static async Task OpenMenuAsync(IPage page, Guid menuId) => await OpenGuestPageAsync(page, $"/menu/{menuId}");

    private static async Task ExpectIconLinksAsync(IPage page)
    {
        await Assertions.Expect(page.Locator("link[rel='icon'][type='image/svg+xml']")).ToHaveAttributeAsync("href", new Regex("^/?favicon\\.svg$"));
        await Assertions.Expect(page.Locator("link[rel='icon'][type='image/png']")).ToHaveAttributeAsync("href", new Regex("^/?favicon-32\\.png$"));
        await Assertions.Expect(page.Locator("link[rel='apple-touch-icon']")).ToHaveAttributeAsync("href", new Regex("^/?apple-touch-icon\\.png$"));
    }
}
