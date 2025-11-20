using System.Text.Json.Serialization;

namespace InfoPanel.MetYr.Models;

public record YrNowcast(
    YrWeatherProperties? Properties
);

public record YrForecast(
    YrWeatherProperties? Properties
);

public record YrWeatherProperties(
    YrTimeseries[]? Timeseries
);

public record YrTimeseries(
    [property: JsonPropertyName("time")] string? Time,
    YrWeatherData? Data
);

public record YrWeatherData(
    YrWeatherInstantDetails? Instant,
    [property: JsonPropertyName("next_1_hours")] YrWeatherNext1Hours? Next1Hours,
    [property: JsonPropertyName("next_6_hours")] YrWeatherNext6Hours? Next6Hours
);

public record YrWeatherInstantDetails(
    YrWeatherDetails? Details
);

public record YrWeatherDetails(
    [property: JsonPropertyName("air_temperature")] double AirTemperature,
    [property: JsonPropertyName("air_pressure_at_sea_level")] double AirPressureAtSeaLevel,
    [property: JsonPropertyName("relative_humidity")] double RelativeHumidity,
    [property: JsonPropertyName("wind_speed")] double WindSpeed,
    [property: JsonPropertyName("wind_from_direction")] double WindFromDirection,
    [property: JsonPropertyName("wind_speed_of_gust")] double? WindSpeedOfGust,
    [property: JsonPropertyName("cloud_area_fraction")] float CloudAreaFraction
);

public record YrWeatherNext1Hours(
    YrWeatherSummary? Summary,
    YrWeatherNextDetails? Details
);

public record YrWeatherNext6Hours(
    YrWeatherSummary? Summary,
    YrWeatherNextDetails? Details
);

public record YrWeatherSummary(
    [property: JsonPropertyName("symbol_code")] string? SymbolCode
);

public record YrWeatherNextDetails(
    [property: JsonPropertyName("precipitation_amount")] double? PrecipitationAmount,
    [property: JsonPropertyName("precipitation_category")] string? PrecipitationCategory
);
