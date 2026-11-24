using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Mpdf.Core;

namespace Mpdf.App;

public partial class MainView : UserControl
{
    private PortablePdf? _pdf;
    private int _page;

    public MainView()
    {
        InitializeComponent();
    }

    private string OutputPath => (OutputBox.Text ?? string.Empty).Trim().Trim('"');

    private double Dpi => double.TryParse(DpiBox.Text, out var dpi) ? dpi : 150;

    private void Status(string text) => StatusLabel.Text = text;

    private void OpenButton_Click(object? sender, RoutedEventArgs e)
    {
        var path = (PathBox.Text ?? string.Empty).Trim().Trim('"');
        try
        {
            _pdf?.Dispose();
            _pdf = PortablePdf.Open(path);
            _page = 0;
            Render();
            Status($"Abierto: {Path.GetFileName(path)} — {_pdf.PageCount} páginas.");
        }
        catch (Exception ex)
        {
            Status($"No se pudo abrir: {ex.Message}");
        }
    }

    private void Render()
    {
        if (_pdf == null) return;
        (PageImage.Source as IDisposable)?.Dispose();
        using var stream = new MemoryStream(_pdf.RenderPage(_page, Dpi));
        PageImage.Source = new Bitmap(stream);
        PageLabel.Text = $"{_page + 1} / {_pdf.PageCount}";
    }

    private void PrevButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_pdf == null || _page <= 0) return;
        _page--;
        Render();
    }

    private void NextButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_pdf == null || _page >= _pdf.PageCount - 1) return;
        _page++;
        Render();
    }

    private void ExportImageButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_pdf == null || OutputPath.Length == 0) return;
        try
        {
            File.WriteAllBytes(OutputPath, _pdf.RenderPage(_page, Dpi));
            Status($"Imagen exportada: {OutputPath}");
        }
        catch (Exception ex)
        {
            Status($"No se pudo exportar la imagen: {ex.Message}");
        }
    }

    private void ExportTextButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_pdf == null || OutputPath.Length == 0) return;
        try
        {
            File.WriteAllText(OutputPath, _pdf.GetDocumentText());
            Status($"Texto exportado: {OutputPath}");
        }
        catch (Exception ex)
        {
            Status($"No se pudo exportar el texto: {ex.Message}");
        }
    }

    private void RotateButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_pdf == null) return;
        try
        {
            _pdf.RotatePage(_page, 1);
            Render();
            Status("Página girada 90°. Usa «Guardar como» para conservar el cambio.");
        }
        catch (Exception ex)
        {
            Status($"No se pudo girar: {ex.Message}");
        }
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_pdf == null || OutputPath.Length == 0) return;
        try
        {
            _pdf.SaveAs(OutputPath);
            Status($"PDF guardado: {OutputPath}");
        }
        catch (Exception ex)
        {
            Status($"No se pudo guardar: {ex.Message}");
        }
    }
}
