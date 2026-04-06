using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class DiscountsController : ControllerBase
{
    private readonly IDiscountService _discountService;

    public DiscountsController(IDiscountService discountService)
    {
        _discountService = discountService;
    }

    [HttpGet]
    [Authorize(Policy = PolicyNames.CanViewDiscounts)]
    public async Task<ActionResult<List<Discount>>> GetAll()
    {
        var discounts = await _discountService.GetAllAsync();
        return Ok(discounts);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanViewDiscounts)]
    public async Task<ActionResult<Discount>> Get(Guid id)
    {
        var discount = await _discountService.GetByIdAsync(id);
        if (discount is null)
            return NotFound();
        return Ok(discount);
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.CanCreateDiscounts)]
    public async Task<ActionResult<Discount>> Create(DiscountDto dto)
    {
        var discount = new Discount
        {
            Code = dto.Code,
            Description = dto.Description,
            Type = dto.Type,
            Value = dto.Value,
            MinOrderAmount = dto.MinOrderAmount,
            ValidFrom = dto.ValidFrom,
            ValidTo = dto.ValidTo,
            MaxUses = dto.MaxUses,
            IsActive = dto.IsActive
        };

        var created = await _discountService.CreateAsync(discount);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanEditDiscounts)]
    public async Task<ActionResult<Discount>> Update(Guid id, DiscountDto dto)
    {
        var updated = await _discountService.UpdateAsync(id, d =>
        {
            d.Code = dto.Code;
            d.Description = dto.Description;
            d.Type = dto.Type;
            d.Value = dto.Value;
            d.MinOrderAmount = dto.MinOrderAmount;
            d.ValidFrom = dto.ValidFrom;
            d.ValidTo = dto.ValidTo;
            d.MaxUses = dto.MaxUses;
            d.IsActive = dto.IsActive;
        });

        if (updated is null)
            return NotFound();
        return Ok(updated);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanDeleteDiscounts)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _discountService.DeleteAsync(id);
        if (!deleted)
            return NotFound();
        return NoContent();
    }

    [HttpPost("validate")]
    [Authorize(Policy = PolicyNames.CanViewDiscounts)]
    public async Task<ActionResult> Validate(ValidateDiscountDto dto)
    {
        var (isValid, error) = await _discountService.ValidateDiscountAsync(dto.Code, dto.OrderSubTotal);
        return Ok(new { IsValid = isValid, ErrorMessage = error });
    }
}
