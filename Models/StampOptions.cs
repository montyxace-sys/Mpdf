using System.Windows.Media;

namespace Mpdf.Models;

public enum StampPlacement
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    Center,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight
}

public sealed record PageNumberOptions(
    string Format,
    int StartNumber,
    double FontSize,
    Color Color,
    StampPlacement Placement,
    double Margin);

public sealed record WatermarkOptions(
    string Text,
    double SizePercent,
    double Opacity,
    double Angle,
    Color Color);

public sealed record ImageExportOptions(
    string Directory,
    string BaseName,
    string Format,
    int Dpi,
    string PageRange);
