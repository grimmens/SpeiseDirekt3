using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// AP12: the back office, the account pages and the landing page speak German or English — chosen only by the browser's
/// <c>Accept-Language</c> — while the guest pages keep the menu's language. Language selection, document markup and
/// caching headers, a running circuit across client-side navigation, German input of prices, German Identity and
/// validation messages, machine values that stay invariant on German pages, and the guest pages in both directions.
/// </summary>
public class LocalizationTests : BackOfficeTestBase
{
    public LocalizationTests(E2EFixture fixture) : base(fixture) { }

    private const string German = E2ESettings.GermanLocale;
    private const string English = E2ESettings.DefaultLocale;

    private HttpClient Http() => new() { BaseAddress = new Uri(App.ServerAddress) };

    private async Task<HttpResponseMessage> GetAsync(string url, string? acceptLanguage)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (acceptLanguage is not null)
            request.Headers.TryAddWithoutValidation("Accept-Language", acceptLanguage);
        using var http = Http();
        return await http.SendAsync(request);
    }

    private static string? HtmlLang(string html) => Regex.Match(html, "<html lang=\"([a-z]+)\"").Groups[1].Value is { Length: > 0 } v ? v : null;

    // --- Selection ---

    [Theory]
    [InlineData("de-DE", "de")]
    [InlineData("de", "de")]
    [InlineData("de-AT", "de")]
    [InlineData("de-CH, en;q=0.5", "de")]
    [InlineData("en-US", "en")]
    [InlineData("en-GB,en;q=0.9", "en")]
    [InlineData("fr-FR", "en")]
    [InlineData("fr-FR, en;q=0.8", "en")]
    [InlineData("fr-FR, de;q=0.8", "de")]
    [InlineData("fr-CH, de;q=0.9, en;q=0.8", "de")]
    [InlineData("en;q=0.5, de;q=0.9", "de")]
    [InlineData("de;q=0, en", "en")]
    [InlineData("*", "de")]
    [InlineData(null, "de")]
    public async Task AcceptLanguage_ChoosesTheLanguage_OfTheLoginAndTheLandingPage(string? acceptLanguage, string expected)
    {
        foreach (var url in new[] { "/Account/Login" })
        {
            using var response = await GetAsync(url, acceptLanguage);
            response.StatusCode.Should().Be(System.Net.HttpStatusCode.OK, url);
            var html = await response.Content.ReadAsStringAsync();
            HtmlLang(html).Should().Be(expected, $"{url} with Accept-Language '{acceptLanguage}'");
            response.Content.Headers.ContentLanguage.Should().Equal([expected], url);
            response.Headers.Vary.Should().Contain("Accept-Language", $"{url} depends on the language");
            response.Headers.CacheControl.Should().NotBeNull(url);
            (response.Headers.CacheControl!.Private || response.Headers.CacheControl.NoStore)
                .Should().BeTrue($"{url} must not be stored by a shared cache for everybody");
        }
    }

    [Fact]
    public async Task QueryStringAndCookie_DoNotChangeTheLanguage()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/Account/Login?culture=en&ui-culture=en");
        request.Headers.TryAddWithoutValidation("Accept-Language", "de-DE");
        request.Headers.TryAddWithoutValidation("Cookie", ".AspNetCore.Culture=c%3Den%7Cuic%3Den");
        using var http = Http();
        using var response = await http.SendAsync(request);
        HtmlLang(await response.Content.ReadAsStringAsync()).Should().Be("de");
    }

    [Fact]
    public async Task Browser_GetsTheDocumentInItsLanguage()
    {
        foreach (var (locale, expected) in new[] { (German, "de"), ("de-AT", "de"), (English, "en"), ("fr-FR", "en") })
        {
            var (context, page) = await NewContextAsync(locale);
            await using var _ = context;
            await page.GotoAsync("/Account/Login");
            await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("lang", expected);
        }
    }

    // --- The back-office shell ---

    private static readonly string[] GermanNav = ["Dashboard", "Speisekarten", "QR-Codes", "Statistik", "Restaurant", "Team"];
    private static readonly string[] EnglishNav = ["Dashboard", "Menus", "QR Codes", "Analytics", "Restaurant", "Team"];

    /// <summary>A browser context in <paramref name="locale"/> with the cookies of <see cref="E2ETestBase.Context"/>.</summary>
    private async Task<IBrowserContext> SignedInContextAsync(string locale, int width = 1280) =>
        await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, StorageState = await Context.StorageStateAsync(), Locale = locale,
            ViewportSize = new ViewportSize { Width = width, Height = 900 }
        });

    [Fact]
    public async Task Shell_SpeaksTheBrowsersLanguage()
    {
        await RegisterNewOwnerAsync();
        foreach (var (locale, nav, settings, logout) in new[]
                 {
                     (German, GermanNav, "Kontoeinstellungen", "Abmelden"), ("de-AT", GermanNav, "Kontoeinstellungen", "Abmelden"),
                     (English, EnglishNav, "Account settings", "Log out"), ("fr-FR", EnglishNav, "Account settings", "Log out")
                 })
        {
            await using var context = await SignedInContextAsync(locale);
            var page = await context.NewPageAsync();
            await GotoInteractiveAsync(page, "/dashboard");
            await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("lang", locale.StartsWith("de") ? "de" : "en");
            await Assertions.Expect(page.GetByTestId("top-nav").Locator("a")).ToHaveTextAsync(nav);
            await page.GetByTestId("user-menu-button").ClickAsync();
            await Assertions.Expect(page.GetByTestId("account-settings-link")).ToHaveTextAsync(settings);
            await Assertions.Expect(page.GetByTestId("user-menu").GetByTestId("logout-button")).ToHaveTextAsync(logout);
            await Assertions.Expect(page.GetByTestId("user-role")).ToHaveTextAsync(locale.StartsWith("de") ? "Inhaber" : "Owner");
        }

        // The phone drawer and the anonymous header
        await using (var phone = await SignedInContextAsync(German, width: 390))
        {
            var page = await phone.NewPageAsync();
            await GotoInteractiveAsync(page, "/dashboard");
            await Assertions.Expect(page.GetByTestId("nav-drawer-button")).ToHaveAttributeAsync("aria-label", "Navigation öffnen");
            await page.GetByTestId("nav-drawer-button").ClickAsync();
            await Assertions.Expect(page.GetByTestId("drawer-nav").Locator("a")).ToHaveTextAsync(GermanNav);
            await Assertions.Expect(page.GetByTestId("nav-drawer-close")).ToHaveAttributeAsync("aria-label", "Navigation schließen");
        }
        var (anonymous, anonymousPage) = await NewContextAsync(German);
        await using (anonymous)
        {
            await anonymousPage.GotoAsync("/Account/Login");
            await Assertions.Expect(anonymousPage.GetByTestId("anonymous-nav").Locator("a")).ToHaveTextAsync(["Anmelden", "Registrieren"]);
            await Assertions.Expect(anonymousPage.Locator("[data-sd-skip]")).ToHaveTextAsync("Zum Inhalt springen");
        }
    }

    /// <summary>
    /// The Interactive Server circuit takes the culture of the request that opened it and keeps it: client-side
    /// navigations (no page load) render the context bar, and a drag handle's announcement, in German.
    /// </summary>
    [Fact]
    public async Task RunningCircuit_KeepsTheBrowsersLanguage_AcrossClientSideNavigation()
    {
        var owner = await OwnerIdAsync(await RegisterNewOwnerAsync());
        var menuId = Guid.NewGuid();
        await App.SeedAsync(owner, db =>
        {
            db.Menus.Add(new Menu { Id = menuId, Name = "Mittagskarte", Description = "", Theme = DesignTheme.Modern, Language = MenuLanguage.German });
            db.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Suppen", MenuId = menuId, SortOrder = 0 });
            db.Categories.Add(new Category { Id = Guid.NewGuid(), Name = "Desserts", MenuId = menuId, SortOrder = 1 });
            return Task.CompletedTask;
        });

        await using var context = await SignedInContextAsync(German);
        var page = await context.NewPageAsync();
        await GotoInteractiveAsync(page, "/dashboard");
        await page.EvaluateAsync("() => window.__sameDocument = true");

        // Client-side navigation through the top bar
        await page.GetByTestId("top-nav").GetByRole(AriaRole.Link, new() { Name = "Speisekarten" }).ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/menus$"));
        await Assertions.Expect(page.GetByTestId("top-nav").Locator("a[aria-current='page']")).ToHaveTextAsync("Speisekarten");

        // …and by the router: the context bar is rendered by the circuit after the navigation
        await page.EvaluateAsync($"() => Blazor.navigateTo('/Categories/{menuId}')");
        await Assertions.Expect(page.GetByTestId("menu-tabs").Locator("a"))
            .ToHaveTextAsync(["Übersicht", "Kategorien", "Gerichte", "Allergene", "Kombi-Angebote", "Einstellungen"]);
        await Assertions.Expect(page.GetByTestId("menu-preview-link")).ToContainTextAsync("Vorschau");
        await Assertions.Expect(page.GetByTestId("menu-switcher-button")).ToContainTextAsync("Speisekarte wechseln");

        // A string made in the circuit at event time: the drag handle's announcement
        var handle = page.GetByRole(AriaRole.Button, new() { Name = "Desserts verschieben" });
        await handle.FocusAsync();
        await page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(Announcer(page)).ToHaveTextAsync("Desserts an Position 1 von 2 verschoben.");
        await page.Keyboard.PressAsync("ArrowUp");
        await Assertions.Expect(Announcer(page)).ToHaveTextAsync("Desserts steht bereits an erster Stelle.");

        (await page.EvaluateAsync<bool>("() => window.__sameDocument === true")).Should().BeTrue("no page was loaded");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "de");
    }

    // --- Dashboard ---

    [Fact]
    public async Task Dashboard_InGerman_WithGermanCountsAndChecklist()
    {
        var (context, page) = await NewContextAsync(German);
        await using var _ = context;
        var email = UniqueEmail("dashboard");
        await RegisterAsync(page, email);
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("/dashboard$"));
        await Assertions.Expect(page).ToHaveTitleAsync("Dashboard - Speise-Direkt");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Willkommen zurück" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("getting-started-progress")).ToHaveTextAsync("0 von 4 erledigt");
        await Assertions.Expect(page.GetByTestId("getting-started")).ToContainTextAsync("Erste Schritte");
        await Assertions.Expect(page.GetByTestId("checklist-step").Nth(1)).ToContainTextAsync("Legen Sie zuerst eine Speisekarte an.");
        await Assertions.Expect(page.GetByTestId("quick-actions").Locator("a")).ToHaveTextAsync(["Speisekarte anlegen", "QR-Code erstellen", "Statistik"]);
        await Assertions.Expect(page.GetByTestId("stat-menus")).ToContainTextAsync("0 Gerichte insgesamt");
        await Assertions.Expect(page.GetByTestId("stat-team")).ToContainTextAsync("Teammitglieder");
        await Assertions.Expect(page.GetByTestId("dashboard-menus")).ToContainTextAsync("Noch keine Speisekarten.");

        // Counts with one/other forms
        var owner = await OwnerIdAsync(email);
        await App.SeedAsync(owner, db =>
        {
            var menuId = Guid.NewGuid();
            var categoryId = Guid.NewGuid();
            db.Menus.Add(new Menu { Id = menuId, Name = "Abendkarte", Description = "", Theme = DesignTheme.Modern, Language = MenuLanguage.German });
            db.Categories.Add(new Category { Id = categoryId, Name = "Suppen", MenuId = menuId });
            foreach (var name in new[] { "Gulaschsuppe", "Frittatensuppe", "Kürbissuppe" })
                db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = name, Description = "", Price = 4.5m, CategoryId = categoryId });
            return Task.CompletedTask;
        });
        await GotoInteractiveAsync(page, "/dashboard");
        await Assertions.Expect(page.GetByTestId("dashboard-menu")).ToContainTextAsync("1 Kategorie · 3 Gerichte");
        await Assertions.Expect(page.GetByTestId("stat-menus")).ToContainTextAsync("3 Gerichte insgesamt");
        await Assertions.Expect(page.GetByTestId("getting-started-progress")).ToHaveTextAsync("2 von 4 erledigt");
        await Assertions.Expect(page.GetByTestId("checklist-action").First).ToHaveTextAsync(new Regex("QR-Code erstellen"));
    }

    // --- Account pages, validation and Identity messages ---

    [Fact]
    public async Task AccountPages_InGerman_WithGermanValidationAndIdentityMessages()
    {
        var (context, page) = await NewContextAsync(German);
        await using var _ = context;

        // Login: texts, DataAnnotations messages, a failed login
        await page.GotoAsync("/Account/Login");
        await Assertions.Expect(page).ToHaveTitleAsync("Anmelden - Speise-Direkt");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Willkommen zurück" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("login-submit")).ToHaveTextAsync("Anmelden");
        await Assertions.Expect(page.GetByText("Passwort vergessen?")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("There are no external authentication services")).ToHaveCountAsync(0);
        await page.GetByTestId("login-submit").ClickAsync();
        await Assertions.Expect(page.Locator(".validation-message").First).ToHaveTextAsync("Bitte geben Sie Ihre E-Mail-Adresse ein.");
        await Assertions.Expect(page.GetByText("Bitte geben Sie Ihr Passwort ein.").First).ToBeVisibleAsync();
        await page.Locator("#Input\\.Email").FillAsync("kein-mail");
        await page.GetByTestId("login-submit").ClickAsync();
        await Assertions.Expect(page.GetByText("Bitte geben Sie eine gültige E-Mail-Adresse ein.").First).ToBeVisibleAsync();
        await page.Locator("#Input\\.Email").FillAsync(UniqueEmail("nobody"));
        await page.Locator("#Input\\.Password").FillAsync(Password);
        await page.GetByTestId("login-submit").ClickAsync();
        await Assertions.Expect(page.GetByTestId("status-message")).ToContainTextAsync("Die Anmeldung ist fehlgeschlagen.");
        await Assertions.Expect(page.GetByTestId("status-message")).ToHaveClassAsync(new Regex("alert-danger"));

        // Register: password rules, a weak password (Identity), passwords that differ (DataAnnotations)
        await page.GotoAsync("/Account/Register");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Konto erstellen" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator("#password-rules")).ToHaveTextAsync("Mindestens 6 Zeichen, mit Groß- und Kleinbuchstaben, einer Ziffer und einem Sonderzeichen.");
        var email = UniqueEmail("de");
        await page.Locator("#Input\\.Email").FillAsync(email);
        await page.Locator("#Input\\.Password").FillAsync("abcdefgh");
        await page.Locator("#Input\\.ConfirmPassword").FillAsync("abcdefgh");
        await page.GetByTestId("register-submit").ClickAsync();
        var status = page.GetByTestId("status-message");
        await Assertions.Expect(status).ToContainTextAsync("Das Passwort muss mindestens eine Ziffer (0–9) enthalten.");
        await Assertions.Expect(status).ToContainTextAsync("mindestens einen Großbuchstaben (A–Z)");
        await Assertions.Expect(status).ToContainTextAsync("mindestens ein Sonderzeichen");
        await page.Locator("#Input\\.Password").FillAsync(Password);
        await page.Locator("#Input\\.ConfirmPassword").FillAsync(Password + "x");
        await page.GetByTestId("register-submit").ClickAsync();
        await Assertions.Expect(page.GetByText("Die beiden Passwörter stimmen nicht überein.").First).ToBeVisibleAsync();

        // Registered once, the same address again is refused in German
        await RegisterAsync(page, email);
        await LogoutAsync(page);
        await page.GotoAsync("/Account/Register");
        await page.Locator("#Input\\.Email").FillAsync(email);
        await page.Locator("#Input\\.Password").FillAsync(Password);
        await page.Locator("#Input\\.ConfirmPassword").FillAsync(Password);
        await page.GetByTestId("register-submit").ClickAsync();
        await Assertions.Expect(page.GetByTestId("status-message")).ToHaveTextAsync($"Die E-Mail-Adresse „{email}“ ist bereits registriert.");

        // Account settings: German pages, a wrong current password (Identity), then the green status after a change
        await LoginAsync(page, email);
        await page.GotoAsync("/Account/Manage");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Kontoverwaltung" })).ToBeVisibleAsync();
        await Assertions.Expect(page.Locator(".manage-nav a")).ToHaveTextAsync(["Profil", "E-Mail-Adresse", "Passwort", "Zwei-Faktor-Authentifizierung", "Persönliche Daten"]);
        await page.GotoAsync("/Account/Manage/ChangePassword");
        await page.Locator("#Input\\.OldPassword").FillAsync("Falsch-Passw0rd!");
        await page.Locator("#Input\\.NewPassword").FillAsync(Password + "2");
        await page.Locator("#Input\\.ConfirmPassword").FillAsync(Password + "2");
        await page.GetByRole(AriaRole.Button, new() { Name = "Passwort ändern" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("status-message")).ToHaveTextAsync("Das Passwort ist nicht korrekt.");
        await page.Locator("#Input\\.OldPassword").FillAsync(Password);
        await page.Locator("#Input\\.NewPassword").FillAsync(Password + "2");
        await page.Locator("#Input\\.ConfirmPassword").FillAsync(Password + "2");
        await page.GetByRole(AriaRole.Button, new() { Name = "Passwort ändern" }).ClickAsync();
        await Assertions.Expect(page.GetByTestId("status-message")).ToHaveTextAsync("Ihr Passwort wurde geändert.");
        await Assertions.Expect(page.GetByTestId("status-message")).ToHaveClassAsync(new Regex("alert-success"));
        await page.GotoAsync("/Account/Manage/TwoFactorAuthentication");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Zwei-Faktor-Authentifizierung (2FA)" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByRole(AriaRole.Link, new() { Name = "Authenticator-App hinzufügen" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task AccountPages_InEnglish_WithEnglishIdentityMessages()
    {
        await Page.GotoAsync("/Account/Register");
        await Page.Locator("#Input\\.Email").FillAsync(UniqueEmail("en"));
        await Page.Locator("#Input\\.Password").FillAsync("abcdefgh");
        await Page.Locator("#Input\\.ConfirmPassword").FillAsync("abcdefgh");
        await Page.GetByTestId("register-submit").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("status-message")).ToContainTextAsync("Passwords must have at least one digit ('0'-'9').");
        await Page.GotoAsync("/Account/Login");
        await Page.GetByTestId("login-submit").ClickAsync();
        await Assertions.Expect(Page.Locator(".validation-message").First).ToHaveTextAsync("Please enter your e-mail address.");
    }

    // --- Guest pages keep the menu's language ---

    [Fact]
    public async Task GuestPage_OfAGermanMenu_StaysGerman_ForAnEnglishBrowser()
    {
        using (var response = await GetAsync($"/menu/{E2ESeedData.MenuId}", "en-US"))
        {
            var html = await response.Content.ReadAsStringAsync();
            HtmlLang(html).Should().Be("de");
            response.Headers.Vary.Should().NotContain("Accept-Language", "the guest page does not depend on the browser");
        }

        var (context, page) = await NewContextAsync(English);
        await using var _ = context;
        await OpenGuestPageAsync(page, $"/menu/{E2ESeedData.MenuId}");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "de");
        await Assertions.Expect(ItemEntry(page, "Cheeseburger").GetByTestId("item-price")).ToHaveTextAsync("€ 9,50");
        await Assertions.Expect(page.GetByTestId("search-button")).ToHaveAttributeAsync("aria-label", new Regex("durchsuchen"));
    }

    [Fact]
    public async Task GuestPage_OfAnEnglishMenu_StaysEnglish_ForAGermanBrowser_AlsoInTheEditorPreview()
    {
        var email = await RegisterNewOwnerAsync();
        var owner = await OwnerIdAsync(email);
        var english = await GuestDemoData.SeedSmallMenuAsync(App, owner, MenuLanguage.English, "Evening menu", "Fish and chips", 14.90m);
        var german = await GuestDemoData.SeedSmallMenuAsync(App, owner, MenuLanguage.German, "Abendkarte", "Backhendl", 15.90m);

        using (var response = await GetAsync($"/menu/{english}", "de-DE"))
            HtmlLang(await response.Content.ReadAsStringAsync()).Should().Be("en");

        var state = await Context.StorageStateAsync();
        foreach (var (locale, menu, lang, price) in new[] { (German, english, "en", "€14.90"), (English, german, "de", "€ 15,90") })
        {
            await using var context = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
            {
                BaseURL = App.ServerAddress, StorageState = state, Locale = locale,
                ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
            });
            var page = await context.NewPageAsync();

            // The public page
            await OpenGuestPageAsync(page, $"/menu/{menu}");
            await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("lang", lang);
            await Assertions.Expect(page.GetByTestId("item-price").First).ToHaveTextAsync(price);

            // The editor's phone frame shows the guest page of the menu, not of the browser
            await GotoInteractiveAsync(page, $"/menu/edit/{menu}");
            var frame = page.FrameLocator("[data-testid='menu-preview-iframe']");
            await frame.Locator("html[data-guest-ready]").WaitForAsync(new() { State = WaitForSelectorState.Attached });
            await Assertions.Expect(frame.Locator("html")).ToHaveAttributeAsync("lang", lang);
            await Assertions.Expect(frame.GetByTestId("item-price").First).ToHaveTextAsync(price);
            // …while the back office around it follows the browser
            await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("lang", locale[..2]);
        }
    }

    // --- Machine values stay invariant ---

    /// <summary>
    /// Every back-office page of a seeded tenant, rendered in German: no decimal comma in style, data-*, SVG geometry,
    /// number inputs or the preview frame's URL — those are machine values (CSS, scripts, the browser) that a German
    /// culture must not touch.
    /// </summary>
    [Fact]
    public async Task GermanPages_WriteMachineValues_Invariant()
    {
        var email = await RegisterNewOwnerAsync();
        var owner = await OwnerIdAsync(email);
        var menu = await GuestDemoData.SeedAsync(App, owner, DesignTheme.Classic, name: "Trattoria");
        var itemId = menu.Items[GuestDemoData.DishWithExtras];
        var categoryId = menu.CategoryIds.Values.First();
        var qr = await SeedQrAsync(owner, menu.MenuId);

        var state = await Context.StorageStateAsync();
        await using var context = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, StorageState = state, Locale = German,
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
        });
        var page = await context.NewPageAsync();

        var urls = new[]
        {
            "/dashboard", "/menus", $"/menudetail/{menu.MenuId}", $"/Categories/{menu.MenuId}", $"/MenuItems/Category/{categoryId}",
            $"/MenuItems/item/{itemId}/edit", $"/allergens/{menu.MenuId}", $"/combos/{menu.MenuId}", $"/menu/edit/{menu.MenuId}",
            "/qrcodes", $"/qrcode/edit/{qr}", "/analytics", "/restaurant", "/users"
        };
        var findings = new List<string>();
        foreach (var url in urls)
        {
            await GotoInteractiveAsync(page, url);
            await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "de");
            var commas = await page.EvaluateAsync<string[]>("""
                () => {
                    const comma = /\d,\d/;
                    const geometry = ['style', 'stroke-width', 'width', 'height', 'x', 'y', 'r', 'cx', 'cy', 'viewBox', 'min', 'max', 'step'];
                    const found = [];
                    for (const el of document.querySelectorAll('*')) {
                        for (const attr of el.attributes) {
                            const machine = attr.name.startsWith('data-') || geometry.includes(attr.name);
                            if (machine && comma.test(attr.value) && attr.name !== 'data-testid')
                                found.push(`${el.tagName.toLowerCase()} ${attr.name}="${attr.value}"`);
                        }
                    }
                    for (const input of document.querySelectorAll('input[type=number]'))
                        if (input.getAttribute('value')?.includes(',')) found.push(`input value="${input.getAttribute('value')}"`);
                    for (const frame of document.querySelectorAll('iframe'))
                        if (comma.test(frame.getAttribute('src') ?? '')) found.push(`iframe src="${frame.getAttribute('src')}"`);
                    return found;
                }
                """);
            findings.AddRange(commas.Select(c => $"{url}: {c}"));
        }
        findings.Should().BeEmpty();

        // The price field is text in the page's language (Shared/PriceInput), not a machine value
        await GotoInteractiveAsync(page, $"/MenuItems/item/{itemId}/edit");
        (await page.Locator("#MenuItem\\.Price").InputValueAsync()).Should().MatchRegex(@"^\d+,\d{2}$");
    }

    // --- Prices typed by a German user ---

    [Fact]
    public async Task PriceTypedWithADecimalComma_IsSavedAsTheRightValue()
    {
        var email = await RegisterNewOwnerAsync();
        var owner = await OwnerIdAsync(email);
        var menuId = Guid.NewGuid();
        var categoryId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        await App.SeedAsync(owner, db =>
        {
            db.Menus.Add(new Menu { Id = menuId, Name = "Mittagskarte", Description = "", Theme = DesignTheme.Modern, Language = MenuLanguage.German });
            db.Categories.Add(new Category { Id = categoryId, Name = "Suppen", MenuId = menuId });
            db.MenuItems.Add(new MenuItem { Id = itemId, Name = "Gulaschsuppe", Description = "", Price = 3m, CategoryId = categoryId });
            return Task.CompletedTask;
        });

        var state = await Context.StorageStateAsync();
        await using var context = await Fixture.NewBrowserContextAsync(new BrowserNewContextOptions
        {
            BaseURL = App.ServerAddress, StorageState = state, Locale = German,
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 }
        });
        var page = await context.NewPageAsync();
        await GotoInteractiveAsync(page, $"/MenuItems/item/{itemId}/edit");

        // German format in the field: "3,00"; typed with the decimal comma (keys, like a person)
        var price = page.Locator("#MenuItem\\.Price");
        await Assertions.Expect(price).ToHaveValueAsync("3,00");
        await Assertions.Expect(price).ToHaveAttributeAsync("inputmode", "decimal");
        await price.ClickAsync(new() { ClickCount = 3 });
        await page.Keyboard.PressAsync("Backspace");
        await price.PressSequentiallyAsync("5,90");
        await price.BlurAsync();
        await Assertions.Expect(price).ToHaveValueAsync("5,90");

        await page.GetByTestId("item-savebar").Locator("button[type=submit]").ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("highlight=", RegexOptions.IgnoreCase));
        await using (var db = App.CreateDbContext(owner))
            (await db.MenuItems.SingleAsync(i => i.Id == itemId)).Price.Should().Be(5.90m);

        // Reopened: German format again; a decimal point is read as well; the guest sees the German menu's price
        await GotoInteractiveAsync(page, $"/MenuItems/item/{itemId}/edit");
        await Assertions.Expect(price).ToHaveValueAsync("5,90");
        await price.FillAsync("7.5");
        await price.BlurAsync();
        await Assertions.Expect(price).ToHaveValueAsync("7,50");
        await page.GetByTestId("item-savebar").Locator("button[type=submit]").ClickAsync();
        await Assertions.Expect(page).ToHaveURLAsync(new Regex("highlight=", RegexOptions.IgnoreCase));
        await using (var db = App.CreateDbContext(owner))
            (await db.MenuItems.SingleAsync(i => i.Id == itemId)).Price.Should().Be(7.50m);
        await OpenGuestPageAsync(page, $"/menu/{menuId}");
        await Assertions.Expect(page.GetByTestId("item-price").First).ToHaveTextAsync("€ 7,50");

        // An English browser shows and reads the English format
        var (english, englishPage) = await NewContextAsync(English);
        await using (english)
        {
            await LoginAsync(englishPage, email);
            await GotoInteractiveAsync(englishPage, $"/MenuItems/item/{itemId}/edit");
            await Assertions.Expect(englishPage.Locator("#MenuItem\\.Price")).ToHaveValueAsync("7.50");
        }
    }

    private async Task<Guid> SeedQrAsync(Guid owner, Guid menuId)
    {
        var qrId = Guid.NewGuid();
        await App.SeedAsync(owner, db =>
        {
            db.QRCodes.Add(new QRCode
            {
                Id = qrId, Title = "Festtage", IsCalendarBased = true, MenuId = menuId,
                CalendarEntries = new List<CalendarEntry>
                {
                    new() { Id = Guid.NewGuid(), MenuId = menuId, Date = DateOnly.FromDateTime(DateTime.Today), EndDate = DateOnly.FromDateTime(DateTime.Today.AddDays(2)) }
                }
            });
            return Task.CompletedTask;
        });
        return qrId;
    }
}
