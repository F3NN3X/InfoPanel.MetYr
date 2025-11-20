using System.Text.Json.Serialization;

namespace InfoPanel.MetYr.Models;

public record NominatimResult(
    [property: JsonPropertyName("lat")] string? Lat,
    [property: JsonPropertyName("lon")] string? Lon
);
