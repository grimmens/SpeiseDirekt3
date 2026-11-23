using SpeiseDirekt.Data;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation
{
    public class TrackingService : ITrackingService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly IVisitorKeyService _visitorKeys;
        private readonly ILogger<TrackingService> _logger;

        public TrackingService(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor, IVisitorKeyService visitorKeys,
            ILogger<TrackingService> logger)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _visitorKeys = visitorKeys;
            _logger = logger;
        }

        public async Task RecordMenuViewAsync(string visitorKey, Guid menuId, Guid? qrCodeId = null)
        {
            try
            {
                _context.MenuViews.Add(new MenuView
                {
                    Id = Guid.NewGuid(),
                    SessionId = visitorKey,
                    MenuId = menuId,
                    QRCodeId = qrCodeId,
                    ViewedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                // No visitor key in the log: it is the analytics' pseudonym, not something to correlate with access logs
                _logger.LogInformation("Recorded menu view: MenuId={MenuId}, QRCodeId={QRCodeId}", menuId, qrCodeId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record menu view: MenuId={MenuId}", menuId);
                // Don't throw - tracking failures shouldn't break the user experience
            }
        }

        public async Task RecordMenuItemClickAsync(string visitorKey, Guid menuItemId, Guid menuId)
        {
            try
            {
                _context.MenuItemClicks.Add(new MenuItemClick
                {
                    Id = Guid.NewGuid(),
                    SessionId = visitorKey,
                    MenuItemId = menuItemId,
                    MenuId = menuId,
                    ClickedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();

                _logger.LogInformation("Recorded menu item click: MenuItemId={MenuItemId}, MenuId={MenuId}", menuItemId, menuId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record menu item click: MenuItemId={MenuItemId}, MenuId={MenuId}", menuItemId, menuId);
                // Don't throw - tracking failures shouldn't break the user experience
            }
        }

        public string GetVisitorKey(Guid tenantId)
        {
            // RemoteIpAddress is the browser's address after UseForwardedHeaders (X-Forwarded-For is trusted only from
            // the loopback proxy); the raw forwarding headers are not read here
            var http = _httpContextAccessor.HttpContext;
            return _visitorKeys.KeyFor(tenantId, http?.Connection.RemoteIpAddress, http?.Request.Headers.UserAgent.ToString());
        }
    }
}
