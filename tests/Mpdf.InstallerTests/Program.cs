using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Microsoft.Win32;
using Mpdf.Installer.Core;

namespace Mpdf.InstallerTests
{
    internal static class Program
    {
        private static int _checks;
        private static int _failures;
        private static string _root;

        private static void Main()
        {
            Console.WriteLine("Pruebas del instalador de Mpdf");
            Console.WriteLine("------------------------------");

            _root = Path.Combine(Path.GetTempPath(), "MpdfInstallerTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);

            try
            {
                CleanRegistry();
                TestFullInstallAndUninstall();
                TestMinimalInstall();
                TestReportMonotonic();
            }
            catch (Exception ex)
            {
                _failures++;
                Console.WriteLine("ERROR: " + ex);
            }
            finally
            {
                CleanRegistry();
                TryDelete(_root);
            }

            Console.WriteLine();
            Console.WriteLine("Resultado: " + _checks + " comprobaciones, " + _failures + " fallos.");
            Environment.ExitCode = _failures == 0 ? 0 : 1;
        }

        private static void TestFullInstallAndUninstall()
        {
            Console.WriteLine();
            Console.WriteLine("Instalación completa con accesos, asociación y administrador:");

            var sandbox = Path.Combine(_root, "full");
            var layout = CreateLayout(sandbox);
            var payload = CreatePayload(sandbox);
            var uninstaller = Encoding.UTF8.GetBytes("uninstallstub");
            var progress = new List<int>();

            SetPreviousPdfDefault(layout, "Acrobat.Document");

            var engine = new InstallEngine(layout, payload, uninstaller,
                (percent, message) => progress.Add(percent));
            engine.Install(new InstallOptions
            {
                DesktopShortcut = true,
                StartMenuShortcut = true,
                AssociatePdf = true,
                RunAsAdministrator = true
            });

            var exe = Path.Combine(layout.InstallDirectory, ProductInfo.ExeName);
            var uninstallerPath = Path.Combine(layout.InstallDirectory, ProductInfo.UninstallerName);

            Check(File.Exists(exe), "Se extrae Mpdf.exe");
            Check(File.Exists(uninstallerPath), "Se copia el desinstalador");
            Check(File.ReadAllText(exe).Contains("falso"), "El contenido del ejecutable es correcto");
            Check(File.Exists(Path.Combine(layout.PublicDesktopDirectory, "Mpdf.lnk")),
                "Se crea el acceso directo del escritorio");

            var startMenu = Path.Combine(layout.StartMenuProgramsDirectory, "Mpdf");
            Check(File.Exists(Path.Combine(startMenu, "Mpdf.lnk")), "Se crea el acceso del menú Inicio");
            Check(File.Exists(Path.Combine(startMenu, "Desinstalar Mpdf.lnk")),
                "Se crea el acceso de desinstalación");

            Check(ReadValue(layout, layout.UninstallKeyPath, "DisplayName") == "Mpdf",
                "La entrada de Aplicaciones instaladas tiene nombre");
            Check(ReadValue(layout, layout.UninstallKeyPath, "DisplayVersion") == "0.1.0",
                "La versión es 0.1.0");
            Check(ReadValue(layout, layout.UninstallKeyPath, "Publisher") == "MSoft",
                "El editor es MSoft");
            Check((ReadValue(layout, layout.UninstallKeyPath, "UninstallString") ?? string.Empty)
                    .Contains(ProductInfo.UninstallerName),
                "La entrada apunta al desinstalador");
            Check(ReadDword(layout, layout.UninstallKeyPath, "EstimatedSize") > 0,
                "Se registra el tamaño estimado");

            var command = ReadValue(layout, layout.ClassesPath + "\\" + ProductInfo.ProgId + "\\shell\\open\\command", null);
            Check(command != null && command.Contains(exe) && command.Contains("%1"),
                "El comando de apertura usa Mpdf y el archivo");
            Check(ReadValue(layout, layout.ClassesPath + "\\.pdf", null) == ProductInfo.ProgId,
                "El .pdf queda asociado a Mpdf");
            Check(ValueExists(layout, layout.ClassesPath + "\\.pdf\\OpenWithProgids", ProductInfo.ProgId),
                "Mpdf aparece en Abrir con");
            Check(ReadValue(layout, layout.RegisteredApplicationsPath, "Mpdf") == layout.CapabilitiesPath,
                "Se registran las capacidades de la aplicación");
            Check(ReadValue(layout, layout.CapabilitiesPath + "\\FileAssociations", ".pdf") == ProductInfo.ProgId,
                "Las capacidades incluyen .pdf");
            Check(ReadValue(layout, layout.AppCompatLayersPath, exe) == "~ RUNASADMIN",
                "Se activa ejecutar como administrador");
            Check(progress.Count > 0 && progress[progress.Count - 1] == 100,
                "El progreso termina en 100 %");

            var uninstallProgress = new List<int>();
            var uninstallerEngine = new UninstallEngine(layout, layout.InstallDirectory,
                (percent, message) => uninstallProgress.Add(percent));
            uninstallerEngine.Uninstall();

            Check(!File.Exists(exe), "Se elimina Mpdf.exe");
            Check(File.Exists(uninstallerPath), "El desinstalador sigue hasta cerrar la ventana");
            Check(!Directory.Exists(startMenu), "Se elimina la carpeta del menú Inicio");
            Check(!File.Exists(Path.Combine(layout.PublicDesktopDirectory, "Mpdf.lnk")),
                "Se elimina el acceso del escritorio");
            Check(ReadValue(layout, layout.UninstallKeyPath, "DisplayName") == null,
                "Se elimina la entrada de Aplicaciones instaladas");
            Check(!KeyExists(layout, layout.ClassesPath + "\\" + ProductInfo.ProgId),
                "Se elimina el ProgId");
            Check(ReadValue(layout, layout.ClassesPath + "\\.pdf", null) == "Acrobat.Document",
                "Se restaura la asociación anterior de .pdf");
            Check(!ValueExists(layout, layout.ClassesPath + "\\.pdf\\OpenWithProgids", ProductInfo.ProgId),
                "Se quita Mpdf de Abrir con");
            Check(ReadValue(layout, layout.RegisteredApplicationsPath, "Mpdf") == null,
                "Se quitan las capacidades registradas");
            Check(ReadValue(layout, layout.AppCompatLayersPath, exe) == null,
                "Se quita ejecutar como administrador");
            Check(uninstallProgress.Count > 0 && uninstallProgress[uninstallProgress.Count - 1] == 100,
                "La desinstalación termina en 100 %");
        }

        private static void TestMinimalInstall()
        {
            Console.WriteLine();
            Console.WriteLine("Instalación sin opciones adicionales:");

            var sandbox = Path.Combine(_root, "minimal");
            var layout = CreateLayout(sandbox);
            var payload = CreatePayload(sandbox);
            SetPreviousPdfDefault(layout, "Acrobat.Document");

            var engine = new InstallEngine(layout, payload, Encoding.UTF8.GetBytes("stub"));
            engine.Install(new InstallOptions
            {
                DesktopShortcut = false,
                StartMenuShortcut = false,
                AssociatePdf = false,
                RunAsAdministrator = false
            });

            Check(!File.Exists(Path.Combine(layout.PublicDesktopDirectory, "Mpdf.lnk")),
                "No se crea acceso en el escritorio");
            Check(!Directory.Exists(Path.Combine(layout.StartMenuProgramsDirectory, "Mpdf")),
                "No se crea acceso en el menú Inicio");
            Check(ReadValue(layout, layout.ClassesPath + "\\.pdf", null) == "Acrobat.Document",
                "No se cambia la asociación de .pdf");
            Check(!KeyExists(layout, layout.ClassesPath + "\\" + ProductInfo.ProgId),
                "No se registra el ProgId");
            Check(File.Exists(Path.Combine(layout.InstallDirectory, ProductInfo.ExeName)),
                "El programa se instala igualmente");

            new UninstallEngine(layout, layout.InstallDirectory).Uninstall();
            Check(ReadValue(layout, layout.UninstallKeyPath, "DisplayName") == null,
                "La desinstalación limpia el registro");
        }

        private static void TestReportMonotonic()
        {
            Console.WriteLine();
            Console.WriteLine("Progreso de la instalación:");

            var sandbox = Path.Combine(_root, "progress");
            var layout = CreateLayout(sandbox);
            var payload = CreatePayload(sandbox, 4000000);
            var percents = new List<int>();

            var engine = new InstallEngine(layout, payload, Encoding.UTF8.GetBytes("stub"),
                (percent, message) => percents.Add(percent));
            engine.Install(new InstallOptions
            {
                DesktopShortcut = false,
                StartMenuShortcut = false,
                AssociatePdf = false
            });

            var monotonic = true;
            for (var index = 1; index < percents.Count; index++)
            {
                if (percents[index] < percents[index - 1])
                {
                    monotonic = false;
                }
            }

            Check(monotonic, "El porcentaje nunca retrocede");
            Check(percents.Count >= 3, "Se notifican varios pasos de progreso");
            Check(percents.Max() == 100, "El progreso alcanza el 100 %");
            Check(percents.Distinct().Count() >= 3, "El porcentaje avanza durante la extracción");
        }

        private static InstallLayout CreateLayout(string sandbox)
        {
            var layout = new InstallLayout
            {
                InstallDirectory = Path.Combine(sandbox, "ProgramFiles", "Mpdf"),
                StartMenuProgramsDirectory = Path.Combine(sandbox, "StartMenu", "Programs"),
                PublicDesktopDirectory = Path.Combine(sandbox, "Desktop"),
                Hive = Registry.CurrentUser,
                UninstallKeyPath = @"Software\MpdfInstallerTests\Uninstall\Mpdf",
                ClassesPath = @"Software\MpdfInstallerTests\Classes",
                CapabilitiesPath = @"Software\MpdfInstallerTests\Capabilities",
                RegisteredApplicationsPath = @"Software\MpdfInstallerTests\RegisteredApplications",
                AppCompatLayersPath = @"Software\MpdfInstallerTests\Layers"
            };
            Directory.CreateDirectory(layout.StartMenuProgramsDirectory);
            Directory.CreateDirectory(layout.PublicDesktopDirectory);
            return layout;
        }

        private static MemoryStream CreatePayload(string sandbox, int padding = 0)
        {
            Directory.CreateDirectory(sandbox);
            var stream = new MemoryStream();
            using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            {
                var entry = archive.CreateEntry(ProductInfo.ExeName, CompressionLevel.Optimal);
                using (var writer = new StreamWriter(entry.Open(), Encoding.UTF8))
                {
                    writer.Write("ejecutable falso de Mpdf");
                    if (padding > 0)
                    {
                        writer.Write(new string('x', padding));
                    }
                }
            }

            stream.Position = 0;
            return stream;
        }

        private static void SetPreviousPdfDefault(InstallLayout layout, string value)
        {
            using (var key = layout.Hive.CreateSubKey(layout.ClassesPath + "\\.pdf"))
            {
                key.SetValue(null, value);
            }
        }

        private static string ReadValue(InstallLayout layout, string path, string name)
        {
            using (var key = layout.Hive.OpenSubKey(path))
            {
                return key == null ? null : key.GetValue(name) as string;
            }
        }

        private static int ReadDword(InstallLayout layout, string path, string name)
        {
            using (var key = layout.Hive.OpenSubKey(path))
            {
                var value = key == null ? null : key.GetValue(name);
                return value == null ? 0 : Convert.ToInt32(value);
            }
        }

        private static bool KeyExists(InstallLayout layout, string path)
        {
            using (var key = layout.Hive.OpenSubKey(path))
            {
                return key != null;
            }
        }

        private static bool ValueExists(InstallLayout layout, string path, string name)
        {
            using (var key = layout.Hive.OpenSubKey(path))
            {
                if (key == null)
                {
                    return false;
                }

                return key.GetValueNames().Any(value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
            }
        }

        private static void CleanRegistry()
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\MpdfInstallerTests", false);
            }
            catch
            {
            }
        }

        private static void Check(bool condition, string description)
        {
            _checks++;
            if (condition)
            {
                return;
            }

            _failures++;
            Console.WriteLine("  FALLO: " + description);
        }

        private static void TryDelete(string path)
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
            }
        }
    }
}
