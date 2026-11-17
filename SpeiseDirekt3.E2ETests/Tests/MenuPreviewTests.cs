using FluentAssertions;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// Owner preview <c>/menu/preview/{id}</c>: login required, only the own tenant's menus, and item clicks
/// are not counted in the analytics (the public page <c>/menu/{id}</c> still counts them).
/// </summary>
public class MenuPreviewTests : BackOfficeTestBase
{
    public MenuPreviewTests(E2EFixture fixture) : base(fixture) { }

    private async Task<Guid> CreateMenuWithItemAsync(string menuName, string itemName)
    {
        var menuId = await CreateMenuViaUiAsync(menuName);
        await CreateCategoryViaUiAsync(menuId, "Kategorie");
        await CreateMenuItemViaUiAsync(menuId, itemName, "Beschreibung", 8.00m);
        return menuId;
    }

    private async Task<int> CountClicksAsync(Guid menuId)
    {
        await using var db = App.CreateDbContext(Guid.Empty);
        return await db.MenuItemClicks.CountAsync(c => c.MenuId == menuId);
    }

    [Fact]
    public async Task Preview_WithoutLogin_RedirectsToLogin()
    {
        await Page.GotoAsync($"/menu/preview/{E2ESeedData.MenuId}");

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/Account/Login\\?returnUrl=.*preview", RegexOptions.IgnoreCase));
        await Assertions.Expect(Page.GetByText(E2ESeedData.MenuName)).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Preview_OfAnotherTenantsMenu_IsNotFound()
    {
        await RegisterNewOwnerAsync();

        // The seeded bistro menu belongs to another tenant
        await Page.GotoAsync($"/menu/preview/{E2ESeedData.MenuId}");

        var notice = Page.GetByTestId("guest-notice");
        await Assertions.Expect(notice).ToHaveAttributeAsync("data-kind", "notfound");
        await Assertions.Expect(Page.GetByText(E2ESeedData.MenuName)).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Preview_ShowsOwnMenuInGuestLayout_WithoutRecordingClicks()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuWithItemAsync("Vorschau Bistro", "Flammkuchen");

        var clickRequests = 0;
        Page.Request += (_, r) => { if (r.Url.Contains("/guest/api/item-click")) clickRequests++; };

        await OpenGuestPageAsync(Page, $"/menu/preview/{menuId}");
        await Assertions.Expect(Page.Locator("h1").First).ToHaveTextAsync("Vorschau Bistro");
        await Assertions.Expect(Page.GetByTestId("guest-topbar")).ToBeVisibleAsync(); // the real guest layout
        await Assertions.Expect(Page.GetByTestId("menu-display")).ToHaveAttributeAsync("data-track-clicks", "false");

        await ClickMenuItemAsync("Flammkuchen");
        await Assertions.Expect(ItemSheet).ToContainTextAsync("Beschreibung");
        // A deep link (?item=) in the preview does not count either
        await OpenGuestPageAsync(Page, $"/menu/preview/{menuId}?item={await ItemIdAsync(menuId, "Flammkuchen")}");
        await Assertions.Expect(ItemSheet).ToBeVisibleAsync();
        await Page.WaitForTimeoutAsync(500); // a click would be recorded right away; give it time to show up
        (await CountClicksAsync(menuId)).Should().Be(0);
        clickRequests.Should().Be(0, "the preview does not even send clicks");

        // Contrast: the public page still records the click
        var (guestContext, guest) = await NewContextAsync();
        await using var _ = guestContext;
        await OpenGuestPageAsync(guest, $"/menu/{menuId}");
        await ClickMenuItemAsync(guest, "Flammkuchen");
        await AssertEventuallyAsync(async () => (await CountClicksAsync(menuId)) == 1);
    }

    [Fact]
    public async Task Preview_ThemeParameter_ShowsAnotherThemeForTheOwner()
    {
        await RegisterNewOwnerAsync();
        var menuId = await CreateMenuWithItemAsync("Themen Vorschau", "Suppe");

        await OpenGuestPageAsync(Page, $"/menu/preview/{menuId}?theme=fancy");
        await Assertions.Expect(Page.GetByTestId("menu-display")).ToHaveAttributeAsync("data-theme", "fancy");

        // Unknown values fall back to the saved theme
        await OpenGuestPageAsync(Page, $"/menu/preview/{menuId}?theme=nonsense");
        await Assertions.Expect(Page.GetByTestId("menu-display")).ToHaveAttributeAsync("data-theme", "modern");
    }

    private async Task<Guid> ItemIdAsync(Guid menuId, string name)
    {
        await using var db = App.CreateDbContext(Guid.Empty);
        return await db.MenuItems.IgnoreQueryFilters().Where(i => i.Name == name && i.Category!.MenuId == menuId).Select(i => i.Id).SingleAsync();
    }

    private static async Task AssertEventuallyAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 50; i++)
        {
            if (await condition()) return;
            await Task.Delay(100);
        }
        (await condition()).Should().BeTrue();
    }
}
