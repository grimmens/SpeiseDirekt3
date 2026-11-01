using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>Free tier (no paid <c>TenantSubscription</c>): up to 99 QR codes a new one can be created, at 100 it is blocked.</summary>
public class QrCodeLimitTests : BackOfficeTestBase
{
    public QrCodeLimitTests(E2EFixture fixture) : base(fixture) { }

    /// <summary>Registers an owner with one menu and seeds <paramref name="count"/> QR codes directly into the DB.</summary>
    private async Task SetUpOwnerWithQrCodesAsync(int count)
    {
        var email = await RegisterNewOwnerAsync();

        Guid ownerId;
        await using (var db = App.CreateDbContext(Guid.Empty))
            ownerId = Guid.Parse((await db.Users.SingleAsync(u => u.Email == email)).Id);

        await App.SeedAsync(ownerId, db =>
        {
            var menu = new Menu { Id = Guid.NewGuid(), Name = "Limit Menü", Description = "", Language = MenuLanguage.German };
            db.Menus.Add(menu);
            for (var i = 0; i < count; i++)
                db.QRCodes.Add(new QRCode { Id = Guid.NewGuid(), Title = $"Tisch {i + 1}", MenuId = menu.Id });
            return Task.CompletedTask;
        });
    }

    private ILocator CreateButton => Page.GetByRole(AriaRole.Button, new() { Name = "Create New QR Code" });

    [Fact]
    public async Task FreeTier_With99QrCodes_CanCreateAnother()
    {
        await SetUpOwnerWithQrCodesAsync(99);

        await GotoInteractiveAsync("/qrcodes");

        await Assertions.Expect(CreateButton).ToBeEnabledAsync();
        await Assertions.Expect(Page.GetByText("Cannot create a new QR code at this time")).ToHaveCountAsync(0);

        // The 100th code can actually be created through the UI
        await CreateDirectQrViaUiAsync("Tisch 100", "Limit Menü");
    }

    [Fact]
    public async Task FreeTier_With100QrCodes_IsBlocked()
    {
        await SetUpOwnerWithQrCodesAsync(100);

        await GotoInteractiveAsync("/qrcodes");

        await Assertions.Expect(CreateButton).ToBeDisabledAsync();
        await Assertions.Expect(Page.GetByText("Cannot create a new QR code at this time")).ToBeVisibleAsync();
    }
}
