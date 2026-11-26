using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SpeiseDirekt.Data;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Api.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestUserId = "11111111-1111-1111-1111-111111111111";

    private SqliteConnection? _connection;

    /// <summary>
    /// Clock used by the app under test. Uses the system clock unless a test fixes the time.
    /// </summary>
    public TestTimeProvider Clock { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        builder.ConfigureServices(services =>
        {
            // Remove all DbContext-related registrations
            var descriptorsToRemove = services
                .Where(d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>)
                         || d.ServiceType == typeof(DbContextOptions)
                         || d.ServiceType.FullName?.Contains("EntityFrameworkCore") == true)
                .ToList();
            foreach (var descriptor in descriptorsToRemove)
                services.Remove(descriptor);

            // Replace IUserIdProvider with a test version that returns TestUserId (outside requests too), except for a
            // request sent as another account (TestAuthHandler.UserHeader)
            var userIdDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IUserIdProvider));
            if (userIdDescriptor != null)
                services.Remove(userIdDescriptor);
            services.AddScoped<IUserIdProvider>(sp => new TestUserIdProvider(TestUserId, sp.GetService<IHttpContextAccessor>()));

            // Replace TimeProvider with a controllable test clock
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            // Add SQLite in-memory database
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            // Replace authentication with a test scheme that auto-succeeds
            services.AddAuthentication("TestScheme")
                .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("TestScheme", _ => { });

            // Ensure database is created
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection?.Dispose();
        }
    }
}

/// <summary>
/// The tenant of the tests: <see cref="CustomWebApplicationFactory.TestUserId"/>, also outside a request (services
/// resolved in a test's own scope). A request carrying <see cref="TestAuthHandler.UserHeader"/> resolves like the app's
/// <c>UserIdProvider</c>: a team member's tenant owner (the <c>TenantOwnerId</c> claim of the claims transformation), else
/// the account itself — so a second owner works on its own tenant.
/// </summary>
internal class TestUserIdProvider : IUserIdProvider
{
    private readonly string _userId;
    private readonly IHttpContextAccessor? _accessor;

    public TestUserIdProvider(string userId, IHttpContextAccessor? accessor = null)
    {
        _userId = userId;
        _accessor = accessor;
    }

    public string GetUserId()
    {
        var user = OtherAccount();
        return user?.FindFirstValue("TenantOwnerId") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier) ?? _userId;
    }

    public string GetActualUserId() => OtherAccount()?.FindFirstValue(ClaimTypes.NameIdentifier) ?? _userId;

    private ClaimsPrincipal? OtherAccount()
    {
        var context = _accessor?.HttpContext;
        return context is not null && context.Request.Headers.ContainsKey(TestAuthHandler.UserHeader)
               && context.User.Identity?.IsAuthenticated == true
            ? context.User
            : null;
    }
}
