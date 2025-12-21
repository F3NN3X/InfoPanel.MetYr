namespace InfoPanel.MetYr.Models;

public record WeatherPluginConfiguration(
    string Location,
    string Latitude,
    string Longitude,
    int? Altitude,
    int RefreshIntervalMinutes,
    string DateTimeFormat,
    string ForecastDateFormat,
    double UtcOffsetHours,
    int ForecastDays,
    string TemperatureUnit,
    string? IconUrl
);
