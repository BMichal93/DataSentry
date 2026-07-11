using System.Collections.Generic;
using System.IO;
using System.Linq;
using DataSentry.Core.Abstractions;
using DataSentry.Core.Classification;
using DataSentry.Core.Detection;
using DataSentry.Core.Scanning;
using DataSentry.Data;
using DataSentry.UI;
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
                "email address",
                "phone number",
                "IP address"
            }),
            "a detector that is written but never registered finds nothing at all");
    }
}
