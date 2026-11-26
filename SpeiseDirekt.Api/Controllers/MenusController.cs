using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt.Repository;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class MenusController : ControllerBase
{
    private readonly IMenuRepository _menuRepository;
    private readonly IMenuTranslationService _translations;
    private readonly ITenantPlanService _plans;
    private readonly IUserIdProvider _userIdProvider;

    public MenusController(IMenuRepository menuRepository, IMenuTranslationService translations,
        ITenantPlanService plans, IUserIdProvider userIdProvider)
    {
        _menuRepository = menuRepository;
        _translations = translations;
        _plans = plans;
        _userIdProvider = userIdProvider;
    }

    [HttpGet]
    [Authorize(Policy = PolicyNames.CanViewMenus)]
    public async Task<ActionResult<List<Menu>>> GetAll()
    {
        var menus = await _menuRepository.GetAllAsync();
        return Ok(menus);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanViewMenus)]
    public async Task<ActionResult<Menu>> Get(Guid id)
    {
        var menu = await _menuRepository.GetByIdAsync(id);

        if (menu is null)
            return NotFound();

        return Ok(menu);
    }

    /// <summary>
    /// The language versions of a menu (<c>translationGroupId</c>), including the menu itself, in language order; just
    /// the menu when it is not linked. Read-only: the versions are managed in the back office.
    /// </summary>
    [HttpGet("{id:guid}/language-versions")]
    [Authorize(Policy = PolicyNames.CanViewMenus)]
    public async Task<ActionResult<List<MenuLanguageVersionDto>>> GetLanguageVersions(Guid id)
    {
        var versions = await _translations.GetVersionsAsync(id);
        if (versions.Count == 0)
            return NotFound();

        return Ok(versions.Select(v => new MenuLanguageVersionDto(v.Id, v.Name, v.Language, v.Id == id)).ToList());
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.CanCreateMenus)]
    public async Task<ActionResult<Menu>> Create(MenuDto dto)
    {
        // Free tier: at most Plans:Free:MaxMenus menus (language versions included); paid tenants have no limit
        var plan = await _plans.GetUsageAsync(_userIdProvider.GetUserId());
        if (!plan.CanCreateMenu)
            return BadRequest($"Menu limit reached ({plan.MaxMenus}). Upgrade your plan to add more menus.");

        var menu = new Menu
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Description = dto.Description?.Trim() ?? string.Empty,
            Theme = dto.Theme,
            Language = dto.Language
        };

        await _menuRepository.CreateAsync(menu);

        return CreatedAtAction(nameof(Get), new { id = menu.Id }, menu);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanEditMenus)]
    public async Task<IActionResult> Update(Guid id, MenuDto dto)
    {
        // A language version may not take a language another version of its group already has
        if (await _translations.ValidateLanguageAsync(id, dto.Language) is { } conflict)
            return BadRequest(conflict);

        var menu = await _menuRepository.UpdateAsync(id, m =>
        {
            m.Name = dto.Name;
            m.Description = dto.Description?.Trim() ?? string.Empty;
            m.Theme = dto.Theme;
            m.Language = dto.Language;
        });

        if (menu is null)
            return NotFound();

        return Ok(menu);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanDeleteMenus)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var deleted = await _menuRepository.DeleteAsync(id);
        if (!deleted)
            return NotFound();

        return NoContent();
    }
}
