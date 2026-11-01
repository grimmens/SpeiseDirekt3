using System.Text.RegularExpressions;
using Microsoft.Playwright;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>Register, login, logout and the login redirect for protected pages.</summary>
public class AuthTests : E2ETestBase
{
    public AuthTests(E2EFixture fixture) : base(fixture) { }

    private ILocator LogoutButton => Page.GetByRole(AriaRole.Button, new() { Name = "Logout" });

    [Fact]
    public async Task Register_SignsInNewAccount()
    {
        var email = await RegisterNewOwnerAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"^{Regex.Escape(App.ServerAddress)}/?$"));
        await Assertions.Expect(Page.GetByText(email).First).ToBeVisibleAsync();
        await Assertions.Expect(LogoutButton.First).ToBeVisibleAsync();
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
        await Assertions.Expect(Page.GetByRole(AriaRole.Link, new() { Name = "Login" }).First).ToBeVisibleAsync();
        await Assertions.Expect(LogoutButton).ToHaveCountAsync(0);

        await LoginAsync(Page, email);
        await Assertions.Expect(Page.GetByText(email).First).ToBeVisibleAsync();
        await Assertions.Expect(LogoutButton.First).ToBeVisibleAsync();
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
}
