# YrWeatherPlugin for InfoPanel

## Overview

**YrWeatherPlugin** is a plugin for the InfoPanel app that retrieves weather data from the [MET Norway Weather API](https://api.met.no/). It provides current weather conditions via the `nowcast/2.0/complete` endpoint (falling back to `locationforecast/2.0/complete` outside supported regions) and a configurable multi-day forecast table. Weather icons are sourced from [OpenWeatherMap](https://openweathermap.org/) by default, with support for custom icon sets via a configurable URL.

## Features
- Fetches current weather data using user-defined latitude/longitude or geocoded location (e.g., "Newcastle, New South Wales (Australia)").
- Displays metrics: temperature, feels-like temperature, humidity, pressure, wind speed/direction/gust, cloud cover, rain, and snow.
- Configurable Forecast: Table with daily weather, temperature range (Celsius or Fahrenheit), precipitation, and wind for 1-10 days.
- Temperature Units: Supports Celsius (`C`) or Fahrenheit (`F`) via INI settings, converting MET/Yr’s Celsius data accordingly.
- Geocoding: Converts location names to coordinates using [Nominatim](https://nominatim.openstreetmap.org/) if exact coordinates aren’t provided.
- Customizable refresh interval, date/time format, and UTC offset for local time display.
- Weather Icons: Uses OpenWeatherMap icons (e.g., `https://openweathermap.org/img/wn/01d@4x.png`) or custom PNG/SVG icons from a user-defined URL.
- Seamless integration with InfoPanel’s UI, including sensor values and text fields.

## Forecast Table Data
The plugin provides a configurable forecast table (default 5 days) with:
- **Date**: Day, date, month (e.g., "Tue 08 Apr").
- **Weather**: Dominant condition (e.g., "cloudy", "rain"), from `next_6_hours` `symbol_code`, displayed as an icon.
- **Temp**: Max/min temperature in chosen unit (e.g., "68°F / 64°F" or "20°C / 18°C").
- **Precip**: Total precipitation in mm (e.g., "1.5 mm"), summed from `next_6_hours`.
- **Wind**: Average speed in m/s and direction (e.g., "3.2 m/s SE").

Example output (Fahrenheit):
| Date       | Weather      | Temp        | Precip | Wind     |
|------------|--------------|-------------|--------|----------|
| Tue 08 Apr | partlycloudy | 68°F / 64°F | 0.0 mm | 3.2 m/s SE |
| Wed 09 Apr | rain         | 70°F / 65°F | 2.3 mm | 4.0 m/s S  |
| Thu 10 Apr | clearsky     | 72°F / 66°F | 0.0 mm | 2.8 m/s SW |
| Fri 11 Apr | cloudy       | 71°F / 65°F | 0.5 mm | 3.1 m/s W  |
| Sat 12 Apr | fair         | 73°F / 67°F | 0.0 mm | 2.5 m/s NW |

## Requirements
- InfoPanel app (latest version recommended).

## Requirements to compile
- .NET runtime compatible with InfoPanel.
- Internet connection for MET Norway API, Nominatim geocoding, and icon fetching.

## Installation
1. Download the latest release from GitHub.
2. Import into InfoPanel via the "Import Plugin" feature.
3. Edit `InfoPanel.MetYr.dll.ini` with your desired location and settings.

## Installation from Source
1. Clone or download this repository.
2. Build the project in a .NET environment.
3. Copy compiled files to your InfoPanel plugins directory.

## Usage
- Launch InfoPanel with the plugin loaded.
- The plugin fetches weather data at the configured interval (default 60 minutes).
- View current conditions and forecast in InfoPanel’s UI.
- Weather icons are rendered from URLs (OpenWeatherMap or custom), if supported by InfoPanel.
- Configure custom icons via the `IconUrl` setting in the INI file for alternative icon sets.

## Configuration
On first load the plugin will automatically create default the .ini file. You will need to edit the .ini file with your details!
Edit `InfoPanel.MetYr.dll.ini`:
```ini
[Yr Weather Plugin]
Location = Newcastle, New South Wales (Australia)
Latitude = -32.92953
Longitude = 151.7801
RefreshIntervalMinutes = 60
DateTimeFormat = yyyy-MM-dd HH:mm
UtcOffsetHours = 10
ForecastDays = 7
TemperatureUnit = F
IconUrl = https://raw.githubusercontent.com/Makin-Things/weather-icons/refs/heads/main/static/
```
- **Location**: City/region (used for geocoding if lat/long omitted).
- **Latitude/Longitude**: Optional exact coordinates (overrides geocoding).
- **RefreshIntervalMinutes**: Refresh frequency (e.g., `60`).
- **DateTimeFormat**: C# date format for "Last Refreshed" (e.g., `yyyy-MM-dd HH:mm`).
- **UtcOffsetHours**: Local time offset from UTC (e.g., `10` for AEST, `-4` for EDT).
- **ForecastDays**: Days to forecast (`1-10`, default `5`).
- **TemperatureUnit**: `C` for Celsius, `F` for Fahrenheit.
- **IconUrl**: Optional URL to a directory containing custom PNG or SVG icons (e.g., `https://raw.githubusercontent.com/Makin-Things/weather-icons/refs/heads/main/static/`). If omitted or invalid, falls back to OpenWeatherMap icons.

### Custom Icon Configuration
To use custom icons, set `IconUrl` to a directory containing PNG or SVG files named according to the plugin’s icon mapping. The plugin attempts to load `.svg` first, falling back to `.png` if the SVG is unavailable. Ensure the URL is accessible and points to a directory with the expected icon files.

**Supported Icon Names**:
The plugin maps MET Norway’s `symbol_code` to the following icon names, used as filenames (e.g., `clear-day.svg` or `clear-day.png`):
- `clear-day`, `clear-night`
- `cloudy-1-day`, `cloudy-1-night` (light clouds/fair)
- `cloudy-2-day`, `cloudy-2-night` (partly cloudy)
- `cloudy`
- `fog`
- `rainy-1`, `rainy-1-day`, `rainy-1-night` (light rain, <2.5 mm/h)
- `rainy-2`, `rainy-2-day`, `rainy-2-night` (moderate rain, 2.5–7.5 mm/h)
- `rainy-3`, `rainy-3-day`, `rainy-3-night` (heavy rain, >7.5 mm/h)
- `snowy-1`, `snowy-1-day`, `snowy-1-night` (light snow, <2.5 mm/h)
- `snowy-2`, `snowy-2-day`, `snowy-2-night` (moderate snow, 2.5–7.5 mm/h)
- `snowy-3`, `snowy-3-day`, `snowy-3-night` (heavy snow, >7.5 mm/h)
- `rain-and-sleet-mix` (sleet)
- `snow-and-sleet-mix` (snow with thunder)
- `scattered-thunderstorms`, `thunderstorms`
- `tropical-storm`, `hurricane`
- `wind`

**Example**:
For `IconUrl=https://example.com/icons/`, the plugin will try:
- `https://example.com/icons/clear-day.svg` or `https://example.com/icons/clear-day.png` for `clearsky_day`.
- Ensure all icon names above are available in the specified directory.

**Example Icon Source**:
Use the [weather-icons](https://github.com/Makin-Things/weather-icons) repository:
```ini
IconUrl = https://raw.githubusercontent.com/Makin-Things/weather-icons/refs/heads/main/static/
```
This provides SVG and PNG icons matching the supported names.
