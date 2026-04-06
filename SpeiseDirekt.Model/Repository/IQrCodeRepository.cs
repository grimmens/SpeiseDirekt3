using SpeiseDirekt.Model;

namespace SpeiseDirekt.Repository;

public interface IQrCodeRepository
{
    Task<List<QRCode>> GetAllAsync();
    Task<QRCode?> GetByIdAsync(Guid id);
    Task<QRCode> CreateAsync(QRCode qrCode);
    Task<QRCode?> UpdateAsync(Guid id, Action<QRCode> updateAction);
    Task<bool> DeleteAsync(Guid id);
    Task<TimeTableEntry?> AddTimeTableEntryAsync(Guid qrCodeId, TimeTableEntry entry);
    Task<bool> RemoveTimeTableEntryAsync(Guid qrCodeId, Guid entryId);
    Task<CalendarEntry?> AddCalendarEntryAsync(Guid qrCodeId, CalendarEntry entry);
    Task<bool> RemoveCalendarEntryAsync(Guid qrCodeId, Guid entryId);
    Task<bool> MenuExistsAsync(Guid menuId);
}
