using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Data;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Api.Controllers;

[Route("api/public")]
[ApiController]
public class MenuDisplayController : ControllerBase
{
    private readonly IMenuService _menuService;
    private readonly ITrackingService _trackingService;
    private readonly IRestaurantProfileService _restaurantProfiles;
    private readonly ApplicationDbContext _db;

    public MenuDisplayController(IMenuService menuService, ITrackingService trackingService,
        IRestaurantProfileService restaurantProfiles, ApplicationDbContext db)
    {
        _menuService = menuService;
        _trackingService = trackingService;
        _restaurantProfiles = restaurantProfiles;
        _db = db;
    }

    [HttpGet("menu/{qrCodeId:guid}")]
    public async Task<IActionResult> GetMenuForQrCode(Guid qrCodeId)
    {
        var menu = await _menuService.GetMenuForQRCodeAsync(qrCodeId);
        if (menu == null)
            return NotFound();

        // The guest's daily visitor key for the menu's restaurant: no cookie, no IP address or user agent stored
        await _trackingService.RecordMenuViewAsync(_trackingService.GetVisitorKey(menu.ApplicationUserId), menu.Id, qrCodeId);

        return Ok(menu);
    }

    /// <summary>
    /// Combos of a menu for guests: trigger item, included items and combo price.
    /// Public path, so tenant query filters are ignored (like <see cref="IMenuService"/>).
    /// </summary>
    [HttpGet("menu/{menuId:guid}/combos")]
    public async Task<ActionResult<List<PublicMenuComboDto>>> GetCombosForMenu(Guid menuId)
    {
        var menuExists = await _db.Menus
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(m => m.Id == menuId);
        if (!menuExists)
            return NotFound();

        var combos = await _db.MenuCombos
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(c => c.MenuId == menuId)
            .OrderBy(c => c.Name)
            .Select(c => new PublicMenuComboDto(
                c.Id,
                c.Name,
                c.Description,
                c.ComboPrice,
                new PublicComboMenuItemDto(
                    c.TriggerMenuItem!.Id,
                    c.TriggerMenuItem.Name,
                    c.TriggerMenuItem.Description,
                    c.TriggerMenuItem.Price),
                c.Items
                    .OrderBy(i => i.MenuItem!.Name)
                    .Select(i => new PublicComboMenuItemDto(
                        i.MenuItem!.Id,
                        i.MenuItem.Name,
                        i.MenuItem.Description,
                        i.MenuItem.Price))
                    .ToList()))
            .ToListAsync();

        return Ok(combos);
    }

    /// <summary>
    /// The restaurant profile of the tenant that owns the menu (name, texts, contact, links, image URLs).
    /// 404 when the menu does not exist or its tenant has no profile.
    /// </summary>
    [HttpGet("menu/{menuId:guid}/restaurant")]
    public async Task<ActionResult<PublicRestaurantProfileDto>> GetRestaurantForMenu(Guid menuId)
    {
        var profile = await _restaurantProfiles.GetForMenuAsync(menuId);
        if (profile is null)
            return NotFound();

        return Ok(new PublicRestaurantProfileDto(
            profile.Name,
            profile.Description,
            ImagePaths.ApiUrl(profile.LogoImagePath),
            ImagePaths.ApiUrl(profile.CoverImagePath),
            profile.Phone,
            profile.Email,
            profile.Website,
            profile.InstagramUrl,
            profile.FacebookUrl,
            profile.GoogleReviewUrl,
            profile.Address,
            profile.OpeningHours));
    }

    /// <summary>
    /// Records an item click under the guest's daily visitor key for the menu's restaurant (same rules as the Blazor app's
    /// guest pages: no cookie, no IP address or user agent stored). Always 204, like before: an unknown menu records
    /// nothing, an unknown item fails inside the tracking service, which never throws.
    /// </summary>
    [HttpPost("menu/{menuId:guid}/items/{menuItemId:guid}/click")]
    public async Task<IActionResult> RecordMenuItemClick(Guid menuId, Guid menuItemId)
    {
        var tenantId = await _db.Menus
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.Id == menuId)
            .Select(m => (Guid?)m.ApplicationUserId)
            .FirstOrDefaultAsync();
        if (tenantId is { } tenant)
            await _trackingService.RecordMenuItemClickAsync(_trackingService.GetVisitorKey(tenant), menuItemId, menuId);

        return NoContent();
    }
}
