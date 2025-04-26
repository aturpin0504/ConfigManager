using RunLog;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace ConfigManager
{
    /// <summary>
    /// Represents a directory entry in the configuration
    /// </summary>
    public class DirectoryEntry
    {
        /// <summary>
        /// Path of the directory
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Optional comma-separated list of exclusions
        /// </summary>
        public string Exclusions { get; set; }

        /// <summary>
        /// Gets the exclusions as an array of strings
        /// </summary>
        public string[] ExclusionsArray
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Exclusions))
                    return new string[0];

                return Exclusions.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(e => e.Trim())
                    .ToArray();
            }
        }

        public override string ToString()
        {
            if (string.IsNullOrWhiteSpace(Exclusions))
                return Path;
            else
                return $"{Path} (Exclusions: {Exclusions})";
        }
    }

    /// <summary>
    /// Represents a drive mapping entry in the configuration
    /// </summary>
    public class DriveMapping
    {
        /// <summary>
        /// The drive letter (e.g., "V:")
        /// </summary>
        public string DriveLetter { get; set; }

        /// <summary>
        /// The UNC path to map to (e.g., "\\server\share")
        /// </summary>
        public string UncPath { get; set; }

        public override string ToString()
        {
            return $"{DriveLetter} -> {UncPath}";
        }
    }

    /// <summary>
    /// Static helper class to assist with getting and setting values in app.config
    /// </summary>
    public static class ConfigHelper
    {
        private static Logger _logger;
        private static bool _configSectionsValidated = false;
        private static readonly object _validationLock = new object();

        /// <summary>
        /// Static constructor that initializes the logger with the default Log.Logger
        /// and ensures config sections exist
        /// </summary>
        static ConfigHelper()
        {
            _logger = Log.Logger;
            // Ensure config sections exist during static initialization
            EnsureConfigSectionsExist();
        }

        /// <summary>
        /// Sets the logger instance to be used by ConfigHelper
        /// </summary>
        /// <param name="logger">An ILogger implementation</param>
        public static void SetLogger(Logger logger)
        {
            if (logger == null)
                throw new ArgumentNullException(nameof(logger));

            _logger = logger;
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
                    // Handle different type conversions
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
                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

                if (config.AppSettings.Settings[key] != null)
                {
                    config.AppSettings.Settings[key].Value = value.ToString();
                    _logger.Debug($"Updated configuration key '{key}' to value '{value}'");
                }
                else
                {
                    config.AppSettings.Settings.Add(key, value.ToString());
                    _logger.Debug($"Added new configuration key '{key}' with value '{value}'");
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
        /// Gets a list of directory entries from the 'directories' section in app.config
        /// </summary>
        /// <returns>A list of DirectoryEntry objects</returns>
        public static List<DirectoryEntry> GetDirectories()
        {
            try
            {
                var directories = new List<DirectoryEntry>();
                var configFilePath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;

                _logger.Debug($"Loading directories from config file: {configFilePath}");

                XDocument doc = XDocument.Load(configFilePath);
                var directoriesElement = doc.Root.Element("directories");

                if (directoriesElement == null)
                {
                    _logger.Warning("'directories' section not found in config file");
                    return directories;
                }

                int entryIndex = 0;
                int validEntries = 0;
                int skippedEntries = 0;

                foreach (var directoryElement in directoriesElement.Elements("directory"))
                {
                    entryIndex++;

                    try
                    {
                        var pathAttribute = directoryElement.Attribute("path");
                        var exclusionsAttribute = directoryElement.Attribute("exclusions");

                        if (pathAttribute == null)
                        {
                            _logger.Warning($"Directory entry at index {entryIndex} skipped: Missing required 'path' attribute");
                            skippedEntries++;
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(pathAttribute.Value))
                        {
                            _logger.Warning($"Directory entry at index {entryIndex} skipped: Empty 'path' attribute");
                            skippedEntries++;
                            continue;
                        }

                        directories.Add(new DirectoryEntry
                        {
                            Path = pathAttribute.Value,
                            Exclusions = exclusionsAttribute?.Value
                        });

                        validEntries++;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Error processing directory entry at index {entryIndex}. Entry will be skipped: {ex.Message}");
                        skippedEntries++;
                    }
                }

                _logger.Information($"Loaded {validEntries} directories from config (skipped {skippedEntries} invalid entries)");
                return directories;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error retrieving directories from configuration");
                return new List<DirectoryEntry>();
            }
        }

        /// <summary>
        /// Gets a list of drive mappings from the 'driveMappings' section in app.config
        /// </summary>
        /// <returns>A list of DriveMapping objects</returns>
        public static List<DriveMapping> GetDriveMappings()
        {
            try
            {
                var mappings = new List<DriveMapping>();
                var configFilePath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;

                _logger.Debug($"Loading drive mappings from config file: {configFilePath}");

                XDocument doc = XDocument.Load(configFilePath);
                var mappingsElement = doc.Root.Element("driveMappings");

                if (mappingsElement == null)
                {
                    _logger.Warning("'driveMappings' section not found in config file");
                    return mappings;
                }

                int mappingIndex = 0;
                int validMappings = 0;
                int skippedMappings = 0;

                foreach (var mappingElement in mappingsElement.Elements("mapping"))
                {
                    mappingIndex++;

                    try
                    {
                        var driveLetterAttribute = mappingElement.Attribute("driveLetter");
                        var uncPathAttribute = mappingElement.Attribute("uncPath");

                        if (driveLetterAttribute == null)
                        {
                            _logger.Warning($"Drive mapping at index {mappingIndex} skipped: Missing required 'driveLetter' attribute");
                            skippedMappings++;
                            continue;
                        }

                        if (uncPathAttribute == null)
                        {
                            _logger.Warning($"Drive mapping at index {mappingIndex} skipped: Missing required 'uncPath' attribute");
                            skippedMappings++;
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(driveLetterAttribute.Value))
                        {
                            _logger.Warning($"Drive mapping at index {mappingIndex} skipped: Empty 'driveLetter' attribute");
                            skippedMappings++;
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(uncPathAttribute.Value))
                        {
                            _logger.Warning($"Drive mapping at index {mappingIndex} skipped: Empty 'uncPath' attribute");
                            skippedMappings++;
                            continue;
                        }

                        // Additional validation for drive letter format
                        string driveLetter = driveLetterAttribute.Value;
                        if (!driveLetter.EndsWith(":") || driveLetter.Length != 2)
                        {
                            _logger.Warning($"Drive mapping at index {mappingIndex} has potentially invalid drive letter format: '{driveLetter}'. Adding anyway, but verify format.");
                        }

                        // Additional validation for UNC path format
                        string uncPath = uncPathAttribute.Value;
                        if (!uncPath.StartsWith("\\\\"))
                        {
                            _logger.Warning($"Drive mapping at index {mappingIndex} has potentially invalid UNC path format: '{uncPath}'. Adding anyway, but verify format.");
                        }

                        mappings.Add(new DriveMapping
                        {
                            DriveLetter = driveLetter,
                            UncPath = uncPath
                        });

                        validMappings++;
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Error processing drive mapping at index {mappingIndex}. Mapping will be skipped: {ex.Message}");
                        skippedMappings++;
                    }
                }

                _logger.Information($"Loaded {validMappings} drive mappings from config (skipped {skippedMappings} invalid mappings)");
                return mappings;
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
        /// <param name="directoryEntry">The directory entry to add</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool AddDirectory(DirectoryEntry directoryEntry)
        {
            if (directoryEntry == null || string.IsNullOrWhiteSpace(directoryEntry.Path))
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
                    .FirstOrDefault(e => e.Attribute("path")?.Value == directoryEntry.Path);

                if (existingEntry != null)
                {
                    // Update existing entry
                    if (string.IsNullOrWhiteSpace(directoryEntry.Exclusions))
                    {
                        existingEntry.Attribute("exclusions")?.Remove();
                    }
                    else
                    {
                        var exclusionsAttr = existingEntry.Attribute("exclusions");
                        if (exclusionsAttr != null)
                            exclusionsAttr.Value = directoryEntry.Exclusions;
                        else
                            existingEntry.Add(new XAttribute("exclusions", directoryEntry.Exclusions));
                    }

                    _logger.Debug($"Updated directory entry: {directoryEntry.Path}");
                }
                else
                {
                    // Create new entry
                    var newElement = new XElement("directory",
                        new XAttribute("path", directoryEntry.Path));

                    if (!string.IsNullOrWhiteSpace(directoryEntry.Exclusions))
                    {
                        newElement.Add(new XAttribute("exclusions", directoryEntry.Exclusions));
                    }

                    directoriesElement.Add(newElement);
                    _logger.Debug($"Added new directory entry: {directoryEntry.Path}");
                }

                doc.Save(configFilePath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error adding directory entry: {directoryEntry.Path}");
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

            // Validate drive letter format: must be a single letter followed by a colon
            if (!System.Text.RegularExpressions.Regex.IsMatch(driveMapping.DriveLetter, @"^[A-Za-z]:$"))
            {
                _logger.Error($"Invalid drive letter format: '{driveMapping.DriveLetter}'. Format must be a single letter followed by a colon (e.g., 'X:')");
                return false;
            }

            // Validate UNC path format: must start with double backslash
            if (!driveMapping.UncPath.StartsWith("\\\\"))
            {
                _logger.Error($"Invalid UNC path format: '{driveMapping.UncPath}'. UNC path must start with double backslash (e.g., '\\\\server\\share')");
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
        /// <param name="driveLetter">The drive letter of the mapping to remove</param>
        /// <returns>True if successful, false otherwise</returns>
        public static bool RemoveDriveMapping(string driveLetter)
        {
            if (string.IsNullOrWhiteSpace(driveLetter))
            {
                _logger.Error("Cannot remove drive mapping with null or empty drive letter");
                return false;
            }

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

                var mappingElement = mappingsElement.Elements("mapping")
                    .FirstOrDefault(m => m.Attribute("driveLetter")?.Value == driveLetter);

                if (mappingElement != null)
                {
                    mappingElement.Remove();
                    doc.Save(configFilePath);
                    _logger.Debug($"Removed drive mapping: {driveLetter}");
                    return true;
                }
                else
                {
                    _logger.Warning($"Drive mapping not found: {driveLetter}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error removing drive mapping: {driveLetter}");
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

                    // Check that the actual sections exist in the config
                    var directoriesElement = doc.Root.Element("directories");
                    if (directoriesElement == null)
                    {
                        doc.Root.Add(new XElement("directories"));
                        configChanged = true;
                        _logger.Debug("Added missing 'directories' element");
                    }

                    var driveMappingsElement = doc.Root.Element("driveMappings");
                    if (driveMappingsElement == null)
                    {
                        doc.Root.Add(new XElement("driveMappings"));
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
    }
}