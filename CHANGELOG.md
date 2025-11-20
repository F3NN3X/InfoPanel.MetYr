# Changelog

All notable changes to the YrWeatherPlugin will be documented in this file, following the [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) format.

## [3.0.0] - 2025-06-03

### Refactor

- **Service-Oriented Architecture**: Complete architectural overhaul moving logic from the main plugin class into dedicated services (`WeatherService`, `GeocodingService`, `IconService`, `ConfigurationService`).
- **Strongly-Typed Models**: Replaced dynamic JSON parsing with strongly-typed records (`WeatherResponse`, `GeocodingResponse`) using `System.Text.Json` for better type safety and performance.
- **Dependency Injection**: Implemented a cleaner dependency flow, initializing services in the plugin startup.

### Optimization

- **Memory Management**: Implemented `static readonly JsonSerializerOptions` in services to reduce object allocations during frequent API calls.
- **Network Efficiency**: Added smart caching for icon file extensions in `IconService` to prevent redundant HTTP checks for `.svg` vs `.png` availability.

### Changed

- **Project Structure**: Reorganized codebase into `Services` and `Models` directories for better maintainability and separation of concerns.

## [2.5.1] - 2025-06-03

### Added

- **Custom Forecast Date Format**: Added `ForecastDateFormat` INI setting to allow customization of how dates appear in the forecast table. Users can now specify date formats like "ddd d MMM" (Wed 8 Jun), "dddd" (Wednesday), "dd/MM" (08/06), or any valid C# date format string.

## [2.5.0] - 2025-06-03

### Added

- **Constant Values**: Added named constants for configuration defaults, API endpoints, weather thresholds, etc. for better maintainability and readability.
- **Method Breakdown**: Split large methods into smaller focused ones (e.g., separated `GetWeather` into `FetchCurrentWeatherData` and `UpdateForecastTable`).
- **Enhanced Null Safety**: Added comprehensive null checks and proper handling of nullable values throughout the code.

### Changed

- **Improved Icon Mapping**: Enhanced weather icon mapping with boolean flags for precipitation intensity and better pattern matching.
- **Asynchronous Operations**: Made icon URL validation fully asynchronous and improved async flow control.
- **Error Handling**: Added more detailed error messages and robust exception handling with appropriate fallbacks.
- **Wind Direction Calculation**: Improved the wind direction algorithm with proper normalization and clearer comments.

### Fixed

- **Null Reference Exceptions**: Fixed potential null reference exceptions when handling API responses.
- **Precipitation Handling**: Enhanced precipitation amount handling with proper nullable value checks.
- **Compiler Warnings**: Resolved all compiler warnings related to nullable reference types.
- **Type Safety**: Improved type safety throughout the codebase with proper null-conditional operators.

## [2.0.2] - 2025-06-02

### Changed

- **Forecast Table Weather Column**: Updated the "Weather" column in the forecast table to display human-readable descriptions (e.g., "Moderate Rain" instead of "rainy-2") using `MapYrSymbolToDescription`, ensuring consistency with `_weatherDesc` formatting.

## [2.0.1] - 2025-06-02

### Added

- **Readable Weather Descriptions**: Implemented `MapYrSymbolToDescription` to convert MET/Yr `symbol_code` values into human-readable weather descriptions (e.g., "lightrain" to "Light Rain") for `_weatherDesc`, with title case formatting and removal of `day`/`night` suffixes.
- **Precipitation Intensity in Descriptions**: Incorporated precipitation amount in `MapYrSymbolToDescription` to differentiate light, moderate, and heavy conditions (e.g., `<2.5 mm` for "Light Rain", `≥7.5 mm` for "Heavy Rain").

## [2.0.0] - 2025-06-02

### Added

- **Custom Icon Support**: Added `IconUrl` INI setting to fetch custom PNG/SVG icons from a user-defined URL (e.g., `https://raw.githubusercontent.com/Makin-Things/weather-icons/refs/heads/main/static/`), with fallback to OpenWeatherMap icons if unavailable.
- **Icon Mapping**: Implemented mapping of MET/Yr `symbol_code` to hyphenated icon names (e.g., `clear-day`, `rainy-1-day`) with precipitation-based intensity logic (e.g., `<2.5 mm/h` for `rainy-1`).
- **Wind Direction Abbreviations**: Changed wind direction labels to abbreviated forms (`N`, `NE`, `E`, `SE`, `S`, `SW`, `W`, `NW`) in forecast table output.

### Changed

- **Plugin Info Placement**: Moved plugin information comment block below `using` statements in `YrWeatherPlugin.cs` for better code organization.
- **Versioning**: Consolidated all recent changes under version 2.0.0 for a unified release.

### Fixed

- **Compilation Errors**: Resolved multiple syntax issues, including string interpolation in `SetCoordinatesFromLocation`, nested class accessibility, `JsonPropertyName` attribute syntax, and `CloudAreaFraction` type declaration.
- **Null Safety**: Added null checks in geocoding logic to prevent `NullReferenceException` when parsing Nominatim results.
- **Unreferenced Labels**: Removed stray labels causing parsing errors.
- **Duplicate Class Definitions**: Corrected redundant class definitions and ensured consistent property types.

## [1.5.1] - 2025-04-08

### Added

- Global testing readiness: Verified plugin functionality with diverse locations (e.g., Reykjavik, Tokyo, Miami) to ensure robust coordinate handling and forecast accuracy worldwide.

### Fixed

- Ensured INI `Latitude` and `Longitude` override geocoding when provided, fixing prior Nominatim override bug.
- Added fallback to `locationforecast/2.0/complete` for current weather if `nowcast/2.0/complete` fails (e.g., outside Norway), ensuring data availability globally.
- Fixed temperature unit display in forecast table to consistently show `°F` or `°C` based on `TemperatureUnit`, with proper Celsius-to-Fahrenheit conversion.

## [1.5.0] - 2025-04-08

### Added

- Temperature unit support: `TemperatureUnit` in INI (`C` for Celsius, `F` for Fahrenheit), converting MET/Yr’s Celsius data accordingly.
- Optional `Latitude` and `Longitude` in INI to specify exact coordinates, overriding geocoding from `Location` if provided.

## [1.5.0] - 2025-04-08

### Added

- Support for temperature units (`C` for Celsius, `F` for Fahrenheit) via `TemperatureUnit` in INI file. Converts MET/Yr’s Celsius data to Fahrenheit when `F` is set.
- Optional `Latitude` and `Longitude` fields in INI file to specify exact coordinates, overriding geocoding from `Location` if provided.

## [1.4.2] - 2025-03-13

### Changed

- Adjusted OpenWeatherMap icon mapping: `fair_day`/`fair_night` now map to `01d`/`01n` (clear sky) instead of `02d`/`02n` (few clouds) to better match user expectations for clear weather conditions.

## [1.4.1] - 2025-03-13

### Changed

- Reverted weather icons from MET/Yr PNGs to OpenWeatherMap icons (`@4x`, 400x400px) to resolve rendering cutoff issues in InfoPanel’s UI.

## [1.4.0] - 2025-03-13

### Added

- Split weather data sources for improved accuracy:
  - Current weather now fetched from `nowcast/2.0/complete` API endpoint.
  - Forecast data fetched from `locationforecast/2.0/complete` API endpoint.
- Attempted use of MET/Yr weather icons from GitHub raw URLs (`https://raw.githubusercontent.com/metno/weathericons/main/weather/png/`).

### Removed

- Retired older unified endpoint usage (`locationforecast/1.9`) in favor of split nowcast and forecast endpoints.

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
