using FluentAssertions;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Fonts, icons, Chart.js, FullCalendar and Tailwind (AP15b: css/tailwind.css, built by tools/tailwind instead of the Play
/// CDN) are served by the app itself, so no page loads anything from another host. The back office loads the Tailwind
/// CSS; the guest pages, the landing page and the anonymous account pages do not — only their own CSS/JS, images and fonts.
/// </summary>
public class LocalAssetsTests : QrCalendarTestBase
{
    public LocalAssetsTests(E2EFixture fixture) : base(fixture) { }

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

    private static void ExpectNothingExternal(List<string> external) =>
        external.Should().BeEmpty("Tailwind, fonts, icons, Bootstrap and Chart.js come from the app, not from a CDN");

    [Fact]
    public async Task GuestPage_LoadsNothingExternal_OnlyItsOwnAssets_AndSelfHostedFonts()
    {
        var external = RecordExternalRequests(Page);
        var local = new List<string>();
        Page.Request += (_, r) => { lock (local) local.Add(new Uri(r.Url).AbsolutePath); };

        await OpenMenuAsync(E2ESeedData.RamenMenuId);
        await ClickMenuItemAsync("Tokyo Ramen");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        external.Should().BeEmpty("guest pages make no external request at all");
        // None of the back-office assets
        local.Should().NotContain(p => p.Contains("bootstrap") || p.Contains("chartjs") || p.Contains("speise.js")
            || p.Contains("analytics") || p.Contains("blazor.web") || p.Contains("tailwind") || p.Contains("app.css")
            || p.Contains("fullcalendar") || p.Contains("shell.") || p.Contains("brand.css") || p.Contains("landing")
            || p.Contains("back-office") || p.Contains("sortable-list") || p.Contains("modal-dialog"));
        local.Should().Contain(p => p.StartsWith("/css/guest-menu")).And.Contain(p => p.StartsWith("/css/guest-themes"))
            .And.Contain(p => p.StartsWith("/js/guest-menu"));
        await Assertions.Expect(Page.Locator("link[href*='tailwind'], script[src*='tailwind'], link[href*='bootstrap']")).ToHaveCountAsync(0);

        (await Page.EvaluateAsync<bool>("async () => { await document.fonts.load('16px Inter'); return document.fonts.check('16px Inter'); }"))
            .Should().BeTrue("Inter is served from wwwroot/fonts");
        (await Page.EvaluateAsync<bool>("async () => (await document.fonts.load('700 16px \"Playfair Display\"')).length > 0"))
            .Should().BeTrue("theme fonts are self-hosted as well");
    }

    [Fact]
    public async Task LandingPage_LoadsNothingExternal_OnlyItsOwnAssets_AndSelfHostedFonts()
    {
        var external = RecordExternalRequests(Page);
        var local = new List<string>();
        Page.Request += (_, r) => { lock (local) local.Add(new Uri(r.Url).AbsolutePath); };

        await Page.GotoAsync("/");
        await Page.Locator("html[data-landing-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        // Every lazy image too
        await Page.EvaluateAsync("async () => { for (const i of document.images) i.loading = 'eager'; await Promise.all([...document.images].filter(i => i.checkVisibility()).map(i => i.decode().catch(() => {}))); }");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        external.Should().BeEmpty("the landing page makes no external request at all");
        // None of the back-office or guest-page assets
        local.Should().NotContain(p => p.Contains("bootstrap") || p.Contains("chartjs") || p.Contains("speise.js")
            || p.StartsWith("/js/analytics") || p.Contains("blazor.web") || p.Contains("tailwind") || p.Contains("app.css")
            || p.Contains("fullcalendar") || p.Contains("shell.") || p.StartsWith("/css/guest-") || p.StartsWith("/js/guest-")
            || p.Contains("_content/") || p.Contains("back-office") || p.Contains("sortable-list"));
        local.Should().Contain(p => p.StartsWith("/css/landing")).And.Contain(p => p.StartsWith("/css/brand"))
            .And.Contain(p => p.StartsWith("/js/landing")).And.Contain(p => p.StartsWith("/images/landing/"));
        await Assertions.Expect(Page.Locator("link[href*='tailwind'], script[src*='tailwind'], link[href*='bootstrap'], script[src*='blazor']")).ToHaveCountAsync(0);

        (await Page.EvaluateAsync<bool>("async () => (await document.fonts.load('800 32px Manrope')).length > 0 && (await document.fonts.load('16px Inter')).length > 0"))
            .Should().BeTrue("Manrope and Inter are served from wwwroot/fonts");
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
    public async Task BackOffice_LoadsNothingExternal_TailwindIsLocalFingerprintedAndLast_InfoIconIsInlineSvg()
    {
        await RegisterNewOwnerAsync();
        var external = RecordExternalRequests(Page);
        var tailwind = new List<IResponse>();
        Page.Response += (_, r) => { if (new Uri(r.Url).AbsolutePath.StartsWith("/css/tailwind.")) lock (tailwind) tailwind.Add(r); };

        await GotoInteractiveAsync("/menu/edit");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        ExpectNothingExternal(external);
        await Assertions.Expect(Page.Locator("script[src*='tailwind']")).ToHaveCountAsync(0);
        (await Page.EvaluateAsync<bool>("() => typeof window.tailwind === 'undefined'")).Should().BeTrue("the Play CDN script is gone");

        // One fingerprinted stylesheet, the last one in <head>: where the Play CDN appended its <style>, so it still wins
        // over app.css, the scoped styles, tailwind-overrides.css, shell.css and back-office.css at equal specificity
        var link = Page.Locator("head link[rel=stylesheet][href^='css/tailwind.']");
        await Assertions.Expect(link).ToHaveCountAsync(1);
        (await link.GetAttributeAsync("href")).Should().MatchRegex(@"^css/tailwind\.[a-z0-9]+\.css$", "the URL carries the content hash");
        var sheets = await Page.EvaluateAsync<string[]>("() => [...document.head.querySelectorAll('link[rel=stylesheet], style')].map(e => e.getAttribute('href') ?? 'style')");
        sheets.Should().Contain(h => h.StartsWith("css/back-office."));
        sheets.Last().Should().Be(await link.GetAttributeAsync("href"), "Tailwind comes after every other stylesheet");

        var response = tailwind.Should().ContainSingle().Subject;
        response.Status.Should().Be(200);
        (await response.HeaderValueAsync("content-type")).Should().StartWith("text/css");
        var css = await response.TextAsync();
        // Static asset endpoint: an ETag is the content's SHA-256 (strong, or weak next to the gzip file's own); production
        // also sends "immutable" for the fingerprinted URL, the E2E environment serves the build's assets with no-cache
        var sha256 = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(await response.BodyAsync()));
        (await response.HeaderValueAsync("etag")).Should().Contain($"\"{sha256}\"");
        css.Should().StartWith("/*! Generated by tools/tailwind/build.sh").And.Contain("tailwindcss v3.4.17 | MIT License");

        // The config is the brand's (css/brand.css): brand palette, brand-tinted grays, the card shadow, responsive variants
        (await Page.EvaluateAsync<string[]>("""
            () => {
                const el = document.createElement('div');
                el.className = 'bg-brand-600 text-gray-500 shadow-sm rounded-xl hidden lg:block';
                document.body.appendChild(el);
                const s = getComputedStyle(el);
                const values = [s.backgroundColor, s.color, s.borderTopLeftRadius, s.display, String(s.boxShadow !== 'none')];
                el.remove();
                return values;
            }
            """)).Should().Equal("rgb(34, 107, 122)", "rgb(89, 102, 110)", "12px", "block", "true");

        // InfoPopoverComp: the former Bootstrap Icons font glyph is now an inline SVG
        await Assertions.Expect(Page.Locator("[data-bs-toggle=popover] svg").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AccountPages_LoadNoTailwind_ExceptTheAccountSettingsOfTheBackOffice()
    {
        var requests = new List<string>();
        Page.Request += (_, r) => { lock (requests) requests.Add(new Uri(r.Url).AbsolutePath); };

        foreach (var url in new[] { "/Account/Login", "/Account/Register", "/Account/ForgotPassword" })
        {
            await Page.GotoAsync(url);
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
            await Assertions.Expect(Page.Locator("html[data-auth-page]")).ToHaveCountAsync(1);
            await Assertions.Expect(Page.Locator("link[href*='tailwind'], script[src*='tailwind']")).ToHaveCountAsync(0);
        }
        requests.Should().NotContain(p => p.Contains("tailwind"), "the anonymous account pages have their own css/auth.css");

        // The account settings (/Account/Manage) are a back-office page and use it
        await RegisterNewOwnerAsync();
        await Page.GotoAsync("/Account/Manage");
        await Assertions.Expect(Page.Locator("link[rel=stylesheet][href^='css/tailwind.']")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task AnalyticsPage_ChartJsAndDateAdapterAreLocal_AndWork()
    {
        await RegisterNewOwnerAsync();
        var external = RecordExternalRequests(Page);

        await GotoInteractiveAsync("/analytics");
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        ExpectNothingExternal(external);
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

        // An existing calendar QR code: served by the app, nothing external
        calendarFiles.Clear();
        await GotoInteractiveAsync($"/qrcode/edit/{calendarQr}");
        await WaitForCalendarAsync(Page);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        calendarFiles.Should().ContainSingle(f => f.Path.StartsWith("/lib/fullcalendar/index.global.min")).Which.Status.Should().Be(200);
        ExpectNothingExternal(external);

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

    [Fact]
    public async Task SortScript_IsLocal_LoadedOnlyWhereAListCanBeSorted_NeverOnGuestPages()
    {
        var ownerId = await RegisterOwnerAsync();
        var menu = await SeedMenuAsync(ownerId, "Sortier Menü");
        var external = RecordExternalRequests(Page);
        var files = new List<(string Path, int Status)>();
        Page.Response += (_, r) =>
        {
            var path = new Uri(r.Url).AbsolutePath;
            if (path.Contains("sortable", StringComparison.OrdinalIgnoreCase))
                lock (files) files.Add((path, r.Status));
        };

        // Pages without a sortable list: not loaded
        foreach (var url in new[] { "/dashboard", "/menus", $"/allergens/{menu}", $"/combos/{menu}" })
        {
            await GotoInteractiveAsync(url);
            await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        }
        files.Should().BeEmpty("only pages with a sortable list load the drag-and-drop script");

        // The categories page loads it, from the app (no third-party library)
        await GotoInteractiveAsync($"/Categories/{menu}");
        await Assertions.Expect(Page.GetByTestId("sort-handle")).ToHaveCountAsync(1);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        files.Should().Contain(f => f.Path.StartsWith("/js/sortable-list") && f.Status == 200);
        ExpectNothingExternal(external);

        // The guest page of that menu: none of it
        var (guestContext, guest) = await NewContextAsync();
        await using var _ = guestContext;
        var guestFiles = new List<string>();
        guest.Request += (_, r) => { lock (guestFiles) guestFiles.Add(new Uri(r.Url).AbsolutePath); };
        await OpenGuestPageAsync(guest, $"/menu/{menu}");
        await guest.WaitForLoadStateAsync(LoadState.NetworkIdle);
        guestFiles.Should().NotContain(p => p.Contains("sortable") || p.Contains("back-office"));
        await Assertions.Expect(guest.Locator("[data-sortable-handle], [data-sortable]")).ToHaveCountAsync(0);
    }
}
