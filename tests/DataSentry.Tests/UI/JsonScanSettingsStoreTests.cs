using System.IO;
using System.Linq;
using DataSentry.UI.Settings;

namespace DataSentry.Tests.UI;

/// <summary>
/// The JSON settings store against a real temporary file — the one place the app touches settings.json,
/// so the round trip and the awkward files (missing, empty, corrupt) are worth exercising for real.
/// </summary>
[TestFixture]
public class JsonScanSettingsStoreTests
{
    private string _settingsFilePath = string.Empty;

    [SetUp]
    public void SetUp() =>
        _settingsFilePath = Path.Combine(Path.GetTempPath(), $"datasentry-settings-{Guid.NewGuid():N}.json");

    [TearDown]
    public void TearDown() => File.Delete(_settingsFilePath);

    [Test]
    public void Load_WhenNoFileExistsYet_ReturnsNull()
    {
        var store = new JsonScanSettingsStore(_settingsFilePath);

        Assert.That(store.Load(), Is.Null, "a first run has no settings to load — the caller uses the defaults");
    }

    [Test]
    public void SaveThenLoad_RoundTripsTheExcludedFolders()
    {
        var store = new JsonScanSettingsStore(_settingsFilePath);

        store.Save(new ScanSettings(["C:/Windows", "D:/Archive"]));
        ScanSettings? loaded = store.Load();

        Assert.That(loaded?.ExcludedFolders, Is.EqualTo(new[] { "C:/Windows", "D:/Archive" }));
    }

    [Test]
    public void SaveThenLoad_AnEmptyList_ComesBackEmptyRatherThanNull()
    {
        // The distinction the whole feature turns on: an empty saved list ("I cleared it") must read back
        // as empty, not as the null that means "no file yet, use the defaults".
        var store = new JsonScanSettingsStore(_settingsFilePath);

        store.Save(new ScanSettings([]));
        ScanSettings? loaded = store.Load();

        Assert.Multiple(() =>
        {
            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.ExcludedFolders, Is.Empty);
        });
    }

    [Test]
    public void Load_AFileHandEditedIntoNonsense_ReadsAsNoSettingsRatherThanThrowing()
    {
        File.WriteAllText(_settingsFilePath, "{ this is not json ]");

        var store = new JsonScanSettingsStore(_settingsFilePath);

        Assert.That(store.Load(), Is.Null, "a corrupt settings file falls back to the defaults, exactly as a missing one does");
    }

    [Test]
    public void Load_AFileMissingTheArray_ReadsAsNoFoldersExcluded()
    {
        // A valid JSON object without the property — an older shape, or a hand edit — must not hand back a
        // null list for the rest of the app to trip over.
        File.WriteAllText(_settingsFilePath, "{}");

        var store = new JsonScanSettingsStore(_settingsFilePath);
        ScanSettings? loaded = store.Load();

        Assert.That(loaded?.ExcludedFolders, Is.Empty);
    }

    [Test]
    public void Save_WhenTheFolderDoesNotExistYet_CreatesItAndWritesCamelCasedJson()
    {
        string nestedPath = Path.Combine(
            Path.GetTempPath(),
            $"datasentry-settings-{Guid.NewGuid():N}",
            "settings.json");

        try
        {
            var store = new JsonScanSettingsStore(nestedPath);

            store.Save(new ScanSettings(["C:/Windows"]));

            Assert.Multiple(() =>
            {
                Assert.That(File.Exists(nestedPath), Is.True, "the %AppData% folder is created if it is not there yet");
                Assert.That(
                    File.ReadAllText(nestedPath),
                    Does.Contain("excludedFolders"),
                    "settings.json reads like a settings file, in the camelCase people expect");
            });
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(nestedPath)!, recursive: true);
        }
    }
}
