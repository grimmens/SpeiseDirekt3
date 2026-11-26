using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using SpeiseDirekt.Model;
using SpeiseDirekt3.E2ETests.Infrastructure;

namespace SpeiseDirekt3.E2ETests.Tests;

/// <summary>
/// The free plan's QR code limit (an owner without the role <c>PaidTenant</c>: one QR code, <c>Plans:Free:MaxQrCodes</c>,
/// <c>ITenantPlanService</c>): the first code can be created; with one code "Create New QR Code" stays visible but
/// disabled with the hint, and <c>/qrcode/edit</c> sends the owner back to the list; editing the existing code stays
/// allowed. The menu side and the dashboard are in <see cref="PlanLimitTests"/>.
/// </summary>
public class QrCodeLimitTests : BackOfficeTestBase
{
    public QrCodeLimitTests(E2EFixture fixture) : base(fixture) { }

    private const string MenuName = "Limit Menü";
    private const string Hint = "The free plan includes one QR code. For more, please contact us.";

    /// <summary>Registers a <b>free</b> owner with one menu and seeds <paramref name="count"/> QR codes directly into the DB.</summary>
    private async Task<Guid> SetUpFreeOwnerWithQrCodesAsync(int count)
    {
        var ownerId = await OwnerIdAsync(await RegisterNewOwnerAsync(paid: false));
        (await App.IsPaidAsync(ownerId)).Should().BeFalse("a self-registered owner starts on the free plan");

        await App.SeedAsync(ownerId, db =>
        {
            var menu = new Menu { Id = Guid.NewGuid(), Name = MenuName, Description = "", Language = MenuLanguage.German };
            db.Menus.Add(menu);
            for (var i = 0; i < count; i++)
                db.QRCodes.Add(new QRCode { Id = Guid.NewGuid(), Title = $"Tisch {i + 1}", MenuId = menu.Id });
            return Task.CompletedTask;
        });
        return ownerId;
    }

    private ILocator CreateButton => Page.GetByTestId("qr-create-button");
    private ILocator LimitHint => Page.GetByTestId("qr-limit-hint");

    private async Task<int> QrCodeCountAsync(Guid ownerId)
    {
        await using var db = App.CreateDbContext(ownerId);
        return await db.QRCodes.CountAsync();
    }

    [Fact]
    public async Task FreeTier_WithoutQrCode_CreatesTheFirst_ThenIsBlocked()
    {
        var ownerId = await SetUpFreeOwnerWithQrCodesAsync(0);

        await GotoInteractiveAsync("/qrcodes");
        await Assertions.Expect(CreateButton).ToBeEnabledAsync();
        await Assertions.Expect(Page.GetByTestId("qr-create-first-button")).ToBeEnabledAsync();
        await Assertions.Expect(LimitHint).ToHaveCountAsync(0);
        await Assertions.Expect(Page.GetByTestId("qr-limit-hint-empty")).ToHaveCountAsync(0);

        // The one code of the free plan is created through the UI …
        await CreateDirectQrViaUiAsync("Tisch 1", MenuName);
        (await QrCodeCountAsync(ownerId)).Should().Be(1);

        // … and the list it returns to blocks the next one at once
        await Assertions.Expect(CreateButton).ToBeDisabledAsync();
        await Assertions.Expect(LimitHint).ToHaveTextAsync(Hint);
    }

    [Fact]
    public async Task FreeTier_WithOneQrCode_IsBlocked_TheButtonStaysWithTheHint()
    {
        await SetUpFreeOwnerWithQrCodesAsync(1);

        await GotoInteractiveAsync("/qrcodes");

        await Assertions.Expect(CreateButton).ToBeVisibleAsync();
        await Assertions.Expect(CreateButton).ToBeDisabledAsync();
        await Assertions.Expect(CreateButton).ToHaveTextAsync("Create New QR Code");
        await Assertions.Expect(CreateButton).ToHaveAttributeAsync("aria-describedby", "qr-limit-hint");
        await Assertions.Expect(LimitHint).ToBeVisibleAsync();
        await Assertions.Expect(LimitHint).ToHaveTextAsync(Hint);
        // No contact address configured: the phrase stays plain text, no price anywhere
        await Assertions.Expect(LimitHint.GetByTestId("plan-limit-contact")).ToHaveCountAsync(0);
        await Assertions.Expect(LimitHint).Not.ToContainTextAsync("€");
        await Assertions.Expect(Page.GetByText("Cannot create a new QR code at this time")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task FreeTier_AtTheLimit_DirectCreateUrl_RedirectsToTheList()
    {
        var ownerId = await SetUpFreeOwnerWithQrCodesAsync(1);

        // The limit is enforced on /qrcode/edit itself, not only by the disabled button
        await GotoInteractiveAsync("/qrcode/edit");

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
        await Assertions.Expect(Page.Locator("#qrTitle")).ToHaveCountAsync(0);
        await Assertions.Expect(CreateButton).ToBeDisabledAsync();
        (await QrCodeCountAsync(ownerId)).Should().Be(1);
    }

    [Fact]
    public async Task FreeTier_AtTheLimit_EditingAnExistingQrCode_IsStillAllowed()
    {
        var ownerId = await SetUpFreeOwnerWithQrCodesAsync(1);
        await GotoInteractiveAsync("/qrcodes");

        await Page.GetByRole(AriaRole.Button, new() { Name = "Edit" }).First.ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcode/edit/"));
        await Assertions.Expect(Page.Locator("#qrTitle")).ToBeEnabledAsync();
        await Page.Locator("#qrTitle").FillAsync("Tisch 1 (Terrasse)");
        await Button("Save Changes").ClickAsync();

        await Assertions.Expect(Page).ToHaveURLAsync(new Regex("/qrcodes$"));
        await Assertions.Expect(Page.GetByTestId("qr-card").Filter(new() { HasText = "Tisch 1 (Terrasse)" })).ToBeVisibleAsync();
        await using var db = App.CreateDbContext(ownerId);
        (await db.QRCodes.SingleAsync()).Title.Should().Be("Tisch 1 (Terrasse)");
    }
}
