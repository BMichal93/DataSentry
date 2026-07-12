using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataSentry.Core.Abstractions;
using DataSentry.Core.Classification;
using DataSentry.Core.Detection;
using DataSentry.Core.Scanning;
using DataSentry.Data;
using DataSentry.UI;
using DataSentry.UI.Dialogs;
using DataSentry.UI.FileActions;
using DataSentry.UI.Scheduling;
using DataSentry.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DataSentry.Tests.UI;

/// <summary>
/// The container, assembled exactly as App.xaml.cs assembles it. A missing registration is a mistake
/// that compiles perfectly and then fails on the user's machine at startup, so it is worth a test:
/// this one fails at build time instead.
/// </summary>
[TestFixture]
public class CompositionRootTests
{
    private ServiceProvider _services = null!;

    [SetUp]
    public void SetUp()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"datasentry-composition-{Guid.NewGuid():N}.db");

        _services = new ServiceCollection()
            .AddDataSentryPersistence(databasePath)
            .AddDataSentryFileSystem()
            .AddDataSentryScanning()
            .AddDataSentryUserInterface()
            .BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
    }

    [TearDown]
    public void TearDown() => _services.Dispose();

    [Test]
    public void CompositionRoot_AsTheApplicationBuildsIt_CanBuildEverythingTheUserInteractsWith()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_services.GetRequiredService<MainViewModel>(), Is.Not.Null);
            Assert.That(_services.GetRequiredService<ScanEngine>(), Is.Not.Null);
        });
    }

    [Test]
    public void CompositionRoot_TheDuplicateSweep_IsRegisteredAlongsideTheRulesRatherThanAsOneOfThem()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_services.GetRequiredService<DuplicateFileSweep>(), Is.Not.Null);

            Assert.That(
                _services.GetServices<IClassificationRule>(),
                Has.None.InstanceOf<DuplicateFileSweep>(),
                "a rule is asked about one file at a time, and no file is a duplicate on its own");
        });
    }

    [Test]
    public void CompositionRoot_TheSeamsCoreTalksThrough_AreTheRealImplementations()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_services.GetRequiredService<IFileSource>(), Is.Not.Null);
            Assert.That(_services.GetRequiredService<IFileContentReader>(), Is.Not.Null);
            Assert.That(_services.GetRequiredService<IScanResultStore>(), Is.Not.Null);
        });
    }

    [Test]
    public void CompositionRoot_TheScanScheduler_IsTheRealWindowsTaskScheduler()
    {
        Assert.That(
            _services.GetRequiredService<IScanScheduler>(),
            Is.InstanceOf<WindowsTaskSchedulerScanScheduler>());
    }

    [Test]
    public void CompositionRoot_TheFolderPicker_IsTheRealWindowsDialog()
    {
        // The one seam where the view model touches Windows, satisfied — as every seam is — at the
        // composition root and nowhere else. A view model that opened a dialog itself could not be
        // tested without a window, which is the whole reason this interface exists.
        Assert.That(_services.GetRequiredService<IFolderPicker>(), Is.InstanceOf<WindowsFolderPicker>());
    }

    [Test]
    public void CompositionRoot_TheThingThatDeletesFiles_IsTheRecycleBinAndNotAPermanentDelete()
    {
        // The single most consequential registration in the application. DataSentry recommends and the
        // user decides — and a user who decides wrong must be able to change their mind, which is only
        // true for as long as this line says "recycle bin" rather than File.Delete.
        Assert.That(_services.GetRequiredService<IFileRecycler>(), Is.InstanceOf<RecycleBinFileRecycler>());
    }

    [Test]
    public void CompositionRoot_TheConfirmationAndTheFileOpener_AreTheRealWindowsOnes()
    {
        Assert.Multiple(() =>
        {
            Assert.That(_services.GetRequiredService<IConfirmationPrompt>(), Is.InstanceOf<WindowsConfirmationPrompt>());
            Assert.That(_services.GetRequiredService<IFileOpener>(), Is.InstanceOf<ShellFileOpener>());
        });
    }

    [Test]
    public void CompositionRoot_TheRules_AreOfferedInThePriorityOrderTheEngineReadsThemIn()
    {
        List<IClassificationRule> rules = _services.GetServices<IClassificationRule>().ToList();

        Assert.That(
            rules.Select(rule => rule.GetType()),
            Is.EqualTo(new[] { typeof(JunkFileRule), typeof(StaleFileRule) }),
            "the first rule with a verdict wins, so a temporary file must be reported as junk and not merely as old");
    }

    [Test]
    public void CompositionRoot_TheDetectors_AreAllRegistered()
    {
        Assert.That(
            _services.GetServices<IPiiDetector>().Select(detector => detector.Name),
            Is.EqualTo(new[]
            {
                "special category term",
                "IBAN",
                "payment card",
                "PESEL",
                "Polish ID card number",
                "email address",
                "phone number",
                "IP address"
            }),
            "a detector that is written but never registered finds nothing at all");
    }
}
