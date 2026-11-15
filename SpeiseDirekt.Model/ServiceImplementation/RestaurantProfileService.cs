using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using SpeiseDirekt.Data;
using SpeiseDirekt.Model;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation;

public class RestaurantProfileService : IRestaurantProfileService
{
    private readonly ApplicationDbContext _db;

    public RestaurantProfileService(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<RestaurantProfile?> GetForOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default) =>
        _db.RestaurantProfiles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.ApplicationUserId == ownerId, cancellationToken);

    public async Task<RestaurantProfile?> GetForMenuAsync(Guid menuId, CancellationToken cancellationToken = default)
    {
        var ownerId = await _db.Menus
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(m => m.Id == menuId)
            .Select(m => (Guid?)m.ApplicationUserId)
            .FirstOrDefaultAsync(cancellationToken);
        return ownerId is { } owner ? await GetForOwnerAsync(owner, cancellationToken) : null;
    }

    public Task<RestaurantProfile?> GetOwnAsync(CancellationToken cancellationToken = default) =>
        _db.RestaurantProfiles.AsNoTracking().FirstOrDefaultAsync(cancellationToken);

    public async Task<RestaurantProfileSaveResult> SaveOwnAsync(RestaurantProfile values, CancellationToken cancellationToken = default)
    {
        var normalized = Normalize(values);
        Validator.ValidateObject(normalized, new ValidationContext(normalized), validateAllProperties: true);

        var profile = await _db.RestaurantProfiles.FirstOrDefaultAsync(cancellationToken);
        var created = profile is null;
        if (profile is null)
        {
            // ApplicationUserId is stamped by the DbContext (current tenant owner)
            profile = new RestaurantProfile { Id = Guid.NewGuid() };
            _db.RestaurantProfiles.Add(profile);
        }

        CopyValues(normalized, profile);
        await _db.SaveChangesAsync(cancellationToken);
        _db.Entry(profile).State = EntityState.Detached;
        return new RestaurantProfileSaveResult(profile, created);
    }

    /// <summary>
    /// A copy of <paramref name="values"/> as it is stored: every text trimmed, empty optional fields <c>null</c>,
    /// line breaks of the multi-line fields as <c>\n</c>.
    /// </summary>
    public static RestaurantProfile Normalize(RestaurantProfile values)
    {
        var copy = new RestaurantProfile { Id = values.Id, ApplicationUserId = values.ApplicationUserId };
        CopyValues(values, copy);
        copy.Name = values.Name?.Trim() ?? string.Empty;
        copy.Description = Multiline(values.Description);
        copy.LogoImagePath = Optional(values.LogoImagePath);
        copy.CoverImagePath = Optional(values.CoverImagePath);
        copy.Phone = Optional(values.Phone);
        copy.Email = Optional(values.Email);
        copy.Website = Optional(values.Website);
        copy.InstagramUrl = Optional(values.InstagramUrl);
        copy.FacebookUrl = Optional(values.FacebookUrl);
        copy.GoogleReviewUrl = Optional(values.GoogleReviewUrl);
        copy.Address = Multiline(values.Address);
        copy.OpeningHours = Multiline(values.OpeningHours);
        return copy;
    }

    private static void CopyValues(RestaurantProfile from, RestaurantProfile to)
    {
        to.Name = from.Name;
        to.Description = from.Description;
        to.LogoImagePath = from.LogoImagePath;
        to.CoverImagePath = from.CoverImagePath;
        to.Phone = from.Phone;
        to.Email = from.Email;
        to.Website = from.Website;
        to.InstagramUrl = from.InstagramUrl;
        to.FacebookUrl = from.FacebookUrl;
        to.GoogleReviewUrl = from.GoogleReviewUrl;
        to.Address = from.Address;
        to.OpeningHours = from.OpeningHours;
    }

    private static string? Optional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Multiline(string? value) =>
        Optional(value?.Replace("\r\n", "\n").Replace('\r', '\n'));
}
