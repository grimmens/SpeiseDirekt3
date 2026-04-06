using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation;

public class PermissionService : IPermissionService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ApplicationDbContext _db;
    private Permission? _cachedPermissions;
    private TenantRole? _cachedRole;
    private bool _resolved;

    public PermissionService(IHttpContextAccessor httpContextAccessor, ApplicationDbContext db)
    {
        _httpContextAccessor = httpContextAccessor;
        _db = db;
    }

    public bool IsOwner()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return false;

        var tenantOwnerId = user.FindFirstValue("TenantOwnerId");
        return string.IsNullOrEmpty(tenantOwnerId);
    }

    public async Task<TenantRole?> GetCurrentRoleAsync()
    {
        await EnsureResolvedAsync();
        return _cachedRole;
    }

    public async Task<Permission> GetCurrentPermissionsAsync()
    {
        await EnsureResolvedAsync();
        return _cachedPermissions ?? Permission.None;
    }

    public async Task<bool> HasPermissionAsync(Permission permission)
    {
        var current = await GetCurrentPermissionsAsync();
        return (current & permission) == permission;
    }

    private async Task EnsureResolvedAsync()
    {
        if (_resolved) return;
        _resolved = true;

        var user = _httpContextAccessor.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true) return;

        var userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return;

        var tenantOwnerId = user.FindFirstValue("TenantOwnerId");

        if (string.IsNullOrEmpty(tenantOwnerId))
        {
            // This user IS the owner — full permissions
            _cachedRole = TenantRole.GeneralManager;
            _cachedPermissions = (Permission)~0L;
            return;
        }

        // Sub-account: look up TenantUser record
        var tenantUser = await _db.TenantUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(tu => tu.ApplicationUserId == userId && tu.TenantOwnerId == tenantOwnerId);

        if (tenantUser == null || !tenantUser.IsActive)
        {
            _cachedRole = null;
            _cachedPermissions = Permission.None;
            return;
        }

        _cachedRole = tenantUser.Role;

        // Use custom permissions if set, otherwise use role defaults
        _cachedPermissions = tenantUser.Permissions != Permission.None
            ? tenantUser.Permissions
            : PermissionDefaults.GetDefaultPermissions(tenantUser.Role);
    }
}
