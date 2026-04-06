using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Model;
using SpeiseDirekt.Repository;

namespace SpeiseDirekt.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class QrCodesController : ControllerBase
{
    private readonly IQrCodeRepository _qrCodeRepository;

    public QrCodesController(IQrCodeRepository qrCodeRepository)
    {
        _qrCodeRepository = qrCodeRepository;
    }

    [HttpGet]
    public async Task<ActionResult<List<QRCode>>> GetAll()
    {
        var qrCodes = await _qrCodeRepository.GetAllAsync();
        return Ok(qrCodes);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<QRCode>> Get(Guid id)
    {
        var qrCode = await _qrCodeRepository.GetByIdAsync(id);

        if (qrCode is null)
            return NotFound();

        return Ok(qrCode);
    }

    [HttpPost]
    public async Task<ActionResult<QRCode>> Create(QrCodeDto dto)
    {
        if (dto.MenuId.HasValue)
        {
            var menuExists = await _qrCodeRepository.MenuExistsAsync(dto.MenuId.Value);
            if (!menuExists)
                return BadRequest("The specified MenuId does not reference an existing menu.");
        }

        var qrCode = new QRCode
        {
            Id = Guid.NewGuid(),
            Title = dto.Title,
            MenuId = dto.MenuId,
            IsTimeTableBased = dto.IsTimeTableBased,
            IsCalendarBased = dto.IsCalendarBased,
            CreatedAt = DateTime.UtcNow
        };

        await _qrCodeRepository.CreateAsync(qrCode);

        return CreatedAtAction(nameof(Get), new { id = qrCode.Id }, qrCode);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, QrCodeDto dto)
    {
        if (dto.MenuId.HasValue)
        {
            var menuExists = await _qrCodeRepository.MenuExistsAsync(dto.MenuId.Value);
            if (!menuExists)
                return BadRequest("The specified MenuId does not reference an existing menu.");
        }

        var qrCode = await _qrCodeRepository.UpdateAsync(id, q =>
        {
            q.Title = dto.Title;
            q.MenuId = dto.MenuId;
            q.IsTimeTableBased = dto.IsTimeTableBased;
            q.IsCalendarBased = dto.IsCalendarBased;
        });

        if (qrCode is null)
            return NotFound();

        return Ok(qrCode);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _qrCodeRepository.DeleteAsync(id);
        if (!deleted)
            return NotFound();

        return NoContent();
    }

    [HttpPost("{id:guid}/timetable-entries")]
    public async Task<ActionResult<TimeTableEntry>> AddTimeTableEntry(Guid id, TimeTableEntryDto dto)
    {
        var menuExists = await _qrCodeRepository.MenuExistsAsync(dto.MenuId);
        if (!menuExists)
            return BadRequest("The specified MenuId does not reference an existing menu.");

        var entry = new TimeTableEntry
        {
            Id = Guid.NewGuid(),
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            MenuId = dto.MenuId
        };

        var result = await _qrCodeRepository.AddTimeTableEntryAsync(id, entry);
        if (result is null)
            return NotFound();

        return Created($"api/qrcodes/{id}/timetable-entries/{entry.Id}", result);
    }

    [HttpDelete("{id:guid}/timetable-entries/{entryId:guid}")]
    public async Task<IActionResult> RemoveTimeTableEntry(Guid id, Guid entryId)
    {
        var removed = await _qrCodeRepository.RemoveTimeTableEntryAsync(id, entryId);
        if (!removed)
            return NotFound();

        return NoContent();
    }

    [HttpPost("{id:guid}/calendar-entries")]
    public async Task<ActionResult<CalendarEntry>> AddCalendarEntry(Guid id, CalendarEntryDto dto)
    {
        var menuExists = await _qrCodeRepository.MenuExistsAsync(dto.MenuId);
        if (!menuExists)
            return BadRequest("The specified MenuId does not reference an existing menu.");

        var entry = new CalendarEntry
        {
            Id = Guid.NewGuid(),
            Date = dto.Date,
            EndDate = dto.EndDate,
            RecurringDayOfWeek = dto.RecurringDayOfWeek,
            MenuId = dto.MenuId
        };

        var result = await _qrCodeRepository.AddCalendarEntryAsync(id, entry);
        if (result is null)
            return NotFound();

        return Created($"api/qrcodes/{id}/calendar-entries/{entry.Id}", result);
    }

    [HttpDelete("{id:guid}/calendar-entries/{entryId:guid}")]
    public async Task<IActionResult> RemoveCalendarEntry(Guid id, Guid entryId)
    {
        var removed = await _qrCodeRepository.RemoveCalendarEntryAsync(id, entryId);
        if (!removed)
            return NotFound();

        return NoContent();
    }
}
