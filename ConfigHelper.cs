using RunLog;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace ConfigManager
{
    /// <summary>
    /// Static helper class to assist with getting and setting values in app.config
    /// </summary>
    public static class ConfigHelper
    {
        private static Logger _logger = Log.Logger;
        private static bool _configSectionsValidated = false;
        private static readonly object _validationLock = new object();

        /// <summary>
        /// Static constructor that initializes the logger with the default Log.Logger
        /// and ensures config sections exist
        /// </summary>
        static ConfigHelper()
        {
            // Ensure config sections exist during static initialization
            EnsureConfigSectionsExist();
        }

        /// <summary>
        /// Sets the logger instance to be used by ConfigHelper
        /// </summary>
        /// <param name="logger">An ILogger implementation</param>
        public static void SetLogger(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.Information("ConfigHelper logger has been configured.");
        }

        /// <summary>
        /// Gets a configuration value from app.config with the specified key and converts it to the requested type
        /// </summary>
        /// <typeparam name="T">The type to convert the configuration value to</typeparam>
        /// <param name="key">The configuration key</param>
        /// <param name="defaultValue">Default value to return if the key is not found</param>
        /// <returns>The configuration value converted to type T, or the defaultValue if not found</returns>
        public static T GetValue<T>(string key, T defaultValue = default)
        {
            try
            {
                string value = ConfigurationManager.AppSettings[key];

                if (string.IsNullOrEmpty(value))
                {
                    _logger.Debug($"Configuration key '{key}' not found. Using default value.");
                    return defaultValue;
                }

                try
                {
                    // Handle collection types
                    if (typeof(T) == typeof(List<string>) || typeof(T) == typeof(IList<string>) || typeof(T) == typeof(IEnumerable<string>))
                    {
                        var items = value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => s.Trim())
                            .ToList();
                        return (T)(object)items;
                    }

                    // Use TypeConverter for more robust conversion
                    var converter = System.ComponentModel.TypeDescriptor.GetConverter(typeof(T));
                    if (converter != null && converter.CanConvertFrom(typeof(string)))
                    {
                        return (T)converter.ConvertFromString(value);
                    }

                    // Original type conversions as fallback
                    if (typeof(T) == typeof(string))
                    {
                        return (T)(object)value;
                    }
                    else if (typeof(T) == typeof(int))
                    {
                        return (T)(object)int.Parse(value);
                    }
                    else if (typeof(T) == typeof(bool))
                    {
                        return (T)(object)bool.Parse(value);
                    }
                    else if (typeof(T) == typeof(double))
                    {
                        return (T)(object)double.Parse(value);
                    }
                    else if (typeof(T) == typeof(DateTime))
                    {
                        return (T)(object)DateTime.Parse(value);
                    }
                    else if (typeof(T) == typeof(Guid))
                    {
                        return (T)(object)Guid.Parse(value);
                    }
                    else if (typeof(T).IsEnum)
                    {
                        return (T)Enum.Parse(typeof(T), value);
                    }
                    else
                    {
                        // For other types, try to use Convert class
                        return (T)Convert.ChangeType(value, typeof(T));
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, $"Error converting configuration value for key '{key}' to type {typeof(T).Name}. Using default value.");
                    return defaultValue;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error retrieving configuration value for key '{key}'. Using default value.");
                return defaultValue;
            }
        }

        /// <summary>
        /// Sets a configuration value in the app.config
        /// </summary>
        /// <typeparam name="T">The type of the value to set</typeparam>
        /// <param name="key">The configuration key</param>
        /// <param name="value">The value to set</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool SetValue<T>(string key, T value)
        {
            try
            {
                // Convert value to string based on type
                string stringValue;

                // Handle collection types
                if (value is IEnumerable<string> stringCollection)
                {
                    stringValue = string.Join(",", stringCollection);
                }
                // Handle custom formatting for certain types
                else if (value is DateTime dateValue)
                {
                    stringValue = dateValue.ToString("o"); // ISO 8601 format
                }
                else if (value is double || value is float || value is decimal)
                {
                    // Use invariant culture for numeric values
                    stringValue = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                }
                else
                {
                    // Default toString for other types
                    stringValue = value?.ToString() ?? string.Empty;
                }

                // Validation before saving (optional)
                if (string.IsNullOrEmpty(key))
                {
                    _logger.Error("Cannot set configuration with empty key");
                    return false;
                }

                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

                if (config.AppSettings.Settings[key] != null)
                {
                    config.AppSettings.Settings[key].Value = stringValue;
                    _logger.Debug($"Updated configuration key '{key}' to value '{stringValue}'");
                }
                else
                {
                    config.AppSettings.Settings.Add(key, stringValue);
                    _logger.Debug($"Added new configuration key '{key}' with value '{stringValue}'");
                }

                config.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error setting configuration value for key '{key}'");
                return false;
            }
        }

        /// <summary>
        /// Gets a list of directory configurations from the 'directories' section in app.config
        /// </summary>
        /// <returns>A list of DirectoryConfig objects</returns>
        public static List<DirectoryConfig> GetDirectories()
        {
            var result = new List<DirectoryConfig>();

            try
            {
                var configFilePath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
                XDocument doc = XDocument.Load(configFilePath);

                _logger.Debug($"Loading directories from config file: {configFilePath}");

                var directoriesElement = doc.Root?.Element("directories");
                if (directoriesElement == null)
                {
                    _logger.Warning("'directories' section not found in config file");
                    return result;
                }

                // Get all directory elements
                var directoryElements = directoriesElement.Elements("directory");
                foreach (var dirElement in directoryElements)
                {
                    var pathAttr = dirElement.Attribute("path");
                    if (pathAttr == null)
                    {
                        _logger.Warning("Found directory element without path attribute");
                        continue;
                    }

                    var dirConfig = new DirectoryConfig
                    {
                        Path = pathAttr.Value
                    };

                    // Get optional exclude elements (using the element format rather than attribute)
                    var excludeElements = dirElement.Elements("exclude");
                    foreach (var excludeElement in excludeElements)
                    {
                        if (!string.IsNullOrWhiteSpace(excludeElement.Value))
                        {
                            dirConfig.Exclusions.Add(excludeElement.Value);
                        }
                    }

                    result.Add(dirConfig);
                }

                _logger.Information($"Loaded {result.Count} directories from config");
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error retrieving directories from configuration");
                return new List<DirectoryConfig>();
            }
        }

        /// <summary>
        /// Gets a list of drive mappings from the 'driveMappings' section in app.config
        /// </summary>
        /// <returns>A list of DriveMapping objects</returns>
        public static List<DriveMapping> GetDriveMappings()
        {
            var result = new List<DriveMapping>();

            try
            {
                var configFilePath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
                XDocument doc = XDocument.Load(configFilePath);

                _logger.Debug($"Loading drive mappings from config file: {configFilePath}");

                var mappingsElement = doc.Root?.Element("driveMappings");
                if (mappingsElement == null)
                {
                    _logger.Warning("'driveMappings' section not found in config file");
                    return result;
                }

                // Get all mapping elements
                var mappingElements = mappingsElement.Elements("mapping");
                foreach (var mappingElement in mappingElements)
                {
                    var driveLetterAttr = mappingElement.Attribute("driveLetter");
                    var uncPathAttr = mappingElement.Attribute("uncPath");

                    if (driveLetterAttr == null || uncPathAttr == null)
                    {
                        _logger.Warning("Found mapping element with missing attributes");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(driveLetterAttr.Value) || string.IsNullOrWhiteSpace(uncPathAttr.Value))
                    {
                        _logger.Warning("Drive mapping found with empty attribute values");
                        continue;
                    }

                    var driveMapping = new DriveMapping
                    {
                        DriveLetter = driveLetterAttr.Value,
                        UncPath = uncPathAttr.Value
                    };

                    result.Add(driveMapping);
                }

                _logger.Information($"Loaded {result.Count} drive mappings from config");
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error retrieving drive mappings from configuration");
                return new List<DriveMapping>();
            }
        }

        /// <summary>
        /// Adds a new directory entry to the 'directories' section in app.config
        /// </summary>
        /// <param name="directoryConfig">The directory config to add</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool AddDirectory(DirectoryConfig directoryConfig)
        {
            if (directoryConfig == null || string.IsNullOrWhiteSpace(directoryConfig.Path))
            {
                _logger.Error("Cannot add directory with null or empty path");
                return false;
            }

            try
            {
                var configFilePath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
                XDocument doc = XDocument.Load(configFilePath);

                var directoriesElement = doc.Root.Element("directories");
                if (directoriesElement == null)
                {
                    directoriesElement = new XElement("directories");
                    doc.Root.Add(directoriesElement);
                }

                // Check if the directory entry already exists
                var existingEntry = directoriesElement.Elements("directory")
                    .FirstOrDefault(e => e.Attribute("path")?.Value == directoryConfig.Path);

                if (existingEntry != null)
                {
                    // Update existing entry - remove all existing exclude elements
                    existingEntry.Elements("exclude").Remove();

                    // Add new exclude elements
                    foreach (var exclusion in directoryConfig.Exclusions)
                    {
                        if (!string.IsNullOrWhiteSpace(exclusion))
                        {
                            existingEntry.Add(new XElement("exclude", exclusion));
                        }
                    }

                    _logger.Debug($"Updated directory entry: {directoryConfig.Path}");
                }
                else
                {
                    // Create new entry
                    var newElement = new XElement("directory",
                        new XAttribute("path", directoryConfig.Path));

                    // Add exclusions as child elements
                    foreach (var exclusion in directoryConfig.Exclusions)
                    {
                        if (!string.IsNullOrWhiteSpace(exclusion))
                        {
                            newElement.Add(new XElement("exclude", exclusion));
                        }
                    }

                    directoriesElement.Add(newElement);
                    _logger.Debug($"Added new directory entry: {directoryConfig.Path}");
                }

                doc.Save(configFilePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error adding directory entry: {directoryConfig.Path}");
                return false;
            }
        }

        /// <summary>
        /// Adds a new drive mapping to the 'driveMappings' section in app.config
        /// </summary>
        /// <param name="driveMapping">The drive mapping to add</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool AddDriveMapping(DriveMapping driveMapping)
        {
            if (driveMapping == null || string.IsNullOrWhiteSpace(driveMapping.DriveLetter) ||
                string.IsNullOrWhiteSpace(driveMapping.UncPath))
            {
                _logger.Error("Cannot add drive mapping with null or empty values");
                return false;
            }

            try
            {
                var configFilePath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
                XDocument doc = XDocument.Load(configFilePath);

                var mappingsElement = doc.Root.Element("driveMappings");
                if (mappingsElement == null)
                {
                    mappingsElement = new XElement("driveMappings");
                    doc.Root.Add(mappingsElement);
                }

                // Check if the mapping already exists
                var existingMapping = mappingsElement.Elements("mapping")
                    .FirstOrDefault(m => m.Attribute("driveLetter")?.Value == driveMapping.DriveLetter);

                if (existingMapping != null)
                {
                    // Update existing mapping
                    var uncPathAttr = existingMapping.Attribute("uncPath");
                    if (uncPathAttr != null)
                        uncPathAttr.Value = driveMapping.UncPath;

                    _logger.Debug($"Updated drive mapping: {driveMapping.DriveLetter} -> {driveMapping.UncPath}");
                }
                else
                {
                    // Create new mapping
                    mappingsElement.Add(new XElement("mapping",
                        new XAttribute("driveLetter", driveMapping.DriveLetter),
                        new XAttribute("uncPath", driveMapping.UncPath)));

                    _logger.Debug($"Added new drive mapping: {driveMapping.DriveLetter} -> {driveMapping.UncPath}");
                }

                doc.Save(configFilePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error adding drive mapping: {driveMapping.DriveLetter} -> {driveMapping.UncPath}");
                return false;
            }
        }

        /// <summary>
        /// Removes a directory entry from the 'directories' section in app.config
        /// </summary>
        /// <param name="path">The path of the directory entry to remove</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool RemoveDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _logger.Error("Cannot remove directory with null or empty path");
                return false;
            }

            try
            {
                var configFilePath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
                XDocument doc = XDocument.Load(configFilePath);

                var directoriesElement = doc.Root.Element("directories");
                if (directoriesElement == null)
                {
                    _logger.Warning("'directories' section not found in config file");
                    return false;
                }

                var directoryElement = directoriesElement.Elements("directory")
                    .FirstOrDefault(e => e.Attribute("path")?.Value == path);

                if (directoryElement != null)
                {
                    directoryElement.Remove();
                    doc.Save(configFilePath);
                    _logger.Debug($"Removed directory entry: {path}");
                    return true;
                }
                else
                {
                    _logger.Warning($"Directory entry not found: {path}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error removing directory entry: {path}");
                return false;
            }
        }

        /// <summary>
        /// Removes a drive mapping from the 'driveMappings' section in app.config
        /// </summary>
        /// <param name="driveLetter">The drive letter of the mapping to remove (with or without colon)</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool RemoveDriveMapping(string driveLetter)
        {
            if (string.IsNullOrWhiteSpace(driveLetter))
            {
                _logger.Error("Cannot remove drive mapping with null or empty drive letter");
                return false;
            }

            // Ensure the drive letter has a colon for proper matching
            string normalizedDriveLetter = EnsureDriveLetterHasColon(driveLetter);

            try
            {
                var configFilePath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
                XDocument doc = XDocument.Load(configFilePath);

                var mappingsElement = doc.Root.Element("driveMappings");
                if (mappingsElement == null)
                {
                    _logger.Warning("'driveMappings' section not found in config file");
                    return false;
                }

                // First try exact match
                var mappingElement = mappingsElement.Elements("mapping")
                    .FirstOrDefault(m => m.Attribute("driveLetter")?.Value == normalizedDriveLetter);

                // If not found, try case-insensitive match
                if (mappingElement == null)
                {
                    mappingElement = mappingsElement.Elements("mapping")
                        .FirstOrDefault(m => string.Equals(m.Attribute("driveLetter")?.Value, normalizedDriveLetter, StringComparison.OrdinalIgnoreCase));
                }

                // If still not found, try matching without colon if user provided with colon
                if (mappingElement == null && normalizedDriveLetter.EndsWith(":"))
                {
                    string driveLetterWithoutColon = normalizedDriveLetter.TrimEnd(':');
                    mappingElement = mappingsElement.Elements("mapping")
                        .FirstOrDefault(m => m.Attribute("driveLetter")?.Value == driveLetterWithoutColon ||
                                           m.Attribute("driveLetter")?.Value == driveLetterWithoutColon + ":");
                }

                if (mappingElement != null)
                {
                    string originalDriveLetter = mappingElement.Attribute("driveLetter")?.Value;
                    mappingElement.Remove();
                    doc.Save(configFilePath);
                    _logger.Debug($"Removed drive mapping: {originalDriveLetter}");
                    return true;
                }
                else
                {
                    _logger.Warning($"Drive mapping not found: {normalizedDriveLetter}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error removing drive mapping: {normalizedDriveLetter}");
                return false;
            }
        }

        /// <summary>
        /// Public method to ensure that the required config sections exist and are properly configured
        /// </summary>
        /// <returns>True if configuration was modified, false otherwise</returns>
        public static bool EnsureConfigSectionsExist()
        {
            // Only validate once
            if (_configSectionsValidated)
                return false;

            lock (_validationLock)
            {
                // Double-check after acquiring the lock
                if (_configSectionsValidated)
                    return false;

                try
                {
                    var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                    bool configChanged = false;

                    // Check if configSections element exists
                    var configFilePath = config.FilePath;
                    XDocument doc = XDocument.Load(configFilePath);

                    var configSectionsElement = doc.Root.Element("configSections");
                    if (configSectionsElement == null)
                    {
                        // Create configSections element if it doesn't exist
                        configSectionsElement = new XElement("configSections");
                        doc.Root.AddFirst(configSectionsElement);
                        configChanged = true;
                        _logger.Debug("Created missing configSections element");
                    }

                    // Check for the directories section
                    var directoriesSection = configSectionsElement.Elements("section")
                        .FirstOrDefault(s => s.Attribute("name")?.Value == "directories");

                    if (directoriesSection == null)
                    {
                        // Add directories section if it's missing
                        configSectionsElement.Add(new XElement("section",
                            new XAttribute("name", "directories"),
                            new XAttribute("type", "DummyType, DummyAssembly")));
                        configChanged = true;
                        _logger.Debug("Added missing 'directories' section declaration");
                    }
                    else
                    {
                        // Check if the type attribute is correct
                        var typeAttr = directoriesSection.Attribute("type");
                        if (typeAttr == null || typeAttr.Value != "DummyType, DummyAssembly")
                        {
                            if (typeAttr == null)
                                directoriesSection.Add(new XAttribute("type", "DummyType, DummyAssembly"));
                            else
                                typeAttr.Value = "DummyType, DummyAssembly";

                            configChanged = true;
                            _logger.Debug("Fixed 'directories' section type attribute");
                        }
                    }

                    // Check for the driveMappings section
                    var driveMappingsSection = configSectionsElement.Elements("section")
                        .FirstOrDefault(s => s.Attribute("name")?.Value == "driveMappings");

                    if (driveMappingsSection == null)
                    {
                        // Add driveMappings section if it's missing
                        configSectionsElement.Add(new XElement("section",
                            new XAttribute("name", "driveMappings"),
                            new XAttribute("type", "DummyType, DummyAssembly")));
                        configChanged = true;
                        _logger.Debug("Added missing 'driveMappings' section declaration");
                    }
                    else
                    {
                        // Check if the type attribute is correct
                        var typeAttr = driveMappingsSection.Attribute("type");
                        if (typeAttr == null || typeAttr.Value != "DummyType, DummyAssembly")
                        {
                            if (typeAttr == null)
                                driveMappingsSection.Add(new XAttribute("type", "DummyType, DummyAssembly"));
                            else
                                typeAttr.Value = "DummyType, DummyAssembly";

                            configChanged = true;
                            _logger.Debug("Fixed 'driveMappings' section type attribute");
                        }
                    }

                    // Check that the actual directories section exists in the config
                    var directoriesElement = doc.Root.Element("directories");
                    if (directoriesElement == null)
                    {
                        doc.Root.Add(new XElement("directories"));
                        configChanged = true;
                        _logger.Debug("Added missing 'directories' element");
                    }

                    // Check that the actual driveMappings section exists in the config
                    var driveMappingsElement = doc.Root.Element("driveMappings");
                    if (driveMappingsElement == null)
                    {
                        // Create an empty driveMappings element without any sample mappings
                        var newDriveMappingsElement = new XElement("driveMappings");
                        doc.Root.Add(newDriveMappingsElement);
                        configChanged = true;
                        _logger.Debug("Added missing 'driveMappings' element");
                    }

                    // Save the changes if needed
                    if (configChanged)
                    {
                        doc.Save(configFilePath);
                        _logger.Information("Configuration file updated with required sections");

                        // Force configuration system to reload
                        try
                        {
                            System.Reflection.FieldInfo initState = typeof(ConfigurationManager)
                                .GetField("s_initState", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

                            if (initState != null)
                            {
                                initState.SetValue(null, 0);
                                _logger.Debug("Configuration system reset successful");
                            }
                            else
                            {
                                _logger.Warning("Could not reset configuration system - field not found");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.Warning($"Error resetting configuration system: {ex.Message}");
                        }
                    }

                    _configSectionsValidated = true;
                    return configChanged;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error ensuring config sections exist");
                    // Important: We don't set _configSectionsValidated to true here
                    // so it will try again on the next access
                    return false;
                }
            }
        }

        /// <summary>
        /// Ensures that a drive letter ends with a colon
        /// </summary>
        /// <param name="driveLetter">The drive letter to normalize</param>
        /// <returns>The drive letter with a colon</returns>
        private static string EnsureDriveLetterHasColon(string driveLetter)
        {
            if (string.IsNullOrEmpty(driveLetter))
                return driveLetter;

            // Remove any trailing non-letter characters
            string cleanedDriveLetter = driveLetter.Trim();

            // If it's already properly formatted with a colon, return as is
            if (Regex.IsMatch(cleanedDriveLetter, @"^[A-Za-z]:$"))
                return cleanedDriveLetter;

            // If it's just a letter without a colon, add the colon
            if (Regex.IsMatch(cleanedDriveLetter, @"^[A-Za-z]$"))
                return cleanedDriveLetter + ":";

            // If it ends with a colon but has other characters, extract just the letter and colon
            Match match = Regex.Match(cleanedDriveLetter, @"([A-Za-z]):?");
            if (match.Success)
                return match.Groups[1].Value + ":";

            // If we can't extract a valid drive letter, return the original
            return driveLetter;
        }
    }

    /// <summary>
    /// Represents a directory entry with its path and exclusions
    /// </summary>
    public class DirectoryConfig
    {
        public string Path { get; set; }
        public List<string> Exclusions { get; set; } = new List<string>();
    }

    /// <summary>
    /// Represents a drive mapping entry with drive letter and UNC path
    /// </summary>
    public class DriveMapping
    {
        public string DriveLetter { get; set; }
        public string UncPath { get; set; }

        public override string ToString()
        {
            return $"{DriveLetter} -> {UncPath}";
        }
    }
}