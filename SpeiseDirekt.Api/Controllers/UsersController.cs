using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Api.Dtos;
using SpeiseDirekt.Data;
using SpeiseDirekt.Infrastructure;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.Api.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class UsersController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserIdProvider _userIdProvider;

    public UsersController(ApplicationDbContext db, UserManager<ApplicationUser> userManager, IUserIdProvider userIdProvider)
    {
        _db = db;
        _userManager = userManager;
        _userIdProvider = userIdProvider;
    }

    [HttpGet]
    [Authorize(Policy = PolicyNames.CanViewUsers)]
    public async Task<ActionResult<List<TenantUser>>> GetAll()
    {
        var tenantOwnerId = _userIdProvider.GetUserId();
        var users = await _db.TenantUsers
            .AsNoTracking()
            .Where(tu => tu.TenantOwnerId == tenantOwnerId)
            .OrderBy(tu => tu.DisplayName)
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanViewUsers)]
    public async Task<ActionResult<TenantUser>> Get(Guid id)
    {
        var tenantOwnerId = _userIdProvider.GetUserId();
        var user = await _db.TenantUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(tu => tu.Id == id && tu.TenantOwnerId == tenantOwnerId);

        if (user is null)
            return NotFound();

        return Ok(user);
    }

    [HttpPost]
    [Authorize(Policy = PolicyNames.CanCreateUsers)]
    public async Task<ActionResult<TenantUser>> Create(CreateTenantUserDto dto)
    {
        var tenantOwnerId = _userIdProvider.GetUserId();

        // Check subscription user limit
        var sub = await _db.TenantSubscriptions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantOwnerId);

        var currentCount = await _db.TenantUsers
            .CountAsync(tu => tu.TenantOwnerId == tenantOwnerId && tu.IsActive);

        if (sub != null && currentCount >= sub.MaxUsers)
            return BadRequest($"User limit reached ({sub.MaxUsers}). Upgrade your plan to add more users.");

        // Create the Identity user
        var appUser = new ApplicationUser
        {
            UserName = dto.Email,
            Email = dto.Email,
            TenantOwnerId = tenantOwnerId
        };

        var password = dto.Password ?? GenerateTemporaryPassword();
        var result = await _userManager.CreateAsync(appUser, password);
        if (!result.Succeeded)
            return BadRequest(result.Errors.Select(e => e.Description));

        // Create TenantUser record
        var tenantUser = new TenantUser
        {
            Id = Guid.NewGuid(),
            ApplicationUserId = appUser.Id,
            TenantOwnerId = tenantOwnerId,
            Role = dto.Role,
            DisplayName = dto.DisplayName,
            IsActive = true
        };

        _db.TenantUsers.Add(tenantUser);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(Get), new { id = tenantUser.Id }, tenantUser);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanEditUsers)]
    public async Task<IActionResult> Update(Guid id, UpdateTenantUserDto dto)
    {
        var tenantOwnerId = _userIdProvider.GetUserId();
        var tenantUser = await _db.TenantUsers
            .FirstOrDefaultAsync(tu => tu.Id == id && tu.TenantOwnerId == tenantOwnerId);

        if (tenantUser is null)
            return NotFound();

        tenantUser.Role = dto.Role;
        tenantUser.IsActive = dto.IsActive;
        if (dto.CustomPermissions.HasValue)
            tenantUser.Permissions = dto.CustomPermissions.Value;

        await _db.SaveChangesAsync();

        return Ok(tenantUser);
    }

    [HttpDelete("{id:guid}")]
    [Authorize(Policy = PolicyNames.CanDeleteUsers)]
    public async Task<IActionResult> Delete(Guid id)
    {
        var tenantOwnerId = _userIdProvider.GetUserId();
        var tenantUser = await _db.TenantUsers
            .FirstOrDefaultAsync(tu => tu.Id == id && tu.TenantOwnerId == tenantOwnerId);

        if (tenantUser is null)
            return NotFound();

        // Deactivate rather than delete
        tenantUser.IsActive = false;
        await _db.SaveChangesAsync();

        return NoContent();
    }

    private static string GenerateTemporaryPassword()
    {
        return $"Temp{Guid.NewGuid():N}"[..16] + "!1";
    }
}
