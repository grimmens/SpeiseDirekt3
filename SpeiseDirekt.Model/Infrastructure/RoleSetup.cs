using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;

namespace SpeiseDirekt.Infrastructure;

/// <summary>Outcome of <see cref="RoleSetup.SyncGlobalAdminsAsync"/>: counts only, never addresses.</summary>
/// <param name="Added">Accounts that got <see cref="AppRoles.GlobalAdmin"/>.</param>
/// <param name="Removed">Accounts that lost it (not listed any more).</param>
/// <param name="Unknown">Listed addresses without an account (skipped).</param>
/// <param name="Failed">Accounts whose role could not be changed (Identity refused; logged with the account id).</param>
public sealed record GlobalAdminSyncResult(int Added, int Removed, int Unknown, int Failed);

/// <summary>
/// The app's Identity roles at startup (web app: <c>RoleSetupHostedService</c>; in the E2E environment after the schema
/// was created): makes sure <see cref="AppRoles.All"/> exist and syncs <see cref="AppRoles.GlobalAdmin"/> from
/// <c>Admin:GlobalAdminEmails</c> — a full sync: the listed accounts (matched by <c>NormalizedEmail</c>) get the role,
/// every other account loses it. Idempotent. Logs ids and counts only, never e-mail addresses.
/// </summary>
public sealed class RoleSetup
{
    private readonly RoleManager<IdentityRole> _roles;
    private readonly UserManager<ApplicationUser> _users;
    private readonly ILogger<RoleSetup> _logger;

    public RoleSetup(RoleManager<IdentityRole> roles, UserManager<ApplicationUser> users, ILogger<RoleSetup> logger)
    {
        _roles = roles;
        _users = users;
        _logger = logger;
    }

    /// <summary>Both roles exist, then the global admins match <paramref name="globalAdminEmails"/>; logs the counts.</summary>
    public async Task<GlobalAdminSyncResult> RunAsync(IEnumerable<string>? globalAdminEmails, CancellationToken cancellationToken = default)
    {
        await EnsureRolesAsync(cancellationToken);
        var result = await SyncGlobalAdminsAsync(globalAdminEmails, cancellationToken);
        _logger.LogInformation(
            "GlobalAdmin sync: {Added} added, {Removed} removed, {Unknown} address(es) without an account",
            result.Added, result.Removed, result.Unknown);
        return result;
    }

    /// <summary>Creates the roles of <see cref="AppRoles.All"/> that do not exist yet.</summary>
    public async Task EnsureRolesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var name in AppRoles.All)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await _roles.RoleExistsAsync(name))
                continue;

            var created = await _roles.CreateAsync(new IdentityRole(name));
            // Another host on the same database (the test hosts) may have created it in the meantime
            if (!created.Succeeded && !await _roles.RoleExistsAsync(name))
                throw new InvalidOperationException(
                    $"Role {name} could not be created: {string.Join(", ", created.Errors.Select(e => e.Code))}");
        }
    }

    /// <summary>
    /// The accounts whose e-mail is listed get <see cref="AppRoles.GlobalAdmin"/>, every other account loses it. Blank
    /// entries are ignored, the comparison is Identity's (normalized e-mail). Needs the role (<see cref="EnsureRolesAsync"/>).
    /// </summary>
    public async Task<GlobalAdminSyncResult> SyncGlobalAdminsAsync(IEnumerable<string>? globalAdminEmails, CancellationToken cancellationToken = default)
    {
        var wanted = (globalAdminEmails ?? [])
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Select(e => _users.NormalizeEmail(e.Trim()))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var listed = wanted.Count == 0
            ? new List<ApplicationUser>()
            : await _users.Users
                .Where(u => u.NormalizedEmail != null && wanted.Contains(u.NormalizedEmail))
                .ToListAsync(cancellationToken);
        var unknown = wanted.Count(e => !listed.Any(u => u.NormalizedEmail == e));

        var current = await _users.GetUsersInRoleAsync(AppRoles.GlobalAdmin);
        var currentIds = current.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
        var listedIds = listed.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);

        int added = 0, removed = 0, failed = 0;
        foreach (var user in listed.Where(u => !currentIds.Contains(u.Id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Succeeded(await _users.AddToRoleAsync(user, AppRoles.GlobalAdmin), "add", user.Id))
                added++;
            else
                failed++;
        }
        foreach (var user in current.Where(u => !listedIds.Contains(u.Id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Succeeded(await _users.RemoveFromRoleAsync(user, AppRoles.GlobalAdmin), "remove", user.Id))
                removed++;
            else
                failed++;
        }

        return new GlobalAdminSyncResult(added, removed, unknown, failed);
    }

    /// <summary>Logs a refused change with the account id and Identity's error codes (descriptions may name the address).</summary>
    private bool Succeeded(IdentityResult result, string action, string userId)
    {
        if (!result.Succeeded)
            _logger.LogWarning("GlobalAdmin sync: could not {Action} the role for account {UserId}: {Errors}",
                action, userId, string.Join(", ", result.Errors.Select(e => e.Code)));
        return result.Succeeded;
    }
}
