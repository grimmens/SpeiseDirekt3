namespace SpeiseDirekt.Infrastructure;

/// <summary>
/// The app's two Identity roles (<c>AspNetRoles</c>, assigned in <c>AspNetUserRoles</c>; spec "global admin and paid
/// tenants", stage 1). Not to be confused with a tenant's team roles (<see cref="SpeiseDirekt.Model.TenantRole"/>).
/// Both are created at startup by <see cref="RoleSetup"/>, never by a migration, so the SQLite environments get them too.
/// </summary>
public static class AppRoles
{
    /// <summary>
    /// Administers every customer (<c>/admin/kunden</c>). Never assigned in the UI: <see cref="RoleSetup"/> syncs it at
    /// every start from <c>Admin:GlobalAdminEmails</c> (<see cref="AdminOptions"/>).
    /// </summary>
    public const string GlobalAdmin = "GlobalAdmin";

    /// <summary>
    /// A paying customer. Assigned to the tenant's <b>owner</b> account (members inherit it through their tenant; the role
    /// on a member account has no effect). Read from the database by <see cref="ServiceInterface.ITenantPlanService"/>.
    /// </summary>
    public const string PaidTenant = "PaidTenant";

    /// <summary>Both roles, in the order <see cref="RoleSetup"/> creates them.</summary>
    public static readonly IReadOnlyList<string> All = [GlobalAdmin, PaidTenant];

    /// <summary>
    /// The role's <c>NormalizedName</c> as Identity stores it (<c>UpperInvariantLookupNormalizer</c>): "GLOBALADMIN",
    /// "PAIDTENANT".
    /// </summary>
    public static string Normalized(string role) => role.ToUpperInvariant();
}
