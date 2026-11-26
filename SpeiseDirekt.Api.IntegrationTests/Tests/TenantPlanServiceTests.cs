using System.Globalization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SpeiseDirekt.Data;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceImplementation;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

/// <summary>
/// <see cref="ITenantPlanService"/>, the only check of the plans: paid = the owner has the role <c>PaidTenant</c>; a
/// free tenant may create one menu and one QR code (Plans:Free). Counts across the tenant filters, owner ids compared as
/// GUIDs, a language version is a menu. The seeded test tenant is paid.
/// </summary>
public class TenantPlanServiceTests : BaseIntegrationTest
{
    private const string TestUserId = CustomWebApplicationFactory.TestUserId;

    public TenantPlanServiceTests(CustomWebApplicationFactory factory) : base(factory) { }

    /// <summary>The service as the app resolves it; its scope acts as the seeded tenant (query filters for the test user).</summary>
    private async Task<T> WithPlansAsync<T>(Func<ITenantPlanService, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<ITenantPlanService>());
    }

    private Task<PlanUsage> UsageAsync(string ownerId) => WithPlansAsync(p => p.GetUsageAsync(ownerId));

    [Fact]
    public async Task TheSeededTenant_IsPaid_AndHasNoLimit()
    {
        (await WithPlansAsync(p => p.IsPaidAsync(TestUserId))).Should().BeTrue();

        var usage = await UsageAsync(TestUserId);
        usage.Should().Be(new PlanUsage(true, 2, 2, null, null));
        usage.CanCreateMenu.Should().BeTrue();
        usage.CanCreateQrCode.Should().BeTrue();
        (await WithPlansAsync(p => p.CanCreateMenuAsync(TestUserId))).Should().BeTrue();
        (await WithPlansAsync(p => p.CanCreateQrCodeAsync(TestUserId))).Should().BeTrue();
    }

    [Fact]
    public async Task AFreeTenant_MayCreateOneMenuAndOneQrCode()
    {
        var owner = await TestSeedData.SeedOwnerAsync(Factory.Services, "free@test.com");

        (await WithPlansAsync(p => p.IsPaidAsync(owner))).Should().BeFalse();
        var empty = await UsageAsync(owner);
        empty.Should().Be(new PlanUsage(false, 0, 0, 1, 1));
        empty.CanCreateMenu.Should().BeTrue();
        empty.CanCreateQrCode.Should().BeTrue();

        await TestSeedData.SeedContentAsync(Factory.Services, owner, menus: 1, qrCodes: 1);

        var full = await UsageAsync(owner);
        full.Should().Be(new PlanUsage(false, 1, 1, 1, 1));
        full.CanCreateMenu.Should().BeFalse();
        full.CanCreateQrCode.Should().BeFalse();
        (await WithPlansAsync(p => p.CanCreateMenuAsync(owner))).Should().BeFalse();
        (await WithPlansAsync(p => p.CanCreateQrCodeAsync(owner))).Should().BeFalse();
    }

    [Fact]
    public async Task OneMenuButNoQrCode_LeavesTheQrCodeOpen()
    {
        var owner = await TestSeedData.SeedOwnerAsync(Factory.Services, "menu-only@test.com");
        await TestSeedData.SeedContentAsync(Factory.Services, owner, menus: 1, qrCodes: 0);

        (await WithPlansAsync(p => p.CanCreateMenuAsync(owner))).Should().BeFalse();
        (await WithPlansAsync(p => p.CanCreateQrCodeAsync(owner))).Should().BeTrue();
    }

    [Fact]
    public async Task TheRole_IsReadAtEveryCall_TakingItAwayBlocks_AndTheContentStays()
    {
        await TestSeedData.SetPaidAsync(Factory.Services, TestUserId, paid: false);

        var free = await UsageAsync(TestUserId);
        free.Should().Be(new PlanUsage(false, 2, 2, 1, 1), "the tenant keeps everything above the limit");
        free.CanCreateMenu.Should().BeFalse();
        free.CanCreateQrCode.Should().BeFalse();

        await TestSeedData.SetPaidAsync(Factory.Services, TestUserId, paid: true);
        (await WithPlansAsync(p => p.CanCreateMenuAsync(TestUserId))).Should().BeTrue();
    }

    [Fact]
    public async Task TheCounts_IgnoreTheTenantFilters_AndCountOnlyTheOwnersRows()
    {
        // The service's scope acts as the test tenant, whose query filters would hide the other tenant's rows
        var other = await TestSeedData.SeedOwnerAsync(Factory.Services, "other@test.com");
        await TestSeedData.SeedContentAsync(Factory.Services, other, menus: 3, qrCodes: 1);

        (await UsageAsync(other)).Should().Be(new PlanUsage(false, 3, 1, 1, 1));
        (await UsageAsync(TestUserId)).Should().Be(new PlanUsage(true, 2, 2, null, null));
    }

    /// <summary>
    /// <c>Menus.ApplicationUserId</c> is a GUID column; SQLite keeps it as upper-case text while the Identity id is lower
    /// case, so comparing the text would never match (the old PaidTenantHandler bug). The service compares GUIDs, and an
    /// upper-case argument works as well.
    /// </summary>
    [Fact]
    public async Task TheOwnerId_IsComparedAsAGuid_ThoughTheStoredTextIsUpperCase()
    {
        const string ownerId = "abcdef12-3456-4890-abcd-ef1234567890";
        await TestSeedData.SeedOwnerAsync(Factory.Services, "hex@test.com", paid: true, id: ownerId);
        var (menus, _) = await TestSeedData.SeedContentAsync(Factory.Services, ownerId, menus: 1, qrCodes: 1);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var name = (await db.Menus.IgnoreQueryFilters().SingleAsync(m => m.Id == menus[0])).Name;
            var stored = await db.Database
                .SqlQuery<string>($"SELECT \"ApplicationUserId\" AS \"Value\" FROM \"Menus\" WHERE \"Name\" = {name}")
                .SingleAsync();
            stored.Should().Be(ownerId.ToUpperInvariant()).And.NotBe(ownerId, "a text comparison would miss it");
        }

        (await UsageAsync(ownerId)).Should().Be(new PlanUsage(true, 1, 1, null, null));
        (await UsageAsync(ownerId.ToUpperInvariant())).Should().Be(new PlanUsage(true, 1, 1, null, null));

        await TestSeedData.SetPaidAsync(Factory.Services, ownerId, paid: false);
        (await UsageAsync(ownerId.ToUpperInvariant())).Should().Be(new PlanUsage(false, 1, 1, 1, 1));
        (await WithPlansAsync(p => p.CanCreateMenuAsync(ownerId.ToUpperInvariant()))).Should().BeFalse();
    }

    [Fact]
    public async Task ALanguageVersion_CountsAsAMenu_AndIsRefusedAtTheLimit()
    {
        // The service's messages follow the current culture (AP12): pin English, the host may be German
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");

        using (var scope = Factory.Services.CreateScope())
        {
            var copy = await scope.ServiceProvider.GetRequiredService<IMenuTranslationService>()
                .CreateTranslationAsync(TestSeedData.Menu1Id, MenuLanguage.English);
            copy.Succeeded.Should().BeTrue(copy.Error);
        }
        (await UsageAsync(TestUserId)).MenuCount.Should().Be(3, "the language version is a menu of its own");

        await TestSeedData.SetPaidAsync(Factory.Services, TestUserId, paid: false);
        using (var scope = Factory.Services.CreateScope())
        {
            var refused = await scope.ServiceProvider.GetRequiredService<IMenuTranslationService>()
                .CreateTranslationAsync(TestSeedData.Menu1Id, MenuLanguage.French);
            refused.Succeeded.Should().BeFalse();
            refused.Error.Should().Be("The free plan includes one menu. For more, please contact us.");
        }
        (await UsageAsync(TestUserId)).MenuCount.Should().Be(3, "nothing was copied");
    }

    [Fact]
    public void TheLimitMessage_HasAGermanVersion()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("de");
        try
        {
            ModelText.Plan_MenuLimitReached.Should()
                .Be("Im kostenlosen Zugang ist eine Speisekarte enthalten. Für mehr wenden Sie sich an uns.");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task PaidTenant_OnAMemberAccount_HasNoEffect()
    {
        var owner = await TestSeedData.SeedOwnerAsync(Factory.Services, "owner-of-member@test.com");
        var member = await TestSeedData.SeedMemberAsync(Factory.Services, owner, "member@test.com");
        await TestSeedData.SetPaidAsync(Factory.Services, member, paid: true);

        (await WithPlansAsync(p => p.IsPaidAsync(owner))).Should().BeFalse("the role belongs on the owner account");
        (await WithPlansAsync(p => p.IsPaidAsync(member))).Should().BeFalse("a member account is no tenant");
    }

    [Theory]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData("")]
    [InlineData("not-a-guid")]
    public async Task AnonymousOrInvalid_NeverPasses(string ownerId)
    {
        (await WithPlansAsync(p => p.IsPaidAsync(ownerId))).Should().BeFalse();
        (await WithPlansAsync(p => p.CanCreateMenuAsync(ownerId))).Should().BeFalse();
        (await WithPlansAsync(p => p.CanCreateQrCodeAsync(ownerId))).Should().BeFalse();
        var usage = await UsageAsync(ownerId);
        usage.IsPaid.Should().BeFalse();
        usage.CanCreateMenu.Should().BeFalse();
        usage.CanCreateQrCode.Should().BeFalse();
    }

    [Fact]
    public async Task TheFreeLimits_ComeFromPlansFree()
    {
        using var scope = Factory.Services.CreateScope();
        var configured = scope.ServiceProvider.GetRequiredService<IOptions<FreePlanOptions>>().Value;
        configured.MaxMenus.Should().Be(1);
        configured.MaxQrCodes.Should().Be(1);

        var owner = await TestSeedData.SeedOwnerAsync(Factory.Services, "bigger-free@test.com");
        await TestSeedData.SeedContentAsync(Factory.Services, owner, menus: 2, qrCodes: 2);
        var plans = new TenantPlanService(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(),
            Options.Create(new FreePlanOptions { MaxMenus = 3, MaxQrCodes = 2 }));

        (await plans.GetUsageAsync(owner)).Should().Be(new PlanUsage(false, 2, 2, 3, 2));
        (await plans.CanCreateMenuAsync(owner)).Should().BeTrue();
        (await plans.CanCreateQrCodeAsync(owner)).Should().BeFalse();
    }
}
