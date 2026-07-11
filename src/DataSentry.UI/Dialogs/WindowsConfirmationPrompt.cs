using System.Threading.Tasks;
using System.Windows;

namespace DataSentry.UI.Dialogs;

/// <summary>The Windows message box, asked in such a way that the destructive answer is never the easy one.</summary>
public sealed class WindowsConfirmationPrompt : IConfirmationPrompt
{
    public Task<bool> ConfirmAsync(string question, string detail)
    {
        MessageBoxResult answer = MessageBox.Show(
            $"{question}\n\n{detail}",
            "DataSentry",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,

            // The default button is No, and that is not a detail. A confirmation whose default is Yes is
            // not a confirmation — it is a speed bump that a user hitting Enter out of habit will never
            // see. Escape closes this dialog, Enter closes this dialog, and both of them mean the files
            // stay where they are. Deleting takes a deliberate click on the word "Yes".
            MessageBoxResult.No);

        return Task.FromResult(answer == MessageBoxResult.Yes);
    }
}
