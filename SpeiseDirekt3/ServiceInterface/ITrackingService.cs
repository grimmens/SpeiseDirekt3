using SpeiseDirekt3.Model;

namespace SpeiseDirekt3.ServiceInterface
{
    public interface ITrackingService
    {
        /// <summary>
        /// Records a menu view when a user accesses a menu via QR code
        /// </summary>
        /// <param name="sessionId">Session ID from cookie</param>
        /// <param name="menuId">ID of the viewed menu</param>
        /// <param name="qrCodeId">ID of the QR code used (optional)</param>
        /// <param name="ipAddress">User's IP address</param>
        /// <param name="userAgent">User's browser user agent</param>
        Task RecordMenuViewAsync(string sessionId, Guid menuId, Guid? qrCodeId = null, string? ipAddress = null, string? userAgent = null);

        /// <summary>
        /// Records a menu item click when a user clicks on a menu item
        /// </summary>
        /// <param name="sessionId">Session ID from cookie</param>
        /// <param name="menuItemId">ID of the clicked menu item</param>
        /// <param name="menuId">ID of the menu containing the item</param>
        /// <param name="ipAddress">User's IP address</param>
        /// <param name="userAgent">User's browser user agent</param>
        Task RecordMenuItemClickAsync(string sessionId, Guid menuItemId, Guid menuId, string? ipAddress = null, string? userAgent = null);

        /// <summary>
        /// Gets or creates a session ID for tracking
        /// </summary>
        /// <returns>Session ID string</returns>
        string GetOrCreateSessionId();
    }
}
