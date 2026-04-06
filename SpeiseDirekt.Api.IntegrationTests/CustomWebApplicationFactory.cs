using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpeiseDirekt.Data;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Api.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestUserId = "11111111-1111-1111-1111-111111111111";

    private SqliteConnection? _connection;

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

            // Replace IUserIdProvider with a test version that always returns TestUserId
            var userIdDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IUserIdProvider));
            if (userIdDescriptor != null)
                services.Remove(userIdDescriptor);
            services.AddScoped<IUserIdProvider>(_ => new TestUserIdProvider(TestUserId));

            // Add SQLite in-memory database
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            // Replace POS Stripe gateway with test double
            var stripeDescriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(IPosStripeGateway));
            if (stripeDescriptor != null)
                services.Remove(stripeDescriptor);
            services.AddTransient<IPosStripeGateway, TestPosStripeGateway>();

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

internal class TestUserIdProvider : IUserIdProvider
{
    private readonly string _userId;
    public TestUserIdProvider(string userId) => _userId = userId;
    public string GetUserId() => _userId;
    public string GetActualUserId() => _userId;
}
