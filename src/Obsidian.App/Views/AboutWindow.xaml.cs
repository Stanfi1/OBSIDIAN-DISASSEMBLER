using System.Windows;
using System.Windows.Input;
using Obsidian.App.Services;

namespace Obsidian.App.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"Version {typeof(App).Assembly.GetName().Version?.ToString(3) ?? "0.1.0"} · Obsidian";
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();
}
