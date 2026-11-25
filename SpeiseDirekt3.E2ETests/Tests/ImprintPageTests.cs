using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// The imprint <c>/impressum</c> (AP15a, <c>Components/Legal/ImprintPage</c>): the operator's German legal text for every
/// visitor (<c>lang="de"</c>), the chrome in the browser's language, the contact address from <c>Landing:ContactEmail</c>
/// (a placeholder while it is empty), the footer links of the landing page and the account pages
/// (<c>Legal:ImprintUrl</c> = <c>/impressum</c> in appsettings.json), nothing external, no sideways scrolling.
/// </summary>
public class ImprintPageTests : E2ETestBase
{
    public ImprintPageTests(E2EFixture fixture) : base(fixture) { }

    public sealed record Chrome(string Locale, string Lang, string Title, string LogIn, string Features, string Legal, string ImprintLink);

    private static readonly Chrome German = new("de-DE", "de", "Impressum | Speise-Direkt", "Anmelden", "Funktionen", "Rechtliches", "Impressum");
    private static readonly Chrome English = new("en-US", "en", "Legal notice (Impressum) | Speise-Direkt", "Log in", "Features", "Legal", "Imprint");

    public static TheoryData<string> Languages => new() { "de", "en" };

    private static Chrome ChromeOf(string language) => language == "de" ? German : English;

    private ILocator Imprint(IPage page) => page.GetByTestId("imprint");

    private IConfiguration Configuration => App.ServerServices.GetRequiredService<IConfiguration>();

    /// <summary>Loads <c>/impressum</c> (200) and waits for <c>js/landing.js</c> of the landing document.</summary>
    private static async Task<IResponse> OpenImprintAsync(IPage page)
    {
        var response = await page.GotoAsync("/impressum");
        response!.Status.Should().Be(200);
        await page.Locator("html[data-landing-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
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

    [Theory]
    [MemberData(nameof(Languages))]
    public async Task Imprint_IsGermanForEveryVisitor_TheChromeFollowsTheBrowser(string language)
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

        var response = await OpenImprintAsync(page);

        // The landing document in the visitor's language; indexable with a canonical URL
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("lang", chrome.Lang);
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("data-landing-page", new Regex(".*"));
        await Assertions.Expect(page).ToHaveTitleAsync(chrome.Title);
        await Assertions.Expect(page.Locator("link[rel='canonical']")).ToHaveAttributeAsync("href", App.ServerAddress + "/impressum");
        await Assertions.Expect(page.Locator("meta[name='robots']")).ToHaveCountAsync(0);
        response.Headers.Should().NotContainKey("x-robots-tag");
        response.Headers["vary"].Should().Contain("Accept-Language");
        // No form, so no cookie either (not even the antiforgery cookie of static SSR, Components/CookielessPages.cs)
        response.Headers.Should().NotContainKey("set-cookie");
        (await context.CookiesAsync()).Should().BeEmpty();

        // The legal text: German, lang="de", the operator's sections in his order
        var imprint = Imprint(page);
        await Assertions.Expect(imprint).ToHaveAttributeAsync("lang", "de");
        await Assertions.Expect(imprint.Locator("h1")).ToHaveTextAsync("Impressum");
        await Assertions.Expect(page.Locator("h1")).ToHaveCountAsync(1);
        await Assertions.Expect(imprint).ToContainTextAsync("Angaben gemäß § 5 E-Commerce-Gesetz (ECG) und § 25 Mediengesetz (MedienG).");
        (await imprint.Locator("h2").AllTextContentsAsync()).Select(t => t.Trim()).Should().Equal(
            "Inhaber / Medieninhaber", "Kontakt", "Unternehmen", "Umsatzsteuer", "Anwendbares Recht", "Verbraucherstreitbeilegung",
            "Haftung für Inhalte", "Haftung für Links", "Urheberrecht");
        await Assertions.Expect(page.GetByTestId("imprint-owner").Locator("p")).ToHaveTextAsync(
            new Regex(@"^\s*Bernhard Grimm\s+Mühlgasse 106\s+2500 Baden\s+Österreich\s*$"));
        var company = page.GetByTestId("imprint-company");
        await Assertions.Expect(company).ToContainTextAsync("Rechtsform: Einzelunternehmen");
        await Assertions.Expect(company).ToContainTextAsync("Unternehmensgegenstand: IT-Beratung und Softwareentwicklung");
        await Assertions.Expect(company).ToContainTextAsync("Auftritt unter dem Namen: Speise-Direkt");
        await Assertions.Expect(page.GetByTestId("imprint-vat").Locator("p")).ToHaveTextAsync(
            "Hinweis gemäß § 6 Abs. 1 Z 27 UStG: Kleinunternehmer — daher keine Umsatzsteuer ausgewiesen, kein Vorsteuerabzug.");
        await Assertions.Expect(page.GetByTestId("imprint-vat").Locator("strong")).ToHaveTextAsync("Kleinunternehmer");
        await Assertions.Expect(page.GetByTestId("imprint-law")).ToContainTextAsync("Erfüllungsort und Gerichtsstand: 2500 Baden, Österreich.");
        // The EU ODR platform is gone (20 July 2025), so is the section; no phone number
        await Assertions.Expect(imprint).Not.ToContainTextAsync("Online-Streitbeilegung");
        await Assertions.Expect(page.Locator("a[href*='ec.europa.eu'], a[href^='tel:']")).ToHaveCountAsync(0);
        await Assertions.Expect(imprint).Not.ToContainTextAsync("Telefon");

        // The chrome in the visitor's language: the note only for a non-German visitor, outside the German text's lang
        await Assertions.Expect(page.GetByTestId("landing-login")).ToHaveTextAsync(chrome.LogIn);
        await Assertions.Expect(imprint.Locator(".lp-eyebrow")).ToHaveAttributeAsync("lang", chrome.Lang);
        await Assertions.Expect(imprint.Locator(".lp-eyebrow")).ToHaveTextAsync(chrome.Legal);
        var note = page.GetByTestId("imprint-language-note");
        if (language == "de")
            await Assertions.Expect(note).ToHaveCountAsync(0);
        else
        {
            await Assertions.Expect(note).ToHaveTextAsync("This legal notice is provided in German.");
            await Assertions.Expect(note).ToHaveAttributeAsync("lang", "en");
        }

        // Header and footer lead back to the landing page's sections; the footer names the imprint and the privacy policy
        // (AP16b), and no demo note (there are no examples here)
        var features = page.GetByTestId("landing-nav").GetByRole(AriaRole.Link, new() { Name = chrome.Features });
        await Assertions.Expect(features).ToHaveAttributeAsync("href", "/#funktionen");
        await Assertions.Expect(page.GetByTestId("footer-nav").GetByRole(AriaRole.Link, new() { Name = chrome.Features }))
            .ToHaveAttributeAsync("href", "/#funktionen");
        await Assertions.Expect(page.GetByTestId("footer-imprint")).ToHaveAttributeAsync("href", "/impressum");
        await Assertions.Expect(page.GetByTestId("footer-imprint")).ToHaveTextAsync(chrome.ImprintLink);
        await Assertions.Expect(page.GetByTestId("footer-privacy")).ToHaveAttributeAsync("href", "/datenschutz");
        await Assertions.Expect(page.GetByTestId("footer-demo-note")).ToHaveCountAsync(0);

        // Only the landing document's own assets, nothing from another host
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);
        external.Should().BeEmpty("the imprint makes no external request");
        await Assertions.Expect(page.Locator("script[src*='tailwind'], link[href*='tailwind'], link[href*='bootstrap'], script[src*='blazor']")).ToHaveCountAsync(0);

        // No sideways scrolling on a small phone and on a desktop
        foreach (var width in new[] { 360, 1280 })
        {
            await page.SetViewportSizeAsync(width, 800);
            await page.EvaluateAsync("() => document.fonts.ready");
            (await WideElementsAsync(page)).Should().BeEmpty($"nothing is wider than the viewport at {width} px");
        }

        // A click on a section link returns to the landing page's section
        await features.ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/#funktionen$"));
        await Assertions.Expect(page.GetByTestId("landing-hero")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Contact_ShowsAPlaceholder_UntilTheContactEmailIsSet_ThenAMailtoLink()
    {
        await OpenImprintAsync(Page);
        var contact = Page.GetByTestId("imprint-contact");
        await Assertions.Expect(Page.GetByTestId("imprint-email-placeholder")).ToHaveTextAsync("E-Mail-Adresse folgt in Kürze.");
        await Assertions.Expect(Page.GetByTestId("imprint-email-placeholder")).ToBeVisibleAsync();
        await Assertions.Expect(contact.Locator("a")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("a[href^='mailto:']")).ToHaveCountAsync(0, new() { Timeout = 1_000 });

        try
        {
            Configuration["Landing:ContactEmail"] = "kontakt@speisedirekt.example";
            await OpenImprintAsync(Page);
            await Assertions.Expect(Page.GetByTestId("imprint-email")).ToHaveAttributeAsync("href", "mailto:kontakt@speisedirekt.example");
            await Assertions.Expect(Page.GetByTestId("imprint-email")).ToHaveTextAsync("kontakt@speisedirekt.example");
            await Assertions.Expect(contact.Locator("p")).ToHaveTextAsync("E-Mail: kontakt@speisedirekt.example");
            await Assertions.Expect(Page.GetByTestId("imprint-email-placeholder")).ToHaveCountAsync(0);
            // The same one address in the footer
            await Assertions.Expect(Page.GetByTestId("footer-contact")).ToHaveAttributeAsync("href", "mailto:kontakt@speisedirekt.example");

            // An unusable value is never shown: the placeholder again
            Configuration["Landing:ContactEmail"] = "kein mail";
            await OpenImprintAsync(Page);
            await Assertions.Expect(Page.GetByTestId("imprint-email-placeholder")).ToBeVisibleAsync();
            await Assertions.Expect(Page.GetByTestId("imprint-email")).ToHaveCountAsync(0);
        }
        finally
        {
            Configuration["Landing:ContactEmail"] = "";
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public async Task FooterLinks_OfTheLandingPageAndTheAccountPages_LeadToTheImprint(string language)
    {
        var chrome = ChromeOf(language);
        var (context, page) = await NewContextAsync(chrome.Locale);
        await using var _ = context;

        // Landing page: "Impressum"/"Imprint" under "Rechtliches"/"Legal", next to the privacy policy (AP16b)
        await page.GotoAsync("/");
        await page.Locator("html[data-landing-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
        var imprintLink = page.GetByTestId("footer-imprint");
        await Assertions.Expect(page.GetByTestId("footer-legal")).ToBeVisibleAsync();
        await Assertions.Expect(imprintLink).ToHaveAttributeAsync("href", "/impressum");
        await Assertions.Expect(imprintLink).ToHaveTextAsync(chrome.ImprintLink);
        await Assertions.Expect(page.GetByTestId("footer-privacy")).ToHaveAttributeAsync("href", "/datenschutz");
        (await context.CookiesAsync()).Should().BeEmpty("the landing page has no form, so no cookie either");
        await imprintLink.ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/impressum$"));
        await Assertions.Expect(Imprint(page).Locator("h1")).ToHaveTextAsync("Impressum");

        // An account page (the [AuthPage] document) has the same link in its footer
        await page.GotoAsync("/Account/Login");
        var authImprint = page.GetByTestId("auth-imprint");
        await Assertions.Expect(authImprint).ToHaveAttributeAsync("href", "/impressum");
        await Assertions.Expect(authImprint).ToHaveTextAsync(chrome.ImprintLink);
        await Assertions.Expect(page.GetByTestId("auth-privacy")).ToHaveAttributeAsync("href", "/datenschutz");
        await authImprint.ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/impressum$"));
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("lang", chrome.Lang);
    }

    [Fact]
    public async Task SignedInUser_ReadsTheImprint_WithoutARedirect()
    {
        await RegisterNewOwnerAsync();

        var response = await Page.APIRequest.GetAsync("/impressum", new() { MaxRedirects = 0 });
        response.Status.Should().Be(200, "unlike /, the imprint does not send a signed-in user to the dashboard");
        await OpenImprintAsync(Page);
        await Assertions.Expect(Imprint(Page).Locator("h1")).ToHaveTextAsync("Impressum");
    }
}
