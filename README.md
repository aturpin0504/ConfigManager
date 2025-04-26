# ConfigManager

ConfigManager is a .NET library that provides a convenient way to manage application configuration settings in your .NET Framework applications. It offers a simplified interface for reading and writing configuration values, managing directory entries, and handling drive mappings through your application's config file.

## Features

- **Type-safe configuration access**: Retrieve strongly-typed configuration values
- **Default value support**: Specify fallback values for missing configuration entries
- **Directory management**: Add, retrieve, and remove directory entries with optional exclusion patterns
- **Drive mapping support**: Define and manage network drive mappings
- **Automatic configuration validation**: Ensures required configuration sections exist
- **Comprehensive logging**: Integrated logging for configuration operations

## Requirements

- .NET Framework 4.7.2 or higher
- System.Configuration namespace

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

// Set a configuration value
ConfigHelper.SetValue("MaxConnections", 100);
```

### Working with Directory Entries

```csharp
// Get all directory entries from config
List<DirectoryEntry> directories = ConfigHelper.GetDirectories();

// Display directory entries
foreach (var dir in directories)
{
    Console.WriteLine($"Directory: {dir.Path}");
    
    if (dir.ExclusionsArray.Length > 0)
    {
        Console.WriteLine("Excluded subdirectories:");
        foreach (var exclusion in dir.ExclusionsArray)
        {
            Console.WriteLine($"  - {exclusion}");
        }
    }
}

// Add a new directory entry with relative path exclusions using Windows path separators
var newDir = new DirectoryEntry
{
    Path = @"C:\MyApplication\Data",
    Exclusions = @"temp,logs\archive,reports\old"
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

// Remove a drive mapping
ConfigHelper.RemoveDriveMapping("V:");
```

### Logging with RunLog

ConfigManager uses the RunLog library for logging operations. RunLog is already integrated with ConfigHelper, and logging is configured automatically when the application starts.

```csharp
// ConfigHelper uses Log.Logger by default, but you can customize it

// Create a custom logger configuration
var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("logs/config.log", rollingInterval: RollingInterval.Day);

// Set the logger instance
Log.Logger = loggerConfig.CreateLogger();

// Optionally set the logger for ConfigHelper specifically
ConfigHelper.SetLogger(Log.Logger);
```

Example logging output:

```
[2025-04-26 10:15:23] [Information] Loaded 5 directories from config (skipped 0 invalid entries)
[2025-04-26 10:15:24] [Debug] Added new directory entry: C:\MyApplication\Data
[2025-04-26 10:15:25] [Warning] Directory entry not found: C:\MyApplication\OldData
```

RunLog provides various log levels (Verbose, Debug, Information, Warning, Error, Fatal) and multiple output destinations including console and file with rolling options.

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
  </appSettings>
  
  <directories>
    <directory path="C:\MyApp\Data" exclusions="temp,logs\archive,old_files" />
    <directory path="C:\MyApp\Logs" />
  </directories>
  
  <driveMappings>
    <mapping driveLetter="V:" uncPath="\\server\share" />
    <mapping driveLetter="X:" uncPath="\\server2\archive" />
  </driveMappings>
</configuration>
```

## Configuration Validation

ConfigManager automatically validates and creates required configuration sections when initialized. The `EnsureConfigSectionsExist()` method is called during static initialization to make sure all necessary sections exist in your config file.

## Error Handling

The library includes comprehensive error handling with detailed logging. Operations that fail will return appropriate values (false for boolean operations, default values for getters) and log the error.

## Best Practices

1. Always specify default values when using `GetValue<T>()` to handle missing configuration entries gracefully
2. Use the strongly-typed methods rather than accessing the configuration values directly
3. Validate drive letter and UNC path formats before adding drive mappings
4. When defining directory exclusions, use relative paths (e.g., "logs\archive", "temp") that are relative to the main directory path
   - Both Windows backslash (`\`) and forward slash (`/`) path separators are supported
5. Separate multiple exclusion paths with commas

## License

MIT