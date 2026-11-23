using System.Windows;
using System.Windows.Input;
using Obsidian.App.Services;

namespace Obsidian.App.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Save_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OpenDataFolder_OnClick(object sender, RoutedEventArgs e)
    {
        try
        {
            ShellHelper.OpenFolder(ShellPaths.DataDirectory);
        }
        catch (Exception ex)
        {
            var message = new MessageWindow("Could not open folder", ex.Message, ex.ToString())
            {
                Owner = this,
            };
            message.ShowDialog();
        }
    }
}
