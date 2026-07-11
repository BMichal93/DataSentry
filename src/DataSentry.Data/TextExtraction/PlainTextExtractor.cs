using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DataSentry.Data.FileSystem;

namespace DataSentry.Data.TextExtraction;

/// <summary>
/// Reads a file that is already text — .txt, .csv, .log, and everything else with no format to
/// unwrap. This is the fallback for unknown extensions, which is why it sniffs for binary content
/// rather than trusting the extension: a .dat holding a JPEG must not be handed to a PII detector.
/// </summary>
public sealed class PlainTextExtractor : ITextExtractor
{
    private const int StreamBufferSizeBytes = 64 * 1024;

    /// <summary>The encodings that announce themselves with a byte order mark.</summary>
    private static readonly Encoding[] MarkedEncodings =
    [
        Encoding.UTF8,
        Encoding.Unicode,
        Encoding.BigEndianUnicode
    ];

    /// <summary>
    /// True for anything: this extractor is the last resort, and content is what decides whether the
    /// file is text, not the name it happens to carry.
    /// </summary>
    public bool CanExtract(string extension) => true;

    public async Task<string?> ExtractTextAsync(
        string filePath,
        int maxCharacters,
        CancellationToken cancellationToken = default)
    {
        await using FileStream stream = FileStreams.OpenRead(filePath, StreamBufferSizeBytes);

        // A character is never more than one byte in the making, so reading maxCharacters bytes can only
        // overshoot the budget, never fall short of it. The decoded sample is trimmed back to size after.
        byte[] sample = new byte[maxCharacters];
        int bytesRead = await stream.ReadAtLeastAsync(
            sample,
            maxCharacters,
            throwOnEndOfStream: false,
            cancellationToken);

        string? text = Decode(sample.AsSpan(0, bytesRead));

        return text is null ? null : TextSample.Truncate(text, maxCharacters);
    }

    /// <summary>
    /// Honours a byte order mark when the file carries one. Without a mark, UTF-8 is assumed, and a
    /// null byte is taken to mean the content is binary: running a PII detector over a JPEG turns up
    /// nothing but false positives, so binary content is not sampled at all.
    ///
    /// The mark is checked first because UTF-16 is full of null bytes and would otherwise be mistaken
    /// for binary. Decoding is lenient — a sample cut mid-character at the byte limit yields a
    /// replacement character, not an exception.
    /// </summary>
    private static string? Decode(ReadOnlySpan<byte> content)
    {
        foreach (Encoding encoding in MarkedEncodings)
        {
            if (content.StartsWith(encoding.Preamble))
            {
                return encoding.GetString(content[encoding.Preamble.Length..]);
            }
        }

        bool isBinary = content.IndexOf((byte)0) >= 0;

        return isBinary ? null : Encoding.UTF8.GetString(content);
    }
}
