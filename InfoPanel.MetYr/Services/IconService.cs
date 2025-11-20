using System.Globalization;

namespace InfoPanel.MetYr.Services;

public class IconService
{
    private const string OPENWEATHERMAP_ICON_URL = "https://openweathermap.org/img/wn/";
    private const double LIGHT_PRECIP_THRESHOLD = 2.5;
    private const double HEAVY_PRECIP_THRESHOLD = 7.5;

    private readonly HttpClient _httpClient;
    private string? _validatedExtension;

    public IconService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public string MapYrSymbolToIcon(string? symbolCode, double? precipitationAmount)
    {
        if (string.IsNullOrEmpty(symbolCode))
            return "cloudy";

        double precip = precipitationAmount ?? 0.0;
        bool isLightPrecipitation = precip < LIGHT_PRECIP_THRESHOLD;
        bool isHeavyPrecipitation = precip >= HEAVY_PRECIP_THRESHOLD;

        return symbolCode.ToLower() switch
        {
            "clearsky_day" => "clear-day",
            "clearsky_night" => "clear-night",
            "fair_day" => "cloudy-1-day",
            "fair_night" => "cloudy-1-night",
            "partlycloudy_day" => "cloudy-2-day",
            "partlycloudy_night" => "cloudy-2-night",
            "cloudy" => "cloudy",
            "fog" => "fog",

            // Rain intensity based on precipitation thresholds
            "lightrain" => isLightPrecipitation ? "rainy-1" : "rainy-2",
            "lightrainshowers_day" => isLightPrecipitation ? "rainy-1-day" : "rainy-2-day",
            "lightrainshowers_night" => isLightPrecipitation ? "rainy-1-night" : "rainy-2-night",
            "rain" => isHeavyPrecipitation ? "rainy-3" : "rainy-2",
            "rainshowers_day" => isHeavyPrecipitation ? "rainy-3-day" : "rainy-2-day",
            "rainshowers_night" => isHeavyPrecipitation ? "rainy-3-night" : "rainy-2-night",
            "heavyrain" => "rainy-3",
            "heavyrainshowers_day" => "rainy-3-day",
            "heavyrainshowers_night" => "rainy-3-night",

            // Snow intensity based on precipitation thresholds
            "lightsnow" => isLightPrecipitation ? "snowy-1" : "snowy-2",
            "lightsnowshowers_day" => isLightPrecipitation ? "snowy-1-day" : "snowy-2-day",
            "lightsnowshowers_night" => isLightPrecipitation ? "snowy-1-night" : "snowy-2-night",
            "snow" => isHeavyPrecipitation ? "snowy-3" : "snowy-2",
            "snowshowers_day" => isHeavyPrecipitation ? "snowy-3-day" : "snowy-2-day",
            "snowshowers_night" => isHeavyPrecipitation ? "snowy-3-night" : "snowy-2-night",
            "heavysnow" => "snowy-3",
            "heavysnowshowers_day" => "snowy-3-day",
            "heavysnowshowers_night" => "snowy-3-night",

            // Mixed precipitation types
            "sleet" => "rain-and-sleet-mix",
            "sleetshowers_day" => "rain-and-sleet-mix",
            "sleetshowers_night" => "rain-and-sleet-mix",
            "lightsleet" => "rain-and-sleet-mix",
            "heavysleet" => "rain-and-sleet-mix",

            // Thunder conditions
            "rainandthunder" => "scattered-thunderstorms",
            "lightrainandthunder" => "scattered-thunderstorms",
            "heavyrainandthunder" => "thunderstorms",
            "snowandthunder" => "snow-and-sleet-mix",
            "lightsnowandthunder" => "snow-and-sleet-mix",
            "heavysnowandthunder" => "snow-and-sleet-mix",

            // Severe weather conditions
            "tropicalstorm" => "tropical-storm",
            "hurricane" => "hurricane",

            // Default cases
            _ when symbolCode.Contains("wind") => "wind",
            _ => "cloudy" // Default fallback
        };
    }

    public string MapYrSymbolToDescription(string? symbolCode, double? precipitationAmount)
    {
        if (string.IsNullOrEmpty(symbolCode))
            return "Cloudy";

        double precip = precipitationAmount ?? 0.0;
        bool isLightPrecipitation = precip < LIGHT_PRECIP_THRESHOLD;
        bool isHeavyPrecipitation = precip >= HEAVY_PRECIP_THRESHOLD;

        // Remove day/night suffix for consistent base codes
        string baseCode = symbolCode.ToLower().Replace("_day", "").Replace("_night", "");

        string description = baseCode switch
        {
            // Clear/cloudy conditions
            "clearsky" => "Clear Sky",
            "fair" => "Mostly Clear",
            "partlycloudy" => "Partly Cloudy",
            "cloudy" => "Cloudy",
            "fog" => "Fog",

            // Rain with intensity levels
            "lightrain" => isLightPrecipitation ? "Light Rain" : "Moderate Rain",
            "lightrainshowers" => isLightPrecipitation ? "Light Rain Showers" : "Moderate Rain Showers",
            "rain" => isHeavyPrecipitation ? "Heavy Rain" : "Moderate Rain",
            "rainshowers" => isHeavyPrecipitation ? "Heavy Rain Showers" : "Moderate Rain Showers",
            "heavyrain" => "Heavy Rain",
            "heavyrainshowers" => "Heavy Rain Showers",

            // Snow with intensity levels
            "lightsnow" => isLightPrecipitation ? "Light Snow" : "Moderate Snow",
            "lightsnowshowers" => isLightPrecipitation ? "Light Snow Showers" : "Moderate Snow Showers",
            "snow" => isHeavyPrecipitation ? "Heavy Snow" : "Moderate Snow",
            "snowshowers" => isHeavyPrecipitation ? "Heavy Snow Showers" : "Moderate Snow Showers",
            "heavysnow" => "Heavy Snow",
            "heavysnowshowers" => "Heavy Snow Showers",

            // Mixed precipitation
            "sleet" => "Sleet",
            "sleetshowers" => "Sleet Showers",
            "lightsleet" => "Light Sleet",
            "heavysleet" => "Heavy Sleet",

            // Thunder conditions
            "rainandthunder" => "Rain with Thunder",
            "lightrainandthunder" => "Light Rain with Thunder",
            "heavyrainandthunder" => "Heavy Rain with Thunder",
            "snowandthunder" => "Snow with Thunder",
            "lightsnowandthunder" => "Light Snow with Thunder",
            "heavysnowandthunder" => "Heavy Snow with Thunder",

            // Severe weather
            "tropicalstorm" => "Tropical Storm",
            "hurricane" => "Hurricane",

            // Default case
            _ when baseCode.Contains("wind") => "Windy",
            _ => "Cloudy" // Default fallback
        };

        // Apply title case for consistent formatting
        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(description.ToLower());
    }

    public async Task<string?> ValidateIconUrlAsync(string? iconUrl)
    {
        Console.WriteLine($"Weather Plugin: Validating IconUrl: '{iconUrl}'");
        if (string.IsNullOrEmpty(iconUrl))
        {
            Console.WriteLine("Weather Plugin: IconUrl is empty or not set, using default OpenWeatherMap icons.");
            return null;
        }

        if (!Uri.IsWellFormedUriString(iconUrl, UriKind.Absolute))
        {
            Console.WriteLine($"Weather Plugin: Invalid IconUrl '{iconUrl}', defaulting to OpenWeatherMap icons.");
            return null;
        }

        Console.WriteLine($"Weather Plugin: Testing custom IconUrl: {iconUrl}");
        try
        {
            string testSvgUrl = $"{iconUrl.TrimEnd('/')}/clear-day.svg";
            var testResponse = await _httpClient.GetAsync(testSvgUrl);
            if (testResponse.IsSuccessStatusCode)
            {
                Console.WriteLine($"Weather Plugin: Successfully accessed test SVG icon: {testSvgUrl}");
                _validatedExtension = ".svg";
                return iconUrl; // Valid
            }
            else
            {
                Console.WriteLine($"Weather Plugin: Test SVG icon not found at {testSvgUrl}, status: {testResponse.StatusCode}");
                string testPngUrl = $"{iconUrl.TrimEnd('/')}/clear-day.png";
                var pngResponse = await _httpClient.GetAsync(testPngUrl);
                if (pngResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Weather Plugin: Successfully accessed test PNG icon: {testPngUrl}");
                    _validatedExtension = ".png";
                    return iconUrl; // Valid
                }
                else
                {
                    Console.WriteLine($"Weather Plugin: Test PNG icon not found either, defaulting to OpenWeatherMap icons.");
                    _validatedExtension = null;
                    return null;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Weather Plugin: Error accessing test icon URL '{iconUrl}': {ex.Message}, defaulting to OpenWeatherMap icons.");
            return null;
        }
    }

    public async Task<string> GetIconUrlAsync(string? customIconUrl, string mappedIcon)
    {
        if (!string.IsNullOrEmpty(customIconUrl))
        {
            // If we have a validated extension, use it directly without network check
            if (!string.IsNullOrEmpty(_validatedExtension))
            {
                string url = $"{customIconUrl.TrimEnd('/')}/{mappedIcon}{_validatedExtension}";
                Console.WriteLine($"Weather Plugin: Using cached custom icon URL: {url}");
                return url;
            }

            Console.WriteLine($"Weather Plugin: Using custom icon URL: {customIconUrl}");
            string iconFileName = mappedIcon; // Already hyphenated from mapping

            string svgUrl = $"{customIconUrl.TrimEnd('/')}/{iconFileName}.svg";
            string pngUrl = $"{customIconUrl.TrimEnd('/')}/{iconFileName}.png";

            try
            {
                Console.WriteLine($"Weather Plugin: Checking SVG: {svgUrl}");
                var svgResponse = await _httpClient.GetAsync(svgUrl);

                if (svgResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Weather Plugin: Using SVG: {svgUrl}");
                    _validatedExtension = ".svg"; // Cache for future
                    return svgUrl;
                }
                else
                {
                    Console.WriteLine($"Weather Plugin: SVG not found, checking PNG: {pngUrl}");
                    var pngResponse = await _httpClient.GetAsync(pngUrl);

                    if (pngResponse.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Weather Plugin: Using PNG: {pngUrl}");
                        _validatedExtension = ".png"; // Cache for future
                        return pngUrl;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Weather Plugin: Error checking icon '{svgUrl}': {ex.Message}, using OpenWeatherMap.");
            }
        }

        Console.WriteLine("Weather Plugin: No custom IconUrl or check failed, using OpenWeatherMap icons.");
        return GetDefaultOpenWeatherMapIcon(mappedIcon);
    }

    private string GetDefaultOpenWeatherMapIcon(string mappedIcon)
    {
        // Map our internal icon codes to OpenWeatherMap's icon code system
        string iconCode = mappedIcon switch
        {
            // Clear sky
            "clear-day" => "01d",
            "clear-night" => "01n",

            // Few clouds / light clouds
            "cloudy-1-day" => "02d",
            "cloudy-1-night" => "02n",

            // Partly cloudy
            "cloudy-2-day" => "03d",
            "cloudy-2-night" => "03n",

            // Cloudy
            "cloudy" => "04d",

            // Fog
            "fog" => "50d",

            // Rain (all levels map to the same base code, with d/n variations)
            "rainy-1" or "rainy-2" or "rainy-3" => "10d",
            "rainy-1-day" or "rainy-2-day" or "rainy-3-day" => "10d",
            "rainy-1-night" or "rainy-2-night" or "rainy-3-night" => "10n",

            // Snow (all levels map to the same base code)
            "snowy-1" or "snowy-2" or "snowy-3" => "13d",
            "snowy-1-day" or "snowy-2-day" or "snowy-3-day" => "13d",
            "snowy-1-night" or "snowy-2-night" or "snowy-3-night" => "13n",

            // Mixed precipitation
            "rain-and-sleet-mix" => "13d",
            "snow-and-sleet-mix" => "13d",

            // Thunderstorms
            "scattered-thunderstorms" or "scattered-thunderstorms-day" => "11d",
            "scattered-thunderstorms-night" => "11n",
            "thunderstorms" => "11d",

            // Severe weather
            "tropical-storm" => "11d",
            "hurricane" => "11d",

            // Wind and fallbacks
            "wind" => "04d",
            _ => "04d" // Default to cloudy
        };

        // Use OpenWeatherMap's @4x for high-resolution icons (usually 400x400px)
        string url = $"{OPENWEATHERMAP_ICON_URL}{iconCode}@4x.png";
        Console.WriteLine($"Weather Plugin: Using OpenWeatherMap icon: {url}");
        return url;
    }
}
