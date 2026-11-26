using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

/// <summary>
/// <see cref="ITenantAdminService"/>, the global admin's customer list: one row per owner account across every tenant
/// (search, sort, the 500 cap), the members read-only, and the paid toggle (idempotent, owners only, admins only).
/// </summary>
public class TenantAdminServiceTests : BaseIntegrationTest
{
    private const string TestUserId = CustomWebApplicationFactory.TestUserId;

    public TenantAdminServiceTests(CustomWebApplicationFactory factory) : base(factory) { }

    private async Task<T> WithAdminAsync<T>(Func<ITenantAdminService, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ITenantAdminService>());
    }

    private Task<IReadOnlyList<TenantRow>> ListAsync(string? search = null) => WithAdminAsync(a => a.ListTenantsAsync(search));

    private async Task<string> SeedAdminAsync()
    {
        var admin = await TestSeedData.SeedOwnerAsync(Factory.Services, "admin@test.com");
        await TestSeedData.SetGlobalAdminAsync(Factory.Services, admin, admin: true);
        return admin;
    }

    private async Task<bool> IsPaidAsync(string ownerId)
    {
        using var scope = Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITenantPlanService>().IsPaidAsync(ownerId);
    }

    /// <summary>"Gasthaus Zur Post": free, a profile, one menu with a guest view, a QR code, one active and one inactive member.</summary>
    private async Task<(string Owner, string Active, string Inactive, DateTime LastView)> SeedPostAsync()
    {
        var owner = await TestSeedData.SeedOwnerAsync(Factory.Services, "post@test.com");
        await TestSeedData.SeedRestaurantProfileAsync(Factory.Services, owner, "Gasthaus Zur Post");
        var (menus, qrCodes) = await TestSeedData.SeedContentAsync(Factory.Services, owner, menus: 1, qrCodes: 1);
        var active = await TestSeedData.SeedMemberAsync(Factory.Services, owner, "kellner@test.com");
        var inactive = await TestSeedData.SeedMemberAsync(Factory.Services, owner, "aushilfe@test.com", active: false);
        var lastView = new DateTime(2026, 11, 20, 18, 30, 0, DateTimeKind.Utc);
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.MenuViews.AddRange(
            new MenuView { Id = Guid.NewGuid(), SessionId = "a", MenuId = menus[0], QRCodeId = qrCodes[0], ViewedAt = lastView.AddDays(-3) },
            new MenuView { Id = Guid.NewGuid(), SessionId = "b", MenuId = menus[0], ViewedAt = lastView });
        await db.SaveChangesAsync();
        return (owner, active, inactive, lastView);
    }

    [Fact]
    public async Task ListsEveryOwner_WithItsNumbers_AcrossTheTenantFilters()
    {
        var post = await SeedPostAsync();
        var admin = await SeedAdminAsync();

        var rows = await ListAsync();

        rows.Select(r => r.OwnerId).Should().BeEquivalentTo(new[] { TestUserId, post.Owner, admin }, "members are no tenants");

        var postRow = rows.Single(r => r.OwnerId == post.Owner);
        postRow.Should().BeEquivalentTo(new TenantRow(post.Owner, "post@test.com", "Gasthaus Zur Post", 1, 1, 1, 1,
            post.LastView, IsPaid: false, IsGlobalAdmin: false));
        postRow.LastGuestView!.Value.Kind.Should().Be(DateTimeKind.Utc);
        postRow.MemberCount.Should().Be(2);

        var seeded = rows.Single(r => r.OwnerId == TestUserId);
        seeded.Email.Should().Be("testuser@test.com");
        seeded.RestaurantName.Should().BeEmpty("the seeded tenant has no profile");
        (seeded.MenuCount, seeded.QrCodeCount, seeded.MemberCount).Should().Be((2, 2, 0));
        seeded.LastGuestView.Should().BeCloseTo(DateTime.UtcNow.AddDays(-1), TimeSpan.FromMinutes(5), "the latest of its views");
        seeded.IsPaid.Should().BeTrue();

        var adminRow = rows.Single(r => r.OwnerId == admin);
        adminRow.IsGlobalAdmin.Should().BeTrue();
        (adminRow.MenuCount, adminRow.QrCodeCount, adminRow.LastGuestView).Should().Be((0, 0, (DateTime?)null));
    }

    [Fact]
    public async Task SortsByRestaurantName_ThenByEmail()
    {
        var zeta = await TestSeedData.SeedOwnerAsync(Factory.Services, "zeta@test.com");
        await TestSeedData.SeedRestaurantProfileAsync(Factory.Services, zeta, "Alpenhof");
        var mike = await TestSeedData.SeedOwnerAsync(Factory.Services, "mike@test.com");
        await TestSeedData.SeedRestaurantProfileAsync(Factory.Services, mike, "zum Hirschen");
        await TestSeedData.SeedOwnerAsync(Factory.Services, "alpha@test.com");

        var rows = await ListAsync();

        rows.Select(r => (r.RestaurantName, r.Email)).Should().Equal(
            ("", "alpha@test.com"), ("", "testuser@test.com"), ("Alpenhof", "zeta@test.com"), ("zum Hirschen", "mike@test.com"));
    }

    [Fact]
    public async Task TheSearch_MatchesTheEmailOrTheRestaurant_IgnoringCase()
    {
        var post = await SeedPostAsync();
        var muehle = await TestSeedData.SeedOwnerAsync(Factory.Services, "info@oelmuehle.test");
        await TestSeedData.SeedRestaurantProfileAsync(Factory.Services, muehle, "Gasthof Ölmühle");

        (await ListAsync("ZUR post")).Select(r => r.OwnerId).Should().Equal(post.Owner);
        (await ListAsync("  POST@TEST ")).Select(r => r.OwnerId).Should().Equal(post.Owner);
        (await ListAsync("ölmühle")).Select(r => r.OwnerId).Should().Equal(muehle);
        (await ListAsync("TestUser")).Select(r => r.OwnerId).Should().Equal(TestUserId);
        (await ListAsync("gast")).Select(r => r.OwnerId).Should().BeEquivalentTo(new[] { post.Owner, muehle });
        (await ListAsync("kellner")).Should().BeEmpty("a member's address finds no tenant");
        (await ListAsync("nichts davon")).Should().BeEmpty();
        (await ListAsync("   ")).Should().HaveCount(3);
    }

    [Fact]
    public async Task TheList_StopsAt500Rows()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            for (var i = 0; i < 505; i++)
            {
                var email = $"tenant{i:000}@test.com";
                db.Users.Add(new ApplicationUser
                {
                    Id = Guid.NewGuid().ToString(), UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                    Email = email, NormalizedEmail = email.ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString()
                });
            }
            await db.SaveChangesAsync();
        }

        ITenantAdminService.MaxRows.Should().Be(500);
        (await ListAsync()).Should().HaveCount(500);
        (await ListAsync("tenant00")).Should().HaveCount(10);
    }

    [Fact]
    public async Task ListMembers_ShowsTheTeam()
    {
        var post = await SeedPostAsync();

        var members = await WithAdminAsync(a => a.ListMembersAsync(post.Owner));

        members.Should().Equal(
            new TenantMemberRow(post.Inactive, "aushilfe@test.com", "aushilfe@test.com", TenantRole.Manager, false),
            new TenantMemberRow(post.Active, "kellner@test.com", "kellner@test.com", TenantRole.Manager, true));
        (await WithAdminAsync(a => a.ListMembersAsync(TestUserId))).Should().BeEmpty();
    }

    [Fact]
    public async Task SetPaid_GivesAndTakesTheRole_AndIsIdempotent()
    {
        var admin = await SeedAdminAsync();
        var post = await SeedPostAsync();

        (await WithAdminAsync(a => a.SetPaidAsync(post.Owner, true, admin))).Should().Be(new SetPaidResult(SetPaidStatus.Saved, false, true));
        (await IsPaidAsync(post.Owner)).Should().BeTrue();
        (await WithAdminAsync(a => a.SetPaidAsync(post.Owner, true, admin))).Should().Be(new SetPaidResult(SetPaidStatus.Saved, true, true));
        (await ListAsync("post@")).Single().IsPaid.Should().BeTrue();

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            (await db.UserRoles.CountAsync(ur => ur.UserId == post.Owner)).Should().Be(1, "no duplicate role row");
        }

        (await WithAdminAsync(a => a.SetPaidAsync(post.Owner, false, admin))).Should().Be(new SetPaidResult(SetPaidStatus.Saved, true, false));
        (await IsPaidAsync(post.Owner)).Should().BeFalse();
        (await WithAdminAsync(a => a.SetPaidAsync(post.Owner, false, admin))).Should().Be(new SetPaidResult(SetPaidStatus.Saved, false, false));
    }

    [Fact]
    public async Task SetPaid_RejectsAMemberAndAnUnknownId()
    {
        var admin = await SeedAdminAsync();
        var post = await SeedPostAsync();

        (await WithAdminAsync(a => a.SetPaidAsync(post.Active, true, admin))).Status.Should().Be(SetPaidStatus.NotAnOwner);
        (await WithAdminAsync(a => a.SetPaidAsync(Guid.NewGuid().ToString(), true, admin))).Status.Should().Be(SetPaidStatus.NotFound);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.UserRoles.AnyAsync(ur => ur.UserId == post.Active)).Should().BeFalse("the member got no role");
        (await IsPaidAsync(post.Owner)).Should().BeFalse("its tenant is unchanged");
    }

    [Fact]
    public async Task SetPaid_NeedsAGlobalAdmin()
    {
        var post = await SeedPostAsync();

        (await WithAdminAsync(a => a.SetPaidAsync(post.Owner, true, TestUserId))).Status.Should().Be(SetPaidStatus.NotAllowed);
        (await WithAdminAsync(a => a.SetPaidAsync(post.Owner, true, ""))).Status.Should().Be(SetPaidStatus.NotAllowed);
        (await IsPaidAsync(post.Owner)).Should().BeFalse();
    }

    [Fact]
    public async Task SetPaid_ChangesNoTenantContent()
    {
        var admin = await SeedAdminAsync();
        var post = await SeedPostAsync();
        var before = await ListAsync("post@");

        (await WithAdminAsync(a => a.SetPaidAsync(post.Owner, true, admin))).Succeeded.Should().BeTrue();

        var after = (await ListAsync("post@")).Single();
        after.Should().Be(before.Single() with { IsPaid = true });
        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.Menus.IgnoreQueryFilters().CountAsync(m => m.ApplicationUserId == Guid.Parse(admin)))
            .Should().Be(0, "nothing was stamped with the admin's id");
    }
}
