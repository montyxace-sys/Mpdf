using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace Mpdf.Controls;

public sealed record SplitOptions(int PagesPerFile, string Directory, string BaseName);

public static class SplitDialog
{
    public static SplitOptions? Show(Window owner, int totalPages, string defaultName, string initialDirectory)
    {
        var window = new Window
        {
            Title = "Dividir PDF",
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Height,
            Width = 480,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            Background = Brushes.White
        };

        var panel = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
        panel.Children.Add(new TextBlock
        {
            Text = $"El documento tiene {totalPages} página{(totalPages == 1 ? "" : "s")}. " +
                   "Se creará un PDF por cada grupo de páginas.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
            Margin = new Thickness(0, 0, 0, 14)
        });

        var pagesLabel = Label("Páginas por archivo");
        panel.Children.Add(pagesLabel);
        var pagesBox = new TextBox
        {
            Text = "1",
            Height = 32,
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 13
        };
        panel.Children.Add(pagesBox);

        var nameLabel = Label("Nombre base");
        nameLabel.Margin = new Thickness(0, 12, 0, 5);
        panel.Children.Add(nameLabel);
        var nameBox = new TextBox
        {
            Text = defaultName,
            Height = 32,
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 13
        };
        panel.Children.Add(nameBox);

        var folderLabel = Label("Carpeta de destino");
        folderLabel.Margin = new Thickness(0, 12, 0, 5);
        panel.Children.Add(folderLabel);

        var folderGrid = new Grid();
        folderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        folderGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var folderBox = new TextBox
        {
            Text = initialDirectory,
            Height = 32,
            Padding = new Thickness(8, 5, 8, 5),
            FontSize = 12,
            IsReadOnly = true
        };
        folderGrid.Children.Add(folderBox);

        var browse = new Button
        {
            Content = "Examinar…",
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(12, 0, 12, 0)
        };
        Grid.SetColumn(browse, 1);
        folderGrid.Children.Add(browse);
        panel.Children.Add(folderGrid);

        browse.Click += (_, _) =>
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Carpeta de destino",
                InitialDirectory = System.IO.Directory.Exists(folderBox.Text)
                    ? folderBox.Text
                    : initialDirectory
            };
            if (dialog.ShowDialog(window) == true) folderBox.Text = dialog.FolderName;
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var accept = new Button
        {
            Content = "Dividir",
            IsDefault = true,
            Width = 92,
            Height = 32,
            Margin = new Thickness(0, 0, 8, 0)
        };
        var cancel = new Button
        {
            Content = "Cancelar",
            IsCancel = true,
            Width = 92,
            Height = 32
        };
        buttons.Children.Add(accept);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        SplitOptions? result = null;
        accept.Click += (_, _) =>
        {
            if (!int.TryParse(pagesBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture,
                    out var pagesPerFile) || pagesPerFile < 1 || pagesPerFile > totalPages)
            {
                MessageBox.Show(window, $"Indica un número entre 1 y {totalPages}.", "Dividir PDF",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(nameBox.Text) || !Directory.Exists(folderBox.Text))
            {
                MessageBox.Show(window, "Indica un nombre y una carpeta de destino válidos.", "Dividir PDF",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            result = new SplitOptions(pagesPerFile, folderBox.Text, Sanitize(nameBox.Text));
            window.DialogResult = true;
        };

        window.Content = panel;
        window.Loaded += (_, _) =>
        {
            pagesBox.Focus();
            pagesBox.SelectAll();
        };
        return window.ShowDialog() == true ? result : null;

        static TextBlock Label(string text) => new()
        {
            Text = text,
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            Margin = new Thickness(0, 0, 0, 5)
        };
    }

    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        return clean.Length == 0 ? "documento" : clean;
    }
}
