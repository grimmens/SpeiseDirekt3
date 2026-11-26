using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SpeiseDirekt.Data;

namespace SpeiseDirekt.Infrastructure;

/// <summary>Marker of <see cref="PolicyNames.GlobalAdmin"/>.</summary>
public sealed class GlobalAdminRequirement : IAuthorizationRequirement
{
}

/// <summary>
/// <see cref="PolicyNames.GlobalAdmin"/>: the signed-in account itself (not its tenant) has the role
/// <see cref="AppRoles.GlobalAdmin"/> — read from the database at every check, never from the sign-in cookie, so the
/// startup sync of <c>Admin:GlobalAdminEmails</c> counts without a new sign-in.
/// </summary>
public sealed class GlobalAdminHandler : AuthorizationHandler<GlobalAdminRequirement>
{
    private readonly ApplicationDbContext _db;

    public GlobalAdminHandler(ApplicationDbContext db)
    {
        _db = db;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, GlobalAdminRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return;

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return;

        if (await _db.HasRoleAsync(userId, AppRoles.GlobalAdmin))
            context.Succeed(requirement);
    }
}
