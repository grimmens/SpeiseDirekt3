using Microsoft.Playwright;

namespace SpeiseDirekt3.E2ETests.Infrastructure;

/// <summary>
/// Gives every test a fresh browser context (own cookies/storage) on the class fixture's server.
/// Requests to other hosts (Tailwind/Bootstrap CDNs, …) are blocked so the tests run offline and deterministically.
/// </summary>
public abstract class E2ETestBase : IClassFixture<E2EFixture>, IAsyncLifetime
{
    protected E2EFixture Fixture { get; }
    protected IBrowserContext Context { get; private set; } = default!;
    protected IPage Page { get; private set; } = default!;

    private static readonly System.Text.RegularExpressions.Regex ComplexMenuModule =
        new(@"^/js/complex-menu(\.[a-z0-9]+)?\.js$");

    protected E2ETestBase(E2EFixture fixture) => Fixture = fixture;

    public async Task InitializeAsync()
    {
        Context = await Fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            BaseURL = Fixture.App.ServerAddress
        });
        Context.SetDefaultTimeout(15_000);

        var appOrigin = new Uri(Fixture.App.ServerAddress).GetLeftPart(UriPartial.Authority);
        await Context.RouteAsync("**/*", route =>
            route.Request.Url.StartsWith(appOrigin, StringComparison.OrdinalIgnoreCase)
                ? route.ContinueAsync()
                : route.AbortAsync());

        Page = await Context.NewPageAsync();
    }

    public async Task DisposeAsync() => await Context.DisposeAsync();

    /// <summary>
    /// Opens the public menu page and waits until the Blazor circuit is interactive.
    /// <c>ComplexMenuDisplayComp</c> imports <c>js/complex-menu.js</c> in its first interactive render,
    /// so that response marks the point from which clicks are handled. The URL may be fingerprinted
    /// (<c>complex-menu.{hash}.js</c>).
    /// </summary>
    protected async Task OpenMenuAsync(Guid menuId)
    {
        var interactive = Page.WaitForResponseAsync(r =>
            ComplexMenuModule.IsMatch(new Uri(r.Url).AbsolutePath));
        await Page.GotoAsync($"/menu/{menuId}");
        await interactive;
    }

    /// <summary>The item overlay (fixed backdrop) that opens when a guest clicks an item.</summary>
    protected ILocator Overlay => Page.Locator("div.fixed.inset-0.z-\\[9999\\]");

    /// <summary>Clicks a menu item card in the category list by its name.</summary>
    protected Task ClickMenuItemAsync(string itemName) =>
        Page.Locator("h3", new PageLocatorOptions { HasTextString = itemName }).First.ClickAsync();
}
