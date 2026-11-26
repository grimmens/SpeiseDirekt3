using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// The privacy policy <c>/datenschutz</c> (AP16b, <c>Components/Legal/PrivacyPage</c>): the operator's approved German
/// text for every visitor (<c>lang="de"</c>, the ten sections in his order), the chrome in the browser's language, no
/// cookie, nothing external, no sideways scrolling; the contact address from <c>Landing:ContactEmail</c> (a placeholder
/// while it is empty); the page counter's opt-out switch (<c>js/legal.js</c>: exactly the key <c>plausible_ignore</c>);
/// and the links to it from every area — the landing and account footers, the guest pages, the demo, the notice pages
/// and the back office (<c>Legal:PrivacyUrl</c> = <c>/datenschutz</c> in appsettings.json).
/// </summary>
public class PrivacyPageTests : E2ETestBase
{
    public PrivacyPageTests(E2EFixture fixture) : base(fixture) { }

    private const string Key = "plausible_ignore";
    private const string StateOn = "Die Zählung ist auf diesem Gerät eingeschaltet.";
    private const string StateOff = "Die Zählung ist auf diesem Gerät abgeschaltet.";
    private const string TurnOff = "Zählung auf diesem Gerät abschalten";
    private const string TurnOn = "Zählung wieder einschalten";

    private static readonly string[] Headings =
    [
        "1. Verantwortlicher", "2. Hosting und Server-Logs", "3. Konto- und Teamdaten", "4. Gästeseiten",
        "5. Cookies und lokale Speicherung", "6. Reichweitenmessung mit Plausible Analytics (self-hosted)",
        "7. Auftragsverarbeiter", "8. Ihre Rechte", "9. Aufsichtsbehörde", "10. Änderungen dieser Erklärung"
    ];

    public sealed record Chrome(string Locale, string Lang, string Title, string Legal, string Contents, string ImprintLink, string PrivacyLink);

    private static readonly Chrome German = new("de-DE", "de", "Datenschutzerklärung | Speise-Direkt", "Rechtliches", "Inhalt", "Impressum", "Datenschutz");
    private static readonly Chrome English = new("en-US", "en", "Privacy policy (Datenschutzerklärung) | Speise-Direkt", "Legal", "Contents", "Imprint", "Privacy");

    public static TheoryData<string> Languages => new() { "de", "en" };

    private static Chrome ChromeOf(string language) => language == "de" ? German : English;

    private IConfiguration Configuration => App.ServerServices.GetRequiredService<IConfiguration>();

    private static ILocator Privacy(IPage page) => page.GetByTestId("privacy");

    /// <summary>Loads <c>/datenschutz</c> (200) and waits for the landing document's script and the opt-out switch's.</summary>
    private static async Task<IResponse> OpenPrivacyAsync(IPage page, bool switchReady = true)
    {
        var response = await page.GotoAsync("/datenschutz");
        response!.Status.Should().Be(200);
        await page.Locator("html[data-landing-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        if (switchReady)
            await page.Locator("[data-legal-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        return response;
    }

    /// <summary>Elements (and the document) wider than the viewport.</summary>
    private static Task<string[]> WideElementsAsync(IPage page) => page.EvaluateAsync<string[]>("""
        () => {
            const width = document.documentElement.clientWidth;
            const wide = [...document.querySelectorAll('body *')]
                .filter(e => e.checkVisibility() && e.getBoundingClientRect().right > width + 1)
                .map(e => e.tagName + '.' + e.className + ' ' + Math.round(e.getBoundingClientRect().right));
            if (document.documentElement.scrollWidth > width) wide.push('document ' + document.documentElement.scrollWidth);
            return wide;
        }
        """);

    private static Task<string?> StoredAsync(IPage page) =>
        page.EvaluateAsync<string?>($"() => window.localStorage.getItem('{Key}')");

    // --- The page ---

    [Theory]
    [MemberData(nameof(Languages))]
    public async Task PrivacyPolicy_IsGermanForEveryVisitor_TheChromeFollowsTheBrowser(string language)
    {
        var chrome = ChromeOf(language);
        var (context, page) = await NewContextAsync(chrome.Locale);
        await using var _ = context;
        var origin = new Uri(App.ServerAddress).GetLeftPart(UriPartial.Authority);
        var external = new List<string>();
        page.Request += (_, r) =>
        {
            if (!r.Url.StartsWith(origin, StringComparison.OrdinalIgnoreCase) && !r.Url.StartsWith("data:"))
                lock (external) external.Add(r.Url);
        };

        var response = await OpenPrivacyAsync(page);

        // The landing document in the visitor's language; indexable with a canonical URL; no cookie at all
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("lang", chrome.Lang);
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-landing-page", new Regex(".*"));
        await Assertions.Expect(page).ToHaveTitleAsync(chrome.Title);
        await Assertions.Expect(page.Locator("link[rel='canonical']")).ToHaveAttributeAsync("href", App.ServerAddress + "/datenschutz");
        await Assertions.Expect(page.Locator("meta[name='robots']")).ToHaveCountAsync(0);
        response.Headers.Should().NotContainKey("x-robots-tag");
        response.Headers["vary"].Should().Contain("Accept-Language");
        response.Headers.Should().NotContainKey("set-cookie");
        (await context.CookiesAsync()).Should().BeEmpty();

        // The text: German, lang="de", one h1, the ten sections in the operator's order, each with its anchor
        var privacy = Privacy(page);
        await Assertions.Expect(privacy).ToHaveAttributeAsync("lang", "de");
        await Assertions.Expect(page.Locator("h1")).ToHaveCountAsync(1);
        await Assertions.Expect(privacy.Locator("h1")).ToHaveTextAsync("Datenschutzerklärung");
        await Assertions.Expect(page.GetByTestId("privacy-intro")).ToHaveTextAsync(new Regex(
            "^Informationen zur Verarbeitung personenbezogener Daten gemäß DSGVO und österreichischem Datenschutzgesetz \\(DSG\\) "
            + "bei der Nutzung der Web-Anwendung Speise-Direkt unter https://app\\.speisedirekt\\.de\\. .*\\(Gästeseiten\\)\\.$"));
        var sections = page.GetByTestId("privacy-section");
        (await sections.Locator("h2").AllTextContentsAsync()).Select(t => t.Trim()).Should().Equal(Headings);
        // The contents list repeats the headings and leads to them
        var contents = page.GetByTestId("privacy-toc").Locator("a");
        (await contents.AllTextContentsAsync()).Select(t => t.Trim()).Should().Equal(Headings);
        for (var i = 0; i < Headings.Length; i++)
        {
            var href = await contents.Nth(i).GetAttributeAsync("href");
            href.Should().StartWith("#");
            await Assertions.Expect(sections.Nth(i)).ToHaveAttributeAsync("id", href![1..]);
        }

        // Words that matter, rendered as the operator wrote them
        await Assertions.Expect(page.GetByTestId("privacy-controller")).ToContainTextAsync(
            "Verantwortlicher im Sinne der DSGVO ist der im Impressum genannte Inhaber: Bernhard Grimm, Mühlgasse 106, 2500 Baden, Österreich, E-Mail:");
        await Assertions.Expect(page.GetByTestId("privacy-imprint-link")).ToHaveAttributeAsync("href", "/impressum");
        await Assertions.Expect(page.GetByTestId("privacy-dsb-link")).ToHaveAttributeAsync("href", "https://www.dsb.gv.at/");
        await Assertions.Expect(page.GetByTestId("privacy-dsb-link")).ToHaveTextAsync("www.dsb.gv.at");
        await Assertions.Expect(privacy).ToContainTextAsync("Hochgeladene Fotos werden verkleinert und in der Datenbank gespeichert; dabei werden eingebettete Metadaten wie Aufnahmeort, Aufnahmezeit und Kameramodell entfernt.");
        await Assertions.Expect(privacy.Locator("code", new() { HasText = Key }).First).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("privacy-version")).ToHaveTextAsync("Stand: November 2026");

        // The cookie table: five cookies, the column names, a header per row
        var table = page.GetByTestId("privacy-cookies");
        (await table.Locator("thead th").AllTextContentsAsync()).Should().Equal("Cookie", "Zweck", "Gültigkeit");
        (await table.Locator("tbody th").AllTextContentsAsync()).Should().Equal(
            ".AspNetCore.Identity.Application", ".AspNetCore.Antiforgery.…", "Identity.StatusMessage",
            "Identity.TwoFactorUserId", "Identity.TwoFactorRememberMe");

        // The chrome in the visitor's language: eyebrow, contents label, the note only for a non-German visitor
        await Assertions.Expect(privacy.Locator(".lp-eyebrow")).ToHaveAttributeAsync("lang", chrome.Lang);
        await Assertions.Expect(privacy.Locator(".lp-eyebrow")).ToHaveTextAsync(chrome.Legal);
        await Assertions.Expect(page.GetByTestId("privacy-toc").Locator(".lp-legal-toc-title")).ToHaveTextAsync(chrome.Contents);
        var note = page.GetByTestId("privacy-language-note");
        if (language == "de")
            await Assertions.Expect(note).ToHaveCountAsync(0);
        else
        {
            await Assertions.Expect(note).ToHaveTextAsync("This privacy policy is provided in German.");
            await Assertions.Expect(note).ToHaveAttributeAsync("lang", "en");
        }

        // Footer: both legal pages, no demo note; the section links lead back to the landing page
        await Assertions.Expect(page.GetByTestId("footer-imprint")).ToHaveAttributeAsync("href", "/impressum");
        await Assertions.Expect(page.GetByTestId("footer-privacy")).ToHaveAttributeAsync("href", "/datenschutz");
        await Assertions.Expect(page.GetByTestId("footer-privacy")).ToHaveTextAsync(chrome.PrivacyLink);
        await Assertions.Expect(page.GetByTestId("footer-demo-note")).ToHaveCountAsync(0);
        await Assertions.Expect(page.GetByTestId("landing-nav").Locator("a").First).ToHaveAttributeAsync("href", new Regex("^/#"));

        // Only the landing document's assets and its own small script, nothing from another host
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        external.Should().BeEmpty("the privacy policy makes no external request");
        await Assertions.Expect(page.Locator("script[src*='tailwind'], link[href*='tailwind'], link[href*='bootstrap'], script[src*='blazor']")).ToHaveCountAsync(0);
        var script = await page.Locator("script[src*='/js/legal.']").GetAttributeAsync("src");
        script.Should().MatchRegex(@"^/js/legal\.[a-z0-9]+\.js$", "the switch's script is our own, fingerprinted file");

        // No sideways scrolling on phones and on a desktop; the cookie table becomes one block per cookie on a phone
        foreach (var width in new[] { 360, 390, 1280 })
        {
            await page.SetViewportSizeAsync(width, 800);
            await page.EvaluateAsync("() => document.fonts.ready");
            (await WideElementsAsync(page)).Should().BeEmpty($"nothing is wider than the viewport at {width} px");
            var display = await table.Locator("tbody tr").First.EvaluateAsync<string>("tr => getComputedStyle(tr).display");
            display.Should().Be(width < 640 ? "block" : "table-row");
        }
    }

    [Fact]
    public async Task ContactEmail_ShowsPlaceholders_UntilItIsSet_ThenMailtoLinks()
    {
        // {E-MAIL} twice in the text: the shortest wording that fits each sentence while Landing:ContactEmail is empty
        await OpenPrivacyAsync(Page);
        var controller = Page.GetByTestId("privacy-controller");
        var rights = Page.GetByTestId("privacy-rights");
        await Assertions.Expect(controller).ToHaveTextAsync(
            "Verantwortlicher im Sinne der DSGVO ist der im Impressum genannte Inhaber: Bernhard Grimm, Mühlgasse 106, 2500 Baden, "
            + "Österreich, E-Mail: folgt in Kürze. Speise-Direkt ist ein Angebot dieses Einzelunternehmens.");
        await Assertions.Expect(rights).ToContainTextAsync(
            "Wenden Sie sich formlos per E-Mail an (Adresse folgt in Kürze) oder per Post an die Anschrift in Punkt 1.");
        await Assertions.Expect(Page.GetByTestId("privacy-email-placeholder")).ToHaveCountAsync(2);
        await Assertions.Expect(Page.Locator("a[href^='mailto:']")).ToHaveCountAsync(0);

        try
        {
            Configuration["Landing:ContactEmail"] = "kontakt@speisedirekt.example";
            await OpenPrivacyAsync(Page);
            var links = Page.GetByTestId("privacy-email");
            await Assertions.Expect(links).ToHaveCountAsync(2);
            for (var i = 0; i < 2; i++)
            {
                await Assertions.Expect(links.Nth(i)).ToHaveAttributeAsync("href", "mailto:kontakt@speisedirekt.example");
                await Assertions.Expect(links.Nth(i)).ToHaveTextAsync("kontakt@speisedirekt.example");
            }
            await Assertions.Expect(controller).ToContainTextAsync("Österreich, E-Mail: kontakt@speisedirekt.example. Speise-Direkt ist");
            await Assertions.Expect(rights).ToContainTextAsync(
                "Wenden Sie sich formlos per E-Mail an kontakt@speisedirekt.example oder per Post an die Anschrift in Punkt 1.");
            await Assertions.Expect(Page.GetByTestId("privacy-email-placeholder")).ToHaveCountAsync(0);

            // An unusable value is never shown
            Configuration["Landing:ContactEmail"] = "kein mail";
            await OpenPrivacyAsync(Page);
            await Assertions.Expect(Page.GetByTestId("privacy-email-placeholder")).ToHaveCountAsync(2);
            await Assertions.Expect(Page.GetByTestId("privacy-email")).ToHaveCountAsync(0);
        }
        finally
        {
            Configuration["Landing:ContactEmail"] = "";
        }
    }

    [Fact]
    public async Task SignedInUser_ReadsThePrivacyPolicy_WithoutARedirect()
    {
        await RegisterNewOwnerAsync();
        var response = await Page.APIRequest.GetAsync("/datenschutz", new() { MaxRedirects = 0 });
        response.Status.Should().Be(200);
        await OpenPrivacyAsync(Page);
        await Assertions.Expect(Privacy(Page).Locator("h1")).ToHaveTextAsync("Datenschutzerklärung");
    }

    // --- The opt-out switch (js/legal.js) ---

    [Fact]
    public async Task OptOut_WritesAndRemovesExactlyPlausibleIgnore_AndTheStateSurvivesAReload()
    {
        await OpenPrivacyAsync(Page);
        await Page.EvaluateAsync("() => window.localStorage.setItem('sd-merkliste:x', 'kept')");
        var state = Page.GetByTestId("privacy-optout-state");
        var button = Page.GetByTestId("privacy-optout-button");

        // Default: counted on this device
        await Assertions.Expect(state).ToHaveTextAsync(StateOn);
        await Assertions.Expect(state).ToHaveAttributeAsync("role", "status");
        await Assertions.Expect(button).ToBeVisibleAsync();
        await Assertions.Expect(button).ToHaveTextAsync(TurnOff);
        (await StoredAsync(Page)).Should().BeNull();

        // Off: the key the tracker reads, exactly "true"
        await button.ClickAsync();
        await Assertions.Expect(state).ToHaveTextAsync(StateOff);
        await Assertions.Expect(button).ToHaveTextAsync(TurnOn);
        (await Page.EvaluateAsync<bool>($"() => window.localStorage.{Key} === 'true'")).Should().BeTrue();
        await Assertions.Expect(button).ToBeFocusedAsync();

        // A reload shows the stored state
        await OpenPrivacyAsync(Page);
        await Assertions.Expect(state).ToHaveTextAsync(StateOff);
        await Assertions.Expect(button).ToHaveTextAsync(TurnOn);

        // On again: the key is gone, nothing else was touched; no cookie on the way
        await button.ClickAsync();
        await Assertions.Expect(state).ToHaveTextAsync(StateOn);
        await Assertions.Expect(button).ToHaveTextAsync(TurnOff);
        (await StoredAsync(Page)).Should().BeNull();
        (await Page.EvaluateAsync<string?>("() => window.localStorage.getItem('sd-merkliste:x')")).Should().Be("kept");
        (await Page.EvaluateAsync<int>("() => window.localStorage.length")).Should().Be(1);
        (await Context.CookiesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task OptOut_WorksWithTheKeyboard_WithAVisibleFocus_AndFollowsOtherTabs()
    {
        await OpenPrivacyAsync(Page);
        var button = Page.GetByTestId("privacy-optout-button");
        var state = Page.GetByTestId("privacy-optout-state");

        // Tab from the element before it, then Enter and Space
        await Page.Locator("[data-testid=privacy-section] code", new() { HasText = Key }).First.ScrollIntoViewIfNeededAsync();
        await Page.EvaluateAsync("""
            () => {
                const before = document.querySelector('[data-testid=privacy-optout]').previousElementSibling;
                before.setAttribute('tabindex', '-1');
                before.focus();
            }
            """);
        await Page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(button).ToBeFocusedAsync();
        (await button.EvaluateAsync<string>("b => b.matches(':focus-visible') ? getComputedStyle(b).outlineStyle : 'none'"))
            .Should().Be("solid", "the focused button shows the ring");
        await Page.Keyboard.PressAsync("Enter");
        await Assertions.Expect(state).ToHaveTextAsync(StateOff);
        (await StoredAsync(Page)).Should().Be("true");
        await Page.Keyboard.PressAsync("Space");
        await Assertions.Expect(state).ToHaveTextAsync(StateOn);
        (await StoredAsync(Page)).Should().BeNull();

        // A second tab of the same browser switches it: this one follows (storage event)
        var other = await Context.NewPageAsync();
        await OpenPrivacyAsync(other);
        await other.GetByTestId("privacy-optout-button").ClickAsync();
        await Assertions.Expect(other.GetByTestId("privacy-optout-state")).ToHaveTextAsync(StateOff);
        await Assertions.Expect(state).ToHaveTextAsync(StateOff);
        await Assertions.Expect(button).ToHaveTextAsync(TurnOn);
        await other.CloseAsync();
    }

    [Fact]
    public async Task OptOut_WithoutStorage_SaysSo_AndHidesTheButton()
    {
        var (context, page) = await NewContextAsync(E2ESettings.GermanLocale);
        await using var _ = context;
        await context.AddInitScriptAsync("""
            Object.defineProperty(window, 'localStorage', { get() { throw new DOMException('blocked', 'SecurityError'); } });
            """);
        await OpenPrivacyAsync(page);
        await Assertions.Expect(page.GetByTestId("privacy-optout-state")).ToHaveTextAsync(
            "Ihr Browser erlaubt dieser Seite keinen Zugriff auf den lokalen Speicher, deshalb lässt sich der Schalter hier nicht verwenden.");
        await Assertions.Expect(page.GetByTestId("privacy-optout-button")).ToBeHiddenAsync();
    }

    [Fact]
    public async Task OptOut_WithoutJavaScript_SaysItNeedsIt_AndShowsNoState()
    {
        var context = await Fixture.NewBrowserContextAsync(new()
        {
            BaseURL = App.ServerAddress, JavaScriptEnabled = false, ViewportSize = new ViewportSize { Width = 390, Height = 844 }
        });
        await using var _ = context;
        var page = await context.NewPageAsync();
        var response = await page.GotoAsync("/datenschutz");
        response!.Status.Should().Be(200);

        // The sentence is a <noscript> right after the label. Whether the browser shows <noscript> content depends on
        // how Playwright turns JavaScript off: this project's Playwright 1.52 with the CI host's Chromium 1243 does not
        // render it, Playwright 1.63 with the same Chromium does (checked 25.11.2026). So the markup is checked here.
        var html = await response.TextAsync();
        html.Should().MatchRegex(
            "<strong>Zählung abschalten:</strong> <span[^>]*data-legal-optout-state[^>]*></span><noscript><span[^>]*>"
            + "Für den Schalter ist JavaScript nötig\\. Ohne JavaScript läuft auch das Zählskript nicht\\.</span></noscript>");

        // Without the script: no state, no button — nothing that could claim the counter is on or off
        var optout = page.GetByTestId("privacy-optout");
        await Assertions.Expect(optout).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("privacy-optout-state")).ToHaveTextAsync("");
        await Assertions.Expect(page.GetByTestId("privacy-optout-button")).ToBeHiddenAsync();
        await Assertions.Expect(optout).Not.ToContainTextAsync("eingeschaltet");
        await Assertions.Expect(optout).Not.ToContainTextAsync("abgeschaltet");
    }

    // --- Links from every area ---

    [Fact]
    public async Task LegalLinks_LeadToThePrivacyPolicy_FromLanding_Account_Guest_Demo_Notice_AndBackOffice()
    {
        // Landing page (German browser)
        var (context, page) = await NewContextAsync(E2ESettings.GermanLocale);
        await using var _ = context;
        await page.GotoAsync("/");
        await Assertions.Expect(page.GetByTestId("footer-privacy")).ToHaveAttributeAsync("href", "/datenschutz");
        await Assertions.Expect(page.GetByTestId("footer-privacy")).ToHaveTextAsync("Datenschutz");

        // A guest page (German menu): German labels, the very end of the page, plain links
        await OpenGuestPageAsync(page, $"/menu/{E2ESeedData.MenuId}");
        await ExpectGuestLinksAsync(page, "Impressum", "Datenschutz");
        await page.GetByTestId("guest-privacy").ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/datenschutz$"));
        await page.Locator("[data-legal-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });

        // The demo in both languages: the guest UI language decides, not the browser
        await OpenGuestPageAsync(page, "/demo");
        await ExpectGuestLinksAsync(page, "Impressum", "Datenschutz");
        await OpenGuestPageAsync(page, "/demo/en");
        await ExpectGuestLinksAsync(page, "Legal notice", "Privacy");

        // A notice page (unknown QR code): German, below the notice
        var notice = await page.GotoAsync($"/qr/{Guid.NewGuid()}");
        notice!.Status.Should().Be(200);
        await Assertions.Expect(page.GetByTestId("guest-notice")).ToBeVisibleAsync();
        await ExpectGuestLinksAsync(page, "Impressum", "Datenschutz");
        (await context.CookiesAsync()).Should().BeEmpty("none of these public pages sets a cookie");

        // An account page (its form sets the antiforgery cookie, so after the cookie check)
        await page.GotoAsync("/Account/Login");
        await Assertions.Expect(page.GetByTestId("auth-privacy")).ToHaveAttributeAsync("href", "/datenschutz");
        await page.GetByTestId("auth-privacy").ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/datenschutz$"));
        await Assertions.Expect(Privacy(page).Locator("h1")).ToHaveTextAsync("Datenschutzerklärung");

        // The back office (English browser): a quiet footer line on every page, the landing page's words
        await RegisterNewOwnerAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/dashboard$"));
        var footer = Page.GetByTestId("backoffice-footer");
        await Assertions.Expect(footer.GetByTestId("backoffice-imprint")).ToHaveAttributeAsync("href", "/impressum");
        await Assertions.Expect(footer.GetByTestId("backoffice-imprint")).ToHaveTextAsync("Imprint");
        await Assertions.Expect(footer.GetByTestId("backoffice-privacy")).ToHaveAttributeAsync("href", "/datenschutz");
        await Assertions.Expect(footer.GetByTestId("backoffice-privacy")).ToHaveTextAsync("Privacy");
        await Assertions.Expect(footer.GetByRole(AriaRole.Navigation)).ToHaveAttributeAsync("aria-label", "Legal");
        await GotoInteractiveAsync("/menus");
        await Assertions.Expect(Page.GetByTestId("backoffice-privacy")).ToBeVisibleAsync();
        // From the circuit the link loads the page from the server (the landing document)
        await Page.GetByTestId("backoffice-privacy").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/datenschutz$"));
        await Assertions.Expect(Privacy(Page).Locator("h1")).ToHaveTextAsync("Datenschutzerklärung");

        // German back office: the German words
        var (german, germanPage) = await NewContextAsync(E2ESettings.GermanLocale);
        await using var __ = german;
        await RegisterAsync(germanPage, UniqueEmail());
        await Assertions.Expect(germanPage.GetByTestId("backoffice-imprint")).ToHaveTextAsync("Impressum");
        await Assertions.Expect(germanPage.GetByTestId("backoffice-privacy")).ToHaveTextAsync("Datenschutz");
        // A phone keeps the page gutter and nothing gets wider than the screen
        await germanPage.SetViewportSizeAsync(390, 844);
        (await WideElementsAsync(germanPage)).Should().BeEmpty();
        var left = await germanPage.GetByTestId("backoffice-imprint").EvaluateAsync<double>("a => a.getBoundingClientRect().left");
        left.Should().BeApproximately(16, 1, "the footer uses the page gutter of 1 rem");
    }

    /// <summary>The guest page's last line: both links with the given labels, after everything else of the page.</summary>
    private static async Task ExpectGuestLinksAsync(IPage page, string imprint, string privacy)
    {
        var line = page.GetByTestId("guest-legal");
        await Assertions.Expect(line).ToBeVisibleAsync();
        await Assertions.Expect(line.GetByTestId("guest-imprint")).ToHaveAttributeAsync("href", "/impressum");
        await Assertions.Expect(line.GetByTestId("guest-imprint")).ToHaveTextAsync(imprint);
        await Assertions.Expect(line.GetByTestId("guest-privacy")).ToHaveAttributeAsync("href", "/datenschutz");
        await Assertions.Expect(line.GetByTestId("guest-privacy")).ToHaveTextAsync(privacy);
        // Nothing visible below it
        var below = await line.EvaluateAsync<string[]>("""
            line => {
                const bottom = line.getBoundingClientRect().bottom;
                return [...document.body.querySelectorAll('*')]
                    .filter(e => !line.contains(e) && !e.contains(line) && e.checkVisibility()
                        && !e.closest('dialog, template') && getComputedStyle(e).position !== 'fixed'
                        && e.getBoundingClientRect().top >= bottom - 1 && e.getBoundingClientRect().height > 0)
                    .map(e => e.tagName + '.' + e.className);
            }
            """);
        below.Should().BeEmpty($"the legal links close {page.Url}");
    }

    [Fact]
    public async Task GuestLinks_FollowTheSettings_AndStayAboveTheFavouritesButton()
    {
        try
        {
            // Only the configured link; both empty = no line
            Configuration["Legal:PrivacyUrl"] = "";
            await OpenGuestPageAsync(Page, "/demo");
            await Assertions.Expect(Page.GetByTestId("guest-imprint")).ToBeVisibleAsync();
            await Assertions.Expect(Page.GetByTestId("guest-privacy")).ToHaveCountAsync(0);
            Configuration["Legal:ImprintUrl"] = "";
            await OpenGuestPageAsync(Page, "/demo");
            await Assertions.Expect(Page.GetByTestId("guest-legal")).ToHaveCountAsync(0);
        }
        finally
        {
            Configuration["Legal:ImprintUrl"] = "/impressum";
            Configuration["Legal:PrivacyUrl"] = "/datenschutz";
        }

        // A phone with a favourite: the floating button never covers the links at the end of the page
        foreach (var theme in new[] { "modern", "classic", "dark" })
        {
            await Page.SetViewportSizeAsync(390, 844);
            await OpenGuestPageAsync(Page, $"/demo?theme={theme}");
            await Page.GetByTestId("favorite-button").First.ClickAsync();
            await Assertions.Expect(Page.GetByTestId("favorites-button")).ToBeVisibleAsync();
            await Page.EvaluateAsync("() => window.scrollTo(0, document.documentElement.scrollHeight)");
            var overlap = await Page.EvaluateAsync<bool>("""
                () => {
                    const fab = document.querySelector('[data-testid=favorites-button]').getBoundingClientRect();
                    return [...document.querySelectorAll('[data-testid=guest-legal] a')].some(a => {
                        const r = a.getBoundingClientRect();
                        return r.left < fab.right && r.right > fab.left && r.top < fab.bottom && r.bottom > fab.top;
                    });
                }
                """);
            overlap.Should().BeFalse($"the favourites button leaves the legal links free ({theme})");
            // Readable in every theme: the muted text colour against the page background
            var contrast = await Page.GetByTestId("guest-privacy").EvaluateAsync<double>("""
                a => {
                    const rgb = s => s.match(/\d+(\.\d+)?/g).slice(0, 3).map(Number);
                    const lum = c => {
                        const [r, g, b] = c.map(v => { v /= 255; return v <= 0.03928 ? v / 12.92 : ((v + 0.055) / 1.055) ** 2.4; });
                        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
                    };
                    const fg = lum(rgb(getComputedStyle(a).color));
                    const bg = lum(rgb(getComputedStyle(a.closest('.gm-legal')).backgroundColor));
                    return (Math.max(fg, bg) + 0.05) / (Math.min(fg, bg) + 0.05);
                }
                """);
            contrast.Should().BeGreaterThan(4.5, $"AA contrast in the {theme} theme");
            await Page.EvaluateAsync("() => window.localStorage.clear()");
        }
    }
}
