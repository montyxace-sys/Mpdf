using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace Mpdf.Installer.Core
{
    public static class SystemHelpers
    {
        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);

        private const int ShcneAssocChanged = 0x08000000;
        private const uint ShcnfIdList = 0x0000;

        public static void NotifyAssociationsChanged()
        {
            SHChangeNotify(ShcneAssocChanged, ShcnfIdList, IntPtr.Zero, IntPtr.Zero);
        }

        public static bool IsElevated()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool IsRunningFrom(string directory)
        {
            if (string.IsNullOrEmpty(directory))
            {
                return false;
            }

            var root = Path.GetFullPath(directory).TrimEnd('\\') + "\\";
            foreach (var process in Process.GetProcessesByName("Mpdf"))
            {
                try
                {
                    var path = process.MainModule == null ? null : process.MainModule.FileName;
                    if (!string.IsNullOrEmpty(path) &&
                        Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch
                {
                    // Un proceso de otro usuario no expone MainModule; se ignora.
                }
                finally
                {
                    process.Dispose();
                }
            }

            return false;
        }

        public static void Launch(string exePath)
        {
            Process.Start(new ProcessStartInfo(exePath)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)
            });
        }

        public static void ScheduleDirectoryRemoval(string directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return;
            }

            var command = "/c ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"" + directory.TrimEnd('\\') + "\"";
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", command)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
            }
            catch
            {
                // Si no se puede programar la limpieza, la carpeta queda para borrarla a mano.
            }
        }

        public static long GetDirectorySize(string directory)
        {
            long size = 0;
            if (!Directory.Exists(directory))
            {
                return 0;
            }

            foreach (var file in Directory.GetFiles(directory))
            {
                try
                {
                    size += new FileInfo(file).Length;
                }
                catch
                {
                    // Archivo bloqueado: se ignora para el tamaño estimado.
                }
            }

            return size;
        }
    }
}
