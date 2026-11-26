using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;

namespace SpeiseDirekt.Infrastructure;

/// <summary>
/// Identity role checks straight from the database (<c>AspNetUserRoles</c> joined with <c>AspNetRoles</c>), never from the
/// sign-in cookie: a role given or taken away counts at the next check, without a new sign-in. Works through
/// <see cref="ApplicationDbContext"/> alone, so also where no <c>UserManager</c> is at hand (authorization handlers).
/// </summary>
internal static class RoleQueries
{
    /// <summary>Whether the account <paramref name="userId"/> (its Identity id as stored) has <paramref name="role"/>.</summary>
    public static Task<bool> HasRoleAsync(this ApplicationDbContext db, string userId, string role, CancellationToken cancellationToken = default)
    {
        var normalized = AppRoles.Normalized(role);
        return db.UserRoles.AnyAsync(ur => ur.UserId == userId
            && db.Roles.Any(r => r.Id == ur.RoleId && r.NormalizedName == normalized), cancellationToken);
    }
}
