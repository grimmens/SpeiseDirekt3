using SpeiseDirekt.Model;

namespace SpeiseDirekt.ServiceInterface
{
    /// <summary>
    /// Records what guests open, for the analytics page: menu views and item clicks. Since AP15a without a cookie, without
    /// the IP address and without the user agent: a row carries only the menu, the QR code or item, the time and the
    /// guest's daily visitor key (<see cref="GetVisitorKey"/>, <see cref="IVisitorKeyService"/>).
    /// </summary>
    public interface ITrackingService
    {
        /// <summary>
        /// Records a menu view (a QR code scan, or a page request of a menu)
        /// </summary>
        /// <param name="visitorKey">The guest's daily visitor key (<see cref="GetVisitorKey"/>)</param>
        /// <param name="menuId">ID of the viewed menu</param>
        /// <param name="qrCodeId">ID of the QR code used (optional)</param>
        Task RecordMenuViewAsync(string visitorKey, Guid menuId, Guid? qrCodeId = null);

        /// <summary>
        /// Records a menu item click when a guest opens a menu item
        /// </summary>
        /// <param name="visitorKey">The guest's daily visitor key (<see cref="GetVisitorKey"/>)</param>
        /// <param name="menuItemId">ID of the clicked menu item</param>
        /// <param name="menuId">ID of the menu containing the item</param>
        Task RecordMenuItemClickAsync(string visitorKey, Guid menuItemId, Guid menuId);

        /// <summary>
        /// The daily visitor key of the guest making the current request, for the restaurant <paramref name="tenantId"/>
        /// (the menu's owner, <c>Menu.ApplicationUserId</c>): computed from the client address (after
        /// <c>UseForwardedHeaders</c>) and the <c>User-Agent</c> header, which are not kept. Sets no cookie. A page
        /// request and the item clicks sent from that page compute the same key on the same day.
        /// </summary>
        string GetVisitorKey(Guid tenantId);
    }
}
