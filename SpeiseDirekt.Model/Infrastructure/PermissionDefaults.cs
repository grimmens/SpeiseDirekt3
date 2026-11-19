using SpeiseDirekt.Model;

namespace SpeiseDirekt.Infrastructure;

/// <summary>
/// The permissions of each role (AP14: permissions follow the role only — no per-member overrides). The owner has every
/// permission, a manager everything but the team (<c>Users*</c>), an account without a role (an inactive or removed
/// member) nothing. <see cref="ServiceImplementation.PermissionService"/> resolves the role; the pages and the API keep
/// asking for single permissions.
/// </summary>
public static class PermissionDefaults
{
    /// <summary>Every defined permission.</summary>
    public static readonly Permission Owner =
        Enum.GetValues<Permission>().Aggregate(Permission.None, (all, permission) => all | permission);

    /// <summary>
    /// Menus (with their language versions), categories, items, allergens, combos, QR codes (the restaurant profile
    /// goes with <see cref="Permission.MenusView"/>/<see cref="Permission.MenusEdit"/>) and analytics — not the team.
    /// </summary>
    public static readonly Permission Manager =
        Permission.MenusView | Permission.MenusCreate | Permission.MenusEdit | Permission.MenusDelete |
        Permission.CategoriesView | Permission.CategoriesCreate | Permission.CategoriesEdit | Permission.CategoriesDelete |
        Permission.MenuItemsView | Permission.MenuItemsCreate | Permission.MenuItemsEdit | Permission.MenuItemsDelete |
        Permission.AllergensView | Permission.AllergensCreate | Permission.AllergensEdit | Permission.AllergensDelete |
        Permission.QrCodesView | Permission.QrCodesCreate | Permission.QrCodesEdit | Permission.QrCodesDelete |
        Permission.MenuCombosView | Permission.MenuCombosCreate | Permission.MenuCombosEdit | Permission.MenuCombosDelete |
        Permission.AnalyticsView;

    /// <summary>The permissions of <paramref name="role"/>; none without a role.</summary>
    public static Permission For(TenantRole? role) => role switch
    {
        TenantRole.Owner => Owner,
        TenantRole.Manager => Manager,
        _ => Permission.None
    };
}
