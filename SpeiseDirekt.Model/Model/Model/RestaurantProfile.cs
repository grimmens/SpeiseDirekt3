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
        [Required(AllowEmptyStrings = false, ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.Name_RequiredShort))]
        [StringLength(NameMaxLength, ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.Restaurant_NameTooLong))]
        public string Name { get; set; } = string.Empty;

        [StringLength(DescriptionMaxLength, ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.Restaurant_DescriptionTooLong))]
        public string? Description { get; set; }

        /// <summary>Stored like <see cref="MenuItem.ImagePath"/> (<c>image/{id}</c> from <c>IImageUploadService</c>).</summary>
        [StringLength(200, ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.Restaurant_ImagePathTooLong))]
        public string? LogoImagePath { get; set; }

        /// <summary>Wide header image, stored like <see cref="LogoImagePath"/>.</summary>
        [StringLength(200, ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.Restaurant_ImagePathTooLong))]
        public string? CoverImagePath { get; set; }

        [StringLength(40, ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.Restaurant_PhoneTooLong))]
        public string? Phone { get; set; }

        [OptionalEmail]
        [StringLength(200, ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.Restaurant_EmailTooLong))]
        public string? Email { get; set; }

        [HttpUrl]
        [StringLength(UrlMaxLength, ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.Restaurant_UrlTooLong))]
        public string? Website { get; set; }

        [HttpUrl]
        [StringLength(UrlMaxLength, ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.Restaurant_UrlTooLong))]
        public string? InstagramUrl { get; set; }

        [HttpUrl]
        [StringLength(UrlMaxLength, ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.Restaurant_UrlTooLong))]
        public string? FacebookUrl { get; set; }

        /// <summary>Link where guests can leave a review (e.g. the Google Maps review link).</summary>
        [HttpUrl]
        [StringLength(UrlMaxLength, ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.Restaurant_UrlTooLong))]
        public string? GoogleReviewUrl { get; set; }

        /// <summary>Postal address, several lines.</summary>
        [StringLength(500, ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.Restaurant_AddressTooLong))]
        public string? Address { get; set; }

        /// <summary>Opening hours as free text, several lines (e.g. "Mo–Fr 11:00–22:00").</summary>
        [StringLength(1000, ErrorMessageResourceType = typeof(ModelText), ErrorMessageResourceName = nameof(ModelText.Restaurant_OpeningHoursTooLong))]
        public string? OpeningHours { get; set; }

        public Guid ApplicationUserId { get; set; }
    }
}
