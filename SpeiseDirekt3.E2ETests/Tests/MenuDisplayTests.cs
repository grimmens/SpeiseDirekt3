using FluentAssertions;
using Microsoft.Playwright;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>Public menu page (/menu/{id}): menu, categories, items, item overlay.</summary>
public class MenuDisplayTests : E2ETestBase
{
    public MenuDisplayTests(E2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task MenuPage_ShowsMenuNameCategoriesAndItems()
    {
        await OpenMenuAsync(E2ESeedData.MenuId);

        await Assertions.Expect(Page.Locator("h1").First).ToHaveTextAsync(E2ESeedData.MenuName);
        foreach (var category in new[] { "Hauptspeisen", "Beilagen", "Getränke" })
            await Assertions.Expect(Page.Locator("h2", new PageLocatorOptions { HasTextString = category })).ToBeVisibleAsync();
        foreach (var item in new[] { "Cheeseburger", "Gartensalat", "Pommes", "Cola" })
            await Assertions.Expect(Page.Locator("h3", new PageLocatorOptions { HasTextString = item }).First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ClickItem_OpensOverlayWithDetails_AndBackButtonClosesIt()
    {
        await OpenMenuAsync(E2ESeedData.MenuId);

        await ClickMenuItemAsync("Gartensalat");

        await Assertions.Expect(Overlay).ToBeVisibleAsync();
        await Assertions.Expect(Overlay).ToContainTextAsync("Saisonal");
        await Assertions.Expect(Overlay).ToContainTextAsync("Hauptspeisen".ToUpperInvariant(), new() { IgnoreCase = true });

        await Overlay.Locator("button").First.ClickAsync();
        await Assertions.Expect(Overlay).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task UnknownMenu_ShowsNotFound()
    {
        await Page.GotoAsync($"/menu/{Guid.NewGuid()}");

        var notice = Page.GetByTestId("guest-notice");
        await Assertions.Expect(notice).ToHaveAttributeAsync("data-kind", "notfound");
        await Assertions.Expect(notice.GetByRole(AriaRole.Heading, new() { Name = "Menü nicht gefunden" })).ToBeVisibleAsync();
        await Assertions.Expect(notice.GetByRole(AriaRole.Link, new() { Name = "Zur Startseite" })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Fixture_UsesSqliteFileInTempDirectory()
    {
        File.Exists(Fixture.App.DatabasePath).Should().BeTrue();
        Path.GetDirectoryName(Fixture.App.DatabasePath).Should().Be(Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar));
        await Task.CompletedTask;
    }
}
