using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using SpeiseDirekt.Data;

namespace SpeiseDirekt.Model;

/// <summary>
/// The two roles of a tenant (AP14). The values are stored in <c>TenantUsers.Role</c> (int): 0 was "GeneralManager"
/// before AP14, the migration <c>TeamRolesOwnerAndManager</c> turned every row into 1.
/// </summary>
public enum TenantRole
{
    /// <summary>The account that registered the restaurant (no <c>TenantOwnerId</c>, no <see cref="TenantUser"/> row):
    /// exactly one per tenant, every permission. Only ever computed, never stored.</summary>
    Owner = 0,

    /// <summary>Every invited team member (<see cref="TenantUser"/>): menus, items, QR codes, restaurant profile and
    /// analytics, but not the team.</summary>
    Manager = 1
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

    // User management (the owner only)
    UsersView = 1L << 24,
    UsersCreate = 1L << 25,
    UsersEdit = 1L << 26,
    UsersDelete = 1L << 27,

    // Bits 30, 32 and 33 were SubscriptionManage, DeliveryView and DeliveryManage (removed in AP14, the product has
    // neither): do not reuse them.
}

/// <summary>
/// A team member of a tenant (an invited sub-account): always a <see cref="TenantRole.Manager"/>. Its permissions follow
/// the role (<see cref="SpeiseDirekt.Infrastructure.PermissionDefaults"/>); an inactive member has none.
/// </summary>
public class TenantUser
{
    public Guid Id { get; set; }

    [Required]
    public string ApplicationUserId { get; set; } = string.Empty;
    public ApplicationUser? ApplicationUser { get; set; }

    [Required]
    public string TenantOwnerId { get; set; } = string.Empty;
    public ApplicationUser? TenantOwner { get; set; }

    /// <summary>Always <see cref="TenantRole.Manager"/>; the permission check does not read it.</summary>
    public TenantRole Role { get; set; } = TenantRole.Manager;

    /// <summary>
    /// Not used since AP14 (permissions follow the role; there are no per-member permissions any more) and always
    /// <see cref="Permission.None"/>. Kept only because the column exists; not part of the API's JSON.
    /// </summary>
    [JsonIgnore]
    public Permission Permissions { get; set; } = Permission.None;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [StringLength(100)]
    public string? DisplayName { get; set; }
}
