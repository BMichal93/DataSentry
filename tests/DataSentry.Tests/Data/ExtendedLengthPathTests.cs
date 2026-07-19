using DataSentry.Data.FileSystem;

namespace DataSentry.Tests.Data;

/// <summary>
/// The <c>\\?\</c> conversion, on its own. The path strings it produces are what let a scan reach past
/// 260 characters, and the fiddly parts — UNC, already-prefixed, the round trip back to a plain path —
/// are exactly where an off-by-one would quietly break a deep scan, so they get their own tests.
/// </summary>
[TestFixture]
public class ExtendedLengthPathTests
{
    [TestCase(@"C:\Users\me\Documents", @"\\?\C:\Users\me\Documents", TestName = "ToFileSystem_ADrivePath_GetsThePrefix")]
    [TestCase(@"C:/Users/me/Documents", @"\\?\C:\Users\me\Documents", TestName = "ToFileSystem_ForwardSlashes_AreNormalisedToBackslashes")]
    [TestCase(@"\\server\share\team\file", @"\\?\UNC\server\share\team\file", TestName = "ToFileSystem_AUncPath_GetsTheUncPrefix")]
    [TestCase(@"\\?\C:\already\extended", @"\\?\C:\already\extended", TestName = "ToFileSystem_AnAlreadyExtendedPath_IsLeftAlone")]
    public void ToFileSystem_ConvertsToTheExtendedForm(string input, string expected)
    {
        Assert.That(ExtendedLengthPath.ToFileSystem(input), Is.EqualTo(expected));
    }

    [TestCase(@"\\?\C:\Users\me\Documents", @"C:\Users\me\Documents", TestName = "ToDisplay_AnExtendedDrivePath_LosesThePrefix")]
    [TestCase(@"\\?\UNC\server\share\team", @"\\server\share\team", TestName = "ToDisplay_AnExtendedUncPath_ComesBackAsAUncPath")]
    [TestCase(@"C:\Users\me\Documents", @"C:\Users\me\Documents", TestName = "ToDisplay_APlainPath_IsLeftAlone")]
    public void ToDisplay_TakesThePrefixBackOff(string input, string expected)
    {
        Assert.That(ExtendedLengthPath.ToDisplay(input), Is.EqualTo(expected));
    }

    [TestCase(@"C:\Users\me\a\b\c")]
    [TestCase(@"\\server\share\team\file")]
    public void ToDisplay_UndoesToFileSystem(string path)
    {
        Assert.That(ExtendedLengthPath.ToDisplay(ExtendedLengthPath.ToFileSystem(path)), Is.EqualTo(path));
    }
}
