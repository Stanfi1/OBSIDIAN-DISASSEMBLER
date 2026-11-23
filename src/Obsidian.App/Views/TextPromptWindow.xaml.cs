using System.Windows;
using System.Windows.Input;

namespace Obsidian.App.Views;

public partial class TextPromptWindow : Window
{
    public TextPromptWindow(string title, string prompt, string initialValue, bool multiline)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        PromptText.Text = prompt;
        ValueBox.Text = initialValue;

        if (multiline)
        {
            ValueBox.AcceptsReturn = true;
            ValueBox.TextWrapping = TextWrapping.Wrap;
            ValueBox.MinHeight = 92;
            ValueBox.VerticalContentAlignment = VerticalAlignment.Top;
            ValueBox.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
        }
    }

    public string Value => ValueBox.Text;

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Ok_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void Cancel_OnClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
