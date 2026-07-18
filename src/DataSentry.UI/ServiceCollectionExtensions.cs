using DataSentry.Core.Classification;
using DataSentry.Core.Detection;
using DataSentry.Core.Scanning;
using DataSentry.UI.Dialogs;
using DataSentry.UI.FileActions;
using DataSentry.UI.Reporting;
using DataSentry.UI.Scheduling;
using DataSentry.UI.Settings;
using DataSentry.UI.ViewModels;
using DataSentry.UI.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DataSentry.UI;

/// <summary>
/// Registers the scan itself, and the window that drives it.
/// </summary>
/// <remarks>
/// The rules and the detectors are registered here rather than in <c>DataSentry.Core</c>, because
/// Core depends on nothing at all — not even on a DI container — and that is worth more than the
/// convenience of an <c>AddDataSentryCore()</c> living next to the classes it registers. Which rules
/// a scan runs is a decision about the product, and the composition root is where the product is
/// assembled.
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// The scan engine and everything it consults. A new rule or a new detector is one more line here
    /// and no change anywhere else — which is the whole reason both sit behind an interface.
    /// </summary>
    public static IServiceCollection AddDataSentryScanning(this IServiceCollection services)
    {
        // Order matters: the first rule with a verdict wins, so a temporary file is reported as junk
        // rather than as merely old — both are true, only one is the reason the user needs.
        services.AddSingleton<IClassificationRule, JunkFileRule>();
        services.AddSingleton<IClassificationRule, StaleFileRule>();

        // Detectors, unlike rules, are all consulted: a file can hold an account number and a diagnosis,
        // and the user needs to be told about both. The order they are registered in is the order the
        // findings are reported in, nothing more — which of them decides the file is settled by the
        // priority order in RecommendationPolicy, and special category data wins it every time.
        services.AddSingleton<IPiiDetector, SpecialCategoryDetector>();
        services.AddSingleton<IPiiDetector, IbanDetector>();
        services.AddSingleton<IPiiDetector, PaymentCardDetector>();
        services.AddSingleton<IPiiDetector, PeselDetector>();
        services.AddSingleton<IPiiDetector, PolishIdCardDetector>();
        services.AddSingleton<IPiiDetector, EmailAddressDetector>();
        services.AddSingleton<IPiiDetector, PhoneNumberDetector>();
        services.AddSingleton<IPiiDetector, IpAddressDetector>();

        // Not a rule, and registered apart from them on purpose: a rule is asked about one file, and no
        // file is a duplicate on its own. This one runs over the results the scan has already written.
        services.AddSingleton<DuplicateFileSweep>();

        services.AddSingleton<ScanEngine>();

        // The delayed start: "scan tonight at 22:00". It owes its place here, not in the UI, to being
        // arithmetic about clocks rather than anything about windows.
        services.AddSingleton<DelayedScanStart>();

        return services;
    }

    public static IServiceCollection AddDataSentryUserInterface(this IServiceCollection services)
    {
        // The one thing in the application that knows a Windows dialog exists. Everything upstream of
        // this line asks for a folder and is told one, which is what keeps the view model testable
        // without a window standing behind it.
        services.AddSingleton<IFolderPicker, WindowsFolderPicker>();

        // The other place the UI touches Windows: the scheduled scan lives in Task Scheduler, so it
        // fires whether or not anyone has the app open.
        services.AddSingleton<IScanScheduler, WindowsTaskSchedulerScanScheduler>();

        // The question that stands between a recommendation and a deleted file. Every path to the
        // recycle bin runs through this, and in the tests it is a fake that can be told to say no.
        services.AddSingleton<IConfirmationPrompt, WindowsConfirmationPrompt>();

        // The only class in DataSentry that can destroy anything — and the only one that can open a
        // flagged file so the user can read what is actually in it, which is the compliant alternative
        // to a report that prints personal data on the screen. Both are conversations with the Windows
        // shell, and both stop here, so that no view model ever has one.
        services.AddSingleton<IFileRecycler, RecycleBinFileRecycler>();
        services.AddSingleton<IFileOpener, ShellFileOpener>();

        // The other seam to a Windows dialog, and the export it feeds: a report the user asked for,
        // written to a file they chose, never anywhere the user did not point at.
        services.AddSingleton<ISaveFilePicker, WindowsSaveFilePicker>();
        services.AddSingleton<ScanReportExporter>();

        // Where the exclusion list is remembered between runs: settings.json, next to the database in
        // %AppData%/DataSentry. The one piece of state the user configures that outlives a session.
        services.AddSingleton<IScanSettingsStore>(_ => new JsonScanSettingsStore());

        services.AddSingleton<ScheduleViewModel>();
        services.AddSingleton<ExclusionListViewModel>();

        // Transient, because Search and Reports each hold their own result list: browsing an old
        // report must never disturb the scan sitting on the other tab.
        services.AddTransient<ResultsViewModel>();

        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<ReportsViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
