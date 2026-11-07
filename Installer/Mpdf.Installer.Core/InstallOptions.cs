namespace Mpdf.Installer.Core
{
    public sealed class InstallOptions
    {
        public bool DesktopShortcut { get; set; } = true;
        public bool StartMenuShortcut { get; set; } = true;
        public bool AssociatePdf { get; set; } = true;
        public bool RunAsAdministrator { get; set; }
    }

    public sealed class InstallProgress
    {
        public InstallProgress(int percent, string message)
        {
            Percent = percent;
            Message = message;
        }

        public int Percent { get; }

        public string Message { get; }
    }
}
