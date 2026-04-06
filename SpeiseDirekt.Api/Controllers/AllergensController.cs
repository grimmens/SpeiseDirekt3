using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    public async Task<ActionResult<List<Allergen>>> GetAll()
    {
        var allergens = await _db.Allergens.OrderBy(a => a.Code).ToListAsync();
        return Ok(allergens);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<Allergen>> Get(Guid id)
    {
        var allergen = await _db.Allergens.FindAsync(id);
        if (allergen is null)
            return NotFound();

        return Ok(allergen);
    }
}
