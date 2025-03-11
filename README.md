# YrWeatherPlugin for InfoPanel

## Overview

**YrWeatherPlugin** is a plugin for the InfoPanel framework that retrieves current weather data from the [MET Norway Weather API](https://api.met.no/). It provides real-time weather information such as temperature, pressure, humidity, wind, precipitation, and weather conditions for a specified location, along with corresponding weather icons sourced from MET Norway’s GitHub repository.

### Features
- Fetches current weather data using latitude/longitude derived from a user-defined location (e.g., "Oslo, Norway").
- Displays current weather metrics including temperature, feels-like temperature, humidity, wind speed/direction, cloud cover, and precipitation.
- 5-Day Forecast: Table summarizing daily weather, temperature range, precipitation, and wind.
- Configurable refresh interval via an INI file.
- Geocoding: Automatically converts location names to coordinates using Nominatim.
- Links to weather icons hosted on [MET Norway’s weathericons GitHub repo](https://github.com/metno/weathericons).
- Built for seamless integration with the InfoPanel plugin app.

## Forecast Table Data
The plugin provides a 5-day forecast table with the following columns:
- **Date**: Day of the week, date, and month (e.g., "Mon 10 Mar").
- **Weather**: Most frequent weather condition for the day, derived from `next_6_hours` `symbol_code` (e.g., "cloudy", "clearsky", "rain"). Strips "_day" or "_night" suffixes.
- **Temp**: Minimum and maximum temperature in Celsius (e.g., "0° / 5°"), based on hourly `instant` data.
- **Precip**: Total precipitation in millimeters (e.g., "2.6 mm"), summed from `next_6_hours` forecasts.
- **Wind**: Average wind speed in meters per second and direction (e.g., "3.4 m/s NE"), calculated from hourly `instant` data.

Example output:
| Date       | Weather      | Temp    | Precip | Wind     |
|------------|--------------|---------|--------|----------|
| Mon 10 Mar | cloudy       | 0° / 1° | 0.0 mm | 3.4 m/s NE |
| Tue 11 Mar | snow         | -1° / 1°| 2.6 mm | 3.1 m/s NE |
| Wed 12 Mar | cloudy       | -1° / 1°| 0.0 mm | 3.1 m/s N  |
| Thu 13 Mar | clearsky     | -3° / 1°| 0.0 mm | 2.0 m/s N  |
| Fri 14 Mar | clearsky     | -4° / 4°| 0.0 mm | 1.6 m/s NW |

### Requirements
- InfoPanel framework (latest version recommended for negative number display fix).
- .NET runtime compatible with the InfoPanel framework.
- Internet connection for API calls to MET Norway and OpenStreetMap (for geocoding).

### Installation 
1. Download the latest release.
2. Import download into InfoPanel using the Import Plugin.
3. Edit YrWeatherPlugin.ini to you location.

### Installation from source
1. Clone or download this repository.
2. Build the project in your preferred .NET environment.
3. Place the compiled `YrWeatherPlugin.dll` and its `.ini` file in your InfoPanel plugins directory.

### Usage
- Launch InfoPanel with the plugin loaded.
- The plugin will fetch weather data for the specified location at the set interval.
- View data in InfoPanel’s UI, including numeric sensors (e.g., temperature) and text fields (e.g., weather description).
- Weather icons are provided as URLs, which InfoPanel can render if supported.

### Configuration
- **`YrWeatherPlugin.ini`**:
  ```ini
  [Yr Weather Plugin]
  Location = YOUR LOCATION
  RefreshIntervalMinutes=60 //How often to refresh data
  DateTimeFormat=dd-MMM-yy HH:mm // Date and time format
  UtcOffsetHours=1 // Offset hours (0 = UTC, 1 = CET -5 = EST)
  ForecastDays=5 // Days in forecast

Edit `YrWeatherPlugin.ini` to adjust:
- `Location`: City or region (e.g., "Oslo, Norway").
- `RefreshIntervalMinutes`: Data refresh frequency in minutes (e.g., `1` for testing, `60` for production).

### Credits
- Built using the MET Norway Weather API ([api.met.no](https://api.met.no/)).
- Weather icons from [metno/weathericons](https://github.com/metno/weathericons).
- Geocoding via [OpenStreetMap Nominatim](https://nominatim.openstreetmap.org/).

### License
This project is licensed under the MIT License. See [LICENSE](LICENSE) for details.

---

Feel free to contribute or report issues via GitHub!
