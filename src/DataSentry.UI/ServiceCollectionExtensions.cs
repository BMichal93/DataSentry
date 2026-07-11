using DataSentry.Core.Classification;
using DataSentry.Core.Detection;
using DataSentry.Core.Scanning;
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
        services.AddSingleton<IPiiDetector, EmailAddressDetector>();
        services.AddSingleton<IPiiDetector, PhoneNumberDetector>();
        services.AddSingleton<IPiiDetector, IpAddressDetector>();

        // Not a rule, and registered apart from them on purpose: a rule is asked about one file, and no
        // file is a duplicate on its own. This one runs over the results the scan has already written.
        services.AddSingleton<DuplicateFileSweep>();

        services.AddSingleton<ScanEngine>();

        return services;
    }

    public static IServiceCollection AddDataSentryUserInterface(this IServiceCollection services)
    {
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();

        return services;
    }
}
