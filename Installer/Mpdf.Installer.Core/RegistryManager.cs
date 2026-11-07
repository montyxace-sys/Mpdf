using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace Mpdf.Installer.Core
{
    public static class RegistryManager
    {
        private const string PreviousPdfDefaultValue = "MpdfPreviousPdfDefault";

        public static void RegisterInstall(InstallLayout layout, string installDirectory,
            InstallOptions options, long estimatedBytes)
        {
            var exe = Path.Combine(installDirectory, ProductInfo.ExeName);
            var uninstaller = Path.Combine(installDirectory, ProductInfo.UninstallerName);

            using (var key = layout.Hive.CreateSubKey(layout.UninstallKeyPath))
            {
                key.SetValue("DisplayName", ProductInfo.Name);
                key.SetValue("DisplayVersion", ReadVersion(exe));
                key.SetValue("Publisher", ProductInfo.Publisher);
                key.SetValue("DisplayIcon", exe + ",0");
                key.SetValue("InstallLocation", installDirectory);
                key.SetValue("UninstallString", "\"" + uninstaller + "\"");
                key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                key.SetValue("EstimatedSize",
                    (int)Math.Min(int.MaxValue, Math.Max(1, estimatedBytes / 1024)), RegistryValueKind.DWord);
                key.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture));
                key.SetValue("URLInfoAbout", ProductInfo.Url);
                key.SetValue("Comments", ProductInfo.Description);
            }

            if (options.AssociatePdf)
            {
                RegisterAssociations(layout, exe);
            }
            else
            {
                UnregisterAssociations(layout, installDirectory);
            }

            SetRunAsAdministrator(layout, exe, options.RunAsAdministrator);
        }

        public static void UnregisterAll(InstallLayout layout, string installDirectory)
        {
            var exe = Path.Combine(installDirectory, ProductInfo.ExeName);
            UnregisterAssociations(layout, installDirectory);
            SetRunAsAdministrator(layout, exe, false);

            try
            {
                layout.Hive.DeleteSubKeyTree(layout.UninstallKeyPath, false);
            }
            catch
            {
                // Si la clave está bloqueada, Windows la eliminará al reiniciar el explorador.
            }
        }

        private static void RegisterAssociations(InstallLayout layout, string exe)
        {
            using (var classes = layout.Hive.CreateSubKey(layout.ClassesPath))
            {
                using (var progId = classes.CreateSubKey(ProductInfo.ProgId))
                {
                    progId.SetValue(null, ProductInfo.FileTypeName);
                    using (var icon = progId.CreateSubKey("DefaultIcon"))
                    {
                        icon.SetValue(null, "\"" + exe + "\",0");
                    }

                    using (var command = progId.CreateSubKey(@"shell\open\command"))
                    {
                        command.SetValue(null, "\"" + exe + "\" \"%1\"");
                    }
                }

                using (var pdf = classes.CreateSubKey(".pdf"))
                {
                    var previous = pdf.GetValue(null) as string;
                    if (!string.Equals(previous, ProductInfo.ProgId, StringComparison.OrdinalIgnoreCase))
                    {
                        using (var uninstall = layout.Hive.CreateSubKey(layout.UninstallKeyPath))
                        {
                            uninstall.SetValue(PreviousPdfDefaultValue, previous ?? string.Empty);
                        }

                        pdf.SetValue(null, ProductInfo.ProgId);
                    }

                    using (var openWith = pdf.CreateSubKey("OpenWithProgids"))
                    {
                        openWith.SetValue(ProductInfo.ProgId, new byte[0], RegistryValueKind.None);
                    }
                }
            }

            using (var capabilities = layout.Hive.CreateSubKey(layout.CapabilitiesPath))
            {
                capabilities.SetValue("ApplicationName", ProductInfo.Name);
                capabilities.SetValue("ApplicationDescription", ProductInfo.Description);
                capabilities.SetValue("ApplicationIcon", exe + ",0");
                using (var associations = capabilities.CreateSubKey("FileAssociations"))
                {
                    associations.SetValue(".pdf", ProductInfo.ProgId);
                }
            }

            using (var registered = layout.Hive.CreateSubKey(layout.RegisteredApplicationsPath))
            {
                registered.SetValue(ProductInfo.Name, layout.CapabilitiesPath);
            }
        }

        private static void UnregisterAssociations(InstallLayout layout, string installDirectory)
        {
            string previousDefault = null;
            var hasPreviousDefault = false;

            using (var uninstall = layout.Hive.OpenSubKey(layout.UninstallKeyPath))
            {
                if (uninstall != null)
                {
                    var stored = uninstall.GetValue(PreviousPdfDefaultValue) as string;
                    if (uninstall.GetValue(PreviousPdfDefaultValue) != null)
                    {
                        hasPreviousDefault = true;
                        previousDefault = stored;
                    }
                }
            }

            using (var classes = layout.Hive.OpenSubKey(layout.ClassesPath, true))
            {
                if (classes != null)
                {
                    try
                    {
                        classes.DeleteSubKeyTree(ProductInfo.ProgId, false);
                    }
                    catch
                    {
                    }

                    using (var pdf = classes.OpenSubKey(".pdf", true))
                    {
                        if (pdf != null)
                        {
                            using (var openWith = pdf.OpenSubKey("OpenWithProgids", true))
                            {
                                openWith?.DeleteValue(ProductInfo.ProgId, false);
                            }

                            var current = pdf.GetValue(null) as string;
                            if (string.Equals(current, ProductInfo.ProgId, StringComparison.OrdinalIgnoreCase))
                            {
                                if (hasPreviousDefault && !string.IsNullOrWhiteSpace(previousDefault) &&
                                    !string.Equals(previousDefault, ProductInfo.ProgId, StringComparison.OrdinalIgnoreCase))
                                {
                                    pdf.SetValue(null, previousDefault);
                                }
                                else
                                {
                                    pdf.DeleteValue(string.Empty, false);
                                }
                            }
                        }
                    }
                }
            }

            try
            {
                using (var registered = layout.Hive.OpenSubKey(layout.RegisteredApplicationsPath, true))
                {
                    registered?.DeleteValue(ProductInfo.Name, false);
                }

                layout.Hive.DeleteSubKeyTree(layout.CapabilitiesPath, false);
                var parent = ParentPath(layout.CapabilitiesPath);
                if (!string.IsNullOrEmpty(parent))
                {
                    using (var parentKey = layout.Hive.OpenSubKey(parent))
                    {
                        if (parentKey != null && parentKey.SubKeyCount == 0 && parentKey.ValueCount == 0)
                        {
                            layout.Hive.DeleteSubKeyTree(parent, false);
                        }
                    }
                }
            }
            catch
            {
                // La limpieza de capacidades no debe impedir la desinstalación.
            }
        }

        private static void SetRunAsAdministrator(InstallLayout layout, string exe, bool enabled)
        {
            try
            {
                using (var layers = layout.Hive.CreateSubKey(layout.AppCompatLayersPath))
                {
                    if (enabled)
                    {
                        layers.SetValue(exe, "~ RUNASADMIN", RegistryValueKind.String);
                    }
                    else
                    {
                        layers.DeleteValue(exe, false);
                    }
                }
            }
            catch
            {
                // Equipos con la clave protegida: la opción se ignora sin romper la instalación.
            }
        }

        private static string ParentPath(string path)
        {
            var index = path == null ? -1 : path.LastIndexOf('\\');
            return index <= 0 ? string.Empty : path.Substring(0, index);
        }

        private static string ReadVersion(string exe)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exe);
                var parts = (info.FileVersion ?? string.Empty).Split('.');
                if (parts.Length >= 3)
                {
                    return parts[0] + "." + parts[1] + "." + parts[2];
                }
            }
            catch
            {
            }

            return ProductInfo.Version;
        }
    }
}
