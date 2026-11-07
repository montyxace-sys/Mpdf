using System;
using System.IO;

namespace Mpdf.Installer.Core
{
    public sealed class UninstallEngine
    {
        private readonly InstallLayout _layout;
        private readonly string _installDirectory;
        private readonly Action<int, string> _report;
        private int _lastPercent = -1;

        public UninstallEngine(InstallLayout layout, string installDirectory,
            Action<int, string> report = null)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _installDirectory = installDirectory ?? throw new ArgumentNullException(nameof(installDirectory));
            _report = report;
        }

        public void Uninstall()
        {
            if (SystemHelpers.IsRunningFrom(_installDirectory))
            {
                throw new InstallerException("Cierra Mpdf antes de desinstalarlo.");
            }

            Report(3, "Comprobando la instalación…");
            if (!Directory.Exists(_installDirectory))
            {
                throw new InstallerException("No se encontró la carpeta de instalación de Mpdf.");
            }

            Report(10, "Eliminando accesos directos…");
            RemoveShortcuts();

            Report(34, "Restaurando el sistema…");
            RegistryManager.UnregisterAll(_layout, _installDirectory);
            SystemHelpers.NotifyAssociationsChanged();

            Report(58, "Eliminando archivos…");
            RemoveFiles();

            Report(94, "Notificando a Windows…");
            SystemHelpers.NotifyAssociationsChanged();

            Report(100, "Desinstalación completada.");
        }

        public static void FinishRemoval(string installDirectory)
        {
            SystemHelpers.ScheduleDirectoryRemoval(installDirectory);
        }

        private void RemoveShortcuts()
        {
            var startMenuFolder = Path.Combine(_layout.StartMenuProgramsDirectory, ProductInfo.Name);
            TryDeleteDirectory(startMenuFolder);
            TryDeleteFile(Path.Combine(_layout.StartMenuProgramsDirectory, ProductInfo.Name + ".lnk"));
            TryDeleteFile(Path.Combine(_layout.PublicDesktopDirectory, ProductInfo.Name + ".lnk"));
        }

        private void RemoveFiles()
        {
            var uninstaller = Path.Combine(_installDirectory, ProductInfo.UninstallerName);
            foreach (var file in Directory.GetFiles(_installDirectory))
            {
                if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(uninstaller),
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                TryDeleteFile(file);
            }

            foreach (var subdirectory in Directory.GetDirectories(_installDirectory))
            {
                TryDeleteDirectory(subdirectory);
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // El desinstalador se elimina al cerrar la ventana.
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            catch
            {
                // La carpeta se elimina al cerrar la ventana.
            }
        }

        private void Report(int percent, string message)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            if (percent == _lastPercent)
            {
                return;
            }

            _lastPercent = percent;
            _report?.Invoke(percent, message);
        }
    }
}
