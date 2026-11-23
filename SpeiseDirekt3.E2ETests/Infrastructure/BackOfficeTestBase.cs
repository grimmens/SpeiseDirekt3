using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace SpeiseDirekt3.E2ETests.Infrastructure;

/// <summary>
/// Helpers that drive the back office through the UI (as a logged-in owner in <see cref="E2ETestBase.Page"/>).
/// Each helper starts with a full page load and waits for the interactive circuit before clicking.
/// </summary>
public abstract class BackOfficeTestBase : E2ETestBase
{
    protected BackOfficeTestBase(E2EFixture fixture) : base(fixture) { }

    private static readonly Regex GuidInPath = new(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");

    private static Guid ExtractGuid(string value) => Guid.Parse(GuidInPath.Match(value).Value);

    protected ILocator Button(string name, bool exact = false) =>
        Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = exact });

    protected ILocator Link(string name, bool exact = false) =>
        Page.GetByRole(AriaRole.Link, new() { Name = name, Exact = exact });

    /// <summary>The item edit page's form (<c>/MenuItems/Category/{id}/new</c>, <c>/MenuItems/item/{id}/edit</c>).</summary>
    protected ILocator ItemEditor => Page.GetByTestId("item-edit-form");

    /// <summary>The sticky save bar's "Save" on the item edit page.</summary>
    protected ILocator SaveItemButton => ItemEditor.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });

    /// <summary>Opens the edit page of an item from the item list ("Edit {name}" link) and waits for its form.</summary>
    protected async Task OpenItemEditorAsync(string itemName)
    {
        await Link($"Edit {itemName}", exact: true).ClickAsync();
        await Assertions.Expect(ItemEditor).ToBeVisibleAsync();
    }

    /// <summary>Saves the item edit page and waits for the item list it returns to.</summary>
    protected async Task SaveItemAndReturnAsync()
    {
        await SaveItemButton.ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/MenuItems/Category/[0-9a-fA-F-]+\\?highlight=", RegexOptions.IgnoreCase));
        await Assertions.Expect(Page.GetByTestId("menu-items")).ToBeVisibleAsync();
    }

    // --- Drag and drop (Shared/Sortable/SortableList, js/sortable-list.js) ---

    /// <summary>The grip handle of a sortable row.</summary>
    protected static ILocator HandleOf(ILocator row) => row.GetByTestId("sort-handle");

    /// <summary>
    /// Waits until the list of <paramref name="rowOrHandle"/> listens: js/sortable-list.js sets
    /// <c>data-sortable-ready</c> on the container once attached. The circuit is interactive before that (the list loads
    /// its script after its first interactive render), and a drag or arrow key that comes earlier does nothing.
    /// </summary>
    protected static Task WaitForSortableAsync(ILocator rowOrHandle) =>
        Assertions.Expect(rowOrHandle.Locator("xpath=ancestor::*[@data-sortable][1]")).ToHaveAttributeAsync("data-sortable-ready", "");

    /// <summary>Focuses a grip handle for the keyboard path (↑/↓), once its list listens.</summary>
    protected static async Task FocusHandleAsync(ILocator handle)
    {
        await WaitForSortableAsync(handle);
        await handle.FocusAsync();
    }

    /// <summary>
    /// Where the pointer has to go so that the dragged row's centre ends up just past the target row's middle (the
    /// handle need not sit at the row's centre, e.g. in the phone cards).
    /// </summary>
    private static async Task<(float X, float StartY, float EndY)> DragPathAsync(ILocator row, ILocator target)
    {
        var handle = HandleOf(row);
        await WaitForSortableAsync(handle);
        await handle.ScrollIntoViewIfNeededAsync();
        var h = (await handle.BoundingBoxAsync())!;
        var r = (await row.BoundingBoxAsync())!;
        var t = (await target.BoundingBoxAsync())!;
        var down = t.Y > r.Y;
        var wantedCentre = down ? t.Y + t.Height * 0.8f : t.Y + t.Height * 0.2f;
        var startY = h.Y + h.Height / 2;
        return (h.X + h.Width / 2, startY, startY + (wantedCentre - (r.Y + r.Height / 2)));
    }

    /// <summary>Drags <paramref name="row"/> by its handle with the mouse onto <paramref name="target"/>'s slot.</summary>
    protected static async Task DragRowAsync(IPage page, ILocator row, ILocator target)
    {
        var (x, startY, endY) = await DragPathAsync(row, target);
        await page.Mouse.MoveAsync(x, startY);
        await page.Mouse.DownAsync();
        await page.Mouse.MoveAsync(x, startY + Math.Sign(endY - startY) * 6, new() { Steps = 2 });
        await page.Mouse.MoveAsync(x, endY, new() { Steps = 14 });
        await page.Mouse.UpAsync();
    }

    protected Task DragRowAsync(ILocator row, ILocator target) => DragRowAsync(Page, row, target);

    /// <summary>
    /// A real touch drag (Chromium's DevTools protocol: touchStart, touchMove…, touchEnd), as a finger on a phone
    /// produces it; the page needs a context with <c>HasTouch</c>.
    /// </summary>
    protected static async Task TouchDragAsync(IPage page, float x, float startY, float endY, int steps = 12)
    {
        var cdp = await page.Context.NewCDPSessionAsync(page);
        object Touch(string type, float y) => new Dictionary<string, object>
        {
            ["type"] = type,
            ["touchPoints"] = type == "touchEnd" ? Array.Empty<object>() : new object[] { new Dictionary<string, object> { ["x"] = x, ["y"] = y } }
        };
        await cdp.SendAsync("Input.dispatchTouchEvent", (Dictionary<string, object>)Touch("touchStart", startY));
        for (var i = 1; i <= steps; i++)
        {
            await cdp.SendAsync("Input.dispatchTouchEvent", (Dictionary<string, object>)Touch("touchMove", startY + (endY - startY) * i / steps));
            await page.WaitForTimeoutAsync(16);
        }
        await cdp.SendAsync("Input.dispatchTouchEvent", (Dictionary<string, object>)Touch("touchEnd", endY));
        await cdp.DetachAsync();
    }

    /// <summary>Drags <paramref name="row"/> by its handle with a finger onto <paramref name="target"/>'s slot.</summary>
    protected static async Task TouchDragRowAsync(IPage page, ILocator row, ILocator target)
    {
        var (x, startY, endY) = await DragPathAsync(row, target);
        await TouchDragAsync(page, x, startY, endY);
    }

    /// <summary>The live region the sortable lists announce their moves in (Layout/MainLayout).</summary>
    protected static ILocator Announcer(IPage page) => page.GetByTestId("sd-announcer");

    /// <summary>The allergen checkbox with this code on the item edit page.</summary>
    protected ILocator AllergenCheckbox(string code) =>
        ItemEditor.Locator($"label[data-allergen-code='{code}'] input[type=checkbox]");

    /// <summary>Creates a menu via <c>/menu/edit</c> and returns its id (read from the link on <c>/menus</c>).</summary>
    protected async Task<Guid> CreateMenuViaUiAsync(string name, string description = "Created by E2E test")
    {
        await GotoInteractiveAsync("/menu/edit");
        await Page.Locator("#menuName").FillAsync(name);
        await Page.Locator("#menuDesc").FillAsync(description);
        await Button("Create Menu").ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/menus$"));
        var link = MenuCardLink(Page, name);
        await Assertions.Expect(link).ToBeVisibleAsync();
        return ExtractGuid((await link.GetAttributeAsync("href"))!);
    }

    /// <summary>The menu card on <c>/menus</c> (other links to the menu, e.g. the menu switcher's, have no <c>h3</c>).</summary>
    protected static ILocator MenuCardLink(IPage page, string menuName) =>
        page.Locator("a[href^='menudetail/']", new() { Has = page.Locator("h3", new() { HasTextString = menuName }) });

    /// <summary>Adds a category via <c>/Categories/{menuId}</c>.</summary>
    protected async Task CreateCategoryViaUiAsync(Guid menuId, string name)
    {
        await GotoInteractiveAsync($"/Categories/{menuId}");
        await Page.Locator("#Input\\.Name").FillAsync(name);
        await Button("Add Category").ClickAsync();
        await Assertions.Expect(Page.GetByTestId("category-row").Locator("a", new() { HasTextString = name })).ToBeVisibleAsync();
    }

    /// <summary>Creates an allergen via <c>/allergens/{menuId}</c> (expects success).</summary>
    protected async Task CreateAllergenViaUiAsync(Guid menuId, string code, string name)
    {
        await SubmitAllergenAsync(menuId, code, name);
        await Assertions.Expect(Page.Locator("table td", new() { HasTextString = name })).ToBeVisibleAsync();
    }

    /// <summary>Fills and submits the "Add New Allergen" form (no assertion).</summary>
    protected async Task SubmitAllergenAsync(Guid menuId, string code, string name)
    {
        await GotoInteractiveAsync($"/allergens/{menuId}");
        await Page.Locator("#Input\\.Code").FillAsync(code);
        await Page.Locator("#Input\\.Name").FillAsync(name);
        await Button("Add Allergen").ClickAsync();
    }

    /// <summary>
    /// Creates a menu item in the menu's first category via <c>/MenuItems/{menuId}</c> and the item edit page
    /// ("New Menu Entry"); returns on the list it goes back to. <paramref name="allergenCodes"/> are ticked in the
    /// page's allergen list.
    /// </summary>
    protected Task CreateMenuItemViaUiAsync(
        Guid menuId, string name, string description, decimal price, params string[] allergenCodes) =>
        CreateMenuItemViaUiAsync(menuId, name, description, price, DietaryLabels.None, allergenCodes);

    /// <summary>Dietary label checkboxes of the item editor.</summary>
    protected sealed record DietaryLabels(bool Vegan = false, bool Vegetarian = false, bool Spicy = false)
    {
        public static readonly DietaryLabels None = new();
    }

    /// <inheritdoc cref="CreateMenuItemViaUiAsync(Guid, string, string, decimal, string[])"/>
    protected async Task CreateMenuItemViaUiAsync(
        Guid menuId, string name, string description, decimal price, DietaryLabels labels, params string[] allergenCodes)
    {
        await GotoInteractiveAsync($"/MenuItems/{menuId}");
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/MenuItems/Category/", RegexOptions.IgnoreCase));

        await Link("New Menu Entry").ClickAsync();
        await Assertions.Expect(ItemEditor).ToBeVisibleAsync();

        await ItemEditor.Locator("#MenuItem\\.Name").FillAsync(name);
        await ItemEditor.Locator("#MenuItem\\.Description").FillAsync(description);
        await ItemEditor.Locator("#MenuItem\\.Price").FillAsync(price.ToString(CultureInfo.InvariantCulture));
        foreach (var code in allergenCodes)
            await AllergenCheckbox(code).CheckAsync();

        await ItemEditor.Locator("#MenuItem\\.IsVegan").SetCheckedAsync(labels.Vegan);
        await ItemEditor.Locator("#MenuItem\\.IsVegetarian").SetCheckedAsync(labels.Vegetarian);
        await ItemEditor.Locator("#MenuItem\\.IsSpicy").SetCheckedAsync(labels.Spicy);

        await SaveItemAndReturnAsync();
        await Assertions.Expect(Page.Locator("td", new() { HasTextString = name }).First).ToBeVisibleAsync();
    }

    /// <summary>Creates a simple (direct menu) QR code via <c>/qrcode/edit</c> and returns its id.</summary>
    protected async Task<Guid> CreateDirectQrViaUiAsync(string title, string menuName)
    {
        await GotoInteractiveAsync("/qrcode/edit");
        await Page.Locator("#qrTitle").FillAsync(title);
        await Page.Locator("#menuSelect").SelectOptionAsync(new SelectOptionValue { Label = menuName });
        await Button("Create QR Code").ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
        var link = Page.Locator("a[href^='qr/']", new() { HasTextString = title });
        await Assertions.Expect(link).ToBeVisibleAsync();
        return ExtractGuid((await link.GetAttributeAsync("href"))!);
    }
}
