using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Not a test in the usual sense: walks a seeded tenant through every back-office page and stores
/// full-page screenshots (desktop 1280 px and phone 390 px) for design reviews, plus the QR code calendar editor
/// (<see cref="ShootCalendarEditorAsync"/>) at both widths. Runs only when
/// <c>E2E_SCREENSHOT_DIR</c> points at a directory; set <c>E2E_ALLOW_EXTERNAL_REQUESTS=1</c> as well so
/// Tailwind and the icon font come from their CDNs and the pages look like production.
/// <code>
/// E2E_SCREENSHOT_DIR=/tmp/shots E2E_ALLOW_EXTERNAL_REQUESTS=1 dotnet test SpeiseDirekt3.E2ETests --filter FullyQualifiedName~AdminScreenshotTour
/// </code>
/// The browser language is <c>E2E_LOCALE</c> (default <c>en-US</c>; <c>de-DE</c> for the German back office — use another
/// <c>E2E_SCREENSHOT_DIR</c> per language, the file names are the same). Next to every full-page shot a <c>.txt</c> holds
/// the page's visible text and its title/aria-label/placeholder/alt texts, to look for words left in the other language.
/// </summary>
public class AdminScreenshotTour : QrCalendarTestBase
{
    public const string DirectoryEnvVar = "E2E_SCREENSHOT_DIR";

    public AdminScreenshotTour(E2EFixture fixture) : base(fixture) { }

    protected override string ContextLocale => E2ESettings.TourLocale;

    private static string? Directory => Environment.GetEnvironmentVariable(DirectoryEnvVar) is { Length: > 0 } d ? d : null;

    private sealed record Seeded(Guid MenuId, Guid CategoryId, Guid EmptyMenuId, Guid TimeTableQrId, Guid CalendarQrId, Guid TenantUserId,
        Guid DrinksCategoryId, Guid HolidayQrId, int HolidayYear, string ManagerEmail);

    [Fact]
    public async Task Capture_every_back_office_page()
    {
        var dir = Directory;
        if (dir is null) return; // opt-in only
        System.IO.Directory.CreateDirectory(dir);

        var email = await RegisterNewOwnerAsync();
        Guid ownerId;
        await using (var db = App.CreateDbContext(Guid.Empty))
            ownerId = Guid.Parse((await db.Users.SingleAsync(u => u.Email == email)).Id);
        var seeded = await SeedTenantAsync(ownerId);

        // Desktop
        await ShootAsync(Page, dir, "desktop", seeded);
        await ShootCalendarEditorAsync(Page, dir, "desktop", seeded);

        // Phone: same cookies, narrow viewport
        var state = await Context.StorageStateAsync();
        await using var phoneContext = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress,
            Locale = ContextLocale,
            StorageState = state,
            ViewportSize = new ViewportSize { Width = 390, Height = 844 },
            DeviceScaleFactor = 2,
            IsMobile = true,
            HasTouch = true
        });
        phoneContext.SetDefaultTimeout(15_000);
        var phone = await phoneContext.NewPageAsync();
        await ShootAsync(phone, dir, "phone", seeded, phoneSubset: true);
        await ShootCalendarEditorAsync(phone, dir, "phone", seeded);

        // Guest view of the same menu in every design theme (phone)
        foreach (var theme in Enum.GetValues<DesignTheme>())
        {
            await App.SeedAsync(ownerId, async db =>
                (await db.Menus.SingleAsync(m => m.Id == seeded.MenuId)).Theme = theme);
            await OpenGuestPageAsync(phone, $"/menu/{seeded.MenuId}");
            await SettleAsync(phone);
            await phone.ScreenshotAsync(new() { Path = Path.Combine(dir, $"guest-{theme.ToString().ToLowerInvariant()}.png"), FullPage = true });
        }

        // No page may scroll sideways on a phone: every back-office page at 360 and 390 px, written to phone-overflow.txt
        await CheckPhoneOverflowAsync(state, dir, seeded);

        // Anonymous visitors: the Identity pages with the anonymous header, desktop and phone
        foreach (var (prefix, width, height) in new[] { ("desktop", 1280, 900), ("phone", 390, 844) })
        {
            await using var anonymous = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
            {
                BaseURL = App.ServerAddress,
                Locale = ContextLocale,
                ViewportSize = new ViewportSize { Width = width, Height = height },
                DeviceScaleFactor = prefix == "phone" ? 2 : 1
            });
            var anon = await anonymous.NewPageAsync();
            foreach (var (name, url) in new[] { ("account-login", "/Account/Login"), ("account-register", "/Account/Register") })
            {
                await anon.GotoAsync(url);
                await SettleAsync(anon);
                await anon.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{prefix}-{name}.png"), FullPage = true });
            }
        }

        // Empty tenant (first-run experience)
        var (emptyContext, emptyPage) = await NewContextAsync();
        await using (emptyContext)
        {
            await RegisterAsync(emptyPage, UniqueEmail("empty"));
            foreach (var (name, url) in new[] { ("menus", "/menus"), ("qrcodes", "/qrcodes"), ("analytics", "/analytics"), ("users", "/users"), ("home", "/dashboard") })
            {
                await GotoInteractiveAsync(emptyPage, url);
                await SettleAsync(emptyPage);
                await emptyPage.ScreenshotAsync(new() { Path = Path.Combine(dir, $"desktop-empty-{name}.png"), FullPage = true });
            }
        }
    }

    /// <summary>
    /// The final review of AP13: every back-office page, the account settings and the anonymous account pages at 360,
    /// 390, 768, 1024, 1280 and 1440 px (or <c>E2E_REVIEW_WIDTHS</c>, comma-separated), full page at device scale 1, into
    /// <c>$E2E_SCREENSHOT_DIR/review/{width}/{name}.png</c>, and <c>review/overflow.txt</c>: every page whose document,
    /// or a visible element, is wider than the viewport ("none" when all fit). Opt-in like the other facts; run it per
    /// language with <c>E2E_LOCALE</c> and <c>E2E_ALLOW_EXTERNAL_REQUESTS=1</c> (Tailwind from the CDN).
    /// </summary>
    [Fact]
    public async Task Capture_review_widths()
    {
        var dir = Directory;
        if (dir is null) return; // opt-in only
        var widths = (Environment.GetEnvironmentVariable("E2E_REVIEW_WIDTHS") is { Length: > 0 } w ? w : "360,390,768,1024,1280,1440")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(int.Parse).ToArray();
        var root = Path.Combine(dir, "review");
        System.IO.Directory.CreateDirectory(root);

        var email = await RegisterNewOwnerAsync();
        Guid ownerId;
        await using (var db = App.CreateDbContext(Guid.Empty))
            ownerId = Guid.Parse((await db.Users.SingleAsync(u => u.Email == email)).Id);
        var s = await SeedTenantAsync(ownerId);
        var state = await Context.StorageStateAsync();

        var pages = new (string Name, string Url)[]
        {
            ("dashboard", "/dashboard"), ("menus", "/menus"), ("menudetail", $"/menudetail/{s.MenuId}"),
            ("categories", $"/Categories/{s.MenuId}"), ("items", $"/MenuItems/Category/{s.CategoryId}"),
            ("items-drinks", $"/MenuItems/Category/{s.DrinksCategoryId}"), ("item-new", $"/MenuItems/Category/{s.CategoryId}/new"),
            ("allergens", $"/allergens/{s.MenuId}"), ("combos", $"/combos/{s.MenuId}"), ("menu-edit", $"/menu/edit/{s.MenuId}"),
            ("menu-new", "/menu/edit"), ("menudetail-empty", $"/menudetail/{s.EmptyMenuId}"), ("qrcodes", "/qrcodes"),
            ("qrcode-timetable", $"/qrcode/edit/{s.TimeTableQrId}"), ("qrcode-calendar", $"/qrcode/edit/{s.HolidayQrId}"),
            ("qrcode-new", "/qrcode/edit"), ("analytics", "/analytics"), ("restaurant", "/restaurant"), ("users", "/users"),
            ("users-invite", "/users/invite"), ("users-edit", $"/users/edit/{s.TenantUserId}"), ("manage", "/Account/Manage"),
            ("manage-email", "/Account/Manage/Email"), ("manage-password", "/Account/Manage/ChangePassword"),
            ("manage-2fa", "/Account/Manage/TwoFactorAuthentication"), ("manage-authenticator", "/Account/Manage/EnableAuthenticator"),
            ("manage-data", "/Account/Manage/PersonalData")
        };
        var anonymousPages = new (string Name, string Url)[]
        {
            ("login", "/Account/Login"), ("register", "/Account/Register"), ("forgot", "/Account/ForgotPassword"),
            ("reset", "/Account/ResetPassword?code=YWJj"), ("lockout", "/Account/Lockout"), ("access-denied", "/Account/AccessDenied")
        };

        const string WideElements = """
            width => [...document.querySelectorAll('body *')]
                .filter(e => e.checkVisibility() && e.getBoundingClientRect().right > width + 1
                    && !e.closest('[data-sd-tab-strip], .fc-scroller, .overflow-x-auto'))
                .slice(0, 5).map(e => e.tagName.toLowerCase() + '.' + String(e.className).split(' ').slice(0, 3).join('.'))
            """;
        var findings = new List<string>();
        foreach (var width in widths)
        {
            var folder = Path.Combine(root, width.ToString(System.Globalization.CultureInfo.InvariantCulture));
            System.IO.Directory.CreateDirectory(folder);
            foreach (var signedIn in new[] { true, false })
            {
                await using var context = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
                {
                    BaseURL = App.ServerAddress,
                    Locale = ContextLocale,
                    StorageState = signedIn ? state : null,
                    ViewportSize = new ViewportSize { Width = width, Height = 900 },
                    IsMobile = width < 768,
                    HasTouch = width < 1024
                });
                context.SetDefaultTimeout(15_000);
                var page = await context.NewPageAsync();
                foreach (var (name, url) in signedIn ? pages : anonymousPages)
                {
                    if (url.StartsWith("/Account")) await page.GotoAsync(url);
                    else await GotoInteractiveAsync(page, url);
                    await SettleAsync(page);
                    var overflow = await page.EvaluateAsync<int>("width => document.documentElement.scrollWidth - width", width);
                    var wide = await page.EvaluateAsync<string[]>(WideElements, width);
                    if (overflow > 0 || wide.Length > 0)
                        findings.Add($"{width}px {url}: document +{overflow}px; {string.Join(", ", wide)}");
                    await page.ScreenshotAsync(new() { Path = Path.Combine(folder, $"{name}.png"), FullPage = true });
                }
            }
        }
        await File.WriteAllLinesAsync(Path.Combine(root, "overflow.txt"), findings.Count == 0 ? ["none"] : findings);
    }

    /// <summary>
    /// AP14, the team (owner and managers): as the owner the team list, the invitation, a member's page and the removal
    /// confirmation; as the manager "Max Kellner" the dashboard with its navigation (top bar and user menu from 1024 px,
    /// the drawer below) and the access-denied page of <c>/users</c>. At 1280 and 390 px into
    /// <c>$E2E_SCREENSHOT_DIR/team/{width}-{name}.png</c> (+ <c>.txt</c> with the page's texts); per language with
    /// <c>E2E_LOCALE</c>.
    /// </summary>
    [Fact]
    public async Task Capture_team_pages()
    {
        var dir = Directory;
        if (dir is null) return; // opt-in only
        var root = Path.Combine(dir, "team");
        System.IO.Directory.CreateDirectory(root);

        var email = await RegisterNewOwnerAsync();
        var ownerId = await OwnerIdAsync(email);
        var s = await SeedTenantAsync(ownerId);
        var ownerState = await Context.StorageStateAsync();

        foreach (var (width, height) in new[] { (1280, 900), (390, 844) })
        {
            BrowserNewContextOptions Options(string? state) => new()
            {
                BaseURL = App.ServerAddress,
                Locale = ContextLocale,
                StorageState = state,
                ViewportSize = new ViewportSize { Width = width, Height = height },
                DeviceScaleFactor = width < 768 ? 2 : 1,
                IsMobile = width < 768,
                HasTouch = width < 1024
            };
            async Task Shot(IPage page, string name, bool fullPage = true)
            {
                await SettleAsync(page);
                var file = Path.Combine(root, $"{width}-{name}");
                await page.ScreenshotAsync(new() { Path = file + ".png", FullPage = fullPage });
                await File.WriteAllTextAsync(file + ".txt", await PageTextsAsync(page));
            }

            // The owner
            await using (var ownerContext = await Fixture.NewBrowserContextAsync(Options(ownerState)))
            {
                ownerContext.SetDefaultTimeout(15_000);
                var owner = await ownerContext.NewPageAsync();
                await GotoInteractiveAsync(owner, "/users");
                await Shot(owner, "owner-list");
                await GotoInteractiveAsync(owner, "/users/invite");
                await Shot(owner, "owner-invite");
                await GotoInteractiveAsync(owner, $"/users/edit/{s.TenantUserId}");
                await Shot(owner, "owner-edit");
                await owner.GetByTestId("member-remove").ClickAsync();
                await Assertions.Expect(owner.GetByTestId("confirmation")).ToBeVisibleAsync();
                await Shot(owner, "owner-remove-confirm", fullPage: false);
                await owner.Locator("#cancelBtn").ClickAsync();
                await GotoInteractiveAsync(owner, "/dashboard");
                await Shot(owner, "owner-dashboard");
            }

            // The manager
            await using (var managerContext = await Fixture.NewBrowserContextAsync(Options(null)))
            {
                managerContext.SetDefaultTimeout(15_000);
                var manager = await managerContext.NewPageAsync();
                await LoginAsync(manager, s.ManagerEmail);
                await GotoInteractiveAsync(manager, "/dashboard");
                await Shot(manager, "manager-dashboard");
                if (width >= 1024)
                {
                    await manager.GetByTestId("user-menu-button").ClickAsync();
                    await Shot(manager, "manager-user-menu", fullPage: false);
                }
                else
                {
                    await manager.GetByTestId("nav-drawer-button").ClickAsync();
                    await Assertions.Expect(manager.GetByTestId("nav-drawer")).ToBeVisibleAsync();
                    await Shot(manager, "manager-drawer", fullPage: false);
                }
                await manager.GotoAsync("/users");
                await Shot(manager, "manager-users-denied");
            }
        }
    }

    /// <summary>
    /// Loads every back-office page at 360 and 390 px (Tailwind from the CDN, as in production) and writes the pages whose
    /// document is wider than the viewport to <c>phone-overflow.txt</c> ("none" when all fit).
    /// </summary>
    private async Task CheckPhoneOverflowAsync(string storageState, string dir, Seeded s)
    {
        var urls = new[]
        {
            "/dashboard", "/menus", $"/menudetail/{s.MenuId}", $"/Categories/{s.MenuId}", $"/MenuItems/Category/{s.CategoryId}",
            $"/MenuItems/Category/{s.DrinksCategoryId}", $"/allergens/{s.MenuId}", $"/combos/{s.MenuId}", $"/menu/edit/{s.MenuId}",
            "/menu/edit", $"/menudetail/{s.EmptyMenuId}", $"/MenuItems/{s.EmptyMenuId}", "/qrcodes", $"/qrcode/edit/{s.TimeTableQrId}",
            $"/qrcode/edit/{s.HolidayQrId}", "/qrcode/edit", "/analytics", "/restaurant", "/users", "/users/invite",
            $"/users/edit/{s.TenantUserId}", "/Account/Manage", "/Account/Manage/ChangePassword"
        };
        var findings = new List<string>();
        foreach (var width in new[] { 360, 390 })
        {
            await using var context = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
            {
                BaseURL = App.ServerAddress,
                Locale = ContextLocale,
                StorageState = storageState,
                ViewportSize = new ViewportSize { Width = width, Height = 800 },
                IsMobile = true,
                HasTouch = true
            });
            context.SetDefaultTimeout(15_000);
            var page = await context.NewPageAsync();
            foreach (var url in urls)
            {
                if (url.StartsWith("/Account")) await page.GotoAsync(url);
                else await GotoInteractiveAsync(page, url);
                await SettleAsync(page);
                // Against the configured width, not innerWidth: a mobile viewport grows to fit wide content
                var overflow = await page.EvaluateAsync<int>("width => document.documentElement.scrollWidth - width", width);
                if (overflow > 0) findings.Add($"{width}px {url}: {overflow}px too wide");
            }
        }
        await File.WriteAllLinesAsync(Path.Combine(dir, "phone-overflow.txt"), findings.Count == 0 ? ["none"] : findings);
    }

    private async Task ShootAsync(IPage page, string dir, string prefix, Seeded s, bool phoneSubset = false)
    {
        async Task Shot(string name, string url, Func<IPage, Task>? after = null)
        {
            await GotoInteractiveAsync(page, url);
            if (after is not null) await after(page);
            await SettleAsync(page);
            await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{prefix}-{name}.png"), FullPage = true });
            await File.WriteAllTextAsync(Path.Combine(dir, $"{prefix}-{name}.txt"), await PageTextsAsync(page));
        }

        await Shot("home", "/dashboard");
        await Shot("menus", "/menus");
        await Shot("menudetail", $"/menudetail/{s.MenuId}");
        await Shot("categories", $"/Categories/{s.MenuId}");
        await Shot("menuitems", $"/MenuItems/{s.MenuId}", async p =>
            await Assertions.Expect(p).ToHaveURLAsync(new System.Text.RegularExpressions.Regex("/MenuItems/Category/", System.Text.RegularExpressions.RegexOptions.IgnoreCase)));
        await Shot("qrcodes", "/qrcodes");
        await Shot("qrcode-edit-timetable", $"/qrcode/edit/{s.TimeTableQrId}");
        await Shot("analytics", "/analytics");
        await Shot("menu-edit", $"/menu/edit/{s.MenuId}");
        await Shot("allergens", $"/allergens/{s.MenuId}");
        await Shot("combos", $"/combos/{s.MenuId}");
        await Shot("users", "/users");

        // Guest-menu data model: restaurant profile, an item list with variants/extras, the editor with both
        await Shot("restaurant", "/restaurant");
        await Shot("menuitems-drinks", $"/MenuItems/Category/{s.DrinksCategoryId}");
        // The item edit page (AP11: its own page instead of the modal), full page and at the variants with the save bar
        // Selectors here work in every language (the tour runs with E2E_LOCALE): the item's name links to its page
        await Shot("menuitems-editor-options", $"/MenuItems/Category/{s.DrinksCategoryId}", async p =>
        {
            await p.GetByRole(AriaRole.Link, new() { Name = "Melange", Exact = true }).ClickAsync();
            await Assertions.Expect(p.GetByTestId("item-edit-form")).ToBeVisibleAsync();
        });
        await GotoInteractiveAsync(page, $"/MenuItems/Category/{s.DrinksCategoryId}");
        await page.GetByRole(AriaRole.Link, new() { Name = "Melange", Exact = true }).ClickAsync();
        await page.GetByTestId("price-variants").ScrollIntoViewIfNeededAsync();
        await page.GetByTestId("variant-row").First.Locator("input").First.FillAsync("Klein (0,25 l)");
        await SettleAsync(page);
        await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{prefix}-menuitems-editor-options-scrolled.png"), FullPage = false });
        // Leaving with unsaved changes asks first (the quiet confirmation)
        await page.GetByTestId("menu-tabs").Locator("[data-tab='categories']").ClickAsync();
        await Assertions.Expect(page.Locator("#confirmationModal")).ToBeVisibleAsync();
        await SettleAsync(page);
        await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{prefix}-menuitems-editor-leave-confirm.png"), FullPage = false });
        await page.Locator("#cancelBtn").ClickAsync();

        // Drag and drop: a category lifted half-way over the next one (mouse down/move without release)
        await GotoInteractiveAsync(page, $"/Categories/{s.MenuId}");
        await SettleAsync(page);
        var handle = page.GetByTestId("category-row").First.GetByTestId("sort-handle");
        var box = (await handle.BoundingBoxAsync())!;
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2 + 30, new() { Steps = 4 });
        await page.Mouse.MoveAsync(box.X + box.Width / 2, box.Y + box.Height / 2 + 75, new() { Steps = 6 });
        await page.WaitForTimeoutAsync(300);
        await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{prefix}-categories-dragging.png"), FullPage = false });
        await page.Keyboard.PressAsync("Escape");
        await page.Mouse.UpAsync();

        // Inline edit of a category and of an allergen
        await GotoInteractiveAsync(page, $"/Categories/{s.MenuId}");
        await page.GetByTestId("category-row").Nth(1).Locator("button.sd-row-action").First.ClickAsync();
        await SettleAsync(page);
        await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{prefix}-categories-inline-edit.png"), FullPage = false });
        await GotoInteractiveAsync(page, $"/allergens/{s.MenuId}");
        await page.GetByTestId("allergen-row").First.Locator("button.sd-row-action").First.ClickAsync();
        await SettleAsync(page);
        await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{prefix}-allergens-inline-edit.png"), FullPage = false });

        // The quiet confirmation and the combo editor panel
        await GotoInteractiveAsync(page, $"/combos/{s.MenuId}");
        await page.GetByTestId("combo-card").First.Locator("button.sd-row-action--danger").ClickAsync();
        await Assertions.Expect(page.Locator("#confirmationModal")).ToBeVisibleAsync();
        await SettleAsync(page);
        await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{prefix}-confirmation.png"), FullPage = false });
        await page.Locator("#cancelBtn").ClickAsync();
        await page.GetByTestId("combo-card").First.Locator("button.sd-btn--secondary").ClickAsync();
        await Assertions.Expect(page.GetByTestId("combo-editor")).ToBeVisibleAsync();
        await SettleAsync(page);
        await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{prefix}-combo-editor.png"), FullPage = false });
        await page.Keyboard.PressAsync("Escape");

        // The menu switcher of the context bar, open
        await GotoInteractiveAsync(page, $"/Categories/{s.MenuId}");
        await page.GetByTestId("menu-switcher-button").ClickAsync();
        await Assertions.Expect(page.GetByTestId("menu-switcher")).ToBeVisibleAsync();
        await SettleAsync(page);
        await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{prefix}-menu-switcher-open.png"), FullPage = false });

        if (phoneSubset)
        {
            // The navigation drawer, opened from the top bar's menu button
            await GotoInteractiveAsync(page, $"/MenuItems/Category/{s.DrinksCategoryId}");
            await page.GetByTestId("nav-drawer-button").ClickAsync();
            await Assertions.Expect(page.Locator("dialog[data-testid='nav-drawer'][open]")).ToHaveCountAsync(1);
            await SettleAsync(page);
            await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{prefix}-drawer-open.png"), FullPage = false });
            return;
        }

        // The user menu, open
        await GotoInteractiveAsync(page, "/menus");
        await page.GetByTestId("user-menu-button").ClickAsync();
        await Assertions.Expect(page.GetByTestId("user-menu")).ToBeVisibleAsync();
        await SettleAsync(page);
        await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{prefix}-user-menu-open.png"), FullPage = false });

        await Shot("menuitems-editor", $"/MenuItems/Category/{s.CategoryId}/new");
        await Shot("menu-new", "/menu/edit");
        await Shot("menudetail-empty-menu", $"/menudetail/{s.EmptyMenuId}");
        await Shot("categories-empty-menu", $"/Categories/{s.EmptyMenuId}");
        await Shot("menuitems-empty-menu", $"/MenuItems/{s.EmptyMenuId}");
        await Shot("qrcode-new", "/qrcode/edit");
        await Shot("qrcode-edit-calendar", $"/qrcode/edit/{s.CalendarQrId}");
        await Shot("users-invite", "/users/invite");
        await Shot("users-edit", $"/users/edit/{s.TenantUserId}");

        // Identity pages render statically (no circuit)
        await page.GotoAsync("/Account/Manage");
        await SettleAsync(page);
        await page.ScreenshotAsync(new() { Path = Path.Combine(dir, $"{prefix}-account-manage.png"), FullPage = true });
    }

    /// <summary>
    /// The calendar of the QR code "Festtage (Kalender)" in December: the month grid with the Christmas period, the weekly
    /// fish Friday (struck through on the 25th) and New Year's Eve, the entry panel (side panel / bottom sheet), a conflicting
    /// period with its warning and marks, and the list view. Nothing is saved.
    /// </summary>
    private static async Task ShootCalendarEditorAsync(IPage page, string dir, string prefix, Seeded s)
    {
        string File(string name) => Path.Combine(dir, $"{prefix}-{name}.png");
        var editor = page.GetByTestId("calendar-editor");
        var openPanel = page.Locator("dialog[data-testid='calendar-panel'][open]");

        await GotoInteractiveAsync(page, $"/qrcode/edit/{s.HolidayQrId}");
        await WaitForCalendarAsync(page);
        await GotoMonthAsync(page, s.HolidayYear, 12);
        await SettleAsync(page);
        await page.ScreenshotAsync(new() { Path = File("qrcode-calendar"), FullPage = true });
        await editor.ScreenshotAsync(new() { Path = File("qrcode-calendar-editor") });

        // Click on the Christmas period: the panel with its fields and sentence
        await CalendarOf(page).Locator($"td[data-date='{s.HolidayYear}-12-24'] .fc-event").ClickAsync();
        await Assertions.Expect(openPanel).ToHaveCountAsync(1);
        await SettleAsync(page);
        await page.ScreenshotAsync(new() { Path = File("qrcode-calendar-panel") });
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(openPanel).ToHaveCountAsync(0);

        // A period that overlaps Christmas: warning in the panel, then the marks and the alert
        await page.GetByTestId("calendar-add-entry").ClickAsync();
        await Assertions.Expect(openPanel).ToHaveCountAsync(1);
        await page.Locator("#cal-panel-menu").SelectOptionAsync(new SelectOptionValue { Label = "Silvester" });
        await page.Locator("#cal-panel-date").FillAsync($"{s.HolidayYear}-12-26");
        await page.Locator("#cal-panel-until").FillAsync($"{s.HolidayYear}-12-27");
        await Assertions.Expect(page.GetByTestId("calendar-panel-conflict")).ToBeVisibleAsync();
        await SettleAsync(page);
        await page.ScreenshotAsync(new() { Path = File("qrcode-calendar-panel-conflict") });
        await page.GetByTestId("calendar-panel-apply").ClickAsync();
        await Assertions.Expect(openPanel).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByTestId("calendar-conflicts")).ToBeVisibleAsync();
        await SettleAsync(page);
        await editor.ScreenshotAsync(new() { Path = File("qrcode-calendar-conflict") });
        await System.IO.File.WriteAllTextAsync(Path.Combine(dir, $"{prefix}-qrcode-calendar-conflict.txt"), await PageTextsAsync(page));

        // List view of the month
        await CalendarOf(page).Locator(".fc-listMonth-button").ClickAsync();
        await Assertions.Expect(CalendarOf(page).Locator(".fc-list").First).ToBeVisibleAsync();
        await SettleAsync(page);
        await editor.ScreenshotAsync(new() { Path = File("qrcode-calendar-list") });
        await System.IO.File.WriteAllTextAsync(Path.Combine(dir, $"{prefix}-qrcode-calendar-list.txt"), await PageTextsAsync(page));
    }

    /// <summary>Tailwind's CDN build compiles classes after load; give it a moment and wait for fonts.</summary>
    /// <summary>
    /// The parts AP12a localized, for a language review (<c>E2E_LOCALE=de-DE</c>): the dashboard (first run and with a
    /// tenant), the open user menu and phone drawer, a menu's context bar with the switcher open, the confirmation, and the
    /// account pages — login and registration with validation and Identity messages, the account settings — at 1280 and
    /// 390 px, into <c>$E2E_SCREENSHOT_DIR/shell-account</c>.
    /// </summary>
    [Fact]
    public async Task Capture_shell_dashboard_and_account_pages()
    {
        var dir = Directory;
        if (dir is null) return; // opt-in only
        var target = Path.Combine(dir, "shell-account");
        System.IO.Directory.CreateDirectory(target);

        var email = await RegisterNewOwnerAsync();
        Guid ownerId;
        await using (var db = App.CreateDbContext(Guid.Empty))
            ownerId = Guid.Parse((await db.Users.SingleAsync(u => u.Email == email)).Id);
        var seeded = await SeedTenantAsync(ownerId);
        var state = await Context.StorageStateAsync();

        foreach (var (prefix, width, height, scale) in new[] { ("desktop", 1280, 900, 1), ("phone", 390, 844, 2) })
        {
            var phone = prefix == "phone";
            await using var context = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
            {
                BaseURL = App.ServerAddress, Locale = ContextLocale, StorageState = state, ReducedMotion = ReducedMotion.Reduce,
                ViewportSize = new ViewportSize { Width = width, Height = height }, DeviceScaleFactor = scale, IsMobile = phone, HasTouch = phone
            });
            context.SetDefaultTimeout(15_000);
            var page = await context.NewPageAsync();
            async Task Shot(string name, bool fullPage = true)
            {
                await SettleAsync(page);
                await page.ScreenshotAsync(new() { Path = Path.Combine(target, $"{prefix}-{name}.png"), FullPage = fullPage });
            }

            await GotoInteractiveAsync(page, "/dashboard");
            await Shot("dashboard");
            if (phone)
            {
                await page.GetByTestId("nav-drawer-button").ClickAsync();
                await Assertions.Expect(page.GetByTestId("nav-drawer")).ToBeVisibleAsync();
                await Shot("drawer", fullPage: false);
                await page.Keyboard.PressAsync("Escape");
            }
            else
            {
                await page.GetByTestId("user-menu-button").ClickAsync();
                await Shot("user-menu", fullPage: false);
                await page.Keyboard.PressAsync("Escape");
            }

            await GotoInteractiveAsync(page, $"/Categories/{seeded.MenuId}");
            await page.GetByTestId("menu-switcher-button").ClickAsync();
            await Shot("context-switcher", fullPage: false);
            await page.Keyboard.PressAsync("Escape");
            await page.GetByTestId("category-row").First.GetByTestId("sort-handle").FocusAsync();
            await page.Keyboard.PressAsync("ArrowUp");
            await Shot("context-bar", fullPage: false);

            foreach (var (name, url) in new[]
                     {
                         ("account-manage", "/Account/Manage"), ("account-email", "/Account/Manage/Email"),
                         ("account-password", "/Account/Manage/ChangePassword"), ("account-2fa", "/Account/Manage/TwoFactorAuthentication"),
                         ("account-authenticator", "/Account/Manage/EnableAuthenticator"), ("account-personal-data", "/Account/Manage/PersonalData"),
                         ("account-delete", "/Account/Manage/DeletePersonalData")
                     })
            {
                await page.GotoAsync(url);
                await Shot(name);
            }
            await page.GotoAsync("/Account/Manage/ChangePassword");
            await page.Locator("#Input\\.OldPassword").FillAsync("Falsch-Passw0rd!");
            await page.Locator("#Input\\.NewPassword").FillAsync("abc");
            await page.Locator("#Input\\.ConfirmPassword").FillAsync("abd");
            await page.Locator("main form button[type=submit]").Last.ClickAsync();
            await Shot("account-password-errors");

            // Anonymous: login and registration with their messages, the small pages
            await using var anonymous = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
            {
                BaseURL = App.ServerAddress, Locale = ContextLocale,
                ViewportSize = new ViewportSize { Width = width, Height = height }, DeviceScaleFactor = scale, IsMobile = phone, HasTouch = phone
            });
            var anon = await anonymous.NewPageAsync();
            async Task AnonShot(string name)
            {
                await SettleAsync(anon);
                await anon.ScreenshotAsync(new() { Path = Path.Combine(target, $"{prefix}-{name}.png"), FullPage = true });
            }
            await anon.GotoAsync("/Account/Login");
            await AnonShot("login");
            await anon.GetByTestId("login-submit").ClickAsync();
            await AnonShot("login-validation");
            await anon.Locator("#Input\\.Email").FillAsync(email);
            await anon.Locator("#Input\\.Password").FillAsync("Falsch-Passw0rd!");
            await anon.GetByTestId("login-submit").ClickAsync();
            await AnonShot("login-failed");
            await anon.GotoAsync("/Account/Register");
            await anon.Locator("#Input\\.Email").FillAsync(email);
            await anon.Locator("#Input\\.Password").FillAsync("abcdefgh");
            await anon.Locator("#Input\\.ConfirmPassword").FillAsync("abcdefgh");
            await anon.GetByTestId("register-submit").ClickAsync();
            await AnonShot("register-identity-errors");
            foreach (var (name, url) in new[]
                     {
                         ("forgot-password", "/Account/ForgotPassword"), ("reset-password", "/Account/ResetPassword?code=YWJj"),
                         ("lockout", "/Account/Lockout"), ("access-denied", "/Account/AccessDenied"),
                         ("resend-confirmation", "/Account/ResendEmailConfirmation"), ("invalid-reset", "/Account/InvalidPasswordReset")
                     })
            {
                await anon.GotoAsync(url);
                await AnonShot(name);
            }
        }

        // First run: an empty tenant's dashboard
        var (emptyContext, emptyPage) = await NewContextAsync();
        await using (emptyContext)
        {
            await RegisterAsync(emptyPage, UniqueEmail("empty"));
            await SettleAsync(emptyPage);
            await emptyPage.ScreenshotAsync(new() { Path = Path.Combine(target, "desktop-dashboard-empty.png"), FullPage = true });
        }
    }

    /// <summary>The visible text of the page plus every title, aria-label, placeholder and alt text, one per line.</summary>
    internal static Task<string> PageTextsAsync(IPage page) => page.EvaluateAsync<string>(@"() => {
        const attrs = [...document.querySelectorAll('[title],[aria-label],[placeholder],[alt]')]
            .flatMap(el => ['title', 'aria-label', 'placeholder', 'alt'].filter(a => el.hasAttribute(a)).map(a => `@${a}: ${el.getAttribute(a)}`));
        return [document.title, document.body.innerText, ...attrs].join('\n');
    }");

    private static async Task SettleAsync(IPage page)
    {
        try { await page.WaitForLoadStateAsync(LoadState.NetworkIdle, new() { Timeout = 5_000 }); } catch (TimeoutException) { }
        await page.WaitForTimeoutAsync(700);
    }

    private async Task<Seeded> SeedTenantAsync(Guid ownerId)
    {
        var menuId = Guid.NewGuid();
        var emptyMenuId = Guid.NewGuid();
        var lunchMenuId = Guid.NewGuid();
        var timeTableQrId = Guid.NewGuid();
        var calendarQrId = Guid.NewGuid();
        var holidayQrId = Guid.NewGuid();
        // The next Christmas (this year's until Dec 26), the fish Fridays from November into January
        var today = DateOnly.FromDateTime(DateTime.Today);
        var holidayYear = today.Month == 12 && today.Day > 26 ? today.Year + 1 : today.Year;
        var firstFriday = new DateOnly(holidayYear, 11, 1);
        while (firstFriday.DayOfWeek != DayOfWeek.Friday) firstFriday = firstFriday.AddDays(1);
        var lastFriday = new DateOnly(holidayYear + 1, 1, 31);
        while (lastFriday.DayOfWeek != DayOfWeek.Friday) lastFriday = lastFriday.AddDays(-1);
        var tenantUserId = Guid.NewGuid();
        // The team (AP14: managers): "Max Kellner" (active, signs in with the tests' password) and "Anna Berger" (inactive).
        // The facts of this class share one database: a later tenant gets numbered addresses.
        string managerEmail;
        await using (var check = App.CreateDbContext(ownerId))
        {
            var taken = await check.Users.CountAsync(u => u.Email!.StartsWith("kellner") && u.Email.EndsWith("@gasthaus.test"));
            managerEmail = taken == 0 ? "kellner@gasthaus.test" : $"kellner{taken + 1}@gasthaus.test";
        }
        Guid firstCategoryId = Guid.Empty;
        Guid drinksCategoryId = Guid.Empty;

        await App.SeedAsync(ownerId, async db =>
        {
            // --- Main menu with four categories, allergens, dietary flags and a combo
            var allergens = new Dictionary<string, Allergen>();
            foreach (var (code, name) in new[] { ("A", "Glutenhaltiges Getreide"), ("C", "Eier"), ("G", "Milch / Laktose"), ("L", "Sellerie"), ("H", "Schalenfrüchte"), ("O", "Sulfite") })
                allergens[code] = new Allergen { Id = Guid.NewGuid(), Code = code, Name = name, MenuId = menuId };
            db.Allergens.AddRange(allergens.Values);

            var vorspeisen = new Category { Id = Guid.NewGuid(), Name = "Vorspeisen", MenuId = menuId };
            var haupt = new Category { Id = Guid.NewGuid(), Name = "Hauptspeisen", MenuId = menuId };
            var desserts = new Category { Id = Guid.NewGuid(), Name = "Desserts", MenuId = menuId };
            var getraenke = new Category { Id = Guid.NewGuid(), Name = "Getränke", MenuId = menuId };
            firstCategoryId = vorspeisen.Id;
            // Menu card order (starters first), not alphabetical
            (vorspeisen.SortOrder, haupt.SortOrder, desserts.SortOrder, getraenke.SortOrder) = (0, 1, 2, 3);

            db.Menus.Add(new Menu
            {
                Id = menuId, Name = "Gasthaus Zur Post", Description = "Hauptkarte, ganztägig",
                Theme = DesignTheme.Classic, Language = MenuLanguage.German,
                Categories = new List<Category> { vorspeisen, haupt, desserts, getraenke }
            });

            MenuItem Item(Category c, string name, string desc, decimal price, bool vegan = false, bool veg = false, bool spicy = false, params string[] codes)
            {
                var item = new MenuItem { Id = Guid.NewGuid(), Name = name, Description = desc, Price = price, CategoryId = c.Id, IsVegan = vegan, IsVegetarian = veg, IsSpicy = spicy };
                foreach (var code in codes) item.Allergens.Add(allergens[code]);
                db.MenuItems.Add(item);
                return item;
            }

            Item(vorspeisen, "Frittatensuppe", "Klare Rindsuppe mit Frittaten", 5.20m, codes: new[] { "A", "C", "L" });
            Item(vorspeisen, "Kürbiscremesuppe", "Mit Kernöl und Kürbiskernen", 5.90m, veg: true, codes: new[] { "G", "L" });
            Item(vorspeisen, "Bruschetta", "Tomaten, Basilikum, Knoblauch", 7.40m, vegan: true, codes: new[] { "A" });
            var schnitzel = Item(haupt, "Wiener Schnitzel vom Kalb", "Mit Petersilerdäpfeln und Preiselbeeren", 24.90m, codes: new[] { "A", "C", "G" });
            Item(haupt, "Zwiebelrostbraten", "Mit Bratkartoffeln und Röstzwiebeln", 26.50m, codes: new[] { "A", "O" });
            Item(haupt, "Käsespätzle", "Mit Bergkäse und Röstzwiebeln", 15.80m, veg: true, codes: new[] { "A", "C", "G" });
            Item(haupt, "Chili sin Carne", "Bohnen, Mais, Paprika, Reis", 14.20m, vegan: true, spicy: true);
            Item(haupt, "Tagesfisch", "Nach Marktangebot, Preis nach Tagespreis", 0m, codes: new[] { "G" });
            var strudel = Item(desserts, "Apfelstrudel", "Hausgemacht, mit Vanillesauce", 6.80m, veg: true, codes: new[] { "A", "C", "G" });
            Item(desserts, "Kaiserschmarrn", "Mit Zwetschkenröster, für 2 Personen", 13.90m, veg: true, codes: new[] { "A", "C", "G" });
            var spritzer = Item(getraenke, "Sommerspritzer 0,25 l", "Grüner Veltliner mit Soda", 4.10m, vegan: true, codes: new[] { "O" });
            Item(getraenke, "Almdudler 0,35 l", "", 3.60m, vegan: true);
            Item(getraenke, "Melange", "Wiener Kaffeehaus-Klassiker", 4.20m, veg: true, codes: new[] { "G" });

            // --- Guest-menu data model: subtitles, labels, variants, extras and the restaurant profile
            vorspeisen.Description = "Suppen und kleine Speisen";
            haupt.Description = "Warme Küche bis 21:30 Uhr";
            drinksCategoryId = getraenke.Id;
            schnitzel.IsPopular = true;
            MenuItemPriceOption Option(PriceOptionKind kind, string name, decimal price, int sortOrder) =>
                new() { Id = Guid.NewGuid(), Kind = kind, Name = name, Price = price, SortOrder = sortOrder };
            spritzer.Name = "Sommerspritzer";
            spritzer.PriceOptions.Add(Option(PriceOptionKind.Variant, "0,25 l", 4.10m, 0));
            spritzer.PriceOptions.Add(Option(PriceOptionKind.Variant, "0,5 l", 7.40m, 1));
            var melange = db.ChangeTracker.Entries<MenuItem>().Single(e => e.Entity.Name == "Melange").Entity;
            melange.Price = 3.40m;
            melange.IsAlcoholFree = true;
            melange.IsNew = true;
            melange.PriceOptions.Add(Option(PriceOptionKind.Variant, "Klein", 3.40m, 0));
            melange.PriceOptions.Add(Option(PriceOptionKind.Variant, "Groß", 4.20m, 1));
            melange.PriceOptions.Add(Option(PriceOptionKind.Extra, "+ Hafermilch", 0.50m, 0));
            melange.PriceOptions.Add(Option(PriceOptionKind.Extra, "+ Schlagobers", 0.60m, 1));
            var spaetzle = db.ChangeTracker.Entries<MenuItem>().Single(e => e.Entity.Name == "Käsespätzle").Entity;
            spaetzle.PriceOptions.Add(Option(PriceOptionKind.Extra, "+ Speck", 2.50m, 0));
            var cover = new Image { Id = Guid.NewGuid(), MimeType = "image/png", Content = GradientPng(1200, 400, (120, 72, 40), (214, 168, 96)) };
            var logo = new Image { Id = Guid.NewGuid(), MimeType = "image/png", Content = GradientPng(320, 320, (30, 58, 110), (90, 140, 200)) };
            db.Images.AddRange(cover, logo);
            db.RestaurantProfiles.Add(new RestaurantProfile
            {
                Id = Guid.NewGuid(), Name = "Gasthaus Zur Post",
                Description = "Wiener Wirtshausküche seit 1890: Schnitzel, Rostbraten und Mehlspeisen aus der eigenen Küche.",
                LogoImagePath = $"image/{logo.Id}", CoverImagePath = $"image/{cover.Id}",
                Phone = "+43 1 234 56 78", Email = "info@example.com", Website = "https://example.com",
                InstagramUrl = "https://example.com/instagram", GoogleReviewUrl = "https://example.com/review",
                Address = "Hauptplatz 1\n1010 Wien", OpeningHours = "Mo–Fr 11:00–23:00\nSa 10:00–23:00\nSo und Feiertag 10:00–21:00"
            });

            db.MenuCombos.Add(new MenuCombo
            {
                Id = Guid.NewGuid(), Name = "Mittagsmenü", Description = "Schnitzel mit Strudel und Spritzer",
                ComboPrice = 29.90m, MenuId = menuId, TriggerMenuItemId = schnitzel.Id,
                Items = { new MenuComboItem { MenuItemId = strudel.Id }, new MenuComboItem { MenuItemId = spritzer.Id } }
            });

            // --- Lunch menu (small) and an empty menu (freshly created, nothing in it yet)
            var lunchCat = new Category { Id = Guid.NewGuid(), Name = "Mittagsteller", MenuId = lunchMenuId };
            db.Menus.Add(new Menu
            {
                Id = lunchMenuId, Name = "Mittagskarte", Description = "Mo bis Fr 11:30 bis 14:00",
                Theme = DesignTheme.Modern, Language = MenuLanguage.German, Categories = new List<Category> { lunchCat }
            });
            db.MenuItems.AddRange(
                new MenuItem { Id = Guid.NewGuid(), Name = "Menü 1: Gulasch", Description = "Mit Semmelknödel", Price = 11.90m, CategoryId = lunchCat.Id },
                new MenuItem { Id = Guid.NewGuid(), Name = "Menü 2: Gemüselasagne", Description = "Mit Blattsalat", Price = 10.90m, CategoryId = lunchCat.Id, IsVegetarian = true });

            db.Menus.Add(new Menu { Id = emptyMenuId, Name = "Abendkarte", Description = "", Theme = DesignTheme.Elegant, Language = MenuLanguage.German });

            // --- QR codes: direct, time table, calendar
            var directQrId = Guid.NewGuid();
            db.QRCodes.Add(new QRCode { Id = directQrId, Title = "Tisch 1", MenuId = menuId });
            db.QRCodes.Add(new QRCode { Id = Guid.NewGuid(), Title = "Tisch 2", MenuId = menuId });
            db.QRCodes.Add(new QRCode
            {
                Id = timeTableQrId, Title = "Eingang (Zeitplan)", IsTimeTableBased = true,
                TimeTableEntries = new List<TimeTableEntry>
                {
                    new() { Id = Guid.NewGuid(), StartTime = new TimeOnly(11, 0), EndTime = new TimeOnly(14, 0), MenuId = lunchMenuId },
                    new() { Id = Guid.NewGuid(), StartTime = new TimeOnly(17, 0), EndTime = new TimeOnly(22, 0), MenuId = menuId }
                }
            });
            db.QRCodes.Add(new QRCode
            {
                Id = calendarQrId, Title = "Tagesaktion (Kalender)", IsCalendarBased = true, MenuId = menuId,
                CalendarEntries = new List<CalendarEntry>
                {
                    new() { Id = Guid.NewGuid(), Date = DateOnly.FromDateTime(DateTime.Today), MenuId = lunchMenuId },
                    new() { Id = Guid.NewGuid(), Date = DateOnly.FromDateTime(DateTime.Today), RecurringDayOfWeek = DayOfWeek.Friday, EndDate = DateOnly.FromDateTime(DateTime.Today.AddMonths(2)), MenuId = menuId }
                }
            });

            // --- Holiday calendar: Christmas period, weekly fish Friday with an until date, New Year's Eve
            Guid SmallMenu(string name, string dish, decimal price)
            {
                var id = Guid.NewGuid();
                var category = new Category { Id = Guid.NewGuid(), Name = "Gerichte", MenuId = id };
                db.Menus.Add(new Menu { Id = id, Name = name, Description = "", Theme = DesignTheme.Classic, Language = MenuLanguage.German, Categories = new List<Category> { category } });
                db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = dish, Description = "", Price = price, CategoryId = category.Id });
                return id;
            }
            var christmasMenu = SmallMenu("Weihnachtsmenü", "Gansl mit Rotkraut und Knödel", 29.50m);
            var fishMenu = SmallMenu("Fischfreitag", "Gebratene Forelle mit Petersilerdäpfeln", 21.90m);
            var newYearMenu = SmallMenu("Silvester", "Silvestermenü in fünf Gängen", 79.00m);
            db.QRCodes.Add(new QRCode
            {
                Id = holidayQrId, Title = "Festtage (Kalender)", IsCalendarBased = true, MenuId = menuId,
                CalendarEntries = new List<CalendarEntry>
                {
                    new() { Id = Guid.NewGuid(), Date = firstFriday, EndDate = lastFriday, RecurringDayOfWeek = DayOfWeek.Friday, MenuId = fishMenu },
                    new() { Id = Guid.NewGuid(), Date = new DateOnly(holidayYear, 12, 24), EndDate = new DateOnly(holidayYear, 12, 26), MenuId = christmasMenu },
                    new() { Id = Guid.NewGuid(), Date = new DateOnly(holidayYear, 12, 31), MenuId = newYearMenu }
                }
            });

            // --- The team: two managers, one of them deactivated
            foreach (var (id, email, name, active) in new[]
                     {
                         (tenantUserId, managerEmail, "Max Kellner", true),
                         (Guid.NewGuid(), managerEmail.Replace("kellner", "berger"), "Anna Berger", false)
                     })
            {
                var account = new ApplicationUser
                {
                    Id = Guid.NewGuid().ToString(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                    Email = email, NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true,
                    SecurityStamp = Guid.NewGuid().ToString(), TenantOwnerId = ownerId.ToString()
                };
                account.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<ApplicationUser>().HashPassword(account, Password);
                db.Users.Add(account);
                db.TenantUsers.Add(new TenantUser
                {
                    Id = id, ApplicationUserId = account.Id, TenantOwnerId = ownerId.ToString(),
                    Role = TenantRole.Manager, DisplayName = name, IsActive = active
                });
            }

            // --- Two weeks of views and clicks for the analytics page
            var rnd = new Random(42);
            for (var day = 0; day < 14; day++)
            {
                var date = DateTime.UtcNow.Date.AddDays(-day);
                for (var i = 0; i < 5 + rnd.Next(12); i++)
                {
                    var session = Guid.NewGuid().ToString("N");
                    db.MenuViews.Add(new MenuView { Id = Guid.NewGuid(), SessionId = session, MenuId = menuId, QRCodeId = directQrId, ViewedAt = date.AddHours(11 + rnd.Next(10)) });
                    if (rnd.Next(3) == 0)
                        db.MenuItemClicks.Add(new MenuItemClick { Id = Guid.NewGuid(), SessionId = session, MenuId = menuId, MenuItemId = schnitzel.Id, ClickedAt = date.AddHours(12) });
                }
                for (var i = 0; i < rnd.Next(5); i++)
                    db.MenuViews.Add(new MenuView { Id = Guid.NewGuid(), SessionId = Guid.NewGuid().ToString("N"), MenuId = lunchMenuId, QRCodeId = timeTableQrId, ViewedAt = date.AddHours(12) });
            }
            await Task.CompletedTask;
        });

        return new Seeded(menuId, firstCategoryId, emptyMenuId, timeTableQrId, calendarQrId, tenantUserId, drinksCategoryId,
            holidayQrId, holidayYear, managerEmail);
    }

    /// <summary>A diagonal two-colour gradient as PNG (stand-in for a photo in the screenshots).</summary>
    private static byte[] GradientPng(int width, int height, (byte R, byte G, byte B) from, (byte R, byte G, byte B) to)
    {
        using var image = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(width, height);
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                var t = (x / (double)width + y / (double)height) / 2;
                byte Mix(byte a, byte b) => (byte)(a + (b - a) * t);
                image[x, y] = new SixLabors.ImageSharp.PixelFormats.Rgba32(Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B));
            }
        using var stream = new MemoryStream();
        SixLabors.ImageSharp.ImageExtensions.SaveAsPng(image, stream);
        return stream.ToArray();
    }
}
