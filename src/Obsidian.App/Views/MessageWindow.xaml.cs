using System.Windows;
using System.Windows.Input;

namespace Obsidian.App.Views;

public partial class MessageWindow : Window
{
    public MessageWindow(string title, string message, string? details = null)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;

        if (!string.IsNullOrWhiteSpace(details))
        {
            DetailsBox.Text = details;
            DetailsBox.Visibility = Visibility.Visible;
        }
    }

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Close_OnClick(object sender, RoutedEventArgs e) => Close();

    private void Ok_OnClick(object sender, RoutedEventArgs e) => Close();
}
