using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>Register, login, logout and the login redirect for protected pages.</summary>
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
        await Assertions.Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Log in", Exact = true })).ToBeVisibleAsync();
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
