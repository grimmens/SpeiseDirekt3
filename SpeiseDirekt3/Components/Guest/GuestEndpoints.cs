using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt3.Components.Guest;

/// <summary>
/// HTTP endpoints of the static guest pages (they have no circuit, so <c>js/guest-menu.js</c> talks to these).
/// </summary>
public static class GuestEndpoints
{
    public const string ItemClickPath = "/guest/api/item-click";

    /// <summary>Body of <see cref="ItemClickPath"/>: <c>{ "menuId": "…", "itemId": "…" }</c>.</summary>
    public sealed record ItemClickRequest(Guid MenuId, Guid ItemId);

    public static IEndpointRouteBuilder MapGuestEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // JSON body, not a form: the antiforgery middleware does not apply. Anonymous on purpose.
        endpoints.MapPost(ItemClickPath, RecordItemClickAsync);
        return endpoints;
    }

    /// <summary>
    /// Records a <c>MenuItemClick</c> for an item the guest opened. 204 when recorded (or deliberately ignored:
    /// the owner preview), 400 for a malformed body, 404 when the item does not belong to the menu.
    /// The session comes from the <c>ITrackingService</c> cookie (created here if missing).
    /// </summary>
    private static async Task<IResult> RecordItemClickAsync(HttpContext http, ApplicationDbContext db, ITrackingService tracking)
    {
        ItemClickRequest? request;
        try
        {
            request = await http.Request.ReadFromJsonAsync<ItemClickRequest>(http.RequestAborted);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or BadHttpRequestException)
        {
            return Results.BadRequest();
        }

        if (request is null || request.MenuId == Guid.Empty || request.ItemId == Guid.Empty)
            return Results.BadRequest();

        // The owner preview never counts (its page does not send clicks either; this is the safety net)
        if (IsFromOwnerPreview(http.Request))
            return Results.NoContent();

        var belongsToMenu = await db.MenuItems
            .IgnoreQueryFilters()
            .AnyAsync(i => i.Id == request.ItemId && i.Category!.MenuId == request.MenuId, http.RequestAborted);
        if (!belongsToMenu)
            return Results.NotFound();

        await tracking.RecordMenuItemClickAsync(tracking.GetOrCreateSessionId(), request.ItemId, request.MenuId);
        return Results.NoContent();
    }

    private static bool IsFromOwnerPreview(HttpRequest request) =>
        Uri.TryCreate(request.Headers.Referer.ToString(), UriKind.Absolute, out var referer)
        && referer.AbsolutePath.StartsWith("/menu/preview/", StringComparison.OrdinalIgnoreCase);
}
