using System.ComponentModel.DataAnnotations;
using SpeiseDirekt.Data;

namespace SpeiseDirekt.Model
{
    public class Address
    {
        public Guid Id { get; set; }

        [Required]
        public string ApplicationUserId { get; set; } = string.Empty;
        public ApplicationUser? ApplicationUser { get; set; }

        [StringLength(100)]
        public string? Label { get; set; }

        [Required, StringLength(200)]
        public string Street { get; set; } = string.Empty;

        [StringLength(20)]
        public string? HouseNumber { get; set; }

        [StringLength(20)]
        public string? PostalCode { get; set; }

        [Required, StringLength(100)]
        public string City { get; set; } = string.Empty;

        [StringLength(100)]
        public string? State { get; set; }

        [StringLength(100)]
        public string? Country { get; set; }

        public bool IsDefault { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
