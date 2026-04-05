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
public class MenusController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public MenusController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<Menu>>> GetAll()
    {
        var menus = await _db.Menus.ToListAsync();
        return Ok(menus);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Menu>> Get(Guid id)
    {
        var menu = await _db.Menus
            .Include(m => m.Categories!)
                .ThenInclude(c => c.MenuItems!)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (menu is null)
            return NotFound();

        return Ok(menu);
    }

    [HttpPost]
    public async Task<ActionResult<Menu>> Create(MenuDto dto)
    {
        var menu = new Menu
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Description = dto.Description,
            Theme = dto.Theme,
            Language = dto.Language
        };

        _db.Menus.Add(menu);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = menu.Id }, menu);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, MenuDto dto)
    {
        var menu = await _db.Menus.FindAsync(id);
        if (menu is null)
            return NotFound();

        menu.Name = dto.Name;
        menu.Description = dto.Description;
        menu.Theme = dto.Theme;
        menu.Language = dto.Language;

        await _db.SaveChangesAsync();

        return Ok(menu);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var menu = await _db.Menus.FindAsync(id);
        if (menu is null)
            return NotFound();

        _db.Menus.Remove(menu);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
