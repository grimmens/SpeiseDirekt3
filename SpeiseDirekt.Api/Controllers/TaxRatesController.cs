using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Api.Controllers;

[Route("api/tax-rates")]
[ApiController]
[Authorize]
public class TaxRatesController : ControllerBase
{
    private readonly ITaxService _taxService;

    public TaxRatesController(ITaxService taxService)
    {
        _taxService = taxService;
    }

    [HttpGet]
    [Authorize(Policy = PolicyNames.CanViewTaxRates)]
    public async Task<ActionResult<List<TaxRate>>> GetAll()
    {
        var rates = await _taxService.GetAllAsync();
        return Ok(rates);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanViewTaxRates)]
    public async Task<ActionResult<TaxRate>> Get(Guid id)
    {
        var rate = await _taxService.GetByIdAsync(id);
        if (rate is null)
            return NotFound();
        return Ok(rate);
    }

    [HttpGet("default")]
    [Authorize(Policy = PolicyNames.CanViewTaxRates)]
    public async Task<ActionResult<TaxRate>> GetDefault()
    {
        var rate = await _taxService.GetDefaultAsync();
        if (rate is null)
            return NotFound("No default tax rate configured.");
        return Ok(rate);
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.CanCreateTaxRates)]
    public async Task<ActionResult<TaxRate>> Create(TaxRateDto dto)
    {
        var rate = await _taxService.CreateAsync(dto.Name, dto.Rate, dto.IsDefault);
        return CreatedAtAction(nameof(Get), new { id = rate.Id }, rate);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanEditTaxRates)]
    public async Task<ActionResult<TaxRate>> Update(Guid id, TaxRateDto dto)
    {
        var rate = await _taxService.UpdateAsync(id, dto.Name, dto.Rate);
        if (rate is null)
            return NotFound();

        if (dto.IsDefault)
            await _taxService.SetDefaultAsync(id);

        return Ok(rate);
    }

    [HttpPut("{id:guid}/default")]
    [Authorize(Policy = PolicyNames.CanEditTaxRates)]
    public async Task<IActionResult> SetDefault(Guid id)
    {
        var rate = await _taxService.GetByIdAsync(id);
        if (rate is null)
            return NotFound();

        await _taxService.SetDefaultAsync(id);
        return Ok();
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanDeleteTaxRates)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _taxService.DeleteAsync(id);
        if (!deleted)
            return NotFound();
        return NoContent();
    }
}
