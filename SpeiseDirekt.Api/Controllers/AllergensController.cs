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
public class AllergensController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public AllergensController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    [Authorize(Policy = "CanViewAllergens")]
    public async Task<ActionResult<List<Allergen>>> GetAll([FromQuery] Guid? menuId)
    {
        if (menuId is null)
            return BadRequest("menuId query parameter is required.");

        var allergens = await _db.Allergens
            .Where(a => a.MenuId == menuId.Value)
            .OrderBy(a => a.Code)
            .ToListAsync();

        return Ok(allergens);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = "CanViewAllergens")]
    public async Task<ActionResult<Allergen>> Get(Guid id)
    {
        var allergen = await _db.Allergens.FindAsync(id);
        if (allergen is null)
            return NotFound();

        return Ok(allergen);
    }

    [HttpPost]
    [Authorize(Policy = "CanCreateAllergens")]
    public async Task<ActionResult<Allergen>> Create(AllergenDto dto)
    {
        var codeExists = await _db.Allergens
            .AnyAsync(a => a.MenuId == dto.MenuId && a.Code == dto.Code);

        if (codeExists)
            return BadRequest($"An allergen with code '{dto.Code}' already exists in this menu.");

        var allergen = new Allergen
        {
            Id = Guid.NewGuid(),
            Code = dto.Code,
            Name = dto.Name,
            MenuId = dto.MenuId
        };

        _db.Allergens.Add(allergen);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = allergen.Id }, allergen);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = "CanEditAllergens")]
    public async Task<IActionResult> Update(Guid id, AllergenDto dto)
    {
        var allergen = await _db.Allergens.FindAsync(id);
        if (allergen is null)
            return NotFound();

        var codeExists = await _db.Allergens
            .AnyAsync(a => a.MenuId == allergen.MenuId && a.Code == dto.Code && a.Id != id);

        if (codeExists)
            return BadRequest($"An allergen with code '{dto.Code}' already exists in this menu.");

        allergen.Code = dto.Code;
        allergen.Name = dto.Name;
        await _db.SaveChangesAsync();

        return Ok(allergen);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = "CanDeleteAllergens")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var allergen = await _db.Allergens.FindAsync(id);
        if (allergen is null)
            return NotFound();

        _db.Allergens.Remove(allergen);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
