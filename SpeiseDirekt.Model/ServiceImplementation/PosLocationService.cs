using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using SpeiseDirekt.ServiceInterface;

namespace SpeiseDirekt.ServiceImplementation;

public class PosLocationService : IPosLocationService
{
    private readonly HttpClient _httpClient;
    private DateTime _lastRequestTime = DateTime.MinValue;
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromSeconds(1);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public PosLocationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SpeiseDirekt/1.0");
        _httpClient.BaseAddress = new Uri("https://nominatim.openstreetmap.org/");
    }

    public async Task<List<LocationResult>> SearchAsync(string query, string? countryCode = null)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<LocationResult>();

        await EnforceRateLimitAsync();

        var url = $"search?format=json&addressdetails=1&limit=5&q={Uri.EscapeDataString(query)}";
        if (!string.IsNullOrEmpty(countryCode))
            url += $"&countrycodes={Uri.EscapeDataString(countryCode)}";

        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            return new List<LocationResult>();

        var results = await response.Content.ReadFromJsonAsync<List<NominatimSearchResult>>(JsonOptions);
        if (results == null)
            return new List<LocationResult>();

        return results.Select(MapToLocationResult).ToList();
    }

    public async Task<LocationResult?> ReverseGeocodeAsync(double lat, double lon)
    {
        await EnforceRateLimitAsync();

        var latStr = lat.ToString(CultureInfo.InvariantCulture);
        var lonStr = lon.ToString(CultureInfo.InvariantCulture);
        var url = $"reverse?format=json&lat={latStr}&lon={lonStr}";

        var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            return null;

        var result = await response.Content.ReadFromJsonAsync<NominatimSearchResult>(JsonOptions);
        if (result == null)
            return null;

        return MapToLocationResult(result);
    }

    private async Task EnforceRateLimitAsync()
    {
        var elapsed = DateTime.UtcNow - _lastRequestTime;
        if (elapsed < DebounceInterval)
        {
            await Task.Delay(DebounceInterval - elapsed);
        }
        _lastRequestTime = DateTime.UtcNow;
    }

    private static LocationResult MapToLocationResult(NominatimSearchResult result)
    {
        var address = result.Address;
        return new LocationResult
        {
            DisplayName = result.DisplayName ?? "",
            Street = address?.Road ?? "",
            HouseNumber = address?.HouseNumber ?? "",
            City = address?.City ?? address?.Town ?? address?.Village ?? "",
            PostalCode = address?.Postcode ?? "",
            Country = address?.Country ?? "",
            Latitude = double.TryParse(result.Lat, CultureInfo.InvariantCulture, out var lat) ? lat : 0,
            Longitude = double.TryParse(result.Lon, CultureInfo.InvariantCulture, out var lon) ? lon : 0
        };
    }

    private class NominatimSearchResult
    {
        public string? DisplayName { get; set; }
        public string? Lat { get; set; }
        public string? Lon { get; set; }
        public NominatimAddress? Address { get; set; }
    }

    private class NominatimAddress
    {
        public string? Road { get; set; }
        public string? HouseNumber { get; set; }
        public string? City { get; set; }
        public string? Town { get; set; }
        public string? Village { get; set; }
        public string? Postcode { get; set; }
        public string? Country { get; set; }
    }
}
