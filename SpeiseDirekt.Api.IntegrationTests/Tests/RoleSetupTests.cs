using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SpeiseDirekt.Data;
using SpeiseDirekt.Infrastructure;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

/// <summary>
/// <see cref="RoleSetup"/>, the web app's startup step: both roles exist, and <c>GlobalAdmin</c> is fully synced from
/// <c>Admin:GlobalAdminEmails</c> (listed accounts get it, every other account loses it, unknown addresses are skipped);
/// idempotent; the log line carries counts, never addresses.
/// </summary>
public class RoleSetupTests : BaseIntegrationTest
{
    public RoleSetupTests(CustomWebApplicationFactory factory) : base(factory) { }

    private async Task<GlobalAdminSyncResult> RunAsync(params string[] emails) => (await RunLoggedAsync(emails)).Result;

    private async Task<(GlobalAdminSyncResult Result, List<string> Log)> RunLoggedAsync(params string[] emails)
    {
        using var scope = Factory.Services.CreateScope();
        var log = new ListLogger<RoleSetup>();
        var setup = new RoleSetup(scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>(),
            scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(), log);
        return (await setup.RunAsync(emails), log.Messages);
    }

    private async Task<List<string>> AdminIdsAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return await (from ur in db.UserRoles
                      join r in db.Roles on ur.RoleId equals r.Id
                      where r.NormalizedName == "GLOBALADMIN"
                      orderby ur.UserId
                      select ur.UserId).ToListAsync();
    }

    private async Task<List<(string? Name, string? NormalizedName)>> RolesAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return (await db.Roles.AsNoTracking().OrderBy(r => r.Name).ToListAsync())
            .Select(r => (r.Name, r.NormalizedName)).ToList();
    }

    [Fact]
    public void TheRoleNames_AreExactlyGlobalAdminAndPaidTenant()
    {
        AppRoles.All.Should().Equal("GlobalAdmin", "PaidTenant");
        AppRoles.Normalized(AppRoles.PaidTenant).Should().Be("PAIDTENANT");
    }

    [Fact]
    public async Task CreatesBothRoles_WhenTheyAreMissing()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.UserRoles.ExecuteDeleteAsync();
            await db.Roles.ExecuteDeleteAsync();
        }

        await RunAsync();

        (await RolesAsync()).Should().Equal(("GlobalAdmin", "GLOBALADMIN"), ("PaidTenant", "PAIDTENANT"));
    }

    [Fact]
    public async Task AddsAListedAddress_MatchedLikeIdentityMatchesEmails()
    {
        var chef = await TestSeedData.SeedOwnerAsync(Factory.Services, "chef@test.com");

        var result = await RunAsync("  Chef@Test.COM ");

        result.Should().Be(new GlobalAdminSyncResult(1, 0, 0, 0));
        (await AdminIdsAsync()).Should().Equal(chef);
    }

    [Fact]
    public async Task RemovesEveryAdminWhoIsNotListed()
    {
        var a = await TestSeedData.SeedOwnerAsync(Factory.Services, "a@test.com");
        var b = await TestSeedData.SeedOwnerAsync(Factory.Services, "b@test.com");
        (await RunAsync("a@test.com", "b@test.com")).Added.Should().Be(2);

        (await RunAsync("a@test.com")).Should().Be(new GlobalAdminSyncResult(0, 1, 0, 0));
        (await AdminIdsAsync()).Should().Equal(a);

        // An admin given the role by other means is removed as well: the list is the only source
        await TestSeedData.SetGlobalAdminAsync(Factory.Services, b, admin: true);
        (await RunAsync()).Should().Be(new GlobalAdminSyncResult(0, 2, 0, 0));
        (await AdminIdsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task SkipsAnAddressWithoutAnAccount_AndLogsCountsOnly()
    {
        var (result, log) = await RunLoggedAsync("nobody@test.com", "testuser@test.com", "");

        result.Should().Be(new GlobalAdminSyncResult(1, 0, 1, 0));
        (await AdminIdsAsync()).Should().Equal(CustomWebApplicationFactory.TestUserId);
        log.Should().ContainSingle(m => m == "GlobalAdmin sync: 1 added, 0 removed, 1 address(es) without an account");
        log.Should().NotContain(m => m.Contains('@'), "no e-mail address is logged");
    }

    [Fact]
    public async Task IsIdempotent()
    {
        await TestSeedData.SeedOwnerAsync(Factory.Services, "chef@test.com");
        var first = await RunAsync("chef@test.com");
        var rolesAfterFirst = await RolesAsync();
        var adminsAfterFirst = await AdminIdsAsync();

        var second = await RunAsync("chef@test.com");

        first.Added.Should().Be(1);
        second.Should().Be(new GlobalAdminSyncResult(0, 0, 0, 0));
        (await RolesAsync()).Should().Equal(rolesAfterFirst).And.HaveCount(2);
        (await AdminIdsAsync()).Should().Equal(adminsAfterFirst);
    }

    [Fact]
    public async Task LeavesPaidTenantAlone()
    {
        await RunAsync("testuser@test.com");
        await RunAsync();

        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(CustomWebApplicationFactory.TestUserId);
        (await users.GetRolesAsync(user!)).Should().Equal(AppRoles.PaidTenant);
    }

    /// <summary>Collects the formatted log messages.</summary>
    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = new();
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => Messages.Add(formatter(state, exception));
    }
}
