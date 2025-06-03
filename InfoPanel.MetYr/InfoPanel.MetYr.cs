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
 * Version: 2.5.0
 * Author: F3NN3X
 * Description: An InfoPanel plugin for retrieving weather data from MET Norway's Yr API (api.met.no). Provides current weather conditions via nowcast (temperature, wind, precipitation, etc.) and a configurable forecast table via locationforecast. Falls back to locationforecast for current data if nowcast unavailable. Supports configurable locations (name or lat/long), temperature units (C/F), date formats, UTC offset adjustment, and custom icon URLs via an INI file, with automatic geocoding using Nominatim when lat/long not provided. Updates hourly by default, with robust null safety and detailed logging. Supports both PNG and SVG icons with standardized naming.
 * Changelog (Recent):
 *   - v2.5.0 (Jun 03, 2025): Major code improvements: Added constants for configuration values, enhanced null safety, improved async behavior, optimized large methods, improved error handling, and added better icon mapping.
 *   - v2.0.2 (Jun 02, 2025): Updated forecast table's Weather column to use human-readable descriptions from MapYrSymbolToDescription (e.g., "Moderate Rain" instead of "rainy-2").
 *   - v2.0.1 (Jun 02, 2025): Improved _weatherDesc formatting by mapping symbolCode to human-readable descriptions (e.g., "lightrain" to "Light Rain"), removing day/night suffixes, and applying title case.
 *   - v2.0.0 (Jun 02, 2025): Consolidated changes: fixed compilation errors, added null reference checks, updated icon mapping, corrected duplicate class definitions, ensured hyphenated icon file names, changed wind direction labels to abbreviations, and moved plugin info text below using statements.
 * Note: Full history in CHANGELOG.md. Requires internet access for API calls.
 */

namespace InfoPanel.MetYr
{
    public class YrWeatherPlugin : BasePlugin
    {
        // Constants
        private const string VERSION = "2.5.0";
        private const string DEFAULT_LOCATION = "Oslo, Norway";
        private const string DEFAULT_USER_AGENT = "InfoPanel-YrWeatherPlugin/2.5.0 (contact@example.com)";
        private const string DEFAULT_DATE_FORMAT = "yyyy-MM-dd HH:mm";
        private const int DEFAULT_REFRESH_INTERVAL = 60; // minutes
        private const int DEFAULT_FORECAST_DAYS = 5;
        private const string DEFAULT_TEMP_UNIT = "C";
        
        // API endpoints
        private const string NOMINATIM_API_URL = "https://nominatim.openstreetmap.org/search";
        private const string MET_NOWCAST_API_URL = "https://api.met.no/weatherapi/nowcast/2.0/complete";
        private const string MET_FORECAST_API_URL = "https://api.met.no/weatherapi/locationforecast/2.0/complete";
        private const string OPENWEATHERMAP_ICON_URL = "https://openweathermap.org/img/wn/";
        
        // Default coordinates (Singapore)
        private const double DEFAULT_LATITUDE = 1.3521;
        private const double DEFAULT_LONGITUDE = 103.8198;
        
        // Precipitation thresholds for intensity classification
        private const double LIGHT_PRECIP_THRESHOLD = 2.5; // mm/h
        private const double HEAVY_PRECIP_THRESHOLD = 7.5; // mm/h
        
        // Temperature threshold for wind chill calculation
        private const double WIND_CHILL_TEMP_THRESHOLD = 10.0; // °C
        private const double WIND_CHILL_SPEED_THRESHOLD = 1.33; // m/s

        private static readonly HttpClient _httpClient = new HttpClient();
        
        private double _latitude;
        private double _longitude;
        private string? _location;
        private bool _coordinatesSet = false;
        private string? _iconUrl;

        private int _refreshIntervalMinutes = DEFAULT_REFRESH_INTERVAL;
        private DateTime _lastUpdateTime = DateTime.MinValue;
        private string _dateTimeFormat = DEFAULT_DATE_FORMAT;
        private double _utcOffsetHours = 0;
        private int _forecastDays = DEFAULT_FORECAST_DAYS;
        private string _temperatureUnit = DEFAULT_TEMP_UNIT;

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
                "InfoPanel-YrWeatherPlugin/2.5.0 (contact@example.com)"
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
                    CreateDefaultIniFile(parser);
                }
                else
                {
                    Console.WriteLine($"Weather Plugin: Reading INI file from {_configFilePath}");
                    config = parser.ReadFile(_configFilePath);
                    string iniContent = File.ReadAllText(_configFilePath);
                    Console.WriteLine($"Weather Plugin: INI file content:\n{iniContent}");

                    LoadConfigurationFromIni(config);
                    
                    // We initialize async work but don't wait for completion in Initialize
                    // This will be completed by the time UpdateAsync runs
                    Task.Run(() => ValidateIconUrlAsync(_iconUrl));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Weather Plugin: Error reading INI file '{_configFilePath}': {ex.Message}");
                SetDefaultConfiguration();
            }

            LogConfigurationSummary();
        }
        
        private void CreateDefaultIniFile(FileIniDataParser parser)
        {
            var config = new IniData();
            config["Yr Weather Plugin"]["Location"] = DEFAULT_LOCATION;
            config["Yr Weather Plugin"]["Latitude"] = "";
            config["Yr Weather Plugin"]["Longitude"] = "";
            config["Yr Weather Plugin"]["RefreshIntervalMinutes"] = DEFAULT_REFRESH_INTERVAL.ToString();
            config["Yr Weather Plugin"]["DateTimeFormat"] = DEFAULT_DATE_FORMAT;
            config["Yr Weather Plugin"]["UtcOffsetHours"] = "0";
            config["Yr Weather Plugin"]["ForecastDays"] = DEFAULT_FORECAST_DAYS.ToString();
            config["Yr Weather Plugin"]["TemperatureUnit"] = DEFAULT_TEMP_UNIT;
            config["Yr Weather Plugin"]["IconUrl"] = "";
            parser.WriteFile(_configFilePath, config);
            
            SetDefaultConfiguration();
        }
        
        private void SetDefaultConfiguration()
        {
            _location = DEFAULT_LOCATION;
            _refreshIntervalMinutes = DEFAULT_REFRESH_INTERVAL;
            _dateTimeFormat = DEFAULT_DATE_FORMAT;
            _utcOffsetHours = 0;
            _forecastDays = DEFAULT_FORECAST_DAYS;
            _temperatureUnit = DEFAULT_TEMP_UNIT;
            _iconUrl = null;
        }
        
        private void LoadConfigurationFromIni(IniData config)
        {
            _location = config["Yr Weather Plugin"]["Location"] ?? DEFAULT_LOCATION;
            _iconUrl = config["Yr Weather Plugin"]["IconUrl"]?.Trim();

            string latStr = config["Yr Weather Plugin"]["Latitude"]?.Trim() ?? "";
            string lonStr = config["Yr Weather Plugin"]["Longitude"]?.Trim() ?? "";

            if (!int.TryParse(config["Yr Weather Plugin"]["RefreshIntervalMinutes"], out _refreshIntervalMinutes) || _refreshIntervalMinutes <= 0)
                _refreshIntervalMinutes = DEFAULT_REFRESH_INTERVAL;

            string formatFromIni = config["Yr Weather Plugin"]["DateTimeFormat"] ?? DEFAULT_DATE_FORMAT;
            _dateTimeFormat = ValidateDateTimeFormat(formatFromIni) ? formatFromIni : DEFAULT_DATE_FORMAT;

            if (!double.TryParse(config["Yr Weather Plugin"]["UtcOffsetHours"], NumberStyles.Any, CultureInfo.InvariantCulture, out _utcOffsetHours))
            {
                Console.WriteLine($"Weather Plugin: Invalid UtcOffsetHours '{config["Yr Weather Plugin"]["UtcOffsetHours"]}', defaulting to 0");
                _utcOffsetHours = 0;
            }

            if (!int.TryParse(config["Yr Weather Plugin"]["ForecastDays"], out _forecastDays) || _forecastDays < 1 || _forecastDays > 10)
                _forecastDays = DEFAULT_FORECAST_DAYS;

            _temperatureUnit = config["Yr Weather Plugin"]["TemperatureUnit"]?.ToUpper() == "F" ? "F" : "C";
            string tempUnit = _temperatureUnit == "F" ? "°F" : "°C";
            _temp = new PluginSensor("temp", "Temperature", _temp.Value, tempUnit);
            _feelsLike = new PluginSensor("feels_like", "Feels Like", _feelsLike.Value, tempUnit);

            LoadCoordinatesFromIni(latStr, lonStr);
        }
        
        private void LoadCoordinatesFromIni(string latStr, string lonStr)
        {
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
        }
        
        private void LogConfigurationSummary()
        {
            Console.WriteLine($"Weather Plugin: Read location from INI: {_location}");
            Console.WriteLine($"Weather Plugin: Refresh interval set to: {_refreshIntervalMinutes} minutes");
            Console.WriteLine($"Weather Plugin: DateTime format set to: {_dateTimeFormat}");
            Console.WriteLine($"Weather Plugin: UTC offset hours set to: {_utcOffsetHours}");
            Console.WriteLine($"Weather Plugin: Forecast days set to: {_forecastDays}");
            Console.WriteLine($"Weather Plugin: Temperature unit set to: {_temperatureUnit}");
        }
        
        private async Task ValidateIconUrlAsync(string? iconUrl)
        {
            Console.WriteLine($"Weather Plugin: Validating IconUrl: '{iconUrl}'");
            if (string.IsNullOrEmpty(iconUrl))
            {
                Console.WriteLine("Weather Plugin: IconUrl is empty or not set, using default OpenWeatherMap icons.");
                _iconUrl = null;
                return;
            }
            
            if (!Uri.IsWellFormedUriString(iconUrl, UriKind.Absolute))
            {
                Console.WriteLine($"Weather Plugin: Invalid IconUrl '{iconUrl}', defaulting to OpenWeatherMap icons.");
                _iconUrl = null;
                return;
            }
            
            Console.WriteLine($"Weather Plugin: Testing custom IconUrl: {iconUrl}");
            try
            {
                string testSvgUrl = $"{iconUrl.TrimEnd('/')}/clear-day.svg";
                var testResponse = await _httpClient.GetAsync(testSvgUrl);
                if (testResponse.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Weather Plugin: Successfully accessed test SVG icon: {testSvgUrl}");
                }
                else
                {
                    Console.WriteLine($"Weather Plugin: Test SVG icon not found at {testSvgUrl}, status: {testResponse.StatusCode}");
                    string testPngUrl = $"{iconUrl.TrimEnd('/')}/clear-day.png";
                    var pngResponse = await _httpClient.GetAsync(testPngUrl);
                    if (pngResponse.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"Weather Plugin: Successfully accessed test PNG icon: {testPngUrl}");
                    }
                    else
                    {
                        Console.WriteLine($"Weather Plugin: Test PNG icon not found either, defaulting to OpenWeatherMap icons.");
                        _iconUrl = null;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Weather Plugin: Error accessing test icon URL '{iconUrl}': {ex.Message}, defaulting to OpenWeatherMap icons.");
                _iconUrl = null;
            }
        }

        private async Task GetWeather(CancellationToken cancellationToken)
        {
            try
            {
                string latStr = _latitude.ToString("0.0000", CultureInfo.InvariantCulture);
                string lonStr = _longitude.ToString("0.0000", CultureInfo.InvariantCulture);
                string nowcastUrl = $"{MET_NOWCAST_API_URL}?lat={latStr}&lon={lonStr}";
                string forecastUrl = $"{MET_FORECAST_API_URL}?lat={latStr}&lon={lonStr}";

                YrTimeseries? currentTimeseries = await FetchCurrentWeatherData(nowcastUrl, forecastUrl, cancellationToken);
                await UpdateForecastTable(forecastUrl, cancellationToken);
                
                if (currentTimeseries?.data?.instant?.details != null)
                {
                    UpdateWeatherData(currentTimeseries);
                }
                else
                {
                    Console.WriteLine("Weather Plugin: No valid timeseries data available.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Weather Plugin: Error fetching weather: {ex.Message}");
            }
        }

        private async Task<YrTimeseries?> FetchCurrentWeatherData(string nowcastUrl, string forecastUrl, CancellationToken cancellationToken)
        {
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
                
                // Try getting current weather from forecast if nowcast fails
                var forecastResponse = await _httpClient.GetAsync(forecastUrl, cancellationToken);
                if (forecastResponse.IsSuccessStatusCode)
                {
                    var forecastJson = await forecastResponse.Content.ReadAsStringAsync(cancellationToken);
                    var forecast = JsonSerializer.Deserialize<YrForecast>(
                        forecastJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                    );

                    if (forecast?.Properties?.Timeseries?.Length > 0)
                    {
                        currentTimeseries = forecast.Properties.Timeseries[0];
                        Console.WriteLine($"Weather Plugin: Using forecast timeseries: {currentTimeseries.time}");
                    }
                }
                else
                {
                    Console.WriteLine($"Weather Plugin: Forecast API call failed: {forecastResponse.StatusCode}");
                }
            }
            
            return currentTimeseries;
        }
        
        private async Task UpdateForecastTable(string forecastUrl, CancellationToken cancellationToken)
        {
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
        }
        
        private void UpdateWeatherData(YrTimeseries currentTimeseries)
        {
            if (currentTimeseries?.data?.instant?.details == null)
            {
                Console.WriteLine("Weather Plugin: Missing instant details in weather data");
                return;
            }

            var instant = currentTimeseries.data.instant;
            var next1Hour = currentTimeseries.data.next1Hours;

            // Update basic weather information
            _name.Value = _location ?? $"Lat:{_latitude.ToString(CultureInfo.InvariantCulture)}, Lon:{_longitude.ToString(CultureInfo.InvariantCulture)}";
            _weather.Value = next1Hour?.summary?.symbolCode?.Split('_')[0] ?? "-";
            _weatherDesc.Value = MapYrSymbolToDescription(next1Hour?.summary?.symbolCode, next1Hour?.details?.precipitationAmount) ?? "-";

            // Update weather icon
            string mappedIcon = MapYrSymbolToIcon(next1Hour?.summary?.symbolCode, next1Hour?.details?.precipitationAmount);
            _weatherIcon.Value = mappedIcon;
            Console.WriteLine($"Weather Plugin: Mapped symbol '{next1Hour?.summary?.symbolCode}' to icon: {mappedIcon}, description: {_weatherDesc.Value}");

            // Set the icon URL (either custom or default)
            UpdateWeatherIconUrl(mappedIcon, cancellationToken: CancellationToken.None).GetAwaiter().GetResult();

            // Update temperature data
            if (instant.details != null)
            {
                UpdateTemperatureData(instant.details);
                
                // Update other weather metrics
                UpdateWeatherMetrics(instant.details, next1Hour);
            }
            else
            {
                Console.WriteLine("Weather Plugin: Missing details in instant data");
            }
        }
        
        private void UpdateTemperatureData(YrWeatherDetails details)
        {
            float tempC = (float)details.airTemperature;
            float feelsLikeC = (float)CalculateFeelsLike(tempC, details.windSpeed, details.relativeHumidity);
            _temp.Value = _temperatureUnit == "F" ? ConvertCelsius(tempC) : tempC;
            _feelsLike.Value = _temperatureUnit == "F" ? ConvertCelsius(feelsLikeC) : feelsLikeC;
        }
        
        private void UpdateWeatherMetrics(YrWeatherDetails details, YrWeatherNext1Hours? next1Hour) 
        {
            _pressure.Value = (float)details.airPressureAtSeaLevel;
            _seaLevel.Value = (float)details.airPressureAtSeaLevel;
            _humidity.Value = (float)details.relativeHumidity;
            _windSpeed.Value = (float)details.windSpeed;
            _windDeg.Value = (float)details.windFromDirection;
            _windGust.Value = (float)(details.windSpeedOfGust ?? details.windSpeed);
            _clouds.Value = (float)details.cloudAreaFraction;
            _rain.Value = (float)(next1Hour?.details?.precipitationAmount ?? 0);
            
            // Fix nullable warning by ensuring we access precipitationAmount safely
            if (next1Hour?.details?.precipitationCategory == "snow" && next1Hour.details.precipitationAmount.HasValue)
            {
                _snow.Value = (float)next1Hour.details.precipitationAmount.Value;
            }
            else
            {
                _snow.Value = 0;
            }

            Console.WriteLine($"Weather Plugin: Set data - Temp: {_temp.Value}{_temp.Unit}, Weather: {_weather.Value}, Description: {_weatherDesc.Value}, Icon: {_weatherIcon.Value}");
        }
        
        private async Task UpdateWeatherIconUrl(string mappedIcon, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrEmpty(_iconUrl))
            {
                Console.WriteLine($"Weather Plugin: Using custom icon URL: {_iconUrl}");
                string iconFileName = mappedIcon; // Already hyphenated from mapping
                await TrySetCustomIconUrl(iconFileName, cancellationToken);
            }
            else
            {
                Console.WriteLine("Weather Plugin: No custom IconUrl, using OpenWeatherMap icons.");
                SetDefaultOpenWeatherMapIcon();
            }
        }
        
        private async Task TrySetCustomIconUrl(string iconFileName, CancellationToken cancellationToken)
        {
            string svgUrl = $"{_iconUrl!.TrimEnd('/')}/{iconFileName}.svg";
            string pngUrl = $"{_iconUrl.TrimEnd('/')}/{iconFileName}.png";
            
            try
            {
                Console.WriteLine($"Weather Plugin: Checking SVG: {svgUrl}");
                var svgResponse = await _httpClient.GetAsync(svgUrl, cancellationToken);
                
                if (svgResponse.IsSuccessStatusCode)
                {
                    _weatherIconUrl.Value = svgUrl;
                    Console.WriteLine($"Weather Plugin: Using SVG: {_weatherIconUrl.Value}");
                }
                else
                {
                    Console.WriteLine($"Weather Plugin: SVG not found, checking PNG: {pngUrl}");
                    var pngResponse = await _httpClient.GetAsync(pngUrl, cancellationToken);
                    
                    if (pngResponse.IsSuccessStatusCode)
                    {
                        _weatherIconUrl.Value = pngUrl;
                        Console.WriteLine($"Weather Plugin: Using PNG: {_weatherIconUrl.Value}");
                    }
                    else
                    {
                        Console.WriteLine("Weather Plugin: PNG not found, using OpenWeatherMap.");
                        SetDefaultOpenWeatherMapIcon();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Weather Plugin: Error checking icon '{svgUrl}': {ex.Message}, using OpenWeatherMap.");
                SetDefaultOpenWeatherMapIcon();
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
                    SetFallbackCoordinates();
                    return;
                }

                string nominatimUrl = $"{NOMINATIM_API_URL}?q={Uri.EscapeDataString(location)}&format=json&limit=1";
                Console.WriteLine($"Weather Plugin: Geocoding URL: {nominatimUrl}");
                var response = await _httpClient.GetAsync(nominatimUrl, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync(cancellationToken);
                    Console.WriteLine($"Weather Plugin: Nominatim response: {json}");
                    var results = JsonSerializer.Deserialize<NominatimResult[]>(json);
                    
                    if (results?.Length > 0 && !string.IsNullOrEmpty(results[0].lat) && !string.IsNullOrEmpty(results[0].lon))
                    {
                        try
                        {
                            // Use null-forgiving operator to tell the compiler we've already checked for null
                            // Using ! to assert that results[0].lat and results[0].lon are not null at this point
                            _latitude = double.Parse(results[0].lat!, CultureInfo.InvariantCulture);
                            _longitude = double.Parse(results[0].lon!, CultureInfo.InvariantCulture);
                            Console.WriteLine(
                                $"Weather Plugin: Coordinates set from Nominatim - Lat: {_latitude}, Lon: {_longitude}"
                            );
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Weather Plugin: Error parsing coordinates - Lat: {results[0].lat}, Lon: {results[0].lon}, Error: {ex.Message}");
                            SetFallbackCoordinates();
                        }
                    }
                    else
                    {
                        Console.WriteLine("Weather Plugin: No valid geocoding results available, using fallback.");
                        SetFallbackCoordinates();
                    }
                }
                else
                {
                    Console.WriteLine($"Weather Plugin: Geocoding failed with status: {response.StatusCode}");
                    SetFallbackCoordinates();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Weather Plugin: Error geocoding location '{location}': {ex.Message}");
                SetFallbackCoordinates();
            }
        }

        private void SetFallbackCoordinates()
        {
            _latitude = DEFAULT_LATITUDE;
            _longitude = DEFAULT_LONGITUDE;
            Console.WriteLine($"Weather Plugin: Using default fallback coordinates: {_latitude}, {_longitude}");
        }

        private float ConvertCelsius(double celsius) => (float)(celsius * 9.0 / 5.0 + 32.0);

        private string MapYrSymbolToIcon(string? symbolCode, double? precipitationAmount)
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

        private string MapYrSymbolToDescription(string? symbolCode, double? precipitationAmount)
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

        private double CalculateFeelsLike(double tempC, double windSpeed, double humidity)
        {
            try
            {
                // Wind chill calculation for cold temperatures with sufficient wind
                if (tempC < WIND_CHILL_TEMP_THRESHOLD && windSpeed >= WIND_CHILL_SPEED_THRESHOLD)
                {
                    double windSpeedKmH = windSpeed * 3.6; // Convert m/s to km/h
                    return Math.Round(13.12 + 0.6215 * tempC - 11.37 * Math.Pow(windSpeedKmH, 0.16) + 0.3965 * tempC * Math.Pow(windSpeedKmH, 0.16), 1);
                }
                
                // For other conditions, return the actual temperature
                return tempC;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Weather Plugin: Error calculating feels-like: {ex.Message}");
                return tempC; // Fallback to actual temperature on error
            }
        }

        private DataTable BuildForecastTable(YrTimeseries[] timeseries)
        {
            var dataTable = new DataTable();
            try
            {
                InitializeForecastTableColumns(dataTable);
                
                var dailyData = GetDailyForecastData(timeseries);
                
                foreach (var dayData in dailyData.OrderBy(d => d.Key))
                {
                    AddDayToForecastTable(dataTable, dayData.Key, dayData.Value);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Weather Plugin: Error building forecast table: {ex.Message}");
            }

            return dataTable;
        }
        
        private void InitializeForecastTableColumns(DataTable dataTable)
        {
            dataTable.Columns.Add("Date", typeof(PluginText));
            dataTable.Columns.Add("Weather", typeof(PluginText));
            dataTable.Columns.Add("Temp", typeof(PluginText));
            dataTable.Columns.Add("Precip", typeof(PluginSensor));
            dataTable.Columns.Add("Wind", typeof(PluginText));
        }
        
        private Dictionary<DateTime, List<YrTimeseries>> GetDailyForecastData(YrTimeseries[] timeseries)
        {
            var now = DateTime.UtcNow;
            var startTime = now.AddDays(1).Date; // Start from tomorrow
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
            
            return dailyBlocks;
        }
        
        private void AddDayToForecastTable(DataTable dataTable, DateTime day, List<YrTimeseries> blockData)
        {
            var row = dataTable.NewRow();
            
            // Add date column
            string dateStr = day.ToString("dddd dd MMM", CultureInfo.InvariantCulture);
            row["Date"] = new PluginText($"date_{day:yyyyMMdd}", dateStr);
            
            // Add weather column with description
            var weatherData = GetDominantWeatherForDay(blockData);
            string description = MapYrSymbolToDescription(weatherData.SymbolCode, weatherData.MaxPrecipitation);
            row["Weather"] = new PluginText($"weather_{day:yyyyMMdd}", description ?? "-");
            
            // Add temperature column
            var tempsC = blockData.Select(t => t?.data?.instant?.details?.airTemperature ?? 0).ToList();
            string tempStr = FormatTemperatureRange(tempsC.Max(), tempsC.Min());
            row["Temp"] = new PluginText($"temp_{day:yyyyMMdd}", tempStr);
            
            // Add precipitation column
            float precip = (float)blockData.Sum(t => t?.data?.next6Hours?.details?.precipitationAmount ?? 0);
            row["Precip"] = new PluginSensor($"precip_{day:yyyyMMdd}", "Precip", precip, "mm");
            
            // Add wind column
            string windStr = GetAverageWindString(blockData);
            row["Wind"] = new PluginText($"wind_{day:yyyyMMdd}", windStr);
            
            dataTable.Rows.Add(row);
            Console.WriteLine($"Weather Plugin: Added forecast row - Date: {dateStr}, Weather: {description}, Temp: {tempStr}, Precip: {precip:F1} mm, Wind: {windStr}");
        }
        
        private string GetAverageWindString(List<YrTimeseries> blockData)
        {
            var windSpeeds = blockData.Select(t => t?.data?.instant?.details?.windSpeed ?? 0).Average();
            var windDir = blockData.Select(t => t?.data?.instant?.details?.windFromDirection ?? 0).Average();
            string windDirStr = GetWindDirection(windDir);
            return $"{windSpeeds:F1} m/s {windDirStr}";
        }
        
        private string FormatTemperatureRange(double maxTemp, double minTemp)
        {
            if (_temperatureUnit == "F")
            {
                return $"{ConvertCelsius(maxTemp):F0} °F / {ConvertCelsius(minTemp):F0} °F";
            }
            else
            {
                return $"{maxTemp:F0} °C / {minTemp:F0} °C";
            }
        }
        
        private (string? SymbolCode, double MaxPrecipitation) GetDominantWeatherForDay(List<YrTimeseries> blockData)
        {
            var validSymbolCodes = blockData
                .Where(t => t?.data?.next6Hours?.summary?.symbolCode != null)
                .Select(t => new { 
                    SymbolCode = t!.data!.next6Hours!.summary!.symbolCode!, 
                    Precip = t!.data!.next6Hours!.details?.precipitationAmount ?? 0 
                })
                .ToList();
                
            if (!validSymbolCodes.Any())
                return (null, 0);
                
            var dominantWeather = validSymbolCodes
                .GroupBy(x => x.SymbolCode)
                .OrderByDescending(g => g.Count())
                .ThenByDescending(g => g.Sum(x => x.Precip))
                .First();
                
            return (dominantWeather.Key, validSymbolCodes.Max(x => x.Precip));
        }
        
        private void SetDefaultOpenWeatherMapIcon()
        {
            // Map our internal icon codes to OpenWeatherMap's icon code system
            string iconCode = _weatherIcon.Value switch
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
            _weatherIconUrl.Value = $"{OPENWEATHERMAP_ICON_URL}{iconCode}@4x.png";
            Console.WriteLine($"Weather Plugin: Using OpenWeatherMap icon: {_weatherIconUrl.Value}");
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

            try 
            {
                // Ensure coordinates are set before fetching weather
                if (!_coordinatesSet)
                {
                    await SetCoordinatesFromLocation(_location, cancellationToken);
                    _coordinatesSet = true;
                }

                // Only proceed if not cancelled
                if (!cancellationToken.IsCancellationRequested)
                {
                    await GetWeather(cancellationToken);
                    _lastUpdateTime = DateTime.UtcNow;
                    UpdateLastRefreshedText();
                }
                else
                {
                    Console.WriteLine("Weather Plugin: Weather fetch cancelled.");
                }
            }
            catch (Exception ex) 
            {
                Console.WriteLine($"Weather Plugin: Error in UpdateAsync: {ex.Message}");
            }
        }
        
        private void UpdateLastRefreshedText()
        {
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
                
                // Fall back to a standard format if the custom format fails
                DateTime adjustedTime = _lastUpdateTime.AddHours(_utcOffsetHours);
                _lastRefreshed.Value = adjustedTime.ToString(DEFAULT_DATE_FORMAT, CultureInfo.InvariantCulture);
                Console.WriteLine($"Weather Plugin: Fell back to default format: {_lastRefreshed.Value}");
            }
        }

        private string GetWindDirection(double degrees)
        {
            // Convert degrees to one of 8 cardinal directions (N, NE, E, SE, S, SW, W, NW)
            string[] directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            
            // Normalize the direction to 0-360 range
            degrees = ((degrees % 360) + 360) % 360;
            
            // Each direction covers 45 degrees, with the center of "N" at 0/360 degrees
            // Math.Round ensures we get the closest cardinal direction
            int index = (int)Math.Round(degrees / 45.0) % 8;
            return directions[index];
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

        public override void Close() 
        {
            // Nothing to clean up
        }

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