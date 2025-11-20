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

    public async Task<YrTimeseries?> GetCurrentWeatherAsync(double lat, double lon, CancellationToken cancellationToken)
    {
        string latStr = lat.ToString("0.0000", CultureInfo.InvariantCulture);
        string lonStr = lon.ToString("0.0000", CultureInfo.InvariantCulture);
        string nowcastUrl = $"{MET_NOWCAST_API_URL}?lat={latStr}&lon={lonStr}";
        string forecastUrl = $"{MET_FORECAST_API_URL}?lat={latStr}&lon={lonStr}";

        YrTimeseries? currentTimeseries = null;

        Console.WriteLine($"Weather Plugin: Fetching current weather: {nowcastUrl}");
        var nowcastResponse = await _httpClient.GetAsync(nowcastUrl, cancellationToken);
        if (nowcastResponse.IsSuccessStatusCode)
        {
            var nowcastJson = await nowcastResponse.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine($"Weather Plugin: Nowcast response (first 500 chars): {nowcastJson.Substring(0, Math.Min(nowcastJson.Length, 500))}...");
            var nowcast = JsonSerializer.Deserialize<YrNowcast>(nowcastJson, _jsonOptions);

            if (nowcast?.Properties?.Timeseries?.Length > 0)
            {
                currentTimeseries = nowcast.Properties.Timeseries[0];
                Console.WriteLine($"Weather Plugin: Current timeseries from nowcast: {currentTimeseries.Time}");
            }
            else
            {
                Console.WriteLine("Weather Plugin: No timeseries data in nowcast.");
            }
        }
        else
        {
            Console.WriteLine($"Weather Plugin: Nowcast failed with status: {nowcastResponse.StatusCode}, falling back to forecast.");

            // Try getting current weather from forecast if nowcast fails
            var forecastResponse = await _httpClient.GetAsync(forecastUrl, cancellationToken);
            if (forecastResponse.IsSuccessStatusCode)
            {
                var forecastJson = await forecastResponse.Content.ReadAsStringAsync(cancellationToken);
                var forecast = JsonSerializer.Deserialize<YrForecast>(forecastJson, _jsonOptions);

                if (forecast?.Properties?.Timeseries?.Length > 0)
                {
                    currentTimeseries = forecast.Properties.Timeseries[0];
                    Console.WriteLine($"Weather Plugin: Using forecast timeseries: {currentTimeseries.Time}");
                }
            }
            else
            {
                Console.WriteLine($"Weather Plugin: Forecast API call failed: {forecastResponse.StatusCode}");
            }
        }

        return currentTimeseries;
    }

    public async Task<YrTimeseries[]?> GetForecastAsync(double lat, double lon, CancellationToken cancellationToken)
    {
        string latStr = lat.ToString("0.0000", CultureInfo.InvariantCulture);
        string lonStr = lon.ToString("0.0000", CultureInfo.InvariantCulture);
        string forecastUrl = $"{MET_FORECAST_API_URL}?lat={latStr}&lon={lonStr}";

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
