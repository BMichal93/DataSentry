using System.Windows.Controls;

namespace DataSentry.UI.Views;

/// <summary>
/// The result list both tabs share. Its DataContext arrives from the tab that hosts it — a
/// <see cref="DataSentry.UI.ViewModels.ResultsViewModel"/> — so there is nothing to do here.
/// </summary>
public partial class ResultsView : UserControl
{
    public ResultsView()
    {
        InitializeComponent();
    }
}
