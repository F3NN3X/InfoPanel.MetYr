# Changelog

All notable changes to the YrWeatherPlugin will be documented in this file, following the [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) format.

## [1.3.0] - 2025-03-11

### Added
- **UTC Offset Adjustment**: Added `UtcOffsetHours` INI setting to adjust the "Last Refreshed" time to local time (e.g., `+1` for CET, `-5` for EST). Replaces the earlier `ShowUtcOffset` boolean.

### Changed
- **Time Display Logic**: Improved the `UpdateAsync` method with simplified time adjustment and added debug logging to track UTC time, adjusted time, and final formatted output.
- **Forecast Temperature Order**: Swapped the Min/Max temperature display in the forecast table to show Max first, then Min (e.g., "15° / 5°" instead of "5° / 15°").

### Fixed
- Ensured robust parsing of `UtcOffsetHours` with fallback to 0 if invalid, with appropriate logging.

## [1.2.0] - 2025-03-11

### Added
- **Custom Date Formatting**: Implemented configurable C# custom date formatting for "Last Refreshed" with validation via the `DateTimeFormat` INI setting.

### Changed
- **Forecast Field Rename**: Renamed "5-Day Forecast" to "Forecast" for flexibility, reflecting the configurable `ForecastDays` setting.

## [1.1.0] - 2025-03-10
- **Enhanced Forecast Reliability**: Switched 5-day forecast weather to use `next_6_hours` data for consistent symbol codes, fixing blank "Weather" entries.
- **Null Safety**: Added null checks and `DateTime.TryParse` in `BuildForecastTable` to resolve CS8604/CS8602 warnings.
- **Documentation**: Added detailed code comments and updated README with table data explanation.

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