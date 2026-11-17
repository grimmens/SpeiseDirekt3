using System.ComponentModel.DataAnnotations;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Api.Dtos;

/// <summary>
/// The tenant's restaurant profile (<c>PUT /api/restaurant-profile</c>). Text fields replace the stored values (omitted
/// or empty = not set); URLs must be absolute <c>http(s)://</c> addresses.
/// </summary>
public record RestaurantProfileDto
{
    [Required(AllowEmptyStrings = false, ErrorMessage = "Name is required.")]
    [StringLength(RestaurantProfile.NameMaxLength, ErrorMessage = "The name can have at most 100 characters.")]
    public string Name { get; init; } = string.Empty;

    [StringLength(RestaurantProfile.DescriptionMaxLength, ErrorMessage = "The description can have at most 1000 characters.")]
    public string? Description { get; init; }

    /// <summary>
    /// <c>image/{id}</c> of an image uploaded with <c>POST /api/images</c> (its <c>id</c>). Omitted or <c>null</c> keeps the
    /// current logo, an empty string removes it.
    /// </summary>
    public string? LogoImagePath { get; init; }

    /// <summary>Like <see cref="LogoImagePath"/>, for the wide cover image.</summary>
    public string? CoverImagePath { get; init; }

    [StringLength(40, ErrorMessage = "The phone number can have at most 40 characters.")]
    public string? Phone { get; init; }

    [OptionalEmail]
    [StringLength(200, ErrorMessage = "The e-mail address can have at most 200 characters.")]
    public string? Email { get; init; }

    [HttpUrl]
    [StringLength(RestaurantProfile.UrlMaxLength, ErrorMessage = "The web address can have at most 500 characters.")]
    public string? Website { get; init; }

    [HttpUrl]
    [StringLength(RestaurantProfile.UrlMaxLength, ErrorMessage = "The web address can have at most 500 characters.")]
    public string? InstagramUrl { get; init; }

    [HttpUrl]
    [StringLength(RestaurantProfile.UrlMaxLength, ErrorMessage = "The web address can have at most 500 characters.")]
    public string? FacebookUrl { get; init; }

    [HttpUrl]
    [StringLength(RestaurantProfile.UrlMaxLength, ErrorMessage = "The web address can have at most 500 characters.")]
    public string? GoogleReviewUrl { get; init; }

    [StringLength(500, ErrorMessage = "The address can have at most 500 characters.")]
    public string? Address { get; init; }

    [StringLength(1000, ErrorMessage = "The opening hours can have at most 1000 characters.")]
    public string? OpeningHours { get; init; }
}

/// <summary>
/// The restaurant profile as guests see it (<c>GET /api/public/menu/{menuId}/restaurant</c>): no ids, images as URLs of
/// <c>GET /api/images/{id}</c>.
/// </summary>
public record PublicRestaurantProfileDto(
    string Name,
    string? Description,
    string? LogoImageUrl,
    string? CoverImageUrl,
    string? Phone,
    string? Email,
    string? Website,
    string? InstagramUrl,
    string? FacebookUrl,
    string? GoogleReviewUrl,
    string? Address,
    string? OpeningHours);
