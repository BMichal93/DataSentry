using DataSentry.Core.Models;

namespace DataSentry.Core.Detection;

/// <summary>
/// One kind of personal data, looked for in a sample of a file's text. Each detector is a class of
/// its own, so a new one is added by registering it — never by editing the detectors already there.
/// </summary>
/// <remarks>
/// A detector is handed a <see cref="string"/> and nothing else. It never learns what file the text
/// came from, never opens anything, and never returns what it matched — only the category, the count
/// and how sure it is. <see cref="PiiFinding"/> has nowhere to put the value, which is the point: a
/// tool that leaks the data it was built to protect is a breach, and the surest way to never leak it
/// is to never carry it.
/// </remarks>
public interface IPiiDetector
{
    /// <summary>The name the count is reported under, e.g. "IBAN" in "3 IBANs".</summary>
    string Name { get; }

    PiiCategory Category { get; }

    /// <summary>What the detector found, or null when it found nothing.</summary>
    PiiFinding? Detect(string text);
}
