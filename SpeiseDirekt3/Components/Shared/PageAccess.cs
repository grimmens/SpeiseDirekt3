using Microsoft.AspNetCore.Components;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt3.Components.Shared;

/// <summary>
/// The back office's access check of a page (AP14): without the permission the browser goes to
/// <c>/Account/AccessDenied</c> (a full load, like <c>/qrcodes</c> always did). The owner and an active manager pass every
/// area page except the team pages (owner only); an inactive or removed member passes none.
/// </summary>
public static class PageAccess
{
    public const string AccessDeniedPath = "/Account/AccessDenied";

    /// <summary>True when the signed-in account has <paramref name="permission"/>; otherwise redirects and returns false
    /// (the page must stop loading).</summary>
    public static async Task<bool> RequireAsync(this IPermissionService permissions, Permission permission, NavigationManager navigation)
    {
        if (await permissions.HasPermissionAsync(permission)) return true;
        navigation.NavigateTo(AccessDeniedPath, forceLoad: true);
        return false;
    }
}
