using SpeiseDirekt.Model;

namespace SpeiseDirekt.Infrastructure;

public static class PermissionDefaults
{
    private static readonly Permission AllPermissions = (Permission)~0L;

    private static readonly Permission ManagerPermissions =
        Permission.MenusView | Permission.MenusCreate | Permission.MenusEdit | Permission.MenusDelete |
        Permission.CategoriesView | Permission.CategoriesCreate | Permission.CategoriesEdit | Permission.CategoriesDelete |
        Permission.MenuItemsView | Permission.MenuItemsCreate | Permission.MenuItemsEdit | Permission.MenuItemsDelete |
        Permission.AllergensView | Permission.AllergensCreate | Permission.AllergensEdit | Permission.AllergensDelete |
        Permission.QrCodesView | Permission.QrCodesCreate | Permission.QrCodesEdit | Permission.QrCodesDelete |
        Permission.MenuCombosView | Permission.MenuCombosCreate | Permission.MenuCombosEdit | Permission.MenuCombosDelete |
        Permission.AnalyticsView |
        Permission.UsersView | Permission.UsersCreate | Permission.UsersEdit;

    private static readonly Permission CashierPermissions =
        Permission.MenusView |
        Permission.CategoriesView |
        Permission.MenuItemsView | Permission.MenuItemsEdit |
        Permission.AllergensView |
        Permission.QrCodesView;

    private static readonly Permission EmployeePermissions =
        Permission.MenusView |
        Permission.CategoriesView |
        Permission.MenuItemsView |
        Permission.AllergensView;

    private static readonly Permission DriverPermissions =
        Permission.MenusView |
        Permission.DeliveryView | Permission.DeliveryManage;

    public static Permission GetDefaultPermissions(TenantRole role) => role switch
    {
        TenantRole.GeneralManager => AllPermissions,
        TenantRole.Manager => ManagerPermissions,
        TenantRole.Cashier => CashierPermissions,
        TenantRole.Employee => EmployeePermissions,
        TenantRole.Driver => DriverPermissions,
        _ => Permission.None
    };
}
