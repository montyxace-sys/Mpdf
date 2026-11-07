using System;
using System.IO;

namespace Mpdf.Installer.Core
{
    public sealed class InstallEngine
    {
        private readonly InstallLayout _layout;
        private readonly Stream _payloadZip;
        private readonly byte[] _uninstallerBytes;
        private readonly Action<int, string> _report;
        private int _lastPercent = -1;

        public InstallEngine(InstallLayout layout, Stream payloadZip, byte[] uninstallerBytes,
            Action<int, string> report = null)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _payloadZip = payloadZip;
            _uninstallerBytes = uninstallerBytes;
            _report = report;
        }

        public string InstallDirectory => _layout.InstallDirectory;

        public long PayloadSize => _payloadZip == null ? 0 : PayloadExtractor.GetUncompressedSize(_payloadZip);

        public void Install(InstallOptions options)
        {
            options = options ?? new InstallOptions();
            var directory = _layout.InstallDirectory;
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new InstallerException("No se ha indicado una carpeta de instalación.");
            }

            Report(1, "Preparando la instalación…");
            if (SystemHelpers.IsRunningFrom(directory))
            {
                throw new InstallerException("Cierra Mpdf antes de continuar con la instalación.");
            }

            if (_payloadZip == null)
            {
                throw new InstallerException("El instalador no contiene los archivos de Mpdf.");
            }

            if (_uninstallerBytes == null || _uninstallerBytes.Length == 0)
            {
                throw new InstallerException("El instalador no contiene el desinstalador.");
            }

            Directory.CreateDirectory(directory);

            Report(3, "Extrayendo archivos…");
            PayloadExtractor.Extract(_payloadZip, directory, Report, 4, 70);

            Report(72, "Preparando la desinstalación…");
            File.WriteAllBytes(Path.Combine(directory, ProductInfo.UninstallerName), _uninstallerBytes);

            Report(76, "Creando accesos directos…");
            CreateShortcuts(directory, options);

            Report(88, "Configurando el sistema…");
            var size = SystemHelpers.GetDirectorySize(directory);
            RegistryManager.RegisterInstall(_layout, directory, options, size);

            Report(97, "Notificando a Windows…");
            SystemHelpers.NotifyAssociationsChanged();

            Report(100, "Instalación completada.");
        }

        private void CreateShortcuts(string directory, InstallOptions options)
        {
            var exe = Path.Combine(directory, ProductInfo.ExeName);
            var uninstaller = Path.Combine(directory, ProductInfo.UninstallerName);

            var startMenuFolder = Path.Combine(_layout.StartMenuProgramsDirectory, ProductInfo.Name);
            if (options.StartMenuShortcut)
            {
                ShortcutFactory.Create(Path.Combine(startMenuFolder, ProductInfo.Name + ".lnk"), exe,
                    ProductInfo.Description);
                ShortcutFactory.Create(Path.Combine(startMenuFolder, "Desinstalar Mpdf.lnk"), uninstaller,
                    "Desinstalar Mpdf");
            }
            else
            {
                DeleteDirectory(startMenuFolder);
                DeleteFile(Path.Combine(_layout.StartMenuProgramsDirectory, ProductInfo.Name + ".lnk"));
            }

            var desktopLink = Path.Combine(_layout.PublicDesktopDirectory, ProductInfo.Name + ".lnk");
            if (options.DesktopShortcut)
            {
                ShortcutFactory.Create(desktopLink, exe, ProductInfo.Description);
            }
            else
            {
                DeleteFile(desktopLink);
            }
        }

        private static void DeleteFile(string path)
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
                // Un acceso directo en uso se ignora.
            }
        }

        private static void DeleteDirectory(string path)
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
                // Se reintenta en la próxima instalación.
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
