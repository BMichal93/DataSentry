using System.Collections.Generic;
using DataSentry.UI.Settings;

namespace DataSentry.Tests.Fakes;

/// <summary>
/// settings.json without the file. Hands back whatever it was seeded with — null for a first run — and
/// remembers everything written to it, because "was the edit saved?" is exactly what the tests ask.
/// </summary>
internal sealed class FakeScanSettingsStore : IScanSettingsStore
{
    private ScanSettings? _settings;

    /// <param name="initial">What a Load sees before anything is saved. Null means no settings file yet.</param>
    public FakeScanSettingsStore(ScanSettings? initial = null)
    {
        _settings = initial;
    }

    /// <summary>Every list that was written, in order. Empty means the store was never asked to save.</summary>
    public List<ScanSettings> Saved { get; } = [];

    public ScanSettings? Load() => _settings;

    public void Save(ScanSettings settings)
    {
        _settings = settings;
        Saved.Add(settings);
    }
}
