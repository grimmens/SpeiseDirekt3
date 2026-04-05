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
public class CategoriesController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public CategoriesController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<Category>>> GetAll([FromQuery] Guid? menuId)
    {
        var query = _db.Categories.AsQueryable();

        if (menuId.HasValue)
            query = query.Where(c => c.MenuId == menuId.Value);

        var categories = await query.ToListAsync();
        return Ok(categories);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Category>> Get(Guid id)
    {
        var category = await _db.Categories
            .Include(c => c.MenuItems!)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category is null)
            return NotFound();

        return Ok(category);
    }

    [HttpPost]
    public async Task<ActionResult<Category>> Create(CategoryDto dto)
    {
        var menuExists = await _db.Menus.AnyAsync(m => m.Id == dto.MenuId);
        if (!menuExists)
            return BadRequest("The specified MenuId does not reference an existing menu.");

        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            MenuId = dto.MenuId
        };

        _db.Categories.Add(category);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = category.Id }, category);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, CategoryDto dto)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category is null)
            return NotFound();

        var menuExists = await _db.Menus.AnyAsync(m => m.Id == dto.MenuId);
        if (!menuExists)
            return BadRequest("The specified MenuId does not reference an existing menu.");

        category.Name = dto.Name;
        category.MenuId = dto.MenuId;

        await _db.SaveChangesAsync();

        return Ok(category);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var category = await _db.Categories.FindAsync(id);
        if (category is null)
            return NotFound();

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
