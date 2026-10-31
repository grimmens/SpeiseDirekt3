using System;
using System.Threading.Tasks;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.ServiceInterface
{
    
    public interface IMenuService
    {
        /// <summary>
        /// Liefert das zu diesem QR-Code gehörende Menü.
        /// Vorrang: passender Kalender-Eintrag für heute (kalenderbasiert), dann die aktuelle
        /// TimeTableEntry (zeitgesteuert), sonst das direkt zugeordnete Menü.
        /// </summary>
        /// <param name="qrCodeId">Die Id des QR-Codes.</param>
        /// <returns>Das aufgelöste Menu oder null, falls nicht gefunden oder kein Eintrag aktiv.</returns>
        Task<Menu?> GetMenuForQRCodeAsync(Guid qrCodeId);
    }
}
