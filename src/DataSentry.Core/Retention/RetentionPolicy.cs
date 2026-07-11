namespace DataSentry.Core.Retention;

/// <summary>
/// GDPR Art. 5(1)(e): keep data no longer than the purpose needs. The purpose here is
/// "let the user act on a scan", which is short. Not a user setting.
/// </summary>
public static class RetentionPolicy
{
    public const int ReportRetentionDays = 30;

    /// <summary>Reports scanned before this moment are past their retention window.</summary>
    public static DateTimeOffset CutoffFrom(DateTimeOffset nowUtc) =>
        nowUtc.AddDays(-ReportRetentionDays);
}
