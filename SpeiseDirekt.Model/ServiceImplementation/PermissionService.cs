using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SpeiseDirekt.Data;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation;

public class PermissionService : IPermissionService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<PermissionService> _logger;
    private Permission? _cachedPermissions;
    private TenantRole? _cachedRole;
    private bool _resolved;

    public PermissionService(
        IHttpContextAccessor httpContextAccessor,
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        ILogger<PermissionService> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _db = db;
        _userManager = userManager;
        _logger = logger;
    }

    public async Task<TenantRole?> GetRoleForUserAsync(ApplicationUser user)
    {
        var roleNames = await _userManager.GetRolesAsync(user);

        var matched = new List<TenantRole>();
        foreach (var r in Enum.GetValues<TenantRole>())
        {
            if (roleNames.Contains(r.ToString()))
                matched.Add(r);
        }

        if (matched.Count > 1)
        {
            _logger.LogWarning(
                "User {UserId} has multiple tenant roles assigned: {Roles}. Only one is expected; returning the first ({Selected}).",
                user.Id, string.Join(", ", matched), matched[0]);
        }

        return matched.Count == 0 ? null : matched[0];
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

        // Sub-account: look up TenantUser record (for IsActive + permission overrides)
        var tenantUser = await _db.TenantUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(tu => tu.ApplicationUserId == userId && tu.TenantOwnerId == tenantOwnerId);

        if (tenantUser == null || !tenantUser.IsActive)
        {
            _cachedRole = null;
            _cachedPermissions = Permission.None;
            return;
        }

        _cachedRole = GetRoleFromClaims(user);

        // Use custom permissions if set, otherwise use role defaults
        _cachedPermissions = tenantUser.Permissions != Permission.None
            ? tenantUser.Permissions
            : PermissionDefaults.GetDefaultPermissions(_cachedRole ?? TenantRole.Customer);
    }

    private static TenantRole? GetRoleFromClaims(ClaimsPrincipal user)
    {
        foreach (var role in Enum.GetValues<TenantRole>())
        {
            if (user.IsInRole(role.ToString()))
                return role;
        }
        return null;
    }
}
