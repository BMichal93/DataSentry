using DataSentry.UI.Scheduling;

namespace DataSentry.Tests.UI;

/// <summary>
/// The command line the scheduled task launches the app with. Small, but it is the seam between the
/// Task Scheduler and the scan — a launch misread here either scans nothing or opens a window at
/// five in the afternoon on someone who did not ask for one.
/// </summary>
[TestFixture]
public class HeadlessScanTests
{
    [Test]
    public void FolderPathFrom_AScheduledLaunch_NamesTheFolder() =>
        Assert.That(HeadlessScan.FolderPathFrom(["--scan", @"C:\work"]), Is.EqualTo(@"C:\work"));

    [Test]
    public void FolderPathFrom_AnOrdinaryLaunch_IsNull() =>
        Assert.That(HeadlessScan.FolderPathFrom([]), Is.Null);

    [Test]
    public void FolderPathFrom_TheFlagWithoutAFolder_IsNullRatherThanACrash() =>
        Assert.That(HeadlessScan.FolderPathFrom(["--scan"]), Is.Null);

    [Test]
    public void FolderPathFrom_TheFlagInAnyCase_StillCounts() =>
        Assert.That(HeadlessScan.FolderPathFrom(["--SCAN", @"C:\work"]), Is.EqualTo(@"C:\work"));
}
