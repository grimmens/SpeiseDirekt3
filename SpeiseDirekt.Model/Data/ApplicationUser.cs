using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using SpeiseDirekt.Model;

namespace SpeiseDirekt.Data
{
    public class ApplicationUser : IdentityUser
    {
        public TenantSubscription? TenantSubscription { get; set; }

        /// <summary>
        /// Null means this user IS the tenant owner. Set for sub-accounts.
        /// </summary>
        public string? TenantOwnerId { get; set; }
        public ApplicationUser? TenantOwner { get; set; }

        public ICollection<TenantUser>? TenantUsers { get; set; }

        public ICollection<Address>? Addresses { get; set; }

        [StringLength(100)]
        public string? FirstName { get; set; }

        [StringLength(100)]
        public string? LastName { get; set; }
    }

}
