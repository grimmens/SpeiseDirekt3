using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Infrastructure;

/// <summary>
/// <see cref="PolicyNames.PaidTenant"/>: the signed-in account's tenant has the role <see cref="AppRoles.PaidTenant"/>
/// (read from the database by <see cref="ITenantPlanService"/>), nothing else. Creating menus and QR codes is checked
/// with <see cref="ITenantPlanService.CanCreateMenuAsync"/> / <see cref="ITenantPlanService.CanCreateQrCodeAsync"/>,
/// not with this policy.
/// </summary>
public class PaidTenantHandler : AuthorizationHandler<PaidTenantRequirement>
{
    private readonly ITenantPlanService _plans;

    public PaidTenantHandler(ITenantPlanService plans)
    {
        _plans = plans;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PaidTenantRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return;

        // The tenant owner, as IUserIdProvider.GetUserId(): a member carries the TenantOwnerId claim
        // (TenantClaimsTransformation), the owner is the account itself
        var ownerId = context.User.FindFirstValue("TenantOwnerId")
                      ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(ownerId))
            return;

        if (await _plans.IsPaidAsync(ownerId))
            context.Succeed(requirement);
    }
}

public class PaidTenantRequirement : IAuthorizationRequirement
{
    // marker class — no properties needed
}
