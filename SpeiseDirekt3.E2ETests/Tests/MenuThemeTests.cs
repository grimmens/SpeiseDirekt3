using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Design themes (<c>Menu.Theme</c>) style the guest view via <c>css/guest-themes.css</c> (variables per
/// <c>data-theme</c>) and <c>css/guest-menu.css</c>. Both are local, so colors and fonts are checked for real.
/// </summary>
public class MenuThemeTests : BackOfficeTestBase
{
    public MenuThemeTests(E2EFixture fixture) : base(fixture) { }

    private ILocator MenuDisplay(IPage page) => page.GetByTestId("menu-display");

    private async Task<Guid> SeedMenuAsync(DesignTheme theme)
    {
        var menuId = Guid.NewGuid();
        await App.SeedAsync(E2ESeedData.OwnerId, db =>
        {
            var category = new Category { Id = Guid.NewGuid(), Name = "Hauptspeisen", MenuId = menuId };
            db.Menus.Add(new Menu
            {
                Id = menuId, Name = $"Theme {theme}", Description = "Theme-Test", Theme = theme,
                Language = MenuLanguage.German, Categories = new List<Category> { category }
            });
            db.MenuItems.Add(new MenuItem
            {
                Id = Guid.NewGuid(), Name = "Testgericht", Description = "Beschreibung", Price = 9.90m, CategoryId = category.Id
            });
            return Task.CompletedTask;
        });
        return menuId;
    }

    private static Task<string> ComputedAsync(ILocator locator, string property) =>
        locator.EvaluateAsync<string>($"el => getComputedStyle(el).{property}");

    [Theory]
    [InlineData(DesignTheme.Elegant, "rgb(252, 248, 244)", "Playfair Display")]
    [InlineData(DesignTheme.Classic, "rgb(251, 245, 242)", "Playfair Display")]
    [InlineData(DesignTheme.Minimal, "rgb(255, 255, 255)", "Inter")]
    [InlineData(DesignTheme.Fancy, "rgb(255, 253, 252)", "Pacifico")]
    [InlineData(DesignTheme.Dark, "rgb(18, 18, 18)", "Playfair Display")]
    [InlineData(DesignTheme.Modern, "rgb(255, 255, 255)", "Manrope")]
    public async Task Theme_ChangesColorsAndFontsOfTheGuestView(DesignTheme theme, string pageBackground, string headingFont)
    {
        var menuId = await SeedMenuAsync(theme);

        await OpenMenuAsync(menuId);

        var display = MenuDisplay(Page);
        await Assertions.Expect(display).ToHaveAttributeAsync("data-theme", theme.ToString().ToLowerInvariant());
        (await ComputedAsync(display, "backgroundColor")).Should().Be(pageBackground);
        // The page behind the menu (body, overscroll) follows the theme as well
        (await ComputedAsync(Page.Locator("body"), "backgroundColor")).Should().Be(pageBackground);
        (await ComputedAsync(display.Locator("h1"), "fontFamily")).Should().Contain(headingFont);
        (await ComputedAsync(display.Locator("h2").First, "fontFamily")).Should().Contain(headingFont);
    }

    [Fact]
    public async Task DarkTheme_HasLightTextOnDarkSurfaces_EverywhereInTheGuestView()
    {
        var menuId = await SeedMenuAsync(DesignTheme.Dark);
        await OpenMenuAsync(menuId);

        var display = MenuDisplay(Page);
        (await ComputedAsync(display.Locator("h1"), "color")).Should().Be("rgb(242, 212, 218)");
        var row = display.GetByTestId("list-row").First;
        (await ComputedAsync(row.Locator("h3"), "color")).Should().Be("rgb(232, 232, 232)");
        (await ComputedAsync(display.GetByTestId("guest-topbar"), "backgroundColor")).Should().NotBe("rgb(255, 255, 255)");
        // Active chip: dark text on the rose accent, the others light text on dark grey
        var chips = display.GetByTestId("category-chip");
        (await ComputedAsync(chips.First, "backgroundColor")).Should().Be("rgb(196, 106, 127)");
        (await ComputedAsync(chips.First, "color")).Should().Be("rgb(26, 16, 19)");

        // Sheet surface is dark too
        await ClickMenuItemAsync("Testgericht");
        var panel = ItemSheet.Locator(".gm-sheet-panel");
        (await ComputedAsync(panel, "backgroundColor")).Should().Be("rgb(30, 30, 30)");
        (await ComputedAsync(ItemSheet.Locator("h2"), "color")).Should().Be("rgb(232, 232, 232)");
    }

    [Fact]
    public async Task TextCardInPhotoGrid_UsesTheThemeSurface()
    {
        // A category with one photo and one item without: grid, the second is a text card
        var menuId = Guid.NewGuid();
        await App.SeedAsync(E2ESeedData.OwnerId, db =>
        {
            var category = new Category { Id = Guid.NewGuid(), Name = "Hauptspeisen", MenuId = menuId };
            db.Menus.Add(new Menu { Id = menuId, Name = "Dunkle Karte", Description = "", Theme = DesignTheme.Dark, Language = MenuLanguage.German, Categories = new List<Category> { category } });
            db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = "Mit Foto", Description = "", Price = 9m, CategoryId = category.Id, ImagePath = "apple-touch-icon.png", SortOrder = 0 });
            db.MenuItems.Add(new MenuItem { Id = Guid.NewGuid(), Name = "Ohne Foto", Description = "Nur Text", Price = 8m, CategoryId = category.Id, SortOrder = 1 });
            return Task.CompletedTask;
        });
        await OpenMenuAsync(menuId);

        var textCard = ItemEntry("Ohne Foto");
        await Assertions.Expect(textCard).ToHaveAttributeAsync("data-has-image", "false");
        (await ComputedAsync(textCard.Locator("a"), "backgroundColor")).Should().Be("rgb(30, 30, 30)");
        (await ComputedAsync(textCard.Locator("h3"), "color")).Should().Be("rgb(232, 232, 232)");
    }

    [Fact]
    public async Task ThemeChosenInEditor_UpdatesPreview_AndIsShownToGuests()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuViaUiAsync("Theme Bistro");
        await CreateCategoryViaUiAsync(menuId, "Pizza");
        await CreateMenuItemViaUiAsync(menuId, "Margherita", "Tomate, Mozzarella", 9.00m);

        await GotoInteractiveAsync($"/menu/edit/{menuId}");
        var frame = Page.FrameLocator("[data-testid='menu-preview-iframe']");
        await Assertions.Expect(frame.GetByTestId("menu-display")).ToHaveAttributeAsync("data-theme", "modern");

        // The live preview next to the form follows the selection before saving: switched in place by postMessage
        // (js/menu-preview-draft.js → js/guest-preview.js), the frame is not reloaded
        await Page.Locator("#themeSelect").SelectOptionAsync("Dark");
        await Assertions.Expect(frame.GetByTestId("menu-display")).ToHaveAttributeAsync("data-theme", "dark");
        await Assertions.Expect(Page.GetByTestId("menu-preview-iframe")).ToHaveAttributeAsync("src", new Regex("theme=modern&lang=de$"));
        (await ComputedAsync(frame.GetByTestId("menu-display"), "backgroundColor")).Should().Be("rgb(18, 18, 18)");

        // Not saved yet: guests still see the old theme
        var (guestContext, guest) = await NewContextAsync();
        await using var _ = guestContext;
        await OpenGuestPageAsync(guest, $"/menu/{menuId}");
        await Assertions.Expect(MenuDisplay(guest)).ToHaveAttributeAsync("data-theme", "modern");

        await Button("Save Changes").ClickAsync();
        await Assertions.Expect(Page).ToHaveURLAsync(new Regex($"/menudetail/{menuId}$"));

        await OpenGuestPageAsync(guest, $"/menu/{menuId}");
        await Assertions.Expect(MenuDisplay(guest)).ToHaveAttributeAsync("data-theme", "dark");
        (await ComputedAsync(MenuDisplay(guest), "backgroundColor")).Should().Be("rgb(18, 18, 18)");
    }

    [Fact]
    public async Task ThemeParameter_IsIgnoredOnThePublicPage()
    {
        var menuId = await SeedMenuAsync(DesignTheme.Classic);

        await OpenGuestPageAsync(Page, $"/menu/{menuId}?theme=dark");

        await Assertions.Expect(MenuDisplay(Page)).ToHaveAttributeAsync("data-theme", "classic");
    }
}
