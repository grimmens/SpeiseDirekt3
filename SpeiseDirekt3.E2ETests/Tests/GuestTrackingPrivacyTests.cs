using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Guest analytics without cookie, IP address or user agent (AP15a): the guest pages (<c>/qr/{id}</c>, <c>/menu/{id}</c>,
/// <c>/demo</c>) set no cookie at all, and a view or item click is stored under the guest's daily visitor key
/// (<c>IVisitorKeyService</c>: HMAC over tenant, client address and user agent, in-memory salt replaced at local
/// midnight) — the same for the page request and the clicks of one browser on one day, another one for another user
/// agent, another tenant or after the salt changed. The analytics page still counts views, clicks and sessions.
/// </summary>
public class GuestTrackingPrivacyTests : QrCalendarTestBase
{
    public GuestTrackingPrivacyTests(E2EFixture fixture) : base(fixture) { }

    private static readonly Regex VisitorKey = new("^[0-9a-f]{16}$");

    /// <summary>A guest's browser context (1280 px) with its own <paramref name="userAgent"/>; nothing leaves the app.</summary>
    private async Task<(IBrowserContext Context, IPage Page)> GuestAsync(string? userAgent = null)
    {
        var context = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, ViewportSize = new ViewportSize { Width = 1280, Height = 900 }, UserAgent = userAgent
        });
        context.SetDefaultTimeout(15_000);
        var origin = new Uri(App.ServerAddress).GetLeftPart(UriPartial.Authority);
        await context.RouteAsync("**/*", r => r.Request.Url.StartsWith(origin, StringComparison.OrdinalIgnoreCase) ? r.ContinueAsync() : r.AbortAsync());
        return (context, await context.NewPageAsync());
    }

    /// <summary>A menu with one item ("{name} Gericht") and a direct QR code for it, in the tenant <paramref name="ownerId"/>.</summary>
    private async Task<(Guid MenuId, Guid QrId)> SeedMenuWithQrAsync(Guid ownerId, string name)
    {
        var menuId = await SeedMenuAsync(ownerId, name);
        var qrId = await SeedQrAsync(ownerId, new QRCode { Title = $"{name} QR", MenuId = menuId });
        return (menuId, qrId);
    }

    private async Task<List<string>> ViewKeysAsync(Guid qrId)
    {
        await using var db = App.CreateDbContext(Guid.Empty);
        return await db.MenuViews.Where(v => v.QRCodeId == qrId).OrderBy(v => v.ViewedAt).Select(v => v.SessionId).ToListAsync();
    }

    private async Task<List<string>> ClickKeysAsync(Guid menuId)
    {
        await using var db = App.CreateDbContext(Guid.Empty);
        return await db.MenuItemClicks.Where(c => c.MenuId == menuId).OrderBy(c => c.ClickedAt).Select(c => c.SessionId).ToListAsync();
    }

    private static async Task<List<T>> EventuallyAsync<T>(Func<Task<List<T>>> read, int count)
    {
        var rows = await read();
        for (var i = 0; i < 50 && rows.Count < count; i++)
        {
            await Task.Delay(100);
            rows = await read();
        }
        rows.Should().HaveCount(count);
        return rows;
    }

    [Fact]
    public async Task GuestPages_SetNoCookie_ViewAndClicksShareTheDailyVisitorKey()
    {
        var (menuId, qrId) = await SeedMenuWithQrAsync(E2ESeedData.OwnerId, "Datenschutzmenü");
        var (context, guest) = await GuestAsync();
        await using var _ = context;
        var setCookies = new List<string>();
        guest.Response += async (_, response) =>
        {
            foreach (var header in await response.HeadersArrayAsync())
                if (header.Name.Equals("set-cookie", StringComparison.OrdinalIgnoreCase))
                    lock (setCookies) setCookies.Add($"{response.Url}: {header.Value}");
        };

        // A scan records a view, opening an item a click (POST /guest/api/item-click), a deep link another click
        await OpenGuestPageAsync(guest, $"/qr/{qrId}");
        var view = (await EventuallyAsync(() => ViewKeysAsync(qrId), 1)).Single();
        await ClickMenuItemAsync(guest, "Datenschutzmenü Gericht");
        await EventuallyAsync(() => ClickKeysAsync(menuId), 1);
        await OpenGuestPageAsync(guest, $"/menu/{menuId}");
        Guid itemId;
        await using (var db = App.CreateDbContext(Guid.Empty))
            itemId = await db.MenuItems.IgnoreQueryFilters().Where(i => i.Category!.MenuId == menuId).Select(i => i.Id).SingleAsync();
        await OpenGuestPageAsync(guest, $"/menu/{menuId}?item={itemId}");
        var clicks = await EventuallyAsync(() => ClickKeysAsync(menuId), 2);
        await OpenGuestPageAsync(guest, "/demo");
        await guest.WaitForLoadStateAsync(LoadState.NetworkIdle);

        // The key: 16 hex characters, not a GUID of a cookie; the same for the scan, the click and the deep link
        view.Should().MatchRegex(VisitorKey);
        Guid.TryParse(view, out var parsed).Should().BeFalse("the key is no cookie value ({0})", parsed);
        clicks.Should().AllBe(view, "the same browser on the same day is the same visitor");

        // No cookie at all: none in the browser, no Set-Cookie on any response of these pages
        (await context.CookiesAsync()).Should().BeEmpty();
        setCookies.Should().BeEmpty();
        var direct = await guest.APIRequest.GetAsync($"/qr/{qrId}");
        direct.Headers.Keys.Should().NotContain("set-cookie");
    }

    [Fact]
    public async Task AnotherUserAgent_OrAnotherTenant_GetsAnotherKey()
    {
        var (menuId, qrId) = await SeedMenuWithQrAsync(E2ESeedData.OwnerId, "Agentenmenü");
        var otherTenant = Guid.NewGuid();
        var (otherMenuId, otherQrId) = await SeedMenuWithQrAsync(otherTenant, "Nachbarmenü");

        var (firstContext, first) = await GuestAsync();
        await using var _ = firstContext;
        var (secondContext, second) = await GuestAsync("Mozilla/5.0 (X11; Linux aarch64) E2E-OtherBrowser/1.0");
        await using var __ = secondContext;

        await OpenGuestPageAsync(first, $"/qr/{qrId}");
        await EventuallyAsync(() => ViewKeysAsync(qrId), 1);
        await OpenGuestPageAsync(second, $"/qr/{qrId}");
        var keys = await EventuallyAsync(() => ViewKeysAsync(qrId), 2);
        keys.Should().OnlyContain(k => VisitorKey.IsMatch(k));
        keys[1].Should().NotBe(keys[0], "another user agent on the same address is another visitor");

        // The same browser at another restaurant: not linkable to its visit of the first one
        await OpenGuestPageAsync(first, $"/qr/{otherQrId}");
        await ClickMenuItemAsync(first, "Nachbarmenü Gericht");
        var other = (await EventuallyAsync(() => ViewKeysAsync(otherQrId), 1)).Single();
        other.Should().MatchRegex(VisitorKey).And.NotBe(keys[0], "the tenant is part of the key");
        (await EventuallyAsync(() => ClickKeysAsync(otherMenuId), 1)).Single().Should().Be(other);
        (await ClickKeysAsync(menuId)).Should().BeEmpty();
    }

    [Fact]
    public async Task TheKey_StaysTheWholeDay_AndChangesWhenTheSaltRotatesAtMidnight()
    {
        var (_, qrId) = await SeedMenuWithQrAsync(E2ESeedData.OwnerId, "Mitternachtsmenü");
        var (context, guest) = await GuestAsync();
        await using var _ = context;

        try
        {
            App.Clock.SetLocalNow(new DateTime(2026, 11, 20, 0, 5, 0));
            await OpenGuestPageAsync(guest, $"/qr/{qrId}");
            await EventuallyAsync(() => ViewKeysAsync(qrId), 1);
            App.Clock.SetLocalNow(new DateTime(2026, 11, 20, 23, 59, 0));
            await OpenGuestPageAsync(guest, $"/qr/{qrId}");
            await EventuallyAsync(() => ViewKeysAsync(qrId), 2);
            App.Clock.SetLocalNow(new DateTime(2026, 11, 21, 0, 0, 0));
            await OpenGuestPageAsync(guest, $"/qr/{qrId}");
            var keys = await EventuallyAsync(() => ViewKeysAsync(qrId), 3);

            keys.Should().OnlyContain(k => VisitorKey.IsMatch(k));
            keys[1].Should().Be(keys[0], "one salt for the whole local day");
            keys[2].Should().NotBe(keys[0], "a new salt at local midnight");
        }
        finally
        {
            App.Clock.Reset();
        }
    }

    [Fact]
    public async Task Analytics_StillCountsViewsClicksAndSessions()
    {
        var ownerId = await RegisterOwnerAsync();
        var (menuId, qrId) = await SeedMenuWithQrAsync(ownerId, "Statistikmenü");

        // Guest 1 scans and opens a dish; guest 2 (another browser) scans twice
        var (firstContext, first) = await GuestAsync();
        await using var _ = firstContext;
        await OpenGuestPageAsync(first, $"/qr/{qrId}");
        await ClickMenuItemAsync(first, "Statistikmenü Gericht");
        await EventuallyAsync(() => ClickKeysAsync(menuId), 1);
        var (secondContext, second) = await GuestAsync("Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) E2E/1.0");
        await using var __ = secondContext;
        await OpenGuestPageAsync(second, $"/qr/{qrId}");
        await OpenGuestPageAsync(second, $"/qr/{qrId}");
        await EventuallyAsync(() => ViewKeysAsync(qrId), 3);

        await GotoInteractiveAsync("/analytics");
        var summary = Page.GetByTestId("analytics-summary");
        await Assertions.Expect(summary.GetByTestId("summary-views").GetByTestId("summary-value")).ToHaveTextAsync("3");
        await Assertions.Expect(summary.GetByTestId("summary-clicks").GetByTestId("summary-value")).ToHaveTextAsync("1");
        await Assertions.Expect(summary.GetByTestId("summary-sessions").GetByTestId("summary-value")).ToHaveTextAsync("2");
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Sessions", Exact = true })).ToBeVisibleAsync();
    }
}
