using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;

namespace SpeiseDirekt.Infrastructure;

public class TenantClaimsTransformation : IClaimsTransformation
{
    private readonly ApplicationDbContext _db;

    public TenantClaimsTransformation(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity?.IsAuthenticated != true)
            return principal;

        // If claim already injected, skip DB lookup
        if (principal.HasClaim(c => c.Type == "TenantOwnerId"))
            return principal;

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
            return principal;

        var appUser = await _db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.TenantOwnerId })
            .FirstOrDefaultAsync();

        if (appUser == null)
            return principal;

        var identity = principal.Identity as ClaimsIdentity;
        if (identity == null)
            return principal;

        if (!string.IsNullOrEmpty(appUser.TenantOwnerId))
        {
            identity.AddClaim(new Claim("TenantOwnerId", appUser.TenantOwnerId));
        }

        return principal;
    }
}
