# Changelog

All notable changes to the YrWeatherPlugin will be documented in this file, following the [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) format.

## [1.0.0] - 2025-03-10

### Added
- Initial plugin creation for InfoPanel, fetching current weather data from MET Norway’s API (`https://api.met.no/weatherapi/locationforecast/2.0/complete`).
- Location-based geocoding using OpenStreetMap Nominatim API to convert user-defined locations (e.g., "Porsgrunn, Norway") to latitude/longitude.
- Configurable INI file (`YrWeatherPlugin.ini`) for setting location and refresh interval.
- Weather metrics:
  - Temperature (`temp`), Feels Like (`feels_like`), Humidity (`humidity`), Pressure (`pressure`, `sea_level`).
  - Wind Speed (`wind_speed`), Wind Direction (`wind_deg`), Wind Gust (`wind_gust`).
  - Cloud Cover (`clouds`), Rain (`rain`), Snow (`snow`).
- Text fields for weather condition (`weather`), description (`weather_desc`), and icon code (`weather_icon`).
- Weather icon URL (`weather_icon_url`) initially set via MET’s weathericon API (later adjusted).
- `CalculateFeelsLike` method for wind chill calculation when temperature < 10°C and wind speed > 1.33 m/s.
- Logging for debugging API calls, data parsing, and refresh timing.

### Changed
- **Decimal Formatting**: Fixed comma (`,`) vs. dot (`.`) issue by enforcing `CultureInfo.InvariantCulture` for all numeric conversions.
- **Data Scope**: Removed forecast-dependent sensors (`maxTemp`, `minTemp`, `groundLevel`) to focus on current data only (`Timeseries[0]`).
- **Negative Numbers**: Collaborated with InfoPanel developer to fix downstream rendering of negative values (e.g., `-2.0742567` displaying as `2.0742567`); temporary `feels_like_text` workaround added and later removed.
- **Refresh Rate**: Added `_lastUpdateTime` tracking and logging to verify `UpdateInterval` adherence (e.g., `Time since last update: 1.02 minutes`).
- **Weather Icons**: Switched from MET’s weathericon API (which returned 404s) to direct GitHub raw links (`https://raw.githubusercontent.com/metno/weathericons/main/weather/png/{symbol_code}.png`).

### Fixed
- Geocoding fallback to default coordinates (Singapore: 1.3521, 103.8198) if location lookup fails.
- Handling of null or missing API data (e.g., `next_1_hours`, `precipitation_category`) with defaults like `0` or `-`.
- Icon URL validation to ensure only reachable GitHub links are set, falling back to `-` on failure.

### Notes
- Version 1.0.0 marks a stable release with current weather data, refresh functionality, and icon support fully operational.
- Future updates will include forecast data integration with InfoPanel’s new table view feature.

---

This is the initial release of YrWeatherPlugin, built from scratch to version 1.0.0 through iterative collaboration and refinement.