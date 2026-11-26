using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using SpeiseDirekt.Data;
using SpeiseDirekt.Data.Migrations;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

/// <summary>
/// The data-only migration <c>RemoveUnusedLegacyRoles</c>: one SQL statement, an empty <c>Down</c>, and its SQL (the
/// statement SQL Server runs) deletes the six legacy roles only where nothing references them — here on SQLite.
/// </summary>
public class RemoveUnusedLegacyRolesMigrationTests : BaseIntegrationTest
{
    public RemoveUnusedLegacyRolesMigrationTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public void TheMigration_IsOneSqlStatement_AndDownChangesNothing()
    {
        var migration = new RemoveUnusedLegacyRoles();
        migration.UpOperations.Should().ContainSingle()
            .Which.Should().BeOfType<SqlOperation>()
            .Which.Sql.Should().Be(RemoveUnusedLegacyRoles.UpSql);
        migration.DownOperations.Should().BeEmpty();
    }

    [Fact]
    public async Task TheMigrationSql_DeletesOnlyTheUnusedLegacyRoles()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        IdentityRole Role(string name, string? normalized) => new(name) { NormalizedName = normalized };
        var manager = Role("Manager", "MANAGER");
        var cashier = Role("Cashier", null);                 // a seed may have left the normalized name empty
        var driver = Role("Driver", "DRIVER");               // assigned to a user: stays
        var employee = Role("Employee", "EMPLOYEE");         // has a role claim: stays
        var customer = Role("customer", "CUSTOMER");         // other casing
        var generalManager = Role("GeneralManager", "GENERALMANAGER");
        var kitchen = Role("Kitchen", "KITCHEN");            // not one of the six: stays
        db.Roles.AddRange(manager, cashier, driver, employee, customer, generalManager, kitchen);
        db.UserRoles.Add(new IdentityUserRole<string> { UserId = CustomWebApplicationFactory.TestUserId, RoleId = driver.Id });
        db.RoleClaims.Add(new IdentityRoleClaim<string> { RoleId = employee.Id, ClaimType = "permission", ClaimValue = "x" });
        await db.SaveChangesAsync();

        var deleted = await db.Database.ExecuteSqlRawAsync(RemoveUnusedLegacyRoles.UpSql);

        deleted.Should().Be(4);
        var names = await db.Roles.AsNoTracking().OrderBy(r => r.Name).Select(r => r.Name).ToListAsync();
        // The seeded GlobalAdmin is unused too, but it is not a legacy role
        names.Should().Equal("Driver", "Employee", "GlobalAdmin", "Kitchen", "PaidTenant");
        (await db.UserRoles.CountAsync()).Should().Be(2, "the test tenant's PaidTenant and the driver assignment stay");
        (await db.RoleClaims.CountAsync()).Should().Be(1);
    }
}
