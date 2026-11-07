using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Mpdf.Installer.Core;

namespace Mpdf.Setup
{
    public partial class SetupWindow : Window
    {
        private enum View
        {
            Options,
            Progress,
            Done,
            Error
        }

        private readonly InstallLayout _layout = InstallLayout.ForMachine();
        private Stream _payload;
        private byte[] _uninstaller;
        private bool _busy;
        private string _installedDirectory;

        public SetupWindow()
        {
            InitializeComponent();
            txtVersion.Text = "Versión " + ProductInfo.Version;
            txtPath.Text = _layout.InstallDirectory;

            if (!LoadPayload())
            {
                ShowError("El instalador está incompleto: faltan los archivos de Mpdf. " +
                          "Vuelve a generarlo ejecutando build-setup.ps1.");
            }
        }

        private bool LoadPayload()
        {
            var assembly = Assembly.GetExecutingAssembly();
            _payload = assembly.GetManifestResourceStream("Mpdf.payload.zip");

            using (var stream = assembly.GetManifestResourceStream("Mpdf.uninstall.exe"))
            {
                if (stream != null)
                {
                    using (var memory = new MemoryStream())
                    {
                        stream.CopyTo(memory);
                        _uninstaller = memory.ToArray();
                    }
                }
            }

            if (_payload == null || _uninstaller == null)
            {
                return false;
            }

            var bytes = PayloadExtractor.GetUncompressedSize(_payload);
            txtSize.Text = "Espacio necesario: " + Math.Max(1, (int)Math.Ceiling(bytes / 1048576.0)) + " MB";
            return true;
        }

        private async void BtnInstall_Click(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                return;
            }

            if (!SystemHelpers.IsElevated())
            {
                ShowError("Este instalador necesita permisos de administrador. " +
                          "Cierra esta ventana y vuelve a abrirlo aceptando el aviso de Windows.");
                return;
            }

            if (_payload == null || _uninstaller == null)
            {
                ShowError("El instalador no contiene los archivos de Mpdf. Descárgalo de nuevo.");
                return;
            }

            var directory = (txtPath.Text ?? string.Empty).Trim();
            if (!TryValidateDirectory(directory, out var error))
            {
                ShowError(error);
                return;
            }

            _layout.InstallDirectory = Path.GetFullPath(directory).TrimEnd('\\');

            var options = new InstallOptions
            {
                DesktopShortcut = chkDesktop.IsChecked == true,
                StartMenuShortcut = chkStartMenu.IsChecked == true,
                AssociatePdf = chkAssociate.IsChecked == true,
                RunAsAdministrator = chkAdmin.IsChecked == true
            };

            _busy = true;
            SetOptionsEnabled(false);
            UpdateProgress(0, "Preparando la instalación…");
            UpdateView(View.Progress);

            try
            {
                var engine = new InstallEngine(_layout, _payload, _uninstaller, ReportProgress);
                await Task.Run(() => engine.Install(options));

                _installedDirectory = engine.InstallDirectory;
                txtDonePath.Text = _installedDirectory;
                UpdateView(View.Done);
            }
            catch (Exception ex)
            {
                var message = ex is InstallerException
                    ? ex.Message
                    : "No se pudo completar la instalación." + Environment.NewLine + ex.Message;
                ShowError(message);
            }
            finally
            {
                _busy = false;
            }
        }

        private void ReportProgress(int percent, string message)
        {
            Dispatcher.BeginInvoke(new Action(() => UpdateProgress(percent, message)));
        }

        private void UpdateProgress(int percent, string message)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            Bar.Value = percent;
            txtPercent.Text = percent + " %";
            txtProgressStatus.Text = message;
        }

        private void UpdateView(View view)
        {
            ProgressView.Visibility = view == View.Progress ? Visibility.Visible : Visibility.Collapsed;
            DoneView.Visibility = view == View.Done ? Visibility.Visible : Visibility.Collapsed;
            ErrorView.Visibility = view == View.Error ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetOptionsEnabled(bool enabled)
        {
            chkDesktop.IsEnabled = enabled;
            chkStartMenu.IsEnabled = enabled;
            chkAssociate.IsEnabled = enabled;
            chkAdmin.IsEnabled = enabled;
            btnChangeFolder.IsEnabled = enabled;
            btnInstall.IsEnabled = enabled;
            btnCancel.IsEnabled = enabled;
        }

        private void ShowError(string message)
        {
            txtError.Text = message;
            UpdateView(View.Error);
        }

        private static bool TryValidateDirectory(string directory, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(directory))
            {
                error = "Escribe la carpeta donde quieres instalar Mpdf.";
                return false;
            }

            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(directory);
            }
            catch
            {
                error = "La carpeta indicada no es válida.";
                return false;
            }

            if (fullPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            {
                error = "La carpeta contiene caracteres no permitidos.";
                return false;
            }

            var fileName = Path.GetFileName(fullPath.TrimEnd('\\'));
            if (string.IsNullOrEmpty(fileName))
            {
                error = "Elige una carpeta dentro de una unidad, no la raíz.";
                return false;
            }

            if (fileName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                error = "El nombre de la carpeta no es válido.";
                return false;
            }

            if (File.Exists(fullPath))
            {
                error = "Ya existe un archivo con ese nombre. Elige otra carpeta.";
                return false;
            }

            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (!string.IsNullOrEmpty(windows) &&
                fullPath.StartsWith(windows.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
            {
                error = "No se puede instalar dentro de la carpeta de Windows.";
                return false;
            }

            return true;
        }

        private void BtnChangeFolder_Click(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                return;
            }

            using (var dialog = new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "Selecciona la carpeta donde se instalará Mpdf";
                dialog.ShowNewFolderButton = true;

                var current = (txtPath.Text ?? string.Empty).Trim();
                if (Directory.Exists(current))
                {
                    dialog.SelectedPath = current;
                }

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                {
                    return;
                }

                var chosen = dialog.SelectedPath.TrimEnd('\\');
                txtPath.Text = string.Equals(Path.GetFileName(chosen), ProductInfo.Name,
                    StringComparison.OrdinalIgnoreCase)
                    ? chosen
                    : Path.Combine(chosen, ProductInfo.Name);
            }
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_installedDirectory))
            {
                return;
            }

            var exe = Path.Combine(_installedDirectory, ProductInfo.ExeName);
            try
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "\"" + exe + "\"")
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                try
                {
                    SystemHelpers.Launch(exe);
                }
                catch (Exception ex)
                {
                    ShowError("No se pudo abrir Mpdf." + Environment.NewLine + ex.Message);
                }
            }
        }

        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_busy)
            {
                return;
            }

            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            if (!_busy)
            {
                Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            if (!_busy)
            {
                Close();
            }
        }

        private void BtnDoneClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            if (_busy)
            {
                e.Cancel = true;
                return;
            }

            base.OnClosing(e);
        }
    }
}
