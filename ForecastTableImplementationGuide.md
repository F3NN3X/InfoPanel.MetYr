# InfoPanel Table Implementation Guide

This document provides a comprehensive guide to implementing tables in InfoPanel plugins. Tables allow you to display structured data with multiple columns and rows in a clean, organized format.

## Table of Contents

1. [Overview](#overview)
2. [Core Components](#core-components)
3. [Implementation Steps](#implementation-steps)
4. [Data Processing Pipeline](#data-processing-pipeline)
5. [Column Format String](#column-format-string)
6. [Complete Code Examples](#complete-code-examples)
7. [Best Practices](#best-practices)
8. [Troubleshooting](#troubleshooting)

## Overview

The table feature in InfoPanel allows plugins to display tabular data with multiple columns and rows. This is ideal for displaying structured information like schedules, comparisons, forecasts, logs, or any data that benefits from a grid layout.

### Key Features

- **Dynamic columns**: Define column types (PluginText, PluginSensor, etc.)
- **Flexible formatting**: Column width control via format string
- **Data binding**: Automatic data presentation in InfoPanel UI
- **Type-safe**: Strongly-typed column data using InfoPanel.Plugins types

### Common Use Cases

- **Weather forecasts**: Daily weather data with conditions, temperatures, precipitation
- **Event schedules**: Time, event name, location, status
- **System monitoring**: Service name, status, CPU usage, memory usage
- **Financial data**: Date, stock symbol, price, change percentage
- **Log entries**: Timestamp, level, message, source
- **Task lists**: Priority, task name, assignee, due date

## Core Components

### 1. PluginTable Declaration

```csharp
// Generic table format - adjust column widths based on your data
private static readonly string _tableFormat = "0:150|1:100|2:80|3:60|4:100";
private PluginTable _dataTable;
```

**Breakdown:**

- `_tableFormat`: Column width specification (see [Column Format String](#column-format-string))
- `_dataTable`: The table instance that gets populated and exported to InfoPanel

**Weather Forecast Example:**

```csharp
private static readonly string _forecastTableFormat = "0:150|1:100|2:80|3:60|4:100";
private PluginTable _forecastTable;
```

### 2. Constructor Initialization

```csharp
_dataTable = new PluginTable("Table Name", new DataTable(), _tableFormat);
```

**Parameters:**

- `"Table Name"`: Display name shown in InfoPanel UI
- `new DataTable()`: Initial empty DataTable (populated later)
- `_tableFormat`: Column width configuration

**Weather Forecast Example:**

```csharp
_forecastTable = new PluginTable("Forecast", new DataTable(), _forecastTableFormat);
```

### 3. Registration with Plugin Container

```csharp
public override void Load(List<IPluginContainer> containers)
{
    var container = new PluginContainer("Your Plugin Name");
    // ... other entries ...
    container.Entries.AddRange([
        // ... other plugin items ...
        _dataTable  // Add your table here
    ]);
    containers.Add(container);
}
```

**Weather Forecast Example:**

```csharp
public override void Load(List<IPluginContainer> containers)
{
    var container = new PluginContainer(_location ?? $"Lat:{_latitude}, Lon:{_longitude}");
    // ... other entries ...
    container.Entries.AddRange([
        // ... other plugin items ...
        _forecastTable
    ]);
    containers.Add(container);
}
```

## Implementation Steps

### Step 1: Define Table Structure

```csharp
// 1. Declare table format string (column_index:width_pixels)
private static readonly string _tableFormat = "0:150|1:100|2:80|3:60|4:100";

// 2. Declare the PluginTable field
private PluginTable _dataTable;
```

### Step 2: Initialize in Constructor

```csharp
public YourPlugin() : base("plugin-id", "Plugin Name", "Description")
{
    _dataTable = new PluginTable("Table Display Name", new DataTable(), _tableFormat);
}
```

### Step 3: Define Table Schema

Create a method to set up your DataTable columns:

```csharp
private void InitializeTableColumns(DataTable dataTable)
{
    dataTable.Columns.Add("Column1", typeof(PluginText));
    dataTable.Columns.Add("Column2", typeof(PluginText));  
    dataTable.Columns.Add("Column3", typeof(PluginText));
    dataTable.Columns.Add("Column4", typeof(PluginSensor));
    dataTable.Columns.Add("Column5", typeof(PluginText));
}
```

**Weather Forecast Example:**

```csharp
private void InitializeForecastTableColumns(DataTable dataTable)
{
    dataTable.Columns.Add("Date", typeof(PluginText));
    dataTable.Columns.Add("Weather", typeof(PluginText));  
    dataTable.Columns.Add("Temp", typeof(PluginText));
    dataTable.Columns.Add("Precip", typeof(PluginSensor));
    dataTable.Columns.Add("Wind", typeof(PluginText));
}
```

**Column Types Available:**

- `typeof(PluginText)`: Text display with ID and value
- `typeof(PluginSensor)`: Numeric values with units
- Other InfoPanel.Plugins types as supported

### Step 4: Populate Table Data

```csharp
private DataTable BuildTable(YourDataSource[] data)
{
    var dataTable = new DataTable();
    try
    {
        // Initialize columns
        InitializeTableColumns(dataTable);
        
        // Process your data and add rows
        foreach (var item in data)
        {
            AddRowToTable(dataTable, item);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Plugin: Error building table: {ex.Message}");
    }
    return dataTable;
}
```

**Weather Forecast Example:**

```csharp
private DataTable BuildForecastTable(YrTimeseries[] data)
{
    var dataTable = new DataTable();
    try
    {
        InitializeForecastTableColumns(dataTable);
        var dailyData = GetDailyForecastData(data);
        
        foreach (var dayData in dailyData.OrderBy(d => d.Key))
        {
            AddDayToForecastTable(dataTable, dayData.Key, dayData.Value);
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Weather Plugin: Error building forecast table: {ex.Message}");
    }
    return dataTable;
}
```

### Step 5: Add Rows to Table

```csharp
private void AddRowToTable(DataTable dataTable, YourDataItem item)
{
    var row = dataTable.NewRow();
    
    // Column 1 (PluginText with unique ID)
    row["Column1"] = new PluginText($"col1_{item.Id}", item.DisplayValue);
    
    // Column 2 (PluginText)
    row["Column2"] = new PluginText($"col2_{item.Id}", item.Description);
    
    // Column 3 (PluginText)  
    row["Column3"] = new PluginText($"col3_{item.Id}", item.Status);
    
    // Column 4 (PluginSensor with units)
    row["Column4"] = new PluginSensor($"col4_{item.Id}", "Metric", (float)item.Value, item.Unit);
    
    // Column 5 (PluginText)
    row["Column5"] = new PluginText($"col5_{item.Id}", item.AdditionalInfo);
    
    dataTable.Rows.Add(row);
}
```

**Weather Forecast Example:**

```csharp
private void AddDayToForecastTable(DataTable dataTable, DateTime day, List<YrTimeseries> blockData)
{
    var row = dataTable.NewRow();
    
    // Date column (PluginText with unique ID)
    string dateStr = day.ToString(_forecastDateFormat, CultureInfo.InvariantCulture);
    row["Date"] = new PluginText($"date_{day:yyyyMMdd}", dateStr);
    
    // Weather column (PluginText)
    var weatherData = GetDominantWeatherForDay(blockData);
    string description = MapYrSymbolToDescription(weatherData.SymbolCode, weatherData.MaxPrecipitation);
    row["Weather"] = new PluginText($"weather_{day:yyyyMMdd}", description ?? "-");
    
    // Temperature column (PluginText)  
    var tempsC = blockData.Select(t => t?.data?.instant?.details?.airTemperature ?? 0).ToList();
    string tempStr = FormatTemperatureRange(tempsC.Max(), tempsC.Min());
    row["Temp"] = new PluginText($"temp_{day:yyyyMMdd}", tempStr);
    
    // Precipitation column (PluginSensor with units)
    float precip = (float)blockData.Sum(t => t?.data?.next6Hours?.details?.precipitationAmount ?? 0);
    row["Precip"] = new PluginSensor($"precip_{day:yyyyMMdd}", "Precip", precip, "mm");
    
    // Wind column (PluginText)
    string windStr = GetAverageWindString(blockData);
    row["Wind"] = new PluginText($"wind_{day:yyyyMMdd}", windStr);
    
    dataTable.Rows.Add(row);
}
```

### Step 6: Update Table in Plugin Lifecycle

```csharp
public override async Task UpdateAsync(CancellationToken cancellationToken)
{
    try 
    {
        // Fetch your data
        var data = await GetData(cancellationToken);
        
        // Build and assign new table
        _dataTable.Value = BuildTable(data);
        
        Console.WriteLine($"Plugin: Updated table with {data.Length} rows");
    }
    catch (Exception ex) 
    {
        Console.WriteLine($"Plugin: Error updating table: {ex.Message}");
    }
}
```

**Weather Forecast Example:**

```csharp
public override async Task UpdateAsync(CancellationToken cancellationToken)
{
    try 
    {
        // Fetch forecast data
        var forecastData = await GetForecastData(cancellationToken);
        
        // Build and assign new forecast table
        _forecastTable.Value = BuildForecastTable(forecastData.Properties.Timeseries);
        
        Console.WriteLine($"Weather Plugin: Updated forecast table");
    }
    catch (Exception ex) 
    {
        Console.WriteLine($"Weather Plugin: Error updating forecast: {ex.Message}");
    }
}
```

## Data Processing Pipeline

Tables often require processing raw data into a structured format. Here are common patterns:

### 1. Raw Data Grouping

Group your raw data by a key (e.g., date, category, status):

```csharp
private Dictionary<string, List<YourDataType>> GroupData(YourDataType[] rawData)
{
    var grouped = new Dictionary<string, List<YourDataType>>();

    foreach (var item in rawData)
    {
        string groupKey = DetermineGroupKey(item); // e.g., item.Date.ToString("yyyy-MM-dd")
        
        if (!grouped.ContainsKey(groupKey))
            grouped[groupKey] = new List<YourDataType>();
        
        grouped[groupKey].Add(item);
    }
    
    return grouped;
}
```

**Weather Forecast Example (YrWeatherPlugin):**

```csharp
private Dictionary<DateTime, List<YrTimeseries>> GetDailyForecastData(YrTimeseries[] timeseries)
{
    var now = DateTime.UtcNow;
    var startTime = now.AddDays(1).Date; // Start from tomorrow
    var endTime = startTime.AddDays(_forecastDays);
    var dailyBlocks = new Dictionary<DateTime, List<YrTimeseries>>();

    foreach (var ts in timeseries)
    {
        if (ts?.time == null || !DateTime.TryParse(ts.time, out var tsTime))
            continue;
            
        var tsDate = tsTime.Date;
        if (tsDate >= startTime && tsDate < endTime)
        {
            if (!dailyBlocks.ContainsKey(tsDate))
                dailyBlocks[tsDate] = new List<YrTimeseries>();
            
            dailyBlocks[tsDate].Add(ts);
        }
    }
    
    return dailyBlocks;
}
```

### 2. Data Aggregation

Once grouped, aggregate the data for each group:

```csharp
private AggregatedData ProcessGroup(List<YourDataType> groupData)
{
    return new AggregatedData
    {
        Average = groupData.Average(x => x.Value),
        Maximum = groupData.Max(x => x.Value),
        Count = groupData.Count,
        MostCommon = groupData.GroupBy(x => x.Category)
                            .OrderByDescending(g => g.Count())
                            .First().Key
    };
}
```

**Weather Forecast Example (Finding Dominant Weather):**

```csharp
private (string? SymbolCode, double MaxPrecipitation) GetDominantWeatherForDay(List<YrTimeseries> blockData)
{
    var validSymbolCodes = blockData
        .Where(t => t?.data?.next6Hours?.summary?.symbolCode != null)
        .Select(t => new { 
            SymbolCode = t!.data!.next6Hours!.summary!.symbolCode!, 
            Precip = t!.data!.next6Hours!.details?.precipitationAmount ?? 0 
        })
        .ToList();
        
    if (!validSymbolCodes.Any())
        return (null, 0);
        
    var dominantWeather = validSymbolCodes
        .GroupBy(x => x.SymbolCode)
        .OrderByDescending(g => g.Count())            // Most frequent
        .ThenByDescending(g => g.Sum(x => x.Precip))  // Highest precipitation
        .First();
        
    return (dominantWeather.Key, validSymbolCodes.Max(x => x.Precip));
}
```

### 3. Other Common Aggregation Patterns

**System Monitoring Example:**

```csharp
private ServiceSummary AggregateServiceData(List<ServiceMetric> metrics)
{
    return new ServiceSummary
    {
        Status = metrics.Any(m => m.Status == "Error") ? "Error" : "OK",
        AvgCpuUsage = metrics.Average(m => m.CpuUsage),
        MaxMemoryUsage = metrics.Max(m => m.MemoryUsage),
        UptimePercentage = CalculateUptime(metrics)
    };
}
```

**Financial Data Example:**

```csharp
private DayTradingSummary AggregateTradingData(List<Trade> trades)
{
    return new DayTradingSummary
    {
        OpenPrice = trades.First().Price,
        ClosePrice = trades.Last().Price,
        HighPrice = trades.Max(t => t.Price),
        LowPrice = trades.Min(t => t.Price),
        Volume = trades.Sum(t => t.Volume),
        PercentChange = CalculatePercentChange(trades.First().Price, trades.Last().Price)
    };
}
```

## Column Format String

The format string controls column widths in the InfoPanel UI:

```csharp
private static readonly string _tableFormat = "0:150|1:100|2:80|3:60|4:100";
```

**Format Breakdown:**

- `0:150` = Column 0, 150 pixels wide
- `1:100` = Column 1, 100 pixels wide  
- `2:80` = Column 2, 80 pixels wide
- `3:60` = Column 3, 60 pixels wide
- `4:100` = Column 4, 100 pixels wide
- `|` = Column separator

**Design Guidelines:**

- **Date columns**: 100-150px (depends on date format length)
- **Short text**: 80-100px (weather icons, directions)
- **Numeric values**: 60-80px (temperatures, precipitation)
- **Long text**: 120-150px (descriptions, ranges)

**Examples:**

```csharp
// Simple 3-column table
"0:120|1:80|2:100"

// Wide description column
"0:100|1:200|2:80|3:90"

// Compact mobile-friendly
"0:80|1:60|2:60|3:70"
```

## Complete Code Examples

### Example 1: Simple System Status Table

Here's a basic system monitoring table:

```csharp
public class SystemMonitorPlugin : BasePlugin
{
    private static readonly string _tableFormat = "0:120|1:80|2:100|3:80";
    private PluginTable _statusTable;
    
    public SystemMonitorPlugin() : base("system-monitor", "System Status", "Shows system service status")
    {
        _statusTable = new PluginTable("System Status", new DataTable(), _tableFormat);
    }
    
    public override async Task UpdateAsync(CancellationToken cancellationToken)
    {
        try
        {
            var services = await GetSystemServices();
            _statusTable.Value = BuildStatusTable(services);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"System Monitor: Error updating table: {ex.Message}");
        }
    }
    
    private DataTable BuildStatusTable(SystemService[] services)
    {
        var table = new DataTable();
        
        // Define columns
        table.Columns.Add("Service", typeof(PluginText));
        table.Columns.Add("Status", typeof(PluginText));  
        table.Columns.Add("CPU", typeof(PluginSensor));
        table.Columns.Add("Memory", typeof(PluginSensor));
        
        // Add rows
        for (int i = 0; i < services.Length; i++)
        {
            var service = services[i];
            var row = table.NewRow();
            
            row["Service"] = new PluginText($"service_{i}", service.Name);
            row["Status"] = new PluginText($"status_{i}", service.Status);
            row["CPU"] = new PluginSensor($"cpu_{i}", "CPU", (float)service.CpuUsage, "%");
            row["Memory"] = new PluginSensor($"memory_{i}", "Memory", (float)service.MemoryMB, "MB");
            
            table.Rows.Add(row);
        }
        
        return table;
    }
    
    public override void Load(List<IPluginContainer> containers)
    {
        var container = new PluginContainer("System Monitor");
        container.Entries.Add(_statusTable);
        containers.Add(container);
    }
}
```

### Example 2: Weather Forecast Table (Real Implementation)

Here's the actual weather forecast implementation from YrWeatherPlugin:

```csharp
public class YrWeatherPlugin : BasePlugin
{
    private static readonly string _forecastTableFormat = "0:150|1:100|2:80|3:60|4:100";
    private PluginTable _forecastTable;
    private int _forecastDays = 5;
    private string _forecastDateFormat = "dddd dd MMM";
    
    public YrWeatherPlugin() : base("yr-weather-plugin", "Weather Info - MET/Yr", "Description")
    {
        _forecastTable = new PluginTable("Forecast", new DataTable(), _forecastTableFormat);
    }
    
    private DataTable BuildForecastTable(YrTimeseries[] timeseries)
    {
        var dataTable = new DataTable();
        try
        {
            InitializeForecastTableColumns(dataTable);
            var dailyData = GetDailyForecastData(timeseries);
            
            foreach (var dayData in dailyData.OrderBy(d => d.Key))
            {
                AddDayToForecastTable(dataTable, dayData.Key, dayData.Value);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Weather Plugin: Error building forecast table: {ex.Message}");
        }
        return dataTable;
    }
    
    private void InitializeForecastTableColumns(DataTable dataTable)
    {
        dataTable.Columns.Add("Date", typeof(PluginText));
        dataTable.Columns.Add("Weather", typeof(PluginText));
        dataTable.Columns.Add("Temp", typeof(PluginText));
        dataTable.Columns.Add("Precip", typeof(PluginSensor));
        dataTable.Columns.Add("Wind", typeof(PluginText));
    }
    
    private void AddDayToForecastTable(DataTable dataTable, DateTime day, List<YrTimeseries> blockData)
    {
        var row = dataTable.NewRow();
        
        // Date column
        string dateStr = day.ToString(_forecastDateFormat, CultureInfo.InvariantCulture);
        row["Date"] = new PluginText($"date_{day:yyyyMMdd}", dateStr);
        
        // Weather column with description
        var weatherData = GetDominantWeatherForDay(blockData);
        string description = MapYrSymbolToDescription(weatherData.SymbolCode, weatherData.MaxPrecipitation);
        row["Weather"] = new PluginText($"weather_{day:yyyyMMdd}", description ?? "-");
        
        // Temperature column
        var tempsC = blockData.Select(t => t?.data?.instant?.details?.airTemperature ?? 0).ToList();
        string tempStr = FormatTemperatureRange(tempsC.Max(), tempsC.Min());
        row["Temp"] = new PluginText($"temp_{day:yyyyMMdd}", tempStr);
        
        // Precipitation column
        float precip = (float)blockData.Sum(t => t?.data?.next6Hours?.details?.precipitationAmount ?? 0);
        row["Precip"] = new PluginSensor($"precip_{day:yyyyMMdd}", "Precip", precip, "mm");
        
        // Wind column
        string windStr = GetAverageWindString(blockData);
        row["Wind"] = new PluginText($"wind_{day:yyyyMMdd}", windStr);
        
        dataTable.Rows.Add(row);
    }
    
    public override void Load(List<IPluginContainer> containers)
    {
        var container = new PluginContainer(_location ?? $"Lat:{_latitude}, Lon:{_longitude}");
        container.Entries.AddRange([
            _name, _weather, _weatherDesc, _weatherIcon, _weatherIconUrl, _lastRefreshed,
            _temp, _pressure, _seaLevel, _feelsLike, _humidity,
            _windSpeed, _windDeg, _windGust, _clouds, _rain, _snow,
            _forecastTable
        ]);
        containers.Add(container);
    }
}
```

### Example 3: Event Schedule Table

A simple event schedule implementation:

### Example 3: Event Schedule Table

A simple event schedule implementation:

```csharp
public class EventSchedulePlugin : BasePlugin
{
    private static readonly string _tableFormat = "0:100|1:200|2:120|3:80";
    private PluginTable _scheduleTable;
    
    public EventSchedulePlugin() : base("event-schedule", "Event Schedule", "Shows upcoming events")
    {
        _scheduleTable = new PluginTable("Upcoming Events", new DataTable(), _tableFormat);
    }
    
    private DataTable BuildScheduleTable(Event[] events)
    {
        var table = new DataTable();
        
        // Define columns
        table.Columns.Add("Time", typeof(PluginText));
        table.Columns.Add("Event", typeof(PluginText));  
        table.Columns.Add("Location", typeof(PluginText));
        table.Columns.Add("Status", typeof(PluginText));
        
        // Add rows
        for (int i = 0; i < Math.Min(events.Length, 10); i++)
        {
            var evt = events[i];
            var row = table.NewRow();
            
            row["Time"] = new PluginText($"time_{i}", evt.StartTime.ToString("HH:mm"));
            row["Event"] = new PluginText($"event_{i}", evt.Title);
            row["Location"] = new PluginText($"location_{i}", evt.Location);
            row["Status"] = new PluginText($"status_{i}", evt.Status);
            
            table.Rows.Add(row);
        }
        
        return table;
    }
    
    public class Event
    {
        public DateTime StartTime { get; set; }
        public string Title { get; set; } = "";
        public string Location { get; set; } = "";
        public string Status { get; set; } = "";
    }
}
```

## Best Practices

### 1. Unique IDs for Table Items

Always use unique IDs for PluginText and PluginSensor objects:

```csharp
// Good: Include row identifier
new PluginText($"weather_{day:yyyyMMdd}", description)

// Bad: Same ID for multiple rows  
new PluginText("weather", description)
```

### 2. Error Handling

Wrap table building in try-catch blocks:

```csharp
private DataTable BuildTable(YourData[] data)
{
    var table = new DataTable();
    try
    {
        InitializeColumns(table);
        foreach (var item in data)
        {
            try 
            {
                AddRowToTable(table, item);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Plugin: Error adding row: {ex.Message}");
                // Continue with other rows
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Plugin: Error building table: {ex.Message}");
    }
    return table;
}
```

### 3. Data Validation

Validate data before adding to table:

```csharp
private void AddRowToTable(DataTable table, DataItem item)
{
    if (item?.Date == null)
    {
        Console.WriteLine("Plugin: Skipping invalid data item");
        return;
    }
    
    var row = table.NewRow();
    // ... populate row ...
    table.Rows.Add(row);
}
```

### 4. Configurable Formatting

Make formats configurable via INI or constants:

```csharp
private const string DEFAULT_DATE_FORMAT = "dddd dd MMM";
private string _forecastDateFormat = DEFAULT_DATE_FORMAT;

// In configuration loading:
_forecastDateFormat = config["DateFormat"] ?? DEFAULT_DATE_FORMAT;

// In table building:
string dateStr = day.ToString(_forecastDateFormat, CultureInfo.InvariantCulture);
```

### 5. Performance Considerations

- Limit the number of rows (5-10 for most tables, 20+ only if necessary)
- Use StringBuilder for complex string formatting
- Cache processed data when possible
- Avoid unnecessary DataTable recreations

```csharp
private DataTable BuildTable(YourData[] data)
{
    var table = new DataTable();
    InitializeColumns(table);
    
    // Limit rows for performance and UI readability
    int maxRows = Math.Min(data.Length, _maxTableRows);
    
    for (int i = 0; i < maxRows; i++)
    {
        AddRowToTable(table, data[i], i);
    }
    
    return table;
}
```

## Troubleshooting

### Common Issues

1. **Table not appearing in InfoPanel**
   - Check that `_dataTable` is added to container in `Load()` method
   - Verify the table has columns and rows
   - Ensure plugin is loading without exceptions

2. **Columns too narrow/wide**
   - Adjust the format string: `"0:150|1:100|2:80"`
   - Test different screen resolutions
   - Consider content length when setting widths

3. **Data not updating**
   - Ensure `_dataTable.Value = newDataTable` is called
   - Check that `UpdateAsync` is completing without errors
   - Verify data source is providing new data

4. **Exception on table creation**
   - Check column types match InfoPanel.Plugins types
   - Ensure all PluginText/PluginSensor objects have unique IDs
   - Validate data before creating table items

### Debug Logging

Add comprehensive logging to diagnose issues:

```csharp
private DataTable BuildTable(YourData[] data)
{
    Console.WriteLine($"Plugin: Building table with {data.Length} data items");
    
    var table = new DataTable();
    InitializeColumns(table);
    
    foreach (var item in data)
    {
        AddRowToTable(table, item);
        Console.WriteLine($"Plugin: Added row for {item.Id}");
    }
    
    Console.WriteLine($"Plugin: Table built with {table.Rows.Count} rows, {table.Columns.Count} columns");
    return table;
}
```

This guide provides the complete foundation for implementing tables in InfoPanel plugins. The pattern can be adapted for any tabular data display needs - from simple status displays to complex data dashboards.
