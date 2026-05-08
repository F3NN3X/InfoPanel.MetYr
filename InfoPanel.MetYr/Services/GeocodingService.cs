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

                NominatimResult[]? results;
                try
                {
                    results = JsonSerializer.Deserialize<NominatimResult[]>(json, _jsonOptions);
                }
                catch (JsonException jex)
                {
                    Console.WriteLine($"Weather Plugin: Nominatim JSON deserialize FAILED: {jex.Message}. Body length: {json.Length}.");
                    return null;
                }

                if (results == null)
                    Console.WriteLine("Weather Plugin: Nominatim deserialize returned NULL.");
                else if (results.Length == 0)
                    Console.WriteLine($"Weather Plugin: Nominatim returned 0 results for '{location}'. Try a more specific query in the INI.");

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
                string failBody = "";
                try { failBody = await response.Content.ReadAsStringAsync(cancellationToken); } catch { }
                Console.WriteLine($"Weather Plugin: Geocoding failed: {(int)response.StatusCode} {response.StatusCode}. Body (first 500 chars): {failBody.Substring(0, Math.Min(failBody.Length, 500))}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Weather Plugin: Error geocoding location '{location}': {ex.Message}");
        }

        return null;
    }
}
