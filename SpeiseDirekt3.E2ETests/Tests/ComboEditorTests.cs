using System.Text.RegularExpressions;
using Microsoft.Playwright;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>Combo editor (<c>/combos/{MenuId}</c>): a combo created in the UI is offered to guests and in the preview.</summary>
public class ComboEditorTests : BackOfficeTestBase
{
    public ComboEditorTests(E2EFixture fixture) : base(fixture) { }

    private static Regex Price(string value) => new(Regex.Escape(value).Replace("\\.", "[.,]"));

    /// <summary>Owner with a menu (Burger, Pommes, Cola) and a combo created via the combo editor.</summary>
    private async Task<Guid> SetUpMenuWithComboViaUiAsync()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Combo Bistro");
        await CreateCategoryViaUiAsync(menuId, "Alles");
        await CreateMenuItemViaUiAsync(menuId, "Burger", "Rind", 9.50m);
        await CreateMenuItemViaUiAsync(menuId, "Pommes", "Knusprig", 4.00m);
        await CreateMenuItemViaUiAsync(menuId, "Cola", "0,33 l", 3.50m);

        await GotoInteractiveAsync($"/combos/{menuId}");
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "No combos yet" })).ToBeVisibleAsync();
        await Button("New Combo").First.ClickAsync();

        var editor = Page.Locator("div.fixed", new() { Has = Page.Locator("#ComboName") });
        await Assertions.Expect(editor).ToBeVisibleAsync();
        await editor.Locator("#ComboName").FillAsync("Burger Menü");
        await editor.Locator("#ComboDesc").FillAsync("Burger mit Pommes und Cola");
        await editor.Locator("#ComboPrice").FillAsync("6.00");

        // Trigger: the option text contains the formatted price, so select by value
        var burgerOption = editor.Locator("#TriggerItem option", new() { HasTextString = "Burger" });
        await editor.Locator("#TriggerItem").SelectOptionAsync((await burgerOption.GetAttributeAsync("value"))!);

        // Included items via the MultiSelectComboBox
        var search = editor.GetByPlaceholder("Search menu items...");
        foreach (var item in new[] { "Pommes", "Cola" })
        {
            await editor.Locator("input[type=text]").Last.ClickAsync();
            await editor.Locator("input[type=text]").Last.FillAsync(item);
            await editor.Locator("button", new() { HasTextString = item }).First.ClickAsync();
        }
        await Assertions.Expect(search).ToHaveCountAsync(0); // placeholder disappears once items are selected

        await Button("Create Combo").ClickAsync();
        await Assertions.Expect(editor).ToBeHiddenAsync();

        // The combo card lists trigger and included items
        await Assertions.Expect(Page.GetByRole(AriaRole.Heading, new() { Name = "Burger Menü" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByText("Includes (2 items)")).ToBeVisibleAsync();
        return menuId;
    }

    private static async Task ExpectComboInOverlayAsync(IPage page)
    {
        await ClickMenuItemAsync(page, "Burger");
        var section = OverlayOf(page).Locator("div", new()
        {
            Has = page.Locator("h2", new() { HasTextString = "Kombi-Angebote" })
        }).Last;
        await Assertions.Expect(section).ToBeVisibleAsync();
        await Assertions.Expect(section).ToContainTextAsync("Burger Menü");
        await Assertions.Expect(section).ToContainTextAsync("Burger mit Pommes und Cola");
        await Assertions.Expect(section.Locator("li")).ToHaveCountAsync(2);
        await Assertions.Expect(section).ToContainTextAsync(Price("€ 6.00"));
        await Assertions.Expect(section).ToContainTextAsync(Price("Sie sparen € 1.50"));
    }

    [Fact]
    public async Task ComboCreatedInEditor_IsShownToGuestsInItemOverlay()
    {
        var menuId = await SetUpMenuWithComboViaUiAsync();

        var (guestContext, guest) = await NewContextAsync();
        await using var _ = guestContext;
        await OpenPublicMenuPageAsync(guest, $"/menu/{menuId}");

        await ExpectComboInOverlayAsync(guest);

        // Included item that is no trigger: no combo offer
        await OverlayOf(guest).Locator("button").First.ClickAsync();
        await ClickMenuItemAsync(guest, "Pommes");
        await Assertions.Expect(OverlayOf(guest)).ToContainTextAsync("Knusprig");
        await Assertions.Expect(OverlayOf(guest).GetByText("Kombi-Angebote")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ComboIsShownInMenuPreview()
    {
        var menuId = await SetUpMenuWithComboViaUiAsync();

        await OpenPublicMenuPageAsync(Page, $"/menu/preview/{menuId}");

        await Assertions.Expect(Page.Locator("h1").First).ToHaveTextAsync("Combo Bistro");
        await ExpectComboInOverlayAsync(Page);
    }

    [Fact]
    public async Task ComboEditor_WithoutIncludedItems_ShowsValidationError()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Combo Validierung");
        await CreateCategoryViaUiAsync(menuId, "Alles");
        await CreateMenuItemViaUiAsync(menuId, "Burger", "Rind", 9.50m);

        await GotoInteractiveAsync($"/combos/{menuId}");
        await Button("New Combo").First.ClickAsync();
        var editor = Page.Locator("div.fixed", new() { Has = Page.Locator("#ComboName") });
        await editor.Locator("#ComboName").FillAsync("Unvollständig");
        await editor.Locator("#ComboPrice").FillAsync("5");
        var burgerOption = editor.Locator("#TriggerItem option", new() { HasTextString = "Burger" });
        await editor.Locator("#TriggerItem").SelectOptionAsync((await burgerOption.GetAttributeAsync("value"))!);

        await Button("Create Combo").ClickAsync();

        await Assertions.Expect(editor.GetByText("Select at least one included item.")).ToBeVisibleAsync();
        await Assertions.Expect(editor).ToBeVisibleAsync();
    }
}
