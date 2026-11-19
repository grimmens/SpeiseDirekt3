using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation;

/// <summary>
/// The signed-in account's role and permissions (AP14: they follow the role only). Read from the database, not from
/// claims, so a member who was deactivated or removed loses every permission at once: the owner (an account without
/// <c>TenantOwnerId</c>) has all, an active <see cref="TenantUser"/> is a manager, anything else has none.
/// Resolved once per instance (the service is transient: every component gets its own).
/// </summary>
public class PermissionService : IPermissionService
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ApplicationDbContext _db;
    private TenantRole? _cachedRole;
    private bool _resolved;

    public PermissionService(IHttpContextAccessor httpContextAccessor, ApplicationDbContext db)
    {
        _httpContextAccessor = httpContextAccessor;
        _db = db;
    }

    public async Task<TenantRole?> GetCurrentRoleAsync()
    {
        await EnsureResolvedAsync();
        return _cachedRole;
    }

    public async Task<Permission> GetCurrentPermissionsAsync() =>
        PermissionDefaults.For(await GetCurrentRoleAsync());

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

        var account = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.TenantOwnerId })
            .FirstOrDefaultAsync();
        if (account is null) return; // removed in the meantime

        if (string.IsNullOrEmpty(account.TenantOwnerId))
        {
            _cachedRole = TenantRole.Owner;
            return;
        }

        // A team member: a manager while its TenantUser row is active (the row's Role is not read)
        var active = await _db.TenantUsers
            .AsNoTracking()
            .AnyAsync(tu => tu.ApplicationUserId == userId && tu.TenantOwnerId == account.TenantOwnerId && tu.IsActive);
        _cachedRole = active ? TenantRole.Manager : null;
    }
}
