using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpeiseDirekt.ServiceInterface;

public class AddressValidationResult
{
    public bool IsValid { get; set; }
    public string? ErrorMessage { get; set; }
    public LocationResult? SuggestedAddress { get; set; }
}

public class AddressSuggestion
{
    public string Street { get; set; } = "";
    public string HouseNumber { get; set; } = "";
    public string City { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string DisplayText { get; set; } = "";
}

public interface IAddressValidationService
{
    Task<AddressValidationResult> ValidateAddressAsync(string street, string? houseNumber, string city, string? postalCode);
    Task<List<AddressSuggestion>> GetSuggestionsAsync(string query);
}
