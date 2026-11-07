using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Mpdf.Controls;
using Mpdf.Models;
using Mpdf.Services;

namespace Mpdf;

public partial class MainWindow : Window
{
    private const string PdfFilter = "Documentos PDF (*.pdf)|*.pdf|Todos los archivos (*.*)|*.*";

    private readonly DispatcherTimer _searchTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly DispatcherTimer _fitTimer = new() { Interval = TimeSpan.FromMilliseconds(140) };

    private DocumentSession? _session;
    private PageModel? _hookedPage;
    private bool _syncingSelection;
    private bool _syncingComments;
    private bool _syncingSidebarTabs;
    private bool _fitMode = true;
    private bool _closingAfterSave;
    private bool _fullscreen;
    private WindowState _stateBeforeFullscreen = WindowState.Normal;
    private Rect _boundsBeforeFullscreen;
    private int _searchVersion;
    private string? _lastSearch;
    private List<PaletteCommand> _paletteCommands = new();

    public MainWindow()
    {
        InitializeComponent();

        var workArea = SystemParameters.WorkArea;
        Width = Math.Min(Width, Math.Max(MinWidth, workArea.Width - 24));
        Height = Math.Min(Height, Math.Max(MinHeight, workArea.Height - 24));
        Left = workArea.Left + (workArea.Width - Width) / 2;
        Top = workArea.Top + (workArea.Height - Height) / 2;

        _searchTimer.Tick += (_, _) =>
        {
            _searchTimer.Stop();
            _ = RunSearchAsync();
        };
        _fitTimer.Tick += (_, _) =>
        {
            _fitTimer.Stop();
            if (_fitMode) FitWidth();
        };
        lstPages.SizeChanged += (_, _) =>
        {
            if (!_fitMode) return;
            _fitTimer.Stop();
            _fitTimer.Start();
        };

        BuildPaletteCommands();
        ShowSidebarPanel("Pages");
        UpdateToolButtons();
        UpdateUi();
    }

    private bool IsBusy => busyOverlay.Visibility == Visibility.Visible;
    private PageModel? SelectedPage => lstPages.SelectedItem as PageModel ?? lstThumbs.SelectedItem as PageModel;

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        RootLayout.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateUi();
        var file = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(IsPdfFile);
        if (file != null) _ = OpenPathAsync(file);
        else SetStatus("Abre un PDF para empezar.");
    }

    private async void Window_Closing(object sender, CancelEventArgs e)
    {
        if (_closingAfterSave) return;

        var session = _session;
        if (session?.IsDirty != true)
        {
            session?.Dispose();
            return;
        }

        var result = MessageBox.Show(this,
            "Hay cambios sin guardar.\n\n¿Quieres guardarlos antes de cerrar?",
            "Mpdf", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        if (result == MessageBoxResult.Cancel)
        {
            e.Cancel = true;
            return;
        }

        if (result == MessageBoxResult.No)
        {
            session.Dispose();
            return;
        }

        e.Cancel = true;
        if (await SaveCurrentAsync())
        {
            _closingAfterSave = true;
            Close();
        }
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (IsBusy || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;

        var files = ((string[])e.Data.GetData(DataFormats.FileDrop))
            .Where(IsPdfFile)
            .ToArray();
        if (files.Length == 0)
        {
            SetStatus("Suelta un archivo PDF.");
            return;
        }

        if (_session?.HasDocument != true)
        {
            await OpenPathAsync(files[0]);
            if (files.Length > 1 && _session?.HasDocument == true)
                await AppendFilesAsync(files.Skip(1));
        }
        else
        {
            await AppendFilesAsync(files);
        }
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        var shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.O:
                    _ = PickAndOpenAsync();
                    e.Handled = true;
                    return;
                case Key.S:
                    _ = shift ? SaveAsAsync() : SaveCurrentAsync();
                    e.Handled = true;
                    return;
                case Key.Z:
                    if (_session?.CanUndo == true)
                    {
                        _session.Undo();
                        UpdateUi();
                    }
                    e.Handled = true;
                    return;
                case Key.Y:
                    if (_session?.CanRedo == true)
                    {
                        _session.Redo();
                        UpdateUi();
                    }
                    e.Handled = true;
                    return;
                case Key.K:
                    OpenPalette();
                    e.Handled = true;
                    return;
                case Key.F:
                    txtSearch.Focus();
                    txtSearch.SelectAll();
                    e.Handled = true;
                    return;
                case Key.D0:
                case Key.NumPad0:
                    FitWidth();
                    e.Handled = true;
                    return;
                case Key.OemPlus:
                case Key.Add:
                    StepZoom(1.15);
                    e.Handled = true;
                    return;
                case Key.OemMinus:
                case Key.Subtract:
                    StepZoom(1 / 1.15);
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.F11)
        {
            ToggleFullscreen();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F3)
        {
            if (shift) PreviousSearch();
            else NextSearch();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape)
        {
            if (paletteOverlay.Visibility == Visibility.Visible)
            {
                ClosePalette();
            }
            else if (_fullscreen)
            {
                ToggleFullscreen();
            }
            else if (txtSearch.IsKeyboardFocusWithin || txtSearch.Text.Length > 0)
            {
                txtSearch.Clear();
                lstPages.Focus();
            }
            else
            {
                SetTool(EditorTool.Select);
            }
            e.Handled = true;
        }
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximize();
            return;
        }

        if (e.ButtonState != MouseButtonState.Pressed) return;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnMaximize_Click(object sender, RoutedEventArgs e) => ToggleMaximize();

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void ToggleMaximize()
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private async void BtnOpen_Click(object sender, RoutedEventArgs e) => await PickAndOpenAsync();

    private async void BtnOpenEmpty_Click(object sender, RoutedEventArgs e) => await PickAndOpenAsync();

    private async Task PickAndOpenAsync()
    {
        if (IsBusy) return;
        var dialog = new OpenFileDialog
        {
            Title = "Abrir PDF",
            Filter = PdfFilter,
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true) await OpenPathAsync(dialog.FileName);
    }

    private async Task OpenPathAsync(string path)
    {
        if (IsBusy) return;
        if (!File.Exists(path))
        {
            SetStatus("No se encontró el archivo.");
            return;
        }

        if (!await ConfirmDiscardAsync()) return;

        SetBusy(true, "Abriendo PDF…");
        string? password = null;
        try
        {
            for (var attempt = 0; attempt < 4; attempt++)
            {
                try
                {
                    var session = await DocumentSession.OpenAsync(path, password);
                    SetSession(session);
                    SetStatus($"Documento abierto · {session.Pages.Count} página{(session.Pages.Count == 1 ? "" : "s")}.");
                    ScheduleFitWidth();
                    return;
                }
                catch (PdfException ex) when (ex.Message.Contains("contraseña", StringComparison.OrdinalIgnoreCase))
                {
                    SetBusy(false);
                    var prompt = PasswordDialog.Show(this, password == null
                        ? "Este PDF está protegido. Introduce la contraseña para abrirlo."
                        : "La contraseña no es correcta. Inténtalo de nuevo.");
                    if (prompt == null)
                    {
                        SetStatus("Apertura cancelada.");
                        return;
                    }
                    password = prompt;
                    SetBusy(true, "Abriendo PDF…");
                }
            }
        }
        catch (Exception ex)
        {
            ShowError("No se pudo abrir el PDF", ex);
        }
        finally
        {
            SetBusy(false);
            UpdateUi();
        }
    }

    private async Task<bool> ConfirmDiscardAsync()
    {
        if (_session?.IsDirty != true) return true;

        var result = MessageBox.Show(this,
            "Hay cambios sin guardar en el documento actual.\n\n¿Quieres guardarlos antes de continuar?",
            "Mpdf", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Cancel => false,
            MessageBoxResult.Yes => await SaveCurrentAsync(),
            _ => true
        };
    }

    private void SetSession(DocumentSession session)
    {
        var previous = _session;
        if (previous != null)
        {
            previous.StateChanged -= OnSessionStateChanged;
            previous.SearchChanged -= OnSessionSearchChanged;
            previous.PagesReloaded -= OnPagesReloaded;
        }

        _session = session;
        session.StateChanged += OnSessionStateChanged;
        session.SearchChanged += OnSessionSearchChanged;
        session.PagesReloaded += OnPagesReloaded;

        _searchVersion++;
        _lastSearch = null;
        _searchTimer.Stop();
        txtSearch.Text = "";
        txtSearchCount.Text = "";
        searchHint.Visibility = Visibility.Visible;

        HookSelectedPage(null);
        _syncingSelection = true;
        try
        {
            lstPages.ItemsSource = session.Pages;
            lstThumbs.ItemsSource = session.Pages;
            var first = session.Pages.Count > 0 ? 0 : -1;
            lstPages.SelectedIndex = first;
            lstThumbs.SelectedIndex = first;
        }
        finally
        {
            _syncingSelection = false;
        }
        HookSelectedPage(SelectedPage);

        emptyState.Visibility = Visibility.Collapsed;
        _fitMode = true;

        UpdateToolButtons();
        UpdateUi();

        previous?.Dispose();
    }

    private void OnSessionStateChanged(object? sender, EventArgs e) => UpdateUi();

    private void OnSessionSearchChanged(object? sender, EventArgs e) => UpdateSearchCount();

    private void OnPagesReloaded(object? sender, EventArgs e) => UpdateUi();

    private void UpdateUi()
    {
        var session = _session;
        var hasDoc = session?.HasDocument == true;
        var page = SelectedPage;

        btnSave.IsEnabled = hasDoc;
        btnSaveAs.IsEnabled = hasDoc;
        emptyState.Visibility = hasDoc ? Visibility.Collapsed : Visibility.Visible;

        txtFileName.Text = hasDoc ? session!.DisplayName : "Sin documento";
        txtPageCount.Text = hasDoc ? session!.Pages.Count.ToString() : "0";
        txtDirty.Text = session?.IsDirty == true ? "● Cambios sin guardar" : "";
        btnUndo.IsEnabled = session?.CanUndo == true;
        btnRedo.IsEnabled = session?.CanRedo == true;
        btnClearMarks.IsEnabled = page != null && page.Annotations.Count > 0;

        miInsertBlank.IsEnabled = hasDoc;
        miInsertPdf.IsEnabled = hasDoc;
        miExtractRange.IsEnabled = hasDoc;
        miSplit.IsEnabled = hasDoc;
        miProperties.IsEnabled = hasDoc;
        miCopyText.IsEnabled = hasDoc;
        miExportText.IsEnabled = hasDoc;
        miExportImages.IsEnabled = hasDoc;
        miPageNumbers.IsEnabled = hasDoc;
        miWatermark.IsEnabled = hasDoc;
        miRotateAll.IsEnabled = hasDoc;

        txtStatusPage.Text = !hasDoc
            ? "Sin documento"
            : page != null
                ? $"Página {page.DisplayLabel} de {session!.Pages.Count}"
                : $"{session!.Pages.Count} páginas";

        Title = hasDoc
            ? $"{session!.DisplayName}{(session.IsDirty ? " *" : "")} — Mpdf"
            : "Mpdf — Editor PDF";

        UpdateZoomUi();
        UpdateSearchCount();
        UpdateSidePanels();
    }

    private void UpdateZoomUi()
    {
        var hasDoc = _session?.HasDocument == true;
        var percent = (_session?.Zoom ?? 1.0) * 100;
        txtZoom.Text = hasDoc ? $"{percent:0} %" : "100 %";
        txtZoomStatus.Text = hasDoc ? $"{percent:0} %" : "";
    }

    private void UpdateSearchCount()
    {
        var session = _session;
        var query = txtSearch.Text.Trim();
        if (session == null || query.Length == 0)
        {
            txtSearchCount.Text = "";
            return;
        }

        if (!string.Equals(_lastSearch, query, StringComparison.Ordinal))
        {
            txtSearchCount.Text = "";
            return;
        }

        var hits = session.SearchHits.Count;
        if (hits == 0)
        {
            txtSearchCount.Text = "Sin resultados";
            return;
        }

        var index = Math.Clamp(session.SearchIndex + 1, 1, hits);
        txtSearchCount.Text = $"{index}/{hits}";
    }

    private void SetTool(EditorTool tool)
    {
        EditorState.CurrentTool = tool;
        UpdateToolButtons();
        SetStatus(tool switch
        {
            EditorTool.Select => "Seleccionar.",
            EditorTool.Highlight => "Resaltar: arrastra sobre el texto.",
            EditorTool.Underline => "Subrayar: arrastra sobre el área.",
            EditorTool.Strikeout => "Tachar: arrastra sobre el área.",
            EditorTool.Pen => "Lápiz: dibuja con el ratón.",
            EditorTool.Rectangle => "Forma: arrastra para dibujar un rectángulo.",
            EditorTool.Eraser => "Borrador: pulsa sobre una marca para quitarla.",
            _ => ""
        });
    }

    private void UpdateToolButtons()
    {
        tglSelect.IsChecked = EditorState.CurrentTool == EditorTool.Select;
        tglHighlight.IsChecked = EditorState.CurrentTool == EditorTool.Highlight;
        tglUnderline.IsChecked = EditorState.CurrentTool == EditorTool.Underline;
        tglStrikeout.IsChecked = EditorState.CurrentTool == EditorTool.Strikeout;
        tglPen.IsChecked = EditorState.CurrentTool == EditorTool.Pen;
        tglRectangle.IsChecked = EditorState.CurrentTool == EditorTool.Rectangle;
        tglEraser.IsChecked = EditorState.CurrentTool == EditorTool.Eraser;
    }

    private void Tool_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag } && Enum.TryParse<EditorTool>(tag, out var tool))
            SetTool(tool);
        else
            UpdateToolButtons();
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string hex }) return;
        try
        {
            if (ColorConverter.ConvertFromString(hex) is not Color color) return;
            EditorState.PenColor = color;
            EditorState.HighlightColor = color;
            SetStatus("Color de dibujo actualizado.");
        }
        catch (FormatException)
        {
        }
    }

    private void BtnUndo_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.CanUndo != true) return;
        _session.Undo();
        UpdateUi();
        SetStatus("Última marca deshecha.");
    }

    private void BtnRedo_Click(object sender, RoutedEventArgs e)
    {
        if (_session?.CanRedo != true) return;
        _session.Redo();
        UpdateUi();
        SetStatus("Última marca rehecha.");
    }

    private void BtnClearMarks_Click(object sender, RoutedEventArgs e)
    {
        var page = SelectedPage;
        if (_session == null || page == null || page.Annotations.Count == 0) return;
        _session.ClearAnnotations(page);
        UpdateUi();
        SetStatus($"Marcas de la página {page.Number} eliminadas.");
    }

    private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        searchHint.Visibility = txtSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (txtSearch.Text.Length == 0)
        {
            _searchTimer.Stop();
            _searchVersion++;
            _lastSearch = null;
            _session?.ClearSearch();
            txtSearchCount.Text = "";
            return;
        }

        _searchTimer.Stop();
        _searchTimer.Start();
    }

    private async void TxtSearch_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _searchTimer.Stop();
            await RunSearchAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            txtSearch.Clear();
            e.Handled = true;
        }
    }

    private async Task RunSearchAsync()
    {
        var session = _session;
        if (session?.HasDocument != true) return;

        var query = txtSearch.Text.Trim();
        if (query.Length == 0)
        {
            session.ClearSearch();
            txtSearchCount.Text = "";
            return;
        }

        var version = ++_searchVersion;
        _lastSearch = query;
        try
        {
            var hits = await session.SearchAsync(query);
            if (version != _searchVersion || !ReferenceEquals(session, _session)) return;

            txtSearchCount.Text = hits.Count == 0 ? "Sin resultados" : "";
            if (hits.Count > 0)
            {
                NavigateToSearchHit();
                SetStatus($"{hits.Count} coincidencia{(hits.Count == 1 ? "" : "s")} · {query}");
            }
            else
            {
                SetStatus($"Sin coincidencias para «{query}».");
            }
        }
        catch (Exception ex)
        {
            if (version == _searchVersion) ShowError("No se pudo buscar en el documento", ex);
        }
    }

    private void BtnSearchNext_Click(object sender, RoutedEventArgs e) => NextSearch();

    private void BtnSearchPrev_Click(object sender, RoutedEventArgs e) => PreviousSearch();

    private void NextSearch()
    {
        if (_session?.SearchHits.Count > 0)
        {
            _session.NextSearchHit();
            NavigateToSearchHit();
        }
    }

    private void PreviousSearch()
    {
        if (_session?.SearchHits.Count > 0)
        {
            _session.PreviousSearchHit();
            NavigateToSearchHit();
        }
    }

    private void NavigateToSearchHit()
    {
        var session = _session;
        var hit = session?.CurrentSearchHit;
        if (session == null || hit == null)
        {
            UpdateSearchCount();
            return;
        }

        var page = session.Pages.FirstOrDefault(p => p.Id == hit.PageId);
        if (page == null) return;

        SelectPage(page);
        lstPages.ScrollIntoView(page);
        lstThumbs.ScrollIntoView(page);
        UpdateSearchCount();
    }

    private void StepZoom(double factor)
    {
        var session = _session;
        if (session?.HasDocument != true) return;

        _fitMode = false;
        var zoom = Math.Round(session.Zoom * factor * 100) / 100;
        session.SetZoom(zoom);
        UpdateZoomUi();
        UpdateUi();
    }

    private void FitWidth()
    {
        var session = _session;
        if (session?.HasDocument != true || session.Pages.Count == 0) return;

        _fitMode = true;
        var available = lstPages.ActualWidth;
        if (available <= 0) return;

        var maxBase = session.Pages.Max(p => p.BaseWidth);
        if (maxBase <= 0) return;

        var usable = Math.Max(200, available - 96);
        var zoom = Math.Clamp(usable / maxBase, 0.15, 3.0);
        session.SetZoom(Math.Round(zoom * 100) / 100);
        UpdateZoomUi();
    }

    private void ScheduleFitWidth(double scrollRatio = 0)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            FitWidth();
            if (scrollRatio <= 0) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var scroller = FindScrollViewer(lstPages);
                if (scroller is { ScrollableHeight: > 0 })
                    scroller.ScrollToVerticalOffset(scrollRatio * scroller.ScrollableHeight);
            }), DispatcherPriority.Loaded);
        }), DispatcherPriority.Loaded);
    }

    private void BtnZoomIn_Click(object sender, RoutedEventArgs e) => StepZoom(1.15);

    private void BtnZoomOut_Click(object sender, RoutedEventArgs e) => StepZoom(1 / 1.15);

    private void BtnFit_Click(object sender, RoutedEventArgs e) => FitWidth();

    private void BtnRotateLeft_Click(object sender, RoutedEventArgs e) => RotateSelected(-1);

    private void BtnRotateRight_Click(object sender, RoutedEventArgs e) => RotateSelected(1);

    private void RotateSelected(int delta)
    {
        var session = _session;
        var page = SelectedPage;
        if (session == null || page == null || IsBusy) return;
        if (!session.RotatePage(page, delta)) return;

        SetStatus(delta > 0 ? "Página girada a la derecha." : "Página girada a la izquierda.");
        if (_fitMode) ScheduleFitWidth();
        UpdateUi();
    }

    private void BtnMoveUp_Click(object sender, RoutedEventArgs e) => MoveSelected(-1);

    private void BtnMoveDown_Click(object sender, RoutedEventArgs e) => MoveSelected(1);

    private void MoveSelected(int direction)
    {
        var session = _session;
        var page = SelectedPage;
        if (session == null || page == null || IsBusy) return;
        if (!session.MovePage(page, direction)) return;

        SelectPage(page);
        SetStatus(direction < 0 ? "Página movida hacia arriba." : "Página movida hacia abajo.");
        UpdateUi();
    }

    private void BtnDeletePage_Click(object sender, RoutedEventArgs e)
    {
        var session = _session;
        var page = SelectedPage;
        if (session == null || page == null || IsBusy) return;

        if (session.Pages.Count <= 1)
        {
            SetStatus("No se puede eliminar la única página del documento.");
            return;
        }

        var result = MessageBox.Show(this, $"¿Eliminar la página {page.Number}?", "Mpdf",
            MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes) return;

        var index = page.Index;
        if (!session.DeletePage(page)) return;

        SelectPage(session.Pages[Math.Clamp(index, 0, session.Pages.Count - 1)]);
        SetStatus("Página eliminada.");
        UpdateUi();
    }

    private async void BtnExtractPage_Click(object sender, RoutedEventArgs e)
    {
        var session = _session;
        var page = SelectedPage;
        if (session == null || page == null || IsBusy) return;

        var dialog = new SaveFileDialog
        {
            Title = "Extraer página",
            Filter = PdfFilter,
            DefaultExt = ".pdf",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"pagina-{page.Number}.pdf"
        };
        if (dialog.ShowDialog(this) != true) return;

        SetBusy(true, "Extrayendo página…");
        try
        {
            await session.ExportPageAsync(page, dialog.FileName);
            SetStatus("Página extraída: " + Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex)
        {
            ShowError("No se pudo extraer la página", ex);
        }
        finally
        {
            SetBusy(false);
            UpdateUi();
        }
    }

    private async void BtnMerge_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy) return;

        var dialog = new OpenFileDialog
        {
            Title = "Combinar PDFs",
            Filter = PdfFilter,
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true || dialog.FileNames.Length == 0) return;

        var files = dialog.FileNames;
        if (_session?.HasDocument == true)
        {
            await AppendFilesAsync(files);
            return;
        }

        await OpenPathAsync(files[0]);
        if (files.Length > 1 && _session?.HasDocument == true)
            await AppendFilesAsync(files.Skip(1));
    }

    private async Task AppendFilesAsync(IEnumerable<string> files)
    {
        var session = _session;
        if (session == null) return;

        SetBusy(true, "Combinando PDFs…");
        var added = new List<PageModel>();
        var failed = new List<string>();
        try
        {
            foreach (var file in files)
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(file);
                    added.AddRange(await session.AppendDocumentAsync(bytes));
                }
                catch (Exception ex)
                {
                    failed.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
        finally
        {
            SetBusy(false);
        }

        if (added.Count > 0)
        {
            SelectPage(added[0]);
            SetStatus($"Combinado · {added.Count} página{(added.Count == 1 ? "" : "s")} añadida{(added.Count == 1 ? "" : "s")}.");
        }

        if (failed.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n", failed),
                "Algunos archivos no se pudieron combinar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        UpdateUi();
    }

    private void BtnTools_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: not null } button) return;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = PlacementMode.Bottom;
        button.ContextMenu.IsOpen = true;
    }

    private void MenuInsertBlank_Click(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session?.HasDocument != true) return;

        var page = SelectedPage;
        var index = page?.Index + 1 ?? session.Pages.Count;
        var width = page?.UnrotatedWidth ?? 595;
        var height = page?.UnrotatedHeight ?? 842;
        try
        {
            var blank = session.InsertBlankPage(index, width, height);
            SelectPage(blank);
            SetStatus($"Página en blanco insertada después de la {page?.DisplayLabel ?? "última"}.");
            UpdateUi();
        }
        catch (Exception ex)
        {
            ShowError("No se pudo insertar la página", ex);
        }
    }

    private async void MenuInsertPdf_Click(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session?.HasDocument != true || IsBusy) return;

        var dialog = new OpenFileDialog
        {
            Title = "Insertar PDF",
            Filter = PdfFilter,
            CheckFileExists = true,
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;

        var insertAt = (SelectedPage?.Index ?? session.Pages.Count - 1) + 1;
        PageModel? first = null;
        var failed = new List<string>();

        SetBusy(true, "Insertando páginas…");
        try
        {
            foreach (var file in dialog.FileNames)
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(file);
                    var added = await session.InsertDocumentAsync(bytes, insertAt);
                    insertAt += added.Count;
                    first ??= added.FirstOrDefault();
                }
                catch (Exception ex)
                {
                    failed.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }
        finally
        {
            SetBusy(false);
        }

        if (first != null)
        {
            SelectPage(first);
            SetStatus("Páginas insertadas en la posición elegida.");
            UpdateUi();
        }

        if (failed.Count > 0)
        {
            MessageBox.Show(this, string.Join("\n", failed),
                "Algunos archivos no se pudieron insertar", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void MenuExtractRange_Click(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session?.HasDocument != true || IsBusy) return;

        var range = InputDialog.Show(this, "Extraer rango de páginas",
            $"El documento tiene {session.Pages.Count} páginas. Puedes usar rangos como 1-3,5.",
            "Páginas que quieres extraer", $"1-{session.Pages.Count}");
        if (range == null) return;

        var dialog = new SaveFileDialog
        {
            Title = "Guardar extracto",
            Filter = PdfFilter,
            DefaultExt = ".pdf",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = BaseName(session.FilePath) + "-extracto.pdf"
        };
        if (dialog.ShowDialog(this) != true) return;

        SetBusy(true, "Extrayendo páginas…");
        try
        {
            await session.ExportRangeAsync(range, dialog.FileName);
            SetStatus("Extracto guardado: " + Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex)
        {
            ShowError("No se pudo extraer el rango", ex);
        }
        finally
        {
            SetBusy(false);
            UpdateUi();
        }
    }

    private async void MenuSplit_Click(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session?.HasDocument != true || IsBusy) return;

        var directory = string.IsNullOrWhiteSpace(session.FilePath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : Path.GetDirectoryName(session.FilePath) ?? Environment.CurrentDirectory;

        var options = SplitDialog.Show(this, session.Pages.Count, BaseName(session.FilePath), directory);
        if (options == null) return;

        SetBusy(true, "Dividiendo PDF…");
        try
        {
            var paths = await session.SplitAsync(options.Directory, options.BaseName, options.PagesPerFile);
            SetStatus($"PDF dividido en {paths.Count} archivo{(paths.Count == 1 ? "" : "s")}.");
            MessageBox.Show(this,
                $"Se han creado {paths.Count} archivos en:\n{options.Directory}",
                "PDF dividido", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError("No se pudo dividir el PDF", ex);
        }
        finally
        {
            SetBusy(false);
            UpdateUi();
        }
    }

    private void MenuMerge_Click(object sender, RoutedEventArgs e) => BtnMerge_Click(sender, e);

    private async void MenuExportImages_Click(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session?.HasDocument != true || IsBusy) return;

        var directory = string.IsNullOrWhiteSpace(session.FilePath)
            ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            : Path.GetDirectoryName(session.FilePath) ?? Environment.CurrentDirectory;

        var options = ExportImagesDialog.Show(this, session.Pages.Count,
            BaseName(session.FilePath), directory);
        if (options == null) return;

        SetBusy(true, "Exportando páginas como imágenes…");
        try
        {
            var paths = await session.ExportImagesAsync(options);
            SetStatus($"{paths.Count} imagen{(paths.Count == 1 ? "" : "es")} exportada{(paths.Count == 1 ? "" : "s")}.");
            MessageBox.Show(this,
                $"Se {(paths.Count == 1 ? "ha creado 1 imagen" : $"han creado {paths.Count} imágenes")} en:\n{options.Directory}",
                "Imágenes exportadas", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            ShowError("No se pudieron exportar las imágenes", ex);
        }
        finally
        {
            SetBusy(false);
            UpdateUi();
        }
    }

    private async void MenuPageNumbers_Click(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session?.HasDocument != true || IsBusy) return;

        var options = StampDialog.ShowPageNumbers(this, session.Pages.Count);
        if (options == null) return;

        SetBusy(true, "Añadiendo numeración…");
        try
        {
            await session.AddPageNumbersAsync(options);
            SetStatus("Numeración añadida a todas las páginas.");
        }
        catch (Exception ex)
        {
            ShowError("No se pudo añadir la numeración", ex);
        }
        finally
        {
            SetBusy(false);
            UpdateUi();
        }
    }

    private async void MenuWatermark_Click(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session?.HasDocument != true || IsBusy) return;

        var options = StampDialog.ShowWatermark(this, session.Pages.Count);
        if (options == null) return;

        SetBusy(true, "Añadiendo la marca de agua…");
        try
        {
            await session.AddWatermarkAsync(options);
            SetStatus("Marca de agua añadida a todas las páginas.");
        }
        catch (Exception ex)
        {
            ShowError("No se pudo añadir la marca de agua", ex);
        }
        finally
        {
            SetBusy(false);
            UpdateUi();
        }
    }

    private void MenuRotateAllRight_Click(object sender, RoutedEventArgs e) => RotateAll(1);

    private void MenuRotateAllLeft_Click(object sender, RoutedEventArgs e) => RotateAll(-1);

    private void RotateAll(int delta)
    {
        var session = _session;
        if (session?.HasDocument != true || IsBusy) return;

        session.RotateAllPages(delta);
        SetStatus(delta > 0
            ? "Todas las páginas giradas 90° a la derecha."
            : "Todas las páginas giradas 90° a la izquierda.");
        UpdateUi();
    }

    private void MenuProperties_Click(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session?.HasDocument != true) return;
        PropertiesDialog.Show(this, session, session.FilePath);
    }

    private async void MenuCopyText_Click(object sender, RoutedEventArgs e)
    {
        var session = _session;
        var page = SelectedPage;
        if (session?.HasDocument != true || page == null || IsBusy) return;

        SetBusy(true, "Extrayendo texto…");
        try
        {
            var text = await session.GetPageTextAsync(page);
            if (string.IsNullOrWhiteSpace(text))
            {
                SetStatus("Esta página no contiene texto seleccionable.");
                return;
            }

            Clipboard.SetText(text);
            SetStatus($"Texto de la página {page.DisplayLabel} copiado ({text.Length} caracteres).");
        }
        catch (Exception ex)
        {
            ShowError("No se pudo copiar el texto", ex);
        }
        finally
        {
            SetBusy(false);
            UpdateUi();
        }
    }

    private async void MenuExportText_Click(object sender, RoutedEventArgs e)
    {
        var session = _session;
        if (session?.HasDocument != true || IsBusy) return;

        var dialog = new SaveFileDialog
        {
            Title = "Exportar texto",
            Filter = "Texto (*.txt)|*.txt|Todos los archivos (*.*)|*.*",
            DefaultExt = ".txt",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = BaseName(session.FilePath) + ".txt"
        };
        if (dialog.ShowDialog(this) != true) return;

        SetBusy(true, "Extrayendo texto…");
        try
        {
            var text = await session.GetDocumentTextAsync();
            await File.WriteAllTextAsync(dialog.FileName, text);
            SetStatus("Texto exportado: " + Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex)
        {
            ShowError("No se pudo exportar el texto", ex);
        }
        finally
        {
            SetBusy(false);
            UpdateUi();
        }
    }

    private void MenuPalette_Click(object sender, RoutedEventArgs e) => OpenPalette();

    private void MenuFullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullscreen();

    private async void BtnSave_Click(object sender, RoutedEventArgs e) => await SaveCurrentAsync();

    private async void BtnSaveAs_Click(object sender, RoutedEventArgs e) => await SaveAsAsync();

    private async Task<bool> SaveCurrentAsync()
    {
        var session = _session;
        if (session?.HasDocument != true) return false;
        if (string.IsNullOrEmpty(session.FilePath)) return await SaveAsAsync();
        return await SaveToAsync(session, session.FilePath);
    }

    private async Task<bool> SaveAsAsync()
    {
        var session = _session;
        if (session?.HasDocument != true) return false;

        var dialog = new SaveFileDialog
        {
            Title = "Guardar PDF",
            Filter = PdfFilter,
            DefaultExt = ".pdf",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = string.IsNullOrEmpty(session.FilePath)
                ? "documento.pdf"
                : Path.GetFileName(session.FilePath)
        };
        if (dialog.ShowDialog(this) != true) return false;
        return await SaveToAsync(session, dialog.FileName);
    }

    private async Task<bool> SaveToAsync(DocumentSession session, string path)
    {
        SetBusy(true, "Guardando PDF…");
        try
        {
            await session.SaveAsync(path);
            SetStatus("Guardado: " + Path.GetFileName(path));
            UpdateUi();
            return true;
        }
        catch (Exception ex)
        {
            ShowError("No se pudo guardar el PDF", ex);
            return false;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void LstPages_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        _syncingSelection = true;
        try
        {
            lstThumbs.SelectedItem = lstPages.SelectedItem;
        }
        finally
        {
            _syncingSelection = false;
        }
        if (SelectedPage != null) lstThumbs.ScrollIntoView(SelectedPage);
        HookSelectedPage(SelectedPage);
        UpdateUi();
    }

    private void LstThumbs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection) return;
        _syncingSelection = true;
        try
        {
            lstPages.SelectedItem = lstThumbs.SelectedItem;
        }
        finally
        {
            _syncingSelection = false;
        }
        if (SelectedPage != null) lstPages.ScrollIntoView(SelectedPage);
        HookSelectedPage(SelectedPage);
        UpdateUi();
    }

    private void SelectPage(PageModel? page)
    {
        _syncingSelection = true;
        try
        {
            lstPages.SelectedItem = page;
            lstThumbs.SelectedItem = page;
        }
        finally
        {
            _syncingSelection = false;
        }
        HookSelectedPage(page);
        if (page != null)
        {
            lstPages.ScrollIntoView(page);
            lstThumbs.ScrollIntoView(page);
        }
        UpdateUi();
    }

    private void HookSelectedPage(PageModel? page)
    {
        if (_hookedPage != null)
            _hookedPage.Annotations.CollectionChanged -= OnSelectedPageAnnotationsChanged;

        _hookedPage = page;
        if (_hookedPage != null)
            _hookedPage.Annotations.CollectionChanged += OnSelectedPageAnnotationsChanged;
    }

    private void OnSelectedPageAnnotationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        btnClearMarks.IsEnabled = _hookedPage?.Annotations.Count > 0;
    }

    private void SidebarTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string tag }) ShowSidebarPanel(tag);
    }

    private void SidebarTab_Checked(object sender, RoutedEventArgs e)
    {
        if (_syncingSidebarTabs || pagesPanel == null) return;
        if (sender is ToggleButton { Tag: string tag }) ShowSidebarPanel(tag);
    }

    private void ShowSidebarPanel(string tag)
    {
        pagesPanel.Visibility = tag == "Pages" ? Visibility.Visible : Visibility.Collapsed;
        bookmarksPanel.Visibility = tag == "Bookmarks" ? Visibility.Visible : Visibility.Collapsed;
        commentsPanel.Visibility = tag == "Comments" ? Visibility.Visible : Visibility.Collapsed;
        attachmentsPanel.Visibility = tag == "Attachments" ? Visibility.Visible : Visibility.Collapsed;

        _syncingSidebarTabs = true;
        try
        {
            tabPages.IsChecked = tag == "Pages";
            tabBookmarks.IsChecked = tag == "Bookmarks";
            tabComments.IsChecked = tag == "Comments";
            tabAttachments.IsChecked = tag == "Attachments";
        }
        finally
        {
            _syncingSidebarTabs = false;
        }
    }

    private void UpdateSidePanels()
    {
        var session = _session;
        var hasDoc = session?.HasDocument == true;

        var bookmarks = hasDoc ? session!.Bookmarks : Array.Empty<BookmarkNode>();
        treeBookmarks.ItemsSource = bookmarks;
        txtBookmarkCount.Text = bookmarks.Count.ToString();
        bookmarksEmpty.Visibility = bookmarks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        var attachments = hasDoc ? session!.Attachments : Array.Empty<AttachmentInfo>();
        lstAttachments.ItemsSource = attachments;
        txtAttachmentCount.Text = attachments.Count.ToString();
        attachmentsEmpty.Visibility = attachments.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        RefreshComments();
    }

    private void RefreshComments()
    {
        var session = _session;
        var entries = new List<CommentEntry>();
        if (session?.HasDocument == true)
        {
            foreach (var page in session.Pages)
            {
                foreach (var annotation in page.Annotations)
                    entries.Add(new CommentEntry(page, annotation));
            }
        }

        _syncingComments = true;
        try
        {
            lstComments.ItemsSource = entries;
        }
        finally
        {
            _syncingComments = false;
        }

        txtCommentCount.Text = entries.Count.ToString();
        commentsEmpty.Visibility = entries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void LstComments_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingComments) return;
        if (lstComments.SelectedItem is CommentEntry entry) SelectPage(entry.Page);
    }

    private void CommentDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CommentEntry entry }) return;
        _session?.RemoveAnnotation(entry.Page, entry.Annotation);
        SetStatus("Marca eliminada.");
        UpdateUi();
    }

    private void TreeBookmarks_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not BookmarkNode { HasPage: true } bookmark) return;
        var session = _session;
        if (session == null || bookmark.PageIndex >= session.Pages.Count) return;
        SelectPage(session.Pages[bookmark.PageIndex]);
        SetStatus($"Marcador: {bookmark.Title}");
    }

    private async void AttachmentSave_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AttachmentInfo attachment }) return;
        var session = _session;
        if (session == null || IsBusy) return;

        var dialog = new SaveFileDialog
        {
            Title = "Guardar adjunto",
            FileName = attachment.Name,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog(this) != true) return;

        SetBusy(true, "Guardando adjunto…");
        try
        {
            await session.SaveAttachmentAsync(attachment, dialog.FileName);
            SetStatus("Adjunto guardado: " + Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex)
        {
            ShowError("No se pudo guardar el adjunto", ex);
        }
        finally
        {
            SetBusy(false);
            UpdateUi();
        }
    }

    private async void AttachmentOpen_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: AttachmentInfo attachment }) return;
        var session = _session;
        if (session == null || IsBusy) return;

        var name = Path.GetFileName(attachment.Name);
        if (string.IsNullOrWhiteSpace(name)) name = "adjunto";

        SetBusy(true, "Abriendo adjunto…");
        try
        {
            var directory = Path.Combine(Path.GetTempPath(), "Mpdf", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, name);
            await session.SaveAttachmentAsync(attachment, path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            SetStatus("Adjunto abierto: " + name);
        }
        catch (Exception ex)
        {
            ShowError("No se pudo abrir el adjunto", ex);
        }
        finally
        {
            SetBusy(false);
            UpdateUi();
        }
    }

    private bool HasDocument => _session?.HasDocument == true;

    private void BuildPaletteCommands()
    {
        _paletteCommands = new List<PaletteCommand>
        {
            new("Abrir PDF…", "Ctrl+O", () => true, () => _ = PickAndOpenAsync()),
            new("Guardar", "Ctrl+S", () => HasDocument, () => _ = SaveCurrentAsync()),
            new("Guardar como…", "Ctrl+Shift+S", () => HasDocument, () => _ = SaveAsAsync()),
            new("Insertar página en blanco", "Herramientas", () => HasDocument,
                () => MenuInsertBlank_Click(this, new RoutedEventArgs())),
            new("Insertar PDF…", "Herramientas", () => HasDocument,
                () => MenuInsertPdf_Click(this, new RoutedEventArgs())),
            new("Combinar PDFs…", "Herramientas", () => true,
                () => BtnMerge_Click(this, new RoutedEventArgs())),
            new("Extraer rango de páginas…", "Herramientas", () => HasDocument,
                () => MenuExtractRange_Click(this, new RoutedEventArgs())),
            new("Dividir PDF…", "Herramientas", () => HasDocument,
                () => MenuSplit_Click(this, new RoutedEventArgs())),
            new("Exportar páginas como imágenes…", "Herramientas", () => HasDocument,
                () => MenuExportImages_Click(this, new RoutedEventArgs())),
            new("Añadir numeración…", "Herramientas", () => HasDocument,
                () => MenuPageNumbers_Click(this, new RoutedEventArgs())),
            new("Marca de agua…", "Herramientas", () => HasDocument,
                () => MenuWatermark_Click(this, new RoutedEventArgs())),
            new("Girar todas las páginas a la derecha", "Página", () => HasDocument,
                () => MenuRotateAllRight_Click(this, new RoutedEventArgs())),
            new("Girar todas las páginas a la izquierda", "Página", () => HasDocument,
                () => MenuRotateAllLeft_Click(this, new RoutedEventArgs())),
            new("Propiedades del documento…", "Herramientas", () => HasDocument,
                () => MenuProperties_Click(this, new RoutedEventArgs())),
            new("Copiar texto de esta página", "Texto", () => HasDocument,
                () => MenuCopyText_Click(this, new RoutedEventArgs())),
            new("Exportar texto…", "Texto", () => HasDocument,
                () => MenuExportText_Click(this, new RoutedEventArgs())),
            new("Buscar en el documento", "Ctrl+F", () => HasDocument, FocusSearch),
            new("Ajustar al ancho", "Ctrl+0", () => HasDocument, FitWidth),
            new("Ampliar", "Ctrl++", () => HasDocument, () => StepZoom(1.15)),
            new("Reducir", "Ctrl+-", () => HasDocument, () => StepZoom(1 / 1.15)),
            new("Girar página a la izquierda", "Página", () => HasDocument, () => RotateSelected(-1)),
            new("Girar página a la derecha", "Página", () => HasDocument, () => RotateSelected(1)),
            new("Mover página hacia arriba", "Página", () => HasDocument, () => MoveSelected(-1)),
            new("Mover página hacia abajo", "Página", () => HasDocument, () => MoveSelected(1)),
            new("Eliminar página", "Página", () => _session?.Pages.Count > 1,
                () => BtnDeletePage_Click(this, new RoutedEventArgs())),
            new("Herramienta: seleccionar", "Anotar", () => true, () => SetTool(EditorTool.Select)),
            new("Herramienta: resaltar", "Anotar", () => true, () => SetTool(EditorTool.Highlight)),
            new("Herramienta: subrayar", "Anotar", () => true, () => SetTool(EditorTool.Underline)),
            new("Herramienta: tachar", "Anotar", () => true, () => SetTool(EditorTool.Strikeout)),
            new("Herramienta: lápiz", "Anotar", () => true, () => SetTool(EditorTool.Pen)),
            new("Herramienta: forma", "Anotar", () => true, () => SetTool(EditorTool.Rectangle)),
            new("Herramienta: borrador", "Anotar", () => true, () => SetTool(EditorTool.Eraser)),
            new("Deshacer", "Ctrl+Z", () => _session?.CanUndo == true, () =>
            {
                _session?.Undo();
                UpdateUi();
            }),
            new("Rehacer", "Ctrl+Y", () => _session?.CanRedo == true, () =>
            {
                _session?.Redo();
                UpdateUi();
            }),
            new("Pantalla completa", "F11", () => true, ToggleFullscreen)
        };
    }

    private void FocusSearch()
    {
        txtSearch.Focus();
        txtSearch.SelectAll();
    }

    private void OpenPalette()
    {
        if (IsBusy) return;
        paletteOverlay.Visibility = Visibility.Visible;
        txtPalette.Text = "";
        RefreshPalette("");
        Dispatcher.BeginInvoke(new Action(() =>
        {
            txtPalette.Focus();
            Keyboard.Focus(txtPalette);
        }), DispatcherPriority.Input);
    }

    private void ClosePalette()
    {
        if (paletteOverlay.Visibility != Visibility.Visible) return;
        paletteOverlay.Visibility = Visibility.Collapsed;
        paletteHint.Visibility = Visibility.Visible;
        lstPages.Focus();
    }

    private void RefreshPalette(string query)
    {
        var filtered = _paletteCommands
            .Where(command => command.Enabled())
            .Where(command => query.Length == 0
                || command.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || command.Hint.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        lstPalette.ItemsSource = filtered;
        if (filtered.Count > 0) lstPalette.SelectedIndex = 0;
        paletteHint.Visibility = txtPalette.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TxtPalette_TextChanged(object sender, TextChangedEventArgs e)
        => RefreshPalette(txtPalette.Text.Trim());

    private void TxtPalette_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                ClosePalette();
                e.Handled = true;
                break;
            case Key.Enter:
                ExecutePaletteCommand();
                e.Handled = true;
                break;
            case Key.Down when lstPalette.Items.Count > 0:
                lstPalette.SelectedIndex = Math.Min(lstPalette.SelectedIndex + 1, lstPalette.Items.Count - 1);
                lstPalette.ScrollIntoView(lstPalette.SelectedItem);
                e.Handled = true;
                break;
            case Key.Up when lstPalette.Items.Count > 0:
                lstPalette.SelectedIndex = Math.Max(lstPalette.SelectedIndex - 1, 0);
                lstPalette.ScrollIntoView(lstPalette.SelectedItem);
                e.Handled = true;
                break;
        }
    }

    private void LstPalette_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (lstPalette.SelectedItem is null) return;
        ExecutePaletteCommand();
    }

    private void ExecutePaletteCommand()
    {
        if (lstPalette.SelectedItem is not PaletteCommand command) return;
        ClosePalette();
        command.Run();
    }

    private void PaletteBackdrop_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => ClosePalette();

    private void PaletteCard_MouseLeftButtonDown(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void ToggleFullscreen()
    {
        var scroller = FindScrollViewer(lstPages);
        var scrollRatio = scroller is { ScrollableHeight: > 0 }
            ? scroller.VerticalOffset / scroller.ScrollableHeight
            : 0;

        _fullscreen = !_fullscreen;

        if (_fullscreen)
        {
            _stateBeforeFullscreen = WindowState;
            if (WindowState == WindowState.Normal)
                _boundsBeforeFullscreen = new Rect(Left, Top, ActualWidth, ActualHeight);
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = _stateBeforeFullscreen;
            if (_stateBeforeFullscreen == WindowState.Normal && _boundsBeforeFullscreen.Width > 0)
            {
                Left = _boundsBeforeFullscreen.Left;
                Top = _boundsBeforeFullscreen.Top;
                Width = _boundsBeforeFullscreen.Width;
                Height = _boundsBeforeFullscreen.Height;
            }
        }

        topBar.Visibility = _fullscreen ? Visibility.Collapsed : Visibility.Visible;
        toolBar.Visibility = _fullscreen ? Visibility.Collapsed : Visibility.Visible;
        statusBar.Visibility = _fullscreen ? Visibility.Collapsed : Visibility.Visible;
        sideBar.Visibility = _fullscreen ? Visibility.Collapsed : Visibility.Visible;
        fullscreenHint.Visibility = _fullscreen ? Visibility.Visible : Visibility.Collapsed;
        SidebarColumn.Width = _fullscreen ? new GridLength(0) : new GridLength(192);
        RootLayout.RowDefinitions[0].Height = _fullscreen ? new GridLength(0) : new GridLength(46);
        RootLayout.RowDefinitions[1].Height = _fullscreen ? new GridLength(0) : new GridLength(64);
        RootLayout.RowDefinitions[3].Height = _fullscreen ? new GridLength(0) : new GridLength(30);

        SetStatus(_fullscreen
            ? "Pantalla completa. Pulsa F11 o Esc para salir."
            : "Vista normal.");
        if (_fitMode) ScheduleFitWidth(scrollRatio);
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        if (root is ScrollViewer viewer) return viewer;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (FindScrollViewer(VisualTreeHelper.GetChild(root, i)) is { } found) return found;
        }
        return null;
    }

    private static string BaseName(string? path)
        => string.IsNullOrWhiteSpace(path) ? "documento" : Path.GetFileNameWithoutExtension(path);

    private static string DescribeKind(AnnotationKind kind) => kind switch
    {
        AnnotationKind.Highlight => "Resaltado",
        AnnotationKind.Underline => "Subrayado",
        AnnotationKind.Strikeout => "Tachado",
        AnnotationKind.Rectangle => "Rectángulo",
        AnnotationKind.Ink => "Trazo de lápiz",
        _ => "Marca"
    };

    private sealed class CommentEntry
    {
        public CommentEntry(PageModel page, AnnotationModel annotation)
        {
            Page = page;
            Annotation = annotation;
            Title = DescribeKind(annotation.Kind);
            Subtitle = $"Página {page.DisplayLabel}" +
                       (annotation.Kind == AnnotationKind.Ink
                           ? $" · {annotation.Points.Count} puntos"
                           : "");
            ColorBrush = new SolidColorBrush(annotation.Color);
            ColorBrush.Freeze();
        }

        public PageModel Page { get; }
        public AnnotationModel Annotation { get; }
        public string Title { get; }
        public string Subtitle { get; }
        public Brush ColorBrush { get; }
    }

    private sealed record PaletteCommand(string Name, string Hint, Func<bool> Enabled, Action Run);

    private void SetBusy(bool busy, string text = "Procesando…")
    {
        busyOverlay.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        if (busy) busyText.Text = text;
    }

    private void SetStatus(string message) => txtStatus.Text = message;

    private void ShowError(string title, Exception ex)
    {
        SetStatus(title + ".");
        MessageBox.Show(this, ex.Message, title, MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static bool IsPdfFile(string path)
    {
        return File.Exists(path) && Path.GetExtension(path).Equals(".pdf", StringComparison.OrdinalIgnoreCase);
    }
}
