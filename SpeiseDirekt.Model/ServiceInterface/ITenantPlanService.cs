namespace SpeiseDirekt.ServiceInterface;

/// <summary>
/// What a tenant has and may still create (<see cref="ITenantPlanService.GetUsageAsync"/>).
/// </summary>
/// <param name="IsPaid">The owner has the role <c>PaidTenant</c>.</param>
/// <param name="MenuCount">Every menu of the tenant (language versions included).</param>
/// <param name="QrCodeCount">Every QR code of the tenant.</param>
/// <param name="MaxMenus">The menu limit; <c>null</c> = unlimited (paid).</param>
/// <param name="MaxQrCodes">The QR code limit; <c>null</c> = unlimited (paid).</param>
public sealed record PlanUsage(bool IsPaid, int MenuCount, int QrCodeCount, int? MaxMenus, int? MaxQrCodes)
{
    /// <summary>One more menu is allowed (below the limit, or no limit).</summary>
    public bool CanCreateMenu => MaxMenus is not { } max || MenuCount < max;

    /// <summary>One more QR code is allowed (below the limit, or no limit).</summary>
    public bool CanCreateQrCode => MaxQrCodes is not { } max || QrCodeCount < max;
}

/// <summary>
/// The only check of the plans (free / paid): whether a tenant is paid and how many menus and QR codes it may create.
/// Works with an explicit owner id — callers pass <see cref="IUserIdProvider.GetUserId"/>, which is the owner for a team
/// member too — and reads the database at every call (never the sign-in cookie, never the tenant query filters), so a
/// change of the paid status counts at once, also for members who are signed in. An empty or invalid id
/// (<see cref="Guid.Empty"/> = anonymous) is never paid and may create nothing.
/// </summary>
public interface ITenantPlanService
{
    /// <summary>The owner account <paramref name="ownerId"/> has the role <c>PaidTenant</c> (on a member account it has no effect).</summary>
    Task<bool> IsPaidAsync(string ownerId);

    /// <summary>Paid status, the tenant's menu and QR code counts and its limits.</summary>
    Task<PlanUsage> GetUsageAsync(string ownerId);

    /// <summary>The tenant may create one more menu (a language version is a menu too).</summary>
    Task<bool> CanCreateMenuAsync(string ownerId);

    /// <summary>The tenant may create one more QR code.</summary>
    Task<bool> CanCreateQrCodeAsync(string ownerId);
}
