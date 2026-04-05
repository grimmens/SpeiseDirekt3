using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;
using System.Security.Claims;

namespace SpeiseDirekt.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class QrCodesController : ControllerBase
{
    private readonly ApplicationDbContext _db;

    public QrCodesController(ApplicationDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<ActionResult<List<QRCode>>> GetAll()
    {
        var qrCodes = await _db.QRCodes.ToListAsync();
        return Ok(qrCodes);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<QRCode>> Get(Guid id)
    {
        var qrCode = await _db.QRCodes
            .Include(q => q.TimeTableEntries)
            .Include(q => q.CalendarEntries)
            .Include(q => q.Menu)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (qrCode is null)
            return NotFound();

        return Ok(qrCode);
    }

    [HttpPost]
    public async Task<ActionResult<QRCode>> Create(QrCodeDto dto)
    {
        if (dto.MenuId.HasValue)
        {
            var menuExists = await _db.Menus.AnyAsync(m => m.Id == dto.MenuId.Value);
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

        _db.QRCodes.Add(qrCode);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = qrCode.Id }, qrCode);
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, QrCodeDto dto)
    {
        var qrCode = await _db.QRCodes.FindAsync(id);
        if (qrCode is null)
            return NotFound();

        if (dto.MenuId.HasValue)
        {
            var menuExists = await _db.Menus.AnyAsync(m => m.Id == dto.MenuId.Value);
            if (!menuExists)
                return BadRequest("The specified MenuId does not reference an existing menu.");
        }

        qrCode.Title = dto.Title;
        qrCode.MenuId = dto.MenuId;
        qrCode.IsTimeTableBased = dto.IsTimeTableBased;
        qrCode.IsCalendarBased = dto.IsCalendarBased;

        await _db.SaveChangesAsync();

        return Ok(qrCode);
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var qrCode = await _db.QRCodes
            .Include(q => q.TimeTableEntries)
            .Include(q => q.CalendarEntries)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (qrCode is null)
            return NotFound();

        _db.QRCodes.Remove(qrCode);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id:guid}/timetable-entries")]
    public async Task<ActionResult<TimeTableEntry>> AddTimeTableEntry(Guid id, TimeTableEntryDto dto)
    {
        var qrCode = await _db.QRCodes.FindAsync(id);
        if (qrCode is null)
            return NotFound();

        var menuExists = await _db.Menus.AnyAsync(m => m.Id == dto.MenuId);
        if (!menuExists)
            return BadRequest("The specified MenuId does not reference an existing menu.");

        var entry = new TimeTableEntry
        {
            Id = Guid.NewGuid(),
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            MenuId = dto.MenuId,
            QRCodeId = id
        };

        _db.TimeTableEntries.Add(entry);
        await _db.SaveChangesAsync();

        return Created($"api/qrcodes/{id}/timetable-entries/{entry.Id}", entry);
    }

    [HttpDelete("{id:guid}/timetable-entries/{entryId:guid}")]
    public async Task<IActionResult> RemoveTimeTableEntry(Guid id, Guid entryId)
    {
        var entry = await _db.TimeTableEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.QRCodeId == id);

        if (entry is null)
            return NotFound();

        _db.TimeTableEntries.Remove(entry);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id:guid}/calendar-entries")]
    public async Task<ActionResult<CalendarEntry>> AddCalendarEntry(Guid id, CalendarEntryDto dto)
    {
        var qrCode = await _db.QRCodes.FindAsync(id);
        if (qrCode is null)
            return NotFound();

        var menuExists = await _db.Menus.AnyAsync(m => m.Id == dto.MenuId);
        if (!menuExists)
            return BadRequest("The specified MenuId does not reference an existing menu.");

        var entry = new CalendarEntry
        {
            Id = Guid.NewGuid(),
            Date = dto.Date,
            EndDate = dto.EndDate,
            RecurringDayOfWeek = dto.RecurringDayOfWeek,
            MenuId = dto.MenuId,
            QRCodeId = id
        };

        _db.CalendarEntries.Add(entry);
        await _db.SaveChangesAsync();

        return Created($"api/qrcodes/{id}/calendar-entries/{entry.Id}", entry);
    }

    [HttpDelete("{id:guid}/calendar-entries/{entryId:guid}")]
    public async Task<IActionResult> RemoveCalendarEntry(Guid id, Guid entryId)
    {
        var entry = await _db.CalendarEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.QRCodeId == id);

        if (entry is null)
            return NotFound();

        _db.CalendarEntries.Remove(entry);
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
