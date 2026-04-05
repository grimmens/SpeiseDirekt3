using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class MenuItemsController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public MenuItemsController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<MenuItem>>> GetAll([FromQuery] Guid? categoryId)
    {
        var query = _db.MenuItems.AsQueryable();

        if (categoryId.HasValue)
            query = query.Where(mi => mi.CategoryId == categoryId.Value);

        var menuItems = await query.ToListAsync();
        return Ok(menuItems);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MenuItem>> Get(Guid id)
    {
        var menuItem = await _db.MenuItems
            .Include(mi => mi.Category)
            .FirstOrDefaultAsync(mi => mi.Id == id);

        if (menuItem is null)
            return NotFound();

        return Ok(menuItem);
    }

    [HttpPost]
    public async Task<ActionResult<MenuItem>> Create(MenuItemDto dto)
    {
        var categoryExists = await _db.Categories.AnyAsync(c => c.Id == dto.CategoryId);
        if (!categoryExists)
            return BadRequest("The specified CategoryId does not reference an existing category.");

        var menuItem = new MenuItem
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Description = dto.Description,
            Allergens = dto.Allergens,
            Price = dto.Price,
            CategoryId = dto.CategoryId
        };

        _db.MenuItems.Add(menuItem);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = menuItem.Id }, menuItem);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, MenuItemDto dto)
    {
        var menuItem = await _db.MenuItems.FindAsync(id);
        if (menuItem is null)
            return NotFound();

        var categoryExists = await _db.Categories.AnyAsync(c => c.Id == dto.CategoryId);
        if (!categoryExists)
            return BadRequest("The specified CategoryId does not reference an existing category.");

        menuItem.Name = dto.Name;
        menuItem.Description = dto.Description;
        menuItem.Allergens = dto.Allergens;
        menuItem.Price = dto.Price;
        menuItem.CategoryId = dto.CategoryId;

        await _db.SaveChangesAsync();

        return Ok(menuItem);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var menuItem = await _db.MenuItems.FindAsync(id);
        if (menuItem is null)
            return NotFound();

        _db.MenuItems.Remove(menuItem);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
