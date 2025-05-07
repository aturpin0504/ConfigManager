# ConfigManager

ConfigManager is a .NET library that provides a convenient way to manage application configuration settings in your .NET Framework applications. It offers a simplified interface for reading and writing configuration values, managing directory entries, and handling drive mappings through your application's config file.

## Features

- **Type-safe configuration access**: Retrieve strongly-typed configuration values with improved type conversion
- **Collection type support**: Handle List<string>, IEnumerable<string> and other collection types
- **Default value support**: Specify fallback values for missing configuration entries
- **Directory management**: Add, retrieve, and remove directory entries with optional exclusion patterns
- **Drive mapping support**: Define and manage network drive mappings with enhanced validation
- **Automatic configuration validation**: Ensures required configuration sections exist with thread-safe validation
- **Improved XML handling**: Better XML parsing with XDocument instead of direct XML manipulation
- **Comprehensive logging**: Integrated logging for configuration operations
- **Thread-safe operation**: Double-check locking pattern for config section validation
- **Culture-invariant formatting**: Numeric values use invariant culture to avoid regional issues
- **Robust error handling**: Detailed error messages and validation for configuration entries

## Requirements

- .NET Framework 4.7.2 or higher
- System.Configuration namespace
- RunLog 3.3.0 or higher

## Installation

Install the AaTurpin.ConfigManager package from NuGet:

```bash
Install-Package AaTurpin.ConfigManager
# or
dotnet add package AaTurpin.ConfigManager
```

Package URL: [NuGet Gallery](https://www.nuget.org/packages/AaTurpin.ConfigManager)

## Usage

### Basic Configuration Access

```csharp
// Get a string value
string serverName = ConfigHelper.GetValue<string>("ServerName", "localhost");

// Get an integer value with default
int port = ConfigHelper.GetValue<int>("Port", 8080);

// Get a boolean value
bool enableLogging = ConfigHelper.GetValue<bool>("EnableLogging", false);

// Get a collection value (comma or semicolon separated values)
List<string> allowedIPs = ConfigHelper.GetValue<List<string>>("AllowedIPs", new List<string>());

// Set a configuration value
ConfigHelper.SetValue("MaxConnections", 100);
```

### Working with Directory Entries

```csharp
// Get all directory entries from config
List<DirectoryConfig> directories = ConfigHelper.GetDirectories();

// Display directory entries
foreach (var dir in directories)
{
    Console.WriteLine($"Directory: {dir.Path}");
    
    if (dir.Exclusions.Count > 0)
    {
        Console.WriteLine("Excluded subdirectories:");
        foreach (var exclusion in dir.Exclusions)
        {
            Console.WriteLine($"  - {exclusion}");
        }
    }
}

// Add a new directory entry with exclusions
var newDir = new DirectoryConfig
{
    Path = @"C:\MyApplication\Data",
    Exclusions = new List<string> { "temp", @"logs\archive", @"reports\old" }
};
ConfigHelper.AddDirectory(newDir);

// Remove a directory entry
ConfigHelper.RemoveDirectory(@"C:\MyApplication\OldData");
```

### Working with Drive Mappings

```csharp
// Get all drive mappings from config
List<DriveMapping> mappings = ConfigHelper.GetDriveMappings();

// Display drive mappings
foreach (var mapping in mappings)
{
    Console.WriteLine($"Drive mapping: {mapping.DriveLetter} -> {mapping.UncPath}");
}

// Add a new drive mapping
var newMapping = new DriveMapping
{
    DriveLetter = "V:",
    UncPath = @"\\server\share"
};
ConfigHelper.AddDriveMapping(newMapping);

// Remove a drive mapping (with or without colon)
ConfigHelper.RemoveDriveMapping("V:");
// or
ConfigHelper.RemoveDriveMapping("V");
```

### Improved Error Handling and Validation

The library includes enhanced error handling with more detailed logging and validation:

```csharp
// Drive letter normalization handles various formats
ConfigHelper.AddDriveMapping(new DriveMapping
{
    DriveLetter = "V",  // Will be normalized to "V:" automatically
    UncPath = @"\\server\share"
});

// Case-insensitive comparison for drive mappings
ConfigHelper.RemoveDriveMapping("v"); // Will match "V:" in config

// Error handling for invalid entries
try {
    ConfigHelper.AddDriveMapping(null); // Will log error and return false
} catch (ArgumentNullException ex) {
    // Exception handling
}
```

### Logging with RunLog 3.3.0

ConfigManager uses RunLog 3.3.0 for enhanced logging operations:

```csharp
// ConfigHelper uses Log.Logger by default, but you can customize it

// Create a custom logger configuration
var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console(LogLevel.Information)
    .WriteTo.File("logs/config.log", 
                  rollingInterval: RollingInterval.Day, 
                  enableBuffering: true,  // New in RunLog 3.3.0
                  bufferSize: 100);       // New in RunLog 3.3.0

// Set the logger instance
Log.Logger = loggerConfig.CreateLogger();

// Optionally set the logger for ConfigHelper specifically
ConfigHelper.SetLogger(Log.Logger);
```

Example logging output with improved detail:

```
[2025-04-26 10:15:23] [Information] Loaded 5 directories from config
[2025-04-26 10:15:23] [Debug] Directory at path 'C:\MyApplication\Data' loaded with 3 exclusions
[2025-04-26 10:15:24] [Debug] Updated directory entry: C:\MyApplication\Data
[2025-04-26 10:15:25] [Warning] Directory entry not found: C:\MyApplication\OldData
```

RunLog 3.3.0 provides various log levels (Verbose, Debug, Information, Warning, Error, Fatal) and multiple output destinations with improved performance through optional buffering and background processing.

## Configuration File Structure

The configuration file should have the following structure:

```xml
<?xml version="1.0" encoding="utf-8" ?>
<configuration>
  <configSections>
    <section name="directories" type="DummyType, DummyAssembly" />
    <section name="driveMappings" type="DummyType, DummyAssembly" />
  </configSections>
  
  <appSettings>
    <add key="ServerName" value="myserver" />
    <add key="Port" value="8080" />
    <add key="EnableLogging" value="true" />
    <add key="AllowedIPs" value="192.168.1.1,192.168.1.2,10.0.0.1" />
  </appSettings>
  
  <directories>
    <directory path="C:\MyApp\Data">
        <exclude>temp</exclude>
        <exclude>logs\archive</exclude>
        <exclude>old_files</exclude>
    </directory>
    <directory path="C:\MyApp\Logs" />
  </directories>
  
  <driveMappings>
    <mapping driveLetter="V:" uncPath="\\server\share" />
    <mapping driveLetter="X:" uncPath="\\server2\archive" />
  </driveMappings>
</configuration>
```

## Configuration Validation

ConfigManager automatically validates and creates required configuration sections when initialized. The `EnsureConfigSectionsExist()` method is called during static initialization to make sure all necessary sections exist in your config file, and now uses thread-safe double-check locking for better performance.

## Thread Safety and Improved XML Handling

The library now uses:
- Double-check locking pattern for thread-safe config validation
- XDocument for more robust XML processing
- Better exception handling throughout
- Improved attribute validation for XML elements
- Configuration system reset after modifying the config file

## Best Practices

1. Always specify default values when using `GetValue<T>()` to handle missing configuration entries gracefully
2. Use the strongly-typed methods rather than accessing the configuration values directly
3. Leverage collection type support for comma/semicolon-separated values
4. Use the case-insensitive matching for more flexible configuration handling
5. Take advantage of the improved drive letter normalization (with or without colon)
6. When defining directory exclusions:
   - Use separate `<exclude>` elements for each path to exclude
   - Use relative paths (e.g., "logs\\archive", "temp") that are relative to the main directory path
   - Both Windows backslash (`\`) and forward slash (`/`) path separators are supported
7. Check log output for validation warnings and errors after configuration changes
8. Benefit from the ISO 8601 date handling for DateTime values

## License

MIT