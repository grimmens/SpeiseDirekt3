using Microsoft.Extensions.Localization;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;

namespace SpeiseDirekt3.Components.UserManagement;

/// <summary>
/// The permission areas and their actions as the team pages show them: the role legend's tooltips on <c>/users</c> and
/// the permission checkboxes on <c>/users/edit/{id}</c>. Names are TeamText keys (AP12): <c>Area_*</c>, <c>Action_*</c>
/// (lower case, for the legend's lines) and <c>Perm_*</c> (capitalised, for the checkboxes).
/// </summary>
public static class PermissionCatalog
{
    public sealed record Entry(string ActionKey, string LabelKey, Permission Value);

    public sealed record Area(string NameKey, IReadOnlyList<Entry> Entries);

    private static Entry View(Permission p) => new(nameof(TeamText.Action_View), nameof(TeamText.Perm_View), p);
    private static Entry Create(Permission p) => new(nameof(TeamText.Action_Create), nameof(TeamText.Perm_Create), p);
    private static Entry Edit(Permission p) => new(nameof(TeamText.Action_Edit), nameof(TeamText.Perm_Edit), p);
    private static Entry Delete(Permission p) => new(nameof(TeamText.Action_Delete), nameof(TeamText.Perm_Delete), p);
    private static Entry Manage(Permission p) => new(nameof(TeamText.Action_Manage), nameof(TeamText.Perm_Manage), p);

    public static readonly IReadOnlyList<Area> Areas =
    [
        new(nameof(TeamText.Area_Menus), [View(Permission.MenusView), Create(Permission.MenusCreate), Edit(Permission.MenusEdit), Delete(Permission.MenusDelete)]),
        new(nameof(TeamText.Area_Categories), [View(Permission.CategoriesView), Create(Permission.CategoriesCreate), Edit(Permission.CategoriesEdit), Delete(Permission.CategoriesDelete)]),
        new(nameof(TeamText.Area_MenuItems), [View(Permission.MenuItemsView), Create(Permission.MenuItemsCreate), Edit(Permission.MenuItemsEdit), Delete(Permission.MenuItemsDelete)]),
        new(nameof(TeamText.Area_Allergens), [View(Permission.AllergensView), Create(Permission.AllergensCreate), Edit(Permission.AllergensEdit), Delete(Permission.AllergensDelete)]),
        new(nameof(TeamText.Area_Combos), [View(Permission.MenuCombosView), Create(Permission.MenuCombosCreate), Edit(Permission.MenuCombosEdit), Delete(Permission.MenuCombosDelete)]),
        new(nameof(TeamText.Area_QrCodes), [View(Permission.QrCodesView), Create(Permission.QrCodesCreate), Edit(Permission.QrCodesEdit), Delete(Permission.QrCodesDelete)]),
        new(nameof(TeamText.Area_Analytics), [View(Permission.AnalyticsView)]),
        new(nameof(TeamText.Area_Team),
        [
            View(Permission.UsersView),
            new(nameof(TeamText.Action_Invite), nameof(TeamText.Perm_Invite), Permission.UsersCreate),
            Edit(Permission.UsersEdit),
            new(nameof(TeamText.Action_Remove), nameof(TeamText.Perm_Remove), Permission.UsersDelete)
        ]),
        new(nameof(TeamText.Area_Delivery), [View(Permission.DeliveryView), Manage(Permission.DeliveryManage)]),
        new(nameof(TeamText.Area_Subscription), [Manage(Permission.SubscriptionManage)]),
    ];

    /// <summary>Tooltip of a role in the legend: what it may do by default, one line per area ("Menus: view, create").</summary>
    public static string RoleSummary(IStringLocalizer<TeamText> texts, string roleName, TenantRole role)
    {
        var permissions = PermissionDefaults.GetDefaultPermissions(role);
        var lines = Areas
            .Select(a => (a.NameKey, Actions: a.Entries.Where(e => permissions.HasFlag(e.Value)).Select(e => texts[e.ActionKey].Value).ToList()))
            .Where(a => a.Actions.Count > 0)
            .Select(a => texts["Summary_Line", texts[a.NameKey], string.Join(", ", a.Actions)].Value)
            .ToList();
        return lines.Count == 0
            ? texts["Summary_None", roleName]
            : texts["Summary_Heading", roleName] + "\n" + string.Join("\n", lines);
    }
}
