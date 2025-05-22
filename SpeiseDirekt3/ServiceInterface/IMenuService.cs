using System;
using System.Threading.Tasks;
using SpeiseDirekt3.Model;

namespace SpeiseDirekt3.ServiceInterface
{
    
    public interface IMenuService
    {
        /// <summary>
        /// Liefert das zu diesem QR-Code gehörende Menü.
        /// Wenn der QR-Code zeitgesteuert ist, das Menü der aktuellen TimeTableEntry.
        /// </summary>
        /// <param name="qrCodeId">Die Id des QR-Codes.</param>
        /// <returns>Das aufgelöste Menu oder null, falls nicht gefunden oder kein Eintrag aktiv.</returns>
        Task<Menu?> GetMenuForQRCodeAsync(Guid qrCodeId);
    }
}
