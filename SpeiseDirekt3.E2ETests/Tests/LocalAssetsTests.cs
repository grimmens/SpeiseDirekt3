using FluentAssertions;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Fonts, icons, Chart.js and FullCalendar are served by the app itself. The back office still loads the Tailwind Play
/// CDN (cdn.tailwindcss.com); the guest pages load nothing external at all — only their own CSS/JS and fonts.
/// </summary>
public class LocalAssetsTests : QrCalendarTestBase
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
            || p.Contains("analytics") || p.Contains("blazor.web") || p.Contains("tailwind") || p.Contains("app.css")
            || p.Contains("fullcalendar"));
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

    [Fact]
    public async Task FullCalendar_IsLocal_AndLoadedOnlyWhereACalendarIsShown()
    {
        var ownerId = await RegisterOwnerAsync();
        var menu = await SeedMenuAsync(ownerId, "Kalender Menü");
        var directQr = await SeedQrAsync(ownerId, new QRCode { Title = "Direkt", MenuId = menu });
        var calendarQr = await SeedQrAsync(ownerId, new QRCode
        {
            Title = "Kalender", IsCalendarBased = true, MenuId = menu,
            CalendarEntries = new List<CalendarEntry> { Calendar(new DateOnly(2026, 12, 24), menu) }
        });
        var external = RecordExternalRequests(Page);
        var calendarFiles = new List<(string Path, int Status)>();
        Page.Response += (_, r) =>
        {
            var path = new Uri(r.Url).AbsolutePath;
            if (path.Contains("fullcalendar", StringComparison.OrdinalIgnoreCase))
                lock (calendarFiles) calendarFiles.Add((path, r.Status));
        };
        async Task ExpectNotLoadedAsync(string page)
        {
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            (await Page.EvaluateAsync<bool>("() => typeof window.FullCalendar === 'undefined'")).Should().BeTrue($"{page} shows no calendar");
            calendarFiles.Should().NotContain(f => f.Path.StartsWith("/lib/fullcalendar/"), $"{page} shows no calendar");
        }

        // Pages without a calendar: neither the bundle nor the interop module
        foreach (var url in new[] { "/qrcodes", "/menu/edit", $"/qrcode/edit/{directQr}", "/qrcode/edit" })
        {
            await GotoInteractiveAsync(url);
            await ExpectNotLoadedAsync(url);
        }
        calendarFiles.Should().BeEmpty();

        // The new QR code's editor loads it lazily once the calendar mode is picked
        await SelectCalendarModeAsync();
        calendarFiles.Should().Contain(f => f.Path.StartsWith("/lib/fullcalendar/index.global.min") && f.Status == 200);
        calendarFiles.Should().Contain(f => f.Path.StartsWith("/js/fullcalendar-interop") && f.Status == 200);
        (await Page.EvaluateAsync<string>("() => window.FullCalendar.version")).Should().Be("6.1.22");

        // An existing calendar QR code: served by the app, nothing external but Tailwind
        calendarFiles.Clear();
        await GotoInteractiveAsync($"/qrcode/edit/{calendarQr}");
        await WaitForCalendarAsync(Page);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        calendarFiles.Should().ContainSingle(f => f.Path.StartsWith("/lib/fullcalendar/index.global.min")).Which.Status.Should().Be(200);
        ExpectOnlyTailwindCdn(external);

        // The guest page of that code loads none of it
        calendarFiles.Clear();
        App.Clock.SetLocalNow(new DateTime(2026, 12, 24, 12, 0, 0));
        var (guestContext, guest) = await NewContextAsync();
        await using var _ = guestContext;
        guest.Response += (_, r) =>
        {
            if (r.Url.Contains("fullcalendar", StringComparison.OrdinalIgnoreCase))
                lock (calendarFiles) calendarFiles.Add((new Uri(r.Url).AbsolutePath, r.Status));
        };
        await OpenGuestPageAsync(guest, $"/qr/{calendarQr}");
        await guest.WaitForLoadStateAsync(LoadState.NetworkIdle);
        calendarFiles.Should().BeEmpty();
        (await guest.EvaluateAsync<bool>("() => typeof window.FullCalendar === 'undefined'")).Should().BeTrue();
    }
}
