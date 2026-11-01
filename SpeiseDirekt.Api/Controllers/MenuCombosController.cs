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
public class MenuCombosController : ControllerBase
{
    private readonly IMenuComboRepository _comboRepository;
    private readonly ApplicationDbContext _db;

    public MenuCombosController(IMenuComboRepository comboRepository, ApplicationDbContext db)
    {
        _comboRepository = comboRepository;
        _db = db;
    }

    [HttpGet]
    [Authorize(Policy = PolicyNames.CanViewMenuCombos)]
    public async Task<ActionResult<List<MenuCombo>>> GetAll([FromQuery] Guid? menuId)
    {
        if (menuId is null)
            return BadRequest("menuId query parameter is required.");

        var combos = await _comboRepository.GetAllByMenuAsync(menuId.Value);
        return Ok(combos.OrderBy(c => c.Name).ToList());
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanViewMenuCombos)]
    public async Task<ActionResult<MenuCombo>> Get(Guid id)
    {
        var combo = await _comboRepository.GetByIdAsync(id);
        if (combo is null)
            return NotFound();

        return Ok(combo);
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.CanCreateMenuCombos)]
    public async Task<ActionResult<MenuCombo>> Create(MenuComboDto dto)
    {
        var menuExists = await _db.Menus.AnyAsync(m => m.Id == dto.MenuId);
        if (!menuExists)
            return BadRequest("The specified MenuId does not reference an existing menu.");

        var error = await ValidateAsync(dto, dto.MenuId);
        if (error is not null)
            return BadRequest(error);

        var combo = new MenuCombo
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Description = dto.Description ?? string.Empty,
            ComboPrice = dto.ComboPrice,
            TriggerMenuItemId = dto.TriggerMenuItemId!.Value,
            MenuId = dto.MenuId,
            Items = DistinctItemIds(dto).Select(itemId => new MenuComboItem { MenuItemId = itemId }).ToList()
        };

        await _comboRepository.CreateAsync(combo);

        var created = await _comboRepository.GetByIdAsync(combo.Id);
        return CreatedAtAction(nameof(Get), new { id = combo.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanEditMenuCombos)]
    public async Task<IActionResult> Update(Guid id, MenuComboDto dto)
    {
        var existing = await _comboRepository.GetByIdAsync(id);
        if (existing is null)
            return NotFound();

        // A combo stays in its menu; the items are validated against that menu.
        var error = await ValidateAsync(dto, existing.MenuId);
        if (error is not null)
            return BadRequest(error);

        var itemIds = DistinctItemIds(dto);
        await _comboRepository.UpdateAsync(id, combo =>
        {
            combo.Name = dto.Name;
            combo.Description = dto.Description ?? string.Empty;
            combo.ComboPrice = dto.ComboPrice;
            combo.TriggerMenuItemId = dto.TriggerMenuItemId!.Value;

            // Replace the included items (removed ones are deleted as orphans)
            foreach (var item in combo.Items.Where(i => !itemIds.Contains(i.MenuItemId)).ToList())
                combo.Items.Remove(item);
            foreach (var itemId in itemIds.Where(i => combo.Items.All(ci => ci.MenuItemId != i)))
                combo.Items.Add(new MenuComboItem { MenuItemId = itemId });
        });

        var updated = await _comboRepository.GetByIdAsync(id);
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanDeleteMenuCombos)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _comboRepository.DeleteAsync(id);
        if (!deleted)
            return NotFound();

        return NoContent();
    }

    private static List<Guid> DistinctItemIds(MenuComboDto dto) =>
        (dto.ItemIds ?? new List<Guid>()).Distinct().ToList();

    /// <summary>
    /// Mirrors the rules of <c>MenuComboEditor.razor</c> (name, trigger item, at least one included item)
    /// and additionally checks the price and that all items belong to the combo's menu.
    /// Returns an error message or <c>null</c> if the request is valid.
    /// </summary>
    private async Task<string?> ValidateAsync(MenuComboDto dto, Guid menuId)
    {
        if (string.IsNullOrWhiteSpace(dto.Name))
            return "Name is required.";

        if (dto.TriggerMenuItemId is null || dto.TriggerMenuItemId == Guid.Empty)
            return "A trigger item is required.";

        var itemIds = DistinctItemIds(dto);
        if (itemIds.Count == 0 || itemIds.Contains(Guid.Empty))
            return "Select at least one included item.";

        if (dto.ComboPrice < 0)
            return "ComboPrice must not be negative.";

        var triggerInMenu = await _db.MenuItems
            .AnyAsync(mi => mi.Id == dto.TriggerMenuItemId && mi.Category!.MenuId == menuId);
        if (!triggerInMenu)
            return "The trigger item does not belong to the combo's menu.";

        var itemsInMenu = await _db.MenuItems
            .CountAsync(mi => itemIds.Contains(mi.Id) && mi.Category!.MenuId == menuId);
        if (itemsInMenu != itemIds.Count)
            return "All included items must belong to the combo's menu.";

        return null;
    }
}
