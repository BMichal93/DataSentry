using System.Threading.Tasks;

namespace DataSentry.UI.FileActions;

/// <summary>
/// Opens a file in whatever application the user already uses for that kind of file. Returns null when
/// it opened, or the reason it could not.
/// </summary>
/// <remarks>
/// <b>This is the compliant way to look at a file DataSentry has flagged, and it is why the report can
/// afford to say so little.</b> The report never shows a matched PII value — not the IBAN, not the
/// email address, not the diagnosis — because a report that reprinted them would be a second copy of
/// the very data it exists to police, readable by anyone the report is shown to. But a user asked to
/// make a decision about a file has to be able to see the file.
///
/// So the answer is not to leak the content into the report; it is to open the file where it already
/// lives, in the application that already opens it, behind the access controls that already guard it.
/// Nothing is copied, nothing is extracted, and nothing new is exposed — the user simply reads their
/// own spreadsheet, as they could have done all along.
/// </remarks>
public interface IFileOpener
{
    Task<string?> OpenAsync(string filePath);
}
