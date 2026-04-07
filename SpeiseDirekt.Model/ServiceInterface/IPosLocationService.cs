using System.Collections.Generic;
using System.Threading.Tasks;

namespace SpeiseDirekt.ServiceInterface;

public class LocationResult
{
    public string DisplayName { get; set; } = "";
    public string Street { get; set; } = "";
    public string HouseNumber { get; set; } = "";
    public string City { get; set; } = "";
    public string PostalCode { get; set; } = "";
    public string Country { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}

public interface IPosLocationService
{
    Task<List<LocationResult>> SearchAsync(string query, string? countryCode = null);
    Task<LocationResult?> ReverseGeocodeAsync(double lat, double lon);
}
