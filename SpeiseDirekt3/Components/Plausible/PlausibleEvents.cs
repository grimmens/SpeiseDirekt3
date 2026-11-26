using Microsoft.EntityFrameworkCore;
using Microsoft.JSInterop;
using SpeiseDirekt.Data;

namespace SpeiseDirekt3.Components.Plausible;

/// <summary>
/// Conversion events of the interactive back office (AP16, scoped = one per circuit): <c>window.plausible(name)</c> over
/// <see cref="IJSRuntime"/>, so the event carries the browser's page like every other one (the queue of
/// <c>js/site.js</c> holds it until the tracker has loaded). Does nothing — not even the database count — while the
/// counter is off (<see cref="PlausibleSettings"/>: everywhere but production). A failed call (circuit gone, script
/// missing) is logged and ignored: an event never breaks a save.
/// <para>
/// "First" means first in the database, decided on the server right after the save: the tenant (the query filters:
/// the owner's data, whoever of the team saves) has exactly one menu / QR code now. A tenant that deletes its only menu
/// and creates a new one sends the event again; that is accepted. The preview seed and the demo never save through these
/// pages, so they never send one.
/// </para>
/// </summary>
public sealed class PlausibleEvents(IJSRuntime js, IConfiguration configuration, ILogger<PlausibleEvents> logger)
{
    public bool Enabled => PlausibleSettings.From(configuration).Enabled;

    /// <summary>A new menu was saved with <paramref name="db"/>: <see cref="PlausibleGoals.FirstMenu"/> when it is the tenant's only one.</summary>
    public async Task MenuCreatedAsync(ApplicationDbContext db)
    {
        if (Enabled && await db.Menus.CountAsync() == 1)
            await SendAsync(PlausibleGoals.FirstMenu);
    }

    /// <summary>A new QR code was saved with <paramref name="db"/>: <see cref="PlausibleGoals.FirstQrCode"/> when it is the tenant's only one.</summary>
    public async Task QrCodeCreatedAsync(ApplicationDbContext db)
    {
        if (Enabled && await db.QRCodes.CountAsync() == 1)
            await SendAsync(PlausibleGoals.FirstQrCode);
    }

    /// <summary>Sends the event <paramref name="name"/> (no props) from the circuit's page.</summary>
    public async Task SendAsync(string name)
    {
        if (!Enabled)
            return;
        try
        {
            await js.InvokeVoidAsync("plausible", name);
        }
        catch (Exception e) when (e is JSException or JSDisconnectedException or TaskCanceledException or InvalidOperationException)
        {
            logger.LogDebug(e, "Conversion event {Name} not sent", name);
        }
    }
}
