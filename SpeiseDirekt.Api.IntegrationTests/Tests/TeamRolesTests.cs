using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.DependencyInjection;
using SpeiseDirekt.Data;
using SpeiseDirekt.Data.Migrations;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

/// <summary>The two roles of AP14 (pure): what the owner and a manager may do.</summary>
public class TeamRolesTests
{
    private static readonly Permission[] TeamPermissions =
        [Permission.UsersView, Permission.UsersCreate, Permission.UsersEdit, Permission.UsersDelete];

    [Fact]
    public void ThereAreExactlyTwoRoles_WithTheirStoredValues()
    {
        Enum.GetValues<TenantRole>().Should().Equal(TenantRole.Owner, TenantRole.Manager);
        ((int)TenantRole.Owner).Should().Be(0);
        ((int)TenantRole.Manager).Should().Be(1);
        new TenantUser().Role.Should().Be(TenantRole.Manager, "a new team member is a manager");
    }

    [Fact]
    public void TheOwner_HasEveryPermission()
    {
        foreach (var permission in Enum.GetValues<Permission>())
            PermissionDefaults.For(TenantRole.Owner).HasFlag(permission).Should().BeTrue($"the owner may {permission}");
    }

    [Fact]
    public void AManager_HasEverythingButTheTeam()
    {
        var manager = PermissionDefaults.For(TenantRole.Manager);
        foreach (var permission in Enum.GetValues<Permission>().Where(p => p != Permission.None))
            manager.HasFlag(permission).Should().Be(!TeamPermissions.Contains(permission), $"{permission} for a manager");
    }

    [Fact]
    public void WithoutARole_ThereAreNoPermissions() =>
        PermissionDefaults.For(null).Should().Be(Permission.None);

    [Fact]
    public void TheRemovedAreas_AreGone_AndTheOtherBitsKeepTheirValues()
    {
        Enum.GetNames<Permission>().Should().NotContain(["DeliveryView", "DeliveryManage", "SubscriptionManage"]);
        ((long)Permission.MenusView).Should().Be(1L << 0);
        ((long)Permission.QrCodesDelete).Should().Be(1L << 19);
        ((long)Permission.AnalyticsView).Should().Be(1L << 20);
        ((long)Permission.MenuCombosDelete).Should().Be(1L << 28);
        ((long)Permission.UsersDelete).Should().Be(1L << 27);
    }
}

/// <summary>
/// The data-only migration <c>TeamRolesOwnerAndManager</c>: no schema operation, and its SQL (the statement SQL Server
/// runs, see the migration) turns every team member into a manager without permission overrides — here on SQLite.
/// </summary>
public class TeamRolesMigrationTests : BaseIntegrationTest
{
    public TeamRolesMigrationTests(CustomWebApplicationFactory factory) : base(factory) { }

    [Fact]
    public void TheMigration_IsOneSqlStatement_AndDownChangesNothing()
    {
        var migration = new TeamRolesOwnerAndManager();
        migration.UpOperations.Should().ContainSingle()
            .Which.Should().BeOfType<SqlOperation>()
            .Which.Sql.Should().Be("UPDATE [TenantUsers] SET [Role] = 1, [Permissions] = 0;");
        migration.DownOperations.Should().BeEmpty();
    }

    [Fact]
    public async Task TheMigrationSql_MakesEveryMemberAManager_AndClearsTheOverrides()
    {
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        // Rows as they could be before AP14: general manager (0), manager (1), cashier (2), employee (3), driver (4),
        // some with permission overrides (incl. the removed delivery bits 32/33), one inactive
        var before = new (int Role, long Permissions, bool Active)[]
        {
            (0, 0, true), (1, 0, true), (2, (1L << 0) | (1L << 16), true), (3, 0, false), (4, (1L << 32) | (1L << 33), true)
        };
        foreach (var (role, permissions, active) in before)
        {
            var id = Guid.NewGuid().ToString();
            var email = $"before-{id}@test.com";
            db.Users.Add(new ApplicationUser
            {
                Id = id, UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email,
                SecurityStamp = Guid.NewGuid().ToString(), TenantOwnerId = CustomWebApplicationFactory.TestUserId
            });
            db.TenantUsers.Add(new TenantUser
            {
                Id = Guid.NewGuid(), ApplicationUserId = id, TenantOwnerId = CustomWebApplicationFactory.TestUserId,
                Role = (TenantRole)role, Permissions = (Permission)permissions, IsActive = active, DisplayName = $"Rolle {role}"
            });
        }
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync(TeamRolesOwnerAndManager.UpSql);

        var after = await db.TenantUsers.AsNoTracking().OrderBy(tu => tu.DisplayName).ToListAsync();
        after.Should().HaveCount(before.Length);
        after.Should().OnlyContain(tu => tu.Role == TenantRole.Manager && tu.Permissions == Permission.None);
        after.Select(tu => tu.IsActive).Should().Equal(true, true, true, false, true); // the status stays
    }
}
