using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Model;
using SpeiseDirekt.Repository;

namespace SpeiseDirekt.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class MenusController : ControllerBase
{
    private readonly IMenuRepository _menuRepository;

    public MenusController(IMenuRepository menuRepository)
    {
        _menuRepository = menuRepository;
    }

    [HttpGet]
    public async Task<ActionResult<List<Menu>>> GetAll()
    {
        var menus = await _menuRepository.GetAllAsync();
        return Ok(menus);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Menu>> Get(Guid id)
    {
        var menu = await _menuRepository.GetByIdAsync(id);

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

        await _menuRepository.CreateAsync(menu);

        return CreatedAtAction(nameof(Get), new { id = menu.Id }, menu);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, MenuDto dto)
    {
        var menu = await _menuRepository.UpdateAsync(id, m =>
        {
            m.Name = dto.Name;
            m.Description = dto.Description;
            m.Theme = dto.Theme;
            m.Language = dto.Language;
        });

        if (menu is null)
            return NotFound();

        return Ok(menu);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _menuRepository.DeleteAsync(id);
        if (!deleted)
            return NotFound();

        return NoContent();
    }
}
