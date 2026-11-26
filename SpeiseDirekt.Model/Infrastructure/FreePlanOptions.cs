namespace SpeiseDirekt.Infrastructure;

/// <summary>
/// Section <c>Plans:Free</c> (<c>appsettings.json</c> of both hosts): what a tenant without
/// <see cref="AppRoles.PaidTenant"/> may create. Paid tenants have no limit. Existing content above a limit stays and
/// stays editable; only creating is refused. The UI texts of the free tier ("The free plan includes one menu …") assume
/// the limit 1: change them together with these numbers.
/// </summary>
public sealed class FreePlanOptions
{
    public const string SectionName = "Plans:Free";

    /// <summary>Menus of a free tenant; a language version is a menu of its own and counts.</summary>
    public int MaxMenus { get; set; } = 1;

    /// <summary>QR codes of a free tenant.</summary>
    public int MaxQrCodes { get; set; } = 1;
}
