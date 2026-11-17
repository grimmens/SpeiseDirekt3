namespace SpeiseDirekt.Infrastructure;

/// <summary>
/// Images live in the <c>Images</c> table; entities store them as <c>image/{id}</c> (what
/// <c>ImageDatabaseUploadService.UploadImageAsync</c> returns). The Blazor app serves them at <c>/image/{id}</c>, the API
/// at <c>/api/images/{id}</c>.
/// </summary>
public static class ImagePaths
{
    private const string Prefix = "image/";

    public static string ForImage(Guid imageId) => Prefix + imageId;

    /// <summary>The image id of a stored path <c>image/{id}</c>; false for anything else.</summary>
    public static bool TryGetImageId(string? path, out Guid imageId)
    {
        imageId = Guid.Empty;
        return path is not null
            && path.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            && Guid.TryParse(path.AsSpan(Prefix.Length), out imageId);
    }

    /// <summary>URL of a stored image path in the Web API (<c>/api/images/{id}</c>), or null.</summary>
    public static string? ApiUrl(string? path) => TryGetImageId(path, out var id) ? $"/api/images/{id}" : null;
}
