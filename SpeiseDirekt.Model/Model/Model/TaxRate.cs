using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt.Model
{
    public class TaxRate : IAppUserEntity
    {
        public Guid Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Rate as decimal, e.g. 0.2000 for 20%.</summary>
        [Precision(5, 4)]
        public decimal Rate { get; set; }

        public bool IsDefault { get; set; }

        public Guid ApplicationUserId { get; set; }
    }
}
