using FluentAssertions;
using Microsoft.Playwright;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Fonts, icons and Chart.js are served by the app itself. The back office still loads the Tailwind Play CDN
/// (cdn.tailwindcss.com); the guest pages load nothing external at all — only their own CSS/JS and fonts.
/// </summary>
public class LocalAssetsTests : BackOfficeTestBase
{
    public LocalAssetsTests(E2EFixture fixture) : base(fixture) { }

    private const string AllowedExternalHost = "cdn.tailwindcss.com";

    /// <summary>Collects every request to another host than the app (the context blocks them anyway).</summary>
    private List<string> RecordExternalRequests(IPage page)
    {
        var appOrigin = new Uri(App.ServerAddress).GetLeftPart(UriPartial.Authority);
        var external = new List<string>();
        page.Request += (_, r) =>
        {
            if (!r.Url.StartsWith(appOrigin, StringComparison.OrdinalIgnoreCase) && !r.Url.StartsWith("data:"))
                lock (external) external.Add(r.Url);
        };
        return external;
    }

    private static void ExpectOnlyTailwindCdn(List<string> external) =>
        external.Should().OnlyContain(url => new Uri(url).Host == AllowedExternalHost,
            "fonts, icons, Bootstrap and Chart.js must come from the app, not from a CDN");

    [Fact]
    public async Task GuestPage_LoadsNothingExternal_OnlyItsOwnAssets_AndSelfHostedFonts()
    {
        var external = RecordExternalRequests(Page);
        var local = new List<string>();
        Page.Request += (_, r) => { lock (local) local.Add(new Uri(r.Url).AbsolutePath); };

        await OpenMenuAsync(E2ESeedData.RamenMenuId);
        await ClickMenuItemAsync("Tokyo Ramen");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        external.Should().BeEmpty("guest pages make no external request at all (no Tailwind CDN)");
        // None of the back-office assets
        local.Should().NotContain(p => p.Contains("bootstrap") || p.Contains("chartjs") || p.Contains("speise.js")
            || p.Contains("analytics") || p.Contains("blazor.web") || p.Contains("tailwind") || p.Contains("app.css"));
        local.Should().Contain(p => p.StartsWith("/css/guest-menu")).And.Contain(p => p.StartsWith("/css/guest-themes"))
            .And.Contain(p => p.StartsWith("/js/guest-menu"));
        await Assertions.Expect(Page.Locator("script[src*='tailwindcss'], link[href*='bootstrap']")).ToHaveCountAsync(0);

        (await Page.EvaluateAsync<bool>("async () => { await document.fonts.load('16px Inter'); return document.fonts.check('16px Inter'); }"))
            .Should().BeTrue("Inter is served from wwwroot/fonts");
        (await Page.EvaluateAsync<bool>("async () => (await document.fonts.load('700 16px \"Playfair Display\"')).length > 0"))
            .Should().BeTrue("theme fonts are self-hosted as well");
    }

    [Fact]
    public async Task GuestNoticePage_LoadsNothingExternal()
    {
        var external = RecordExternalRequests(Page);

        await OpenGuestPageAsync(Page, $"/qr/{Guid.NewGuid()}");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Assertions.Expect(Page.GetByTestId("guest-notice")).ToBeVisibleAsync();
        external.Should().BeEmpty();
    }

    [Fact]
    public async Task BackOffice_LoadsNothingExternalButTailwind_InfoIconIsInlineSvg()
    {
        await RegisterNewOwnerAsync();
        var external = RecordExternalRequests(Page);

        await GotoInteractiveAsync("/menu/edit");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        ExpectOnlyTailwindCdn(external);
        // InfoPopoverComp: the former Bootstrap Icons font glyph is now an inline SVG
        await Assertions.Expect(Page.Locator("[data-bs-toggle=popover] svg").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AnalyticsPage_ChartJsAndDateAdapterAreLocal_AndWork()
    {
        await RegisterNewOwnerAsync();
        var external = RecordExternalRequests(Page);

        await GotoInteractiveAsync("/analytics");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        ExpectOnlyTailwindCdn(external);
        (await Page.EvaluateAsync<string>("() => window.Chart?.version")).Should().Be("4.6.1");
        // A time scale needs the date-fns adapter; without it Chart.js throws "This method is not implemented"
        (await Page.EvaluateAsync<string>("""
            () => {
                const canvas = document.createElement('canvas');
                document.body.appendChild(canvas);
                try {
                    const chart = new Chart(canvas, {
                        type: 'line',
                        data: { datasets: [{ data: [{ x: '2026-11-01', y: 1 }, { x: '2026-11-02', y: 2 }] }] },
                        options: { animation: false, scales: { x: { type: 'time', time: { unit: 'day' } } } }
                    });
                    chart.destroy();
                    return 'ok';
                } catch (e) { return e.message; } finally { canvas.remove(); }
            }
            """)).Should().Be("ok");
    }
}
