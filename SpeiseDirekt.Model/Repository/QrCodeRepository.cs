using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public class QrCodeRepository : IQrCodeRepository
{
    private readonly ApplicationDbContext _db;

    public QrCodeRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<List<QRCode>> GetAllAsync()
    {
        return await _db.QRCodes.ToListAsync();
    }

    public async Task<QRCode?> GetByIdAsync(Guid id)
    {
        return await _db.QRCodes
            .Include(q => q.TimeTableEntries)
            .Include(q => q.CalendarEntries)
            .Include(q => q.Menu)
            .FirstOrDefaultAsync(q => q.Id == id);
    }

    public async Task<QRCode> CreateAsync(QRCode qrCode)
    {
        _db.QRCodes.Add(qrCode);
        await _db.SaveChangesAsync();
        return qrCode;
    }

    public async Task<QRCode?> UpdateAsync(Guid id, Action<QRCode> updateAction)
    {
        var qrCode = await _db.QRCodes.FindAsync(id);
        if (qrCode is null)
            return null;

        updateAction(qrCode);
        await _db.SaveChangesAsync();
        return qrCode;
    }

    public async Task<bool> DeleteAsync(Guid id)
    {
        var qrCode = await _db.QRCodes
            .Include(q => q.TimeTableEntries)
            .Include(q => q.CalendarEntries)
            .FirstOrDefaultAsync(q => q.Id == id);

        if (qrCode is null)
            return false;

        // Remove tracking records that reference this QR code (configured with NoAction FK)
        var menuViews = await _db.MenuViews.Where(v => v.QRCodeId == id).ToListAsync();
        _db.MenuViews.RemoveRange(menuViews);

        _db.QRCodes.Remove(qrCode);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<TimeTableEntry?> AddTimeTableEntryAsync(Guid qrCodeId, TimeTableEntry entry)
    {
        var qrCode = await _db.QRCodes.FindAsync(qrCodeId);
        if (qrCode is null)
            return null;

        entry.QRCodeId = qrCodeId;
        _db.TimeTableEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task<bool> RemoveTimeTableEntryAsync(Guid qrCodeId, Guid entryId)
    {
        var entry = await _db.TimeTableEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.QRCodeId == qrCodeId);

        if (entry is null)
            return false;

        _db.TimeTableEntries.Remove(entry);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<CalendarEntry?> AddCalendarEntryAsync(Guid qrCodeId, CalendarEntry entry)
    {
        var qrCode = await _db.QRCodes.FindAsync(qrCodeId);
        if (qrCode is null)
            return null;

        entry.QRCodeId = qrCodeId;
        _db.CalendarEntries.Add(entry);
        await _db.SaveChangesAsync();
        return entry;
    }

    public async Task<bool> RemoveCalendarEntryAsync(Guid qrCodeId, Guid entryId)
    {
        var entry = await _db.CalendarEntries
            .FirstOrDefaultAsync(e => e.Id == entryId && e.QRCodeId == qrCodeId);

        if (entry is null)
            return false;

        _db.CalendarEntries.Remove(entry);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> MenuExistsAsync(Guid menuId)
    {
        return await _db.Menus.AnyAsync(m => m.Id == menuId);
    }
}
