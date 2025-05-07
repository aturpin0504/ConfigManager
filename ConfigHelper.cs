using RunLog;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace ConfigManager
{
    /// <summary>
    /// Static helper class to assist with configuration management and access to application settings.
    /// Provides methods for reading and writing configuration values, and managing directory and drive mapping configurations.
    /// </summary>
    public static class ConfigHelper
    {
        private static Logger _logger = Log.Logger;
        private static bool _configValidated = false;
        private static readonly object _lockObj = new object();

        static ConfigHelper()
        {
            // Initialize config sections during static construction
            EnsureConfigSectionsExist();
        }

        /// <summary>
        /// Sets the logger instance to be used by ConfigHelper.
        /// </summary>
        /// <param name="logger">The logger instance to use.</param>
        /// <exception cref="ArgumentNullException">Thrown when the logger is null.</exception>
        public static void SetLogger(Logger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _logger.Information("ConfigHelper logger configured");
        }

        /// <summary>
        /// Gets a configuration value with automatic type conversion from AppSettings.
        /// </summary>
        /// <typeparam name="T">The type to convert the value to.</typeparam>
        /// <param name="key">The configuration key to retrieve.</param>
        /// <param name="defaultValue">Default value to return if the key is not found or conversion fails.</param>
        /// <returns>The configuration value converted to the specified type, or the default value if not found or conversion fails.</returns>
        public static T GetValue<T>(string key, T defaultValue = default)
        {
            try
            {
                string value = ConfigurationManager.AppSettings[key];
                if (string.IsNullOrEmpty(value))
                    return defaultValue;

                // Handle special case for string lists
                if (typeof(T) == typeof(List<string>) || typeof(T) == typeof(IEnumerable<string>))
                {
                    var items = value.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(s => s.Trim())
                        .ToList();
                    return (T)(object)items;
                }

                // Use type converter for standard conversions
                var converter = System.ComponentModel.TypeDescriptor.GetConverter(typeof(T));
                if (converter?.CanConvertFrom(typeof(string)) == true)
                    return (T)converter.ConvertFromString(value);

                // Fallback to basic conversion
                return (T)Convert.ChangeType(value, typeof(T));
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, $"Error getting config value '{key}'. Using default.");
                return defaultValue;
            }
        }

        /// <summary>
        /// Sets a configuration value in AppSettings.
        /// </summary>
        /// <typeparam name="T">The type of the value to set.</typeparam>
        /// <param name="key">The configuration key to set.</param>
        /// <param name="value">The value to set.</param>
        /// <returns>True if the value was successfully set, false otherwise.</returns>
        public static bool SetValue<T>(string key, T value)
        {
            try
            {
                // Convert value to string
                string stringValue;
                if (value is IEnumerable<string> stringList)
                    stringValue = string.Join(",", stringList);
                else if (value is double || value is float || value is decimal)
                    stringValue = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
                else
                    stringValue = value?.ToString() ?? string.Empty;

                var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);

                if (config.AppSettings.Settings[key] != null)
                    config.AppSettings.Settings[key].Value = stringValue;
                else
                    config.AppSettings.Settings.Add(key, stringValue);

                config.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection("appSettings");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error setting config value '{key}'");
                return false;
            }
        }

        /// <summary>
        /// Gets directory configurations from the config file.
        /// </summary>
        /// <returns>A list of directory configurations including their paths and exclusions.</returns>
        public static List<DirectoryConfig> GetDirectories()
        {
            var result = new List<DirectoryConfig>();

            try
            {
                var configPath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
                var doc = XDocument.Load(configPath);

                var directoriesElement = doc.Root?.Element("directories");
                if (directoriesElement == null)
                    return result;

                foreach (var dirElement in directoriesElement.Elements("directory"))
                {
                    var pathAttr = dirElement.Attribute("path");
                    if (pathAttr == null)
                        continue;

                    var dirConfig = new DirectoryConfig { Path = pathAttr.Value };

                    // Get exclusions
                    foreach (var excludeElement in dirElement.Elements("exclude"))
                    {
                        if (!string.IsNullOrWhiteSpace(excludeElement.Value))
                            dirConfig.Exclusions.Add(excludeElement.Value);
                    }

                    result.Add(dirConfig);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading directories from config");
                return result;
            }
        }

        /// <summary>
        /// Gets drive mappings from the config file.
        /// </summary>
        /// <returns>A list of drive mappings containing drive letters and UNC paths.</returns>
        public static List<DriveMapping> GetDriveMappings()
        {
            var result = new List<DriveMapping>();

            try
            {
                var configPath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
                var doc = XDocument.Load(configPath);

                var mappingsElement = doc.Root?.Element("driveMappings");
                if (mappingsElement == null)
                    return result;

                foreach (var mappingElement in mappingsElement.Elements("mapping"))
                {
                    var driveLetterAttr = mappingElement.Attribute("driveLetter");
                    var uncPathAttr = mappingElement.Attribute("uncPath");

                    if (driveLetterAttr != null && uncPathAttr != null &&
                        !string.IsNullOrWhiteSpace(driveLetterAttr.Value) &&
                        !string.IsNullOrWhiteSpace(uncPathAttr.Value))
                    {
                        // Ensure drive letter includes a colon
                        string normalizedDriveLetter = NormalizeDriveLetter(driveLetterAttr.Value, true);

                        result.Add(new DriveMapping
                        {
                            DriveLetter = normalizedDriveLetter,
                            UncPath = uncPathAttr.Value
                        });
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error loading drive mappings from config");
                return result;
            }
        }

        /// <summary>
        /// Adds or updates a directory entry in the config file.
        /// </summary>
        /// <param name="directoryConfig">The directory configuration to add or update.</param>
        /// <returns>True if the operation was successful, false otherwise.</returns>
        public static bool AddDirectory(DirectoryConfig directoryConfig)
        {
            if (directoryConfig == null || string.IsNullOrWhiteSpace(directoryConfig.Path))
                return false;

            try
            {
                var configPath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
                var doc = XDocument.Load(configPath);

                var directoriesElement = doc.Root.Element("directories");
                if (directoriesElement == null)
                {
                    directoriesElement = new XElement("directories");
                    doc.Root.Add(directoriesElement);
                }

                // Look for existing entry
                var existingEntry = directoriesElement.Elements("directory")
                    .FirstOrDefault(e => e.Attribute("path")?.Value == directoryConfig.Path);

                if (existingEntry != null)
                {
                    // Update existing - remove exclusions and re-add
                    existingEntry.Elements("exclude").Remove();
                }
                else
                {
                    // Create new entry
                    existingEntry = new XElement("directory", new XAttribute("path", directoryConfig.Path));
                    directoriesElement.Add(existingEntry);
                }

                // Add exclusions
                foreach (var exclusion in directoryConfig.Exclusions)
                {
                    if (!string.IsNullOrWhiteSpace(exclusion))
                        existingEntry.Add(new XElement("exclude", exclusion));
                }

                doc.Save(configPath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error adding directory: {directoryConfig.Path}");
                return false;
            }
        }

        /// <summary>
        /// Adds or updates a drive mapping in the config file.
        /// </summary>
        /// <param name="driveMapping">The drive mapping to add or update.</param>
        /// <returns>True if the operation was successful, false otherwise.</returns>
        public static bool AddDriveMapping(DriveMapping driveMapping)
        {
            if (driveMapping == null ||
                string.IsNullOrWhiteSpace(driveMapping.DriveLetter) ||
                string.IsNullOrWhiteSpace(driveMapping.UncPath))
                return false;

            try
            {
                var configPath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
                var doc = XDocument.Load(configPath);

                var mappingsElement = doc.Root.Element("driveMappings");
                if (mappingsElement == null)
                {
                    mappingsElement = new XElement("driveMappings");
                    doc.Root.Add(mappingsElement);
                }

                // Normalize drive letter - ensure it has a colon for storage
                string normalizedDriveWithColon = NormalizeDriveLetter(driveMapping.DriveLetter, true);

                // For comparison, we'll use just the letter without colon
                string normalizedDriveForComparison = NormalizeDriveLetter(driveMapping.DriveLetter, false);

                // Look for existing mapping
                var existingMapping = mappingsElement.Elements("mapping")
                    .FirstOrDefault(m => string.Equals(
                        NormalizeDriveLetter(m.Attribute("driveLetter")?.Value, false),
                        normalizedDriveForComparison,
                        StringComparison.OrdinalIgnoreCase));

                if (existingMapping != null)
                {
                    // Update UNC path
                    existingMapping.Attribute("uncPath").Value = driveMapping.UncPath;
                    // Also update the drive letter format to ensure it has a colon
                    existingMapping.Attribute("driveLetter").Value = normalizedDriveWithColon;
                }
                else
                {
                    // Add new mapping with normalized drive letter that includes colon
                    mappingsElement.Add(new XElement("mapping",
                        new XAttribute("driveLetter", normalizedDriveWithColon),
                        new XAttribute("uncPath", driveMapping.UncPath)));
                }

                doc.Save(configPath);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error adding drive mapping: {driveMapping.DriveLetter}");
                return false;
            }
        }

        /// <summary>
        /// Removes a directory from the config file.
        /// </summary>
        /// <param name="path">The path of the directory to remove.</param>
        /// <returns>True if the directory was found and removed, false otherwise.</returns>
        public static bool RemoveDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            try
            {
                var configPath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
                var doc = XDocument.Load(configPath);

                var directoriesElement = doc.Root?.Element("directories");
                if (directoriesElement == null)
                    return false;

                var directoryElement = directoriesElement.Elements("directory")
                    .FirstOrDefault(e => e.Attribute("path")?.Value == path);

                if (directoryElement != null)
                {
                    directoryElement.Remove();
                    doc.Save(configPath);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error removing directory: {path}");
                return false;
            }
        }

        /// <summary>
        /// Removes a drive mapping from the config file.
        /// </summary>
        /// <param name="driveLetter">The drive letter of the mapping to remove.</param>
        /// <returns>True if the drive mapping was found and removed, false otherwise.</returns>
        public static bool RemoveDriveMapping(string driveLetter)
        {
            if (string.IsNullOrWhiteSpace(driveLetter))
                return false;

            // For comparison, use normalized drive letter without colon
            string normalizedDrive = NormalizeDriveLetter(driveLetter, false);

            try
            {
                var configPath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
                var doc = XDocument.Load(configPath);

                var mappingsElement = doc.Root?.Element("driveMappings");
                if (mappingsElement == null)
                    return false;

                // Find the mapping with normalized comparison
                var mappingElement = mappingsElement.Elements("mapping")
                    .FirstOrDefault(m => string.Equals(
                        NormalizeDriveLetter(m.Attribute("driveLetter")?.Value, false),
                        normalizedDrive,
                        StringComparison.OrdinalIgnoreCase));

                if (mappingElement != null)
                {
                    mappingElement.Remove();
                    doc.Save(configPath);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, $"Error removing drive mapping: {driveLetter}");
                return false;
            }
        }

        /// <summary>
        /// Ensures all required config sections exist in the configuration file.
        /// Creates sections for directories and drive mappings if they don't exist.
        /// </summary>
        /// <returns>True if any changes were made to the configuration file, false otherwise.</returns>
        public static bool EnsureConfigSectionsExist()
        {
            if (_configValidated)
                return false;

            lock (_lockObj)
            {
                if (_configValidated)
                    return false;

                try
                {
                    var configPath = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None).FilePath;
                    var doc = XDocument.Load(configPath);
                    bool changed = false;

                    // Check for configSections element
                    var sectionsElement = doc.Root.Element("configSections");
                    if (sectionsElement == null)
                    {
                        sectionsElement = new XElement("configSections");
                        doc.Root.AddFirst(sectionsElement);
                        changed = true;
                    }

                    // Ensure directories section exists
                    if (!HasSection(sectionsElement, "directories"))
                    {
                        sectionsElement.Add(new XElement("section",
                            new XAttribute("name", "directories"),
                            new XAttribute("type", "DummyType, DummyAssembly")));
                        changed = true;
                    }

                    // Ensure driveMappings section exists
                    if (!HasSection(sectionsElement, "driveMappings"))
                    {
                        sectionsElement.Add(new XElement("section",
                            new XAttribute("name", "driveMappings"),
                            new XAttribute("type", "DummyType, DummyAssembly")));
                        changed = true;
                    }

                    // Check for actual sections in the document
                    if (doc.Root.Element("directories") == null)
                    {
                        doc.Root.Add(new XElement("directories"));
                        changed = true;
                    }

                    if (doc.Root.Element("driveMappings") == null)
                    {
                        doc.Root.Add(new XElement("driveMappings"));
                        changed = true;
                    }

                    if (changed)
                        doc.Save(configPath);

                    _configValidated = true;
                    return changed;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error validating config sections");
                    return false;
                }
            }
        }

        // Helper method to check if a section exists
        private static bool HasSection(XElement configSections, string sectionName)
        {
            return configSections.Elements("section")
                .Any(s => string.Equals(s.Attribute("name")?.Value, sectionName, StringComparison.OrdinalIgnoreCase));
        }

        // Helper method to normalize drive letters
        private static string NormalizeDriveLetter(string driveLetter, bool includeColon = true)
        {
            if (string.IsNullOrEmpty(driveLetter))
                return string.Empty;

            // Extract just the drive letter part
            Match match = Regex.Match(driveLetter.Trim(), @"^([A-Za-z]):?");
            if (match.Success)
            {
                string letter = match.Groups[1].Value.ToUpper();
                return includeColon ? letter + ":" : letter;
            }

            return driveLetter;
        }
    }

    /// <summary>
    /// Represents a directory configuration with path and exclusion list.
    /// </summary>
    public class DirectoryConfig
    {
        /// <summary>
        /// Gets or sets the directory path.
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Gets or sets the list of exclusions (subdirectories or patterns to exclude).
        /// </summary>
        public List<string> Exclusions { get; set; } = new List<string>();
    }

    /// <summary>
    /// Represents a drive mapping between a drive letter and a UNC path.
    /// </summary>
    public class DriveMapping
    {
        /// <summary>
        /// Gets or sets the drive letter (e.g., "V:" or "V").
        /// </summary>
        public string DriveLetter { get; set; }

        /// <summary>
        /// Gets or sets the UNC path (e.g., "\\server\share").
        /// </summary>
        public string UncPath { get; set; }

        /// <summary>
        /// Returns a string that represents the current drive mapping.
        /// </summary>
        /// <returns>A string in the format "DriveLetter -> UncPath".</returns>
        public override string ToString() => $"{DriveLetter} -> {UncPath}";
    }
}