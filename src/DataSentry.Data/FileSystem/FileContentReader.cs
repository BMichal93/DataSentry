using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DataSentry.Core.Abstractions;
using DataSentry.Data.TextExtraction;

namespace DataSentry.Data.FileSystem;

/// <summary>
/// Reads the content of a real file: the text a detector samples, and the hash that settles whether
/// two files are the same file.
/// </summary>
public sealed class FileContentReader : IFileContentReader
{
    private const int StreamBufferSizeBytes = 64 * 1024;

    private readonly IReadOnlyList<ITextExtractor> _formatExtractors;
    private readonly PlainTextExtractor _plainTextExtractor;

    /// <summary>
    /// The plain text extractor is taken separately rather than as one more of the format extractors,
    /// because it is not a peer of theirs: it is the fallback, and it claims every extension. Leaving
    /// it in the list would make the answer depend on the order things happened to be registered in.
    /// </summary>
    public FileContentReader(
        IEnumerable<ITextExtractor> formatExtractors,
        PlainTextExtractor plainTextExtractor)
    {
        _formatExtractors = formatExtractors.ToList();
        _plainTextExtractor = plainTextExtractor;
    }

    public Task<string?> ReadTextSampleAsync(
        string filePath,
        int maxCharacters,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxCharacters);

        string extension = Path.GetExtension(filePath).ToLowerInvariant();

        ITextExtractor extractor = _formatExtractors.FirstOrDefault(candidate => candidate.CanExtract(extension))
            ?? _plainTextExtractor;

        return extractor.ExtractTextAsync(filePath, maxCharacters, cancellationToken);
    }

    public async Task<string> ComputeContentHashAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        await using FileStream stream = FileStreams.OpenRead(filePath, StreamBufferSizeBytes);

        // Streamed, never buffered: a duplicate is just as likely to be a four-gigabyte video as a
        // spreadsheet, and neither belongs in memory.
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);

        return Convert.ToHexString(hash);
    }
}
