using System.Globalization;
using System.Text.Json;
using InfoPanel.MetYr.Models;

namespace InfoPanel.MetYr.Services;

public class GeocodingService
{
    private const string NOMINATIM_API_URL = "https://nominatim.openstreetmap.org/search";
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public GeocodingService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<(double Lat, double Lon)?> GetCoordinatesAsync(string location, CancellationToken cancellationToken)
    {
        try
        {
            string nominatimUrl = $"{NOMINATIM_API_URL}?q={Uri.EscapeDataString(location)}&format=json&limit=1";
            Console.WriteLine($"Weather Plugin: Geocoding URL: {nominatimUrl}");

            var response = await _httpClient.GetAsync(nominatimUrl, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"Weather Plugin: Nominatim response: {json}");

                var results = JsonSerializer.Deserialize<NominatimResult[]>(json, _jsonOptions);

                if (results?.Length > 0 && !string.IsNullOrEmpty(results[0].Lat) && !string.IsNullOrEmpty(results[0].Lon))
                {
                    if (double.TryParse(results[0].Lat, NumberStyles.Any, CultureInfo.InvariantCulture, out double lat) &&
                        double.TryParse(results[0].Lon, NumberStyles.Any, CultureInfo.InvariantCulture, out double lon))
                    {
                        return (lat, lon);
                    }
                }
            }
            else
            {
                Console.WriteLine($"Weather Plugin: Geocoding failed with status: {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Weather Plugin: Error geocoding location '{location}': {ex.Message}");
        }

        return null;
    }
}
