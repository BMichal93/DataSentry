using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DataSentry.UI.ViewModels;

/// <summary>
/// The <see cref="INotifyPropertyChanged"/> plumbing, written once. Every MVVM application needs it and
/// WPF does not supply it; there is nothing to decide here, so there is nothing to repeat in three
/// view models either.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        Notify(propertyName);
    }

    /// <summary>
    /// For the properties that are computed from others rather than stored — "Showing 1–100 of 482"
    /// changes when the page does, and has no field of its own to set.
    /// </summary>
    protected void Notify([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
