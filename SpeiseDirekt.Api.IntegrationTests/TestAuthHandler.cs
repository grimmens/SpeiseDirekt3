using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SpeiseDirekt.Api.IntegrationTests;

public class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    /// <summary>Requests carrying this header are treated as anonymous (no authenticated user).</summary>
    public const string AnonymousHeader = "X-Test-Anonymous";

    /// <summary>
    /// Requests carrying this header are authenticated as the account with that id instead of the tenant owner
    /// (<see cref="CustomWebApplicationFactory.TestUserId"/>), e.g. a team member: the app's claims transformation and
    /// permission service then resolve its role from the database like in production.
    /// </summary>
    public const string UserHeader = "X-Test-User";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (Request.Headers.ContainsKey(AnonymousHeader))
            return Task.FromResult(AuthenticateResult.NoResult());

        var userId = Request.Headers.TryGetValue(UserHeader, out var header) && !string.IsNullOrEmpty(header)
            ? header.ToString()
            : CustomWebApplicationFactory.TestUserId;
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Name, "testuser@test.com"),
            new Claim(ClaimTypes.Email, "testuser@test.com"),
        };
        var identity = new ClaimsIdentity(claims, "TestScheme");
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, "TestScheme");

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
