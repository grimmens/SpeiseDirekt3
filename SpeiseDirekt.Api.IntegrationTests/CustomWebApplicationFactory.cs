using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpeiseDirekt.Data;

namespace SpeiseDirekt.Api.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string TestUserId = "11111111-1111-1111-1111-111111111111";
    public const string TestDbConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=SpeiseDirektTests;Trusted_Connection=True;";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Remove the existing DbContext registration
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ApplicationDbContext>));
            if (descriptor != null)
                services.Remove(descriptor);

            // Add test database
            services.AddDbContext<ApplicationDbContext>(options =>
            {
                options.UseSqlServer(TestDbConnectionString,
                    o => o.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery));
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
}
