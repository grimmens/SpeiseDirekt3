using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation;

public class AddressValidationService : IAddressValidationService
{
    private readonly IPosLocationService _locationService;

    public AddressValidationService(IPosLocationService locationService)
    {
        _locationService = locationService;
    }

    public async Task<AddressValidationResult> ValidateAddressAsync(string street, string? houseNumber, string city, string? postalCode)
    {
        var queryParts = new List<string> { street };
        if (!string.IsNullOrWhiteSpace(houseNumber))
            queryParts[0] = $"{street} {houseNumber}";
        if (!string.IsNullOrWhiteSpace(postalCode))
            queryParts.Add(postalCode);
        queryParts.Add(city);

        var query = string.Join(", ", queryParts);
        var results = await _locationService.SearchAsync(query, "at");

        if (results.Count == 0)
        {
            return new AddressValidationResult
            {
                IsValid = false,
                ErrorMessage = "Die eingegebene Adresse konnte nicht verifiziert werden."
            };
        }

        // Check for a close match: same postal code and non-empty street
        var exactMatch = results.FirstOrDefault(r =>
            !string.IsNullOrWhiteSpace(r.Street) &&
            !string.IsNullOrWhiteSpace(r.PostalCode) &&
            r.PostalCode == postalCode);

        if (exactMatch != null)
        {
            // Check if the result differs from input (suggest correction)
            var streetMatches = string.Equals(exactMatch.Street, street, StringComparison.OrdinalIgnoreCase);
            var houseMatches = string.Equals(exactMatch.HouseNumber ?? "", houseNumber ?? "", StringComparison.OrdinalIgnoreCase);
            var cityMatches = string.Equals(exactMatch.City, city, StringComparison.OrdinalIgnoreCase);

            if (!streetMatches || !houseMatches || !cityMatches)
            {
                return new AddressValidationResult
                {
                    IsValid = true,
                    SuggestedAddress = exactMatch
                };
            }

            return new AddressValidationResult { IsValid = true };
        }

        // No exact postal code match — check if any result has a non-empty street (loose match)
        var looseMatch = results.FirstOrDefault(r => !string.IsNullOrWhiteSpace(r.Street));
        if (looseMatch != null)
        {
            return new AddressValidationResult
            {
                IsValid = true,
                SuggestedAddress = looseMatch
            };
        }

        return new AddressValidationResult
        {
            IsValid = false,
            ErrorMessage = "Die eingegebene Adresse konnte nicht verifiziert werden."
        };
    }

    public async Task<List<AddressSuggestion>> GetSuggestionsAsync(string query)
    {
        var results = await _locationService.SearchAsync(query, "at");

        return results
            .Where(r => !string.IsNullOrWhiteSpace(r.Street))
            .Select(r => new AddressSuggestion
            {
                Street = r.Street,
                HouseNumber = r.HouseNumber,
                City = r.City,
                PostalCode = r.PostalCode,
                DisplayText = FormatDisplayText(r)
            })
            .ToList();
    }

    private static string FormatDisplayText(LocationResult r)
    {
        var streetPart = string.IsNullOrWhiteSpace(r.HouseNumber)
            ? r.Street
            : $"{r.Street} {r.HouseNumber}";
        var cityPart = string.IsNullOrWhiteSpace(r.PostalCode)
            ? r.City
            : $"{r.PostalCode} {r.City}";
        return $"{streetPart}, {cityPart}";
    }
}
