using System.Windows.Controls;

namespace DataSentry.UI.Views;

/// <summary>
/// The Search tab. Its DataContext — a <see cref="DataSentry.UI.ViewModels.SearchViewModel"/> —
/// arrives from the window that hosts it, so there is nothing to do here.
/// </summary>
public partial class SearchView : UserControl
{
    public SearchView()
    {
        InitializeComponent();
    }
}
