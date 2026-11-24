using Avalonia;
using Avalonia.iOS;
using Foundation;
using MpdfApp = Mpdf.App.App;

namespace Mpdf.iOS;

[Register("AppDelegate")]
public partial class AppDelegate : AvaloniaAppDelegate<MpdfApp>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder);
}
