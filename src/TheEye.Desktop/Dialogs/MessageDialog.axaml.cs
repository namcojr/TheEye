using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;

namespace TheEye.Desktop.Dialogs;

/// <summary>
/// The Avalonia stand-in for the message box the Windows shell uses when it has
/// to tell the user something outside the main window: a crash, or a settings
/// file it could not save.
/// </summary>
public partial class MessageDialog : Window
{
    public MessageDialog()
    {
        InitializeComponent();
    }

    public string Headline
    {
        get => HeadlineText.Text ?? string.Empty;
        init => HeadlineText.Text = value;
    }

    public string Message
    {
        get => MessageText.Text ?? string.Empty;
        init => MessageText.Text = value;
    }

    /// <summary>Shows the dialog over the main window when there is one.</summary>
    public static async Task ShowAsync(string headline, string message, Window? owner = null)
    {
        var dialog = new MessageDialog { Headline = headline, Message = message };
        var mainWindow = owner ??
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

        // A crash can fire before the shell has a window to be modal to, and a
        // modal dialog without an owner would never return on some platforms.
        if (mainWindow is { IsVisible: true })
        {
            await dialog.ShowDialog(mainWindow);
        }
        else
        {
            dialog.Show();
        }
    }

    private void OnOkClick(object? sender, RoutedEventArgs e) => Close();
}
