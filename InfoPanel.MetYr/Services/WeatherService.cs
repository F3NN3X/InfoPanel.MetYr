using System.Globalization;
using System.Text.Json;
using InfoPanel.MetYr.Models;

namespace InfoPanel.MetYr.Services;

public class WeatherService
{
    private const string MET_NOWCAST_API_URL = "https://api.met.no/weatherapi/nowcast/2.0/complete";
    private const string MET_FORECAST_API_URL = "https://api.met.no/weatherapi/locationforecast/2.0/complete";

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public WeatherService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<YrTimeseries?> GetCurrentWeatherAsync(double lat, double lon, int? altitude, CancellationToken cancellationToken)
    {
        string latStr = lat.ToString("0.0000", CultureInfo.InvariantCulture);
        string lonStr = lon.ToString("0.0000", CultureInfo.InvariantCulture);

        // Build URL with optional altitude parameter for accurate temperature
        string altitudeParam = altitude.HasValue ? $"&altitude={altitude.Value}" : "";
        string forecastUrl = $"{MET_FORECAST_API_URL}?lat={latStr}&lon={lonStr}{altitudeParam}";

        YrTimeseries? currentTimeseries = null;

        // Use Forecast API for current weather because:
        // 1. It supports altitude parameter for accurate temperature correction
        // 2. It includes next_1_hours data with symbol_code for weather icons
        // Note: Nowcast has more frequent updates but doesn't support altitude or weather symbols
        Console.WriteLine($"Weather Plugin: Fetching current weather: {forecastUrl}");
        if (altitude.HasValue)
        {
            Console.WriteLine($"Weather Plugin: Using altitude {altitude.Value}m for temperature correction");
        }

        var forecastResponse = await _httpClient.GetAsync(forecastUrl, cancellationToken);
        if (forecastResponse.IsSuccessStatusCode)
        {
            var forecastJson = await forecastResponse.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"Weather Plugin: Forecast response (first 500 chars): {forecastJson.Substring(0, Math.Min(forecastJson.Length, 500))}...");
            var forecast = JsonSerializer.Deserialize<YrForecast>(forecastJson, _jsonOptions);

            if (forecast?.Properties?.Timeseries?.Length > 0)
            {
                // Find the timeseries entry closest to now
                var now = DateTime.UtcNow;
                currentTimeseries = forecast.Properties.Timeseries
                    .Where(t => DateTime.TryParse(t.Time, out _))
                    .OrderBy(t => Math.Abs((DateTime.Parse(t.Time!) - now).TotalMinutes))
                    .FirstOrDefault() ?? forecast.Properties.Timeseries[0];

                Console.WriteLine($"Weather Plugin: Current timeseries from forecast: {currentTimeseries.Time}");
            }
            else
            {
                Console.WriteLine("Weather Plugin: No timeseries data in forecast.");
            }
        }
        else
        {
            Console.WriteLine($"Weather Plugin: Forecast API call failed: {forecastResponse.StatusCode}");
        }

        return currentTimeseries;
    }

    public async Task<YrTimeseries[]?> GetForecastAsync(double lat, double lon, int? altitude, CancellationToken cancellationToken)
    {
        string latStr = lat.ToString("0.0000", CultureInfo.InvariantCulture);
        string lonStr = lon.ToString("0.0000", CultureInfo.InvariantCulture);

        // Include altitude parameter for accurate temperature forecasts
        string altitudeParam = altitude.HasValue ? $"&altitude={altitude.Value}" : "";
        string forecastUrl = $"{MET_FORECAST_API_URL}?lat={latStr}&lon={lonStr}{altitudeParam}";

        Console.WriteLine($"Weather Plugin: Fetching forecast: {forecastUrl}");
        var forecastResponse = await _httpClient.GetAsync(forecastUrl, cancellationToken);
        if (forecastResponse.IsSuccessStatusCode)
        {
            var forecastJson = await forecastResponse.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"Weather Plugin: Forecast response (first 500 chars): {forecastJson.Substring(0, Math.Min(forecastJson.Length, 500))}...");
            var forecast = JsonSerializer.Deserialize<YrForecast>(forecastJson, _jsonOptions);

            if (forecast?.Properties?.Timeseries?.Length > 0)
            {
                return forecast.Properties.Timeseries;
            }
            else
            {
                Console.WriteLine("Weather Plugin: No timeseries data in forecast.");
            }
        }
        else
        {
            Console.WriteLine($"Weather Plugin: Forecast failed: {forecastResponse.StatusCode}");
        }

        return null;
    }
}
