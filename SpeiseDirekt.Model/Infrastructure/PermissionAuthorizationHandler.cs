using Microsoft.AspNetCore.Authorization;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Infrastructure;

public class PermissionRequirement : IAuthorizationRequirement
{
    public Permission RequiredPermission { get; }
    public PermissionRequirement(Permission permission) => RequiredPermission = permission;
}

public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IPermissionService _permissionService;

    public PermissionAuthorizationHandler(IPermissionService permissionService)
    {
        _permissionService = permissionService;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (await _permissionService.HasPermissionAsync(requirement.RequiredPermission))
        {
            context.Succeed(requirement);
        }
    }
}
