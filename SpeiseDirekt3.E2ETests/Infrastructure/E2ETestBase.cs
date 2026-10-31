using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace SpeiseDirekt3.E2ETests.Infrastructure;

/// <summary>
/// Gives every test a fresh browser context (own cookies/storage) on the class fixture's server and
/// resets the app clock. Requests to other hosts (Tailwind/Bootstrap CDNs, fonts, …) are blocked unless
/// <see cref="E2ESettings.AllowExternalRequestsEnvVar"/> is set; while blocked, a minimal stylesheet
/// emulates the Tailwind display utilities so hidden modals stay hidden.
/// </summary>
public abstract class E2ETestBase : IClassFixture<E2EFixture>, IAsyncLifetime
{
    /// <summary>Password used for all accounts created by the tests (meets the Identity defaults).</summary>
    protected const string Password = "E2e-Passw0rd!";

    protected E2EFixture Fixture { get; }
    protected E2EAppFactory App => Fixture.App;
    protected IBrowserContext Context { get; private set; } = default!;
    protected IPage Page { get; private set; } = default!;

    private static readonly Regex ComplexMenuModule = new(@"^/js/complex-menu(\.[a-z0-9]+)?\.js$");

    /// <summary>
    /// Sent by the server once the router has rendered interactively in a new circuit; from then on
    /// the page reacts to clicks (before that, during prerendering, clicks are lost).
    /// </summary>
    private const string CircuitReadyMarker = "enableNavigationInterception";

    protected E2ETestBase(E2EFixture fixture) => Fixture = fixture;

    public async Task InitializeAsync()
    {
        App.Clock.Reset();
        (Context, Page) = await NewContextAsync();
    }

    public async Task DisposeAsync() => await Context.DisposeAsync();

    /// <summary>
    /// A new, independent browser context (e.g. an anonymous guest next to a logged-in owner).
    /// Disposed by the caller.
    /// </summary>
    protected async Task<(IBrowserContext Context, IPage Page)> NewContextAsync()
    {
        var context = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress,
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
        });
        context.SetDefaultTimeout(15_000);

        if (!E2ESettings.AllowExternalRequests)
        {
            var appOrigin = new Uri(App.ServerAddress).GetLeftPart(UriPartial.Authority);
            await context.RouteAsync("**/*", route =>
                route.Request.Url.StartsWith(appOrigin, StringComparison.OrdinalIgnoreCase)
                    ? route.ContinueAsync()
                    : route.AbortAsync());

            // Adopted stylesheets survive Blazor's DOM patching of <head> (enhanced navigation)
            await context.AddInitScriptAsync($$"""
                (() => {
                    const sheet = new CSSStyleSheet();
                    sheet.replaceSync({{System.Text.Json.JsonSerializer.Serialize(E2ESettings.TailwindVisibilityCss)}});
                    document.adoptedStyleSheets = [...document.adoptedStyleSheets, sheet];
                })();
                """);
            await context.AddInitScriptAsync(E2ESettings.BootstrapStubJs);
        }

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

        await DismissCookieConsentAsync(page);
    }

    /// <summary>Full page load of <paramref name="url"/>, then waits for the interactive circuit.</summary>
    protected Task GotoInteractiveAsync(string url) => GotoInteractiveAsync(Page, url);

    protected static Task GotoInteractiveAsync(IPage page, string url) =>
        WaitForInteractiveAsync(page, () => page.GotoAsync(url));

    /// <summary>
    /// The BytexDigital cookie consent prompt is currently disabled (<c>CookieConsentHandler</c> is commented
    /// out in <c>Routes.razor</c>). Should it come back, accept it so it does not cover the page.
    /// </summary>
    protected static async Task DismissCookieConsentAsync(IPage page)
    {
        var accept = page.Locator("button", new PageLocatorOptions
        {
            HasTextRegex = new Regex("^(Accept all|Alle akzeptieren|Accept|Akzeptieren)$")
        });
        if (await accept.CountAsync() > 0 && await accept.First.IsVisibleAsync())
            await accept.First.ClickAsync();
    }

    // --- Public menu page ---

    /// <summary>
    /// Opens the public menu page and waits until the Blazor circuit is interactive.
    /// <c>ComplexMenuDisplayComp</c> imports <c>js/complex-menu.js</c> in its first interactive render,
    /// so that response marks the point from which clicks are handled. The URL may be fingerprinted
    /// (<c>complex-menu.{hash}.js</c>).
    /// </summary>
    protected Task OpenMenuAsync(Guid menuId) => OpenPublicMenuPageAsync(Page, $"/menu/{menuId}");

    /// <summary>Like <see cref="OpenMenuAsync"/> for any URL that shows the complex menu display (e.g. <c>/qr/{id}</c>).</summary>
    protected static async Task OpenPublicMenuPageAsync(IPage page, string url)
    {
        var interactive = page.WaitForResponseAsync(r =>
            ComplexMenuModule.IsMatch(new Uri(r.Url).AbsolutePath));
        await page.GotoAsync(url);
        var module = await interactive;
        if (!module.Ok)
            throw new InvalidOperationException($"{module.Url} returned HTTP {module.Status}; static web assets are not served.");
    }

    /// <summary>The item overlay (fixed backdrop) that opens when a guest clicks an item.</summary>
    protected ILocator Overlay => OverlayOf(Page);

    protected static ILocator OverlayOf(IPage page) => page.Locator("div.fixed.inset-0.z-\\[9999\\]");

    /// <summary>Clicks a menu item card in the category list by its name.</summary>
    protected Task ClickMenuItemAsync(string itemName) => ClickMenuItemAsync(Page, itemName);

    protected static Task ClickMenuItemAsync(IPage page, string itemName) =>
        page.Locator("h3", new PageLocatorOptions { HasTextString = itemName }).First.ClickAsync();

    // --- Accounts ---

    /// <summary>A unique e-mail address: every test works in its own tenant (the class shares one DB).</summary>
    protected static string UniqueEmail(string prefix = "owner") => $"{prefix}-{Guid.NewGuid():N}@e2e.test";

    /// <summary>Registers a new account via <c>/Account/Register</c>; the app signs it in right away.</summary>
    protected static async Task RegisterAsync(IPage page, string email, string password = Password)
    {
        await page.GotoAsync("/Account/Register");
        await page.Locator("#Input\\.Email").FillAsync(email);
        await page.Locator("#Input\\.Password").FillAsync(password);
        await page.Locator("#Input\\.ConfirmPassword").FillAsync(password);
        await WaitForInteractiveAsync(page, () => page.GetByRole(AriaRole.Button, new() { Name = "Create Account" }).ClickAsync());
    }

    /// <summary>Logs in via <c>/Account/Login</c> and waits for the (interactive) start page.</summary>
    protected static async Task LoginAsync(IPage page, string email, string password = Password, string? returnUrl = null)
    {
        await page.GotoAsync(returnUrl is null ? "/Account/Login" : $"/Account/Login?returnUrl={Uri.EscapeDataString(returnUrl)}");
        await page.Locator("#Input\\.Email").FillAsync(email);
        await page.Locator("#Input\\.Password").FillAsync(password);
        await WaitForInteractiveAsync(page, () => page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync());
    }

    /// <summary>Registers a fresh owner account (own tenant) in <see cref="Page"/> and returns its e-mail.</summary>
    protected async Task<string> RegisterNewOwnerAsync()
    {
        var email = UniqueEmail();
        await RegisterAsync(Page, email);
        return email;
    }

    /// <summary>Logs out via the sidebar's logout form.</summary>
    protected static Task LogoutAsync(IPage page) =>
        WaitForInteractiveAsync(page, () => page.GetByRole(AriaRole.Button, new() { Name = "Logout" }).First.ClickAsync());
}
