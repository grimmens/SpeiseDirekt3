using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.ServiceInterface;

public interface IPermissionService
{
    Task<bool> HasPermissionAsync(Permission permission);
    Task<Permission> GetCurrentPermissionsAsync();
    Task<TenantRole?> GetCurrentRoleAsync();
    Task<TenantRole?> GetRoleForUserAsync(ApplicationUser user);
    bool IsOwner();
}
