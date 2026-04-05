using Microsoft.AspNetCore.Mvc;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Api.Controllers;

[Route("api/public")]
[ApiController]
public class MenuDisplayController : ControllerBase
{
    private readonly IMenuService _menuService;
    private readonly ITrackingService _trackingService;

    public MenuDisplayController(IMenuService menuService, ITrackingService trackingService)
    {
        _menuService = menuService;
        _trackingService = trackingService;
    }

    [HttpGet("menu/{qrCodeId:guid}")]
    public async Task<IActionResult> GetMenuForQrCode(Guid qrCodeId)
    {
        var menu = await _menuService.GetMenuForQRCodeAsync(qrCodeId);
        if (menu == null)
            return NotFound();

        var sessionId = _trackingService.GetOrCreateSessionId();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        await _trackingService.RecordMenuViewAsync(sessionId, menu.Id, qrCodeId, ipAddress, userAgent);

        return Ok(menu);
    }

    [HttpPost("menu/{menuId:guid}/items/{menuItemId:guid}/click")]
    public async Task<IActionResult> RecordMenuItemClick(Guid menuId, Guid menuItemId)
    {
        var sessionId = _trackingService.GetOrCreateSessionId();
        var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = Request.Headers.UserAgent.ToString();

        await _trackingService.RecordMenuItemClickAsync(sessionId, menuItemId, menuId, ipAddress, userAgent);

        return NoContent();
    }
}
