using SpeiseDirekt.Model;

namespace SpeiseDirekt.ServiceInterface;

/// <summary>One customer (owner account, <c>TenantOwnerId == null</c>) on the admin page.</summary>
/// <param name="OwnerId">The owner's Identity id (the tenant id).</param>
/// <param name="Email">The owner's e-mail address (empty when the account has none).</param>
/// <param name="RestaurantName"><see cref="RestaurantProfile.Name"/>, empty without a profile.</param>
/// <param name="MenuCount">Every menu of the tenant (language versions included).</param>
/// <param name="QrCodeCount">Every QR code of the tenant.</param>
/// <param name="ActiveMembers">Active team members (<see cref="TenantUser"/> rows of the owner).</param>
/// <param name="InactiveMembers">Deactivated team members.</param>
/// <param name="LastGuestView">The latest <see cref="MenuView.ViewedAt"/> over the tenant's menus (UTC), null without any.</param>
/// <param name="IsPaid">The owner has the role <c>PaidTenant</c>.</param>
/// <param name="IsGlobalAdmin">The owner account is a global admin (a badge on the page).</param>
public sealed record TenantRow(
    string OwnerId,
    string Email,
    string RestaurantName,
    int MenuCount,
    int QrCodeCount,
    int ActiveMembers,
    int InactiveMembers,
    DateTime? LastGuestView,
    bool IsPaid,
    bool IsGlobalAdmin)
{
    /// <summary>Every team member, active or not.</summary>
    public int MemberCount => ActiveMembers + InactiveMembers;
}

/// <summary>A team member of a tenant, read-only for the admin.</summary>
public sealed record TenantMemberRow(string UserId, string Email, string? DisplayName, TenantRole Role, bool IsActive);

/// <summary>What <see cref="ITenantAdminService.SetPaidAsync"/> did.</summary>
public enum SetPaidStatus
{
    /// <summary>The account has the wanted status now (also when it had it already: idempotent).</summary>
    Saved,
    /// <summary>No account with that id.</summary>
    NotFound,
    /// <summary>The id is a team member's, not an owner's: <c>PaidTenant</c> belongs to the owner. Nothing changed.</summary>
    NotAnOwner,
    /// <summary>The acting account is not a global admin (any more). Nothing changed.</summary>
    NotAllowed,
    /// <summary>Identity refused the change (logged with ids and error codes). Nothing changed.</summary>
    Failed
}

/// <summary>
/// Outcome of <see cref="ITenantAdminService.SetPaidAsync"/>: the owner's paid status before and after the call (equal
/// after a failed change; both false when the call was refused before the status was read: <see cref="SetPaidStatus.NotAllowed"/>,
/// <see cref="SetPaidStatus.NotFound"/>, <see cref="SetPaidStatus.NotAnOwner"/>).
/// </summary>
public sealed record SetPaidResult(SetPaidStatus Status, bool WasPaid, bool IsPaid)
{
    public bool Succeeded => Status == SetPaidStatus.Saved;
}

/// <summary>
/// The global admin's view of every customer (<c>/admin/kunden</c>): cross-tenant, every query ignores the tenant
/// filters. Reads tenant data and changes only the role <c>PaidTenant</c> of an owner account — never any tenant content.
/// </summary>
public interface ITenantAdminService
{
    /// <summary>Hard cap of <see cref="ListTenantsAsync"/> (no paging yet: few tenants).</summary>
    public const int MaxRows = 500;

    /// <summary>
    /// One row per owner account, at most <see cref="MaxRows"/>. <paramref name="search"/> (trimmed; empty = all) matches
    /// the e-mail or the restaurant name, ignoring case. Sorted by restaurant name, then by e-mail.
    /// </summary>
    Task<IReadOnlyList<TenantRow>> ListTenantsAsync(string? search);

    /// <summary>The team members of the owner <paramref name="ownerId"/> (e-mail, display name, role, active), by e-mail.</summary>
    Task<IReadOnlyList<TenantMemberRow>> ListMembersAsync(string ownerId);

    /// <summary>
    /// Gives the owner <paramref name="ownerId"/> the role <c>PaidTenant</c> or takes it away, through Identity's
    /// <c>UserManager</c>; idempotent. Only owner accounts are accepted, and only when <paramref name="adminUserId"/> is a
    /// global admin (read from the database). Logs the admin's and the owner's ids and the change, never addresses.
    /// </summary>
    Task<SetPaidResult> SetPaidAsync(string ownerId, bool paid, string adminUserId);
}
