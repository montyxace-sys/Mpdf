using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mpdf.Controls;

public static class InputDialog
{
    public static string? Show(Window owner, string title, string message, string label, string initial)
    {
        var window = new Window
        {
            Title = title,
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Height,
            Width = 430,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            Background = Brushes.White
        };

        var panel = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
            Margin = new Thickness(0, 0, 0, 12)
        });
        panel.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            Margin = new Thickness(0, 0, 0, 5)
        });

        var input = new TextBox
        {
            Text = initial,
            Height = 32,
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 13
        };
        panel.Children.Add(input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var accept = new Button
        {
            Content = "Aceptar",
            IsDefault = true,
            Width = 88,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var cancel = new Button
        {
            Content = "Cancelar",
            IsCancel = true,
            Width = 88,
            Height = 32
        };
        string? result = null;
        accept.Click += (_, _) =>
        {
            result = input.Text.Trim();
            window.DialogResult = true;
        };
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        window.Content = panel;
        window.Loaded += (_, _) =>
        {
            input.Focus();
            input.SelectAll();
        };
        return window.ShowDialog() == true ? result : null;
    }
}
