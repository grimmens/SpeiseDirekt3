using SpeiseDirekt.Model;

namespace SpeiseDirekt.ServiceInterface;

public interface IPermissionService
{
    Task<bool> HasPermissionAsync(Permission permission);
    Task<Permission> GetCurrentPermissionsAsync();

    /// <summary><see cref="TenantRole.Owner"/>, <see cref="TenantRole.Manager"/> (an active team member) or null
    /// (anonymous, an inactive or a removed member).</summary>
    Task<TenantRole?> GetCurrentRoleAsync();
}
