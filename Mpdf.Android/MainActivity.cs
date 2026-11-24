using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using MpdfApp = Mpdf.App.App;

namespace Mpdf.Android;

[Activity(
    Label = "Mpdf",
    Theme = "@style/MpdfTheme.NoActionBar",
    Icon = "@drawable/Icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : AvaloniaMainActivity<MpdfApp>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) => base.CustomizeAppBuilder(builder);
}
