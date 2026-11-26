using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace SpeiseDirekt3.E2ETests.Infrastructure;

/// <summary>
/// Gives every test a fresh browser context (own cookies/storage) on the class fixture's server and
/// resets the app clock. Requests to other hosts are blocked unless <see cref="E2ESettings.AllowExternalRequestsEnvVar"/>
/// is set (<see cref="E2EFixture.BlockExternalRequestsAsync"/>); the app needs none (AP15b: Tailwind is local too).
/// </summary>
public abstract class E2ETestBase : IClassFixture<E2EFixture>, IAsyncLifetime
{
    /// <summary>Password used for all accounts created by the tests (meets the Identity defaults).</summary>
    protected const string Password = E2ESeedData.Password;

    protected E2EFixture Fixture { get; }
    protected E2EAppFactory App => Fixture.App;
    protected IBrowserContext Context { get; private set; } = default!;
    protected IPage Page { get; private set; } = default!;

    /// <summary>
    /// Sent by the server once the router has rendered interactively in a new circuit; from then on
    /// the page reacts to clicks (before that, during prerendering, clicks are lost).
    /// </summary>
    private const string CircuitReadyMarker = "enableNavigationInterception";

    protected E2ETestBase(E2EFixture fixture) => Fixture = fixture;

    /// <summary>Browser language of <see cref="Page"/> and of <see cref="NewContextAsync"/> without a locale.</summary>
    protected virtual string ContextLocale => E2ESettings.DefaultLocale;

    public async Task InitializeAsync()
    {
        App.Clock.Reset();
        (Context, Page) = await NewContextAsync();
    }

    public async Task DisposeAsync() => await Context.DisposeAsync();

    /// <summary>
    /// A new, independent browser context (e.g. an anonymous guest next to a logged-in owner), 1280 × 900 px, in the
    /// browser language <paramref name="locale"/> (default <see cref="ContextLocale"/>, i.e. English).
    /// Disposed by the caller.
    /// </summary>
    protected async Task<(IBrowserContext Context, IPage Page)> NewContextAsync(string? locale = null)
    {
        // In the browser of that language (E2EFixture.BrowserForAsync), external requests blocked
        var context = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress,
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
            Locale = locale ?? ContextLocale
        });
        context.SetDefaultTimeout(15_000);

        return (context, await context.NewPageAsync());
    }

    // --- Navigation / Blazor interactivity ---

    /// <summary>
    /// Runs <paramref name="action"/> (a navigation, a form post that redirects, …) and waits until the
    /// resulting page has an interactive Blazor circuit. Use for back-office pages before clicking.
    /// </summary>
    protected static async Task WaitForInteractiveAsync(IPage page, Func<Task> action)
    {
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnFrame(object? _, IWebSocketFrame frame)
        {
            if (frame.Text?.Contains(CircuitReadyMarker) == true)
                ready.TrySetResult();
        }
        void OnWebSocket(object? _, IWebSocket ws)
        {
            if (ws.Url.Contains("/_blazor"))
                ws.FrameReceived += OnFrame;
        }

        page.WebSocket += OnWebSocket;
        try
        {
            await action();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"Blazor circuit did not become interactive on {page.Url}.");
        }
        finally
        {
            page.WebSocket -= OnWebSocket;
        }
    }

    /// <summary>Full page load of <paramref name="url"/>, then waits for the interactive circuit.</summary>
    protected Task GotoInteractiveAsync(string url) => GotoInteractiveAsync(Page, url);

    protected static Task GotoInteractiveAsync(IPage page, string url) =>
        WaitForInteractiveAsync(page, () => page.GotoAsync(url));

    // --- Guest pages (static SSR + js/guest-menu.js) ---

    /// <summary>Opens the public menu page <c>/menu/{id}</c> and waits until the guest script is ready.</summary>
    protected Task OpenMenuAsync(Guid menuId) => OpenGuestPageAsync(Page, $"/menu/{menuId}");

    /// <summary>
    /// Full page load of a guest page (<c>/menu/{id}</c>, <c>/qr/{id}</c>, <c>/menu/preview/{id}</c>). They render
    /// statically (no circuit); <c>js/guest-menu.js</c> sets <c>data-guest-ready</c> on <c>&lt;html&gt;</c> once
    /// sheets, chips and item links react. Notice pages set it too.
    /// </summary>
    protected static async Task OpenGuestPageAsync(IPage page, string url)
    {
        var response = await page.GotoAsync(url);
        if (response is { Ok: false })
            throw new InvalidOperationException($"{url} returned HTTP {response.Status}.");
        await WaitForGuestReadyAsync(page);
    }

    /// <summary>Waits for <c>data-guest-ready</c> (set by <c>js/guest-menu.js</c>; fails if the module did not load).</summary>
    protected static Task WaitForGuestReadyAsync(IPage page) =>
        page.Locator("html[data-guest-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });

    /// <summary>The item sheet (<c>&lt;dialog data-sheet="item"&gt;</c>); visible while an item is open.</summary>
    protected ILocator ItemSheet => ItemSheetOf(Page);

    protected static ILocator ItemSheetOf(IPage page) => page.GetByTestId("item-sheet");

    /// <summary>The card (photo category) or list row (text-only category) of an item, by its exact name.</summary>
    protected static ILocator ItemEntry(IPage page, string itemName) =>
        page.Locator("[data-testid='item-card'], [data-testid='list-row']",
            new() { Has = page.Locator("h3", new() { HasTextRegex = ExactText(itemName) }) });

    protected ILocator ItemEntry(string itemName) => ItemEntry(Page, itemName);

    /// <summary>Opens an item by clicking its card/row (exact name) and waits for the item sheet.</summary>
    protected Task ClickMenuItemAsync(string itemName) => ClickMenuItemAsync(Page, itemName);

    protected static async Task ClickMenuItemAsync(IPage page, string itemName)
    {
        await ItemEntry(page, itemName).Locator("a[data-item-link]").ClickAsync();
        var sheet = ItemSheetOf(page);
        await Assertions.Expect(sheet).ToBeVisibleAsync();
        await Assertions.Expect(sheet.GetByRole(AriaRole.Heading, new() { Name = itemName, Exact = true })).ToBeVisibleAsync();
    }

    /// <summary>Closes the item sheet with its close button and waits until it is hidden.</summary>
    protected static async Task CloseItemSheetAsync(IPage page)
    {
        await ItemSheetOf(page).Locator("[data-sheet-close]").ClickAsync();
        await Assertions.Expect(ItemSheetOf(page)).ToBeHiddenAsync();
    }

    protected static Regex ExactText(string text) => new($"^\\s*{Regex.Escape(text)}\\s*$");

    // --- Accounts ---

    /// <summary>A unique e-mail address: every test works in its own tenant (the class shares one DB).</summary>
    protected static string UniqueEmail(string prefix = "owner") => $"{prefix}-{Guid.NewGuid():N}@e2e.test";

    /// <summary>
    /// Registers a new account via <c>/Account/Register</c>; the app signs it in right away. A self-registered owner is on
    /// the free plan (one menu, one QR code); the tests' owners are made <b>paid</b> right after (role <c>PaidTenant</c>,
    /// <see cref="E2EAppFactory.SetPaidAsync"/>), so a test can create as many menus and QR codes as it needs.
    /// <paramref name="paid"/> = false keeps the app's default: a free owner (the plan-limit tests).
    /// </summary>
    protected async Task RegisterAsync(IPage page, string email, string password = Password, bool paid = true)
    {
        await page.GotoAsync("/Account/Register");
        await page.Locator("#Input\\.Email").FillAsync(email);
        await page.Locator("#Input\\.Password").FillAsync(password);
        await page.Locator("#Input\\.ConfirmPassword").FillAsync(password);
        // By test id: the button's text depends on the browser language
        await WaitForInteractiveAsync(page, () => page.GetByTestId("register-submit").ClickAsync());
        // Read from the database at every check: counts from the next page on, no new sign-in
        if (paid)
            await SetPaidAsync(await OwnerIdAsync(email), true);
    }

    /// <summary>Logs in via <c>/Account/Login</c> and waits for the (interactive) start page.</summary>
    protected static async Task LoginAsync(IPage page, string email, string password = Password, string? returnUrl = null)
    {
        await page.GotoAsync(returnUrl is null ? "/Account/Login" : $"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        await page.Locator("#Input\\.Email").FillAsync(email);
        await page.Locator("#Input\\.Password").FillAsync(password);
        await WaitForInteractiveAsync(page, () => page.GetByTestId("login-submit").ClickAsync());
    }

    /// <summary>
    /// Registers a fresh owner account (own tenant) in <see cref="Page"/> and returns its e-mail. Paid unless
    /// <paramref name="paid"/> is false (see <see cref="RegisterAsync"/>).
    /// </summary>
    protected async Task<string> RegisterNewOwnerAsync(bool paid = true)
    {
        var email = UniqueEmail();
        await RegisterAsync(Page, email, paid: paid);
        return email;
    }

    /// <summary>The owner <paramref name="ownerId"/> on the paid plan (role <c>PaidTenant</c>) or the free one.</summary>
    protected Task SetPaidAsync(Guid ownerId, bool paid) => App.SetPaidAsync(ownerId, paid);

    /// <summary>An owner account as <see cref="SeedOwnerAsync"/> created it.</summary>
    protected sealed record SeededOwner(string Email, Guid OwnerId);

    /// <summary>
    /// Seeds an owner account (own tenant, no content) straight into the database, with <see cref="Password"/> — faster
    /// than <see cref="RegisterAsync"/> when the test does not need a signed-in page. Paid unless <paramref name="paid"/>
    /// is false, like the registered owners. Sign in with <see cref="LoginAsync"/>.
    /// </summary>
    protected async Task<SeededOwner> SeedOwnerAsync(string? email = null, bool paid = true)
    {
        email ??= UniqueEmail();
        var account = new SpeiseDirekt.Data.ApplicationUser
        {
            Id = Guid.NewGuid().ToString(), UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email,
            NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString()
        };
        account.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<SpeiseDirekt.Data.ApplicationUser>().HashPassword(account, Password);
        var ownerId = Guid.Parse(account.Id);
        await App.SeedAsync(ownerId, db =>
        {
            db.Users.Add(account);
            return Task.CompletedTask;
        });
        if (paid)
            await SetPaidAsync(ownerId, true);
        return new SeededOwner(email, ownerId);
    }

    /// <summary>A team member as <see cref="SeedTeamMemberAsync"/> created it.</summary>
    protected sealed record TeamMember(string Email, string AccountId, Guid TenantUserId);

    /// <summary>
    /// Seeds a team member of <paramref name="ownerId"/> (a manager — AP14 knows no other role): an Identity user with
    /// <see cref="Password"/> and its <c>TenantUser</c> row, active or not. Sign in with <see cref="LoginAsync"/>.
    /// </summary>
    protected async Task<TeamMember> SeedTeamMemberAsync(Guid ownerId, string displayName = "Max Kellner", bool active = true, string? email = null)
    {
        email ??= UniqueEmail("team");
        var account = new SpeiseDirekt.Data.ApplicationUser
        {
            Id = Guid.NewGuid().ToString(), UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email,
            NormalizedEmail = email.ToUpperInvariant(), EmailConfirmed = true, SecurityStamp = Guid.NewGuid().ToString(),
            TenantOwnerId = ownerId.ToString()
        };
        account.PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<SpeiseDirekt.Data.ApplicationUser>().HashPassword(account, Password);
        var member = new SpeiseDirekt.Model.TenantUser
        {
            Id = Guid.NewGuid(), ApplicationUserId = account.Id, TenantOwnerId = ownerId.ToString(),
            Role = SpeiseDirekt.Model.TenantRole.Manager, DisplayName = displayName, IsActive = active
        };
        await App.SeedAsync(ownerId, db =>
        {
            db.Users.Add(account);
            db.TenantUsers.Add(member);
            return Task.CompletedTask;
        });
        return new TeamMember(email, account.Id, member.Id);
    }

    /// <summary>The tenant (owner) id of the account <paramref name="email"/>.</summary>
    protected async Task<Guid> OwnerIdAsync(string email)
    {
        await using var db = App.CreateDbContext(Guid.Empty);
        return Guid.Parse((await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.SingleAsync(db.Users, u => u.Email == email)).Id);
    }

    /// <summary>
    /// Logs out with the logout form of the top bar's user menu (from 1024 px) or of the navigation drawer (narrower) and
    /// waits for the page it returns to: the same URL, which asks for a login when it needs one (the dashboard does:
    /// the static login page, no circuit).
    /// </summary>
    protected static async Task LogoutAsync(IPage page)
    {
        var userMenu = page.GetByTestId("user-menu-button");
        if (await userMenu.IsVisibleAsync())
            await userMenu.ClickAsync();
        else
            await page.GetByTestId("nav-drawer-button").ClickAsync();
        await page.GetByTestId("logout-button").Filter(new() { Visible = true }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("user-menu-button")).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByTestId("nav-drawer-button")).ToHaveCountAsync(0);
        await page.WaitForLoadStateAsync(LoadState.Load);
    }
}
