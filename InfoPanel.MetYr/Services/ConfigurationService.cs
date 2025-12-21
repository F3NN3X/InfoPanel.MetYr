using System.Globalization;
using System.Reflection;
using InfoPanel.MetYr.Models;
using IniParser;
using IniParser.Model;

namespace InfoPanel.MetYr.Services;

public class ConfigurationService
{
    private const string DEFAULT_LOCATION = "Oslo, Norway";
    private const string DEFAULT_DATE_FORMAT = "yyyy-MM-dd HH:mm";
    private const string DEFAULT_FORECAST_DATE_FORMAT = "dddd dd MMM";
    private const int DEFAULT_REFRESH_INTERVAL = 60;
    private const int DEFAULT_FORECAST_DAYS = 5;
    private const string DEFAULT_TEMP_UNIT = "C";

    private readonly string _configFilePath;
    private readonly FileIniDataParser _parser;

    public ConfigurationService()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        _configFilePath = $"{assembly.ManifestModule.FullyQualifiedName}.ini";
        _parser = new FileIniDataParser();
    }

    public string ConfigFilePath => _configFilePath;

    public WeatherPluginConfiguration LoadConfiguration()
    {
        try
        {
            if (!File.Exists(_configFilePath))
            {
                Console.WriteLine($"Weather Plugin: INI file not found at {_configFilePath}, creating default.");
                CreateDefaultIniFile();
                return GetDefaultConfiguration();
            }
            else
            {
                Console.WriteLine($"Weather Plugin: Reading INI file from {_configFilePath}");
                var config = _parser.ReadFile(_configFilePath);
                string iniContent = File.ReadAllText(_configFilePath);
                Console.WriteLine($"Weather Plugin: INI file content:\n{iniContent}");

                return ParseConfiguration(config);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Weather Plugin: Error reading INI file '{_configFilePath}': {ex.Message}");
            return GetDefaultConfiguration();
        }
    }

    private void CreateDefaultIniFile()
    {
        var config = new IniData();
        config["Yr Weather Plugin"]["Location"] = DEFAULT_LOCATION;
        config["Yr Weather Plugin"]["Latitude"] = "";
        config["Yr Weather Plugin"]["Longitude"] = "";
        config["Yr Weather Plugin"]["Altitude"] = "";
        config["Yr Weather Plugin"]["RefreshIntervalMinutes"] = DEFAULT_REFRESH_INTERVAL.ToString();
        config["Yr Weather Plugin"]["DateTimeFormat"] = DEFAULT_DATE_FORMAT;
        config["Yr Weather Plugin"]["ForecastDateFormat"] = DEFAULT_FORECAST_DATE_FORMAT;
        config["Yr Weather Plugin"]["UtcOffsetHours"] = "0";
        config["Yr Weather Plugin"]["ForecastDays"] = DEFAULT_FORECAST_DAYS.ToString();
        config["Yr Weather Plugin"]["TemperatureUnit"] = DEFAULT_TEMP_UNIT;
        config["Yr Weather Plugin"]["IconUrl"] = "";
        _parser.WriteFile(_configFilePath, config);
    }

    private WeatherPluginConfiguration GetDefaultConfiguration()
    {
        return new WeatherPluginConfiguration(
            DEFAULT_LOCATION,
            "",
            "",
            null,
            DEFAULT_REFRESH_INTERVAL,
            DEFAULT_DATE_FORMAT,
            DEFAULT_FORECAST_DATE_FORMAT,
            0,
            DEFAULT_FORECAST_DAYS,
            DEFAULT_TEMP_UNIT,
            null
        );
    }

    private WeatherPluginConfiguration ParseConfiguration(IniData config)
    {
        string location = config["Yr Weather Plugin"]["Location"] ?? DEFAULT_LOCATION;
        string? iconUrl = config["Yr Weather Plugin"]["IconUrl"]?.Trim();
        string latStr = config["Yr Weather Plugin"]["Latitude"]?.Trim() ?? "";
        string lonStr = config["Yr Weather Plugin"]["Longitude"]?.Trim() ?? "";

        // Parse altitude (in meters above sea level) - optional but recommended for accurate temperatures
        int? altitude = null;
        string altStr = config["Yr Weather Plugin"]["Altitude"]?.Trim() ?? "";
        if (!string.IsNullOrEmpty(altStr) && int.TryParse(altStr, out int altValue))
        {
            altitude = altValue;
            Console.WriteLine($"Weather Plugin: Altitude set to {altitude}m");
        }

        if (!int.TryParse(config["Yr Weather Plugin"]["RefreshIntervalMinutes"], out int refreshInterval) || refreshInterval <= 0)
            refreshInterval = DEFAULT_REFRESH_INTERVAL;

        string formatFromIni = config["Yr Weather Plugin"]["DateTimeFormat"] ?? DEFAULT_DATE_FORMAT;
        string dateTimeFormat = ValidateDateTimeFormat(formatFromIni) ? formatFromIni : DEFAULT_DATE_FORMAT;

        string forecastFormatFromIni = config["Yr Weather Plugin"]["ForecastDateFormat"] ?? DEFAULT_FORECAST_DATE_FORMAT;
        string forecastDateFormat = ValidateDateTimeFormat(forecastFormatFromIni) ? forecastFormatFromIni : DEFAULT_FORECAST_DATE_FORMAT;

        if (!double.TryParse(config["Yr Weather Plugin"]["UtcOffsetHours"], NumberStyles.Any, CultureInfo.InvariantCulture, out double utcOffset))
        {
            Console.WriteLine($"Weather Plugin: Invalid UtcOffsetHours '{config["Yr Weather Plugin"]["UtcOffsetHours"]}', defaulting to 0");
            utcOffset = 0;
        }

        if (!int.TryParse(config["Yr Weather Plugin"]["ForecastDays"], out int forecastDays) || forecastDays < 1 || forecastDays > 10)
            forecastDays = DEFAULT_FORECAST_DAYS;

        string tempUnit = config["Yr Weather Plugin"]["TemperatureUnit"]?.ToUpper() == "F" ? "F" : "C";

        return new WeatherPluginConfiguration(
            location,
            latStr,
            lonStr,
            altitude,
            refreshInterval,
            dateTimeFormat,
            forecastDateFormat,
            utcOffset,
            forecastDays,
            tempUnit,
            iconUrl
        );
    }

    public bool ValidateDateTimeFormat(string format)
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
}
