using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Mpdf.Models;

namespace Mpdf.Controls;

public static class StampDialog
{
    private sealed record PlacementChoice(string Label, StampPlacement Value);
    private sealed record ColorChoice(string Label, Color Value);
    private sealed record AngleChoice(string Label, double Value);

    private static readonly PlacementChoice[] Placements =
    {
        new("Arriba a la izquierda", StampPlacement.TopLeft),
        new("Arriba centrado", StampPlacement.TopCenter),
        new("Arriba a la derecha", StampPlacement.TopRight),
        new("Centro a la izquierda", StampPlacement.MiddleLeft),
        new("Centro", StampPlacement.Center),
        new("Centro a la derecha", StampPlacement.MiddleRight),
        new("Abajo a la izquierda", StampPlacement.BottomLeft),
        new("Abajo centrado", StampPlacement.BottomCenter),
        new("Abajo a la derecha", StampPlacement.BottomRight)
    };

    private static readonly ColorChoice[] NumberColors =
    {
        new("Gris", Color.FromRgb(0x44, 0x44, 0x44)),
        new("Negro", Color.FromRgb(0x11, 0x18, 0x27)),
        new("Azul", Color.FromRgb(0x1D, 0x4E, 0xD8)),
        new("Rojo", Color.FromRgb(0xB9, 0x1C, 0x1C))
    };

    private static readonly ColorChoice[] WatermarkColors =
    {
        new("Rojo", Color.FromRgb(0xB0, 0x1C, 0x1C)),
        new("Gris", Color.FromRgb(0x6B, 0x72, 0x80)),
        new("Azul", Color.FromRgb(0x1D, 0x4E, 0xD8)),
        new("Verde", Color.FromRgb(0x15, 0x80, 0x3D)),
        new("Negro", Color.FromRgb(0x11, 0x18, 0x27))
    };

    private static readonly AngleChoice[] Angles =
    {
        new("Diagonal (45°)", 45),
        new("Horizontal (0°)", 0),
        new("Diagonal inversa (-45°)", -45)
    };

    public static PageNumberOptions? ShowPageNumbers(Window owner, int pageCount)
    {
        var window = BuildWindow(owner, "Añadir numeración",
            $"Se numerarán las {pageCount} páginas del documento.");

        var panel = (StackPanel)window.Content;
        AddLabel(panel, "Formato");
        var formatBox = AddTextBox(panel, "{n}");
        AddHint(panel, "Usa {n} para el número de página, por ejemplo «Pág. {n} de " + pageCount + "».");

        AddLabel(panel, "Número inicial");
        var startBox = AddTextBox(panel, "1");

        AddLabel(panel, "Posición");
        var placementBox = AddCombo(panel, Placements, 7);

        AddLabel(panel, "Tamaño de letra");
        var sizeBox = AddTextBox(panel, "10");

        AddLabel(panel, "Color");
        var colorBox = AddCombo(panel, NumberColors, 0);

        var (buttons, accept, _) = AddButtons(panel, "Añadir numeración");
        PageNumberOptions? result = null;
        accept.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(formatBox.Text) || !formatBox.Text.Contains("{n}"))
            {
                Warn(window, "El formato debe incluir {n}.");
                return;
            }

            if (!int.TryParse(startBox.Text.Trim(), NumberStyles.Integer,
                    CultureInfo.CurrentCulture, out var start) || start < 0)
            {
                Warn(window, "El número inicial debe ser 0 o mayor.");
                return;
            }

            if (!double.TryParse(sizeBox.Text.Trim().Replace(',', '.'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var size) || size is < 4 or > 96)
            {
                Warn(window, "El tamaño debe estar entre 4 y 96.");
                return;
            }

            var placement = (placementBox.SelectedItem as PlacementChoice)?.Value
                            ?? StampPlacement.BottomCenter;
            var color = (colorBox.SelectedItem as ColorChoice)?.Value ?? NumberColors[0].Value;
            result = new PageNumberOptions(formatBox.Text.Trim(), start, size, color, placement, 24);
            window.DialogResult = true;
        };

        window.Loaded += (_, _) =>
        {
            formatBox.Focus();
            formatBox.SelectAll();
        };
        return window.ShowDialog() == true ? result : null;
    }

    public static WatermarkOptions? ShowWatermark(Window owner, int pageCount)
    {
        var window = BuildWindow(owner, "Marca de agua",
            $"La marca se aplicará a las {pageCount} páginas y se guardará con el documento.");

        var panel = (StackPanel)window.Content;
        AddLabel(panel, "Texto");
        var textBox = AddTextBox(panel, "CONFIDENCIAL");

        AddLabel(panel, "Tamaño");
        var sizeSlider = AddSlider(panel, 20, 100, 70, "% del ancho");

        AddLabel(panel, "Opacidad");
        var opacitySlider = AddSlider(panel, 5, 100, 20, "%");

        AddLabel(panel, "Ángulo");
        var angleBox = AddCombo(panel, Angles, 0);

        AddLabel(panel, "Color");
        var colorBox = AddCombo(panel, WatermarkColors, 0);

        var (buttons, accept, _) = AddButtons(panel, "Aplicar marca de agua");
        WatermarkOptions? result = null;
        accept.Click += (_, _) =>
        {
            var text = textBox.Text.Trim();
            if (text.Length == 0)
            {
                Warn(window, "Escribe el texto de la marca de agua.");
                return;
            }

            if (text.Length > 120)
            {
                Warn(window, "El texto es demasiado largo (máximo 120 caracteres).");
                return;
            }

            var angle = (angleBox.SelectedItem as AngleChoice)?.Value ?? 45;
            var color = (colorBox.SelectedItem as ColorChoice)?.Value ?? WatermarkColors[0].Value;
            result = new WatermarkOptions(text, sizeSlider.Value, opacitySlider.Value / 100.0,
                angle, color);
            window.DialogResult = true;
        };

        window.Loaded += (_, _) =>
        {
            textBox.Focus();
            textBox.SelectAll();
        };
        return window.ShowDialog() == true ? result : null;
    }

    private static Window BuildWindow(Window owner, string title, string description)
    {
        var window = new Window
        {
            Title = title,
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
            Text = description,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x29, 0x3B)),
            Margin = new Thickness(0, 0, 0, 12)
        });
        window.Content = panel;
        return window;
    }

    private static void AddLabel(StackPanel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
            Margin = new Thickness(0, 12, 0, 5)
        });
    }

    private static void AddHint(StackPanel panel, string text)
    {
        panel.Children.Add(new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0x8A, 0x9E)),
            Margin = new Thickness(0, 4, 0, 0)
        });
    }

    private static TextBox AddTextBox(StackPanel panel, string text)
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

    private static ComboBox AddCombo<T>(StackPanel panel, T[] items, int selectedIndex)
    {
        var combo = new ComboBox
        {
            ItemsSource = items,
            DisplayMemberPath = "Label",
            SelectedIndex = selectedIndex,
            Height = 32,
            FontSize = 12.5
        };
        panel.Children.Add(combo);
        return combo;
    }

    private static Slider AddSlider(StackPanel panel, double min, double max, double value, string suffix)
    {
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });

        var slider = new Slider
        {
            Minimum = min,
            Maximum = max,
            Value = value,
            TickFrequency = 5,
            IsSnapToTickEnabled = true,
            VerticalAlignment = VerticalAlignment.Center
        };
        row.Children.Add(slider);

        var readout = new TextBlock
        {
            Text = $"{value:0} {suffix}",
            FontSize = 11.5,
            Foreground = new SolidColorBrush(Color.FromRgb(0x33, 0x41, 0x55)),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(readout, 1);
        row.Children.Add(readout);
        slider.ValueChanged += (_, _) => readout.Text = $"{slider.Value:0} {suffix}";

        panel.Children.Add(row);
        return slider;
    }

    private static (StackPanel Container, Button Accept, Button Cancel) AddButtons(
        StackPanel panel, string acceptText)
    {
        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 20, 0, 0)
        };
        var accept = new Button
        {
            Content = acceptText,
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
        return (buttons, accept, cancel);
    }

    private static void Warn(Window window, string message)
    {
        MessageBox.Show(window, message, window.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
