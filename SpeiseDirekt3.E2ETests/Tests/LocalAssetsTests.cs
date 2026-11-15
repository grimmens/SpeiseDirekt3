using FluentAssertions;
using Microsoft.Playwright;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Fonts, icons and Chart.js are served by the app itself. The only remaining external dependency is
/// the Tailwind Play CDN (cdn.tailwindcss.com).
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
    public async Task PublicMenuPage_LoadsNothingExternalButTailwind_AndUsesSelfHostedFonts()
    {
        var external = RecordExternalRequests(Page);

        await OpenMenuAsync(E2ESeedData.RamenMenuId);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        ExpectOnlyTailwindCdn(external);
        (await Page.EvaluateAsync<bool>("async () => { await document.fonts.load('16px Inter'); return document.fonts.check('16px Inter'); }"))
            .Should().BeTrue("Inter is served from wwwroot/fonts");
        (await Page.EvaluateAsync<bool>("async () => (await document.fonts.load('700 16px \"Playfair Display\"')).length > 0"))
            .Should().BeTrue("theme fonts are self-hosted as well");
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
