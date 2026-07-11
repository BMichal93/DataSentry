using System.Collections.Generic;
using System.Threading.Tasks;
using DataSentry.UI.FileActions;

namespace DataSentry.Tests.Fakes;

/// <summary>Opens nothing. Records what it was asked to open.</summary>
internal sealed class FakeFileOpener : IFileOpener
{
    public List<string> Opened { get; } = [];

    public Task<string?> OpenAsync(string filePath)
    {
        Opened.Add(filePath);

        return Task.FromResult<string?>(null);
    }
}
