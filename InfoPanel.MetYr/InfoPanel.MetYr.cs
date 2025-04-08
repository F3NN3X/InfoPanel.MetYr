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
 * Version: 1.5.1
 * Author: F3NN3X
 * Description: An InfoPanel plugin for retrieving weather data from MET Norway's Yr API (api.met.no). Provides current weather conditions via nowcast (temperature, wind, precipitation, etc.) and a configurable forecast table via locationforecast. Falls back to locationforecast for current data if nowcast unavailable. Supports configurable locations (name or lat/long), temperature units (C/F), date formats, and UTC offset adjustment via an INI file, with automatic geocoding using Nominatim when lat/long not provided. Updates hourly by default, with robust null safety and detailed logging.
 * Changelog (Recent):
 *   - v1.5.1 (Apr 08, 2025): Fixed INI lat/long override, added fallback to locationforecast for current weather if nowcast fails, ensured consistent C-to-F conversion.
 *   - v1.5.0 (Apr 08, 2025): Added support for Celsius/Fahrenheit via INI (TemperatureUnit), and optional Latitude/Longitude in INI to override geocoding.
 *   - v1.4.2 (Mar 13, 2025): Adjusted OpenWeatherMap icon mapping: fair_day/night to 01d/01n (clear sky) instead of 02d/02n.
 * Note: Full history in CHANGELOG.md. Requires internet access for API calls.
 */

namespace InfoPanel.Extras
{
    public class YrWeatherPlugin : BasePlugin
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        
        private static readonly Dictionary<string, string> YrToOpenWeatherIconCode = new()
        {
            { "clearsky_day", "01d" }, { "clearsky_night", "01n" },
            { "fair_day", "01d" }, { "fair_night", "01n" },
            { "partlycloudy_day", "02d" }, { "partlycloudy_night", "02n" },
            { "cloudy", "04d" },
            { "rain", "10d" }, { "rain_day", "10d" }, { "rain_night", "10n" },
            { "lightrain", "10d" }, { "lightrain_day", "10d" }, { "lightrain_night", "10n" },
            { "heavyrain", "10d" },
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

        private double _latitude;
        private double _longitude;
        private string? _location;
        private bool _coordinatesSet = false;
        
        private int _refreshIntervalMinutes = 60;
        private DateTime _lastUpdateTime = DateTime.MinValue;
        private string _dateTimeFormat = "yyyy-MM-dd HH:mm";
        private double _utcOffsetHours = 0;
        private int _forecastDays = 5;
        private string _temperatureUnit = "C";

        private readonly PluginText _name = new("name", "Name", "-");
        private readonly PluginText _weather = new("weather", "Weather", "-");
        private readonly PluginText _weatherDesc = new("weather_desc", "Weather Description", "-");
        private readonly PluginText _weatherIcon = new("weather_icon", "Weather Icon", "-");
        private readonly PluginText _weatherIconUrl = new("weather_icon_url", "Weather Icon URL", "-");
        private readonly PluginText _lastRefreshed = new("last_refreshed", "Last Refreshed", "-");

        private PluginSensor _temp;
        private readonly PluginSensor _pressure = new("pressure", "Pressure", 0, "hPa");
        private readonly PluginSensor _seaLevel = new("sea_level", "Sea Level Pressure", 0, "hPa");
        private PluginSensor _feelsLike;
        private readonly PluginSensor _humidity = new("humidity", "Humidity", 0, "%");

        private readonly PluginSensor _windSpeed = new("wind_speed", "Wind Speed", 0, "m/s");
        private readonly PluginSensor _windDeg = new("wind_deg", "Wind Degree", 0, "°");
        private readonly PluginSensor _windGust = new("wind_gust", "Wind Gust", 0, "m/s");

        private readonly PluginSensor _clouds = new("clouds", "Clouds", 0, "%");
        private readonly PluginSensor _rain = new("rain", "Rain", 0, "mm/h");
        private readonly PluginSensor _snow = new("snow", "Snow", 0, "mm/h");

        private static readonly string _forecastTableFormat = "0:150|1:100|2:80|3:60|4:100";
        private PluginTable _forecastTable;

        public YrWeatherPlugin()
            : base(
                "yr-weather-plugin",
                "Weather Info - MET/Yr",
                "Retrieves current weather from nowcast and forecasts from locationforecast via api.met.no, using OpenWeatherMap icons."
            )
        {
            _httpClient.DefaultRequestHeaders.Add(
                "User-Agent",
                "InfoPanel-YrWeatherPlugin/1.5.1 (contact@example.com)"
            );
            _temp = new PluginSensor("temp", "Temperature", 0, "°C");
            _feelsLike = new PluginSensor("feels_like", "Feels Like", 0, "°C");
            _forecastTable = new PluginTable("Forecast", new DataTable(), _forecastTableFormat);
        }

        private string? _configFilePath = null;
        public override string? ConfigFilePath => _configFilePath;
        public override TimeSpan UpdateInterval => TimeSpan.FromMinutes(_refreshIntervalMinutes);

        public override void Initialize()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            _configFilePath = $"{assembly.ManifestModule.FullyQualifiedName}.ini";

            var parser = new FileIniDataParser();
            IniData config;
            if (!File.Exists(_configFilePath))
            {
                config = new IniData();
                config["Yr Weather Plugin"]["Location"] = "Oslo, Norway";
                config["Yr Weather Plugin"]["Latitude"] = "";
                config["Yr Weather Plugin"]["Longitude"] = "";
                config["Yr Weather Plugin"]["RefreshIntervalMinutes"] = "60";
                config["Yr Weather Plugin"]["DateTimeFormat"] = "yyyy-MM-dd HH:mm";
                config["Yr Weather Plugin"]["UtcOffsetHours"] = "0";
                config["Yr Weather Plugin"]["ForecastDays"] = "5";
                config["Yr Weather Plugin"]["TemperatureUnit"] = "C";
                parser.WriteFile(_configFilePath, config);
                _location = "Oslo, Norway";
                _refreshIntervalMinutes = 60;
                _dateTimeFormat = "yyyy-MM-dd HH:mm";
                _utcOffsetHours = 0;
                _forecastDays = 5;
                _temperatureUnit = "C";
            }
            else
            {
                config = parser.ReadFile(_configFilePath);
                _location = config["Yr Weather Plugin"]["Location"] ?? "Oslo, Norway";

                // Read optional Latitude and Longitude with null handling
                string latStr = config["Yr Weather Plugin"]["Latitude"]?.Trim() ?? "";
                string lonStr = config["Yr Weather Plugin"]["Longitude"]?.Trim() ?? "";

                if (!int.TryParse(config["Yr Weather Plugin"]["RefreshIntervalMinutes"], out _refreshIntervalMinutes) || _refreshIntervalMinutes <= 0)
                    _refreshIntervalMinutes = 60;

                string formatFromIni = config["Yr Weather Plugin"]["DateTimeFormat"] ?? "yyyy-MM-dd HH:mm";
                _dateTimeFormat = ValidateDateTimeFormat(formatFromIni) ? formatFromIni : "yyyy-MM-dd HH:mm";

                if (!double.TryParse(config["Yr Weather Plugin"]["UtcOffsetHours"], NumberStyles.Any, CultureInfo.InvariantCulture, out _utcOffsetHours))
                {
                    Console.WriteLine($"Weather Plugin: Invalid UtcOffsetHours '{config["Yr Weather Plugin"]["UtcOffsetHours"]}', defaulting to 0");
                    _utcOffsetHours = 0;
                }

                if (!int.TryParse(config["Yr Weather Plugin"]["ForecastDays"], out _forecastDays) || _forecastDays < 1 || _forecastDays > 10)
                    _forecastDays = 5;

                _temperatureUnit = config["Yr Weather Plugin"]["TemperatureUnit"]?.ToUpper() == "F" ? "F" : "C";
                string tempUnit = _temperatureUnit == "F" ? "°F" : "°C";
                _temp = new PluginSensor("temp", "Temperature", _temp.Value, tempUnit);
                _feelsLike = new PluginSensor("feels_like", "Feels Like", _feelsLike.Value, tempUnit);

                // Prioritize INI coordinates if provided
                if (!string.IsNullOrEmpty(latStr) && !string.IsNullOrEmpty(lonStr) &&
                    double.TryParse(latStr, NumberStyles.Any, CultureInfo.InvariantCulture, out _latitude) &&
                    double.TryParse(lonStr, NumberStyles.Any, CultureInfo.InvariantCulture, out _longitude))
                {
                    _coordinatesSet = true;
                    Console.WriteLine($"Weather Plugin: Coordinates set from INI - Lat: {_latitude}, Lon: {_longitude}");
                }
                else
                {
                    _coordinatesSet = false; // Force geocoding if INI coords invalid or missing
                    Console.WriteLine($"Weather Plugin: INI Latitude/Longitude invalid or missing, will use geocoding.");
                }
            }

            Console.WriteLine($"Weather Plugin: Read location from INI: {_location}");
            Console.WriteLine($"Weather Plugin: Refresh interval set to: {_refreshIntervalMinutes} minutes");
            Console.WriteLine($"Weather Plugin: DateTime format set to: {_dateTimeFormat}");
            Console.WriteLine($"Weather Plugin: UTC offset hours set to: {_utcOffsetHours}");
            Console.WriteLine($"Weather Plugin: Forecast days set to: {_forecastDays}");
            Console.WriteLine($"Weather Plugin: Temperature unit set to: {_temperatureUnit}");
        }

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

        public override void Close() { }

        public override void Load(List<IPluginContainer> containers)
        {
            var container = new PluginContainer(_location ?? $"Lat:{_latitude}, Lon:{_longitude}");
            container.Entries.AddRange(
                [_name, _weather, _weatherDesc, _weatherIcon, _weatherIconUrl, _lastRefreshed]
            );
            container.Entries.AddRange(
                [
                    _temp, _pressure, _seaLevel, _feelsLike, _humidity,
                    _windSpeed, _windDeg, _windGust, _clouds, _rain, _snow,
                    _forecastTable
                ]
            );
            containers.Add(container);
        }

        [PluginAction("Visit MET/Yr API Docs")]
        public void LaunchApiUrl()
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = "https://developer.yr.no/doc/GettingStarted/", UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error launching URL: {ex.Message}");
            }
        }

        public override void Update() => throw new NotImplementedException();

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
                await SetCoordinatesFromLocation(_location, cancellationToken);
                _coordinatesSet = true;
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                await GetWeather(cancellationToken);
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

        private async Task SetCoordinatesFromLocation(string? location, CancellationToken cancellationToken)
        {
            if (_coordinatesSet)
            {
                Console.WriteLine($"Weather Plugin: Using coordinates from INI - Lat: {_latitude}, Lon: {_longitude}");
                return;
            }

            if (string.IsNullOrEmpty(location))
            {
                Console.WriteLine("Weather Plugin: Location is empty, using fallback coordinates.");
                _latitude = 1.3521;
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
                                $"Weather Plugin: Coordinates set from Nominatim - Lat: {_latitude}, Lon: {_longitude}"
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

        private float ConvertCelsiusToFahrenheit(double celsius) => (float)(celsius * 9.0 / 5.0 + 32.0);

        private async Task GetWeather(CancellationToken cancellationToken)
        {
            try
            {
                string latStr = _latitude.ToString("0.0000", CultureInfo.InvariantCulture);
                string lonStr = _longitude.ToString("0.0000", CultureInfo.InvariantCulture);
                string nowcastUrl = $"https://api.met.no/weatherapi/nowcast/2.0/complete?lat={latStr}&lon={lonStr}";
                string forecastUrl = $"https://api.met.no/weatherapi/locationforecast/2.0/complete?lat={latStr}&lon={lonStr}";

                YrTimeseries? currentTimeseries = null;

                // Try nowcast first
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
                        currentTimeseries = nowcast.Properties.Timeseries[0];
                        Console.WriteLine($"Weather Plugin: Current timeseries from nowcast: {currentTimeseries.Time}");
                    }
                    else
                    {
                        Console.WriteLine("Weather Plugin: No timeseries data in nowcast response.");
                    }
                }
                else
                {
                    Console.WriteLine($"Weather Plugin: Nowcast request failed with status: {nowcastResponse.StatusCode}, falling back to forecast.");
                }

                // Fetch forecast (and use as fallback for current if nowcast failed)
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
                        // Use first timeseries as current if nowcast failed
                        if (currentTimeseries == null)
                        {
                            currentTimeseries = forecast.Properties.Timeseries[0];
                            Console.WriteLine($"Weather Plugin: Using forecast timeseries for current: {currentTimeseries.Time}");
                        }
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

                // Process current weather data
                if (currentTimeseries?.Data?.Instant?.Details != null)
                {
                    var instant = currentTimeseries.Data.Instant;
                    var next1Hour = currentTimeseries.Data.Next1Hours;

                    _name.Value = _location ?? $"Lat:{_latitude.ToString(CultureInfo.InvariantCulture)}, Lon:{_longitude.ToString(CultureInfo.InvariantCulture)}";
                    _weather.Value = next1Hour?.Summary?.SymbolCode?.Split('_')[0] ?? "-";
                    _weatherDesc.Value = next1Hour?.Summary?.SymbolCode?.Replace("_", " ") ?? "-";
                    _weatherIcon.Value = next1Hour?.Summary?.SymbolCode ?? "-";
                    string iconCode = YrToOpenWeatherIconCode.TryGetValue(_weatherIcon.Value, out var code) ? code : "04d";
                    _weatherIconUrl.Value = $"https://openweathermap.org/img/wn/{iconCode}@4x.png";
                    Console.WriteLine($"Weather Plugin: Current icon URL: {_weatherIconUrl.Value}");

                    float tempC = (float)instant.Details.AirTemperature;
                    float feelsLikeC = (float)CalculateFeelsLike(instant.Details.AirTemperature, instant.Details.WindSpeed, instant.Details.RelativeHumidity);
                    _temp.Value = _temperatureUnit == "F" ? ConvertCelsiusToFahrenheit(tempC) : tempC;
                    _feelsLike.Value = _temperatureUnit == "F" ? ConvertCelsiusToFahrenheit(feelsLikeC) : feelsLikeC;

                    _pressure.Value = (float)instant.Details.AirPressureAtSeaLevel;
                    _seaLevel.Value = (float)instant.Details.AirPressureAtSeaLevel;
                    _humidity.Value = (float)instant.Details.RelativeHumidity;
                    _windSpeed.Value = (float)instant.Details.WindSpeed;
                    _windDeg.Value = (float)instant.Details.WindFromDirection;
                    _windGust.Value = (float)(instant.Details.WindSpeedOfGust ?? instant.Details.WindSpeed);
                    _clouds.Value = (float)instant.Details.CloudAreaFraction;
                    _rain.Value = (float)(next1Hour?.Details?.PrecipitationAmount ?? 0);
                    _snow.Value = next1Hour?.Details?.PrecipitationCategory == "snow" ? (float)(next1Hour.Details.PrecipitationAmount) : 0;

                    Console.WriteLine($"Weather Plugin: Current data set - Temp: {_temp.Value}{_temp.Unit}, Weather: {_weather.Value}");
                }
                else
                {
                    Console.WriteLine("Weather Plugin: No valid current timeseries data available.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Weather Plugin: Error fetching weather data: {ex.Message}");
            }
        }

        private DataTable BuildForecastTable(YrTimeseries[] timeseries)
        {
            var dataTable = new DataTable();
            dataTable.Columns.Add("Date", typeof(PluginText));
            dataTable.Columns.Add("Weather", typeof(PluginText));
            dataTable.Columns.Add("Temp", typeof(PluginText));
            dataTable.Columns.Add("Precip", typeof(PluginSensor));
            dataTable.Columns.Add("Wind", typeof(PluginText));

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
                    ? validSymbolCodes.GroupBy(s => s).OrderByDescending(g => g.Count()).ThenBy(g => g.Key).First().Key.Split('_')[0]
                    : "-";
                row["Weather"] = new PluginText("weather", weatherStr);

                var tempsC = blockData.Select(t => t?.Data?.Instant?.Details?.AirTemperature ?? 0);
                string tempStr = _temperatureUnit == "F"
                    ? $"{ConvertCelsiusToFahrenheit(tempsC.Max()):F0}°F / {ConvertCelsiusToFahrenheit(tempsC.Min()):F0}°F"
                    : $"{tempsC.Max():F0}°C / {tempsC.Min():F0}°C";
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

        private string GetWindDirection(double degrees)
        {
            string[] directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int index = (int)Math.Round(degrees / 45.0) % 8;
            return directions[index];
        }

        private double CalculateFeelsLike(double temp, double windSpeed, double humidity)
        {
            if (temp < 10 && windSpeed > 1.33)
            {
                double windKmh = windSpeed * 3.6;
                return 13.12 + 0.6215 * temp - 11.37 * Math.Pow(windKmh, 0.16) + 0.3965 * temp * Math.Pow(windKmh, 0.16);
            }
            return temp;
        }

        private class NominatimResult { [JsonPropertyName("lat")] public string Lat { get; set; } = "0"; [JsonPropertyName("lon")] public string Lon { get; set; } = "0"; }
        private class YrNowcast { public YrProperties? Properties { get; set; } }
        private class YrNowcastProperties { public YrNowcastTimeseries[]? Timeseries { get; set; } }
        private class YrNowcastTimeseries { [JsonPropertyName("time")] public string? Time { get; set; } public YrData? Data { get; set; } }
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