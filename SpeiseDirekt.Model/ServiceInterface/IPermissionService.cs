using SpeiseDirekt.Model;

namespace SpeiseDirekt.ServiceInterface;

public interface IPermissionService
{
    Task<bool> HasPermissionAsync(Permission permission);
    Task<Permission> GetCurrentPermissionsAsync();
    Task<TenantRole?> GetCurrentRoleAsync();
    bool IsOwner();
}
