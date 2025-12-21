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
using InfoPanel.MetYr.Models;
using InfoPanel.MetYr.Services;

/*
 * Plugin: InfoPanel.MetYr
 * Version: 3.0.1
 * Author: F3NN3X
 * Description: An InfoPanel plugin for retrieving weather data from MET Norway's Yr API (api.met.no). Provides current weather conditions via nowcast (temperature, wind, precipitation, etc.) and a configurable forecast table via locationforecast. Falls back to locationforecast for current data if nowcast unavailable. Supports configurable locations (name or lat/long), temperature units (C/F), date formats, UTC offset adjustment, and custom icon URLs via an INI file, with automatic geocoding using Nominatim when lat/long not provided. Updates hourly by default, with robust null safety and detailed logging. Supports both PNG and SVG icons with standardized naming.
 * Changelog (Recent):
 *   - v2.5.1 (Jun 03, 2025): Added custom date format for forecast table via new ForecastDateFormat option in INI file.
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
        private const string VERSION = "3.0.1";

        private readonly ConfigurationService _configurationService;
        private readonly WeatherService _weatherService;
        private readonly GeocodingService _geocodingService;
        private readonly IconService _iconService;

        private WeatherPluginConfiguration _config = null!;
        private DateTime _lastUpdateTime = DateTime.MinValue;
        private bool _coordinatesSet = false;
        private double _latitude;
        private double _longitude;

        // Plugin Entries
        private readonly PluginText _name = new("name", "Name", "-");
        private readonly PluginText _weather = new("weather", "Weather", "-");
        private readonly PluginText _weatherDesc = new("weather_desc", "Weather Description", "-");
        private readonly PluginText _weatherIcon = new("weather_icon", "Weather Icon", "-");
        private readonly PluginText _weatherIconUrl = new("weather_icon_url", "Weather Icon URL", "-");
        private readonly PluginText _lastRefreshed = new("last_refreshed", "Last Refreshed", "-");

        private readonly PluginText _temp = new("temp", "Temperature", "-");
        private readonly PluginSensor _pressure = new("pressure", "Pressure", 0, "hPa");
        private readonly PluginSensor _seaLevel = new("sea_level", "Sea Level Pressure", 0, "hPa");
        private readonly PluginText _feelsLike = new("feels_like", "Feels Like", "-");
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
            var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add(
                "User-Agent",
                $"InfoPanel-YrWeatherPlugin/{VERSION} (contact@example.com)"
            );

            _configurationService = new ConfigurationService();
            _weatherService = new WeatherService(httpClient);
            _geocodingService = new GeocodingService(httpClient);
            _iconService = new IconService(httpClient);

            _forecastTable = new PluginTable("Forecast", new DataTable(), _forecastTableFormat);
        }

        public override string? ConfigFilePath => _configurationService.ConfigFilePath;
        public override TimeSpan UpdateInterval => TimeSpan.FromMinutes(_config?.RefreshIntervalMinutes ?? 60);

        public override void Initialize()
        {
            _config = _configurationService.LoadConfiguration();

            // Initialize coordinates from config if available
            if (!string.IsNullOrEmpty(_config.Latitude) && !string.IsNullOrEmpty(_config.Longitude) &&
                double.TryParse(_config.Latitude, NumberStyles.Any, CultureInfo.InvariantCulture, out _latitude) &&
                double.TryParse(_config.Longitude, NumberStyles.Any, CultureInfo.InvariantCulture, out _longitude))
            {
                _coordinatesSet = true;
                Console.WriteLine($"Weather Plugin: Coordinates set from INI - Lat: {_latitude}, Lon: {_longitude}");
            }
            else
            {
                _coordinatesSet = false;
                Console.WriteLine($"Weather Plugin: INI Latitude/Longitude invalid or missing, will use geocoding.");
            }

            // Validate icon URL async
            Task.Run(() => _iconService.ValidateIconUrlAsync(_config.IconUrl));

            LogConfigurationSummary();
        }

        private void LogConfigurationSummary()
        {
            Console.WriteLine($"Weather Plugin: Read location from INI: {_config.Location}");
            Console.WriteLine($"Weather Plugin: Refresh interval set to: {_config.RefreshIntervalMinutes} minutes");
            Console.WriteLine($"Weather Plugin: DateTime format set to: {_config.DateTimeFormat}");
            Console.WriteLine($"Weather Plugin: Forecast date format set to: {_config.ForecastDateFormat}");
            Console.WriteLine($"Weather Plugin: UTC offset hours set to: {_config.UtcOffsetHours}");
            Console.WriteLine($"Weather Plugin: Forecast days set to: {_config.ForecastDays}");
            Console.WriteLine($"Weather Plugin: Temperature unit set to: {_config.TemperatureUnit}");
            Console.WriteLine($"Weather Plugin: Altitude set to: {(_config.Altitude.HasValue ? $"{_config.Altitude}m" : "not specified (using API default)")}");
        }

        public override void Update() => throw new NotImplementedException();

        public override async Task UpdateAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested) return;

            var now = DateTime.UtcNow;
            Console.WriteLine($"Weather Plugin: UpdateAsync called at {now:yyyy-MM-ddTHH:mm:ssZ}");

            try
            {
                if (!_coordinatesSet)
                {
                    var coords = await _geocodingService.GetCoordinatesAsync(_config.Location, cancellationToken);
                    if (coords.HasValue)
                    {
                        _latitude = coords.Value.Lat;
                        _longitude = coords.Value.Lon;
                        _coordinatesSet = true;
                        Console.WriteLine($"Weather Plugin: Coordinates set from Nominatim - Lat: {_latitude}, Lon: {_longitude}");
                    }
                    else
                    {
                        // Fallback
                        _latitude = 1.3521; // Singapore
                        _longitude = 103.8198;
                        Console.WriteLine($"Weather Plugin: Using default fallback coordinates: {_latitude}, {_longitude}");
                    }
                }

                if (!cancellationToken.IsCancellationRequested)
                {
                    var weatherData = await _weatherService.GetCurrentWeatherAsync(_latitude, _longitude, _config.Altitude, cancellationToken);
                    if (weatherData != null)
                    {
                        UpdateWeatherData(weatherData);
                    }

                    var forecastData = await _weatherService.GetForecastAsync(_latitude, _longitude, _config.Altitude, cancellationToken);
                    if (forecastData != null)
                    {
                        UpdateForecastTable(forecastData);
                    }

                    _lastUpdateTime = DateTime.UtcNow;
                    UpdateLastRefreshedText();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Weather Plugin: Error in UpdateAsync: {ex.Message}");
            }
        }

        private void UpdateWeatherData(YrTimeseries currentTimeseries)
        {
            if (currentTimeseries?.Data?.Instant?.Details == null) return;

            var instant = currentTimeseries.Data.Instant;
            var next1Hour = currentTimeseries.Data.Next1Hours;

            _name.Value = _config.Location;
            _weather.Value = next1Hour?.Summary?.SymbolCode?.Split('_')[0] ?? "-";
            _weatherDesc.Value = _iconService.MapYrSymbolToDescription(next1Hour?.Summary?.SymbolCode, next1Hour?.Details?.PrecipitationAmount) ?? "-";

            string mappedIcon = _iconService.MapYrSymbolToIcon(next1Hour?.Summary?.SymbolCode, next1Hour?.Details?.PrecipitationAmount);
            _weatherIcon.Value = mappedIcon;

            // Update icon URL
            _weatherIconUrl.Value = _iconService.GetIconUrlAsync(_config.IconUrl, mappedIcon).GetAwaiter().GetResult();

            if (instant.Details != null)
            {
                UpdateTemperatureData(instant.Details);
                UpdateWeatherMetrics(instant.Details, next1Hour);
            }
        }

        private void UpdateTemperatureData(YrWeatherDetails details)
        {
            float tempC = (float)details.AirTemperature;
            float feelsLikeC = (float)CalculateFeelsLike(tempC, details.WindSpeed, details.RelativeHumidity);

            float tempVal = _config.TemperatureUnit == "F" ? ConvertCelsius(tempC) : tempC;
            float feelsLikeVal = _config.TemperatureUnit == "F" ? ConvertCelsius(feelsLikeC) : feelsLikeC;
            string unit = _config.TemperatureUnit == "F" ? "°F" : "°C";

            _temp.Value = $"{tempVal:F1} {unit}";
            _feelsLike.Value = $"{feelsLikeVal:F1} {unit}";
        }

        private void UpdateWeatherMetrics(YrWeatherDetails details, YrWeatherNext1Hours? next1Hour)
        {
            _pressure.Value = (float)details.AirPressureAtSeaLevel;
            _seaLevel.Value = (float)details.AirPressureAtSeaLevel;
            _humidity.Value = (float)details.RelativeHumidity;
            _windSpeed.Value = (float)details.WindSpeed;
            _windDeg.Value = (float)details.WindFromDirection;
            _windGust.Value = (float)(details.WindSpeedOfGust ?? details.WindSpeed);
            _clouds.Value = (float)details.CloudAreaFraction;
            _rain.Value = (float)(next1Hour?.Details?.PrecipitationAmount ?? 0);

            if (next1Hour?.Details?.PrecipitationCategory == "snow" && next1Hour.Details.PrecipitationAmount.HasValue)
            {
                _snow.Value = (float)next1Hour.Details.PrecipitationAmount.Value;
            }
            else
            {
                _snow.Value = 0;
            }
        }

        private void UpdateForecastTable(YrTimeseries[] timeseries)
        {
            var dataTable = new DataTable();
            InitializeForecastTableColumns(dataTable);

            var dailyData = GetDailyForecastData(timeseries);

            foreach (var dayData in dailyData.OrderBy(d => d.Key))
            {
                AddDayToForecastTable(dataTable, dayData.Key, dayData.Value);
            }

            _forecastTable.Value = dataTable;
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
            var startTime = now.AddDays(1).Date;
            var endTime = startTime.AddDays(_config.ForecastDays);
            var dailyBlocks = new Dictionary<DateTime, List<YrTimeseries>>();

            foreach (var ts in timeseries)
            {
                if (ts?.Time == null || !DateTime.TryParse(ts.Time, out var tsTime)) continue;

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

            string dateStr = day.ToString(_config.ForecastDateFormat, CultureInfo.InvariantCulture);
            row["Date"] = new PluginText($"date_{day:yyyyMMdd}", dateStr);

            var weatherData = GetDominantWeatherForDay(blockData);
            string description = _iconService.MapYrSymbolToDescription(weatherData.SymbolCode, weatherData.MaxPrecipitation);
            row["Weather"] = new PluginText($"weather_{day:yyyyMMdd}", description ?? "-");

            var tempsC = blockData.Select(t => t?.Data?.Instant?.Details?.AirTemperature ?? 0).ToList();
            string tempStr = FormatTemperatureRange(tempsC.Max(), tempsC.Min());
            row["Temp"] = new PluginText($"temp_{day:yyyyMMdd}", tempStr);

            float precip = (float)blockData.Sum(t => t?.Data?.Next6Hours?.Details?.PrecipitationAmount ?? 0);
            row["Precip"] = new PluginSensor($"precip_{day:yyyyMMdd}", "Precip", precip, "mm");

            string windStr = GetAverageWindString(blockData);
            row["Wind"] = new PluginText($"wind_{day:yyyyMMdd}", windStr);

            dataTable.Rows.Add(row);
        }

        private string GetAverageWindString(List<YrTimeseries> blockData)
        {
            var windSpeeds = blockData.Select(t => t?.Data?.Instant?.Details?.WindSpeed ?? 0).Average();
            var windDir = blockData.Select(t => t?.Data?.Instant?.Details?.WindFromDirection ?? 0).Average();
            string windDirStr = GetWindDirection(windDir);
            return $"{windSpeeds:F1} m/s {windDirStr}";
        }

        private string FormatTemperatureRange(double maxTemp, double minTemp)
        {
            if (_config.TemperatureUnit == "F")
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
                .Where(t => t?.Data?.Next6Hours?.Summary?.SymbolCode != null)
                .Select(t => new
                {
                    SymbolCode = t!.Data!.Next6Hours!.Summary!.SymbolCode!,
                    Precip = t!.Data!.Next6Hours!.Details?.PrecipitationAmount ?? 0
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

        private void UpdateLastRefreshedText()
        {
            try
            {
                DateTime adjustedTime = _lastUpdateTime.AddHours(_config.UtcOffsetHours);
                string formattedTime = adjustedTime.ToString(_config.DateTimeFormat, CultureInfo.InvariantCulture);
                _lastRefreshed.Value = formattedTime;
            }
            catch (FormatException)
            {
                DateTime adjustedTime = _lastUpdateTime.AddHours(_config.UtcOffsetHours);
                _lastRefreshed.Value = adjustedTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
            }
        }

        private string GetWindDirection(double degrees)
        {
            string[] directions = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            degrees = ((degrees % 360) + 360) % 360;
            int index = (int)Math.Round(degrees / 45.0) % 8;
            return directions[index];
        }

        private double CalculateFeelsLike(double tempC, double windSpeed, double humidity)
        {
            // Wind chill calculation for cold temperatures with sufficient wind
            if (tempC < 10.0 && windSpeed >= 1.33)
            {
                double windSpeedKmH = windSpeed * 3.6; // Convert m/s to km/h
                return Math.Round(13.12 + 0.6215 * tempC - 11.37 * Math.Pow(windSpeedKmH, 0.16) + 0.3965 * tempC * Math.Pow(windSpeedKmH, 0.16), 1);
            }
            return tempC;
        }

        private float ConvertCelsius(double celsius) => (float)(celsius * 9.0 / 5.0 + 32.0);

        public override void Close() { }

        public override void Load(List<IPluginContainer> containers)
        {
            var container = new PluginContainer(_config?.Location ?? $"Lat:{_latitude}, Lon:{_longitude}");
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


}