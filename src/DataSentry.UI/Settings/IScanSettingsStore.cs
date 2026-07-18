namespace DataSentry.UI.Settings;

/// <summary>
/// Reads and writes the user's scan options at <c>%AppData%/DataSentry/settings.json</c>.
/// </summary>
/// <remarks>
/// <see cref="Load"/> returns <see langword="null"/> when there is no settings file yet — a first run —
/// which the caller answers with the machine defaults rather than an empty list. An empty
/// <see cref="ScanSettings.ExcludedFolders"/> is a different answer: the user who cleared the list meant
/// to clear it, and that must survive a restart just as an added folder does.
/// </remarks>
public interface IScanSettingsStore
{
    ScanSettings? Load();

    void Save(ScanSettings settings);
}
