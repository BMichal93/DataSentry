using System.IO;

namespace DataSentry.Data.FileSystem;

/// <summary>
/// A path in its <c>\\?\</c> extended-length form, and the way back. Windows caps an ordinary path at
/// 260 characters, and a shared drive nested a few teams deep runs past that — so every place the data
/// layer hands a path to the file system converts it here first, because the 260-character limit does
/// not apply to the extended form.
/// </summary>
/// <remarks>
/// Nothing above the file system ever sees the prefix: the walker takes it straight back off with
/// <see cref="ToDisplay"/>, so the <c>FilePath</c> on a scan result — in the database, on the screen —
/// reads the way the user wrote it. The extended form is a detail of how the bytes are reached, not part
/// of the file's identity.
/// </remarks>
internal static class ExtendedLengthPath
{
    private const string Prefix = @"\\?\";
    private const string UncPrefix = @"\\?\UNC\";

    /// <summary>
    /// The form to hand the file system. <c>\\?\</c> turns off the 260-character limit — and every bit
    /// of path normalisation with it, which is why the path is fully qualified first: the prefix wants
    /// backslashes, and no <c>.</c> or <c>..</c> left to resolve.
    /// </summary>
    public static string ToFileSystem(string path)
    {
        if (path.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return path;
        }

        string qualified = Path.GetFullPath(path);

        // A UNC path \\server\share takes a different prefix: \\?\UNC\server\share, where the UNC form
        // stands in for the two leading backslashes.
        return qualified.StartsWith(@"\\", StringComparison.Ordinal)
            ? UncPrefix + qualified[2..]
            : Prefix + qualified;
    }

    /// <summary>The path as the user knows it, with the extended-length prefix taken back off.</summary>
    public static string ToDisplay(string path)
    {
        if (path.StartsWith(UncPrefix, StringComparison.Ordinal))
        {
            return @"\\" + path[UncPrefix.Length..];
        }

        return path.StartsWith(Prefix, StringComparison.Ordinal)
            ? path[Prefix.Length..]
            : path;
    }
}
