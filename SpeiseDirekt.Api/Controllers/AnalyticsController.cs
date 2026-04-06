using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly IAnalyticsService _analyticsService;

    public AnalyticsController(IAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    [HttpGet("traffic-per-user")]
    [Authorize(Policy = PolicyNames.CanViewAnalytics)]
    public async Task<ActionResult<List<UserTrafficData>>> GetTrafficPerUser(
        [FromQuery] TimeRange timeRange = TimeRange.Last7Days)
    {
        var data = await _analyticsService.GetTrafficPerUserAsync(timeRange);
        return Ok(data);
    }

    [HttpGet("traffic-per-menu")]
    [Authorize(Policy = PolicyNames.CanViewAnalytics)]
    public async Task<ActionResult<List<MenuTrafficData>>> GetTrafficPerMenu(
        [FromQuery] TimeRange timeRange = TimeRange.Last7Days)
    {
        var data = await _analyticsService.GetTrafficPerMenuAsync(timeRange);
        return Ok(data);
    }

    [HttpGet("traffic-per-menuitem")]
    [Authorize(Policy = PolicyNames.CanViewAnalytics)]
    public async Task<ActionResult<List<MenuItemTrafficData>>> GetTrafficPerMenuItem(
        [FromQuery] TimeRange timeRange = TimeRange.Last7Days)
    {
        var data = await _analyticsService.GetTrafficPerMenuItemAsync(timeRange);
        return Ok(data);
    }
}
