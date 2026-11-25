using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// The landing page on <c>/</c> (AP10): static SSR with its own document (no circuit, no Tailwind/Bootstrap/blazor.web.js,
/// nothing external), sections and calls to action, the phone menu sheet, no sideways scrolling, readable without JS,
/// reduced motion, images and the weight budget, SEO tags, the operator's legal links, and the routing around it
/// (logged in → <c>/dashboard</c>). The language-dependent tests run for a German and an English browser (AP12).
/// </summary>
public class LandingPageTests : E2ETestBase
{
    public LandingPageTests(E2EFixture fixture) : base(fixture) { }

    /// <summary>What the page says in one language (the copy is in Resources/LandingText.resx).</summary>
    public sealed record Copy(
        string Locale, string Lang, string OgLocale, string Title, string Headline, string[] Nav, string FaqTitle,
        string CostQuestion, string CostAnswer, string AppQuestion, string MadeUp, string DemoData, string Legend,
        string Register, string Login, string Imprint, string Privacy, string[] ForbiddenClaims,
        string Demo = "", string DemoPath = "", string TryDark = "");

    private static readonly Copy German = new(
        "de-DE", "de", "de_DE", "Digitale Speisekarte mit QR-Code | Speise-Direkt", "Ihre Speisekarte, ein Scan entfernt.",
        ["Funktionen", "Designs", "So funktioniert’s", "FAQ"], "Häufige Fragen",
        "Was kostet Speise-Direkt?", "Die Registrierung ist kostenlos.", "Brauchen meine Gäste eine App?",
        "erfundene Restaurant", "Demodaten", "Design in der Vorschau", "Kostenlos registrieren", "Anmelden",
        "Impressum", "Datenschutz", ["dauerhaft kostenlos", "für immer kostenlos", "Kunden", "Bewertungen von"],
        "Live-Demo ansehen", "/demo", "Design „Dunkel“ live ausprobieren");

    private static readonly Copy English = new(
        "en-US", "en", "en_US", "Digital menu with QR code | Speise-Direkt", "Your menu, one scan away.",
        ["Features", "Designs", "How it works", "FAQ"], "Frequently asked questions",
        "What does Speise-Direkt cost?", "Registration is free.", "Do my guests need an app?",
        "made-up restaurant", "demo data", "Design in the preview", "Register for free", "Log in",
        "Imprint", "Privacy", ["free forever", "forever free", "customers", "reviews from"],
        "See the live demo", "/demo/en", "Try the Dark design live");

    public static TheoryData<string> Languages => new() { "de", "en" };

    private static Copy CopyOf(string language) => language == "de" ? German : English;

    private const string Headline = "Your menu, one scan away.";

    /// <summary>Loads <c>/</c> and waits for <c>js/landing.js</c> (it sets <c>data-landing-ready</c>).</summary>
    private static async Task OpenLandingAsync(IPage page)
    {
        var response = await page.GotoAsync("/");
        response!.Status.Should().Be(200);
        await page.Locator("html[data-landing-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
    }

    private async Task<(IBrowserContext Context, IPage Page)> PhoneAsync(int width = 390, bool javaScript = true, ReducedMotion? motion = null,
        string? locale = null)
    {
        var context = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, ViewportSize = new ViewportSize { Width = width, Height = 844 },
            IsMobile = true, HasTouch = true, JavaScriptEnabled = javaScript, ReducedMotion = motion, Locale = locale
        });
        context.SetDefaultTimeout(15_000);
        // Same rule as every test context: nothing leaves the app
        var origin = new Uri(App.ServerAddress).GetLeftPart(UriPartial.Authority);
        await context.RouteAsync("**/*", r => r.Request.Url.StartsWith(origin, StringComparison.OrdinalIgnoreCase) ? r.ContinueAsync() : r.AbortAsync());
        return (context, await context.NewPageAsync());
    }

    /// <summary>Makes every image eager and waits until the visible ones are decoded.</summary>
    private static Task LoadAllImagesAsync(IPage page) => page.EvaluateAsync("""
        async () => {
            for (const img of document.images) img.loading = 'eager';
            await Promise.all([...document.images].filter(i => i.checkVisibility()).map(i => i.decode().catch(() => {})));
        }
        """);

    private static readonly string[] SectionIds =
    [
        "landing-hero", "landing-proof", "landing-features", "landing-designs", "landing-admin", "landing-more",
        "landing-steps", "landing-faq", "landing-cta", "landing-footer"
    ];

    // --- Document ---

    [Theory]
    [MemberData(nameof(Languages))]
    public async Task AnonymousRoot_IsStatic_WithItsOwnDocument_AndNoErrors(string language)
    {
        var copy = CopyOf(language);
        var (context, page) = await NewContextAsync(copy.Locale);
        await using var _ = context;
        var origin = new Uri(App.ServerAddress).GetLeftPart(UriPartial.Authority);
        var requests = new List<string>();
        var errors = new List<string>();
        var sockets = new List<string>();
        page.Request += (_, r) => { lock (requests) requests.Add(r.Url); };
        page.Console += (_, m) => { if (m.Type == "error") lock (errors) errors.Add(m.Text); };
        page.PageError += (_, e) => { lock (errors) errors.Add(e); };
        page.WebSocket += (_, ws) => { lock (sockets) sockets.Add(ws.Url); };

        await OpenLandingAsync(page);
        await LoadAllImagesAsync(page);
        await page.EvaluateAsync("() => window.scrollTo(0, document.documentElement.scrollHeight)");
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var Page = page;

        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("lang", copy.Lang);
        await Assertions.Expect(Page.Locator("html[data-landing-page]")).ToHaveCountAsync(1);
        await Assertions.Expect(Page.Locator("html[data-guest-page]")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("base")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("script[src*='blazor'], script[src*='tailwind'], link[href*='tailwind'], link[href*='bootstrap'], script[src*='chart']")).ToHaveCountAsync(0);
        (await Page.EvaluateAsync<bool>("() => typeof window.Blazor === 'undefined' && typeof window.tailwind === 'undefined'")).Should().BeTrue();

        requests.Should().OnlyContain(u => u.StartsWith(origin, StringComparison.OrdinalIgnoreCase) || u.StartsWith("data:"),
            "the landing page requests nothing from another host");
        requests.Should().NotContain(u => u.Contains("blazor.web") || u.Contains("bootstrap") || u.Contains("tailwind") || u.Contains("_framework"));
        sockets.Should().BeEmpty("no circuit: the page is static SSR");
        errors.Should().BeEmpty();
    }

    // --- Content and links ---

    [Theory]
    [MemberData(nameof(Languages))]
    public async Task Sections_CallsToAction_AndAnchors_AreThere_AndLinkCorrectly(string language)
    {
        var copy = CopyOf(language);
        var (context, Page) = await NewContextAsync(copy.Locale);
        await using var _ = context;
        await OpenLandingAsync(Page);

        // One h1, a logical heading order
        await Assertions.Expect(Page.Locator("h1")).ToHaveCountAsync(1);
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1, Name = copy.Headline })).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("h1 .lp-mark")).ToHaveCountAsync(1);
        var levels = await Page.EvaluateAsync<int[]>("() => [...document.querySelectorAll('h1,h2,h3,h4,h5,h6')].map(h => +h.tagName[1])");
        levels[0].Should().Be(1);
        for (var i = 1; i < levels.Length; i++)
            levels[i].Should().BeLessThanOrEqualTo(levels[i - 1] + 1, "no heading level is skipped (heading {0})", i);

        foreach (var id in SectionIds)
            await Assertions.Expect(Page.GetByTestId(id)).ToHaveCountAsync(1);
        await Assertions.Expect(Page.GetByTestId("faq-item")).ToHaveCountAsync(9);
        // The examples are labelled as made up
        await Assertions.Expect(Page.GetByTestId("footer-demo-note")).ToContainTextAsync(copy.MadeUp);
        await Assertions.Expect(Page.GetByTestId("card-analytics")).ToContainTextAsync(copy.DemoData);

        // Calls to action
        foreach (var id in new[] { "landing-register", "hero-register", "steps-register", "cta-register", "footer-register" })
            await Assertions.Expect(Page.GetByTestId(id)).ToHaveAttributeAsync("href", "/Account/Register");
        foreach (var id in new[] { "landing-login", "cta-login", "footer-login" })
            await Assertions.Expect(Page.GetByTestId(id)).ToHaveAttributeAsync("href", "/Account/Login");
        await Assertions.Expect(Page.GetByTestId("landing-nav").Locator("a")).ToHaveTextAsync(copy.Nav);

        // Every in-page link has its target
        var missing = await Page.EvaluateAsync<string[]>("""
            () => [...document.querySelectorAll('a[href^="#"]')].map(a => a.getAttribute('href').slice(1))
                .filter(id => !document.getElementById(id))
            """);
        missing.Should().BeEmpty();

        // An anchor scrolls its section into view below the sticky header
        await Page.GetByTestId("landing-nav").GetByRole(AriaRole.Link, new() { Name = "FAQ" }).ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("#faq$"));
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = copy.FaqTitle })).ToBeInViewportAsync();
        await Page.WaitForFunctionAsync("""
            () => { const h = document.querySelector('[data-lp-header]').getBoundingClientRect();
                    const t = document.getElementById('faq').getBoundingClientRect(); return Math.abs(t.top - h.bottom) < 24 || t.top >= h.bottom; }
            """);

        // The hero's second button opens the live demo in the visitor's language (AP13)
        await Assertions.Expect(Page.GetByTestId("hero-demo")).ToHaveAttributeAsync("href", copy.DemoPath);
        await Assertions.Expect(Page.GetByTestId("hero-demo")).ToHaveTextAsync(copy.Demo);
        await Page.GetByTestId("hero-demo").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(Regex.Escape(copy.DemoPath) + "$"));
        await Page.Locator("html[data-guest-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        await Assertions.Expect(Page.GetByTestId("demo-note")).ToBeVisibleAsync();
        await Page.GoBackAsync();

        // The hero's CTA opens the registration
        await Page.GetByTestId("hero-register").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/Account/Register$"));
        await Assertions.Expect(Page.Locator("#Input\\.Email")).ToBeVisibleAsync();
        await Page.GoBackAsync();
        await Page.GetByTestId("landing-login").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/Account/Login$"));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public async Task Faq_IsNativeDetails_AndHonestAboutCost(string language)
    {
        var copy = CopyOf(language);
        var (context, Page) = await NewContextAsync(copy.Locale);
        await using var _ = context;
        await OpenLandingAsync(Page);

        var cost = Page.GetByTestId("faq-item").Filter(new() { HasText = copy.CostQuestion });
        await Assertions.Expect(cost).ToHaveJSPropertyAsync("open", false);
        await cost.Locator("summary").ClickAsync();
        await Assertions.Expect(cost).ToHaveJSPropertyAsync("open", true);
        await Assertions.Expect(cost.Locator(".lp-faq-answer")).ToHaveTextAsync(copy.CostAnswer);

        // Keyboard: Enter on the summary toggles it
        var app = Page.GetByTestId("faq-item").Filter(new() { HasText = copy.AppQuestion });
        await app.Locator("summary").FocusAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(app).ToHaveJSPropertyAsync("open", true);

        // No prices, no "forever free", no fake proof anywhere on the page
        var text = await Page.Locator("body").InnerTextAsync();
        text.Should().NotContain("€").And.NotContainAny(copy.ForbiddenClaims);
    }

    // --- Phone menu sheet ---

    [Theory]
    [MemberData(nameof(Languages))]
    public async Task PhoneMenuSheet_OpensAndCloses_WithEscape_Backdrop_Button_Link_AndWidening(string language)
    {
        var copy = CopyOf(language);
        var (context, page) = await PhoneAsync(locale: copy.Locale);
        await using var _ = context;
        await OpenLandingAsync(page);

        var button = page.GetByTestId("landing-menu-button");
        var sheet = page.GetByTestId("landing-menu");
        var openSheet = page.Locator("dialog[data-testid='landing-menu'][open]");
        await Assertions.Expect(page.GetByTestId("landing-nav")).ToBeHiddenAsync();
        await Assertions.Expect(page.GetByTestId("landing-register")).ToBeInViewportAsync();
        await Assertions.Expect(button).ToHaveAttributeAsync("role", "button");
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-expanded", "false");

        // Escape
        await button.ClickAsync();
        await Assertions.Expect(openSheet).ToHaveCountAsync(1);
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-expanded", "true");
        var first = sheet.GetByRole(AriaRole.Link, new() { Name = copy.Nav[0] });
        await Assertions.Expect(first).ToBeFocusedAsync();
        // AP13: opened by a tap, the focused first link shows no ring (WebKit would draw one for a script focus) until
        // a key is pressed; opened by the keyboard it shows the ring at once
        await Assertions.Expect(first).ToHaveAttributeAsync("data-lp-quiet", "");
        (await first.EvaluateAsync<string>("e => getComputedStyle(e).outlineStyle")).Should().Be("none");
        await page.Keyboard.PressAsync("Shift");
        await Assertions.Expect(first).Not.ToHaveAttributeAsync("data-lp-quiet", "");
        await Assertions.Expect(page.Locator("html[data-lp-menu-open]")).ToHaveCountAsync(1);
        await Assertions.Expect(sheet.GetByRole(AriaRole.Link, new() { Name = copy.Register })).ToHaveAttributeAsync("href", "/Account/Register");
        await Assertions.Expect(sheet.GetByRole(AriaRole.Link, new() { Name = copy.Login })).ToHaveAttributeAsync("href", "/Account/Login");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(openSheet).ToHaveCountAsync(0);
        await Assertions.Expect(button).ToBeFocusedAsync();
        await Assertions.Expect(button).ToHaveAttributeAsync("aria-expanded", "false");
        await Assertions.Expect(page.Locator("html[data-lp-menu-open]")).ToHaveCountAsync(0);

        // Opened by the keyboard: the ring shows at once
        await button.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(openSheet).ToHaveCountAsync(1);
        await Assertions.Expect(first).ToBeFocusedAsync();
        await Assertions.Expect(first).Not.ToHaveAttributeAsync("data-lp-quiet", "");
        (await first.EvaluateAsync<string>("e => getComputedStyle(e).outlineStyle")).Should().Be("solid");
        await page.Keyboard.PressAsync("Escape");
        await Assertions.Expect(openSheet).ToHaveCountAsync(0);

        // Close button
        await button.ClickAsync();
        await page.GetByTestId("landing-menu-close").ClickAsync();
        await Assertions.Expect(openSheet).ToHaveCountAsync(0);
        await Assertions.Expect(button).ToBeFocusedAsync();

        // Backdrop (below the panel)
        await button.ClickAsync();
        await Assertions.Expect(openSheet).ToHaveCountAsync(1);
        await page.Mouse.ClickAsync(195, 820);
        await Assertions.Expect(openSheet).ToHaveCountAsync(0);

        // A followed link closes it and scrolls to its section
        await button.ClickAsync();
        await sheet.GetByRole(AriaRole.Link, new() { Name = "FAQ" }).ClickAsync();
        await Assertions.Expect(openSheet).ToHaveCountAsync(0);
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("#faq$"));
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = copy.FaqTitle })).ToBeInViewportAsync();

        // Widening to the desktop header closes it
        await page.EvaluateAsync("() => window.scrollTo(0, 0)");
        await button.ClickAsync();
        await Assertions.Expect(openSheet).ToHaveCountAsync(1);
        await page.SetViewportSizeAsync(1280, 900);
        await Assertions.Expect(openSheet).ToHaveCountAsync(0);
        await Assertions.Expect(button).ToBeHiddenAsync();
        await Assertions.Expect(page.GetByTestId("landing-nav")).ToBeVisibleAsync();
    }

    // --- Layout ---

    [Theory]
    [MemberData(nameof(Languages))]
    public async Task NoHorizontalOverflow_AtPhoneTabletAndDesktopWidths(string language)
    {
        foreach (var width in new[] { 360, 390, 768, 1024, 1280, 1440 })
        {
            var (context, page) = await PhoneAsync(width, locale: CopyOf(language).Locale);
            await using var _ = context;
            await OpenLandingAsync(page);
            await LoadAllImagesAsync(page);
            var overflow = await page.EvaluateAsync<int>("() => document.documentElement.scrollWidth - window.innerWidth");
            overflow.Should().BeLessThanOrEqualTo(0, $"the page does not scroll sideways at {width} px");

            // Nothing sticks out of the viewport either (elements clipped by an overflow:hidden parent excepted)
            var wide = await page.EvaluateAsync<string[]>("""
                () => [...document.querySelectorAll('main *, header *, footer *')].filter(e => {
                    const r = e.getBoundingClientRect();
                    if (r.width === 0 || r.right <= innerWidth + 1) return false;
                    for (let p = e.parentElement; p; p = p.parentElement)
                        if (getComputedStyle(p).overflowX !== 'visible') return false;
                    return true;
                }).map(e => e.className || e.tagName)
                """);
            wide.Should().BeEmpty($"no element is wider than the viewport at {width} px");
        }
    }

    [Fact]
    public async Task SkipLink_IsTheFirstTabStop_AndTheHeaderCondensesOnScroll()
    {
        await OpenLandingAsync(Page);

        await Page.Keyboard.PressAsync("Tab");
        var skip = Page.GetByTestId("landing-skip");
        await Assertions.Expect(skip).ToBeFocusedAsync();
        await Assertions.Expect(skip).ToBeInViewportAsync();
        await Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(Page.Locator("#lp-main")).ToBeFocusedAsync();

        var header = Page.GetByTestId("landing-header");
        await Assertions.Expect(header).Not.ToHaveAttributeAsync("data-scrolled", new Regex(".*"));
        var tall = (await header.BoundingBoxAsync())!.Height;
        await Page.Mouse.WheelAsync(0, 600);
        await Assertions.Expect(header).ToHaveAttributeAsync("data-scrolled", "");
        await Page.WaitForFunctionAsync($"() => document.querySelector('[data-lp-header]').getBoundingClientRect().height < {tall.ToString(System.Globalization.CultureInfo.InvariantCulture)} - 4");
        (await header.BoundingBoxAsync())!.Y.Should().Be(0, "the header stays at the top");
        await Page.EvaluateAsync("() => window.scrollTo(0, 0)");
        await Assertions.Expect(header).Not.ToHaveAttributeAsync("data-scrolled", new Regex(".*"));
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public async Task ThemeSwitcher_ShowsTheChosenDesign_ByMouseAndKeyboard(string language)
    {
        var copy = CopyOf(language);
        var (context, Page) = await NewContextAsync(copy.Locale);
        await using var _ = context;
        await OpenLandingAsync(Page);
        var shots = Page.GetByTestId("theme-shot");
        await Assertions.Expect(shots).ToHaveCountAsync(6);
        await Page.GetByTestId("landing-designs").ScrollIntoViewIfNeededAsync();

        async Task ExpectShownAsync(string key)
        {
            foreach (var other in new[] { "modern", "classic", "elegant", "minimal", "fancy", "dark" })
            {
                var shot = Page.Locator($"[data-testid='theme-shot'][data-theme='{other}']");
                if (other == key) await Assertions.Expect(shot).ToBeVisibleAsync();
                else await Assertions.Expect(shot).ToBeHiddenAsync();
            }
            await Page.WaitForFunctionAsync($"() => {{ const i = document.querySelector(\"[data-testid='theme-shot'][data-theme='{key}']\"); return i.complete && i.naturalWidth > 0; }}");
        }

        // The switcher starts with another design than the hero's
        await ExpectShownAsync("classic");
        await Assertions.Expect(Page.Locator("#lp-theme-classic")).ToBeCheckedAsync();
        await Page.Locator("label[for='lp-theme-dark']").ClickAsync();
        await ExpectShownAsync("dark");
        await Assertions.Expect(Page.Locator("#lp-theme-dark")).ToBeCheckedAsync();

        // A radio group: Space picks the focused design, the arrow keys move the choice
        await Page.Locator("#lp-theme-modern").FocusAsync();
        await Page.Keyboard.PressAsync("Space");
        await ExpectShownAsync("modern");
        await Page.Keyboard.PressAsync("ArrowRight");
        await ExpectShownAsync("classic");
        await Assertions.Expect(Page.GetByRole(AriaRole.Group, new() { Name = copy.Legend })).ToBeVisibleAsync();

        // AP13: below the choices one link tries the chosen design in the live demo
        var tryLive = Page.Locator("[data-testid='theme-demo-link']:visible");
        await Assertions.Expect(tryLive).ToHaveCountAsync(1);
        await Assertions.Expect(tryLive).ToHaveAttributeAsync("href", copy.DemoPath + "?theme=classic");
        await Page.Locator("label[for='lp-theme-dark']").ClickAsync();
        await Assertions.Expect(tryLive).ToHaveCountAsync(1);
        await Assertions.Expect(tryLive).ToHaveTextAsync(copy.TryDark);
        await tryLive.ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex(Regex.Escape(copy.DemoPath) + @"\?theme=dark$"));
        await Assertions.Expect(Page.GetByTestId("menu-display")).ToHaveAttributeAsync("data-theme", "dark");
    }

    // --- Without JS, reduced motion ---

    [Theory]
    [MemberData(nameof(Languages))]
    public async Task WithoutJavaScript_EverySectionIsReadable_AndTheFaqMenuAndSwitcherWork(string language)
    {
        var copy = CopyOf(language);
        var (context, page) = await PhoneAsync(javaScript: false, locale: copy.Locale);
        await using var _ = context;
        var response = await page.GotoAsync("/");
        response!.Status.Should().Be(200);

        await Assertions.Expect(page.Locator("html[data-landing-ready]")).ToHaveCountAsync(0);
        foreach (var id in SectionIds)
        {
            await page.GetByTestId(id).ScrollIntoViewIfNeededAsync();
            await Assertions.Expect(page.GetByTestId(id)).ToBeVisibleAsync();
        }
        var hidden = await page.EvaluateAsync<int>("() => [...document.querySelectorAll('[data-reveal]')].filter(e => getComputedStyle(e).opacity !== '1').length");
        hidden.Should().Be(0, "nothing waits for a script to fade in");

        // FAQ
        var cost = page.GetByTestId("faq-item").Filter(new() { HasText = copy.CostQuestion });
        await cost.Locator("summary").ClickAsync();
        await Assertions.Expect(cost.Locator(".lp-faq-answer")).ToBeVisibleAsync();

        // The menu button is a link to the footer's navigation
        await page.EvaluateAsync("() => window.scrollTo(0, 0)");
        await page.GetByTestId("landing-menu-button").ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("#lp-footer-nav$"));
        await Assertions.Expect(page.GetByTestId("footer-nav")).ToBeInViewportAsync();
        await Assertions.Expect(page.GetByTestId("footer-nav").Locator("a")).ToHaveCountAsync(4);

        // The theme switcher is CSS only (jump there instantly: Playwright's click cannot hit a target that a smooth
        // scroll (scroll-behavior: smooth) is still moving)
        await page.EvaluateAsync("() => document.getElementById('designs').scrollIntoView({ behavior: 'instant' })");
        await page.Locator("label[for='lp-theme-fancy']").ClickAsync();
        await Assertions.Expect(page.Locator("[data-testid='theme-shot'][data-theme='fancy']")).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("[data-testid='theme-shot'][data-theme='classic']")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task ReducedMotion_ShowsEverythingAtOnce_OtherwiseSectionsFadeInWhenReached()
    {
        var (calm, calmPage) = await PhoneAsync(1280, motion: ReducedMotion.Reduce);
        await using (calm)
        {
            await OpenLandingAsync(calmPage);
            await Assertions.Expect(calmPage.Locator("html[data-lp-reveal]")).ToHaveCountAsync(0);
            (await calmPage.EvaluateAsync<int>("() => [...document.querySelectorAll('[data-reveal]')].filter(e => getComputedStyle(e).opacity !== '1').length"))
                .Should().Be(0);
            (await calmPage.EvaluateAsync<string>("() => getComputedStyle(document.documentElement).scrollBehavior")).Should().Be("auto");
        }

        var (moving, page) = await PhoneAsync(1280, motion: ReducedMotion.NoPreference);
        await using (moving)
        {
            await OpenLandingAsync(page);
            await Assertions.Expect(page.Locator("html[data-lp-reveal]")).ToHaveCountAsync(1);
            // The hero is never hidden; a section further down waits until it is reached
            await Assertions.Expect(page.GetByTestId("landing-hero")).ToBeVisibleAsync();
            var faq = page.Locator("#faq .lp-faq-list");
            (await faq.EvaluateAsync<string>("e => getComputedStyle(e).opacity")).Should().Be("0");
            await faq.ScrollIntoViewIfNeededAsync();
            await Assertions.Expect(faq).ToHaveAttributeAsync("data-revealed", "");
            await page.WaitForFunctionAsync("() => getComputedStyle(document.querySelector('#faq .lp-faq-list')).opacity === '1'");
        }
    }

    // --- Images ---

    [Theory]
    [MemberData(nameof(Languages))]
    public async Task Images_HaveAltAndDimensions_Load_AndStayWithinTheBudget(string language)
    {
        var copy = CopyOf(language);
        var (context, Page) = await NewContextAsync(copy.Locale);
        await using var _ = context;
        var responses = new List<IResponse>();
        Page.Response += (_, r) => { lock (responses) responses.Add(r); };
        async Task<long> BytesAsync(Func<string, bool> path)
        {
            IResponse[] all;
            lock (responses) all = responses.ToArray();
            long sum = 0;
            foreach (var r in all.Where(r => path(new Uri(r.Url).AbsolutePath)))
            {
                var sizes = await r.Request.SizesAsync();
                sum += sizes.ResponseBodySize + sizes.ResponseHeadersSize;
            }
            return sum;
        }

        // First load at 1280 px (1x images, nothing below the fold)
        await OpenLandingAsync(Page);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        (await BytesAsync(_ => true)).Should().BeLessThan(1_500_000, "the whole first load stays under 1.5 MB");
        (await BytesAsync(p => p.Contains("/images/landing/guest-modern"))).Should().BePositive()
            .And.BeLessThan(200_000, "the hero image ≤ 200 KB");
        var hero = Page.GetByTestId("hero-phone").Locator("img");
        await Assertions.Expect(hero).ToHaveAttributeAsync("loading", "eager");
        await Assertions.Expect(hero).ToHaveAttributeAsync("fetchpriority", "high");
        var lazy = await Page.EvaluateAsync<string[]>(
            "() => [...document.querySelectorAll('.lp-phone img, .lp-browser img, .lp-card-media img')].filter(i => !i.closest('[data-testid=hero-phone]') && i.getAttribute('loading') !== 'lazy').map(i => i.src)");
        lazy.Should().BeEmpty("every image below the hero loads lazily");

        // Every image: alt text, width/height, and loaded with 200
        await LoadAllImagesAsync(Page);
        await Page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        var report = System.Text.Json.JsonSerializer.Deserialize<ImageInfo[]>(await Page.EvaluateAsync<string>("""
            () => JSON.stringify([...document.images].map(i => ({
                src: i.currentSrc || i.src, alt: i.getAttribute('alt'), width: i.getAttribute('width'), height: i.getAttribute('height'),
                visible: i.checkVisibility(), loaded: i.complete && i.naturalWidth > 0,
                product: i.closest('.lp-phone, .lp-browser, .lp-card-media') !== null })))
            """), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web))!;
        report.Should().NotBeEmpty();
        foreach (var image in report)
        {
            image.Alt.Should().NotBeNull($"{image.Src} has an alt attribute");
            image.Width.Should().NotBeNullOrEmpty($"{image.Src} reserves its width");
            image.Height.Should().NotBeNullOrEmpty($"{image.Src} reserves its height");
            if (image.Product) image.Alt.Should().NotBeEmpty($"{image.Src} describes the screenshot");
            if (image.Visible) image.Loaded.Should().BeTrue($"{image.Src} loads");
        }
        IResponse[] images;
        lock (responses) images = responses.Where(r => new Uri(r.Url).AbsolutePath.StartsWith("/images/landing/")).ToArray();
        images.Should().NotBeEmpty().And.OnlyContain(r => r.Status == 200);
        images.Should().OnlyContain(r => new Uri(r.Url).AbsolutePath.EndsWith(".webp"));

        // The back-office screenshots are the browser language's set, the guest menu's are shared
        var backOffice = images.Select(r => new Uri(r.Url).AbsolutePath)
            .Where(p => p.Contains("/qr-calendar-") || p.Contains("/editor-preview-") || p.Contains("/analytics-")).ToArray();
        backOffice.Should().NotBeEmpty().And.OnlyContain(p => p.Contains($"-{copy.Lang}.") || p.Contains($"-{copy.Lang}-"),
            "the page shows the back office in the visitor's language");
        var alt = await Page.Locator("img[src*='/qr-calendar-']").GetAttributeAsync("alt");
        alt.Should().Contain(language == "de" ? "Kalender" : "calendar");
    }

    public sealed record ImageInfo(string Src, string? Alt, string? Width, string? Height, bool Visible, bool Loaded, bool Product);

    // --- SEO ---

    [Theory]
    [MemberData(nameof(Languages))]
    public async Task Head_HasTitle_Description_Canonical_AndOpenGraph(string language)
    {
        var copy = CopyOf(language);
        var (context, Page) = await NewContextAsync(copy.Locale);
        await using var _ = context;
        await OpenLandingAsync(Page);

        await Assertions.Expect(Page).ToHaveTitleAsync(copy.Title);
        string? Meta(string selector) => Page.Locator(selector).GetAttributeAsync("content").GetAwaiter().GetResult();
        var description = Meta("meta[name='description']");
        description.Should().Contain(language == "de" ? "QR-Code" : "QR code");
        description!.Length.Should().BeLessThanOrEqualTo(160);
        (await Page.Locator("link[rel='canonical']").GetAttributeAsync("href")).Should().Be(App.ServerAddress + "/");
        Meta("meta[property='og:url']").Should().Be(App.ServerAddress + "/");
        Meta("meta[property='og:type']").Should().Be("website");
        Meta("meta[property='og:locale']").Should().Be(copy.OgLocale);
        Meta("meta[property='og:title']").Should().Be(copy.Title);
        // One URL serves both languages: no hreflang alternates
        await Assertions.Expect(Page.Locator("link[rel='alternate'][hreflang]")).ToHaveCountAsync(0);
        Meta("meta[property='og:description']").Should().NotBeNullOrWhiteSpace();
        Meta("meta[name='twitter:card']").Should().Be("summary_large_image");
        var image = Meta("meta[property='og:image']");
        image.Should().StartWith(App.ServerAddress + $"/images/landing/og-{copy.Lang}");
        Meta("meta[property='og:image:width']").Should().Be("1200");
        Meta("meta[property='og:image:height']").Should().Be("630");

        var png = await Page.APIRequest.GetAsync(image!);
        png.Status.Should().Be(200);
        png.Headers["content-type"].Should().Be("image/png");
        var bytes = await png.BodyAsync();
        // PNG IHDR: width and height (big-endian) at bytes 16 and 20
        (bytes[16] << 24 | bytes[17] << 16 | bytes[18] << 8 | bytes[19]).Should().Be(1200);
        (bytes[20] << 24 | bytes[21] << 16 | bytes[22] << 8 | bytes[23]).Should().Be(630);

        // Behind a proxy the absolute URLs use the forwarded scheme and host
        var proxied = await Page.APIRequest.GetAsync("/", new()
        {
            Headers = new Dictionary<string, string> { ["X-Forwarded-Proto"] = "https", ["X-Forwarded-Host"] = "speisekarte.example" }
        });
        var html = await proxied.TextAsync();
        html.Should().Contain("<link rel=\"canonical\" href=\"https://speisekarte.example/\"")
            .And.Contain("content=\"https://speisekarte.example/images/landing/og");
    }

    // --- SEO (AP13): robots.txt, sitemap.xml, JSON-LD ---

    [Fact]
    public async Task RobotsTxt_AllowsTheLandingAndGuestPages_DisallowsBackOfficeAndAccount()
    {
        var response = await Page.APIRequest.GetAsync("/robots.txt", new()
        {
            Headers = new Dictionary<string, string> { ["X-Forwarded-Proto"] = "https", ["X-Forwarded-Host"] = "speisekarte.example" }
        });
        response.Status.Should().Be(200);
        response.Headers["content-type"].Should().StartWith("text/plain");
        var lines = (await response.TextAsync()).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        lines.Should().Contain("User-agent: *").And.Contain("Allow: /");
        foreach (var path in new[] { "/Account/", "/dashboard", "/menus", "/menu/edit", "/menu/preview/", "/qrcodes", "/analytics", "/restaurant", "/users" })
            lines.Should().Contain($"Disallow: {path}");
        // Guest pages stay crawlable: no rule matches /menu/{id} or /qr/{id}, nor the demo (it says noindex itself)
        var disallowed = lines.Where(l => l.StartsWith("Disallow: ")).Select(l => l["Disallow: ".Length..]).ToArray();
        foreach (var url in new[] { "/", "/impressum", $"/menu/{Guid.NewGuid()}", $"/qr/{Guid.NewGuid()}", "/demo" })
            disallowed.Should().NotContain(p => url.StartsWith(p, StringComparison.Ordinal), url);
        foreach (var url in new[] { "/dashboard", "/Account/Login", "/menu/edit/1", "/menu/preview/1", "/Categories/1", "/MenuItems/1", "/users/invite" })
            disallowed.Should().Contain(p => url.StartsWith(p, StringComparison.Ordinal), url);
        lines.Should().Contain("Sitemap: https://speisekarte.example/sitemap.xml");
    }

    [Fact]
    public async Task SitemapXml_ListsTheLandingPageTheImprintAndThePrivacyPolicy()
    {
        var response = await Page.APIRequest.GetAsync("/sitemap.xml", new()
        {
            Headers = new Dictionary<string, string> { ["X-Forwarded-Proto"] = "https", ["X-Forwarded-Host"] = "speisekarte.example" }
        });
        response.Status.Should().Be(200);
        response.Headers["content-type"].Should().StartWith("application/xml");
        var doc = System.Xml.Linq.XDocument.Parse(await response.TextAsync());
        System.Xml.Linq.XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        doc.Root!.Name.Should().Be(ns + "urlset");
        doc.Descendants(ns + "loc").Select(l => l.Value).Should().Equal(
            "https://speisekarte.example/", "https://speisekarte.example/impressum", "https://speisekarte.example/datenschutz");

        // Without a proxy: the server's own origin
        var direct = System.Xml.Linq.XDocument.Parse(await (await Page.APIRequest.GetAsync("/sitemap.xml")).TextAsync());
        direct.Descendants(ns + "loc").Select(l => l.Value).Should().Equal(
            App.ServerAddress + "/", App.ServerAddress + "/impressum", App.ServerAddress + "/datenschutz");
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public async Task JsonLd_DescribesTheApp_WithoutPriceOrRating(string language)
    {
        var copy = CopyOf(language);
        var (context, Page) = await NewContextAsync(copy.Locale);
        await using var _ = context;
        await OpenLandingAsync(Page);

        var scripts = Page.Locator("script[type='application/ld+json']");
        await Assertions.Expect(scripts).ToHaveCountAsync(1);
        using var json = System.Text.Json.JsonDocument.Parse(await scripts.TextContentAsync() ?? "");
        var root = json.RootElement;
        root.GetProperty("@context").GetString().Should().Be("https://schema.org");
        root.GetProperty("@type").GetString().Should().Be("SoftwareApplication");
        root.GetProperty("name").GetString().Should().Be("Speise-Direkt");
        root.GetProperty("operatingSystem").GetString().Should().Be("Web");
        root.GetProperty("applicationCategory").GetString().Should().Be("BusinessApplication");
        root.GetProperty("inLanguage").GetString().Should().Be(copy.Lang);
        root.GetProperty("url").GetString().Should().Be(App.ServerAddress + "/");
        root.GetProperty("description").GetString().Should().Be(await Page.Locator("meta[name='description']").GetAttributeAsync("content"));
        root.GetProperty("image").GetString().Should().Contain($"/images/landing/og-{copy.Lang}");
        foreach (var forbidden in new[] { "offers", "aggregateRating", "price", "review" })
            root.TryGetProperty(forbidden, out var found).Should().BeFalse(forbidden);
    }

    // --- Operator settings ---

    [Theory]
    [MemberData(nameof(Languages))]
    public async Task LegalLinks_AndContact_FollowTheSettings_ImprintAndPrivacyAreOnByDefault(string language)
    {
        var copy = CopyOf(language);
        var (context, Page) = await NewContextAsync(copy.Locale);
        await using var _ = context;
        await OpenLandingAsync(Page);
        // appsettings.json: Legal:ImprintUrl = /impressum (AP15a) and Legal:PrivacyUrl = /datenschutz (AP16b), the app's
        // own pages; no contact address yet
        await Assertions.Expect(Page.GetByTestId("footer-legal")).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("footer-imprint")).ToHaveAttributeAsync("href", "/impressum");
        await Assertions.Expect(Page.GetByTestId("footer-imprint")).ToHaveTextAsync(copy.Imprint);
        await Assertions.Expect(Page.GetByTestId("footer-privacy")).ToHaveAttributeAsync("href", "/datenschutz");
        await Assertions.Expect(Page.GetByTestId("footer-privacy")).ToHaveTextAsync(copy.Privacy);
        await Assertions.Expect(Page.GetByTestId("footer-contact")).ToHaveCountAsync(0);

        var configuration = App.ServerServices.GetRequiredService<IConfiguration>();
        try
        {
            configuration["Legal:ImprintUrl"] = "https://example.com/impressum";
            configuration["Legal:PrivacyUrl"] = "/datenschutz-neu";
            configuration["Landing:ContactEmail"] = "hallo@example.com";
            await OpenLandingAsync(Page);
            await Assertions.Expect(Page.GetByTestId("footer-imprint")).ToHaveAttributeAsync("href", "https://example.com/impressum");
            await Assertions.Expect(Page.GetByTestId("footer-imprint")).ToHaveTextAsync(copy.Imprint);
            await Assertions.Expect(Page.GetByTestId("footer-privacy")).ToHaveAttributeAsync("href", "/datenschutz-neu");
            await Assertions.Expect(Page.GetByTestId("footer-privacy")).ToHaveTextAsync(copy.Privacy);
            await Assertions.Expect(Page.GetByTestId("footer-contact")).ToHaveAttributeAsync("href", "mailto:hallo@example.com");
            await Assertions.Expect(Page.GetByTestId("footer-contact")).ToHaveTextAsync("hallo@example.com");

            // Only http(s) or a path on this site; only the configured one of the two
            configuration["Legal:ImprintUrl"] = "javascript:alert(1)";
            configuration["Legal:PrivacyUrl"] = "";
            configuration["Landing:ContactEmail"] = "kein mail";
            await OpenLandingAsync(Page);
            await Assertions.Expect(Page.GetByTestId("footer-legal")).ToHaveCountAsync(0);
            await Assertions.Expect(Page.GetByTestId("footer-contact")).ToHaveCountAsync(0);
            configuration["Legal:PrivacyUrl"] = "https://example.com/datenschutz";
            await OpenLandingAsync(Page);
            await Assertions.Expect(Page.GetByTestId("footer-imprint")).ToHaveCountAsync(0);
            await Assertions.Expect(Page.GetByTestId("footer-privacy")).ToBeVisibleAsync();
        }
        finally
        {
            configuration["Legal:ImprintUrl"] = "/impressum";
            configuration["Legal:PrivacyUrl"] = "/datenschutz";
            configuration["Landing:ContactEmail"] = "";
        }
    }

    // --- Routing ---

    [Fact]
    public async Task LoggedInUser_OnRoot_LandsOnTheDashboard()
    {
        var email = await RegisterNewOwnerAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/dashboard$"));

        // A server-side redirect, not a page that loads first
        var root = await Page.APIRequest.GetAsync("/", new() { MaxRedirects = 0 });
        root.Status.Should().Be(302);
        root.Headers["location"].Should().EndWith("/dashboard");

        await GotoInteractiveAsync("/");
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/dashboard$"));
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Welcome back" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("dashboard-user")).ToHaveTextAsync(email);
        await Assertions.Expect(Page.GetByTestId("brand-link")).ToHaveAttributeAsync("href", "/dashboard");
        await Assertions.Expect(Page.GetByTestId("top-nav").GetByRole(AriaRole.Link, new() { Name = "Dashboard" })).ToHaveAttributeAsync("href", "dashboard");

        // Client-side navigation to "/" from a circuit: the router does not know the static page, loads it from the
        // server, and the server sends the user to the dashboard
        await GotoInteractiveAsync("/menus");
        await WaitForInteractiveAsync(Page, () => Page.EvaluateAsync("() => Blazor.navigateTo('/')"));
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/dashboard$"));
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Welcome back" })).ToBeVisibleAsync();

        // Logged out, "/" is the landing page again
        await LogoutAsync(Page);
        await OpenLandingAsync(Page);
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Level = 1, Name = Headline })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Dashboard_LivesAtDashboard_AndAsksAnonymousVisitorsToLogIn()
    {
        var email = UniqueEmail();
        var (context, page) = await NewContextAsync();
        await using (context)
            await RegisterAsync(page, email);

        await Page.GotoAsync("/dashboard");
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/Account/Login\\?ReturnUrl=%2Fdashboard", RegexOptions.IgnoreCase));
        await Page.Locator("#Input\\.Email").FillAsync(email);
        await Page.Locator("#Input\\.Password").FillAsync(Password);
        await WaitForInteractiveAsync(Page, () => Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync());
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/dashboard$"));
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Welcome back" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByTestId("top-nav").Locator("a[aria-current='page']")).ToHaveTextAsync("Dashboard");
        await Assertions.Expect(Page.GetByTestId("quick-actions")).ToBeVisibleAsync();

        // Logging in without a return URL ends on the dashboard as well
        await LogoutAsync(Page);
        await LoginAsync(Page, email);
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/dashboard$"));
    }
}
