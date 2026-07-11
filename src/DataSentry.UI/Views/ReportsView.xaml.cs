using System.Windows.Controls;

namespace DataSentry.UI.Views;

/// <summary>
/// The Reports tab. Its DataContext — a <see cref="DataSentry.UI.ViewModels.ReportsViewModel"/> —
/// arrives from the window that hosts it, so there is nothing to do here.
/// </summary>
public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
    }
}
