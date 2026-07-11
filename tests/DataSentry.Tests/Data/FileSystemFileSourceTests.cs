using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DataSentry.Core.Abstractions;
using DataSentry.Core.Models;
using DataSentry.Data.FileSystem;

namespace DataSentry.Tests.Data;

[TestFixture]
public class FileSystemFileSourceTests
{
    private string _rootPath = string.Empty;
    private IFileSource _fileSource = null!;
    private List<ScanError> _errors = null!;

    [SetUp]
    public void SetUp()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), $"datasentry-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootPath);

        _fileSource = new FileSystemFileSource();
        _errors = [];
    }

    [TearDown]
    public void TearDown()
    {
        Directory.Delete(_rootPath, recursive: true);
    }

    [Test]
    public async Task EnumerateFiles_NestedDirectories_ReturnsEveryFileInTheTree()
    {
        GivenFile("invoice.pdf");
        GivenFile("archive/2019/payroll.xlsx");
        GivenFile("archive/2019/deep/nested/notes.txt");

        IReadOnlyList<FileMetadata> files = await EnumerateAsync();

        Assert.That(
            files.Select(file => file.FileName),
            Is.EquivalentTo(new[] { "invoice.pdf", "payroll.xlsx", "notes.txt" }));
        Assert.That(_errors, Is.Empty);
    }

    [Test]
    public async Task EnumerateFiles_EmptyDirectory_ReturnsNothingAndReportsNoError()
    {
        IReadOnlyList<FileMetadata> files = await EnumerateAsync();

        Assert.That(files, Is.Empty);
        Assert.That(_errors, Is.Empty);
    }

    [Test]
    public async Task EnumerateFiles_EmptyFile_ReportsSizeOfZero()
    {
        GivenFile("empty.csv", contents: string.Empty);

        FileMetadata file = (await EnumerateAsync()).Single();

        Assert.That(file.SizeBytes, Is.Zero);
    }

    [Test]
    public async Task EnumerateFiles_FileWithoutExtension_ReportsAnEmptyExtension()
    {
        GivenFile("LICENSE");

        FileMetadata file = (await EnumerateAsync()).Single();

        Assert.That(file.Extension, Is.Empty);
        Assert.That(file.FileName, Is.EqualTo("LICENSE"));
    }

    [Test]
    public async Task EnumerateFiles_UppercaseExtension_ReportsItLowercased()
    {
        GivenFile("EXPORT.CSV");

        FileMetadata file = (await EnumerateAsync()).Single();

        Assert.That(file.Extension, Is.EqualTo(".csv"));
    }

    [Test]
    public async Task EnumerateFiles_FileLockedByAnotherProcess_StillReturnsItsMetadata()
    {
        string lockedPath = GivenFile("in-use.xlsx", contents: "held open");

        using FileStream _ = new(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);

        FileMetadata file = (await EnumerateAsync()).Single();

        Assert.That(file.FilePath, Is.EqualTo(lockedPath));
        Assert.That(_errors, Is.Empty, "metadata does not require opening the file");
    }

    [Test]
    public async Task EnumerateFiles_UnreadableRoot_ReportsTheErrorInsteadOfThrowing()
    {
        string missingRoot = Path.Combine(_rootPath, "gone");

        IReadOnlyList<FileMetadata> files = await EnumerateAsync(missingRoot);

        Assert.That(files, Is.Empty);
        Assert.That(_errors.Single().Path, Is.EqualTo(missingRoot));
        Assert.That(_errors.Single().Reason, Is.EqualTo("Folder not found"));
    }

    [Test]
    public async Task EnumerateFiles_ExcludedFolder_IsSkippedAlongWithEverythingUnderIt()
    {
        GivenFile("invoice.pdf");
        GivenFile("node_modules/left-pad/index.js");
        GivenFile("node_modules/deep/nested/junk.js");

        string excludedPath = Path.Combine(_rootPath, "node_modules");
        IReadOnlyList<FileMetadata> files = await EnumerateAsync(excludedPaths: [excludedPath]);

        Assert.That(files.Select(file => file.FileName), Is.EquivalentTo(new[] { "invoice.pdf" }));
    }

    [Test]
    public async Task EnumerateFiles_FolderSharingAPrefixWithAnExclusion_IsStillScanned()
    {
        GivenFile("data/report.csv");
        GivenFile("datasentry/report.csv");

        string excludedPath = Path.Combine(_rootPath, "data");
        IReadOnlyList<FileMetadata> files = await EnumerateAsync(excludedPaths: [excludedPath]);

        Assert.That(files.Single().FilePath, Does.Contain("datasentry"), "an exclusion ends on a folder boundary");
    }

    [Test]
    public void EnumerateFiles_Cancelled_StopsTheWalk()
    {
        GivenFile("first.txt");
        GivenFile("second.txt");

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.That(
            async () =>
            {
                await foreach (FileMetadata _ in _fileSource.EnumerateFilesAsync(
                    new ScanScope(_rootPath),
                    _errors.Add,
                    cancellation.Token))
                {
                }
            },
            Throws.InstanceOf<OperationCanceledException>());
    }

    [Test]
    public async Task EnumerateFiles_SizeAndTimestamps_AreReadFromTheFile()
    {
        string filePath = GivenFile("report.txt", contents: "12345");
        DateTimeOffset lastModified = new(2019, 3, 4, 10, 30, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(filePath, lastModified.UtcDateTime);

        FileMetadata file = (await EnumerateAsync()).Single();

        Assert.That(file.SizeBytes, Is.EqualTo(5));
        Assert.That(file.LastModifiedUtc, Is.EqualTo(lastModified));
        Assert.That(file.CreatedUtc.Offset, Is.EqualTo(TimeSpan.Zero), "timestamps are UTC");
    }

    private string GivenFile(string relativePath, string contents = "contents")
    {
        string filePath = Path.Combine(_rootPath, relativePath);

        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        File.WriteAllText(filePath, contents);

        return filePath;
    }

    private async Task<IReadOnlyList<FileMetadata>> EnumerateAsync(
        string? rootPath = null,
        IReadOnlyList<string>? excludedPaths = null)
    {
        var scope = new ScanScope(rootPath ?? _rootPath, excludedPaths ?? []);
        var files = new List<FileMetadata>();

        await foreach (FileMetadata file in _fileSource.EnumerateFilesAsync(scope, _errors.Add))
        {
            files.Add(file);
        }

        return files;
    }
}
