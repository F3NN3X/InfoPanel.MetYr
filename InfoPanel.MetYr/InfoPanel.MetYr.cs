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
 * Version: 2.0.0
 * Author: F3NN3X
 * Description: An InfoPanel plugin for retrieving weather data from MET Norway's Yr API (api.met.no). Provides current weather conditions via nowcast (temperature, wind, precipitation, etc.) and a configurable forecast table via locationforecast. Falls back to locationforecast for current data if nowcast unavailable. Supports configurable locations (name or lat/long), temperature units (C/F), date formats, UTC offset adjustment, and custom icon URLs via an INI file, with automatic geocoding using Nominatim when lat/long not provided. Updates hourly by default, with robust null safety and detailed logging. Supports both PNG and SVG icons with standardized naming.
 * Changelog (Recent):
 *   - v2.0.0 (Jun 02, 2025): Consolidated changes: fixed compilation errors (string interpolation, nested class accessibility, JsonPropertyName attribute, CloudAreaFraction type), added null reference checks, removed unreferenced labels, updated icon mapping to use custom hyphenated icon names (e.g., clear-day, rainy-1-day) based on Yr symbol_code with precipitation intensity logic, corrected duplicate class definitions and property types, ensured hyphenated icon file names (e.g., fair-day.svg), changed wind direction labels to abbreviations (N, NE, etc.), and moved plugin info text below using statements.
 * Note: Full history in CHANGELOG.md. Requires internet access for API calls.
 */

namespace InfoPanel.MetYr
{
    public class YrWeatherPlugin : BasePlugin
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        
        private double _latitude;
        private double _longitude;
        private string? _location;
        private bool _coordinatesSet = false;
        private string? _iconUrl;

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
                "Retrieves current weather from nowcast and forecasts from locationforecast via api.met.no, using OpenWeatherMap icons by default or custom icons via INI."
            )
        {
            _httpClient.DefaultRequestHeaders.Add(
                "User-Agent",
                "InfoPanel-YrWeatherPlugin/2.0.0 (contact@example.com)"
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
            try
            {
                if (!File.Exists(_configFilePath))
                {
                    Console.WriteLine($"Weather Plugin: INI file not found at {_configFilePath}, creating default.");
                    config = new IniData();
                    config["Yr Weather Plugin"]["Location"] = "Oslo, Norway";
                    config["Yr Weather Plugin"]["Latitude"] = "";
                    config["Yr Weather Plugin"]["Longitude"] = "";
                    config["Yr Weather Plugin"]["RefreshIntervalMinutes"] = "60";
                    config["Yr Weather Plugin"]["DateTimeFormat"] = "yyyy-MM-dd HH:mm";
                    config["Yr Weather Plugin"]["UtcOffsetHours"] = "0";
                    config["Yr Weather Plugin"]["ForecastDays"] = "5";
                    config["Yr Weather Plugin"]["TemperatureUnit"] = "C";
                    config["Yr Weather Plugin"]["IconUrl"] = "";
                    parser.WriteFile(_configFilePath, config);
                    _location = "Oslo, Norway";
                    _refreshIntervalMinutes = 60;
                    _dateTimeFormat = "yyyy-MM-dd HH:mm";
                    _utcOffsetHours = 0;
                    _forecastDays = 5;
                    _temperatureUnit = "C";
                    _iconUrl = null;
                }
                else
                {
                    Console.WriteLine($"Weather Plugin: Reading INI file from {_configFilePath}");
                    config = parser.ReadFile(_configFilePath);
                    string iniContent = File.ReadAllText(_configFilePath);
                    Console.WriteLine($"Weather Plugin: INI file content:\n{iniContent}");

                    _location = config["Yr Weather Plugin"]["Location"] ?? "Oslo, Norway";
                    _iconUrl = config["Yr Weather Plugin"]["IconUrl"]?.Trim();

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

                    if (!string.IsNullOrEmpty(latStr) && !string.IsNullOrEmpty(lonStr) &&
                        double.TryParse(latStr, NumberStyles.Any, CultureInfo.InvariantCulture, out _latitude) &&
                        double.TryParse(lonStr, NumberStyles.Any, CultureInfo.InvariantCulture, out _longitude))
                    {
                        _coordinatesSet = true;
                        Console.WriteLine($"Weather Plugin: Coordinates set from INI - Lat: {_latitude}, Lon: {_longitude}");
                    }
                    else
                    {
                        _coordinatesSet = false;
                        Console.WriteLine($"Weather Plugin: INI Latitude/Longitude invalid or missing, will use geocoding.");
                    }

                    Console.WriteLine($"Weather Plugin: Read IconUrl from INI: '{_iconUrl}'");
                    if (string.IsNullOrEmpty(_iconUrl))
                    {
                        Console.WriteLine("Weather Plugin: IconUrl is empty or not set, using default OpenWeatherMap icons.");
                        _iconUrl = null;
                    }
                    else if (!Uri.IsWellFormedUriString(_iconUrl, UriKind.Absolute))
                    {
                        Console.WriteLine($"Weather Plugin: Invalid IconUrl '{_iconUrl}', defaulting to OpenWeatherMap icons.");
                        _iconUrl = null;
                    }
                    else
                    {
                        Console.WriteLine($"Weather Plugin: Custom IconUrl set to: {_iconUrl}");
                        try
                        {
                            string testSvgUrl = $"{_iconUrl.TrimEnd('/')}/clear-day.svg";
                            var testResponse = _httpClient.GetAsync(testSvgUrl).Result;
                            if (testResponse.IsSuccessStatusCode)
                            {
                                Console.WriteLine($"Weather Plugin: Successfully accessed test SVG icon: {testSvgUrl}");
                            }
                            else
                            {
                                Console.WriteLine($"Weather Plugin: Test SVG icon not found at {testSvgUrl}, status: {testResponse.StatusCode}, will try PNG.");
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Weather Plugin: Error accessing test icon URL '{_iconUrl}': {ex.Message}, defaulting to OpenWeatherMap icons.");
                            _iconUrl = null;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Weather Plugin: Error reading INI file '{_configFilePath}': {ex.Message}");
                _location = "Oslo, Norway";
                _refreshIntervalMinutes = 60;
                _dateTimeFormat = "yyyy-MM-dd HH:mm";
                _utcOffsetHours = 0;
                _forecastDays = 5;
                _temperatureUnit = "C";
                _iconUrl = null;
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
                    Console.WriteLine($"Weather Plugin: Fell back to: {adjustedTime} (offset: {_utcOffsetHours} hours)");
                }
            }
            else
            {
                Console.WriteLine("Weather Plugin: Weather fetch cancelled.");
            }
        }

        private async Task SetCoordinatesFromLocation(string? location, CancellationToken cancellationToken)
        {
            try
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
                            if (results[0].lat != null && results[0].lon != null)
                            {
                                _latitude = double.Parse(results[0].lat, CultureInfo.InvariantCulture);
                                _longitude = double.Parse(results[0].lon, CultureInfo.InvariantCulture);
                                Console.WriteLine(
                                    $"Weather Plugin: Coordinates set from Nominatim - Lat: {_latitude}, Lon: {_longitude}"
                                );
                            }
                            else
                            {
                                Console.WriteLine("Weather Plugin: Nominatim result has null lat/lon, using fallback.");
                                _latitude = 1.3521;
                                _longitude = 103.8198;
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Weather Plugin: Error parsing coordinates - Lat: {results[0].lat}, Lon: {results[0].lon}, Error: {ex.Message}");
                            _latitude = 1.3521;
                            _longitude = 103.8198;
                        }
                    }
                    else
                    {
                        Console.WriteLine("Weather Plugin: No geocoding results available, using fallback.");
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

        private float ConvertCelsius(double celsius) => (float)(celsius * 9.0 / 5.0 + 32.0);

        private string MapYrSymbolToIcon(string? symbolCode, double? precipitationAmount)
        {
            if (string.IsNullOrEmpty(symbolCode))
                return "cloudy";

            double precip = precipitationAmount ?? 0.0;

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
                "lightrain" => precip < 2.5 ? "rainy-1" : "rainy-2",
                "lightrainshowers_day" => precip < 2.5 ? "rainy-1-day" : "rainy-2-day",
                "lightrainshowers_night" => precip < 2.5 ? "rainy-1-night" : "rainy-2-night",
                "rain" => precip < 7.5 ? "rainy-2" : "rainy-3",
                "rainshowers_day" => precip < 7.5 ? "rainy-2-day" : "rainy-3-day",
                "rainshowers_night" => precip < 7.5 ? "rainy-2-night" : "rainy-3-night",
                "heavyrain" => "rainy-3",
                "heavyrainshowers_day" => "rainy-3-day",
                "heavyrainshowers_night" => "rainy-3-night",
                "lightsnow" => precip < 2.5 ? "snowy-1" : "snowy-2",
                "lightsnowshowers_day" => precip < 2.5 ? "snowy-1-day" : "snowy-2-day",
                "lightsnowshowers_night" => precip < 2.5 ? "snowy-1-night" : "snowy-2-night",
                "snow" => precip < 7.5 ? "snowy-2" : "snowy-3",
                "snowshowers_day" => precip < 7.5 ? "snowy-2-day" : "snowy-3-day",
                "snowshowers_night" => precip < 7.5 ? "snowy-2-night" : "snowy-3-night",
                "heavysnow" => "snowy-3",
                "heavysnowshowers_day" => "snowy-3-day",
                "heavysnowshowers_night" => "snowy-3-night",
                "sleet" => "rain-and-sleet-mix",
                "sleetshowers_day" => "rain-and-sleet-mix",
                "sleetshowers_night" => "rain-and-sleet-mix",
                "lightsleet" => "rain-and-sleet-mix",
                "heavysleet" => "rain-and-sleet-mix",
                "rainandthunder" => "scattered-thunderstorms",
                "lightrainandthunder" => "scattered-thunderstorms",
                "heavyrainandthunder" => "thunderstorms",
                "snowandthunder" => "snow-and-sleet-mix",
                "lightsnowandthunder" => "snow-and-sleet-mix",
                "heavysnowandthunder" => "snow-and-sleet-mix",
                "tropicalstorm" => "tropical-storm",
                "hurricane" => "hurricane",
                _ when symbolCode.Contains("wind") => "wind",
                _ => "cloudy" // Default fallback
            };
        }

        private async Task GetWeather(CancellationToken cancellationToken)
        {
            try
            {
                string latStr = _latitude.ToString("0.0000", CultureInfo.InvariantCulture);
                string lonStr = _longitude.ToString("0.0000", CultureInfo.InvariantCulture);
                string nowcastUrl = $"https://api.met.no/weatherapi/nowcast/2.0/complete?lat={latStr}&lon={lonStr}";
                string forecastUrl = $"https://api.met.no/weatherapi/locationforecast/2.0/complete?lat={latStr}&lon={lonStr}";

                YrTimeseries? currentTimeseries = null;

                Console.WriteLine($"Weather Plugin: Fetching current weather: {nowcastUrl}");
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
                        Console.WriteLine($"Weather Plugin: Current timeseries from nowcast: {currentTimeseries.time}");
                    }
                    else
                    {
                        Console.WriteLine("Weather Plugin: No timeseries data in nowcast.");
                    }
                }
                else
                {
                    Console.WriteLine($"Weather Plugin: Nowcast failed with status: {nowcastResponse.StatusCode}, falling back to forecast.");
                }

                Console.WriteLine($"Weather Plugin: Fetching forecast: {forecastUrl}");
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
                        if (currentTimeseries is null)
                        {
                            currentTimeseries = forecast.Properties.Timeseries[0];
                            Console.WriteLine($"Weather Plugin: Using forecast timeseries: {currentTimeseries.time}");
                        }
                        _forecastTable.Value = BuildForecastTable(forecast.Properties.Timeseries);
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

                if (currentTimeseries?.data?.instant?.details != null)
                {
                    var instant = currentTimeseries.data.instant;
                    var next1Hour = currentTimeseries.data.next1Hours;

                    _name.Value = _location ?? $"Lat:{_latitude.ToString(CultureInfo.InvariantCulture)}, Lon:{_longitude.ToString(CultureInfo.InvariantCulture)}";
                    _weather.Value = next1Hour?.summary?.symbolCode?.Split('_')[0] ?? "-";
                    _weatherDesc.Value = next1Hour?.summary?.symbolCode?.Replace("_", " ") ?? "-";

                    string mappedIcon = MapYrSymbolToIcon(next1Hour?.summary?.symbolCode, next1Hour?.details?.precipitationAmount);
                    _weatherIcon.Value = mappedIcon;
                    Console.WriteLine($"Weather Plugin: Mapped symbol '{next1Hour?.summary?.symbolCode}' to icon: {mappedIcon}");

                    if (!string.IsNullOrEmpty(_iconUrl))
                    {
                        Console.WriteLine($"Weather Plugin: Using custom icon URL: {_iconUrl}");
                        string iconFileName = mappedIcon; // Already hyphenated from mapping
                        string svgUrl = $"{_iconUrl.TrimEnd('/')}/{iconFileName}.svg";
                        string pngUrl = $"{_iconUrl.TrimEnd('/')}/{iconFileName}.png";
                        Console.WriteLine($" Checking SVG: {svgUrl}");
                        try
                        {
                            var svgResponse = await _httpClient.GetAsync(svgUrl, cancellationToken);
                            Console.WriteLine($"SVG response: {svgResponse.StatusCode}");
                            if (svgResponse.IsSuccessStatusCode)
                            {
                                _weatherIconUrl.Value = svgUrl;
                                Console.WriteLine($"Using SVG: {_weatherIconUrl.Value}");
                            }
                            else
                            {
                                Console.WriteLine($"SVG not found, checking PNG: {pngUrl}");
                                var pngResponse = await _httpClient.GetAsync(pngUrl, cancellationToken);
                                Console.WriteLine($"PNG response: {pngResponse.StatusCode}");
                                if (pngResponse.IsSuccessStatusCode)
                                {
                                    _weatherIconUrl.Value = pngUrl;
                                    Console.WriteLine($"Using PNG: {_weatherIconUrl.Value}");
                                }
                                else
                                {
                                    Console.WriteLine("PNG not found, using OpenWeatherMap.");
                                    SetDefaultOpenWeatherMapIcon();
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error checking icon '{svgUrl}': {ex.Message}, using OpenWeatherMap.");
                            SetDefaultOpenWeatherMapIcon();
                        }
                    }
                    else
                    {
                        Console.WriteLine("No custom IconUrl, using OpenWeatherMap icons.");
                        SetDefaultOpenWeatherMapIcon();
                    }

                    float tempC = (float)instant.details.airTemperature;
                    float feelsLikeC = (float)CalculateFeelsLike(tempC, instant.details.windSpeed, instant.details.relativeHumidity);
                    _temp.Value = _temperatureUnit == "F" ? ConvertCelsius(tempC) : tempC;
                    _feelsLike.Value = _temperatureUnit == "F" ? ConvertCelsius(feelsLikeC) : feelsLikeC;

                    _pressure.Value = (float)instant.details.airPressureAtSeaLevel;
                    _seaLevel.Value = (float)instant.details.airPressureAtSeaLevel;
                    _humidity.Value = (float)instant.details.relativeHumidity;
                    _windSpeed.Value = (float)instant.details.windSpeed;
                    _windDeg.Value = (float)instant.details.windFromDirection;
                    _windGust.Value = (float)(instant.details.windSpeedOfGust ?? instant.details.windSpeed);
                    _clouds.Value = (float)instant.details.cloudAreaFraction;
                    _rain.Value = (float)(next1Hour?.details?.precipitationAmount ?? 0);
                    _snow.Value = next1Hour?.details?.precipitationCategory == "snow" ? (float)(next1Hour.details.precipitationAmount) : 0;

                    Console.WriteLine($"Weather Plugin: Set data - Temp: {_temp.Value}{_temp.Unit}, Weather: {_weather.Value}, Icon: {_weatherIcon.Value}");
                }
                else
                    Console.WriteLine("No valid timeseries data.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching weather: {ex.Message}");
            }
        }

        private void SetDefaultOpenWeatherMapIcon()
        {
            string iconCode = _weatherIcon.Value switch
            {
                "clear-day" => "01d",
                "clear-night" => "01n",
                "cloudy-1-day" => "02d",
                "cloudy-1-night" => "02n",
                "cloudy-2-day" => "03d",
                "cloudy-2-night" => "03n",
                "cloudy" => "04d",
                "fog" => "50",
                "rainy-1" => "10",
                "rainy-1-day" => "10d",
                "rainy-1-night" => "10n",
                "rainy-2" => "10",
                "rainy-2-day" => "10d",
                "rainy-2-night" => "10n",
                "rainy-3" => "10",
                "rainy-3-day" => "10d",
                "rainy-3-night" => "10n",
                "snowy-1" => "13",
                "snowy-1-day" => "13d",
                "snowy-1-night" => "13n",
                "snowy-2" => "13",
                "snowy-2-day" => "13d",
                "snowy-2-night" => "13n",
                "snowy-3" => "13",
                "snowy-3-day" => "13d",
                "snowy-3-night" => "13n",
                "rain-and-sleet-mix" => "13",
                "snow-and-sleet-mix" => "13",
                "scattered-thunderstorms" => "11",
                "scattered-thunderstorms-day" => "11d",
                "scattered-thunderstorms-night" => "11n",
                "thunderstorms" => "11",
                "tropical-storm" => "11",
                "hurricane" => "11",
                "wind" => "04",
                _ => "04d"
            };
            _weatherIconUrl.Value = $"https://openweathermap.org/img/wn/{iconCode}.png";
            Console.WriteLine($"Weather Plugin: Using OpenWeatherMap icon: {_weatherIconUrl.Value}");
        }

private DataTable BuildForecastTable(YrTimeseries[] timeseries)
{
    var dataTable = new DataTable();
    try
    {
        dataTable.Columns.Add("Date", typeof(PluginText));
        dataTable.Columns.Add("Weather", typeof(PluginText));
        dataTable.Columns.Add("Temp", typeof(PluginText)); // Changed to PluginText
        dataTable.Columns.Add("Precip", typeof(PluginSensor)); // Changed to PluginSensor
        dataTable.Columns.Add("Wind", typeof(PluginText)); // Changed to PluginText

        var now = DateTime.UtcNow;
        var startTime = now.AddDays(1).Date;
        var endTime = startTime.AddDays(_forecastDays);
        var dailyBlocks = new Dictionary<DateTime, List<YrTimeseries>>();

        foreach (var ts in timeseries)
        {
            if (ts?.time == null || !DateTime.TryParse(ts.time, out var tsTime))
            {
                Console.WriteLine($"Weather Plugin: Skipping invalid time: {ts?.time}");
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

            string dateStr = day.Key.ToString("dddd dd MMM", CultureInfo.InvariantCulture);
            row["Date"] = new PluginText($"date_{day.Key:yyyyMMdd}", dateStr);

            var validSymbolCodes = blockData
                .Where(t => t?.data?.next6Hours?.summary?.symbolCode != null)
                .Select(t => new { SymbolCode = t!.data!.next6Hours!.summary!.symbolCode!, Precip = t!.data!.next6Hours!.details?.precipitationAmount ?? 0 })
                .ToList();
            string? weatherStr = validSymbolCodes.Any()
                ? validSymbolCodes
                    .GroupBy(x => x.SymbolCode)
                    .OrderByDescending(g => g.Count())
                    .ThenByDescending(g => g.Sum(x => x.Precip))
                    .First()
                    .Key
                    .Split('_')[0]
                : null;

            string iconName = MapYrSymbolToIcon(weatherStr, validSymbolCodes.Any() ? validSymbolCodes.Max(x => x.Precip) : 0);
            row["Weather"] = new PluginText($"weather_{day.Key:yyyyMMdd}", iconName ?? "-");

            var tempsC = blockData.Select(t => t?.data?.instant?.details?.airTemperature ?? 0).ToList();
            string tempStr = _temperatureUnit == "F"
                ? $"{ConvertCelsius(tempsC.Max()):F0} °F / {ConvertCelsius(tempsC.Min()):F0} °F"
                : $"{tempsC.Max():F0} °C / {tempsC.Min():F0} °C";
            row["Temp"] = new PluginText($"temp_{day.Key:yyyyMMdd}", tempStr);

            float precip = (float)blockData.Sum(t => t?.data?.next6Hours?.details?.precipitationAmount ?? 0);
            row["Precip"] = new PluginSensor($"precip_{day.Key:yyyyMMdd}", "Precip", precip, "mm");

            var windSpeeds = blockData.Select(t => t?.data?.instant?.details?.windSpeed ?? 0).Average();
            var windDir = blockData.Select(t => t?.data?.instant?.details?.windFromDirection ?? 0).Average();
            string windDirStr = GetWindDirection(windDir);
            string windStr = $"{windSpeeds:F1} m/s {windDirStr}";
            row["Wind"] = new PluginText($"wind_{day.Key:yyyyMMdd}", windStr);

            dataTable.Rows.Add(row);
            Console.WriteLine($"Weather Plugin: Added forecast row - Date: {dateStr}, Weather: {iconName}, Temp: {tempStr}, Precip: {precip:F1} mm, Wind: {windStr}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error building forecast table: {ex.Message}");
    }

    return dataTable;
}

        private string GetWindDirection(double degrees)
        {
            string[] directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            int index = (int)Math.Round(degrees / 45.0) % 8;
            return directions[index];
        }

        private double CalculateFeelsLike(double tempC, double windSpeed, double humidity)
        {
            try
            {
                if (tempC < 10 && windSpeed >= 1.33)
                {
                    double windSpeedKmH = windSpeed * 3.6;
                    return Math.Round(13.12 + 0.6215 * tempC - 11.37 * Math.Pow(windSpeedKmH, 0.16) + 0.3965 * tempC * Math.Pow(windSpeedKmH, 0.16), 1);
                }
                return tempC;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error calculating feels-like: {ex.Message}");
                return tempC;
            }
        }

        public class NominatimResult
        {
            [JsonPropertyName("lat")]
            public string? lat { get; set; }
            [JsonPropertyName("lon")]
            public string? lon { get; set; }
        }

        public class YrNowcast
        {
            public YrWeatherProperties? Properties { get; set; }
        }

        public class YrForecast
        {
            public YrWeatherProperties? Properties { get; set; }
        }

        public class YrWeatherProperties
        {
            public YrTimeseries[]? Timeseries { get; set; }
        }

        public class YrTimeseries
        {
            [JsonPropertyName("time")]
            public string? time { get; set; }
            public YrWeatherData? data { get; set; }
        }

        public class YrWeatherData
        {
            public YrWeatherInstantDetails? instant { get; set; }
            [JsonPropertyName("next_1_hours")]
            public YrWeatherNext1Hours? next1Hours { get; set; }
            [JsonPropertyName("next_6_hours")]
            public YrWeatherNext6Hours? next6Hours { get; set; }
        }

        public class YrWeatherInstantDetails
        {
            public YrWeatherDetails? details { get; set; }
        }

        public class YrWeatherDetails
        {
            [JsonPropertyName("air_temperature")]
            public double airTemperature { get; set; }
            [JsonPropertyName("air_pressure_at_sea_level")]
            public double airPressureAtSeaLevel { get; set; }
            [JsonPropertyName("relative_humidity")]
            public double relativeHumidity { get; set; }
            [JsonPropertyName("wind_speed")]
            public double windSpeed { get; set; }
            [JsonPropertyName("wind_from_direction")]
            public double windFromDirection { get; set; }
            [JsonPropertyName("wind_speed_of_gust")]
            public double? windSpeedOfGust { get; set; }
            [JsonPropertyName("cloud_area_fraction")]
            public float cloudAreaFraction { get; set; }
        }

        public class YrWeatherNext1Hours
        {
            public YrWeatherSummary? summary { get; set; }
            public YrWeatherNextDetails? details { get; set; }
        }

        public class YrWeatherNext6Hours
        {
            public YrWeatherSummary? summary { get; set; }
            public YrWeatherNextDetails? details { get; set; }
        }

        public class YrWeatherSummary
        {
            [JsonPropertyName("symbol_code")]
            public string? symbolCode { get; set; }
        }

        public class YrWeatherNextDetails
        {
            [JsonPropertyName("precipitation_amount")]
            public double? precipitationAmount { get; set; }
            [JsonPropertyName("precipitation_category")]
            public string? precipitationCategory { get; set; }
        }
    }
}