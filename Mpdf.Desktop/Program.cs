using Avalonia;
using MpdfApp = Mpdf.App.App;

namespace Mpdf.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<MpdfApp>().UsePlatformDetect();
}
