using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Register, login, logout and the login redirect for protected pages; the anonymous account pages' own document and
/// layout (AP13: [AuthPage], css/auth.css — the landing page's look, no Tailwind/Bootstrap/blazor.web.js).
/// </summary>
public class AuthTests : E2ETestBase
{
    public AuthTests(E2EFixture fixture) : base(fixture) { }

    private ILocator LogoutButton => Page.GetByRole(AriaRole.Button, new() { Name = "Log out", Exact = true });

    /// <summary>The top bar's user menu shows the account; opened, it offers "Log out".</summary>
    private async Task ExpectSignedInAsync(string email)
    {
        var userMenu = Page.GetByTestId("user-menu-button");
        await Assertions.Expect(userMenu).ToContainTextAsync(email);
        await userMenu.ClickAsync();
        await Assertions.Expect(LogoutButton).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Register_SignsInNewAccount()
    {
        var email = await RegisterNewOwnerAsync();

        // The default return URL "/" is the landing page, which sends a logged-in user to the dashboard
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"^{Regex.Escape(App.ServerAddress)}/dashboard$"));
        await Assertions.Expect(Page.GetByText(email).First).ToBeVisibleAsync();
        await ExpectSignedInAsync(email);
    }

    [Fact]
    public async Task Register_PasswordMismatch_ShowsError()
    {
        await Page.GotoAsync("/Account/Register");
        await Page.Locator("#Input\\.Email").FillAsync(UniqueEmail());
        await Page.Locator("#Input\\.Password").FillAsync(Password);
        await Page.Locator("#Input\\.ConfirmPassword").FillAsync(Password + "x");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Create Account" }).ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/Account/Register"));
        await Assertions.Expect(Page.GetByText("do not match").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Logout_ThenLogin_Works()
    {
        var email = await RegisterNewOwnerAsync();

        await LogoutAsync(Page);
        await Assertions.Expect(Page.GetByTestId("login-submit")).ToBeVisibleAsync();
        await Assertions.Expect(LogoutButton).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("user-menu-button")).ToHaveCountAsync(0);

        await LoginAsync(Page, email);
        await Assertions.Expect(Page.GetByText(email).First).ToBeVisibleAsync();
        await ExpectSignedInAsync(email);
    }

    [Fact]
    public async Task Login_WrongPassword_ShowsError()
    {
        var email = await RegisterNewOwnerAsync();
        await LogoutAsync(Page);

        await Page.GotoAsync("/Account/Login");
        await Page.Locator("#Input\\.Email").FillAsync(email);
        await Page.Locator("#Input\\.Password").FillAsync("Wrong-Passw0rd!");
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();

        await Assertions.Expect(Page.GetByText("Invalid login attempt")).ToBeVisibleAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/Account/Login"));
    }

    [Fact]
    public async Task Login_UnknownUser_ShowsError()
    {
        await Page.GotoAsync("/Account/Login");
        await Page.Locator("#Input\\.Email").FillAsync(UniqueEmail("nobody"));
        await Page.Locator("#Input\\.Password").FillAsync(Password);
        await Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync();

        await Assertions.Expect(Page.GetByText("Invalid login attempt")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ProtectedPage_WithoutLogin_RedirectsToLogin_AndBackAfterLogin()
    {
        var email = await RegisterNewOwnerAsync();
        await LogoutAsync(Page);

        await Page.GotoAsync("/menus");

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/Account/Login\\?returnUrl=.*menus", RegexOptions.IgnoreCase));
        await Assertions.Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" })).ToBeVisibleAsync();

        // Logging in from there returns to the requested page
        await Page.Locator("#Input\\.Email").FillAsync(email);
        await Page.Locator("#Input\\.Password").FillAsync(Password);
        await WaitForInteractiveAsync(Page, () => Page.GetByRole(AriaRole.Button, new() { Name = "Sign in" }).ClickAsync());
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/menus$"));
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Your Menus" })).ToBeVisibleAsync();
    }

    // --- The account pages' own document and layout (AP13) ---

    public static TheoryData<string> AnonymousPages => new()
    {
        "/Account/Login", "/Account/Register", "/Account/ForgotPassword", "/Account/ForgotPasswordConfirmation",
        "/Account/ResetPassword?code=YWJj", "/Account/ResetPasswordConfirmation", "/Account/InvalidPasswordReset",
        "/Account/Lockout", "/Account/AccessDenied", "/Account/ResendEmailConfirmation"
    };

    /// <summary>
    /// Every anonymous account page: the auth document (lang, own CSS, no Tailwind, Bootstrap or blazor.web.js, no
    /// request to another host, noindex), the logo back to the landing page, one h1 in the centred card, and no
    /// sideways scrolling from 360 to 1440 px; the brand panel only from 1024 px.
    /// </summary>
    [Theory]
    [MemberData(nameof(AnonymousPages))]
    public async Task AnonymousAccountPages_HaveTheAuthDocumentAndLayout(string url)
    {
        var external = new List<string>();
        var origin = new Uri(App.ServerAddress).GetLeftPart(UriPartial.Authority);
        Page.Request += (_, r) => { if (!r.Url.StartsWith(origin, StringComparison.OrdinalIgnoreCase) && !r.Url.StartsWith("data:")) lock (external) external.Add(r.Url); };

        var response = await Page.GotoAsync(url);
        response!.Status.Should().Be(200, url);
        var html = Page.Locator("html");
        await Assertions.Expect(html).ToHaveAttributeAsync("data-auth-page", "");
        await Assertions.Expect(html).ToHaveAttributeAsync("lang", "en");
        await Assertions.Expect(Page.Locator("meta[name='robots']")).ToHaveAttributeAsync("content", "noindex");
        await Assertions.Expect(Page.Locator("base")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("link[rel=stylesheet][href*='css/auth']")).ToHaveCountAsync(1);
        (await Page.Locator("script[src]").CountAsync()).Should().Be(0, "the account pages run no script");
        (await Page.Locator("link[href*='bootstrap'], link[href*='app.css'], link[href*='shell.css'], link[href*='tailwind']").CountAsync()).Should().Be(0);
        await Assertions.Expect(Page.GetByTestId("auth-brand")).ToHaveAttributeAsync("href", "/");
        await Assertions.Expect(Page.Locator("h1")).ToHaveCountAsync(1);
        await Assertions.Expect(Page.GetByTestId("auth-card").Locator("h1")).ToBeVisibleAsync();
        await Assertions.Expect(Page).ToHaveTitleAsync(new Regex(" - Speise-Direkt$"));

        foreach (var width in new[] { 360, 390, 768, 1024, 1280, 1440 })
        {
            await Page.SetViewportSizeAsync(width, 800);
            var wide = await Page.EvaluateAsync<string[]>("""
                () => [...document.querySelectorAll('body *')]
                    .filter(e => e.checkVisibility() && e.getBoundingClientRect().right > window.innerWidth + 1)
                    .map(e => e.tagName + '.' + e.className)
                """);
            wide.Should().BeEmpty($"{url} fits at {width} px");
            if (width >= 1024) await Assertions.Expect(Page.GetByTestId("auth-panel")).ToBeVisibleAsync();
            else await Assertions.Expect(Page.GetByTestId("auth-panel")).ToBeHiddenAsync();
            // The card sits in the middle of its column
            var centres = await Page.EvaluateAsync<double[]>("""
                () => { const c = document.querySelector('[data-testid=auth-card]').getBoundingClientRect();
                        const m = document.querySelector('#au-main').getBoundingClientRect();
                        return [c.left + c.width / 2, m.left + m.width / 2]; }
                """);
            centres[0].Should().BeApproximately(centres[1], 1.5, $"the card is centred at {width} px");
        }
        external.Should().BeEmpty();
    }

    [Fact]
    public async Task LoginPage_InTheBrand_ValidationMessagesStyled_KeyboardOrder()
    {
        await Page.SetViewportSizeAsync(1280, 900);
        await Page.GotoAsync("/Account/Login");

        // The brand panel: the landing page's promise and the live demo
        var panel = Page.GetByTestId("auth-panel");
        await Assertions.Expect(panel).ToContainTextAsync("Your menu, one scan away.");
        await Assertions.Expect(panel).ToContainTextAsync("Registration is free.");
        await Assertions.Expect(Page.GetByTestId("auth-demo")).ToHaveAttributeAsync("href", "/demo/en");
        await Assertions.Expect(Page.GetByTestId("auth-header-link")).ToHaveAttributeAsync("href", "/Account/Register");

        // Empty submit: the server's messages under the fields, red, the fields marked
        await Page.GetByTestId("login-submit").ClickAsync();
        var message = Page.Locator(".validation-message").First;
        await Assertions.Expect(message).ToHaveTextAsync("Please enter your e-mail address.");
        (await message.EvaluateAsync<string>("e => getComputedStyle(e).color")).Should().Be("rgb(185, 28, 28)");
        var email = Page.Locator("#Input\\.Email");
        await Assertions.Expect(email).ToHaveAttributeAsync("aria-invalid", "true");
        (await email.EvaluateAsync<string>("e => getComputedStyle(e).borderTopColor")).Should().Be("rgb(185, 28, 28)");

        // A failed login: the red status message
        await email.FillAsync(UniqueEmail("nobody"));
        await Page.Locator("#Input\\.Password").FillAsync(Password);
        await Page.GetByTestId("login-submit").ClickAsync();
        var status = Page.GetByTestId("status-message");
        await Assertions.Expect(status).ToContainTextAsync("Invalid login attempt");
        await Assertions.Expect(status).ToHaveClassAsync(new Regex("au-alert--error"));
        (await status.EvaluateAsync<string>("e => getComputedStyle(e).backgroundColor")).Should().Be("rgb(254, 242, 242)");

        // Keyboard: the skip link first (it jumps to the form), then the panel's links, the header, the form
        await Page.GotoAsync("/Account/Login");
        await Page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(Page.GetByTestId("auth-skip")).ToBeFocusedAsync();
        (await Page.GetByTestId("auth-skip").EvaluateAsync<double>("e => e.getBoundingClientRect().top")).Should().BeGreaterThanOrEqualTo(0, "visible when focused");
        await Page.Keyboard.PressAsync("Enter");
        await Page.Keyboard.PressAsync("Tab");
        await Assertions.Expect(Page.Locator("#Input\\.Email")).ToBeFocusedAsync();
        await Page.Keyboard.PressAsync("Shift+Tab");
        await Assertions.Expect(Page.GetByTestId("auth-header-link")).ToBeFocusedAsync();
        var outline = await Page.GetByTestId("auth-header-link").EvaluateAsync<string>("e => getComputedStyle(e).outlineStyle");
        outline.Should().Be("solid", "the focus is visible");
    }

    [Fact]
    public async Task RegisterPage_LinksToLogin_AndInGerman()
    {
        var (context, page) = await NewContextAsync(E2ESettings.GermanLocale);
        await using var _ = context;
        await page.SetViewportSizeAsync(390, 844);
        await page.GotoAsync("/Account/Register");
        await Assertions.Expect(page.Locator("html")).ToHaveAttributeAsync("lang", "de");
        await Assertions.Expect(page.GetByTestId("auth-header-link")).ToHaveTextAsync("Anmelden");
        await Assertions.Expect(page.GetByTestId("auth-header-link")).ToHaveAttributeAsync("href", "/Account/Login");
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Level = 1, Name = "Konto erstellen" })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("register-login")).ToHaveAttributeAsync("href", "/Account/Login");
        await Assertions.Expect(page.GetByTestId("auth-footer")).ToContainTextAsync("Zur Startseite");
        // The submit button is a 48 px touch target
        (await page.GetByTestId("register-submit").EvaluateAsync<double>("e => e.getBoundingClientRect().height")).Should().BeGreaterThanOrEqualTo(44);
        await page.GetByTestId("register-submit").ClickAsync();
        await Assertions.Expect(page.Locator(".validation-message").First).ToHaveTextAsync("Bitte geben Sie Ihre E-Mail-Adresse ein.");
    }

    /// <summary>A signed-in user without a permission lands on AccessDenied in the account look, with the way back.</summary>
    [Fact]
    public async Task AccessDenied_ForASignedInUser_LeadsBackToTheDashboard()
    {
        await RegisterNewOwnerAsync();
        await Page.GotoAsync("/Account/AccessDenied");
        await Assertions.Expect(Page.Locator("html")).ToHaveAttributeAsync("data-auth-page", "");
        await Assertions.Expect(Page.GetByTestId("auth-header-link")).ToHaveAttributeAsync("href", "/dashboard");
        await Assertions.Expect(Page.GetByTestId("access-denied-dashboard")).ToHaveAttributeAsync("href", "/dashboard");

        // Account settings stay in the back office
        await Page.GotoAsync("/Account/Manage");
        await Assertions.Expect(Page.Locator("html[data-auth-page]")).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("topbar")).ToBeVisibleAsync();
    }

    /// <summary>
    /// The account settings (Account/Manage) in the back office's look (AP13): page header, the section navigation, each
    /// page's h2, back-office buttons and fields — no Bootstrap scaffold markup left — and nothing wider than a phone.
    /// </summary>
    [Fact]
    public async Task AccountSettings_AreInTheBackOfficeLook()
    {
        await RegisterNewOwnerAsync();
        foreach (var url in new[]
        {
            "/Account/Manage", "/Account/Manage/Email", "/Account/Manage/ChangePassword", "/Account/Manage/TwoFactorAuthentication",
            "/Account/Manage/EnableAuthenticator", "/Account/Manage/ResetAuthenticator", "/Account/Manage/PersonalData",
            "/Account/Manage/DeletePersonalData"
        })
        {
            await Page.SetViewportSizeAsync(1280, 900);
            await Page.GotoAsync(url);
            await Assertions.Expect(Page.GetByTestId("topbar")).ToBeVisibleAsync();
            await Assertions.Expect(Page.GetByTestId("page-header").Locator("h1")).ToHaveTextAsync("Account Management");
            await Assertions.Expect(Page.GetByTestId("manage-head").Locator("h2")).ToHaveCountAsync(1);
            (await Page.Locator(".form-floating, .btn, .col-md-4, .col-md-6, .col-xl-6, .text-danger").CountAsync())
                .Should().Be(0, $"{url} has no Bootstrap scaffold markup");

            await Page.SetViewportSizeAsync(390, 844);
            var wide = await Page.EvaluateAsync<string[]>("""
                () => [...document.querySelectorAll('main *')]
                    .filter(e => e.checkVisibility() && e.getBoundingClientRect().right > window.innerWidth + 1)
                    .map(e => e.tagName + '.' + e.className)
                """);
            wide.Should().BeEmpty($"{url} fits a phone");
        }

        // The authenticator setup: the key, the QR code and the code field
        await Page.GotoAsync("/Account/Manage/EnableAuthenticator");
        await Assertions.Expect(Page.GetByTestId("authenticator-key")).Not.ToBeEmptyAsync();
        await Assertions.Expect(Page.Locator("img[src^='data:image/png']")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("#Input\\.Code")).ToHaveClassAsync(new Regex("sd-input"));
    }

    /// <summary>
    /// Behind the reverse proxy (Apache on server2 → http://localhost) the login redirect of a protected page keeps the
    /// browser's scheme and host (UseForwardedHeaders, trusted from the loopback proxy — the test client is on loopback
    /// too). Before AP13 it pointed to http://, one extra hop through Apache's https redirect.
    /// </summary>
    [Theory]
    [InlineData("/dashboard")]
    [InlineData("/qrcodes")]
    [InlineData("/Analytics")]
    public async Task ProtectedPage_BehindProxy_RedirectsToHttpsLogin(string path)
    {
        var response = await Page.APIRequest.GetAsync(path, new()
        {
            MaxRedirects = 0,
            Headers = new Dictionary<string, string> { ["X-Forwarded-Proto"] = "https", ["X-Forwarded-Host"] = "app.speisekarte.example" }
        });

        response.Status.Should().Be(302);
        var location = response.Headers["location"];
        location.Should().StartWith("https://app.speisekarte.example/Account/Login?");
        Uri.UnescapeDataString(location).Should().EndWith("ReturnUrl=" + path);

        // Without the headers the redirect stays on the server's own origin
        var direct = await Page.APIRequest.GetAsync(path, new() { MaxRedirects = 0 });
        direct.Status.Should().Be(302);
        direct.Headers["location"].Should().StartWith(App.ServerAddress + "/Account/Login?");
    }
}
