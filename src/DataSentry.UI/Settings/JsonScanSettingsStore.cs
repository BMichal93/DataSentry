using System.IO;
using System.Text.Json;

namespace DataSentry.UI.Settings;

/// <summary>
/// <c>settings.json</c>, read and written where the SQLite database already lives —
/// <c>%AppData%/DataSentry</c> — so everything the app remembers sits in one place the user never has
/// to go looking for.
/// </summary>
/// <remarks>
/// Synchronous on purpose. The file holds a handful of folder paths, is read once as the window opens
/// and written only when the user edits the exclusion list — there is nothing here to overlap async IO
/// with, and the view model that reads it does so in its constructor, which cannot await. A read that
/// fails, or a file that has been hand-edited into nonsense, is treated as "no settings yet" rather than
/// a crash: it falls back to the defaults exactly as a missing file does. A write that fails costs the
/// user their edit on the next launch, which is a nuisance, not a reason to bring the app down.
/// </remarks>
public sealed class JsonScanSettingsStore : IScanSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly string _settingsFilePath;

    /// <param name="settingsFilePath">
    /// Where the file lives. The app leaves this null and gets the default location; tests point it at a
    /// temporary file.
    /// </param>
    public JsonScanSettingsStore(string? settingsFilePath = null)
    {
        _settingsFilePath = settingsFilePath ?? DefaultSettingsFilePath();
    }

    public ScanSettings? Load()
    {
        try
        {
            if (!File.Exists(_settingsFilePath))
            {
                return null;
            }

            ScanSettings? settings = JsonSerializer.Deserialize<ScanSettings>(
                File.ReadAllText(_settingsFilePath),
                SerializerOptions);

            // A file present but missing the array — hand-edited, or written by an older shape — reads as
            // "no folders excluded", never as a null the rest of the app would have to guard against.
            return settings is null ? null : settings with { ExcludedFolders = settings.ExcludedFolders ?? [] };
        }
        catch (Exception failure) when (failure is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public void Save(ScanSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsFilePath)!);
            File.WriteAllText(_settingsFilePath, JsonSerializer.Serialize(settings, SerializerOptions));
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // Best effort — see the remarks on the type.
        }
    }

    private static string DefaultSettingsFilePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DataSentry",
            "settings.json");
}
