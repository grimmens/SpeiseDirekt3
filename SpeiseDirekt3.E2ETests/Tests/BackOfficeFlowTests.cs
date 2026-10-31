using Microsoft.Playwright;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>An owner builds a menu through the back office; an anonymous guest opens it via the QR code.</summary>
public class BackOfficeFlowTests : BackOfficeTestBase
{
    public BackOfficeFlowTests(E2EFixture fixture) : base(fixture) { }

    [Fact]
    public async Task Owner_CreatesMenuCategoryItemAndQr_GuestSeesItemViaQr()
    {
        await RegisterNewOwnerAsync();

        var menuId = await CreateMenuViaUiAsync("Bistro Flow");
        await CreateCategoryViaUiAsync(menuId, "Suppen");
        await CreateMenuItemViaUiAsync(menuId, "Kürbissuppe", "Mit Kernöl", 6.40m);
        var qrId = await CreateDirectQrViaUiAsync("Tisch 7", "Bistro Flow");

        // Anonymous guest in a separate browser context (no cookies of the owner)
        var (guestContext, guest) = await NewContextAsync();
        await using var _ = guestContext;

        await OpenPublicMenuPageAsync(guest, $"/qr/{qrId}");
        await Assertions.Expect(guest.Locator("h1").First).ToHaveTextAsync("Bistro Flow");
        await Assertions.Expect(guest.Locator("h2", new() { HasTextString = "Suppen" }).First).ToBeVisibleAsync();

        await ClickMenuItemAsync(guest, "Kürbissuppe");
        var overlay = OverlayOf(guest);
        await Assertions.Expect(overlay).ToBeVisibleAsync();
        await Assertions.Expect(overlay).ToContainTextAsync("Mit Kernöl");
        await Assertions.Expect(overlay).ToContainTextAsync(new System.Text.RegularExpressions.Regex("6[.,]40"));
    }

    [Fact]
    public async Task OwnersDoNotSeeEachOthersMenus()
    {
        await RegisterNewOwnerAsync();
        await CreateMenuViaUiAsync("Geheimes Menü A");

        var (otherContext, other) = await NewContextAsync();
        await using var _ = otherContext;
        await RegisterAsync(other, UniqueEmail());
        await GotoInteractiveAsync(other, "/menus");

        await Assertions.Expect(other.GetByRole(AriaRole.Heading, new() { Name = "No menus yet" })).ToBeVisibleAsync();
        await Assertions.Expect(other.GetByText("Geheimes Menü A")).ToHaveCountAsync(0);
    }
}
