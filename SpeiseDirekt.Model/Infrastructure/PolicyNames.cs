namespace SpeiseDirekt.Infrastructure;

public static class PolicyNames
{
    public const string PaidTenant = "PaidTenant";

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

    // Subscription
    public const string CanManageSubscription = "CanManageSubscription";

    // Delivery
    public const string CanViewDelivery = "CanViewDelivery";
    public const string CanManageDelivery = "CanManageDelivery";

    // POS Orders (restaurant transactions)
    public const string CanViewOrders = "CanViewOrders";
    public const string CanCreateOrders = "CanCreateOrders";
    public const string CanEditOrders = "CanEditOrders";
    public const string CanDeleteOrders = "CanDeleteOrders";

    // Tax Rates
    public const string CanViewTaxRates = "CanViewTaxRates";
    public const string CanCreateTaxRates = "CanCreateTaxRates";
    public const string CanEditTaxRates = "CanEditTaxRates";
    public const string CanDeleteTaxRates = "CanDeleteTaxRates";

    // Discounts / Vouchers
    public const string CanViewDiscounts = "CanViewDiscounts";
    public const string CanCreateDiscounts = "CanCreateDiscounts";
    public const string CanEditDiscounts = "CanEditDiscounts";
    public const string CanDeleteDiscounts = "CanDeleteDiscounts";

    // POS Payments (restaurant transactions, not app subscription billing)
    public const string CanViewPosPayments = "CanViewPosPayments";
    public const string CanManagePosPayments = "CanManagePosPayments";
}
