# YrWeatherPlugin for InfoPanel

## Overview

**YrWeatherPlugin** is a plugin for the InfoPanel framework that retrieves current weather data from the [MET Norway Weather API](https://api.met.no/). It provides real-time weather information such as temperature, pressure, humidity, wind, precipitation, and weather conditions for a specified location, along with corresponding weather icons sourced from MET Norway’s GitHub repository.

### Features
- Fetches current weather data using latitude/longitude derived from a user-defined location (e.g., "Porsgrunn, Norway").
- Displays weather metrics including temperature, feels-like temperature, humidity, wind speed/direction, cloud cover, and precipitation.
- Configurable refresh interval via an INI file.
- Links to weather icons hosted on [MET Norway’s weathericons GitHub repo](https://github.com/metno/weathericons).
- Built for seamless integration with the InfoPanel plugin app.

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
    Location=YOURLOCATION, COUTNRYIFNEEDED
    RefreshIntervalMinutes=60

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