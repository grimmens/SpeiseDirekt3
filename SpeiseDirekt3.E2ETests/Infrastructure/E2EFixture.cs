using Microsoft.Playwright;

namespace SpeiseDirekt3.E2ETests.Infrastructure;

/// <summary>
/// Class fixture: one app server, one SQLite file and one browser per browser language per test class.
/// xUnit runs test classes in parallel, so every class has its own isolated database;
/// the tests of one class run one after another and share it.
/// </summary>
public sealed class E2EFixture : IAsyncLifetime
{
    /// <summary>
    /// Optional path to a Chromium binary. If unset, Playwright's own Chromium is used and
    /// installed on first use (one-time download, then cached in the user profile).
    /// </summary>
    public const string ChromiumExecutableEnvVar = "PLAYWRIGHT_CHROMIUM_EXECUTABLE";

    public E2EAppFactory App { get; } = new();

    /// <summary>The browser of <see cref="E2ESettings.DefaultLocale"/> (English); other languages: <see cref="BrowserForAsync"/>.</summary>
    public IBrowser Browser { get; private set; } = default!;

    private IPlaywright? _playwright;
    private readonly Dictionary<string, Task<IBrowser>> _browsers = new(StringComparer.OrdinalIgnoreCase);

    // Test classes start in parallel: install the browser only once per test run
    private static readonly Lazy<Task> BrowserInstall = new(() => Task.Run(() =>
    {
        var exitCode = Microsoft.Playwright.Program.Main(new[] { "install", "chromium" });
        if (exitCode != 0)
            throw new InvalidOperationException(
                $"Playwright could not install Chromium (exit code {exitCode}). " +
                $"Install it manually or set {ChromiumExecutableEnvVar} to a Chromium binary.");
    }));

    public async Task InitializeAsync()
    {
        await App.InitializeAsync();

        var executable = Environment.GetEnvironmentVariable(ChromiumExecutableEnvVar);
        if (string.IsNullOrWhiteSpace(executable))
            await BrowserInstall.Value;

        _playwright = await Playwright.CreateAsync();
        Browser = await BrowserForAsync(E2ESettings.DefaultLocale);
    }

    /// <summary>
    /// The browser for contexts in <paramref name="locale"/>, launched on first use with <c>--accept-lang=</c> that
    /// language. A context's <see cref="BrowserNewContextOptions.Locale"/> sets <c>Accept-Language</c> on its HTTP
    /// requests, but the Chromium of Playwright 1.52 (136) does not apply it to WebSocket handshakes: its headless shell
    /// sends none there (full Chromium: the host's language), so the <c>/_blazor</c> connection — whose
    /// <c>Accept-Language</c> picks the circuit's culture — had none, and every circuit was German (no header =
    /// <c>AppCultures.Default</c>) while the prerendered page was English. Real browsers send their language there;
    /// the browser-wide switch makes the test browser do the same. Newer Chromium builds (153, Chrome 157) apply the
    /// context's locale themselves, the switch then changes nothing.
    /// </summary>
    public Task<IBrowser> BrowserForAsync(string locale)
    {
        lock (_browsers)
        {
            if (!_browsers.TryGetValue(locale, out var browser))
                _browsers[locale] = browser = LaunchAsync(locale);
            return browser;
        }
    }

    private Task<IBrowser> LaunchAsync(string locale)
    {
        var executable = Environment.GetEnvironmentVariable(ChromiumExecutableEnvVar);
        return _playwright!.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            ExecutablePath = string.IsNullOrWhiteSpace(executable) ? null : executable,
            Args = [$"--accept-lang={locale}"]
        });
    }

    /// <summary>
    /// A browser context with <paramref name="options"/>; without a <see cref="BrowserNewContextOptions.Locale"/> it gets
    /// <see cref="E2ESettings.DefaultLocale"/> (English), so no test depends on the host's default language. It runs in
    /// the browser of its language (<see cref="BrowserForAsync"/>). Requests to other hosts are blocked as in every test
    /// context (<see cref="BlockExternalRequestsAsync"/>).
    /// </summary>
    public async Task<IBrowserContext> NewBrowserContextAsync(BrowserNewContextOptions options)
    {
        options.Locale ??= E2ESettings.DefaultLocale;
        var context = await (await BrowserForAsync(options.Locale)).NewContextAsync(options);
        await BlockExternalRequestsAsync(context);
        return context;
    }

    /// <summary>
    /// Aborts every request to another host than the app, unless <see cref="E2ESettings.AllowExternalRequestsEnvVar"/> is
    /// set: the tests run offline and deterministically, and a request the app should not make fails visibly (the app makes
    /// none since AP15b: Tailwind is css/tailwind.css, no longer the Play CDN). The page's <c>Request</c> event still sees
    /// the attempt, which is what <c>LocalAssetsTests</c> checks.
    /// </summary>
    public async Task BlockExternalRequestsAsync(IBrowserContext context)
    {
        if (E2ESettings.AllowExternalRequests)
            return;
        var appOrigin = new Uri(App.ServerAddress).GetLeftPart(UriPartial.Authority);
        await context.RouteAsync("**/*", route =>
            route.Request.Url.StartsWith(appOrigin, StringComparison.OrdinalIgnoreCase)
                ? route.ContinueAsync()
                : route.AbortAsync());
    }

    public async Task DisposeAsync()
    {
        List<Task<IBrowser>> browsers;
        lock (_browsers)
            browsers = _browsers.Values.ToList();
        foreach (var browser in browsers)
        {
            try { await (await browser).DisposeAsync(); }
            catch (PlaywrightException) { } // a launch that failed has nothing to close
        }
        _playwright?.Dispose();
        await App.DisposeAsync(); // stops the server and deletes the SQLite file
    }
}
