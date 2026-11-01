using Microsoft.Playwright;

namespace SpeiseDirekt3.E2ETests.Infrastructure;

/// <summary>
/// Class fixture: one app server, one SQLite file and one browser per test class.
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
    public IBrowser Browser { get; private set; } = default!;
    private IPlaywright? _playwright;

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
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            ExecutablePath = string.IsNullOrWhiteSpace(executable) ? null : executable
        });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
            await Browser.DisposeAsync();
        _playwright?.Dispose();
        await App.DisposeAsync(); // stops the server and deletes the SQLite file
    }
}
