using System.Text.RegularExpressions;
using Microsoft.Playwright;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>Combo offers in the item overlay of the public menu page.</summary>
public class MenuComboDisplayTests : E2ETestBase
{
    public MenuComboDisplayTests(E2EFixture fixture) : base(fixture) { }

    // Prices are formatted with "F2" in the server's culture: accept "6.00" and "6,00"
    private static Regex Price(string value) => new(Regex.Escape(value).Replace("\\.", "[.,]"));

    private ILocator ComboSection => Overlay.Locator("div", new LocatorLocatorOptions
    {
        Has = Page.Locator("h2", new PageLocatorOptions { HasTextString = "Kombi-Angebote" })
    }).Last;

    [Fact]
    public async Task TriggerItem_ShowsComboWithDescriptionItemsAndPrice()
    {
        await OpenMenuAsync(E2ESeedData.MenuId);

        await ClickMenuItemAsync("Cheeseburger");

        await Assertions.Expect(Overlay).ToBeVisibleAsync();
        await Assertions.Expect(ComboSection).ToBeVisibleAsync();
        await Assertions.Expect(ComboSection).ToContainTextAsync(E2ESeedData.ComboName);
        await Assertions.Expect(ComboSection).ToContainTextAsync(E2ESeedData.ComboDescription);
        await Assertions.Expect(ComboSection.Locator("li")).ToHaveCountAsync(2);
        await Assertions.Expect(ComboSection.Locator("li").Nth(0)).ToContainTextAsync("Cola");
        await Assertions.Expect(ComboSection.Locator("li").Nth(1)).ToContainTextAsync("Pommes");
        await Assertions.Expect(ComboSection).ToContainTextAsync(Price("€ 6.00"));
    }

    [Fact]
    public async Task TriggerItem_ShowsSinglePriceSumAndSavings()
    {
        await OpenMenuAsync(E2ESeedData.MenuId);

        await ClickMenuItemAsync("Cheeseburger");

        // Pommes 4.00 + Cola 3.50 = 7.50 single, combo 6.00 → saves 1.50
        await Assertions.Expect(ComboSection.Locator(".line-through")).ToHaveTextAsync(Price("€ 7.50"));
        await Assertions.Expect(ComboSection).ToContainTextAsync(Price("Sie sparen € 1.50"));
    }

    [Fact]
    public async Task ItemThatIsNoTrigger_ShowsNoCombos()
    {
        await OpenMenuAsync(E2ESeedData.MenuId);

        // Pommes is included in the combo, but does not trigger it
        await ClickMenuItemAsync("Pommes");

        await Assertions.Expect(Overlay).ToBeVisibleAsync();
        await Assertions.Expect(Overlay).ToContainTextAsync("Hausgemacht");
        await Assertions.Expect(Overlay.GetByText("Kombi-Angebote")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task MenuWithoutCombos_ShowsNoCombos()
    {
        await OpenMenuAsync(E2ESeedData.MenuWithoutCombosId);

        await ClickMenuItemAsync("Espresso");

        await Assertions.Expect(Overlay).ToBeVisibleAsync();
        await Assertions.Expect(Overlay.GetByText("Kombi-Angebote")).ToHaveCountAsync(0);
    }
}
