using System.Threading.Tasks;
using System.Windows.Input;

namespace DataSentry.UI.ViewModels;

/// <summary>
/// An <see cref="ICommand"/> over an async method, which WPF does not provide and which every MVVM
/// application therefore writes once. It also refuses to run while it is already running: a second
/// scan started on top of the first is never what the user meant.
/// </summary>
internal sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;

    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    /// <summary>WPF asks every command whether it is still enabled whenever anything happens. Let it.</summary>
    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) =>
        !_isRunning && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        _isRunning = true;
        CommandManager.InvalidateRequerySuggested();

        try
        {
            await _execute();
        }
        finally
        {
            _isRunning = false;

            // The scan finished away from any click or keypress, and nothing else would think to ask.
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
