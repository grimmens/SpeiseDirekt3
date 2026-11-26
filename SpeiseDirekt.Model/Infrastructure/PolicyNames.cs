namespace SpeiseDirekt.Infrastructure;

public static class PolicyNames
{
    /// <summary>The tenant has the role <see cref="AppRoles.PaidTenant"/> (<see cref="PaidTenantHandler"/>). Creating
    /// menus and QR codes is checked with <c>ITenantPlanService</c>, not with this policy.</summary>
    public const string PaidTenant = "PaidTenant";

    /// <summary>The signed-in account has the role <see cref="AppRoles.GlobalAdmin"/> (<see cref="GlobalAdminHandler"/>).</summary>
    public const string GlobalAdmin = "GlobalAdmin";

    // Menus
    public const string CanViewMenus = "CanViewMenus";
    public const string CanCreateMenus = "CanCreateMenus";
    public const string CanEditMenus = "CanEditMenus";
    public const string CanDeleteMenus = "CanDeleteMenus";

    // Categories
    public const string CanViewCategories = "CanViewCategories";
    public const string CanCreateCategories = "CanCreateCategories";
    public const string CanEditCategories = "CanEditCategories";
    public const string CanDeleteCategories = "CanDeleteCategories";

    // Menu Items
    public const string CanViewMenuItems = "CanViewMenuItems";
    public const string CanCreateMenuItems = "CanCreateMenuItems";
    public const string CanEditMenuItems = "CanEditMenuItems";
    public const string CanDeleteMenuItems = "CanDeleteMenuItems";

    // Allergens
    public const string CanViewAllergens = "CanViewAllergens";
    public const string CanCreateAllergens = "CanCreateAllergens";
    public const string CanEditAllergens = "CanEditAllergens";
    public const string CanDeleteAllergens = "CanDeleteAllergens";

    // Menu Combos
    public const string CanViewMenuCombos = "CanViewMenuCombos";
    public const string CanCreateMenuCombos = "CanCreateMenuCombos";
    public const string CanEditMenuCombos = "CanEditMenuCombos";
    public const string CanDeleteMenuCombos = "CanDeleteMenuCombos";

    // QR Codes
    public const string CanViewQrCodes = "CanViewQrCodes";
    public const string CanCreateQrCodes = "CanCreateQrCodes";
    public const string CanEditQrCodes = "CanEditQrCodes";
    public const string CanDeleteQrCodes = "CanDeleteQrCodes";

    // Analytics
    public const string CanViewAnalytics = "CanViewAnalytics";

    // Users
    public const string CanViewUsers = "CanViewUsers";
    public const string CanCreateUsers = "CanCreateUsers";
    public const string CanEditUsers = "CanEditUsers";
    public const string CanDeleteUsers = "CanDeleteUsers";
}
