using System.Globalization;
using System.Reflection;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Api.IntegrationTests.Tests;

/// <summary>
/// <see cref="IMenuTranslationService"/> (shared core, used by the back office): the copy of a menu into another language
/// is complete (every scalar field of every copied entity — a new field that the copy forgets fails here) and shares
/// no rows; linking, unlinking and the one-language-per-group rule.
/// </summary>
public class MenuTranslationServiceTests : BaseIntegrationTest
{
    public MenuTranslationServiceTests(CustomWebApplicationFactory factory) : base(factory) { }

    private async Task<T> WithServiceAsync<T>(Func<IMenuTranslationService, ApplicationDbContext, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IMenuTranslationService>(),
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
    }

    private async Task<Menu> LoadAsync(Guid menuId) => await WithServiceAsync((_, db) => db.Menus.AsNoTracking()
        .Include(m => m.Categories!).ThenInclude(c => c.MenuItems!).ThenInclude(i => i.PriceOptions)
        .Include(m => m.Categories!).ThenInclude(c => c.MenuItems!).ThenInclude(i => i.Allergens)
        .Include(m => m.Allergens)
        .AsSplitQuery()
        .SingleAsync(m => m.Id == menuId));

    /// <summary>Every public scalar property (value types, strings) of the entity type except the listed ones.</summary>
    private static IEnumerable<PropertyInfo> Scalars<T>(params string[] except) =>
        typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanWrite && (p.PropertyType.IsValueType || p.PropertyType == typeof(string)))
            .Where(p => !except.Contains(p.Name));

    private static void ShouldCopyScalars<T>(T copy, T original, params string[] except)
    {
        foreach (var property in Scalars<T>(except))
            property.GetValue(copy).Should().Be(property.GetValue(original), $"{typeof(T).Name}.{property.Name} is copied");
    }

    /// <summary>Lunch Menu with every label, a photo, variants and extras on its first item, plus a combo.</summary>
    private async Task EnrichLunchMenuAsync()
    {
        await TestSeedData.SeedCombosAsync(Factory.Services);
        await WithServiceAsync(async (_, db) =>
        {
            var salad = await db.MenuItems.SingleAsync(i => i.Id == TestSeedData.MenuItem1Id);
            salad.IsVegan = salad.IsVegetarian = salad.IsSpicy = salad.IsHalal = salad.IsAlcoholFree = salad.IsNew = salad.IsPopular = true;
            salad.ImagePath = $"image/{TestSeedData.ImageId}";
            salad.SortOrder = 3;
            db.MenuItemPriceOptions.AddRange(
                new MenuItemPriceOption { Id = Guid.NewGuid(), MenuItemId = salad.Id, Kind = PriceOptionKind.Variant, Name = "Small", Price = 8.50m, SortOrder = 0 },
                new MenuItemPriceOption { Id = Guid.NewGuid(), MenuItemId = salad.Id, Kind = PriceOptionKind.Extra, Name = "+ Chicken", Price = 3m, SortOrder = 0 });
            var starters = await db.Categories.SingleAsync(c => c.Id == TestSeedData.Category1Id);
            starters.Description = "Light and fresh";
            starters.SortOrder = 2;
            var milk = await db.Allergens.SingleAsync(a => a.Id == TestSeedData.AllergenMilkId);
            milk.Name = "Milch / Laktose";   // the EU standard German name
            await db.SaveChangesAsync();
            return 0;
        });
    }

    [Fact]
    public async Task CreateTranslation_CopiesEveryFieldIntoNewRows_AndLinksBothMenus()
    {
        await EnrichLunchMenuAsync();

        var result = await WithServiceAsync((s, _) => s.CreateTranslationAsync(TestSeedData.Menu1Id, MenuLanguage.English));

        result.Succeeded.Should().BeTrue(result.Error);
        var original = await LoadAsync(TestSeedData.Menu1Id);
        var copy = await LoadAsync(result.MenuId!.Value);
        copy.Name.Should().Be("Lunch Menu (English)");
        copy.Language.Should().Be(MenuLanguage.English);
        copy.TranslationGroupId.Should().NotBeNull().And.Be(original.TranslationGroupId);
        ShouldCopyScalars(copy, original, nameof(Menu.Id), nameof(Menu.Name), nameof(Menu.Language));

        var category = copy.Categories!.Single();
        var originalCategory = original.Categories!.Single();
        category.Id.Should().NotBe(originalCategory.Id);
        ShouldCopyScalars(category, originalCategory, nameof(Category.Id), nameof(Category.MenuId));

        foreach (var originalItem in originalCategory.MenuItems!)
        {
            var item = category.MenuItems!.Single(i => i.Name == originalItem.Name);
            item.Id.Should().NotBe(originalItem.Id);
            ShouldCopyScalars(item, originalItem, nameof(MenuItem.Id), nameof(MenuItem.CategoryId));
            item.PriceOptions.Should().HaveCount(originalItem.PriceOptions.Count);
            foreach (var option in originalItem.PriceOptions)
            {
                var copied = item.PriceOptions.Single(o => o.Kind == option.Kind && o.Name == option.Name);
                copied.Id.Should().NotBe(option.Id);
                ShouldCopyScalars(copied, option, nameof(MenuItemPriceOption.Id), nameof(MenuItemPriceOption.MenuItemId));
            }
            item.Allergens.Select(a => a.Code).Should().BeEquivalentTo(originalItem.Allergens.Select(a => a.Code));
            item.Allergens.Should().OnlyContain(a => a.MenuId == copy.Id);
        }

        // EU standard names in the target language, the owner's own names unchanged
        copy.Allergens!.Select(a => (a.Code, a.Name)).Should().BeEquivalentTo(new[] { ("A", "Gluten"), ("G", "Milk") });
        foreach (var allergen in copy.Allergens!)
            ShouldCopyScalars(allergen, original.Allergens!.Single(a => a.Code == allergen.Code),
                nameof(Allergen.Id), nameof(Allergen.MenuId), nameof(Allergen.Name));

        await WithServiceAsync(async (_, db) =>
        {
            var combo = await db.MenuCombos.AsNoTracking().Include(c => c.Items).SingleAsync(c => c.MenuId == copy.Id);
            var originalCombo = await db.MenuCombos.AsNoTracking().Include(c => c.Items).SingleAsync(c => c.Id == TestSeedData.LunchComboId);
            ShouldCopyScalars(combo, originalCombo, nameof(MenuCombo.Id), nameof(MenuCombo.MenuId), nameof(MenuCombo.TriggerMenuItemId));
            combo.TriggerMenuItemId.Should().Be(category.MenuItems!.Single(i => i.Name == "Caesar Salad").Id);
            combo.Items.Select(i => i.MenuItemId).Should().Equal(category.MenuItems!.Single(i => i.Name == "Tomato Soup").Id);
            (await db.QRCodes.CountAsync(q => q.MenuId == copy.Id)).Should().Be(0, "the copy gets no QR code");
            return 0;
        });
    }

    [Fact]
    public async Task CreateTranslation_ToGerman_UsesGermanEuNames_AndAppendsANumberWhenTheNameIsTaken()
    {
        // Dinner Menu (English) has "D" = "Fish", the EU standard name
        await WithServiceAsync(async (_, db) =>
        {
            db.Menus.Add(new Menu { Id = Guid.NewGuid(), Name = "Dinner Menu (German)", Description = "", Language = MenuLanguage.German });
            await db.SaveChangesAsync();
            return 0;
        });

        var result = await WithServiceAsync((s, _) => s.CreateTranslationAsync(TestSeedData.Menu2Id, MenuLanguage.German));

        result.Succeeded.Should().BeTrue(result.Error);
        var copy = await LoadAsync(result.MenuId!.Value);
        copy.Name.Should().Be("Dinner Menu (German) 2");
        copy.Allergens!.Single().Name.Should().Be("Fisch");
    }

    [Fact]
    public async Task CreateTranslation_RefusesTheMenusOwnLanguage_AndALanguageTheGroupHas()
    {
        var same = await WithServiceAsync((s, _) => s.CreateTranslationAsync(TestSeedData.Menu1Id, MenuLanguage.German));
        same.Succeeded.Should().BeFalse();

        var first = await WithServiceAsync((s, _) => s.CreateTranslationAsync(TestSeedData.Menu1Id, MenuLanguage.French));
        first.Succeeded.Should().BeTrue(first.Error);
        var again = await WithServiceAsync((s, _) => s.CreateTranslationAsync(TestSeedData.Menu1Id, MenuLanguage.French));
        again.Succeeded.Should().BeFalse();
        again.Error.Should().Contain("Lunch Menu (French)");

        var versions = await WithServiceAsync((s, _) => s.GetVersionsAsync(first.MenuId!.Value));
        versions.Select(v => v.Language).Should().Equal(MenuLanguage.German, MenuLanguage.French);
    }

    [Fact]
    public async Task DeletingTheCopy_LeavesTheOriginalAndItsPhotos()
    {
        await EnrichLunchMenuAsync();
        var result = await WithServiceAsync((s, _) => s.CreateTranslationAsync(TestSeedData.Menu1Id, MenuLanguage.English));

        // The API delete does not remove combos (the back office does): drop the copy's combo first
        await WithServiceAsync(async (_, db) =>
        {
            await db.MenuCombos.Where(c => c.MenuId == result.MenuId).ExecuteDeleteAsync();
            return 0;
        });
        var response = await Client.DeleteAsync($"/api/menus/{result.MenuId}");
        response.IsSuccessStatusCode.Should().BeTrue();

        var original = await LoadAsync(TestSeedData.Menu1Id);
        original.TranslationGroupId.Should().BeNull("a group of one is dissolved");
        original.Categories!.Single().MenuItems.Should().HaveCount(2);
        original.Allergens.Should().HaveCount(2);
        await WithServiceAsync(async (_, db) =>
        {
            (await db.Images.AnyAsync(i => i.Id == TestSeedData.ImageId)).Should().BeTrue("photos are shared read-only");
            return 0;
        });
    }

    [Fact]
    public async Task Link_Unlink_AndCandidates()
    {
        // The service's messages follow the current culture (AP12; the API's is the server's): this test reads the
        // English ones, so it pins them instead of taking the host's (a German Windows would answer in German)
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");

        // Menu1 German, Menu2 English, plus another English and a French menu
        Guid otherEnglish = Guid.NewGuid(), french = Guid.NewGuid();
        await WithServiceAsync(async (_, db) =>
        {
            db.Menus.Add(new Menu { Id = otherEnglish, Name = "Brunch", Description = "", Language = MenuLanguage.English });
            db.Menus.Add(new Menu { Id = french, Name = "Carte", Description = "", Language = MenuLanguage.French });
            await db.SaveChangesAsync();
            return 0;
        });

        var candidates = await WithServiceAsync((s, _) => s.GetLinkCandidatesAsync(TestSeedData.Menu1Id));
        candidates.Select(c => c.Name).Should().Equal("Brunch", "Carte", "Dinner Menu");

        (await WithServiceAsync((s, _) => s.LinkAsync(TestSeedData.Menu1Id, TestSeedData.Menu2Id))).Succeeded.Should().BeTrue();
        candidates = await WithServiceAsync((s, _) => s.GetLinkCandidatesAsync(TestSeedData.Menu1Id));
        candidates.Select(c => c.Name).Should().Equal("Carte");

        // English is taken in the group; a menu of another group cannot join; the own language cannot either
        (await WithServiceAsync((s, _) => s.LinkAsync(TestSeedData.Menu1Id, otherEnglish))).Error.Should().Contain("Dinner Menu");
        (await WithServiceAsync((s, _) => s.ValidateLanguageAsync(TestSeedData.Menu1Id, MenuLanguage.English))).Should().Contain("Dinner Menu");
        (await WithServiceAsync((s, _) => s.ValidateLanguageAsync(TestSeedData.Menu1Id, MenuLanguage.Spanish))).Should().BeNull();
        (await WithServiceAsync((s, _) => s.LinkAsync(french, TestSeedData.Menu2Id))).Error.Should().Contain("linked to other menus");

        (await WithServiceAsync((s, _) => s.LinkAsync(TestSeedData.Menu2Id, french))).Succeeded.Should().BeTrue();
        var versions = await WithServiceAsync((s, _) => s.GetVersionsAsync(french));
        versions.Select(v => v.Name).Should().Equal("Lunch Menu", "Dinner Menu", "Carte");

        // Unlinking one of three keeps the other two; unlinking one of two dissolves the group
        (await WithServiceAsync((s, _) => s.UnlinkAsync(TestSeedData.Menu2Id))).Succeeded.Should().BeTrue();
        (await WithServiceAsync((s, _) => s.GetVersionsAsync(TestSeedData.Menu1Id))).Select(v => v.Name).Should().Equal("Lunch Menu", "Carte");
        (await WithServiceAsync((s, _) => s.UnlinkAsync(french))).Succeeded.Should().BeTrue();
        (await LoadAsync(TestSeedData.Menu1Id)).TranslationGroupId.Should().BeNull();
        (await LoadAsync(french)).TranslationGroupId.Should().BeNull();
    }

    [Fact]
    public async Task TheDatabase_AllowsOneMenuPerLanguageInAGroup()
    {
        var group = Guid.NewGuid();
        var act = () => WithServiceAsync(async (_, db) =>
        {
            db.Menus.Add(new Menu { Id = Guid.NewGuid(), Name = "A", Description = "", Language = MenuLanguage.English, TranslationGroupId = group });
            db.Menus.Add(new Menu { Id = Guid.NewGuid(), Name = "B", Description = "", Language = MenuLanguage.English, TranslationGroupId = group });
            return await db.SaveChangesAsync();
        });

        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
