using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SpeiseDirekt.Data;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation;

/// <inheritdoc cref="ITenantPlanService"/>
/// <remarks>
/// Counts ignore the tenant query filters and compare <c>ApplicationUserId</c> as a <see cref="Guid"/>: the column is a
/// GUID whose text form can be upper case (SQLite), the Identity id is lower case, so a string comparison would miss.
/// The check is count-then-insert; two creates at the same moment may both pass (accepted, no locking).
/// </remarks>
public class TenantPlanService : ITenantPlanService
{
    private readonly ApplicationDbContext db;
    private readonly FreePlanOptions free;

    public TenantPlanService(ApplicationDbContext db, IOptions<FreePlanOptions> options)
    {
        this.db = db;
        free = options.Value;
    }

    private int FreeMaxMenus => Math.Max(0, free.MaxMenus);
    private int FreeMaxQrCodes => Math.Max(0, free.MaxQrCodes);

    public async Task<bool> IsPaidAsync(string ownerId)
    {
        if (!TryOwner(ownerId, out var owner))
            return false;

        // Identity ids are the lower-case text of a GUID; normalizing lets an upper-case argument match too
        var id = owner.ToString();
        var paidTenant = AppRoles.Normalized(AppRoles.PaidTenant);
        return await db.UserRoles.AnyAsync(ur => ur.UserId == id
            && db.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == paidTenant)
            // Only the owner's role counts (decision 2: on a member account it has no effect)
            && db.Users.Any(u => u.Id == id && u.TenantOwnerId == null));
    }

    public async Task<PlanUsage> GetUsageAsync(string ownerId)
    {
        if (!TryOwner(ownerId, out var owner))
            return new PlanUsage(false, 0, 0, 0, 0); // nobody: nothing may be created

        var paid = await IsPaidAsync(ownerId);
        var menus = await CountMenusAsync(owner);
        var qrCodes = await CountQrCodesAsync(owner);
        return paid
            ? new PlanUsage(true, menus, qrCodes, null, null)
            : new PlanUsage(false, menus, qrCodes, FreeMaxMenus, FreeMaxQrCodes);
    }

    public async Task<bool> CanCreateMenuAsync(string ownerId)
    {
        if (!TryOwner(ownerId, out var owner))
            return false;
        return await IsPaidAsync(ownerId) || await CountMenusAsync(owner) < FreeMaxMenus;
    }

    public async Task<bool> CanCreateQrCodeAsync(string ownerId)
    {
        if (!TryOwner(ownerId, out var owner))
            return false;
        return await IsPaidAsync(ownerId) || await CountQrCodesAsync(owner) < FreeMaxQrCodes;
    }

    private Task<int> CountMenusAsync(Guid owner) =>
        db.Menus.IgnoreQueryFilters().CountAsync(m => m.ApplicationUserId == owner);

    private Task<int> CountQrCodesAsync(Guid owner) =>
        db.QRCodes.IgnoreQueryFilters().CountAsync(q => q.ApplicationUserId == owner);

    /// <summary>A real account id: a GUID other than <see cref="Guid.Empty"/> (anonymous).</summary>
    private static bool TryOwner(string? ownerId, out Guid owner) =>
        Guid.TryParse(ownerId, out owner) && owner != Guid.Empty;
}
