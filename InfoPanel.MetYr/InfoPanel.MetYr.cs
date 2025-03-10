using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using InfoPanel.Plugins;
using IniParser;
using IniParser.Model;
using System.Data;

/*
 * Plugin: InfoPanel.MetYr
 * Version: 1.1.0
 * Author: F3NN3X
 * Description: An InfoPanel plugin for retrieving weather data from MET Norway's Yr API (api.met.no). Provides current weather conditions (temperature, wind, precipitation, etc.) and a 5-day forecast table. Supports configurable locations via an INI file, with automatic geocoding using Nominatim. Updates hourly by default, with robust null safety and detailed logging.
 * Changelog (Recent):
 *   - v1.1.0 (Mar 10, 2025): Enhanced forecast reliability and null safety.
 *     - Switched 5-day forecast weather to use next_6_hours data for consistent symbol codes.
 *     - Added null checks and DateTime.TryParse in BuildForecastTable to resolve CS8604/CS8602 warnings.
 *     - Default location updated to Oslo, Norway.
 *   - v1.0.0 (Initial Release): Basic weather and forecast functionality.
 * Note: Full history in CHANGELOG.md. Requires internet access for API calls.
 */

namespace InfoPanel.Extras
{
    public class YrWeatherPlugin : BasePlugin
    {
        // Shared HTTP client for API requests, initialized with a custom User-Agent
        private static readonly HttpClient _httpClient = new HttpClient();
        
        // Location coordinates and metadata
        private double _latitude;              // Latitude of the weather location
        private double _longitude;             // Longitude of the weather location
        private string? _location;             // Human-readable location name (e.g., "Oslo, Norway")
        private bool _coordinatesSet = false;  // Flag to track if coordinates have been initialized
        
        // Update timing
        private int _refreshIntervalMinutes = 60;   // Default refresh interval (1 hour)
        private DateTime _lastUpdateTime = DateTime.MinValue; // Last update timestamp

        // Plugin data fields for current weather
        private readonly PluginText _name = new("name", "Name", "-");                   // Location name
        private readonly PluginText _weather = new("weather", "Weather", "-");         // Current weather condition
        private readonly PluginText _weatherDesc = new("weather_desc", "Weather Description", "-"); // Weather with day/night detail
        private readonly PluginText _weatherIcon = new("weather_icon", "Weather Icon", "-");       // Weather icon code
        private readonly PluginText _weatherIconUrl = new("weather_icon_url", "Weather Icon URL", "-"); // URL to weather icon

        private readonly PluginSensor _temp = new("temp", "Temperature", 0, "°C");         // Current temperature
        private readonly PluginSensor _pressure = new("pressure", "Pressure", 0, "hPa");   // Air pressure
        private readonly PluginSensor _seaLevel = new("sea_level", "Sea Level Pressure", 0, "hPa"); // Sea-level pressure
        private readonly PluginSensor _feelsLike = new("feels_like", "Feels Like", 0, "°C"); // Feels-like temperature
        private readonly PluginSensor _humidity = new("humidity", "Humidity", 0, "%");      // Relative humidity

        private readonly PluginSensor _windSpeed = new("wind_speed", "Wind Speed", 0, "m/s"); // Wind speed
        private readonly PluginSensor _windDeg = new("wind_deg", "Wind Degree", 0, "°");     // Wind direction in degrees
        private readonly PluginSensor _windGust = new("wind_gust", "Wind Gust", 0, "m/s");   // Wind gust speed

        private readonly PluginSensor _clouds = new("clouds", "Clouds", 0, "%");   // Cloud cover percentage
        private readonly PluginSensor _rain = new("rain", "Rain", 0, "mm/h");      // Rain precipitation rate
        private readonly PluginSensor _snow = new("snow", "Snow", 0, "mm/h");      // Snow precipitation rate

        // Forecast table configuration
        private static readonly string _forecastTableFormat = "0:150|1:100|2:80|3:60|4:100"; // Column widths for Date|Weather|Temp|Precip|Wind
        private readonly PluginTable _forecastTable = new("5-Day Forecast", new DataTable(), _forecastTableFormat); // 5-day forecast table

        // Constructor: Initializes the plugin with a unique ID, name, and description
        public YrWeatherPlugin()
            : base(
                "yr-weather-plugin",
                "Weather Info - MET/Yr",
                "Retrieves current weather information and forecasts from api.met.no."
            )
        {
            // Set User-Agent for MET/Yr API compliance
            _httpClient.DefaultRequestHeaders.Add(
                "User-Agent",
                "InfoPanel-YrWeatherPlugin/1.1.0 (contact@example.com)"
            );
        }

        private string? _configFilePath = null; // Path to the INI config file
        public override string? ConfigFilePath => _configFilePath; // Expose config file path
        public override TimeSpan UpdateInterval => TimeSpan.FromMinutes(_refreshIntervalMinutes); // Dynamic update interval

        // Initialize: Sets up the config file and reads initial settings
        public override void Initialize()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            _configFilePath = $"{assembly.ManifestModule.FullyQualifiedName}.ini"; // Config file tied to assembly

            var parser = new FileIniDataParser();
            IniData config;
            if (!File.Exists(_configFilePath))
            {
                // Create default config if none exists
                config = new IniData();
                config["Yr Weather Plugin"]["Location"] = "Oslo, Norway"; // Default location
                config["Yr Weather Plugin"]["RefreshIntervalMinutes"] = "60";
                parser.WriteFile(_configFilePath, config);
                _location = "Oslo, Norway";
                _refreshIntervalMinutes = 60;
            }
            else
            {
                // Read existing config
                config = parser.ReadFile(_configFilePath);
                _location = config["Yr Weather Plugin"]["Location"] ?? "Oslo, Norway";
                if (!int.TryParse(config["Yr Weather Plugin"]["RefreshIntervalMinutes"], out _refreshIntervalMinutes) || _refreshIntervalMinutes <= 0)
                {
                    _refreshIntervalMinutes = 60; // Fallback to 1 hour if invalid
                }
            }

            Console.WriteLine($"Weather Plugin: Read location from INI: {_location}");
            Console.WriteLine($"Weather Plugin: Refresh interval set to: {_refreshIntervalMinutes} minutes");
        }

        // Close: Cleanup method (currently empty)
        public override void Close()
        {
        }

        // Load: Populates the plugin container with data fields
        public override void Load(List<IPluginContainer> containers)
        {
            var container = new PluginContainer(_location ?? $"Lat:{_latitude}, Lon:{_longitude}");
            container.Entries.AddRange(
                [_name, _weather, _weatherDesc, _weatherIcon, _weatherIconUrl]
            );
            container.Entries.AddRange(
                [
                    _temp,
                    _pressure,
                    _seaLevel,
                    _feelsLike,
                    _humidity,
                    _windSpeed,
                    _windDeg,
                    _windGust,
                    _clouds,
                    _rain,
                    _snow,
                    _forecastTable
                ]
            );
            containers.Add(container);
        }

        // PluginAction: Opens the MET/Yr API documentation in a browser
        [PluginAction("Visit MET/Yr API Docs")]
        public void LaunchApiUrl()
        {
            try
            {
                Process.Start(
                    new ProcessStartInfo
                    {
                        FileName = "https://developer.yr.no/doc/GettingStarted/",
                        UseShellExecute = true,
                    }
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error launching URL: {ex.Message}");
            }
        }

        // Update: Synchronous update not implemented (async preferred)
        public override void Update()
        {
            throw new NotImplementedException();
        }

        // UpdateAsync: Main async update method for fetching weather data
        public override async Task UpdateAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("Weather Plugin: UpdateAsync cancelled before starting.");
                return;
            }

            var now = DateTime.UtcNow;
            Console.WriteLine($"Weather Plugin: UpdateAsync called at {now:yyyy-MM-ddTHH:mm:ssZ}");
            if (_lastUpdateTime != DateTime.MinValue)
            {
                var timeSinceLast = now - _lastUpdateTime;
                Console.WriteLine($"Weather Plugin: Time since last update: {timeSinceLast.TotalMinutes:F2} minutes");
            }

            if (!_coordinatesSet)
            {
                await SetCoordinatesFromLocation(_location, cancellationToken); // Set lat/lon from location
                _coordinatesSet = true;
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                await GetWeather(cancellationToken); // Fetch and process weather data
                _lastUpdateTime = DateTime.UtcNow;
            }
            else
            {
                Console.WriteLine("Weather Plugin: Weather fetch cancelled.");
            }
        }

        // SetCoordinatesFromLocation: Geocodes the location using Nominatim
        private async Task SetCoordinatesFromLocation(string? location, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(location))
            {
                Console.WriteLine("Weather Plugin: Location is empty, using fallback coordinates.");
                _latitude = 1.3521; // Default to Singapore as fallback
                _longitude = 103.8198;
                return;
            }

            try
            {
                string nominatimUrl = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(location)}&format=json&limit=1";
                Console.WriteLine($"Weather Plugin: Geocoding URL: {nominatimUrl}");
                var response = await _httpClient.GetAsync(nominatimUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    Console.WriteLine($"Weather Plugin: Nominatim response: {json}");
                    var results = JsonSerializer.Deserialize<NominatimResult[]>(json);
                    if (results?.Length > 0)
                    {
                        try
                        {
                            _latitude = double.Parse(results[0].Lat, CultureInfo.InvariantCulture);
                            _longitude = double.Parse(results[0].Lon, CultureInfo.InvariantCulture);
                            Console.WriteLine(
                                $"Weather Plugin: Coordinates set to Lat: {_latitude.ToString(CultureInfo.InvariantCulture)}, Lon: {_longitude.ToString(CultureInfo.InvariantCulture)}"
                            );
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Weather Plugin: Error parsing coordinates - Lat: {results[0].Lat}, Lon: {results[0].Lon}, Error: {ex.Message}");
                            _latitude = 1.3521;
                            _longitude = 103.8198;
                        }
                    }
                    else
                    {
                        Console.WriteLine("Weather Plugin: No geocoding results found, using fallback.");
                        _latitude = 1.3521;
                        _longitude = 103.8198;
                    }
                }
                else
                {
                    Console.WriteLine($"Weather Plugin: Geocoding failed with status: {response.StatusCode}");
                    _latitude = 1.3521;
                    _longitude = 103.8198;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Weather Plugin: Error geocoding location '{location}': {ex.Message}");
                _latitude = 1.3521;
                _longitude = 103.8198;
            }
        }

        // GetWeather: Fetches and processes weather data from MET/Yr API
        private async Task GetWeather(CancellationToken cancellationToken)
        {
            try
            {
                string latStr = _latitude.ToString("0.0000", CultureInfo.InvariantCulture);
                string lonStr = _longitude.ToString("0.0000", CultureInfo.InvariantCulture);
                string url = $"https://api.met.no/weatherapi/locationforecast/2.0/complete?lat={latStr}&lon={lonStr}";
                Console.WriteLine($"Weather Plugin: Fetching weather from: {url}");
                var response = await _httpClient.GetAsync(url, cancellationToken);

                Console.WriteLine($"Weather Plugin: MET/Yr response status: {response.StatusCode}");
                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Weather Plugin: MET/Yr request failed with status: {response.StatusCode}");
                    return;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                Console.WriteLine($"Weather Plugin: MET/Yr response (first 500 chars): {json.Substring(0, Math.Min(json.Length, 500))}...");
                var forecast = JsonSerializer.Deserialize<YrForecast>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (forecast?.Properties?.Timeseries?.Length > 0)
                {
                    // Process current weather from the first timeseries entry
                    var current = forecast.Properties.Timeseries[0];
                    Console.WriteLine($"Weather Plugin: Selected timeseries time: {current.Time}");
                    Console.WriteLine($"Weather Plugin: Raw timeseries JSON: {JsonSerializer.Serialize(current)}");

                    var instant = current.Data?.Instant;
                    if (instant?.Details == null)
                    {
                        Console.WriteLine("Weather Plugin: No instant details available in response.");
                        return;
                    }

                    var details = instant.Details;
                    var next1Hour = current.Data?.Next1Hours;

                    Console.WriteLine("Weather Plugin: Parsing current weather data...");
                    Console.WriteLine($"Weather Plugin: Raw JSON instant details: {JsonSerializer.Serialize(details)}");

                    _name.Value = _location ?? $"Lat:{_latitude.ToString(CultureInfo.InvariantCulture)}, Lon:{_longitude.ToString(CultureInfo.InvariantCulture)}";
                    _weather.Value = next1Hour?.Summary?.SymbolCode?.Split('_')[0] ?? "-";
                    _weatherDesc.Value = next1Hour?.Summary?.SymbolCode?.Replace("_", " ") ?? "-";
                    _weatherIcon.Value = next1Hour?.Summary?.SymbolCode ?? "-";

                    string potentialIconUrl = next1Hour?.Summary?.SymbolCode != null
                        ? $"https://raw.githubusercontent.com/metno/weathericons/main/weather/png/{next1Hour.Summary.SymbolCode}.png"
                        : "-";
                    _weatherIconUrl.Value = await ValidateIconUrl(potentialIconUrl) ? potentialIconUrl : "-";

                    _temp.Value = (float)details.AirTemperature;
                    _pressure.Value = (float)details.AirPressureAtSeaLevel;
                    _seaLevel.Value = (float)details.AirPressureAtSeaLevel;
                    _feelsLike.Value = (float)CalculateFeelsLike(details.AirTemperature, details.WindSpeed, details.RelativeHumidity);
                    Console.WriteLine($"Weather Plugin: FeelsLike raw value: {_feelsLike.Value.ToString(CultureInfo.InvariantCulture)}");
                    _humidity.Value = (float)details.RelativeHumidity;

                    _windSpeed.Value = (float)details.WindSpeed;
                    _windDeg.Value = (float)details.WindFromDirection;
                    _windGust.Value = (float)(details.WindSpeedOfGust ?? details.WindSpeed);

                    _clouds.Value = (float)details.CloudAreaFraction;
                    _rain.Value = (float)(next1Hour?.Details?.PrecipitationAmount ?? 0);
                    _snow.Value = next1Hour?.Details?.PrecipitationCategory == "snow" ? (float)(next1Hour.Details.PrecipitationAmount) : 0;

                    Console.WriteLine($"Weather Plugin: Data set - Temp: {_temp.Value.ToString(CultureInfo.InvariantCulture)}, FeelsLike: {_feelsLike.Value.ToString(CultureInfo.InvariantCulture)}, Weather: {_weather.Value}");

                    // Build and set the 5-day forecast table
                    _forecastTable.Value = BuildForecastTable(forecast.Properties.Timeseries);
                }
                else
                {
                    Console.WriteLine("Weather Plugin: No timeseries data in response.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Weather Plugin: Error fetching weather data: {ex.Message}");
            }
        }

        // BuildForecastTable: Creates a 5-day forecast table from timeseries data
        private DataTable BuildForecastTable(YrTimeseries[] timeseries)
        {
            var dataTable = new DataTable();
            dataTable.Columns.Add("Date", typeof(PluginText));    // Day of the forecast
            dataTable.Columns.Add("Weather", typeof(PluginText)); // Most frequent weather condition
            dataTable.Columns.Add("Temp", typeof(PluginText));    // Min/max temperature
            dataTable.Columns.Add("Precip", typeof(PluginSensor)); // Total precipitation
            dataTable.Columns.Add("Wind", typeof(PluginText));    // Average wind speed and direction

            // Group timeseries into daily blocks for the next 5 days
            var now = DateTime.UtcNow.Date; // Start from midnight today
            var endTime = now.AddDays(5);
            var dailyBlocks = new Dictionary<DateTime, List<YrTimeseries>>();

            foreach (var ts in timeseries)
            {
                // Safely parse timestamp, skipping invalid entries
                if (ts == null || ts.Time == null || !DateTime.TryParse(ts.Time, out var tsTime))
                {
                    Console.WriteLine($"Weather Plugin: Skipping invalid timeseries entry with null or unparsable time: {ts?.Time}");
                    continue;
                }
                var tsDate = tsTime.Date;
                if (tsDate >= now && tsDate < endTime)
                {
                    if (!dailyBlocks.ContainsKey(tsDate))
                        dailyBlocks[tsDate] = new List<YrTimeseries>();
                    dailyBlocks[tsDate].Add(ts);
                }
            }

            foreach (var day in dailyBlocks.OrderBy(d => d.Key))
            {
                var blockData = day.Value;
                var row = dataTable.NewRow();

                // Date: Format as "Day DD Mon" (e.g., "Mon 10 Mar")
                string dateStr = day.Key.ToString("ddd dd MMM", CultureInfo.CreateSpecificCulture("en-US"));
                row["Date"] = new PluginText("date", dateStr);

                // Weather: Most frequent symbol_code from next_6_hours, stripped of "_day"/"_night"
                var validSymbolCodes = blockData
                    .Where(t => t?.Data?.Next6Hours?.Summary?.SymbolCode != null)
                    .Select(t => t!.Data!.Next6Hours!.Summary!.SymbolCode!)
                    .ToList();
                string weatherStr = validSymbolCodes.Any()
                    ? validSymbolCodes
                        .GroupBy(s => s)
                        .OrderByDescending(g => g.Count())
                        .ThenBy(g => g.Key) // Tiebreaker: alphabetically first
                        .First()
                        .Key
                        .Split('_')[0] // Strip "_day" or "_night"
                    : "-";
                Console.WriteLine($"Weather Plugin: Day {dateStr} - Valid next_6_hours symbol codes: {validSymbolCodes.Count}, Selected: {weatherStr}");
                row["Weather"] = new PluginText("weather", weatherStr);

                // Temperature: Min and max from instant details
                var temps = blockData.Select(t => t?.Data?.Instant?.Details?.AirTemperature ?? 0);
                string tempStr = $"{temps.Min():F0}° / {temps.Max():F0}°";
                row["Temp"] = new PluginText("temp", tempStr);

                // Precipitation: Sum of next_6_hours amounts
                float precip = (float)blockData.Sum(t => t?.Data?.Next6Hours?.Details?.PrecipitationAmount ?? 0);
                row["Precip"] = new PluginSensor("precip", precip, "mm");

                // Wind: Average speed and direction
                var windSpeeds = blockData.Select(t => t?.Data?.Instant?.Details?.WindSpeed ?? 0).Average();
                var windDirs = blockData.Select(t => t?.Data?.Instant?.Details?.WindFromDirection ?? 0).Average();
                string windDirStr = GetWindDirection(windDirs);
                row["Wind"] = new PluginText("wind", $"{windSpeeds:F1} m/s {windDirStr}");

                dataTable.Rows.Add(row);
            }

            return dataTable;
        }

        // GetWindDirection: Converts degrees to a cardinal direction (e.g., "N", "NE")
        private string GetWindDirection(double degrees)
        {
            string[] directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int index = (int)Math.Round(degrees / 45.0) % 8;
            return directions[index];
        }

        // ValidateIconUrl: Checks if an icon URL is valid via a HEAD request
        private async Task<bool> ValidateIconUrl(string url)
        {
            if (url == "-") return false;

            try
            {
                Console.WriteLine($"Weather Plugin: Validating icon URL: {url}");
                var request = new HttpRequestMessage(HttpMethod.Head, url);
                var response = await _httpClient.SendAsync(request);
                bool isValid = response.IsSuccessStatusCode;
                Console.WriteLine($"Weather Plugin: Icon URL validation result: {isValid} (Status: {response.StatusCode})");
                return isValid;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Weather Plugin: Error validating icon URL: {ex.Message}");
                return false;
            }
        }

        // CalculateFeelsLike: Computes wind chill if applicable
        private double CalculateFeelsLike(double temp, double windSpeed, double humidity)
        {
            if (temp < 10 && windSpeed > 1.33)
            {
                double windKmh = windSpeed * 3.6;
                return 13.12 + 0.6215 * temp - 11.37 * Math.Pow(windKmh, 0.16) + 0.3965 * temp * Math.Pow(windKmh, 0.16);
            }
            return temp;
        }

        // JSON model classes for deserialization
        private class NominatimResult { [JsonPropertyName("lat")] public string Lat { get; set; } = "0"; [JsonPropertyName("lon")] public string Lon { get; set; } = "0"; }
        private class YrForecast { public YrProperties? Properties { get; set; } }
        private class YrProperties { public YrTimeseries[]? Timeseries { get; set; } }
        private class YrTimeseries { [JsonPropertyName("time")] public string? Time { get; set; } public YrData? Data { get; set; } }
        private class YrData
        {
            public YrInstant? Instant { get; set; }
            [JsonPropertyName("next_1_hours")] public YrNextHour? Next1Hours { get; set; }
            [JsonPropertyName("next_6_hours")] public YrNextHour? Next6Hours { get; set; }
        }
        private class YrInstant { public YrDetails? Details { get; set; } }
        private class YrDetails
        {
            [JsonPropertyName("air_temperature")] public double AirTemperature { get; set; }
            [JsonPropertyName("air_pressure_at_sea_level")] public double AirPressureAtSeaLevel { get; set; }
            [JsonPropertyName("relative_humidity")] public double RelativeHumidity { get; set; }
            [JsonPropertyName("wind_speed")] public double WindSpeed { get; set; }
            [JsonPropertyName("wind_speed_of_gust")] public double? WindSpeedOfGust { get; set; }
            [JsonPropertyName("wind_from_direction")] public double WindFromDirection { get; set; }
            [JsonPropertyName("cloud_area_fraction")] public double CloudAreaFraction { get; set; }
        }
        private class YrNextHour
        {
            public YrSummary? Summary { get; set; }
            public YrNextHourDetails? Details { get; set; }
        }
        private class YrSummary
        {
            [JsonPropertyName("symbol_code")] public string? SymbolCode { get; set; }
        }
        private class YrNextHourDetails
        {
            [JsonPropertyName("precipitation_amount")] public double PrecipitationAmount { get; set; }
            [JsonPropertyName("precipitation_category")] public string? PrecipitationCategory { get; set; }
        }
    }
}