using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation;

/// <inheritdoc cref="IMenuTranslationService"/>
/// <remarks>
/// Every query goes through the tenant query filters, so only the current tenant's menus are seen or changed.
/// Photos: the copy points to the same <see cref="Image"/> rows. Image rows are never changed, and deleting an item or
/// a menu does not delete them (replacing a photo uploads a new image), so the versions stay independent.
/// </remarks>
public class MenuTranslationService : IMenuTranslationService
{
    private readonly ApplicationDbContext db;

    public MenuTranslationService(ApplicationDbContext db) => this.db = db;

    public async Task<IReadOnlyList<MenuVersion>> GetVersionsAsync(Guid menuId)
    {
        var menu = await db.Menus.AsNoTracking()
            .Where(m => m.Id == menuId)
            .Select(m => new { m.Id, m.Name, m.Language, m.TranslationGroupId })
            .FirstOrDefaultAsync();
        if (menu is null)
            return Array.Empty<MenuVersion>();
        if (menu.TranslationGroupId is not { } groupId)
            return new[] { new MenuVersion(menu.Id, menu.Name, menu.Language) };

        var versions = await db.Menus.AsNoTracking()
            .Where(m => m.TranslationGroupId == groupId)
            .Select(m => new MenuVersion(m.Id, m.Name, m.Language))
            .ToListAsync();
        return versions.InLanguageOrder(v => v.Language).ToList();
    }

    public async Task<IReadOnlyList<MenuVersion>> GetLinkCandidatesAsync(Guid menuId)
    {
        var versions = await GetVersionsAsync(menuId);
        if (versions.Count == 0)
            return Array.Empty<MenuVersion>();

        var taken = versions.Select(v => v.Language).ToHashSet();
        var groupIds = versions.Select(v => v.Id).ToList();
        var others = await db.Menus.AsNoTracking()
            .Where(m => !groupIds.Contains(m.Id))
            .Select(m => new { m.Id, m.Name, m.Language, m.TranslationGroupId })
            .ToListAsync();
        // A group id shared with nobody else (left over) does not count as linked
        var groupSizes = others.Where(m => m.TranslationGroupId is not null)
            .GroupBy(m => m.TranslationGroupId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        return others
            .Where(m => !taken.Contains(m.Language)
                        && (m.TranslationGroupId is not { } g || groupSizes[g] == 1))
            .OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(m => new MenuVersion(m.Id, m.Name, m.Language))
            .ToList();
    }

    public async Task<MenuTranslationResult> CreateTranslationAsync(Guid menuId, MenuLanguage language)
    {
        var source = await db.Menus.AsNoTracking()
            .Include(m => m.Categories!).ThenInclude(c => c.MenuItems!).ThenInclude(i => i.Allergens)
            .Include(m => m.Categories!).ThenInclude(c => c.MenuItems!).ThenInclude(i => i.PriceOptions)
            .Include(m => m.Allergens)
            .AsSplitQuery()
            .FirstOrDefaultAsync(m => m.Id == menuId);
        if (source is null)
            return MenuTranslationResult.Fail(ModelText.Menu_NotFound);

        var target = MenuLanguages.Of(language);
        if (source.Language == language)
            return MenuTranslationResult.Fail(Text(ModelText.Translation_AlreadyInLanguage, target.DisplayName));
        if (await TakenByAsync(source.TranslationGroupId, language, exceptMenuId: menuId) is { } taken)
            return MenuTranslationResult.Fail(Text(ModelText.Translation_VersionExists, target.DisplayName, taken));

        var combos = await db.MenuCombos.AsNoTracking()
            .Include(c => c.Items)
            .Where(c => c.MenuId == menuId)
            .ToListAsync();

        var groupId = source.TranslationGroupId ?? Guid.NewGuid();
        if (source.TranslationGroupId is null)
        {
            var original = await db.Menus.FirstAsync(m => m.Id == menuId);
            original.TranslationGroupId = groupId;
        }

        var copy = new Menu
        {
            Id = Guid.NewGuid(),
            Name = await UniqueNameAsync(CopyName(source.Name, target)),
            Description = source.Description,
            Theme = source.Theme,
            Language = language,
            TranslationGroupId = groupId,
            Categories = new List<Category>()
        };

        var allergens = new Dictionary<Guid, Allergen>();
        foreach (var allergen in source.Allergens ?? Enumerable.Empty<Allergen>())
        {
            var allergenCopy = new Allergen
            {
                Id = Guid.NewGuid(),
                Code = allergen.Code,
                Name = EuAllergens.NameIn(language, allergen.Code, allergen.Name),
                MenuId = copy.Id
            };
            allergens[allergen.Id] = allergenCopy;
            db.Allergens.Add(allergenCopy);
        }

        var items = new Dictionary<Guid, Guid>();
        foreach (var category in source.Categories ?? Enumerable.Empty<Category>())
        {
            var categoryCopy = new Category
            {
                Id = Guid.NewGuid(),
                Name = category.Name,
                Description = category.Description,
                SortOrder = category.SortOrder,
                MenuId = copy.Id,
                MenuItems = new List<MenuItem>()
            };
            foreach (var item in category.MenuItems ?? Enumerable.Empty<MenuItem>())
            {
                var itemCopy = CopyItem(item, categoryCopy.Id);
                foreach (var allergen in item.Allergens)
                {
                    if (allergens.TryGetValue(allergen.Id, out var allergenCopy))
                        itemCopy.Allergens.Add(allergenCopy);
                }
                categoryCopy.MenuItems.Add(itemCopy);
                items[item.Id] = itemCopy.Id;
            }
            copy.Categories.Add(categoryCopy);
        }
        db.Menus.Add(copy);

        foreach (var combo in combos)
        {
            if (!items.TryGetValue(combo.TriggerMenuItemId, out var trigger))
                continue;
            var comboCopy = new MenuCombo
            {
                Id = Guid.NewGuid(),
                Name = combo.Name,
                Description = combo.Description,
                ComboPrice = combo.ComboPrice,
                MenuId = copy.Id,
                TriggerMenuItemId = trigger
            };
            foreach (var included in combo.Items)
            {
                if (items.TryGetValue(included.MenuItemId, out var includedCopy))
                    comboCopy.Items.Add(new MenuComboItem { Id = Guid.NewGuid(), MenuItemId = includedCopy });
            }
            db.MenuCombos.Add(comboCopy);
        }

        // One SaveChanges = one transaction: the copy and the original's group id, or nothing
        await db.SaveChangesAsync();
        return MenuTranslationResult.Ok(copy.Id);
    }

    public async Task<MenuTranslationResult> LinkAsync(Guid menuId, Guid otherMenuId)
    {
        if (menuId == otherMenuId)
            return MenuTranslationResult.Fail(ModelText.Translation_LinkSelf);

        var menu = await db.Menus.FirstOrDefaultAsync(m => m.Id == menuId);
        var other = await db.Menus.FirstOrDefaultAsync(m => m.Id == otherMenuId);
        if (menu is null || other is null)
            return MenuTranslationResult.Fail(ModelText.Menu_NotFound);

        if (menu.TranslationGroupId is not null && other.TranslationGroupId == menu.TranslationGroupId)
            return MenuTranslationResult.Fail(Text(ModelText.Translation_AlreadyVersion, other.Name));
        if (other.TranslationGroupId is { } otherGroup
            && await db.Menus.AnyAsync(m => m.TranslationGroupId == otherGroup && m.Id != other.Id))
            return MenuTranslationResult.Fail(Text(ModelText.Translation_LinkedElsewhere, other.Name));

        var language = MenuLanguages.Of(other.Language);
        if (menu.Language == other.Language)
            return MenuTranslationResult.Fail(Text(ModelText.Translation_SameLanguage, other.Name, language.DisplayName));
        if (await TakenByAsync(menu.TranslationGroupId, other.Language, exceptMenuId: other.Id) is { } taken)
            return MenuTranslationResult.Fail(Text(ModelText.Translation_VersionExists, language.DisplayName, taken));

        var groupId = menu.TranslationGroupId ?? Guid.NewGuid();
        menu.TranslationGroupId = groupId;
        other.TranslationGroupId = groupId;
        await db.SaveChangesAsync();
        return MenuTranslationResult.Ok(other.Id);
    }

    public async Task<MenuTranslationResult> UnlinkAsync(Guid menuId)
    {
        var menu = await db.Menus.FirstOrDefaultAsync(m => m.Id == menuId);
        if (menu is null)
            return MenuTranslationResult.Fail(ModelText.Menu_NotFound);
        if (menu.TranslationGroupId is not { } groupId)
            return MenuTranslationResult.Ok(menuId);

        menu.TranslationGroupId = null;
        var rest = await db.Menus.Where(m => m.TranslationGroupId == groupId && m.Id != menuId).ToListAsync();
        if (rest.Count == 1)
            rest[0].TranslationGroupId = null;   // a group of one is no group
        await db.SaveChangesAsync();
        return MenuTranslationResult.Ok(menuId);
    }

    public async Task<string?> ValidateLanguageAsync(Guid menuId, MenuLanguage language)
    {
        var groupId = await db.Menus.Where(m => m.Id == menuId).Select(m => m.TranslationGroupId).FirstOrDefaultAsync();
        return await TakenByAsync(groupId, language, exceptMenuId: menuId) is { } taken
            ? Text(ModelText.Translation_LanguageTaken, MenuLanguages.Of(language).DisplayName, taken)
            : null;
    }

    /// <summary>A message of <see cref="ModelText"/> in the current UI language (AP12; the API's is the server's, English).</summary>
    private static string Text(string format, params object[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    /// <summary>
    /// Before a menu of <paramref name="groupId"/> is deleted (same transaction): when only one other menu stays in the
    /// group, it is unlinked too. The other versions themselves are never touched otherwise.
    /// </summary>
    public static async Task LeaveGroupOnDeleteAsync(ApplicationDbContext db, Guid menuId, Guid? groupId)
    {
        if (groupId is null)
            return;
        var rest = await db.Menus.Where(m => m.TranslationGroupId == groupId && m.Id != menuId).Select(m => m.Id).ToListAsync();
        if (rest.Count == 1)
            await db.Menus.Where(m => m.Id == rest[0]).ExecuteUpdateAsync(s => s.SetProperty(m => m.TranslationGroupId, (Guid?)null));
    }

    /// <summary>Name of the menu in <paramref name="groupId"/> that has <paramref name="language"/>, if any.</summary>
    private async Task<string?> TakenByAsync(Guid? groupId, MenuLanguage language, Guid exceptMenuId)
    {
        if (groupId is null)
            return null;
        return await db.Menus
            .Where(m => m.TranslationGroupId == groupId && m.Language == language && m.Id != exceptMenuId)
            .Select(m => m.Name)
            .FirstOrDefaultAsync();
    }

    /// <summary>"Trattoria Lago" → "Trattoria Lago (English)"; a language suffix of the source is replaced, not stacked.</summary>
    private static string CopyName(string name, MenuLanguageInfo target)
    {
        var baseName = name.Trim();
        foreach (var language in MenuLanguages.All)
        {
            var suffix = $" ({language.EnglishName})";
            if (baseName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && baseName.Length > suffix.Length)
            {
                baseName = baseName[..^suffix.Length].TrimEnd();
                break;
            }
        }
        return $"{baseName} ({target.EnglishName})";
    }

    /// <summary>Menu names are unique per tenant (menu editor): "X (English)", else "X (English) 2", …</summary>
    private async Task<string> UniqueNameAsync(string name)
    {
        var names = (await db.Menus.Select(m => m.Name).ToListAsync()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(name))
            return name;
        for (var n = 2; ; n++)
        {
            var candidate = $"{name} {n}";
            if (!names.Contains(candidate))
                return candidate;
        }
    }

    /// <summary>Every field of the item, its labels, price options and the photo reference; new ids.</summary>
    private static MenuItem CopyItem(MenuItem item, Guid categoryId)
    {
        var copy = new MenuItem
        {
            Id = Guid.NewGuid(),
            CategoryId = categoryId,
            Name = item.Name,
            Description = item.Description,
            SortOrder = item.SortOrder,
            Price = item.Price,
            ImagePath = item.ImagePath,
            IsVegan = item.IsVegan,
            IsVegetarian = item.IsVegetarian,
            IsSpicy = item.IsSpicy,
            IsHalal = item.IsHalal,
            IsAlcoholFree = item.IsAlcoholFree,
            IsNew = item.IsNew,
            IsPopular = item.IsPopular
        };
        foreach (var option in item.PriceOptions)
        {
            copy.PriceOptions.Add(new MenuItemPriceOption
            {
                Id = Guid.NewGuid(),
                Kind = option.Kind,
                Name = option.Name,
                Price = option.Price,
                SortOrder = option.SortOrder
            });
        }
        return copy;
    }
}
