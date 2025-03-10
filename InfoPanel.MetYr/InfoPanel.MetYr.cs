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

namespace InfoPanel.Extras
{
    public class YrWeatherPlugin : BasePlugin
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private double _latitude;
        private double _longitude;
        private string? _location;
        private bool _coordinatesSet = false;
        private int _refreshIntervalMinutes = 60;
        private DateTime _lastUpdateTime = DateTime.MinValue; // Track last update

        private readonly PluginText _name = new("name", "Name", "-");
        private readonly PluginText _weather = new("weather", "Weather", "-");
        private readonly PluginText _weatherDesc = new("weather_desc", "Weather Description", "-");
        private readonly PluginText _weatherIcon = new("weather_icon", "Weather Icon", "-");
        private readonly PluginText _weatherIconUrl = new("weather_icon_url", "Weather Icon URL", "-");

        private readonly PluginSensor _temp = new("temp", "Temperature", 0, "°C");
        private readonly PluginSensor _pressure = new("pressure", "Pressure", 0, "hPa");
        private readonly PluginSensor _seaLevel = new("sea_level", "Sea Level Pressure", 0, "hPa");
        private readonly PluginSensor _feelsLike = new("feels_like", "Feels Like", 0, "°C");
        private readonly PluginSensor _humidity = new("humidity", "Humidity", 0, "%");

        private readonly PluginSensor _windSpeed = new("wind_speed", "Wind Speed", 0, "m/s");
        private readonly PluginSensor _windDeg = new("wind_deg", "Wind Degree", 0, "°");
        private readonly PluginSensor _windGust = new("wind_gust", "Wind Gust", 0, "m/s");

        private readonly PluginSensor _clouds = new("clouds", "Clouds", 0, "%");
        private readonly PluginSensor _rain = new("rain", "Rain", 0, "mm/h");
        private readonly PluginSensor _snow = new("snow", "Snow", 0, "mm/h");

        public YrWeatherPlugin()
            : base(
                "yr-weather-plugin",
                "Weather Info - MET/Yr",
                "Retrieves current weather information from api.met.no using location names."
            )
        {
            _httpClient.DefaultRequestHeaders.Add(
                "User-Agent",
                "InfoPanel-YrWeatherPlugin/1.0 (hello@themely.dev)"
            );
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
                config["Yr Weather Plugin"]["RefreshIntervalMinutes"] = "60";
                parser.WriteFile(_configFilePath, config);
                _location = "Porsgrunn, Norway";
                _refreshIntervalMinutes = 60;
            }
            else
            {
                config = parser.ReadFile(_configFilePath);
                _location = config["Yr Weather Plugin"]["Location"] ?? "Oslo, Norway";
                if (!int.TryParse(config["Yr Weather Plugin"]["RefreshIntervalMinutes"], out _refreshIntervalMinutes) || _refreshIntervalMinutes <= 0)
                {
                    _refreshIntervalMinutes = 60;
                }
            }

            Console.WriteLine($"Weather Plugin: Read location from INI: {_location}");
            Console.WriteLine($"Weather Plugin: Refresh interval set to: {_refreshIntervalMinutes} minutes");
        }

        public override void Close()
        {
            // No disposal of static HttpClient
        }

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
                ]
            );
            containers.Add(container);
        }

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

        public override void Update()
        {
            throw new NotImplementedException();
        }

        public override async Task UpdateAsync(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Console.WriteLine("Weather Plugin: UpdateAsync cancelled before starting.");
                return;
            }

            // Log refresh timing
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
                _lastUpdateTime = DateTime.UtcNow; // Update last fetch time
            }
            else
            {
                Console.WriteLine("Weather Plugin: Weather fetch cancelled.");
            }
        }

        private async Task SetCoordinatesFromLocation(
            string? location,
            CancellationToken cancellationToken
        )
        {
            if (string.IsNullOrEmpty(location))
            {
                Console.WriteLine("Weather Plugin: Location is empty, using fallback coordinates.");
                _latitude = 1.3521;
                _longitude = 103.8198;
                return;
            }

            try
            {
                string nominatimUrl =
                    $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(location)}&format=json&limit=1";
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
                            Console.WriteLine(
                                $"Weather Plugin: Error parsing coordinates - Lat: {results[0].Lat}, Lon: {results[0].Lon}, Error: {ex.Message}"
                            );
                            _latitude = 1.3521;
                            _longitude = 103.8198;
                        }
                    }
                    else
                    {
                        Console.WriteLine(
                            "Weather Plugin: No geocoding results found, using fallback."
                        );
                        _latitude = 1.3521;
                        _longitude = 103.8198;
                    }
                }
                else
                {
                    Console.WriteLine(
                        $"Weather Plugin: Geocoding failed with status: {response.StatusCode}"
                    );
                    _latitude = 1.3521;
                    _longitude = 103.8198;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"Weather Plugin: Error geocoding location '{location}': {ex.Message}"
                );
                _latitude = 1.3521;
                _longitude = 103.8198;
            }
        }

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

                    Console.WriteLine("Weather Plugin: Parsing weather data...");
                    Console.WriteLine($"Weather Plugin: Raw JSON instant details: {JsonSerializer.Serialize(details)}");

                    _name.Value = _location ?? $"Lat:{_latitude.ToString(CultureInfo.InvariantCulture)}, Lon:{_longitude.ToString(CultureInfo.InvariantCulture)}";
                    _weather.Value = next1Hour?.Summary?.SymbolCode?.Split('_')[0] ?? "-";
                    _weatherDesc.Value = next1Hour?.Summary?.SymbolCode?.Replace("_", " ") ?? "-";
                    _weatherIcon.Value = next1Hour?.Summary?.SymbolCode ?? "-";

                    // Use GitHub raw link for weather icon
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

        private async Task<bool> ValidateIconUrl(string url)
        {
            if (url == "-")
                return false;

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

        private double CalculateFeelsLike(double temp, double windSpeed, double humidity)
        {
            if (temp < 10 && windSpeed > 1.33)
            {
                double windKmh = windSpeed * 3.6;
                return 13.12
                    + 0.6215 * temp
                    - 11.37 * Math.Pow(windKmh, 0.16)
                    + 0.3965 * temp * Math.Pow(windKmh, 0.16);
            }
            return temp;
        }

        private class NominatimResult
        {
            [JsonPropertyName("lat")]
            public string Lat { get; set; } = "0";

            [JsonPropertyName("lon")]
            public string Lon { get; set; } = "0";
        }

        private class YrForecast
        {
            public YrProperties? Properties { get; set; }
        }

        private class YrProperties
        {
            public YrTimeseries[]? Timeseries { get; set; }
        }

        private class YrTimeseries
        {
            [JsonPropertyName("time")]
            public string? Time { get; set; }

            public YrData? Data { get; set; }
        }

        private class YrData
        {
            public YrInstant? Instant { get; set; }

            [JsonPropertyName("next_1_hours")]
            public YrNextHour? Next1Hours { get; set; }
        }

        private class YrInstant
        {
            public YrDetails? Details { get; set; }
        }

        private class YrDetails
        {
            [JsonPropertyName("air_temperature")]
            public double AirTemperature { get; set; }

            [JsonPropertyName("air_pressure_at_sea_level")]
            public double AirPressureAtSeaLevel { get; set; }

            [JsonPropertyName("relative_humidity")]
            public double RelativeHumidity { get; set; }

            [JsonPropertyName("wind_speed")]
            public double WindSpeed { get; set; }

            [JsonPropertyName("wind_speed_of_gust")]
            public double? WindSpeedOfGust { get; set; }

            [JsonPropertyName("wind_from_direction")]
            public double WindFromDirection { get; set; }

            [JsonPropertyName("cloud_area_fraction")]
            public double CloudAreaFraction { get; set; }
        }

        private class YrNextHour
        {
            public YrSummary? Summary { get; set; }
            public YrNextHourDetails? Details { get; set; }
        }

        private class YrSummary
        {
            [JsonPropertyName("symbol_code")]
            public string? SymbolCode { get; set; }
        }

        private class YrNextHourDetails
        {
            [JsonPropertyName("precipitation_amount")]
            public double PrecipitationAmount { get; set; }

            [JsonPropertyName("precipitation_category")]
            public string? PrecipitationCategory { get; set; }
        }
    }
}