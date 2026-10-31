using System.Text.RegularExpressions;
using Microsoft.Playwright;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>The ramen menu card from <see cref="E2ESeedData.RamenDishes"/> as guests see it on <c>/menu/{id}</c>.</summary>
public class RamenMenuTests : E2ETestBase
{
    public RamenMenuTests(E2EFixture fixture) : base(fixture) { }

    private static Regex Exact(string text) => new($"^\\s*{Regex.Escape(text)}\\s*$");

    /// <summary>The card of a dish in the list (exact name: "Smoky Men" vs. "Veggie Smoky Men").</summary>
    private ILocator DishCard(string name) =>
        Page.Locator("div.cursor-pointer", new() { Has = Page.Locator("h3", new() { HasTextRegex = Exact(name) }) });

    /// <summary>Allergen codes as the app renders them: sorted by code, comma separated.</summary>
    private static string ExpectedCodes(E2ESeedData.RamenDish dish) =>
        string.Join(", ", dish.AllergenCodes.OrderBy(c => c, StringComparer.Ordinal));

    [Fact]
    public async Task RamenMenu_ShowsAllDishesWithPriceAndAllergenCodes()
    {
        await OpenMenuAsync(E2ESeedData.RamenMenuId);

        await Assertions.Expect(Page.Locator("h1").First).ToHaveTextAsync(E2ESeedData.RamenMenuName);
        await Assertions.Expect(Page.Locator("div.cursor-pointer")).ToHaveCountAsync(E2ESeedData.RamenDishes.Count);

        foreach (var dish in E2ESeedData.RamenDishes)
        {
            var card = DishCard(dish.Name);
            await Assertions.Expect(card).ToHaveCountAsync(1);
            await Assertions.Expect(card).ToContainTextAsync(new Regex("17[.,]40"));
            await Assertions.Expect(card).ToContainTextAsync(ExpectedCodes(dish));
        }
    }

    [Theory]
    [InlineData("Tokyo Ramen")]
    [InlineData("Hokkaido Ramen")]
    [InlineData("Veggie Smoky Men")]
    [InlineData("Yasai Ramen")]
    public async Task RamenDish_OverlayShowsDescriptionAndAllergens(string name)
    {
        var dish = E2ESeedData.RamenDishes.Single(d => d.Name == name);
        await OpenMenuAsync(E2ESeedData.RamenMenuId);

        await DishCard(name).ClickAsync();

        await Assertions.Expect(Overlay).ToBeVisibleAsync();
        await Assertions.Expect(Overlay.GetByRole(AriaRole.Heading, new() { Name = name, Exact = true }).First).ToBeVisibleAsync();
        await Assertions.Expect(Overlay).ToContainTextAsync(dish.Description);
        await Assertions.Expect(Overlay).ToContainTextAsync(ExpectedCodes(dish));
        await Assertions.Expect(Overlay).ToContainTextAsync(new Regex("€ 17[.,]40"));
    }
}
