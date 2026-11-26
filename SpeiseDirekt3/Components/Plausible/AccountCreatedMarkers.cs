using System.Collections.Concurrent;

namespace SpeiseDirekt3.Components.Plausible;

/// <summary>
/// The one-shot marker of <see cref="PlausibleGoals.AccountCreated"/> (AP16, singleton). The registration is a static SSR
/// form post that signs the new account in and redirects; the event has to come from the page the browser shows after
/// that. <c>Register</c> (and the external-login registration) leave a marker for the new account's id, the first
/// document rendered for that account within <see cref="Lifetime"/> takes it (<c>PlausibleScripts</c> →
/// <c>data-account-created</c> → <c>js/site.js</c> sends the event once) — a reload, the back button or a second tab
/// find none. A document that ends as a redirect (the landing page sends a signed-in visitor on to <c>/dashboard</c>)
/// or an error puts the marker back, so the page after it gets it.
/// <para>
/// Nothing is stored unless the counter is on (<see cref="PlausibleSettings"/>). Only in memory: a restart within the
/// few milliseconds between the post and the page loses one event, which is acceptable. A team member the owner invites
/// is created elsewhere (the team pages, the API) and never gets a marker.
/// </para>
/// </summary>
public sealed class AccountCreatedMarkers(IConfiguration configuration, TimeProvider clock)
{
    /// <summary>How long a marker waits for its page.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _pending = new(StringComparer.Ordinal);

    /// <summary>An account registered itself: its next page sends the event (only while the counter is on).</summary>
    public void AccountCreated(string userId)
    {
        if (!PlausibleSettings.From(configuration).Enabled)
            return;
        var now = clock.GetUtcNow();
        foreach (var entry in _pending)
        {
            if (entry.Value <= now)
                _pending.TryRemove(entry);
        }
        _pending[userId] = now + Lifetime;
    }

    /// <summary>Takes the account's marker (it is gone afterwards); null when there is none or it expired.</summary>
    public DateTimeOffset? Take(string userId) =>
        _pending.TryRemove(userId, out var expires) && expires > clock.GetUtcNow() ? expires : null;

    /// <summary>Puts a taken marker back (its page was not delivered).</summary>
    public void Restore(string userId, DateTimeOffset expires) => _pending.TryAdd(userId, expires);
}
