using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Mpdf.Controls;

public static class PasswordDialog
{
    public static string? Show(Window owner, string message)
    {
        var window = new Window
        {
            Title = "Documento protegido",
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Height,
            Width = 392,
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

        var password = new PasswordBox
        {
            Height = 32,
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 13
        };
        panel.Children.Add(password);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var accept = new Button
        {
            Content = "Abrir",
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
            result = password.Password;
            window.DialogResult = true;
        };
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        window.Content = panel;
        window.Loaded += (_, _) => password.Focus();
        return window.ShowDialog() == true ? result : null;
    }
}
