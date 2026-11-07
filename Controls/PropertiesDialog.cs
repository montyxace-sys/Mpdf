using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mpdf.Models;
using Mpdf.Services;

namespace Mpdf.Controls;

public static class PropertiesDialog
{
    private static readonly Brush Ink = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
    private static readonly Brush Muted = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
    private static readonly Brush Line = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));

    public static void Show(Window owner, DocumentSession session, string? filePath)
    {
        var info = session.Info;

        var window = new Window
        {
            Title = "Propiedades del documento",
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            SizeToContent = SizeToContent.Height,
            Width = 580,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.ToolWindow,
            ShowInTaskbar = false,
            Background = Brushes.White
        };

        var root = new StackPanel { Margin = new Thickness(22, 18, 22, 16) };
        root.Children.Add(new TextBlock
        {
            Text = "Propiedades del documento",
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = Ink
        });
        root.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(filePath) ? "Sin título" : Path.GetFileName(filePath),
            FontSize = 11,
            Foreground = Muted,
            Margin = new Thickness(0, 3, 0, 14)
        });

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(136) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var row = 0;
        Add("Archivo", string.IsNullOrWhiteSpace(filePath) ? "—" : filePath);
        Add("Páginas", session.Pages.Count.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            Add("Tamaño", FormatSize(new FileInfo(filePath).Length));
        Add("Versión", info.VersionText);
        Add("Título", OrDash(info.Title));
        Add("Autor", OrDash(info.Author));
        Add("Asunto", OrDash(info.Subject));
        Add("Palabras clave", OrDash(info.Keywords));
        Add("Creado con", OrDash(info.Creator));
        Add("Productor", OrDash(info.Producer));
        Add("Fecha de creación", OrDash(FormatPdfDate(info.CreationDate)));
        Add("Última modificación", OrDash(FormatPdfDate(info.ModificationDate)));
        Add("Cifrado", info.IsEncrypted
            ? $"Sí (revisión {info.SecurityHandlerRevision})"
            : "No");
        Add("Permisos", DescribePermissions(info));
        Add("Marcadores", session.Bookmarks.Count.ToString(CultureInfo.InvariantCulture));
        Add("Adjuntos", session.Attachments.Count.ToString(CultureInfo.InvariantCulture));

        var scroll = new ScrollViewer
        {
            MaxHeight = 440,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Content = grid
        };
        root.Children.Add(scroll);

        var close = new Button
        {
            Content = "Cerrar",
            IsDefault = true,
            IsCancel = true,
            Width = 92,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        close.Click += (_, _) => window.Close();
        root.Children.Add(close);

        window.Content = root;
        window.ShowDialog();

        void Add(string label, string value)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var name = new TextBlock
            {
                Text = label,
                FontSize = 11,
                Foreground = Muted,
                Margin = new Thickness(0, 0, 12, 9)
            };
            Grid.SetRow(name, row);
            Grid.SetColumn(name, 0);
            grid.Children.Add(name);

            var text = new TextBox
            {
                Text = value,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = Ink,
                FontSize = 11.5,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(0),
                Margin = new Thickness(0, 0, 0, 9),
                Cursor = System.Windows.Input.Cursors.IBeam
            };
            Grid.SetRow(text, row);
            Grid.SetColumn(text, 1);
            grid.Children.Add(text);

            row++;
        }
    }

    private static string OrDash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string FormatSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
            _ => $"{bytes / (1024.0 * 1024.0):0.##} MB"
        };
    }

    private static string FormatPdfDate(string raw)
    {
        if (raw.StartsWith("D:", StringComparison.Ordinal) && raw.Length >= 16)
        {
            var value = raw.Substring(2, 14);
            if (DateTime.TryParseExact(value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
                return date.ToString("g", CultureInfo.CurrentCulture);
        }
        return raw;
    }

    private static string DescribePermissions(DocumentInfo info)
    {
        if (!info.IsEncrypted) return "Sin restricciones";

        var permissions = new List<string>();
        if (info.CanPrint) permissions.Add("imprimir");
        if (info.CanCopy) permissions.Add("copiar");
        if (info.CanModify) permissions.Add("modificar");
        if (info.CanAnnotate) permissions.Add("anotar");
        if (info.CanFillForms) permissions.Add("rellenar formularios");
        if (info.CanAssemble) permissions.Add("ensamblar");
        return permissions.Count == 0 ? "Solo lectura" : string.Join(", ", permissions);
    }
}
