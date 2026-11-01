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
        await OpenPublicMenuPageAsync(guest, $"/menu/{menuId}");

        // Item card shows the codes (sorted), the item without allergens shows none
        var spaetzleCard = guest.Locator("div.cursor-pointer", new() { Has = guest.Locator("h3", new() { HasTextString = "Käsespätzle" }) });
        await Assertions.Expect(spaetzleCard).ToContainTextAsync("A, G");

        await ClickMenuItemAsync(guest, "Käsespätzle");
        await Assertions.Expect(OverlayOf(guest)).ToContainTextAsync("A, G");

        await OverlayOf(guest).Locator("button").First.ClickAsync();
        await ClickMenuItemAsync(guest, "Salat");
        await Assertions.Expect(OverlayOf(guest)).ToContainTextAsync("Ohne Allergene");
        await Assertions.Expect(OverlayOf(guest)).Not.ToContainTextAsync("A, G");
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
}
