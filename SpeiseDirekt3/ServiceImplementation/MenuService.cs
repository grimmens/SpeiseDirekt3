using Microsoft.EntityFrameworkCore;
using SpeiseDirekt3.Data;
using SpeiseDirekt3.Model;
using SpeiseDirekt3.ServiceInterface;

namespace SpeiseDirekt3.ServiceImplementation
{
    

    public class MenuService : IMenuService
    {
        private readonly ApplicationDbContext _db;

        public MenuService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<Menu?> GetMenuForQRCodeAsync(Guid qrCodeId)
        {
            // QR-Code mitsamt Navigationen laden
            var qr = await _db.QRCodes
                .IgnoreQueryFilters()
                .AsSplitQuery()
                .Include(q => q.Menu)
                .ThenInclude(e => e.Categories)
                .ThenInclude(e => e.MenuItems)
                .Include(q => q.TimeTableEntries)
                .FirstOrDefaultAsync(q => q.Id == qrCodeId);

            if (qr == null)
                return null;

            // Einfach-Modus: direkt das QR-Code-Menu zurückgeben
            if (!qr.IsTimeTableBased)
                return qr.Menu;

            // Zeitgesteuert: aktuellen Eintrag finden
            var now = TimeOnly.FromDateTime(DateTime.Now);
            var times = qr.TimeTableEntries
                .SelectMany(entry => SplitCrossMidnight(entry, now))
                .ToList();
            var activeEntry = times
                .FirstOrDefault(e => e.StartTime <= now && e.EndTime > now);

            if (activeEntry == null)
                return null;

            // Menü der gefundenen TimeTableEntry nachladen
            return await GetMenuAsync(activeEntry.MenuId);
        }

        private async Task<Menu?> GetMenuAsync(Guid menuId)
        {
            return await _db.Menus
                .IgnoreQueryFilters()
                .Include(e => e.Categories)
                .ThenInclude(e => e.MenuItems)
                .SingleOrDefaultAsync(e => e.Id == menuId);
        }

        // Hilfsmethode, die Einträge, die über Mitternacht gehen, in zwei Einträge aufspaltet
        private IEnumerable<TimeTableEntry> SplitCrossMidnight(TimeTableEntry entry, TimeOnly now)
        {
            // Kein Split nötig, wenn das Ende nach dem Start liegt und keine Mitternacht betroffen ist
            if (entry.StartTime <= entry.EndTime)
            {
                yield return entry;
                yield break;
            }

            // Wenn der Endzeitpunkt über Mitternacht geht, teilen wir es auf
            var startDate = TimeSpan.FromTicks(entry.StartTime.Value.Ticks);
            var endDate = TimeSpan.FromTicks(entry.EndTime.Value.Ticks);

            // Erstes Segment: Heute vor Mitternacht
            yield return new TimeTableEntry
            {
                StartTime = entry.StartTime,
                EndTime = new TimeOnly(23, 59), // Endet um 23:59
                MenuId = entry.MenuId,
                QRCodeId = entry.QRCodeId
            };

            // Zweites Segment: Morgen nach Mitternacht
            yield return new TimeTableEntry
            {
                StartTime = new TimeOnly(0, 0), // Startet um 00:00
                EndTime = entry.EndTime,
                MenuId = entry.MenuId,
                QRCodeId = entry.QRCodeId
            };
        }
    }

}
