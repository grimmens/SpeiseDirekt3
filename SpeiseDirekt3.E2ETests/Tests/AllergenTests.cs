using Microsoft.Playwright;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>Allergens: create in the back office, assign to an item, guests see the codes; duplicates are rejected.</summary>
public class AllergenTests : BackOfficeTestBase
{
    public AllergenTests(E2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task AllergensAssignedToItem_AreShownToGuests()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Allergen Menü");
        await CreateCategoryViaUiAsync(menuId, "Hauptgerichte");
        await CreateAllergenViaUiAsync(menuId, "A", "Gluten");
        await CreateAllergenViaUiAsync(menuId, "G", "Milch");
        await CreateMenuItemViaUiAsync(menuId, "Käsespätzle", "Mit Röstzwiebeln", 12.90m, "A", "G");
        await CreateMenuItemViaUiAsync(menuId, "Salat", "Ohne Allergene", 7.00m);

        var (guestContext, guest) = await NewContextAsync();
        await using var _ = guestContext;
        await OpenGuestPageAsync(guest, $"/menu/{menuId}");

        // Item entry shows the codes (sorted), the item without allergens shows none
        var spaetzle = ItemEntry(guest, "Käsespätzle");
        await Assertions.Expect(spaetzle.GetByTestId("item-allergens")).ToHaveTextAsync("A, G");
        await Assertions.Expect(spaetzle).ToHaveAttributeAsync("data-allergens", "A G");
        await Assertions.Expect(ItemEntry(guest, "Salat").GetByTestId("item-allergens")).ToHaveCountAsync(0);

        await ClickMenuItemAsync(guest, "Käsespätzle");
        await Assertions.Expect(ItemSheetOf(guest)).ToContainTextAsync("A, G");

        await CloseItemSheetAsync(guest);
        await ClickMenuItemAsync(guest, "Salat");
        await Assertions.Expect(ItemSheetOf(guest)).ToContainTextAsync("Ohne Allergene");
        await Assertions.Expect(ItemSheetOf(guest)).Not.ToContainTextAsync("A, G");
    }

    [Fact]
    public async Task DuplicateAllergenCode_IsRejected()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Duplikat Menü");
        await CreateAllergenViaUiAsync(menuId, "A", "Gluten");

        await SubmitAllergenAsync(menuId, "A", "Noch einmal Gluten");

        await Assertions.Expect(Page.GetByText("This code already exists for this menu.")).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("table td", new() { HasTextString = "Noch einmal Gluten" })).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("table tbody tr")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task SameAllergenCode_IsAllowedInAnotherMenu()
    {
        await RegisterNewOwnerAsync();
        var first = await CreateMenuViaUiAsync("Erstes Menü");
        var second = await CreateMenuViaUiAsync("Zweites Menü");
        await CreateAllergenViaUiAsync(first, "A", "Gluten");

        await CreateAllergenViaUiAsync(second, "A", "Gluten (Zweites Menü)");

        await Assertions.Expect(Page.GetByText("This code already exists for this menu.")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Allergen_IsEditedInTheRow_EnterSaves_EscapeCancels_DuplicateCodeIsRejected()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Inline Allergene");
        await CreateAllergenViaUiAsync(menuId, "A", "Gluten");
        await CreateAllergenViaUiAsync(menuId, "G", "Milch");

        var form = Page.GetByTestId("allergen-edit");
        var code = form.Locator("#Allergen\\.Code");
        var name = form.Locator("#Allergen\\.Name");
        async Task EditAsync(string allergenCode)
        {
            await Page.GetByRole(AriaRole.Button, new() { Name = $"Edit allergen {allergenCode}", Exact = true }).ClickAsync();
            await Assertions.Expect(form).ToBeVisibleAsync();
            await Assertions.Expect(code).ToBeFocusedAsync();
        }

        // Enter saves (code and name), the focus returns to the row's edit button
        await EditAsync("A");
        await name.FillAsync("Glutenhaltiges Getreide");
        await name.PressAsync("Enter");
        await Assertions.Expect(form).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("table td", new() { HasTextString = "Glutenhaltiges Getreide" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.GetByRole(AriaRole.Button, new() { Name = "Edit allergen A", Exact = true })).ToBeFocusedAsync();

        // Escape cancels
        await EditAsync("G");
        await name.FillAsync("Nicht speichern");
        await name.PressAsync("Escape");
        await Assertions.Expect(form).ToHaveCountAsync(0);
        await Assertions.Expect(Page.Locator("table td", new() { HasTextString = "Milch" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("table td", new() { HasTextString = "Nicht speichern" })).ToHaveCountAsync(0);

        // The same messages as the add form: a code of another allergen is refused, an empty name is required
        await EditAsync("G");
        await code.FillAsync("A");
        await code.PressAsync("Enter");
        await Assertions.Expect(form.GetByText("This code already exists for this menu.")).ToBeVisibleAsync();
        await code.FillAsync("G");
        await name.FillAsync("");
        await name.PressAsync("Enter");
        await Assertions.Expect(form.GetByText("The Name field is required.")).ToBeVisibleAsync();
        await form.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Assertions.Expect(form).ToHaveCountAsync(0);

        await GotoInteractiveAsync($"/allergens/{menuId}");
        await Assertions.Expect(Page.GetByTestId("allergen-code")).ToHaveTextAsync(new[] { "A", "G" });
        await Assertions.Expect(Page.Locator("table td", new() { HasTextString = "Glutenhaltiges Getreide" })).ToBeVisibleAsync();
        await Assertions.Expect(Page.Locator("table td", new() { HasTextString = "Milch" })).ToBeVisibleAsync();
    }
}
