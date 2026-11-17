namespace SpeiseDirekt3.Components.Layout;

/// <summary>An entry of the back office's primary navigation (top bar and phone drawer).</summary>
public sealed record ShellNavItem(string Key, string Label, string Href, string IconPath);

/// <summary>A tab of the menu context bar (<see cref="MenuContextBar"/>).</summary>
public enum MenuTab { Overview, Categories, Items, Allergens, Combos, Settings }

/// <summary>
/// The back office's navigation in one place: the primary entries, which entry a URL belongs to, and which pages
/// belong to one menu (they get the menu context bar).
/// </summary>
public static class ShellNavigation
{
    public static readonly IReadOnlyList<ShellNavItem> Items =
    [
        new("dashboard", "Dashboard", "",
            "M3 12l2-2m0 0l7-7 7 7M5 10v10a1 1 0 001 1h3m10-11l2 2m-2-2v10a1 1 0 01-1 1h-3m-6 0a1 1 0 001-1v-4a1 1 0 011-1h2a1 1 0 011 1v4a1 1 0 001 1m-6 0h6"),
        new("menus", "Menus", "menus",
            "M9 5H7a2 2 0 00-2 2v10a2 2 0 002 2h8a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-3 7h3m-3 4h3m-6-4h.01M9 16h.01"),
        new("qrcodes", "QR Codes", "qrcodes",
            "M12 4v1m6 11h2m-6 0h-2v4m0-11v3m0 0h.01M12 12h4.01M16 20h4M4 12h4m12 0h.01M5 8h2a1 1 0 001-1V5a1 1 0 00-1-1H5a1 1 0 00-1 1v2a1 1 0 001 1zm12 0h2a1 1 0 001-1V5a1 1 0 00-1-1h-2a1 1 0 00-1 1v2a1 1 0 001 1zM5 20h2a1 1 0 001-1v-2a1 1 0 00-1-1H5a1 1 0 00-1 1v2a1 1 0 001 1z"),
        new("analytics", "Analytics", "analytics",
            "M9 19v-6a2 2 0 00-2-2H5a2 2 0 00-2 2v6a2 2 0 002 2h2a2 2 0 002-2zm0 0V9a2 2 0 012-2h2a2 2 0 012 2v10m-6 0a2 2 0 002 2h2a2 2 0 002-2m0 0V5a2 2 0 012-2h2a2 2 0 012 2v14a2 2 0 01-2 2h-2a2 2 0 01-2-2z"),
        new("restaurant", "Restaurant", "restaurant",
            "M13.5 21v-7.5a.75.75 0 01.75-.75h3a.75.75 0 01.75.75V21m-4.5 0H2.36m11.14 0H18m0 0h3.64m-1.39 0V9.349m-16.5 11.65V9.35m0 0a3.001 3.001 0 003.75-.615A2.993 2.993 0 009.75 9.75c.896 0 1.7-.393 2.25-1.016a2.993 2.993 0 002.25 1.016c.896 0 1.7-.393 2.25-1.016a3.001 3.001 0 003.75.614m-16.5 0a3.004 3.004 0 01-.621-4.72L4.318 3.44A1.5 1.5 0 015.378 3h13.243a1.5 1.5 0 011.06.44l1.19 1.189a3 3 0 01-.621 4.72m-13.5 8.65h3.75a.75.75 0 00.75-.75V13.5a.75.75 0 00-.75-.75H6.75a.75.75 0 00-.75.75v3.75c0 .414.336.75.75.75z"),
        new("team", "Team", "users",
            "M17 20h5v-2a3 3 0 00-5.356-1.857M17 20H7m10 0v-2c0-.656-.126-1.283-.356-1.857M7 20H2v-2a3 3 0 015.356-1.857M7 20v-2c0-.656.126-1.283.356-1.857m0 0a5.002 5.002 0 019.288 0M15 7a3 3 0 11-6 0 3 3 0 016 0zm6 3a2 2 0 11-4 0 2 2 0 014 0zM7 10a2 2 0 11-4 0 2 2 0 014 0z"),
    ];

    /// <summary>
    /// The primary entry a base-relative URL belongs to (<see cref="ShellNavItem.Key"/>), or null (Identity pages, …).
    /// Every page of a menu counts as "menus", the QR code editor as "qrcodes", the user pages as "team".
    /// </summary>
    public static string? SectionOf(string relativeUrl)
    {
        var path = PathOf(relativeUrl);
        if (path.Length == 0) return "dashboard";
        var first = path.Split('/')[0];
        return first switch
        {
            "menus" or "menudetail" or "categories" or "menuitems" or "allergens" or "combos" => "menus",
            "menu" when path.StartsWith("menu/edit", StringComparison.Ordinal) => "menus",
            "qrcodes" or "qrcode" => "qrcodes",
            "analytics" => "analytics",
            "restaurant" => "restaurant",
            "users" => "team",
            _ => null
        };
    }

    /// <summary>
    /// A page that belongs to one menu: the tab and the id in the URL. <paramref name="IsCategoryId"/> is set for
    /// <c>/MenuItems/Category/{categoryId}</c>, whose menu has to be looked up. Null for every other page (also
    /// <c>/menu/edit</c> without an id, i.e. a new menu).
    /// </summary>
    public static (MenuTab Tab, Guid Id, bool IsCategoryId)? MenuPageOf(string relativeUrl)
    {
        var segments = PathOf(relativeUrl).Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments switch
        {
            ["menudetail", var id] when Guid.TryParse(id, out var g) => (MenuTab.Overview, g, false),
            ["categories", var id] when Guid.TryParse(id, out var g) => (MenuTab.Categories, g, false),
            ["menuitems", "category", var id] when Guid.TryParse(id, out var g) => (MenuTab.Items, g, true),
            ["menuitems", var id] when Guid.TryParse(id, out var g) => (MenuTab.Items, g, false),
            ["allergens", var id] when Guid.TryParse(id, out var g) => (MenuTab.Allergens, g, false),
            ["combos", var id] when Guid.TryParse(id, out var g) => (MenuTab.Combos, g, false),
            ["menu", "edit", var id] when Guid.TryParse(id, out var g) => (MenuTab.Settings, g, false),
            _ => null
        };
    }

    /// <summary>The page of <paramref name="tab"/> for a menu (absolute path, as the pages link to each other).</summary>
    public static string HrefOf(MenuTab tab, Guid menuId) => tab switch
    {
        MenuTab.Overview => $"/menudetail/{menuId}",
        MenuTab.Categories => $"/Categories/{menuId}",
        MenuTab.Items => $"/MenuItems/{menuId}",
        MenuTab.Allergens => $"/allergens/{menuId}",
        MenuTab.Combos => $"/combos/{menuId}",
        MenuTab.Settings => $"/menu/edit/{menuId}",
        _ => throw new ArgumentOutOfRangeException(nameof(tab))
    };

    /// <summary>The owner's preview of a menu (guest page, opened in a new tab).</summary>
    public static string PreviewHrefOf(Guid menuId) => $"/menu/preview/{menuId}";

    /// <summary>"menus/…" from "menus/…?x=1#y", lower case, without leading or trailing slashes.</summary>
    private static string PathOf(string relativeUrl)
    {
        var end = relativeUrl.IndexOfAny(['?', '#']);
        var path = end < 0 ? relativeUrl : relativeUrl[..end];
        return path.Trim('/').ToLowerInvariant();
    }
}
