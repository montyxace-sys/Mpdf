using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Mpdf.Models;
using Mpdf.Services;

namespace Mpdf.Controls;

public static class ExportImagesDialog
{
    private sealed record FormatChoice(string Label, string Value);
    private sealed record DpiChoice(string Label, int Value);

    private static readonly FormatChoice[] Formats =
    {
        new("PNG (sin pérdida)", "png"),
        new("JPEG (archivo más pequeño)", "jpg")
    };

    private static readonly DpiChoice[] Resolutions =
    {
        new("72 ppp · pantalla", 72),
        new("150 ppp · buena calidad", 150),
        new("300 ppp · impresión", 300)
    };

    public static ImageExportOptions? Show(Window owner, int pageCount, string defaultName,
        string initialDirectory)
    {
        var window = new Window
        {
            Title = "Exportar páginas como imágenes",
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Height,
            Width = 500,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            Background = Brushes.White
        };

        var panel = new StackPanel { Margin = new Thickness(20, 18, 20, 16) };
        panel.Children.Add(new TextBlock
        {
            Text = $"Se guardará una imagen por página. El documento tiene {pageCount} páginas.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
            Margin = new Thickness(0, 0, 0, 12)
        });

        AddLabel(panel, "Páginas");
        var rangeBox = AddTextBox(panel, $"1-{pageCount}");
        AddHint(panel, "Puedes usar rangos como 1-3,5. Se incluyen las marcas y el texto añadido.");

        AddLabel(panel, "Nombre base");
        var nameBox = AddTextBox(panel, defaultName);

        AddLabel(panel, "Carpeta de destino");
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
                InitialDirectory = Directory.Exists(folderBox.Text) ? folderBox.Text : initialDirectory
            };
            if (dialog.ShowDialog(window) == true) folderBox.Text = dialog.FolderName;
        };

        AddLabel(panel, "Formato");
        var formatBox = new ComboBox
        {
            ItemsSource = Formats,
            DisplayMemberPath = "Label",
            SelectedIndex = 0,
            Height = 32,
            FontSize = 12.5
        };
        panel.Children.Add(formatBox);

        AddLabel(panel, "Resolución");
        var dpiBox = new ComboBox
        {
            ItemsSource = Resolutions,
            DisplayMemberPath = "Label",
            SelectedIndex = 1,
            Height = 32,
            FontSize = 12.5
        };
        panel.Children.Add(dpiBox);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };
        var accept = new Button
        {
            Content = "Exportar imágenes",
            IsDefault = true,
            MinWidth = 130,
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

        ImageExportOptions? result = null;
        accept.Click += (_, _) =>
        {
            var range = rangeBox.Text.Trim();
            try
            {
                DocumentSession.ParsePageIndices(range, pageCount);
            }
            catch (PdfException ex)
            {
                Warn(window, ex.Message);
                return;
            }

            if (string.IsNullOrWhiteSpace(nameBox.Text) || !Directory.Exists(folderBox.Text))
            {
                Warn(window, "Indica un nombre y una carpeta de destino válidos.");
                return;
            }

            var format = (formatBox.SelectedItem as FormatChoice)?.Value ?? "png";
            var dpi = (dpiBox.SelectedItem as DpiChoice)?.Value ?? 150;
            result = new ImageExportOptions(folderBox.Text, nameBox.Text.Trim(), format, dpi, range);
            window.DialogResult = true;
        };

        window.Content = panel;
        window.Loaded += (_, _) =>
        {
            rangeBox.Focus();
            rangeBox.SelectAll();
        };
        return window.ShowDialog() == true ? result : null;

        static TextBox AddTextBox(StackPanel panel, string text)
        {
            var box = new TextBox
            {
                Text = text,
                Height = 32,
                Padding = new Thickness(8, 5, 8, 5),
                FontSize = 13
            };
            panel.Children.Add(box);
            return box;
        }

        static void AddLabel(StackPanel panel, string text) =>
            panel.Children.Add(new TextBlock
            {
                Text = text,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 12, 0, 5)
            });

        static void AddHint(StackPanel panel, string text) =>
            panel.Children.Add(new TextBlock
            {
                Text = text,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0x8A, 0x9E)),
                Margin = new Thickness(0, 4, 0, 0)
            });

        static void Warn(Window window, string message) =>
            MessageBox.Show(window, message, window.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
