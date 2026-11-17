using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt.Model
{
    /// <summary>
    /// The restaurant behind a tenant's menus (one per tenant): name, description, logo and cover image, contact
    /// details, links and opening hours. Guests see it on the public menu pages of every menu of the tenant
    /// (owner = <see cref="Menu.ApplicationUserId"/>). All fields except <see cref="Name"/> are optional; empty
    /// strings are stored as <c>null</c> (see <c>RestaurantProfileService.Normalize</c>).
    /// </summary>
    public class RestaurantProfile : IAppUserEntity
    {
        public const int NameMaxLength = 100;
        public const int DescriptionMaxLength = 1000;
        public const int UrlMaxLength = 500;

        public Guid Id { get; set; }

        /// <summary>Display name of the restaurant, e.g. "Gasthaus Zur Post".</summary>
        [Required(AllowEmptyStrings = false, ErrorMessage = "Name is required.")]
        [StringLength(NameMaxLength, ErrorMessage = "The name can have at most 100 characters.")]
        public string Name { get; set; } = string.Empty;

        [StringLength(DescriptionMaxLength, ErrorMessage = "The description can have at most 1000 characters.")]
        public string? Description { get; set; }

        /// <summary>Stored like <see cref="MenuItem.ImagePath"/> (<c>image/{id}</c> from <c>IImageUploadService</c>).</summary>
        [StringLength(200)]
        public string? LogoImagePath { get; set; }

        /// <summary>Wide header image, stored like <see cref="LogoImagePath"/>.</summary>
        [StringLength(200)]
        public string? CoverImagePath { get; set; }

        [StringLength(40, ErrorMessage = "The phone number can have at most 40 characters.")]
        public string? Phone { get; set; }

        [OptionalEmail]
        [StringLength(200, ErrorMessage = "The e-mail address can have at most 200 characters.")]
        public string? Email { get; set; }

        [HttpUrl]
        [StringLength(UrlMaxLength, ErrorMessage = "The web address can have at most 500 characters.")]
        public string? Website { get; set; }

        [HttpUrl]
        [StringLength(UrlMaxLength, ErrorMessage = "The web address can have at most 500 characters.")]
        public string? InstagramUrl { get; set; }

        [HttpUrl]
        [StringLength(UrlMaxLength, ErrorMessage = "The web address can have at most 500 characters.")]
        public string? FacebookUrl { get; set; }

        /// <summary>Link where guests can leave a review (e.g. the Google Maps review link).</summary>
        [HttpUrl]
        [StringLength(UrlMaxLength, ErrorMessage = "The web address can have at most 500 characters.")]
        public string? GoogleReviewUrl { get; set; }

        /// <summary>Postal address, several lines.</summary>
        [StringLength(500, ErrorMessage = "The address can have at most 500 characters.")]
        public string? Address { get; set; }

        /// <summary>Opening hours as free text, several lines (e.g. "Mo–Fr 11:00–22:00").</summary>
        [StringLength(1000, ErrorMessage = "The opening hours can have at most 1000 characters.")]
        public string? OpeningHours { get; set; }

        public Guid ApplicationUserId { get; set; }
    }
}
