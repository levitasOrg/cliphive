using System.IO;
using System.Text.Json;

namespace ClipHive;

/// <summary>
/// Persists and loads <see cref="AppSettings"/> as a JSON file in LocalApplicationData.
/// Default config path: %LOCALAPPDATA%\ClipHive\settings.json
/// Writes atomically: write to .tmp then File.Replace.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _configDirectory;
    private readonly string _configPath;
    private readonly string _tempPath;

    /// <summary>
    /// Creates a <see cref="SettingsService"/> using the default
    /// <c>%LOCALAPPDATA%\ClipHive\settings.json</c> path.
    /// </summary>
    public SettingsService()
        : this(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ClipHive"))
    {
    }

    /// <summary>
    /// Creates a <see cref="SettingsService"/> that stores settings in the specified directory.
    /// Intended for use in unit tests.
    /// </summary>
    internal SettingsService(string configDirectory)
    {
        ArgumentException.ThrowIfNullOrEmpty(configDirectory);
        _configDirectory = configDirectory;
        _configPath      = Path.Combine(configDirectory, "settings.json");
        _tempPath        = Path.Combine(configDirectory, "settings.json.tmp");
    }

    /// <summary>Full path to the settings file (useful for diagnostics and tests).</summary>
    internal string ConfigPath => _configPath;

    /// <summary>
    /// Loads settings from disk. Returns a default <see cref="AppSettings"/> if the file
    /// does not exist or cannot be parsed.
    /// </summary>
    public AppSettings Load()
    {
        if (!File.Exists(_configPath))
            return new AppSettings();

        try
        {
            string json = File.ReadAllText(_configPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                ?? new AppSettings();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Corrupt or unreadable — start with defaults
            return new AppSettings();
        }
    }

    /// <summary>
    /// Saves settings atomically: writes to a temporary file then calls
    /// <see cref="File.Replace"/> so the write is crash-safe.
    /// </summary>
    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(_configDirectory);

        string json = JsonSerializer.Serialize(settings, JsonOptions);

        // Write to .tmp file first
        File.WriteAllText(_tempPath, json);

        if (File.Exists(_configPath))
        {
            // Atomically replace: dest ← tmp (backup is discarded)
            File.Replace(_tempPath, _configPath, destinationBackupFileName: null);
        }
        else
        {
            // First-time save: simply move the temp file into place
            File.Move(_tempPath, _configPath);
        }
    }
}
