using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation
{
    

    public class MenuService : IMenuService
    {
        private readonly ApplicationDbContext _db;
        private readonly TimeProvider _timeProvider;

        public MenuService(ApplicationDbContext db, TimeProvider timeProvider)
        {
            _db = db;
            _timeProvider = timeProvider;
        }

        /// <summary>
        /// Liefert das aktuell gültige Menü für einen QR-Code. Maßgeblich ist die lokale Zeit
        /// des <see cref="TimeProvider"/> (<see cref="TimeProvider.GetLocalNow"/>).
        /// </summary>
        /// <remarks>
        /// Reihenfolge der Auflösung:
        /// <list type="number">
        /// <item><description>
        /// <see cref="QRCode.IsCalendarBased"/> und ein <see cref="CalendarEntry"/> passt auf das heutige Datum
        /// → Menü dieses Kalender-Eintrags. Passen mehrere Einträge, gewinnt ein einmaliger Eintrag
        /// (ohne <see cref="CalendarEntry.RecurringDayOfWeek"/>) vor einem wiederkehrenden, sonst der früheste nach <see cref="CalendarEntry.Date"/>.
        /// </description></item>
        /// <item><description>
        /// <see cref="QRCode.IsTimeTableBased"/> → Menü des aktuell aktiven <see cref="TimeTableEntry"/>
        /// (auch über Mitternacht); ist kein Zeitfenster aktiv, wird <c>null</c> geliefert.
        /// </description></item>
        /// <item><description>
        /// Sonst → das direkt zugeordnete Menü <see cref="QRCode.Menu"/> (kann <c>null</c> sein).
        /// </description></item>
        /// </list>
        /// Ein kalenderbasierter QR-Code ohne passenden Eintrag fällt also auf Stufe 2 bzw. 3 zurück.
        /// </remarks>
        /// <param name="qrCodeId">Die Id des QR-Codes.</param>
        /// <returns>Das aufgelöste Menü oder <c>null</c>, falls der QR-Code nicht existiert oder kein Menü aktiv ist.</returns>
        public async Task<Menu?> GetMenuForQRCodeAsync(Guid qrCodeId)
        {
            // QR-Code mitsamt Navigationen laden (öffentlicher Pfad, daher ohne Mandanten-Filter)
            var qr = await _db.QRCodes
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(q => q.Menu)
                .ThenInclude(e => e.Categories)
                .ThenInclude(e => e.MenuItems)
                .ThenInclude(e => e.Allergens)
                .Include(q => q.TimeTableEntries)
                .Include(q => q.CalendarEntries)
                .FirstOrDefaultAsync(q => q.Id == qrCodeId);

            if (qr == null)
                return null;

            var localNow = _timeProvider.GetLocalNow().DateTime;
            var today = DateOnly.FromDateTime(localNow);
            var now = TimeOnly.FromDateTime(localNow);

            // 1. Kalenderbasiert: Eintrag für das heutige Datum suchen
            if (qr.IsCalendarBased)
            {
                var calendarEntry = (qr.CalendarEntries ?? Enumerable.Empty<CalendarEntry>())
                    .Where(e => IsCalendarEntryActive(e, today))
                    .OrderBy(e => e.RecurringDayOfWeek.HasValue) // einmalige Einträge vor wiederkehrenden
                    .ThenBy(e => e.Date)
                    .FirstOrDefault();

                if (calendarEntry != null)
                    return await GetMenuAsync(calendarEntry.MenuId);
            }

            // 2. Zeitgesteuert: aktuellen Eintrag finden
            if (qr.IsTimeTableBased)
            {
                var activeEntry = (qr.TimeTableEntries ?? Enumerable.Empty<TimeTableEntry>())
                    .FirstOrDefault(e => IsTimeTableEntryActive(e, now));

                if (activeEntry == null)
                    return null;

                // Menü der gefundenen TimeTableEntry nachladen
                return await GetMenuAsync(activeEntry.MenuId);
            }

            // 3. Einfach-Modus: direkt das QR-Code-Menü zurückgeben
            return qr.Menu;
        }

        private async Task<Menu?> GetMenuAsync(Guid menuId)
        {
            return await _db.Menus
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Include(e => e.Categories)
                .ThenInclude(e => e.MenuItems)
                .ThenInclude(e => e.Allergens)
                .SingleOrDefaultAsync(e => e.Id == menuId);
        }

        // Einmaliger Eintrag: Date <= heute <= (EndDate ?? Date)
        // Wiederkehrender Eintrag: passender Wochentag, ab Date und (falls gesetzt) bis EndDate
        private static bool IsCalendarEntryActive(CalendarEntry entry, DateOnly today)
        {
            if (entry.RecurringDayOfWeek is null)
                return entry.Date <= today && today <= (entry.EndDate ?? entry.Date);

            return today.DayOfWeek == entry.RecurringDayOfWeek
                && today >= entry.Date
                && (entry.EndDate is null || today <= entry.EndDate);
        }

        // Zeitfenster [StartTime, EndTime). Liegt EndTime vor StartTime, geht das Fenster über Mitternacht
        // und ist aktiv, wenn now >= StartTime (vor Mitternacht) oder now < EndTime (nach Mitternacht).
        private static bool IsTimeTableEntryActive(TimeTableEntry entry, TimeOnly now)
        {
            if (entry.StartTime is not { } start || entry.EndTime is not { } end)
                return false;

            if (start <= end)
                return start <= now && now < end;

            return now >= start || now < end;
        }
    }

}
