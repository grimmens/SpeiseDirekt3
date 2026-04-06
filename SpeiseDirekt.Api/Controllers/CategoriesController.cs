using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt.Repository;

namespace SpeiseDirekt.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class CategoriesController : ControllerBase
{
    private readonly ICategoryRepository _categoryRepository;

    public CategoriesController(ICategoryRepository categoryRepository)
    {
        _categoryRepository = categoryRepository;
    }

    [HttpGet]
    [Authorize(Policy = PolicyNames.CanViewCategories)]
    public async Task<ActionResult<List<Category>>> GetAll([FromQuery] Guid? menuId)
    {
        var categories = await _categoryRepository.GetAllAsync(menuId);
        return Ok(categories);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanViewCategories)]
    public async Task<ActionResult<Category>> Get(Guid id)
    {
        var category = await _categoryRepository.GetByIdAsync(id);

        if (category is null)
            return NotFound();

        return Ok(category);
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.CanCreateCategories)]
    public async Task<ActionResult<Category>> Create(CategoryDto dto)
    {
        var menuExists = await _categoryRepository.MenuExistsAsync(dto.MenuId);
        if (!menuExists)
            return BadRequest("The specified MenuId does not reference an existing menu.");

        var category = new Category
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            MenuId = dto.MenuId
        };

        await _categoryRepository.CreateAsync(category);

        return CreatedAtAction(nameof(Get), new { id = category.Id }, category);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanEditCategories)]
    public async Task<IActionResult> Update(Guid id, CategoryDto dto)
    {
        var menuExists = await _categoryRepository.MenuExistsAsync(dto.MenuId);
        if (!menuExists)
            return BadRequest("The specified MenuId does not reference an existing menu.");

        var category = await _categoryRepository.UpdateAsync(id, c =>
        {
            c.Name = dto.Name;
            c.MenuId = dto.MenuId;
        });

        if (category is null)
            return NotFound();

        return Ok(category);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanDeleteCategories)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _categoryRepository.DeleteAsync(id);
        if (!deleted)
            return NotFound();

        return NoContent();
    }
}
