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
 * Version: 1.4.2
 * Author: F3NN3X
 * Description: An InfoPanel plugin for retrieving weather data from MET Norway's Yr API (api.met.no). Provides current weather conditions via nowcast (temperature, wind, precipitation, etc.) and a configurable forecast table via locationforecast. Supports configurable locations, date formats, and UTC offset adjustment via an INI file, with automatic geocoding using Nominatim. Updates hourly by default, with robust null safety and detailed logging.
 * Changelog (Recent):
 *   - v1.4.2 (Mar 13, 2025): Adjusted OpenWeatherMap icon mapping: fair_day/night to 01d/01n (clear sky) instead of 02d/02n.
 *   - v1.4.1 (Mar 13, 2025): Reverted to OpenWeatherMap icons due to MET/Yr PNG cutoff issue in InfoPanel.
 *   - v1.4.0 (Mar 13, 2025): Split current and forecast data sources, attempted MET/Yr icons.
 * Note: Full history in CHANGELOG.md. Requires internet access for API calls.
 */

namespace InfoPanel.Extras
{
    public class YrWeatherPlugin : BasePlugin
    {
        // Shared HTTP client for API requests, initialized with a custom User-Agent
        private static readonly HttpClient _httpClient = new HttpClient();
        
        // Mapping from MET/Yr symbol_code to OpenWeatherMap icon codes
        private static readonly Dictionary<string, string> YrToOpenWeatherIconCode = new()
        {
            { "clearsky_day", "01d" }, { "clearsky_night", "01n" },
            { "fair_day", "01d" }, { "fair_night", "01n" }, // Updated to clear sky (was 02d/02n)
            { "partlycloudy_day", "02d" }, { "partlycloudy_night", "02n" }, // Few clouds
            { "cloudy", "04d" }, // No day/night distinction in OWM for cloudy
            { "rain", "10d" }, { "rain_day", "10d" }, { "rain_night", "10n" },
            { "lightrain", "10d" }, { "lightrain_day", "10d" }, { "lightrain_night", "10n" },
            { "heavyrain", "10d" }, // OWM lacks heavy rain distinction
            { "lightrainshowers_day", "09d" }, { "lightrainshowers_night", "09n" },
            { "rainshowers_day", "09d" }, { "rainshowers_night", "09n" },
            { "heavyrainshowers_day", "09d" }, { "heavyrainshowers_night", "09n" },
            { "snow", "13d" }, { "snow_day", "13d" }, { "snow_night", "13n" },
            { "lightsnow", "13d" }, { "lightsnow_day", "13d" }, { "lightsnow_night", "13n" },
            { "heavysnow", "13d" }, { "heavysnow_day", "13d" }, { "heavysnow_night", "13n" },
            { "lightsnowshowers_day", "13d" }, { "lightsnowshowers_night", "13n" },
            { "snowshowers_day", "13d" }, { "snowshowers_night", "13n" },
            { "heavysnowshowers_day", "13d" }, { "heavysnowshowers_night", "13n" },
            { "sleet", "13d" }, { "sleet_day", "13d" }, { "sleet_night", "13n" },
            { "lightsleet", "13d" }, { "lightsleet_day", "13d" }, { "lightsleet_night", "13n" },
            { "sleetshowers_day", "13d" }, { "sleetshowers_night", "13n" },
            { "thunder", "11d" }, { "thunder_day", "11d" }, { "thunder_night", "11n" },
            { "rainthunder", "11d" }, { "rainthunder_day", "11d" }, { "rainthunder_night", "11n" },
            { "fog", "50d" }, { "fog_day", "50d" }, { "fog_night", "50n" }
        };

        // Location coordinates and metadata
        private double _latitude;              // Latitude of the weather location
        private double _longitude;             // Longitude of the weather location
        private string? _location;             // Human-readable location name (e.g., "Oslo, Norway")
        private bool _coordinatesSet = false;  // Flag to track if coordinates have been initialized
        
        // Update timing and format
        private int _refreshIntervalMinutes = 60;   // Default refresh interval (1 hour)
        private DateTime _lastUpdateTime = DateTime.MinValue; // Last update timestamp (UTC)
        private string _dateTimeFormat = "yyyy-MM-dd HH:mm";  // Default format for last refreshed time
        private double _utcOffsetHours = 0;                   // UTC offset in hours (e.g., 1 for CET, -5 for EST)

        // Plugin data fields for current weather
        private readonly PluginText _name = new("name", "Name", "-");                   // Location name
        private readonly PluginText _weather = new("weather", "Weather", "-");         // Current weather condition
        private readonly PluginText _weatherDesc = new("weather_desc", "Weather Description", "-"); // Weather with day/night detail
        private readonly PluginText _weatherIcon = new("weather_icon", "Weather Icon", "-");       // Weather icon code
        private readonly PluginText _weatherIconUrl = new("weather_icon_url", "Weather Icon URL", "-"); // URL to weather icon
        private readonly PluginText _lastRefreshed = new("last_refreshed", "Last Refreshed", "-"); // Last update time

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
        private readonly PluginTable _forecastTable = new("Forecast", new DataTable(), _forecastTableFormat); // Forecast table
        private int _forecastDays = 5; // Number of forecast days (configurable)

        // Constructor: Initializes the plugin with a unique ID, name, and description
        public YrWeatherPlugin()
            : base(
                "yr-weather-plugin",
                "Weather Info - MET/Yr",
                "Retrieves current weather from nowcast and forecasts from locationforecast via api.met.no, using OpenWeatherMap icons."
            )
        {
            // Set User-Agent for MET/Yr API compliance
            _httpClient.DefaultRequestHeaders.Add(
                "User-Agent",
                "InfoPanel-YrWeatherPlugin/1.4.2 (contact@example.com)"
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
                config["Yr Weather Plugin"]["DateTimeFormat"] = "yyyy-MM-dd HH:mm"; // Default date/time format
                config["Yr Weather Plugin"]["UtcOffsetHours"] = "0"; // Default: UTC
                config["Yr Weather Plugin"]["ForecastDays"] = "5"; // Default forecast length
                parser.WriteFile(_configFilePath, config);
                _location = "Oslo, Norway";
                _refreshIntervalMinutes = 60;
                _dateTimeFormat = "yyyy-MM-dd HH:mm";
                _utcOffsetHours = 0;
                _forecastDays = 5;
            }
            else
            {
                // Read existing config
                config = parser.ReadFile(_configFilePath);
                _location = config["Yr Weather Plugin"]["Location"] ?? "Oslo, Norway";
                if (!int.TryParse(config["Yr Weather Plugin"]["RefreshIntervalMinutes"], out _refreshIntervalMinutes) || _refreshIntervalMinutes <= 0)
                    _refreshIntervalMinutes = 60; // Fallback to 1 hour if invalid
                
                // Validate and set date format
                string formatFromIni = config["Yr Weather Plugin"]["DateTimeFormat"] ?? "yyyy-MM-dd HH:mm";
                _dateTimeFormat = ValidateDateTimeFormat(formatFromIni) ? formatFromIni : "yyyy-MM-dd HH:mm";
                
                // Parse UtcOffsetHours (e.g., +1, -5), fallback to 0 if invalid
                if (!double.TryParse(config["Yr Weather Plugin"]["UtcOffsetHours"], NumberStyles.Any, CultureInfo.InvariantCulture, out _utcOffsetHours))
                {
                    Console.WriteLine($"Weather Plugin: Invalid UtcOffsetHours '{config["Yr Weather Plugin"]["UtcOffsetHours"]}', defaulting to 0");
                    _utcOffsetHours = 0; // Fallback to UTC
                }
                
                if (!int.TryParse(config["Yr Weather Plugin"]["ForecastDays"], out _forecastDays) || _forecastDays < 1 || _forecastDays > 10)
                    _forecastDays = 5; // Fallback to 5 days, cap at 10 (Yr API limit)
            }

            Console.WriteLine($"Weather Plugin: Read location from INI: {_location}");
            Console.WriteLine($"Weather Plugin: Refresh interval set to: {_refreshIntervalMinutes} minutes");
            Console.WriteLine($"Weather Plugin: DateTime format set to: {_dateTimeFormat}");
            Console.WriteLine($"Weather Plugin: UTC offset hours set to: {_utcOffsetHours}");
            Console.WriteLine($"Weather Plugin: Forecast days set to: {_forecastDays}");
        }

        // ValidateDateTimeFormat: Ensures the custom DateTime format is valid per C# spec
        private bool ValidateDateTimeFormat(string format)
        {
            try
            {
                DateTime testDate = DateTime.UtcNow;
                string result = testDate.ToString(format, CultureInfo.InvariantCulture);
                Console.WriteLine($"Weather Plugin: Validated format '{format}' -> '{result}'");
                return true;
            }
            catch (FormatException ex)
            {
                Console.WriteLine($"Weather Plugin: Invalid DateTime format '{format}': {ex.Message}");
                return false;
            }
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
                [_name, _weather, _weatherDesc, _weatherIcon, _weatherIconUrl, _lastRefreshed]
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
                try
                {
                    DateTime adjustedTime = _lastUpdateTime.AddHours(_utcOffsetHours);
                    string formattedTime = adjustedTime.ToString(_dateTimeFormat, CultureInfo.InvariantCulture);
                    _lastRefreshed.Value = formattedTime;
                    Console.WriteLine($"Weather Plugin: UTC time: {_lastUpdateTime:yyyy-MM-dd HH:mm:ss} UTC");
                    Console.WriteLine($"Weather Plugin: Adjusted time: {adjustedTime:yyyy-MM-dd HH:mm:ss} (offset: {_utcOffsetHours} hours)");
                    Console.WriteLine($"Weather Plugin: Last refreshed set to: {_lastRefreshed.Value}");
                }
                catch (FormatException ex)
                {
                    Console.WriteLine($"Weather Plugin: Error formatting last refreshed time with '{_dateTimeFormat}': {ex.Message}");
                    DateTime adjustedTime = _lastUpdateTime.AddHours(_utcOffsetHours);
                    _lastRefreshed.Value = adjustedTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
                    Console.WriteLine($"Weather Plugin: Fell back to: {_lastRefreshed.Value} (offset: {_utcOffsetHours} hours)");
                }
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

        // GetWeather: Fetches current weather from nowcast and forecast from locationforecast
        private async Task GetWeather(CancellationToken cancellationToken)
        {
            try
            {
                string latStr = _latitude.ToString("0.0000", CultureInfo.InvariantCulture);
                string lonStr = _longitude.ToString("0.0000", CultureInfo.InvariantCulture);
                string nowcastUrl = $"https://api.met.no/weatherapi/nowcast/2.0/complete?lat={latStr}&lon={lonStr}";
                string forecastUrl = $"https://api.met.no/weatherapi/locationforecast/2.0/complete?lat={latStr}&lon={lonStr}";

                // Fetch current weather from nowcast
                Console.WriteLine($"Weather Plugin: Fetching current weather from: {nowcastUrl}");
                var nowcastResponse = await _httpClient.GetAsync(nowcastUrl, cancellationToken);
                if (nowcastResponse.IsSuccessStatusCode)
                {
                    var nowcastJson = await nowcastResponse.Content.ReadAsStringAsync(cancellationToken);
                    Console.WriteLine($"Weather Plugin: Nowcast response (first 500 chars): {nowcastJson.Substring(0, Math.Min(nowcastJson.Length, 500))}...");
                    var nowcast = JsonSerializer.Deserialize<YrNowcast>(
                        nowcastJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );

                    if (nowcast?.Properties?.Timeseries?.Length > 0)
                    {
                        var current = nowcast.Properties.Timeseries[0];
                        Console.WriteLine($"Weather Plugin: Current timeseries time: {current.Time}");
                        var instant = current.Data?.Instant;
                        var next1Hour = current.Data?.Next1Hours;

                        if (instant?.Details != null)
                        {
                            _name.Value = _location ?? $"Lat:{_latitude.ToString(CultureInfo.InvariantCulture)}, Lon:{_longitude.ToString(CultureInfo.InvariantCulture)}";
                            _weather.Value = next1Hour?.Summary?.SymbolCode?.Split('_')[0] ?? "-";
                            _weatherDesc.Value = next1Hour?.Summary?.SymbolCode?.Replace("_", " ") ?? "-";
                            _weatherIcon.Value = next1Hour?.Summary?.SymbolCode ?? "-";
                            string iconCode = YrToOpenWeatherIconCode.TryGetValue(_weatherIcon.Value, out var code) ? code : "04d";
                            _weatherIconUrl.Value = $"https://openweathermap.org/img/wn/{iconCode}@4x.png";
                            Console.WriteLine($"Weather Plugin: Current icon URL: {_weatherIconUrl.Value}");

                            _temp.Value = (float)instant.Details.AirTemperature;
                            _pressure.Value = (float)instant.Details.AirPressureAtSeaLevel;
                            _seaLevel.Value = (float)instant.Details.AirPressureAtSeaLevel;
                            _feelsLike.Value = (float)CalculateFeelsLike(instant.Details.AirTemperature, instant.Details.WindSpeed, instant.Details.RelativeHumidity);
                            _humidity.Value = (float)instant.Details.RelativeHumidity;
                            _windSpeed.Value = (float)instant.Details.WindSpeed;
                            _windDeg.Value = (float)instant.Details.WindFromDirection;
                            _windGust.Value = (float)(instant.Details.WindSpeedOfGust ?? instant.Details.WindSpeed);
                            _clouds.Value = (float)instant.Details.CloudAreaFraction;
                            _rain.Value = (float)(next1Hour?.Details?.PrecipitationAmount ?? 0);
                            _snow.Value = next1Hour?.Details?.PrecipitationCategory == "snow" ? (float)(next1Hour.Details.PrecipitationAmount) : 0;

                            Console.WriteLine($"Weather Plugin: Current data set - Temp: {_temp.Value}, Weather: {_weather.Value}");
                        }
                    }
                    else
                    {
                        Console.WriteLine("Weather Plugin: No timeseries data in nowcast response.");
                    }
                }
                else
                {
                    Console.WriteLine($"Weather Plugin: Nowcast request failed with status: {nowcastResponse.StatusCode}");
                }

                // Fetch forecast from locationforecast
                Console.WriteLine($"Weather Plugin: Fetching forecast from: {forecastUrl}");
                var forecastResponse = await _httpClient.GetAsync(forecastUrl, cancellationToken);
                if (forecastResponse.IsSuccessStatusCode)
                {
                    var forecastJson = await forecastResponse.Content.ReadAsStringAsync(cancellationToken);
                    Console.WriteLine($"Weather Plugin: Forecast response (first 500 chars): {forecastJson.Substring(0, Math.Min(forecastJson.Length, 500))}...");
                    var forecast = JsonSerializer.Deserialize<YrForecast>(
                        forecastJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );

                    if (forecast?.Properties?.Timeseries?.Length > 0)
                    {
                        _forecastTable.Value = BuildForecastTable(forecast.Properties.Timeseries);
                    }
                    else
                    {
                        Console.WriteLine("Weather Plugin: No timeseries data in forecast response.");
                    }
                }
                else
                {
                    Console.WriteLine($"Weather Plugin: Forecast request failed with status: {forecastResponse.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Weather Plugin: Error fetching weather data: {ex.Message}");
            }
        }

        // BuildForecastTable: Creates a forecast table starting from tomorrow
        private DataTable BuildForecastTable(YrTimeseries[] timeseries)
        {
            var dataTable = new DataTable();
            dataTable.Columns.Add("Date", typeof(PluginText));    // Day of the forecast
            dataTable.Columns.Add("Weather", typeof(PluginText)); // Most frequent weather condition
            dataTable.Columns.Add("Temp", typeof(PluginText));    // Max/min temperature
            dataTable.Columns.Add("Precip", typeof(PluginSensor)); // Total precipitation
            dataTable.Columns.Add("Wind", typeof(PluginText));    // Average wind speed and direction

            var now = DateTime.UtcNow.Date;
            var startTime = now.AddDays(1);
            var endTime = startTime.AddDays(_forecastDays);
            var dailyBlocks = new Dictionary<DateTime, List<YrTimeseries>>();

            foreach (var ts in timeseries)
            {
                if (ts == null || ts.Time == null || !DateTime.TryParse(ts.Time, out var tsTime))
                {
                    Console.WriteLine($"Weather Plugin: Skipping invalid timeseries entry with null or unparsable time: {ts?.Time}");
                    continue;
                }
                var tsDate = tsTime.Date;
                if (tsDate >= startTime && tsDate < endTime)
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

                string dateStr = day.Key.ToString("ddd dd MMM", CultureInfo.CreateSpecificCulture("en-US"));
                row["Date"] = new PluginText("date", dateStr);

                var validSymbolCodes = blockData
                    .Where(t => t?.Data?.Next6Hours?.Summary?.SymbolCode != null)
                    .Select(t => t!.Data!.Next6Hours!.Summary!.SymbolCode!)
                    .ToList();
                string weatherStr = validSymbolCodes.Any()
                    ? validSymbolCodes
                        .GroupBy(s => s)
                        .OrderByDescending(g => g.Count())
                        .ThenBy(g => g.Key)
                        .First()
                        .Key
                        .Split('_')[0]
                    : "-";
                row["Weather"] = new PluginText("weather", weatherStr);

                var temps = blockData.Select(t => t?.Data?.Instant?.Details?.AirTemperature ?? 0);
                string tempStr = $"{temps.Max():F0}° / {temps.Min():F0}°";
                row["Temp"] = new PluginText("temp", tempStr);

                float precip = (float)blockData.Sum(t => t?.Data?.Next6Hours?.Details?.PrecipitationAmount ?? 0);
                row["Precip"] = new PluginSensor("precip", precip, "mm");

                var windSpeeds = blockData.Select(t => t?.Data?.Instant?.Details?.WindSpeed ?? 0).Average();
                var windDirs = blockData.Select(t => t?.Data?.Instant?.Details?.WindFromDirection ?? 0).Average();
                string windDirStr = GetWindDirection(windDirs);
                row["Wind"] = new PluginText("wind", $"{windSpeeds:F1} m/s {windDirStr}");

                dataTable.Rows.Add(row);
            }

            return dataTable;
        }

        // GetWindDirection: Converts degrees to a cardinal direction
        private string GetWindDirection(double degrees)
        {
            string[] directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int index = (int)Math.Round(degrees / 45.0) % 8;
            return directions[index];
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
        
        // Nowcast models
        private class YrNowcast { public YrProperties? Properties { get; set; } }
        private class YrNowcastProperties { public YrNowcastTimeseries[]? Timeseries { get; set; } }
        private class YrNowcastTimeseries { [JsonPropertyName("time")] public string? Time { get; set; } public YrData? Data { get; set; } }
        
        // Forecast models
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