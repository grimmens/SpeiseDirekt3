using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.Components.Plausible;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// The page counter (AP16, Plausible CE through the same-host paths <c>/_s/script.js</c> and <c>/_s/event</c>).
/// <para>
/// <b>Off</b> (the default everywhere but production): no tag, no init script and no <c>/_s/</c> request on any document.
/// <b>On</b>: this class switches the <c>Plausible</c> section on its own server (the settings are read per request, the
/// class has its own server, so no other class sees it; every test starts with it off) and serves a <b>recording fake</b>
/// at <c>/_s/script.js</c> from a Playwright route — the real tracker is external and nothing may leave the test. The fake
/// leaves <c>plausible.q</c> (every call) and <c>plausible.o</c> (the init options) where <c>js/site.js</c> put them and
/// records pageviews by the real tracker's documented rule (one on load, one per <c>history.pushState</c>/<c>popstate</c>
/// that changes <c>location.pathname</c>), so the back office's client-side navigation can be counted. Then: every document
/// carries the tag and the init, the editor preview neither; the masking of <c>transformRequest</c>; the six conversion
/// events; nothing external and no cookie on the guest and landing pages.
/// </para>
/// </summary>
public class PlausibleTests : QrCalendarTestBase
{
    private const string ScriptPath = "/_s/script.js";
    private const string Endpoint = "/_s/event";

    /// <summary>Words ad blockers match on: never in a served file name, path or attribute of the tag.</summary>
    private static readonly string[] BlockedWords = ["plausible", "analytics", "stats", "track", "metrics"];

    /// <summary>
    /// Stand-in for the tracker. It does not replace <c>window.plausible</c> (the queue stub of <c>js/site.js</c>), so
    /// <c>plausible.q</c> keeps every call and <c>plausible.o</c> the options; it counts its loads and the pageviews the
    /// real tracker would send (<c>autoCapturePageviews</c>: load, pushState/popstate with a new path).
    /// </summary>
    private const string FakeTracker = """
        (() => {
            const counter = window.__counter = window.__counter || { loads: 0, pageviews: [] };
            counter.loads++;
            let path = null;
            const pageview = () => {
                if (location.pathname === path) return;
                path = location.pathname;
                counter.pageviews.push(location.pathname);
            };
            const pushState = history.pushState;
            history.pushState = function () {
                const result = pushState.apply(this, arguments);
                pageview();
                return result;
            };
            window.addEventListener('popstate', pageview);
            pageview();
        })();
        """;

    private int _trackerRequests;
    private readonly List<string> _posts = new();

    public PlausibleTests(E2EFixture fixture) : base(fixture) => SetCounter(null, null);

    private IConfiguration Configuration => App.ServerServices.GetRequiredService<IConfiguration>();

    private void SetCounter(string? scriptPath, string? endpoint)
    {
        Configuration["Plausible:ScriptPath"] = scriptPath ?? "";
        Configuration["Plausible:Endpoint"] = endpoint ?? "";
    }

    /// <summary>Switches the counter on (the production values) and serves the fake in <see cref="E2ETestBase.Context"/>.</summary>
    private async Task TurnOnAsync()
    {
        SetCounter(ScriptPath, Endpoint);
        await ServeFakeAsync(Context);
    }

    /// <summary>
    /// Serves the fake tracker and swallows posts to the endpoint (the fake posts nothing; a post would be recorded) in
    /// <paramref name="context"/>. Registered after the context's blocking route, so it is matched first.
    /// </summary>
    private async Task ServeFakeAsync(IBrowserContext context)
    {
        await context.RouteAsync("**/_s/script.js", route =>
        {
            Interlocked.Increment(ref _trackerRequests);
            return route.FulfillAsync(new() { Status = 200, ContentType = "text/javascript; charset=utf-8", Body = FakeTracker });
        });
        await context.RouteAsync("**/_s/event", route =>
        {
            lock (_posts) _posts.Add(route.Request.PostData ?? "");
            return route.FulfillAsync(new() { Status = 202, Body = "ok" });
        });
    }

    /// <summary>A second browser context with the fake (the counter's settings are the server's).</summary>
    private async Task<(IBrowserContext Context, IPage Page)> NewCountedContextAsync()
    {
        var (context, page) = await NewContextAsync();
        await ServeFakeAsync(context);
        return (context, page);
    }

    // --- Reading what the page handed to the counter ---

    private sealed record SentEvent(string Name, Dictionary<string, string>? Props);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Every <c>plausible(name, options)</c> call of the current document, in order.</summary>
    private static async Task<List<SentEvent>> EventsAsync(IPage page) =>
        JsonSerializer.Deserialize<List<SentEvent>>(await page.EvaluateAsync<string>(
            "() => JSON.stringify(((window.plausible && window.plausible.q) || []).map(a => ({ name: a[0], props: (a[1] && a[1].props) || null })))"),
            Json)!;

    private static async Task<List<string>> NamesAsync(IPage page) => (await EventsAsync(page)).Select(e => e.Name).ToList();

    private static Task WaitForEventAsync(IPage page, string name) =>
        page.WaitForFunctionAsync("name => ((window.plausible && window.plausible.q) || []).some(a => a[0] === name)", name);

    private static async Task<List<string>> PageviewsAsync(IPage page) =>
        (await page.EvaluateAsync<string[]>("() => (window.__counter && window.__counter.pageviews) || []")).ToList();

    /// <summary>Records the path of every request to <c>/_s/</c> and every URL of another host.</summary>
    private List<string> RecordRequests(IBrowserContext context, Func<string, bool> matches)
    {
        var urls = new List<string>();
        context.Request += (_, request) =>
        {
            if (matches(request.Url))
                lock (urls) urls.Add(request.Url);
        };
        return urls;
    }

    private Func<string, bool> IsExternal
    {
        get
        {
            var origin = new Uri(App.ServerAddress).GetLeftPart(UriPartial.Authority);
            return url => !url.StartsWith(origin, StringComparison.OrdinalIgnoreCase) && !url.StartsWith("data:", StringComparison.Ordinal);
        }
    }

    private static bool IsCounterPath(string url) => new Uri(url).AbsolutePath.StartsWith("/_s/", StringComparison.Ordinal);

    // --- Documents ---

    /// <summary>The counter is off on this document: no tag, no init script, no <c>window.plausible</c>.</summary>
    private static async Task ExpectNoCounterAsync(IPage page)
    {
        (await page.Locator("script[src*='/_s/'], script[data-endpoint], script[src*='/js/site.']").CountAsync())
            .Should().Be(0, $"{page.Url} must not carry the counter");
        (await page.EvaluateAsync<bool>("() => typeof window.plausible === 'undefined'")).Should().BeTrue(page.Url);
    }

    /// <summary>
    /// The counter is on on this document: in <c>&lt;head&gt;</c> first <c>js/site.js</c> (fingerprinted, with the endpoint),
    /// then the tracker, async; both without a word ad blockers match on; the fake loaded once, the init options set.
    /// </summary>
    private static async Task ExpectCounterAsync(IPage page)
    {
        var tags = await page.EvaluateAsync<string>("""
            () => JSON.stringify((() => {
                const init = document.head.querySelectorAll('script[data-endpoint]');
                const tracker = document.head.querySelectorAll('script[src="/_s/script.js"]');
                const all = document.querySelectorAll('script[data-endpoint], script[src*="/_s/"]');
                const attributes = [...all].flatMap(s => [...s.attributes].map(a => `${a.name}=${a.value}`));
                return {
                    init: init.length, tracker: tracker.length, all: all.length,
                    initSrc: init[0]?.getAttribute('src') ?? null,
                    endpoint: init[0]?.dataset.endpoint ?? null,
                    async: tracker[0]?.async ?? false,
                    initFirst: init.length === 1 && tracker.length === 1
                        && !!(init[0].compareDocumentPosition(tracker[0]) & Node.DOCUMENT_POSITION_FOLLOWING),
                    attributes,
                    loads: window.__counter?.loads ?? 0,
                    optionsEndpoint: window.plausible?.o?.endpoint ?? null,
                    transform: typeof window.plausible?.o?.transformRequest
                };
            })())
            """);
        using var doc = JsonDocument.Parse(tags);
        var t = doc.RootElement;
        var where = page.Url;
        t.GetProperty("init").GetInt32().Should().Be(1, where);
        t.GetProperty("tracker").GetInt32().Should().Be(1, where);
        t.GetProperty("all").GetInt32().Should().Be(2, $"{where}: only the two tags, nothing in <body>");
        t.GetProperty("initSrc").GetString().Should().MatchRegex(@"^/js/site\.[a-z0-9]+\.js$", $"{where}: our own fingerprinted file");
        t.GetProperty("endpoint").GetString().Should().Be(Endpoint, where);
        t.GetProperty("async").GetBoolean().Should().BeTrue(where);
        t.GetProperty("initFirst").GetBoolean().Should().BeTrue($"{where}: the init script runs before the tracker");
        foreach (var attribute in t.GetProperty("attributes").EnumerateArray().Select(a => a.GetString()!.ToLowerInvariant()))
            attribute.Should().NotContainAny(BlockedWords, where);
        t.GetProperty("loads").GetInt32().Should().Be(1, $"{where}: the tracker loads once");
        t.GetProperty("optionsEndpoint").GetString().Should().Be(Endpoint, where);
        t.GetProperty("transform").GetString().Should().Be("function", where);
    }

    /// <summary>A registered owner with a menu (one item) and a direct QR code for it.</summary>
    private async Task<(Guid OwnerId, Guid MenuId, Guid QrId)> OwnerWithMenuAndQrAsync(string name)
    {
        var ownerId = await RegisterOwnerAsync();
        var menuId = await SeedMenuAsync(ownerId, name);
        var qrId = await SeedQrAsync(ownerId, new QRCode { Title = $"{name} Tisch", MenuId = menuId });
        return (ownerId, menuId, qrId);
    }

    private static readonly string[] PublicPages = ["/", "/impressum", "/datenschutz", "/demo", "/demo/en"];
    private static readonly string[] AuthPages = ["/Account/Login", "/Account/Register", "/Account/ForgotPassword"];
    private static readonly string[] BackOfficePages = ["/dashboard", "/menus", "/qrcodes", "/Account/Manage"];

    private static IEnumerable<string> GuestPages(Guid menuId, Guid qrId) =>
        [$"/menu/{menuId}", $"/qr/{qrId}", $"/qr/{Guid.NewGuid()}" /* notice page: unknown code */];

    [Fact]
    public async Task Off_NoTag_NoInitScript_AndNoRequest_OnAnyDocument()
    {
        var counterRequests = RecordRequests(Context, IsCounterPath);
        var (_, menuId, qrId) = await OwnerWithMenuAndQrAsync("Aus Karte");
        foreach (var path in BackOfficePages.Append($"/menu/preview/{menuId}"))
        {
            await Page.GotoAsync(path);
            await ExpectNoCounterAsync(Page);
        }

        var (guest, guestPage) = await NewContextAsync();
        await using var _ = guest;
        var guestRequests = RecordRequests(guest, IsCounterPath);
        foreach (var path in PublicPages.Concat(GuestPages(menuId, qrId)).Concat(AuthPages))
        {
            await guestPage.GotoAsync(path);
            await ExpectNoCounterAsync(guestPage);
        }

        counterRequests.Should().BeEmpty();
        guestRequests.Should().BeEmpty();
        _trackerRequests.Should().Be(0);
    }

    [Fact]
    public async Task On_EveryDocument_CarriesTheTagAndTheInit_ButNotTheEditorPreview()
    {
        await TurnOnAsync();
        var (_, menuId, qrId) = await OwnerWithMenuAndQrAsync("An Karte");
        foreach (var path in BackOfficePages)
        {
            await Page.GotoAsync(path);
            await ExpectCounterAsync(Page);
        }

        // The editor's phone preview: neither tag nor init, and no request to /_s/ for it
        var previewRequests = RecordRequests(Context, IsCounterPath);
        await OpenGuestPageAsync(Page, $"/menu/preview/{menuId}");
        await ExpectNoCounterAsync(Page);
        previewRequests.Should().BeEmpty();

        // A guest: every public document carries it, nothing leaves the app, and the guest and landing pages set no cookie
        var (guest, guestPage) = await NewCountedContextAsync();
        await using var _ = guest;
        var external = RecordRequests(guest, IsExternal);
        var setCookies = new List<string>();
        guestPage.Response += (_, response) =>
        {
            if (response.Headers.ContainsKey("set-cookie"))
                lock (setCookies) setCookies.Add(response.Url);
        };
        foreach (var path in PublicPages.Concat(GuestPages(menuId, qrId)))
        {
            await guestPage.GotoAsync(path);
            await ExpectCounterAsync(guestPage);
        }
        (await guest.CookiesAsync()).Should().BeEmpty("the guest and landing pages set no cookie, also with the counter");
        setCookies.Should().BeEmpty();

        foreach (var path in AuthPages)
        {
            await guestPage.GotoAsync(path);
            await ExpectCounterAsync(guestPage);
        }
        external.Should().BeEmpty("the tracker and its endpoint are paths on this site");
        _posts.Should().BeEmpty("the fake posts nothing");
    }

    [Fact]
    public async Task Settings_ThatAreNotPathsOnThisSite_OrOnlyOneOfThem_SwitchItOff()
    {
        await ServeFakeAsync(Context);
        (string? Script, string? Endpoint)[] off =
        [
            (ScriptPath, null), (null, Endpoint),
            ("https://stats.example/js/script.js", Endpoint), ("//cdn.example/script.js", Endpoint),
            (ScriptPath, "https://stats.example/api/event"), (ScriptPath, "//stats.example/api/event"),
            ("/_s/script.js?x=1 2", Endpoint), (@"/\stats.example/script.js", Endpoint)
        ];
        foreach (var (script, endpoint) in off)
        {
            SetCounter(script, endpoint);
            await Page.GotoAsync("/");
            await ExpectNoCounterAsync(Page);
        }

        // Read per request: switched on, the next page has it
        SetCounter(ScriptPath, Endpoint);
        await Page.GotoAsync("/");
        await ExpectCounterAsync(Page);
    }

    [Fact]
    public void ProductionSettings_SwitchItOn_AndNoOtherEnvironmentLoadsThem()
    {
        var root = App.ServerServices.GetRequiredService<IWebHostEnvironment>().ContentRootPath;
        PlausibleSettings For(string? environment)
        {
            var builder = new ConfigurationBuilder().SetBasePath(root).AddJsonFile("appsettings.json", optional: false);
            if (environment is not null)
                builder.AddJsonFile($"appsettings.{environment}.json", optional: true);
            return PlausibleSettings.From(builder.Build());
        }

        For(null).Enabled.Should().BeFalse("appsettings.json keeps the section empty");
        foreach (var environment in new[] { "Development", SpeiseDirekt3.Program.E2EEnvironment, SpeiseDirekt3.Program.PreviewEnvironment })
            For(environment).Enabled.Should().BeFalse(environment);
        For("Production").Should().Be(new PlausibleSettings(ScriptPath, Endpoint));

        // appsettings.Production.json holds the Plausible section and nothing else
        using var production = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "appsettings.Production.json")));
        production.RootElement.EnumerateObject().Select(p => p.Name).Should().Equal("Plausible");
        production.RootElement.GetProperty("Plausible").EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("ScriptPath", "Endpoint");

        // This test server: off unless a test switches it on
        PlausibleSettings.From(Configuration).Enabled.Should().BeFalse();
    }

    // --- Masking (transformRequest) ---

    /// <summary>Runs <paramref name="payload"/> through the init options' <c>transformRequest</c>; null = dropped.</summary>
    private static async Task<JsonElement?> TransformAsync(IPage page, object payload)
    {
        var json = await page.EvaluateAsync<string>(
            "p => JSON.stringify(window.plausible.o.transformRequest(p) || null)", payload);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.ValueKind == JsonValueKind.Null ? null : doc.RootElement.Clone();
    }

    [Fact]
    public async Task TransformRequest_MasksIdsQueryHashAndOwnReferrer_AndDropsTheEditorPreview()
    {
        await TurnOnAsync();
        await Page.GotoAsync("/");
        await ExpectCounterAsync(Page);
        var site = App.ServerAddress;
        var id = "3f2a1b4c-5d6e-4f70-8192-a3b4c5d6e7f8";
        var upper = id.ToUpperInvariant();
        var compact = id.Replace("-", "");

        async Task<string?> PageUrl(string path)
        {
            var result = await TransformAsync(Page, new { n = "pageview", u = site + path, d = "app.speisedirekt.de", r = (string?)null, v = 33 });
            return result?.GetProperty("u").GetString();
        }

        var cases = new (string Path, string Masked)[]
        {
            ($"/qr/{id}", "/qr/:id"),
            ($"/menu/{id}?item={id}#cat-{id}", "/menu/:id"),
            ($"/MenuItems/item/{upper}/edit", "/MenuItems/item/:id/edit"),
            ($"/MenuItems/Category/{id}/new", "/MenuItems/Category/:id/new"),
            ($"/MenuItems/Category/{id}", "/MenuItems/Category/:id"),
            ($"/MenuItems/{id}?highlight={id}", "/MenuItems/:id"),
            ("/MenuItems/not-a-guid", "/MenuItems/:id"),
            ($"/Categories/{id}", "/Categories/:id"),
            ("/allergens/abc", "/allergens/:id"),
            ($"/combos/{id}", "/combos/:id"),
            ($"/menudetail/{id}", "/menudetail/:id"),
            ($"/menu/edit/{id}", "/menu/edit/:id"),
            ($"/qrcode/edit/{compact}", "/qrcode/edit/:id"),
            ($"/users/edit/{id}", "/users/edit/:id"),
            ("/Image/qr/42", "/Image/qr/:id"),
            ("/menu/edit", "/menu/edit"),
            ("/demo/en?theme=classic", "/demo/en"),
            ("/Account/Login?ReturnUrl=%2Fqrcodes", "/Account/Login"),
            ($"/Account/ResetPassword?code=Q2ZESjhB&email=gast%40example.com", "/Account/ResetPassword"),
            ($"/Account/ConfirmEmail?userId={id}&code=abc&returnUrl=%2F", "/Account/ConfirmEmail"),
            ("/?utm_source=newsletter&utm_medium=email&utm_campaign=herbst&utm_content=a&utm_term=b&ref=blog&source=flyer&fbclid=x&email=a%40b.c#faq",
                "/?utm_source=newsletter&utm_medium=email&utm_campaign=herbst&utm_content=a&utm_term=b&ref=blog&source=flyer"),
            ("/impressum#imprint", "/impressum")
        };
        foreach (var (path, masked) in cases)
            (await PageUrl(path)).Should().Be(site + masked, path);

        // The editor's phone preview and a page that is not on this site are dropped (a falsy result)
        (await PageUrl($"/menu/preview/{id}?theme=dark&lang=de")).Should().BeNull();
        (await PageUrl("/Menu/Preview/x")).Should().BeNull();
        (await TransformAsync(Page, new { n = "pageview", u = $"https://elsewhere.example/qr/{id}" })).Should().BeNull();

        // Referrer: this site's is dropped, another site's is kept without query and hash
        async Task<string?> Referrer(string? referrer)
        {
            var result = await TransformAsync(Page, new { n = "pageview", u = site + "/", r = referrer });
            var r = result!.Value.GetProperty("r");
            return r.ValueKind == JsonValueKind.Null ? null : r.GetString();
        }
        (await Referrer(site + "/Account/Login?ReturnUrl=%2Fdashboard")).Should().BeNull();
        (await Referrer(site + "/")).Should().BeNull();
        (await Referrer("https://www.google.com/search?q=speisekarte+qr#top")).Should().Be("https://www.google.com/search");
        (await Referrer("https://news.example/")).Should().Be("https://news.example/");
        (await Referrer("android-app://com.google.android.gm/")).Should().Be("android-app://com.google.android.gm/");
        (await Referrer(null)).Should().BeNull();

        // The tracker's own events: the link target in props.url is masked too; our events keep their props
        var outbound = await TransformAsync(Page, new
        {
            n = "Outbound Link: Click", u = site + $"/qr/{id}?item={id}", r = "https://www.instagram.com/",
            p = new { url = "https://www.instagram.com/gasthaus/?igsh=abc#x" }
        });
        outbound!.Value.GetProperty("u").GetString().Should().Be(site + "/qr/:id");
        outbound.Value.GetProperty("p").GetProperty("url").GetString().Should().Be("https://www.instagram.com/gasthaus/");
        var download = await TransformAsync(Page, new
        {
            n = "File Download", u = site + "/qrcodes", p = new { url = site + $"/files/{id}/karte.pdf?token=secret" }
        });
        download!.Value.GetProperty("p").GetProperty("url").GetString().Should().Be(site + "/files/:id/karte.pdf");
        var own = await TransformAsync(Page, new
        {
            n = "Kostenlos starten", u = site + "/?ref=blog#lp-steps", d = "app.speisedirekt.de", p = new { position = "hero" }
        });
        own!.Value.GetProperty("u").GetString().Should().Be(site + "/?ref=blog");
        own.Value.GetProperty("n").GetString().Should().Be("Kostenlos starten");
        own.Value.GetProperty("d").GetString().Should().Be("app.speisedirekt.de");
        own.Value.GetProperty("p").GetProperty("position").GetString().Should().Be("hero");
    }

    // --- Conversion events ---

    /// <summary>Keeps the register links on the page (the click and its event still happen, the navigation does not).</summary>
    private static Task HoldRegisterLinksAsync(IPage page) => page.EvaluateAsync("""
        () => {
            const hold = e => { if (e.target instanceof Element && e.target.closest('a[data-lp-cta]')) e.preventDefault(); };
            document.addEventListener('click', hold, true);
            document.addEventListener('auxclick', hold, true);
        }
        """);

    private static async Task ExpectLastStartAsync(IPage page, int count, string position)
    {
        var events = await EventsAsync(page);
        events.Should().HaveCount(count, position);
        events[^1].Name.Should().Be("Kostenlos starten");
        events[^1].Props.Should().BeEquivalentTo(new Dictionary<string, string> { ["position"] = position });
    }

    [Fact]
    public async Task Landing_EveryRegisterCallToAction_SendsKostenlosStarten_WithItsPosition()
    {
        await TurnOnAsync();
        await Page.GotoAsync("/");
        await Page.Locator("html[data-landing-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await HoldRegisterLinksAsync(Page);
        (await EventsAsync(Page)).Should().BeEmpty("a pageview is the tracker's, no event of ours on load");

        var count = 0;
        foreach (var (testId, position) in new[]
                 {
                     ("landing-register", "header"), ("hero-register", "hero"), ("steps-register", "steps"),
                     ("cta-register", "cta"), ("footer-register", "footer")
                 })
        {
            await Page.GetByTestId(testId).ClickAsync();
            await ExpectLastStartAsync(Page, ++count, position);
        }
        // A middle click (opens a new tab) counts too
        await Page.GetByTestId("hero-register").ClickAsync(new() { Button = MouseButton.Middle });
        await ExpectLastStartAsync(Page, ++count, "hero");
        // The login and the other links send nothing
        await Page.GetByTestId("landing-login").EvaluateAsync("a => a.addEventListener('click', e => e.preventDefault())");
        await Page.GetByTestId("landing-login").ClickAsync();
        (await EventsAsync(Page)).Should().HaveCount(count);

        // Phone: the register link of the menu sheet
        await Page.SetViewportSizeAsync(390, 844);
        await Page.GetByTestId("landing-menu-button").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("landing-menu")).ToBeVisibleAsync();
        await Page.GetByTestId("menu-register").ClickAsync();
        await ExpectLastStartAsync(Page, ++count, "menu");

        // The imprint has the same header and footer
        await Page.SetViewportSizeAsync(1280, 900);
        await Page.GotoAsync("/impressum");
        await Page.Locator("html[data-landing-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await HoldRegisterLinksAsync(Page);
        await Page.GetByTestId("landing-register").ClickAsync();
        await ExpectLastStartAsync(Page, 1, "header");
        await Page.GetByTestId("footer-register").ClickAsync();
        await ExpectLastStartAsync(Page, 2, "footer");

        // Without the hold the link is followed as always
        await Page.GotoAsync("/");
        await Page.GetByTestId("hero-register").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/Account/Register$"));
    }

    [Fact]
    public async Task Registration_SendsKontoAngelegt_Once_NotOnReload_BackForward_OrASecondTab()
    {
        await TurnOnAsync();
        // Register → "/" (which sends a signed-in visitor on to /dashboard: the marker waits for that page)
        await RegisterAsync(Page, UniqueEmail());
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/dashboard$"));
        (await NamesAsync(Page)).Should().Equal("Konto angelegt");

        await Page.ReloadAsync();
        await ExpectCounterAsync(Page);
        (await NamesAsync(Page)).Should().BeEmpty("a reload finds no marker");

        // Back to the registration page and forward again (a page restored from the cache keeps its old queue: cleared)
        await Page.EvaluateAsync("() => { window.plausible.q = []; }");
        await Page.GoBackAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/Account/Register$"));
        (await NamesAsync(Page)).Should().BeEmpty();
        await Page.GoForwardAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/dashboard$"));
        (await NamesAsync(Page)).Should().BeEmpty();

        var tab = await Context.NewPageAsync();
        await tab.GotoAsync("/dashboard");
        await ExpectCounterAsync(tab);
        (await NamesAsync(tab)).Should().BeEmpty("a second tab finds no marker");

        // With a ReturnUrl the first page is that one
        var (other, otherPage) = await NewCountedContextAsync();
        await using var _ = other;
        await otherPage.GotoAsync("/Account/Register?ReturnUrl=%2Fqrcodes");
        await otherPage.Locator("#Input\\.Email").FillAsync(UniqueEmail());
        await otherPage.Locator("#Input\\.Password").FillAsync(Password);
        await otherPage.Locator("#Input\\.ConfirmPassword").FillAsync(Password);
        await WaitForInteractiveAsync(otherPage, () => otherPage.GetByTestId("register-submit").ClickAsync());
        await Assertions.Expect(otherPage).ToHaveURLAsync(new Regex("/qrcodes$"));
        (await NamesAsync(otherPage)).Should().Equal("Konto angelegt");
        _posts.Should().BeEmpty();
    }

    [Fact]
    public async Task Registration_WhileTheCounterIsOff_SendsNothingLater()
    {
        await ServeFakeAsync(Context);
        await RegisterAsync(Page, UniqueEmail());
        await ExpectNoCounterAsync(Page);

        SetCounter(ScriptPath, Endpoint);
        await Page.ReloadAsync();
        await ExpectCounterAsync(Page);
        (await NamesAsync(Page)).Should().BeEmpty("no marker is left while the counter is off");
    }

    [Fact]
    public async Task InvitedMember_NeverSendsKontoAngelegt()
    {
        await TurnOnAsync();
        await RegisterAsync(Page, UniqueEmail());
        (await NamesAsync(Page)).Should().Equal("Konto angelegt");

        var memberEmail = UniqueEmail("team");
        await InviteAsync(memberEmail, "Max Kellner");
        (await NamesAsync(Page)).Should().BeEmpty("inviting creates an account, but not a registration");

        var (member, memberPage) = await NewCountedContextAsync();
        await using var _ = member;
        await LoginAsync(memberPage, memberEmail);
        await Assertions.Expect(memberPage).ToHaveURLAsync(new Regex("/dashboard$"));
        await ExpectCounterAsync(memberPage);
        (await NamesAsync(memberPage)).Should().BeEmpty("the member's first sign-in is no registration");
    }

    [Fact]
    public async Task FirstMenu_IsSentOnce_WhoeverOfTheTeamSavesIt()
    {
        await TurnOnAsync();
        // The owner registers elsewhere; a manager of the tenant saves its first menu
        var (owner, ownerPage) = await NewCountedContextAsync();
        await using var _ = owner;
        var ownerEmail = UniqueEmail();
        await RegisterAsync(ownerPage, ownerEmail);
        var member = await SeedTeamMemberAsync(await OwnerIdAsync(ownerEmail));
        await LoginAsync(Page, member.Email);

        await CreateMenuViaUiAsync("Erste Karte");
        (await NamesAsync(Page)).Should().Equal("Erste Speisekarte");

        await CreateMenuViaUiAsync("Zweite Karte");
        (await NamesAsync(Page)).Should().BeEmpty("the tenant had a menu already");

        // Editing the first one again is no creation
        await GotoInteractiveAsync("/menus");
        await MenuCardLink(Page, "Erste Karte").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/menudetail/"));
        await GotoInteractiveAsync(Page.Url.Replace("/menudetail/", "/menu/edit/"));
        await Page.Locator("#menuDesc").FillAsync("Geändert");
        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/menudetail/"));
        (await NamesAsync(Page)).Should().BeEmpty();
        _posts.Should().BeEmpty();
    }

    [Fact]
    public async Task FirstQrCode_IsSentOnce_TheSecondIsNot()
    {
        await TurnOnAsync();
        var ownerId = await RegisterOwnerAsync();
        // Seeded in the database: the seed sends nothing (and no "Erste Speisekarte" for it)
        await SeedMenuAsync(ownerId, "QR Karte");

        await CreateDirectQrViaUiAsync("Tisch 1", "QR Karte");
        (await NamesAsync(Page)).Should().Equal("Erster QR-Code");

        await CreateDirectQrViaUiAsync("Tisch 2", "QR Karte");
        (await NamesAsync(Page)).Should().BeEmpty("the tenant had a QR code already");
    }

    [Fact]
    public async Task QrDownload_AndCopyLink_SendTheirEvents()
    {
        await TurnOnAsync();
        var (_, _, qrId) = await OwnerWithMenuAndQrAsync("Teilen Karte");
        await Context.GrantPermissionsAsync(["clipboard-read", "clipboard-write"], new() { Origin = App.ServerAddress });

        await GotoInteractiveAsync("/qrcodes");
        await Assertions.Expect(Page.Locator("html[data-qr-copy='ready']")).ToHaveCountAsync(1);
        await Page.RunAndWaitForDownloadAsync(() => Page.GetByTestId("qr-download").First.ClickAsync());
        (await NamesAsync(Page)).Should().Equal("QR-Code heruntergeladen");

        var copy = Page.GetByTestId("qr-copy").First;
        await copy.ClickAsync();
        await Assertions.Expect(copy).ToHaveAttributeAsync("data-copy-result", "done");
        (await NamesAsync(Page)).Should().Equal("QR-Code heruntergeladen", "QR-Link kopiert");
        (await EventsAsync(Page)).Should().OnlyContain(e => e.Props == null, "the QR events carry no props");

        // The editor of an existing code has the same actions
        await GotoInteractiveAsync($"/qrcode/edit/{qrId}");
        await Assertions.Expect(Page.Locator("html[data-qr-copy='ready']")).ToHaveCountAsync(1);
        await Page.RunAndWaitForDownloadAsync(() => Page.GetByTestId("qr-download").ClickAsync());
        (await NamesAsync(Page)).Should().Equal("QR-Code heruntergeladen");
    }

    [Fact]
    public async Task BackOffice_OneInteractiveNavigation_IsOnePageview()
    {
        await TurnOnAsync();
        await RegisterAsync(Page, UniqueEmail());
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/dashboard$"));
        (await PageviewsAsync(Page)).Should().Equal("/dashboard");

        await Page.GetByTestId("top-nav").GetByRole(AriaRole.Link, new() { Name = "Menus", Exact = true }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/menus$"));
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1 })).ToBeVisibleAsync();
        (await PageviewsAsync(Page)).Should().Equal("/dashboard", "/menus");

        await Page.GoBackAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/dashboard$"));
        (await PageviewsAsync(Page)).Should().Equal("/dashboard", "/menus", "/dashboard");

        // Still the same document: the tracker and the init script ran once, the registration event once
        (await Page.EvaluateAsync<int>("() => window.__counter.loads")).Should().Be(1);
        (await NamesAsync(Page)).Should().Equal("Konto angelegt");
    }
}
