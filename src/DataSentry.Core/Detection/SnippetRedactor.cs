namespace DataSentry.Core.Detection;

/// <summary>
/// Turns a matched value into the shape of it, never the value itself: "48*********12" rather than the
/// account number underneath. Every detector calls this, and only this, on the way to a
/// <see cref="Models.PiiFinding"/> — the one point a matched value ever exists in memory, on its way to
/// being masked before it is carried any further.
/// </summary>
internal static class SnippetRedactor
{
    /// <summary>Characters shown at each end. Enough to tell one finding from another, not enough to identify anyone.</summary>
    private const int VisibleEdgeLength = 2;

    /// <summary>
    /// Below this length there is nothing left to mask once both edges are shown, so the whole value is
    /// replaced instead — a four-character value with two edges showing would not be redacted at all.
    /// </summary>
    private const int ShortestValueWorthPartiallyRevealing = (VisibleEdgeLength * 2) + 1;

    public static string Redact(string matchedValue)
    {
        if (matchedValue.Length < ShortestValueWorthPartiallyRevealing)
        {
            return new string('*', matchedValue.Length);
        }

        string head = matchedValue[..VisibleEdgeLength];
        string tail = matchedValue[^VisibleEdgeLength..];
        string maskedMiddle = new string('*', matchedValue.Length - (VisibleEdgeLength * 2));

        return $"{head}{maskedMiddle}{tail}";
    }
}
