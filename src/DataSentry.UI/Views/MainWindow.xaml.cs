using System.Windows;
using DataSentry.UI.ViewModels;

namespace DataSentry.UI.Views;

/// <summary>
/// The main window. It is handed its view model rather than making one, and does nothing else: every
/// control on it is a binding, so there is no logic here to get wrong and none to test.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();

        DataContext = viewModel;
    }
}
