using System;
using System.IO;
using Microsoft.Win32;

namespace Mpdf.Installer.Core
{
    public sealed class InstallLayout
    {
        public string InstallDirectory { get; set; }
        public string StartMenuProgramsDirectory { get; set; }
        public string PublicDesktopDirectory { get; set; }
        public RegistryKey Hive { get; set; }
        public string UninstallKeyPath { get; set; }
        public string ClassesPath { get; set; }
        public string CapabilitiesPath { get; set; }
        public string RegisteredApplicationsPath { get; set; }
        public string AppCompatLayersPath { get; set; }

        public static InstallLayout ForMachine()
        {
            return new InstallLayout
            {
                InstallDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), ProductInfo.Name),
                StartMenuProgramsDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
                PublicDesktopDirectory = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                Hive = Registry.LocalMachine,
                UninstallKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Mpdf",
                ClassesPath = @"SOFTWARE\Classes",
                CapabilitiesPath = @"SOFTWARE\Mpdf\Capabilities",
                RegisteredApplicationsPath = @"SOFTWARE\RegisteredApplications",
                AppCompatLayersPath = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers"
            };
        }

        public InstallLayout WithInstallDirectory(string directory)
        {
            return new InstallLayout
            {
                InstallDirectory = directory,
                StartMenuProgramsDirectory = StartMenuProgramsDirectory,
                PublicDesktopDirectory = PublicDesktopDirectory,
                Hive = Hive,
                UninstallKeyPath = UninstallKeyPath,
                ClassesPath = ClassesPath,
                CapabilitiesPath = CapabilitiesPath,
                RegisteredApplicationsPath = RegisteredApplicationsPath,
                AppCompatLayersPath = AppCompatLayersPath
            };
        }
    }
}
