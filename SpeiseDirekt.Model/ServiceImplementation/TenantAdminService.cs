using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation;

/// <inheritdoc cref="ITenantAdminService"/>
/// <remarks>
/// The per-tenant numbers are grouped queries (one row per tenant each) joined in memory by the owner id parsed as a
/// <see cref="Guid"/>: <c>ApplicationUserId</c> is a GUID column whose text form is upper case on SQLite, the Identity id
/// is lower-case text, so neither a SQL join on the text nor a list of GUID parameters would match on every provider.
/// </remarks>
public class TenantAdminService : ITenantAdminService
{
    private readonly ApplicationDbContext db;
    private readonly IServiceScopeFactory scopes;
    private readonly ILogger<TenantAdminService> logger;

    public TenantAdminService(ApplicationDbContext db, IServiceScopeFactory scopes, ILogger<TenantAdminService> logger)
    {
        this.db = db;
        this.scopes = scopes;
        this.logger = logger;
    }

    public async Task<IReadOnlyList<TenantRow>> ListTenantsAsync(string? search)
    {
        var term = string.IsNullOrWhiteSpace(search) ? null : search.Trim();

        // One profile per tenant (unique index); its name is matched here, it has no normalized column
        var restaurantOf = (await db.RestaurantProfiles.IgnoreQueryFilters().AsNoTracking()
                .Select(p => new { p.ApplicationUserId, p.Name })
                .ToListAsync())
            .GroupBy(p => p.ApplicationUserId)
            .ToDictionary(g => g.Key, g => g.First().Name);

        var owners = db.Users.AsNoTracking().Where(u => u.TenantOwnerId == null);
        if (term is not null)
        {
            var email = term.ToUpperInvariant(); // NormalizedEmail is upper case (Identity's normalizer)
            var byRestaurant = restaurantOf
                .Where(r => r.Value.Contains(term, StringComparison.CurrentCultureIgnoreCase))
                .SelectMany(r => IdForms(r.Key))
                .ToList();
            owners = owners.Where(u => (u.NormalizedEmail != null && u.NormalizedEmail.Contains(email))
                                       || byRestaurant.Contains(u.Id));
        }

        var accounts = await owners
            .OrderBy(u => u.NormalizedEmail).ThenBy(u => u.Id)
            .Take(ITenantAdminService.MaxRows)
            .Select(u => new { u.Id, u.Email })
            .ToListAsync();
        if (accounts.Count == 0)
            return Array.Empty<TenantRow>();

        var menus = await db.Menus.IgnoreQueryFilters()
            .GroupBy(m => m.ApplicationUserId)
            .Select(g => new { Owner = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Owner, x => x.Count);
        var qrCodes = await db.QRCodes.IgnoreQueryFilters()
            .GroupBy(q => q.ApplicationUserId)
            .Select(g => new { Owner = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Owner, x => x.Count);
        var lastViews = await (from v in db.MenuViews
                               join m in db.Menus.IgnoreQueryFilters() on v.MenuId equals m.Id
                               group v by m.ApplicationUserId into g
                               select new { Owner = g.Key, Last = g.Max(x => x.ViewedAt) })
            .ToDictionaryAsync(x => x.Owner, x => x.Last);
        var members = await db.TenantUsers.AsNoTracking()
            .GroupBy(tu => new { tu.TenantOwnerId, tu.IsActive })
            .Select(g => new { g.Key.TenantOwnerId, g.Key.IsActive, Count = g.Count() })
            .ToListAsync();

        var paidTenant = AppRoles.Normalized(AppRoles.PaidTenant);
        var globalAdmin = AppRoles.Normalized(AppRoles.GlobalAdmin);
        var roles = await (from ur in db.UserRoles
                           join r in db.Roles on ur.RoleId equals r.Id
                           where r.NormalizedName == paidTenant || r.NormalizedName == globalAdmin
                           select new { ur.UserId, r.NormalizedName })
            .ToListAsync();
        bool HasRole(string userId, string role) =>
            roles.Any(r => r.NormalizedName == role && string.Equals(r.UserId, userId, StringComparison.OrdinalIgnoreCase));
        int Members(string ownerId, bool active) => members
            .Where(m => m.IsActive == active && string.Equals(m.TenantOwnerId, ownerId, StringComparison.OrdinalIgnoreCase))
            .Sum(m => m.Count);

        return accounts
            .Select(a =>
            {
                var owner = Guid.TryParse(a.Id, out var id) ? id : Guid.Empty;
                return new TenantRow(
                    a.Id,
                    a.Email ?? string.Empty,
                    restaurantOf.GetValueOrDefault(owner) ?? string.Empty,
                    menus.GetValueOrDefault(owner),
                    qrCodes.GetValueOrDefault(owner),
                    Members(a.Id, active: true),
                    Members(a.Id, active: false),
                    lastViews.TryGetValue(owner, out var last) ? DateTime.SpecifyKind(last, DateTimeKind.Utc) : null,
                    HasRole(a.Id, paidTenant),
                    HasRole(a.Id, globalAdmin));
            })
            .OrderBy(r => r.RestaurantName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Email, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<TenantMemberRow>> ListMembersAsync(string ownerId)
    {
        return await db.TenantUsers.AsNoTracking()
            .Where(tu => tu.TenantOwnerId == ownerId)
            .OrderBy(tu => tu.ApplicationUser!.Email)
            .Select(tu => new TenantMemberRow(tu.ApplicationUserId, tu.ApplicationUser!.Email ?? string.Empty,
                tu.DisplayName, tu.Role, tu.IsActive))
            .ToListAsync();
    }

    public async Task<SetPaidResult> SetPaidAsync(string ownerId, bool paid, string adminUserId)
    {
        if (string.IsNullOrEmpty(adminUserId) || !await db.HasRoleAsync(adminUserId, AppRoles.GlobalAdmin))
            return Refused(SetPaidStatus.NotAllowed, ownerId, adminUserId);

        // A fresh UserManager (and DbContext): in a Blazor circuit the scoped one lives as long as the circuit and
        // would hand back an account it tracked earlier, with a stale concurrency stamp
        using var scope = scopes.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var owner = await users.FindByIdAsync(ownerId);
        if (owner is null)
            return Refused(SetPaidStatus.NotFound, ownerId, adminUserId);
        if (!string.IsNullOrEmpty(owner.TenantOwnerId))
            return Refused(SetPaidStatus.NotAnOwner, ownerId, adminUserId);

        var wasPaid = await users.IsInRoleAsync(owner, AppRoles.PaidTenant);
        if (wasPaid != paid)
        {
            var result = paid
                ? await users.AddToRoleAsync(owner, AppRoles.PaidTenant)
                : await users.RemoveFromRoleAsync(owner, AppRoles.PaidTenant);
            if (!result.Succeeded)
            {
                logger.LogWarning("GlobalAdmin {AdminId} could not set PaidTenant for tenant {OwnerId}: {Errors}",
                    adminUserId, owner.Id, string.Join(", ", result.Errors.Select(e => e.Code)));
                return new SetPaidResult(SetPaidStatus.Failed, wasPaid, wasPaid);
            }
        }

        logger.LogInformation("GlobalAdmin {AdminId} set PaidTenant for tenant {OwnerId}: {Old} -> {New}",
            adminUserId, owner.Id, wasPaid, paid);
        return new SetPaidResult(SetPaidStatus.Saved, wasPaid, paid);
    }

    private SetPaidResult Refused(SetPaidStatus status, string ownerId, string adminUserId)
    {
        logger.LogWarning("GlobalAdmin {AdminId} could not set PaidTenant for {OwnerId}: {Status}", adminUserId, ownerId, status);
        return new SetPaidResult(status, false, false);
    }

    /// <summary>The text forms an Identity id of this GUID may have (Identity writes lower case).</summary>
    private static IEnumerable<string> IdForms(Guid id)
    {
        var text = id.ToString();
        return [text, text.ToUpperInvariant()];
    }
}
