using SpeiseDirekt.Model;

namespace SpeiseDirekt.ServiceInterface;

/// <summary>
/// The tenant's <see cref="RestaurantProfile"/>: read-only loaders for anonymous guest pages and the back office's
/// save (one profile per tenant, created on the first save).
/// </summary>
public interface IRestaurantProfileService
{
    /// <summary>
    /// Guest pages: the profile of tenant owner <paramref name="ownerId"/> (= <see cref="Menu.ApplicationUserId"/>),
    /// without tenant query filters and change tracking. <c>null</c> when the tenant has none.
    /// </summary>
    Task<RestaurantProfile?> GetForOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default);

    /// <summary>Guest pages: the profile of the tenant that owns menu <paramref name="menuId"/>; <c>null</c> when there is no such menu or profile.</summary>
    Task<RestaurantProfile?> GetForMenuAsync(Guid menuId, CancellationToken cancellationToken = default);

    /// <summary>Back office / API: the current tenant's profile (query filter, not tracked); <c>null</c> when none exists yet.</summary>
    Task<RestaurantProfile?> GetOwnAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Back office / API: creates or updates the current tenant's profile with the values of <paramref name="values"/>
    /// (trimmed, empty → <c>null</c>; <c>Id</c> and <c>ApplicationUserId</c> of the argument are ignored).
    /// Throws <see cref="System.ComponentModel.DataAnnotations.ValidationException"/> for invalid values.
    /// </summary>
    Task<RestaurantProfileSaveResult> SaveOwnAsync(RestaurantProfile values, CancellationToken cancellationToken = default);
}

/// <param name="Profile">The saved profile (not tracked).</param>
/// <param name="Created">True when the tenant had no profile before.</param>
public sealed record RestaurantProfileSaveResult(RestaurantProfile Profile, bool Created);
