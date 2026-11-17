using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Data;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt.Repository;

namespace SpeiseDirekt.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class MenuItemsController : ControllerBase
{
    private readonly IMenuItemRepository _menuItemRepository;
    private readonly ApplicationDbContext _db;

    public MenuItemsController(IMenuItemRepository menuItemRepository, ApplicationDbContext db)
    {
        _menuItemRepository = menuItemRepository;
        _db = db;
    }

    [HttpGet]
    [Authorize(Policy = PolicyNames.CanViewMenuItems)]
    public async Task<ActionResult<List<MenuItem>>> GetAll([FromQuery] Guid? categoryId)
    {
        var menuItems = await _menuItemRepository.GetAllAsync(categoryId);
        return Ok(menuItems);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanViewMenuItems)]
    public async Task<ActionResult<MenuItem>> Get(Guid id)
    {
        var menuItem = await _menuItemRepository.GetByIdAsync(id);

        if (menuItem is null)
            return NotFound();

        return Ok(menuItem);
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.CanCreateMenuItems)]
    public async Task<ActionResult<MenuItem>> Create(MenuItemDto dto)
    {
        var categoryExists = await _menuItemRepository.CategoryExistsAsync(dto.CategoryId);
        if (!categoryExists)
            return BadRequest("The specified CategoryId does not reference an existing category.");

        var allergens = await _db.Allergens
            .Where(a => dto.AllergenIds.Contains(a.Id))
            .ToListAsync();

        var menuItem = new MenuItem
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Description = dto.Description ?? string.Empty,
            Allergens = allergens,
            Price = dto.Price,
            CategoryId = dto.CategoryId,
            IsVegan = dto.IsVegan,
            IsVegetarian = dto.IsVegetarian,
            IsSpicy = dto.IsSpicy,
            IsHalal = dto.IsHalal,
            IsAlcoholFree = dto.IsAlcoholFree,
            IsNew = dto.IsNew,
            IsPopular = dto.IsPopular,
            SortOrder = dto.SortOrder ?? await _menuItemRepository.NextSortOrderAsync(dto.CategoryId)
        };
        if (dto.PriceOptions is { } options)
            ReplacePriceOptions(menuItem, options);
        menuItem.ApplyLowestVariantPrice();

        await _menuItemRepository.CreateAsync(menuItem);
        menuItem.PriceOptions = menuItem.PriceOptions
            .OrderBy(o => o.Kind).ThenBy(o => o.SortOrder).ThenBy(o => o.Name).ToList();

        return CreatedAtAction(nameof(Get), new { id = menuItem.Id }, menuItem);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanEditMenuItems)]
    public async Task<IActionResult> Update(Guid id, MenuItemDto dto)
    {
        var categoryExists = await _menuItemRepository.CategoryExistsAsync(dto.CategoryId);
        if (!categoryExists)
            return BadRequest("The specified CategoryId does not reference an existing category.");

        var allergens = await _db.Allergens
            .Where(a => dto.AllergenIds.Contains(a.Id))
            .ToListAsync();

        var menuItem = await _menuItemRepository.UpdateAsync(id, mi =>
        {
            mi.Name = dto.Name;
            mi.Description = dto.Description ?? string.Empty;
            mi.Allergens.Clear();
            foreach (var allergen in allergens)
                mi.Allergens.Add(allergen);
            mi.Price = dto.Price;
            mi.CategoryId = dto.CategoryId;
            mi.IsVegan = dto.IsVegan;
            mi.IsVegetarian = dto.IsVegetarian;
            mi.IsSpicy = dto.IsSpicy;
            mi.IsHalal = dto.IsHalal;
            mi.IsAlcoholFree = dto.IsAlcoholFree;
            mi.IsNew = dto.IsNew;
            mi.IsPopular = dto.IsPopular;
            if (dto.SortOrder is { } sortOrder)
                mi.SortOrder = sortOrder;
            // Omitted options are kept; the price rule applies to the kept ones too
            if (dto.PriceOptions is { } options)
                ReplacePriceOptions(mi, options);
            mi.ApplyLowestVariantPrice();
        });

        if (menuItem is null)
            return NotFound();

        return Ok(menuItem);
    }

    /// <summary>
    /// Replaces the item's variants and extras with <paramref name="options"/> (removed ones are deleted as orphans).
    /// Without a <c>SortOrder</c> an option keeps its position in the list among the options of its kind.
    /// </summary>
    private static void ReplacePriceOptions(MenuItem item, IReadOnlyList<MenuItemPriceOptionDto> options)
    {
        item.PriceOptions.Clear();
        foreach (var group in options.GroupBy(o => o.Kind))
        {
            var position = 0;
            foreach (var option in group)
            {
                // Id left empty: EF generates it and tracks the option as new
                item.PriceOptions.Add(new MenuItemPriceOption
                {
                    Kind = option.Kind,
                    Name = option.Name.Trim(),
                    Price = option.Price,
                    SortOrder = option.SortOrder ?? position
                });
                position++;
            }
        }
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanDeleteMenuItems)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _menuItemRepository.DeleteAsync(id);
        if (!deleted)
            return NotFound();

        return NoContent();
    }
}
