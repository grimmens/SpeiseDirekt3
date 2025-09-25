using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using SpeiseDirekt3.Data;
using SpeiseDirekt3.Model;
using SpeiseDirekt3.ServiceInterface;

namespace SpeiseDirekt3.ServiceImplementation
{
    public class TrackingService : ITrackingService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<TrackingService> _logger;
        private readonly IJSRuntime runtime;

        public TrackingService(ApplicationDbContext context, IHttpContextAccessor httpContextAccessor, ILogger<TrackingService> logger, IJSRuntime runtime)
        {
            _context = context;
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
            this.runtime = runtime;
        }

        public async Task RecordMenuViewAsync(string sessionId, Guid menuId, Guid? qrCodeId = null, string? ipAddress = null, string? userAgent = null)
        {
            try
            {
                var menuView = new MenuView
                {
                    Id = Guid.NewGuid(),
                    SessionId = sessionId,
                    MenuId = menuId,
                    QRCodeId = qrCodeId,
                    ViewedAt = DateTime.UtcNow,
                    IpAddress = ipAddress ?? GetClientIpAddress(),
                    UserAgent = userAgent ?? GetUserAgent()
                };

                _context.MenuViews.Add(menuView);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Recorded menu view: SessionId={SessionId}, MenuId={MenuId}, QRCodeId={QRCodeId}", 
                    sessionId, menuId, qrCodeId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record menu view: SessionId={SessionId}, MenuId={MenuId}", sessionId, menuId);
                // Don't throw - tracking failures shouldn't break the user experience
            }
        }

        public async Task RecordMenuItemClickAsync(string sessionId, Guid menuItemId, Guid menuId, string? ipAddress = null, string? userAgent = null)
        {
            try
            {
                var menuItemClick = new MenuItemClick
                {
                    Id = Guid.NewGuid(),
                    SessionId = sessionId,
                    MenuItemId = menuItemId,
                    MenuId = menuId,
                    ClickedAt = DateTime.UtcNow,
                    IpAddress = ipAddress ?? GetClientIpAddress(),
                    UserAgent = userAgent ?? GetUserAgent()
                };

                _context.MenuItemClicks.Add(menuItemClick);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Recorded menu item click: SessionId={SessionId}, MenuItemId={MenuItemId}, MenuId={MenuId}", 
                    sessionId, menuItemId, menuId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to record menu item click: SessionId={SessionId}, MenuItemId={MenuItemId}", sessionId, menuItemId);
                // Don't throw - tracking failures shouldn't break the user experience
            }
        }

        public async Task<string> GetOrCreateSessionId()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null)
            {
                return Guid.NewGuid().ToString();
            }

            const string sessionCookieName = "SpeiseDirekt_SessionId";
            
            // Try to get existing session ID from cookie
            if (httpContext.Request.Cookies.TryGetValue(sessionCookieName, out var existingSessionId) && 
                !string.IsNullOrEmpty(existingSessionId))
            {
                return existingSessionId;
            }

            // Create new session ID
            var newSessionId = Guid.NewGuid().ToString();
            
            // Set cookie with session ID (expires in 30 days)
            var cookieOptions = new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddDays(30),
                HttpOnly = true,
                Secure = httpContext.Request.IsHttps,
                SameSite = SameSiteMode.Lax
            };
            
            if(httpContext.Response.HasStarted == false)
                httpContext.Response.Cookies.Append(sessionCookieName, newSessionId, cookieOptions);
            else
            {
                // Response has started, use JavaScript to set cookie
                try
                {
                    var expires = DateTimeOffset.UtcNow.AddDays(30).ToString("R"); // RFC 1123 format
                    var secure = httpContext.Request.IsHttps ? "; Secure" : "";
                    var cookieValue = $"{sessionCookieName}={newSessionId}; expires={expires}; path=/; SameSite=Lax{secure}";

                    await this.runtime.InvokeVoidAsync("eval", $"document.cookie = '{cookieValue}';");
                }
                catch (Exception ex)
                {
                    // Handle JS runtime errors (e.g., during prerendering)
                    // Log the exception if needed
                    // The session ID will still be returned and can be used for this request
                }
            }

                return newSessionId;
        }

        private string? GetClientIpAddress()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            if (httpContext == null) return null;

            // Check for forwarded IP first (in case of proxy/load balancer)
            var forwardedFor = httpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                return forwardedFor.Split(',')[0].Trim();
            }

            var realIp = httpContext.Request.Headers["X-Real-IP"].FirstOrDefault();
            if (!string.IsNullOrEmpty(realIp))
            {
                return realIp;
            }

            return httpContext.Connection.RemoteIpAddress?.ToString();
        }

        private string? GetUserAgent()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            return httpContext?.Request.Headers["User-Agent"].FirstOrDefault();
        }
    }
}
