using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Data;
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
    public async Task<ActionResult<List<MenuItem>>> GetAll([FromQuery] Guid? categoryId)
    {
        var menuItems = await _menuItemRepository.GetAllAsync(categoryId);
        return Ok(menuItems);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MenuItem>> Get(Guid id)
    {
        var menuItem = await _menuItemRepository.GetByIdAsync(id);

        if (menuItem is null)
            return NotFound();

        return Ok(menuItem);
    }

    [HttpPost]
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
            Description = dto.Description,
            Allergens = allergens,
            Price = dto.Price,
            CategoryId = dto.CategoryId
        };

        await _menuItemRepository.CreateAsync(menuItem);

        return CreatedAtAction(nameof(Get), new { id = menuItem.Id }, menuItem);
    }

    [HttpPut("{id:guid}")]
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
            mi.Description = dto.Description;
            mi.Allergens.Clear();
            foreach (var allergen in allergens)
                mi.Allergens.Add(allergen);
            mi.Price = dto.Price;
            mi.CategoryId = dto.CategoryId;
        });

        if (menuItem is null)
            return NotFound();

        return Ok(menuItem);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _menuItemRepository.DeleteAsync(id);
        if (!deleted)
            return NotFound();

        return NoContent();
    }
}
