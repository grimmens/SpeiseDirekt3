using System.ComponentModel.DataAnnotations;

namespace SpeiseDirekt3.Components.POS.Account;

public class AddressFormModel
{
    [StringLength(100)]
    public string? Label { get; set; }

    [Required(ErrorMessage = "Bitte gib die Straße ein.")]
    [StringLength(200)]
    public string Street { get; set; } = string.Empty;

    [StringLength(20)]
    public string? HouseNumber { get; set; }

    [StringLength(20)]
    public string? PostalCode { get; set; }

    [Required(ErrorMessage = "Bitte gib den Ort ein.")]
    [StringLength(100)]
    public string City { get; set; } = string.Empty;

    [StringLength(100)]
    public string? State { get; set; }

    [StringLength(100)]
    public string? Country { get; set; }

    public bool IsDefault { get; set; }
}
