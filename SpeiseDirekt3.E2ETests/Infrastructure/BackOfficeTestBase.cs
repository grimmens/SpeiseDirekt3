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

    /// <summary>The menu card on <c>/menus</c> (the sidebar tree links to the same URL, but without an <c>h3</c>).</summary>
    protected static ILocator MenuCardLink(IPage page, string menuName) =>
        page.Locator("a[href^='menudetail/']", new() { Has = page.Locator("h3", new() { HasTextString = menuName }) });

    /// <summary>Adds a category via <c>/Categories/{menuId}</c>.</summary>
    protected async Task CreateCategoryViaUiAsync(Guid menuId, string name)
    {
        await GotoInteractiveAsync($"/Categories/{menuId}");
        await Page.Locator("#Input\\.Name").FillAsync(name);
        await Button("Add Category").ClickAsync();
        await Assertions.Expect(Page.Locator("table a", new() { HasTextString = name })).ToBeVisibleAsync();
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
    /// Creates a menu item in the menu's first category via <c>/MenuItems/{menuId}</c> and the item editor.
    /// <paramref name="allergenCodes"/> are ticked in the editor's allergen list.
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

        await Button("New Menu Entry").ClickAsync();
        var editor = Page.Locator("#menuItemEditModal");
        await Assertions.Expect(editor).ToBeVisibleAsync();

        await editor.Locator("#MenuItem\\.Name").FillAsync(name);
        await editor.Locator("#MenuItem\\.Description").FillAsync(description);
        await editor.Locator("#MenuItem\\.Price").FillAsync(price.ToString(CultureInfo.InvariantCulture));
        foreach (var code in allergenCodes)
            await editor.Locator("label", new() { HasTextRegex = new Regex($"^\\s*{Regex.Escape(code)} - ") })
                .Locator("input[type=checkbox]").CheckAsync();

        await editor.Locator("#MenuItem\\.IsVegan").SetCheckedAsync(labels.Vegan);
        await editor.Locator("#MenuItem\\.IsVegetarian").SetCheckedAsync(labels.Vegetarian);
        await editor.Locator("#MenuItem\\.IsSpicy").SetCheckedAsync(labels.Spicy);

        await editor.GetByRole(AriaRole.Button, new() { Name = "Save Menu Item" }).ClickAsync();
        await Assertions.Expect(editor).ToBeHiddenAsync();
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
