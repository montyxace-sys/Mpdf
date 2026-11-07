using System;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Mpdf.Installer.Core;

namespace Mpdf.Uninstall
{
    public partial class UninstallWindow : Window
    {
        private enum View
        {
            Confirm,
            Progress,
            Done,
            Error
        }

        private readonly string _installDirectory;
        private readonly InstallLayout _layout;
        private bool _busy;
        private bool _uninstalled;

        public UninstallWindow()
        {
            InitializeComponent();

            _installDirectory = (AppDomain.CurrentDomain.BaseDirectory ?? string.Empty).TrimEnd('\\');
            _layout = InstallLayout.ForMachine().WithInstallDirectory(_installDirectory);
            txtPath.Text = _installDirectory;
        }

        private async void BtnUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (_busy)
            {
                return;
            }

            if (!SystemHelpers.IsElevated())
            {
                ShowError("Este desinstalador necesita permisos de administrador. " +
                          "Vuelve a abrirlo aceptando el aviso de Windows.");
                return;
            }

            _busy = true;
            btnUninstall.IsEnabled = false;
            btnCancel.IsEnabled = false;
            UpdateProgress(0, "Preparando la desinstalación…");
            UpdateView(View.Progress);

            try
            {
                var engine = new UninstallEngine(_layout, _installDirectory, ReportProgress);
                await Task.Run(() => engine.Uninstall());
                _uninstalled = true;
                UpdateView(View.Done);
            }
            catch (Exception ex)
            {
                var message = ex is InstallerException
                    ? ex.Message
                    : "No se pudo completar la desinstalación." + Environment.NewLine + ex.Message;
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

        private void ShowError(string message)
        {
            txtError.Text = message;
            UpdateView(View.Error);
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

            if (_uninstalled)
            {
                _uninstalled = false;
                UninstallEngine.FinishRemoval(_installDirectory);
            }

            base.OnClosing(e);
        }
    }
}
