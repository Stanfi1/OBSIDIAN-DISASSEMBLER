using System.Windows;
using System.Windows.Input;
using Obsidian.App.Services;

namespace Obsidian.App.Views;

public partial class SaveChangesWindow : Window
{
    public SaveChangesWindow(string title, string message)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
    }

    public SaveChangesDecision Decision { get; private set; } = SaveChangesDecision.Cancel;

    private void Header_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void Save_OnClick(object sender, RoutedEventArgs e) => CloseWith(SaveChangesDecision.Save);

    private void Discard_OnClick(object sender, RoutedEventArgs e) => CloseWith(SaveChangesDecision.Discard);

    private void Cancel_OnClick(object sender, RoutedEventArgs e) => CloseWith(SaveChangesDecision.Cancel);

    private void CloseWith(SaveChangesDecision decision)
    {
        Decision = decision;
        DialogResult = decision == SaveChangesDecision.Cancel ? false : true;
        Close();
    }
}
