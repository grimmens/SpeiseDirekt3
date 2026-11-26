using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using SpeiseDirekt.Infrastructure;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

/// <summary>
/// The role policies (<see cref="RolePolicyExtensions.AddRolePolicies"/>): <c>PaidTenant</c> = the tenant's owner has
/// the role, for the owner and its members; <c>GlobalAdmin</c> = the account itself has the role. Both read the
/// database, never role claims of the sign-in.
/// </summary>
public class RolePolicyTests : BaseIntegrationTest
{
    public RolePolicyTests(CustomWebApplicationFactory factory) : base(factory) { }

    /// <summary>A signed-in account; a member carries the TenantOwnerId claim like TenantClaimsTransformation adds it.</summary>
    private static ClaimsPrincipal SignedIn(string userId, string? tenantOwnerId = null, params Claim[] extra)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (tenantOwnerId is not null)
            claims.Add(new Claim("TenantOwnerId", tenantOwnerId));
        claims.AddRange(extra);
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private async Task<bool> AllowedAsync(ClaimsPrincipal user, string policy)
    {
        using var scope = Factory.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<IAuthorizationService>().AuthorizeAsync(user, policy)).Succeeded;
    }

    [Fact]
    public async Task PaidTenant_FollowsTheOwnersRole_ForTheOwnerAndItsMembers()
    {
        const string paidOwner = CustomWebApplicationFactory.TestUserId;
        var freeOwner = await TestSeedData.SeedOwnerAsync(Factory.Services, "free@test.com");

        (await AllowedAsync(SignedIn(paidOwner), PolicyNames.PaidTenant)).Should().BeTrue();
        (await AllowedAsync(SignedIn(Guid.NewGuid().ToString(), paidOwner), PolicyNames.PaidTenant)).Should().BeTrue("a member inherits it");
        (await AllowedAsync(SignedIn(freeOwner), PolicyNames.PaidTenant)).Should().BeFalse();
        (await AllowedAsync(SignedIn(Guid.NewGuid().ToString(), freeOwner), PolicyNames.PaidTenant)).Should().BeFalse();
        (await AllowedAsync(new ClaimsPrincipal(new ClaimsIdentity()), PolicyNames.PaidTenant)).Should().BeFalse("anonymous");

        // No new sign-in needed: the same principal follows the database
        await TestSeedData.SetPaidAsync(Factory.Services, freeOwner, paid: true);
        (await AllowedAsync(SignedIn(freeOwner), PolicyNames.PaidTenant)).Should().BeTrue();
    }

    [Fact]
    public async Task GlobalAdmin_IsTheAccountsRoleInTheDatabase_NotAClaim()
    {
        var admin = await TestSeedData.SeedOwnerAsync(Factory.Services, "admin@test.com");
        var principal = SignedIn(admin);

        (await AllowedAsync(principal, PolicyNames.GlobalAdmin)).Should().BeFalse();
        (await AllowedAsync(SignedIn(admin, extra: new Claim(ClaimTypes.Role, AppRoles.GlobalAdmin)), PolicyNames.GlobalAdmin))
            .Should().BeFalse("a role claim of the sign-in is not read");

        await TestSeedData.SetGlobalAdminAsync(Factory.Services, admin, admin: true);
        (await AllowedAsync(principal, PolicyNames.GlobalAdmin)).Should().BeTrue();
        (await AllowedAsync(SignedIn(Guid.NewGuid().ToString(), admin), PolicyNames.GlobalAdmin))
            .Should().BeFalse("a member of the admin's tenant is no admin");

        await TestSeedData.SetGlobalAdminAsync(Factory.Services, admin, admin: false);
        (await AllowedAsync(principal, PolicyNames.GlobalAdmin)).Should().BeFalse();
    }
}
