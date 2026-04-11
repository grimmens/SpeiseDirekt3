using System.ComponentModel.DataAnnotations;
using SpeiseDirekt.Data;

namespace SpeiseDirekt.Model;

public enum TenantRole
{
    GeneralManager = 0,
    Manager = 1,
    Cashier = 2,
    Employee = 3,
    Driver = 4,
    Customer = 5
}

[Flags]
public enum Permission : long
{
    None = 0,

    // Menu permissions
    MenusView = 1L << 0,
    MenusCreate = 1L << 1,
    MenusEdit = 1L << 2,
    MenusDelete = 1L << 3,

    // Category permissions
    CategoriesView = 1L << 4,
    CategoriesCreate = 1L << 5,
    CategoriesEdit = 1L << 6,
    CategoriesDelete = 1L << 7,

    // MenuItem permissions
    MenuItemsView = 1L << 8,
    MenuItemsCreate = 1L << 9,
    MenuItemsEdit = 1L << 10,
    MenuItemsDelete = 1L << 11,

    // Allergen permissions
    AllergensView = 1L << 12,
    AllergensCreate = 1L << 13,
    AllergensEdit = 1L << 14,
    AllergensDelete = 1L << 15,

    // QR Code permissions
    QrCodesView = 1L << 16,
    QrCodesCreate = 1L << 17,
    QrCodesEdit = 1L << 18,
    QrCodesDelete = 1L << 19,

    // Menu Combo permissions
    MenuCombosView = 1L << 21,
    MenuCombosCreate = 1L << 22,
    MenuCombosEdit = 1L << 23,
    MenuCombosDelete = 1L << 28,

    // Analytics
    AnalyticsView = 1L << 20,

    // User management
    UsersView = 1L << 24,
    UsersCreate = 1L << 25,
    UsersEdit = 1L << 26,
    UsersDelete = 1L << 27,

    // Subscription management
    SubscriptionManage = 1L << 30,

    // Delivery-specific
    DeliveryView = 1L << 32,
    DeliveryManage = 1L << 33,

    // POS Orders
    OrdersView = 1L << 34,
    OrdersCreate = 1L << 35,
    OrdersEdit = 1L << 36,
    OrdersDelete = 1L << 37,

    // Tax Rates
    TaxRatesView = 1L << 38,
    TaxRatesCreate = 1L << 39,
    TaxRatesEdit = 1L << 40,
    TaxRatesDelete = 1L << 41,

    // Discounts / Vouchers
    DiscountsView = 1L << 42,
    DiscountsCreate = 1L << 43,
    DiscountsEdit = 1L << 44,
    DiscountsDelete = 1L << 45,

    // POS Payments (restaurant transactions, not app subscription billing)
    PosPaymentsView = 1L << 46,
    PosPaymentsManage = 1L << 47,

    // Customer self-service orders (own orders only)
    OwnOrdersView = 1L << 48,
    OwnOrdersCreate = 1L << 49,
}

public class TenantUser
{
    public Guid Id { get; set; }

    [Required]
    public string ApplicationUserId { get; set; } = string.Empty;
    public ApplicationUser? ApplicationUser { get; set; }

    [Required]
    public string TenantOwnerId { get; set; } = string.Empty;
    public ApplicationUser? TenantOwner { get; set; }

    public Permission Permissions { get; set; } = Permission.None;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [StringLength(100)]
    public string? DisplayName { get; set; }
}
