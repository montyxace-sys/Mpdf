using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mpdf.Models;

namespace Mpdf.Services;

public sealed class PdfException : Exception
{
    public PdfException(string message) : base(message) { }
}

public sealed class DocumentSession : IDisposable
{
    private static readonly object PdfiumGate = new();
    private static bool _libraryReady;

    private readonly object _stateLock = new();
    private readonly Stack<EditAction> _undo = new();
    private readonly Stack<EditAction> _redo = new();
    private IntPtr _document;
    private IntPtr _buffer;
    private string? _password;
    private bool _disposed;
    private int _renderGeneration;

    public DocumentSession()
    {
        EnsureLibrary();
    }

    public ObservableCollection<PageModel> Pages { get; } = new();
    public string? FilePath { get; private set; }
    public bool HasDocument => _document != IntPtr.Zero;
    public bool IsDirty { get; private set; }
    public double Zoom { get; private set; } = 1.0;
    public IReadOnlyList<SearchHit> SearchHits { get; private set; } = Array.Empty<SearchHit>();
    public int SearchIndex { get; private set; } = -1;
    public DocumentInfo Info { get; private set; } = new();
    public IReadOnlyList<BookmarkNode> Bookmarks { get; private set; } = Array.Empty<BookmarkNode>();
    public IReadOnlyList<AttachmentInfo> Attachments { get; private set; } = Array.Empty<AttachmentInfo>();
    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;
    public string DisplayName => string.IsNullOrWhiteSpace(FilePath) ? "Sin título" : Path.GetFileName(FilePath);
    public int RenderGeneration => _renderGeneration;

    public event EventHandler? StateChanged;
    public event EventHandler? SearchChanged;
    public event EventHandler? PagesReloaded;

    private static void EnsureLibrary()
    {
        lock (PdfiumGate)
        {
            if (_libraryReady) return;
            PdfiumNative.FPDF_InitLibrary();
            _libraryReady = true;
        }
    }

    public static async Task<DocumentSession> OpenAsync(string path, string? password = null)
    {
        var bytes = await File.ReadAllBytesAsync(path);
        var session = new DocumentSession();
        await session.LoadAsync(bytes, path, password);
        return session;
    }

    public async Task LoadAsync(byte[] bytes, string? path, string? password)
    {
        var loaded = await Task.Run(() => LoadCore(bytes, password));

        lock (PdfiumGate)
        {
            ReleaseDocument();
            _document = loaded.Document;
            _buffer = loaded.Buffer;
            _password = password;
            FilePath = path;
            _renderGeneration++;
            Info = ReadDocumentInfo(_document, bytes);
            Bookmarks = ReadBookmarks(_document);
            Attachments = ReadAttachments(_document);
        }

        lock (_stateLock)
        {
            _undo.Clear();
            _redo.Clear();
            SearchHits = Array.Empty<SearchHit>();
            SearchIndex = -1;
            IsDirty = false;
        }

        Pages.Clear();
        foreach (var data in loaded.Pages) Pages.Add(CreatePageModel(data));
        PagesReloaded?.Invoke(this, EventArgs.Empty);
        SearchChanged?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<List<PageModel>> AppendDocumentAsync(byte[] bytes, string? password = null)
        => InsertDocumentAsync(bytes, Pages.Count, password);

    public async Task<List<PageModel>> InsertDocumentAsync(byte[] bytes, int index, string? password = null)
    {
        if (bytes.Length == 0) throw new PdfException("El archivo está vacío.");
        index = Math.Clamp(index, 0, Pages.Count);

        var data = await Task.Run(() =>
        {
            var sourceBuffer = Marshal.AllocHGlobal(bytes.Length);
            Marshal.Copy(bytes, 0, sourceBuffer, bytes.Length);
            var source = IntPtr.Zero;
            try
            {
                source = PdfiumNative.FPDF_LoadMemDocument64(sourceBuffer, (nuint)bytes.Length, password);
                if (source == IntPtr.Zero)
                    throw CreateException(PdfiumNative.FPDF_GetLastError(), password);

                var sourceCount = PdfiumNative.FPDF_GetPageCount(source);
                if (sourceCount <= 0) throw new PdfException("El PDF no contiene páginas.");

                lock (PdfiumGate)
                {
                    if (_document == IntPtr.Zero) throw new PdfException("No hay ningún documento abierto.");
                    if (!PdfiumNative.FPDF_ImportPages(_document, source, null, index))
                        throw new PdfException("No se pudieron importar las páginas.");

                    var added = new List<PageData>(sourceCount);
                    for (var i = 0; i < sourceCount; i++) added.Add(ReadPage(_document, index + i));
                    return added;
                }
            }
            finally
            {
                if (source != IntPtr.Zero) PdfiumNative.FPDF_CloseDocument(source);
                Marshal.FreeHGlobal(sourceBuffer);
            }
        });

        return InsertPageModels(index, data);
    }

    public PageModel InsertBlankPage(int index, double width, double height)
    {
        if (width < 36 || height < 36) throw new PdfException("El tamaño de la página no es válido.");
        index = Math.Clamp(index, 0, Pages.Count);

        PageData data;
        lock (PdfiumGate)
        {
            if (_document == IntPtr.Zero) throw new PdfException("No hay ningún documento abierto.");
            var page = PdfiumNative.FPDFPage_New(_document, index, width, height);
            if (page == IntPtr.Zero) throw new PdfException("No se pudo crear la página.");
            try
            {
                PdfiumNative.FPDFPage_GenerateContent(page);
            }
            finally
            {
                PdfiumNative.FPDF_ClosePage(page);
            }
            data = ReadPage(_document, index);
        }

        return InsertPageModels(index, new List<PageData> { data })[0];
    }

    public Task<BitmapSource?> RenderPageAsync(Guid pageId, double targetWidth)
    {
        return Task.Run(() =>
        {
            PageModel? page;
            lock (_stateLock)
            {
                page = Pages.FirstOrDefault(p => p.Id == pageId);
            }
            if (page == null) return null;

            var index = page.Index;
            lock (PdfiumGate)
            {
                if (_disposed || _document == IntPtr.Zero) return null;
                return RenderPageCore(_document, index, targetWidth);
            }
        });
    }

    public bool RotatePage(PageModel page, int delta)
    {
        var newRotation = ((page.Rotation + delta) % 4 + 4) % 4;
        lock (PdfiumGate)
        {
            if (_document == IntPtr.Zero) return false;
            var handle = PdfiumNative.FPDF_LoadPage(_document, page.Index);
            if (handle == IntPtr.Zero) return false;
            try
            {
                PdfiumNative.FPDFPage_SetRotation(handle, newRotation);
            }
            finally
            {
                PdfiumNative.FPDF_ClosePage(handle);
            }
        }

        page.Rotation = newRotation;
        foreach (var annotation in page.Annotations)
        {
            for (var i = 0; i < annotation.Points.Count; i++)
                annotation.Points[i] = RotateNormalizedPoint(annotation.Points[i], delta);
        }

        page.RaiseRotationChanged();
        ClearSearchInternal();
        ClearHistory();
        MarkDirty();
        IncrementRenderGeneration();
        return true;
    }

    public bool DeletePage(PageModel page)
    {
        if (Pages.Count <= 1) return false;
        var index = page.Index;
        lock (PdfiumGate)
        {
            if (_document == IntPtr.Zero) return false;
            PdfiumNative.FPDFPage_Delete(_document, index);
        }

        Pages.Remove(page);
        UpdateIndices();
        ClearSearchInternal();
        ClearHistory();
        MarkDirty();
        IncrementRenderGeneration();
        return true;
    }

    public bool MovePage(PageModel page, int direction)
    {
        var from = page.Index;
        var to = from + direction;
        if (to < 0 || to >= Pages.Count) return false;

        lock (PdfiumGate)
        {
            if (_document == IntPtr.Zero) return false;
            if (!PdfiumNative.FPDF_MovePages(_document, new[] { from }, 1, to)) return false;
        }

        Pages.Move(from, to);
        UpdateIndices();
        ClearSearchInternal();
        ClearHistory();
        MarkDirty();
        IncrementRenderGeneration();
        return true;
    }

    public Task ExportPageAsync(PageModel page, string path)
        => ExportRangeAsync((page.Index + 1).ToString(), path);

    public async Task ExportRangeAsync(string pageRange, string path)
    {
        if (string.IsNullOrWhiteSpace(pageRange))
            throw new PdfException("Indica un rango de páginas, por ejemplo 1-3,5.");

        await Task.Run(() =>
        {
            lock (PdfiumGate)
            {
                if (_document == IntPtr.Zero) throw new PdfException("No hay ningún documento abierto.");
                AddAnnotationsToDocument();
                var target = IntPtr.Zero;
                try
                {
                    target = PdfiumNative.FPDF_CreateNewDocument();
                    if (target == IntPtr.Zero) throw new PdfException("No se pudo crear el documento.");
                    if (!PdfiumNative.FPDF_ImportPages(target, _document, pageRange, 0))
                        throw new PdfException("El rango de páginas no es válido.");
                    WriteFileAtomic(path, SaveDocumentToBytes(target));
                }
                finally
                {
                    if (target != IntPtr.Zero) PdfiumNative.FPDF_CloseDocument(target);
                    RemoveManagedAnnotations();
                }
            }
        });
    }

    public Task<IReadOnlyList<string>> SplitAsync(string directory, string baseName, int pagesPerFile)
    {
        return Task.Run<IReadOnlyList<string>>(() =>
        {
            pagesPerFile = Math.Max(1, pagesPerFile);
            baseName = string.IsNullOrWhiteSpace(baseName) ? "documento" : baseName;

            lock (PdfiumGate)
            {
                if (_document == IntPtr.Zero) throw new PdfException("No hay ningún documento abierto.");
                AddAnnotationsToDocument();
                try
                {
                    var paths = new List<string>();
                    var total = Pages.Count;
                    var part = 1;
                    for (var start = 1; start <= total; start += pagesPerFile)
                    {
                        var end = Math.Min(total, start + pagesPerFile - 1);
                        var range = start == end ? start.ToString() : $"{start}-{end}";
                        var target = PdfiumNative.FPDF_CreateNewDocument();
                        if (target == IntPtr.Zero) throw new PdfException("No se pudo crear el documento.");
                        try
                        {
                            if (!PdfiumNative.FPDF_ImportPages(target, _document, range, 0))
                                throw new PdfException($"No se pudo importar el rango {range}.");
                            var path = Path.Combine(directory, $"{baseName}-{part:00}.pdf");
                            WriteFileAtomic(path, SaveDocumentToBytes(target));
                            paths.Add(path);
                        }
                        finally
                        {
                            PdfiumNative.FPDF_CloseDocument(target);
                        }
                        part++;
                    }
                    return paths;
                }
                finally
                {
                    RemoveManagedAnnotations();
                }
            }
        });
    }

    public async Task AddPageNumbersAsync(PageNumberOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Format))
            throw new PdfException("El formato de la numeración está vacío.");

        var fontSize = Math.Clamp(options.FontSize, 4, 96);
        var margin = Math.Clamp(options.Margin, 0, 200);
        var pages = Pages.ToList();

        await Task.Run(() =>
        {
            lock (PdfiumGate)
            {
                if (_document == IntPtr.Zero) throw new PdfException("No hay ningún documento abierto.");
                var font = PdfiumNative.FPDFText_LoadStandardFont(_document, "Helvetica");
                if (font == IntPtr.Zero) throw new PdfException("No se pudo cargar la fuente de la numeración.");

                var failed = new List<string>();
                try
                {
                    foreach (var page in pages)
                    {
                        var number = options.StartNumber + page.Index;
                        var text = options.Format.Replace("{n}", number.ToString(), StringComparison.Ordinal)
                            .ReplaceLineEndings(" ");
                        if (text.Length == 0) continue;

                        var stamp = new TextStamp(text, fontSize, options.Color, 255, 0,
                            options.Placement, margin, 0);
                        if (!StampPage(font, page, stamp)) failed.Add(page.DisplayLabel);
                    }
                }
                finally
                {
                    PdfiumNative.FPDFFont_Close(font);
                }

                if (failed.Count > 0)
                    throw new PdfException("No se pudo añadir la numeración a las páginas: "
                        + string.Join(", ", failed) + ".");
            }
        });

        AfterStamp(pages);
    }

    public async Task AddWatermarkAsync(WatermarkOptions options)
    {
        var text = options.Text.Trim().ReplaceLineEndings(" ");
        if (text.Length == 0) throw new PdfException("Escribe el texto de la marca de agua.");

        var percent = Math.Clamp(options.SizePercent, 10, 100);
        var alpha = (byte)Math.Round(Math.Clamp(options.Opacity, 0.02, 1) * 255);
        var pages = Pages.ToList();

        await Task.Run(() =>
        {
            lock (PdfiumGate)
            {
                if (_document == IntPtr.Zero) throw new PdfException("No hay ningún documento abierto.");
                var font = PdfiumNative.FPDFText_LoadStandardFont(_document, "Helvetica");
                if (font == IntPtr.Zero) throw new PdfException("No se pudo cargar la fuente de la marca de agua.");

                var failed = new List<string>();
                try
                {
                    foreach (var page in pages)
                    {
                        var stamp = new TextStamp(text, 48, options.Color, alpha,
                            options.Angle, StampPlacement.Center, 24, percent);
                        if (!StampPage(font, page, stamp)) failed.Add(page.DisplayLabel);
                    }
                }
                finally
                {
                    PdfiumNative.FPDFFont_Close(font);
                }

                if (failed.Count > 0)
                    throw new PdfException("No se pudo añadir la marca de agua a las páginas: "
                        + string.Join(", ", failed) + ".");
            }
        });

        AfterStamp(pages);
    }

    public void RotateAllPages(int delta)
    {
        var steps = ((delta % 4) + 4) % 4;
        if (steps == 0 || Pages.Count == 0) return;

        lock (PdfiumGate)
        {
            if (_document == IntPtr.Zero) return;
            foreach (var page in Pages)
            {
                var rotation = (page.Rotation + steps) % 4;
                var handle = PdfiumNative.FPDF_LoadPage(_document, page.Index);
                if (handle == IntPtr.Zero) continue;
                try
                {
                    PdfiumNative.FPDFPage_SetRotation(handle, rotation);
                }
                finally
                {
                    PdfiumNative.FPDF_ClosePage(handle);
                }

                page.Rotation = rotation;
                foreach (var annotation in page.Annotations)
                {
                    for (var i = 0; i < annotation.Points.Count; i++)
                        annotation.Points[i] = RotateNormalizedPoint(annotation.Points[i], steps);
                }
            }
        }

        foreach (var page in Pages) page.RaiseRotationChanged();
        ClearSearchInternal();
        ClearHistory();
        MarkDirty();
        IncrementRenderGeneration();
    }

    public async Task<IReadOnlyList<string>> ExportImagesAsync(ImageExportOptions options)
    {
        var indices = ParsePageIndices(options.PageRange, Pages.Count);
        var dpi = Math.Clamp(options.Dpi, 36, 600);
        var jpeg = options.Format.Equals("jpg", StringComparison.OrdinalIgnoreCase)
                   || options.Format.Equals("jpeg", StringComparison.OrdinalIgnoreCase);
        var baseName = SanitizeFileName(options.BaseName);
        var directory = Path.GetFullPath(options.Directory);
        var pages = indices.Select(i => Pages[i]).ToList();
        var paths = new List<string>(pages.Count);

        await Task.Run(() =>
        {
            Directory.CreateDirectory(directory);
            lock (PdfiumGate)
            {
                if (_document == IntPtr.Zero) throw new PdfException("No hay ningún documento abierto.");
                AddAnnotationsToDocument();
                try
                {
                    foreach (var page in pages)
                    {
                        var targetWidth = page.DisplayWidthPoints * dpi / 72.0;
                        var bitmap = RenderPageCore(_document, page.Index, targetWidth);
                        if (bitmap == null)
                            throw new PdfException($"No se pudo renderizar la página {page.DisplayLabel}.");

                        var extension = jpeg ? "jpg" : "png";
                        var path = Path.Combine(directory,
                            $"{baseName}-{page.Index + 1:000}.{extension}");
                        WriteFileAtomic(path, EncodeImage(bitmap, jpeg));
                        paths.Add(path);
                    }
                }
                finally
                {
                    RemoveManagedAnnotations();
                }
            }
        });

        return paths;
    }

    public static IReadOnlyList<int> ParsePageIndices(string? range, int pageCount)
    {
        if (pageCount <= 0) throw new PdfException("No hay páginas que exportar.");

        var text = string.IsNullOrWhiteSpace(range) ? $"1-{pageCount}" : range.Trim();
        var result = new List<int>();
        foreach (var rawPart in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var part = rawPart.Replace('\u2013', '-');
            var dash = part.IndexOf('-');
            int start;
            int end;
            if (dash < 0)
            {
                if (!int.TryParse(part, out start)) throw InvalidRange(part);
                end = start;
            }
            else
            {
                if (!int.TryParse(part[..dash], out start)
                    || !int.TryParse(part[(dash + 1)..], out end))
                    throw InvalidRange(part);
            }

            if (start > end) (start, end) = (end, start);
            if (start < 1 || end > pageCount) throw InvalidRange(part);

            for (var i = start; i <= end; i++)
                if (!result.Contains(i - 1)) result.Add(i - 1);
        }

        if (result.Count == 0) throw InvalidRange(text);
        return result;

        static PdfException InvalidRange(string part) =>
            new($"El rango de páginas «{part}» no es válido. Usa, por ejemplo, 1-3,5.");
    }

    public async Task SaveAsync(string path)
    {
        await Task.Run(() =>
        {
            byte[] bytes;
            lock (PdfiumGate)
            {
                if (_document == IntPtr.Zero) throw new PdfException("No hay ningún documento abierto.");
                AddAnnotationsToDocument();
                try
                {
                    bytes = SaveDocumentToBytes(_document);
                }
                finally
                {
                    RemoveManagedAnnotations();
                }
            }
            WriteFileAtomic(path, bytes);
        });

        FilePath = path;
        MarkClean();
    }

    public async Task<IReadOnlyList<SearchHit>> SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return Array.Empty<SearchHit>();

        var snapshot = Pages
            .Select(p => new PageSnapshot(p.Id, p.Index, p.UnrotatedWidth, p.UnrotatedHeight, p.Rotation))
            .ToList();

        var hits = await Task.Run(() =>
        {
            var found = new List<SearchHit>();
            lock (PdfiumGate)
            {
                if (_document == IntPtr.Zero) return found;
                foreach (var page in snapshot)
                {
                    if (found.Count >= 2000) break;
                    var pageHandle = PdfiumNative.FPDF_LoadPage(_document, page.Index);
                    if (pageHandle == IntPtr.Zero) continue;
                    var textPage = IntPtr.Zero;
                    var search = IntPtr.Zero;
                    try
                    {
                        textPage = PdfiumNative.FPDFText_LoadPage(pageHandle);
                        if (textPage == IntPtr.Zero) continue;
                        search = PdfiumNative.FPDFText_FindStart(textPage, query, 0, 0);
                        if (search == IntPtr.Zero) continue;

                        while (PdfiumNative.FPDFText_FindNext(search))
                        {
                            var start = PdfiumNative.FPDFText_GetSchResultIndex(search);
                            var count = PdfiumNative.FPDFText_GetSchCount(search);
                            var rectCount = PdfiumNative.FPDFText_CountRects(textPage, start, count);
                            for (var r = 0; r < rectCount; r++)
                            {
                                if (!PdfiumNative.FPDFText_GetRect(textPage, r, out var left, out var top,
                                        out var right, out var bottom))
                                    continue;
                                var rect = PdfRectToNormalized(left, top, right, bottom,
                                    page.UnrotatedWidth, page.UnrotatedHeight, page.Rotation);
                                found.Add(new SearchHit(page.Id, page.Index, rect));
                                if (found.Count >= 2000) break;
                            }
                            if (found.Count >= 2000) break;
                        }
                    }
                    finally
                    {
                        if (search != IntPtr.Zero) PdfiumNative.FPDFText_FindClose(search);
                        if (textPage != IntPtr.Zero) PdfiumNative.FPDFText_ClosePage(textPage);
                        PdfiumNative.FPDF_ClosePage(pageHandle);
                    }
                }
            }
            return found;
        });

        SearchHits = hits;
        SearchIndex = hits.Count > 0 ? 0 : -1;
        SearchChanged?.Invoke(this, EventArgs.Empty);
        return hits;
    }

    public Task<string> GetPageTextAsync(PageModel page)
    {
        return Task.Run(() =>
        {
            lock (PdfiumGate)
            {
                if (_document == IntPtr.Zero) return "";
                return ExtractPageText(_document, page.Index);
            }
        });
    }

    public Task<string> GetDocumentTextAsync()
    {
        return Task.Run(() =>
        {
            lock (PdfiumGate)
            {
                if (_document == IntPtr.Zero) return "";
                var count = Pages.Count;
                var parts = new List<string>(count);
                for (var i = 0; i < count; i++)
                {
                    var text = ExtractPageText(_document, i);
                    if (text.Length > 0) parts.Add(text);
                }
                return string.Join("\n\n", parts);
            }
        });
    }

    public Task SaveAttachmentAsync(AttachmentInfo attachment, string path)
    {
        return Task.Run(() =>
        {
            byte[] bytes;
            lock (PdfiumGate)
            {
                if (_document == IntPtr.Zero) throw new PdfException("No hay ningún documento abierto.");
                var handle = PdfiumNative.FPDFDoc_GetAttachment(_document, attachment.Index);
                if (handle == IntPtr.Zero) throw new PdfException("No se pudo abrir el adjunto.");

                if (!PdfiumNative.FPDFAttachment_GetFile(handle, IntPtr.Zero, 0, out var size))
                    throw new PdfException("No se pudo leer el adjunto.");

                if (size == 0)
                {
                    bytes = Array.Empty<byte>();
                }
                else
                {
                    var buffer = Marshal.AllocHGlobal((int)size);
                    try
                    {
                        if (!PdfiumNative.FPDFAttachment_GetFile(handle, buffer, size, out var written))
                            throw new PdfException("No se pudo leer el adjunto.");
                        var length = (int)written;
                        bytes = new byte[length];
                        Marshal.Copy(buffer, bytes, 0, length);
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
            }

            WriteFileAtomic(path, bytes);
        });
    }

    public void ClearSearch()
    {
        ClearSearchInternal();
    }

    public void NextSearchHit()
    {
        if (SearchHits.Count == 0) return;
        SearchIndex = (SearchIndex + 1) % SearchHits.Count;
        SearchChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PreviousSearchHit()
    {
        if (SearchHits.Count == 0) return;
        SearchIndex = (SearchIndex - 1 + SearchHits.Count) % SearchHits.Count;
        SearchChanged?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyList<SearchHit> SearchHitsForPage(Guid pageId)
    {
        if (SearchHits.Count == 0) return Array.Empty<SearchHit>();
        return SearchHits.Where(h => h.PageId == pageId).ToList();
    }

    public SearchHit? CurrentSearchHit =>
        SearchIndex >= 0 && SearchIndex < SearchHits.Count ? SearchHits[SearchIndex] : null;

    public void SetZoom(double zoom)
    {
        zoom = Math.Clamp(zoom, 0.15, 5.0);
        if (Math.Abs(Zoom - zoom) < 0.0001) return;
        Zoom = zoom;
        foreach (var page in Pages) page.Zoom = zoom;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void InvalidateAllPages()
    {
        foreach (var page in Pages) page.InvalidateBitmap();
        IncrementRenderGeneration();
    }

    public void AddAnnotation(PageModel page, AnnotationModel annotation)
    {
        page.Annotations.Add(annotation);
        PushEdit(new EditAction(
            () => page.Annotations.Remove(annotation),
            () =>
            {
                if (!page.Annotations.Contains(annotation)) page.Annotations.Add(annotation);
            }));
        MarkDirty();
    }

    public void RemoveAnnotation(PageModel page, AnnotationModel annotation)
    {
        var index = page.Annotations.IndexOf(annotation);
        if (index < 0) return;
        page.Annotations.RemoveAt(index);
        PushEdit(new EditAction(
            () =>
            {
                if (!page.Annotations.Contains(annotation))
                    page.Annotations.Insert(Math.Min(index, page.Annotations.Count), annotation);
            },
            () => page.Annotations.Remove(annotation)));
        MarkDirty();
    }

    public void ClearAnnotations(PageModel page)
    {
        if (page.Annotations.Count == 0) return;
        var snapshot = page.Annotations.ToList();
        page.Annotations.Clear();
        PushEdit(new EditAction(
            () =>
            {
                foreach (var annotation in snapshot)
                    if (!page.Annotations.Contains(annotation))
                        page.Annotations.Add(annotation);
            },
            () =>
            {
                foreach (var annotation in snapshot)
                    page.Annotations.Remove(annotation);
            }));
        MarkDirty();
    }

    public void Undo()
    {
        if (_undo.Count == 0) return;
        var edit = _undo.Pop();
        edit.Undo();
        _redo.Push(edit);
        MarkDirty();
    }

    public void Redo()
    {
        if (_redo.Count == 0) return;
        var edit = _redo.Pop();
        edit.Redo();
        _undo.Push(edit);
        MarkDirty();
    }

    public void MarkDirty()
    {
        IsDirty = true;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void MarkClean()
    {
        IsDirty = false;
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        _disposed = true;
        lock (PdfiumGate)
        {
            ReleaseDocument();
        }
        Pages.Clear();
    }

    private PageModel CreatePageModel(PageData data)
    {
        var model = new PageModel(this, data.Id, data.Width, data.Height, data.Rotation)
        {
            Index = Pages.Count,
            Zoom = Zoom,
            Label = data.Label
        };
        foreach (var annotation in data.Annotations) model.Annotations.Add(annotation);
        return model;
    }

    private List<PageModel> InsertPageModels(int index, List<PageData> data)
    {
        var models = new List<PageModel>(data.Count);
        for (var i = 0; i < data.Count; i++)
        {
            var model = CreatePageModel(data[i]);
            Pages.Insert(index + i, model);
            models.Add(model);
        }

        UpdateIndices();
        ClearSearchInternal();
        ClearHistory();
        MarkDirty();
        IncrementRenderGeneration();
        return models;
    }

    private void PushEdit(EditAction edit)
    {
        _undo.Push(edit);
        _redo.Clear();
    }

    private void ClearHistory()
    {
        _undo.Clear();
        _redo.Clear();
    }

    private void ReleaseDocument()
    {
        if (_document != IntPtr.Zero)
        {
            PdfiumNative.FPDF_CloseDocument(_document);
            _document = IntPtr.Zero;
        }
        if (_buffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_buffer);
            _buffer = IntPtr.Zero;
        }
    }

    private void UpdateIndices()
    {
        for (var i = 0; i < Pages.Count; i++) Pages[i].Index = i;
    }

    private void ClearSearchInternal()
    {
        if (SearchHits.Count == 0 && SearchIndex < 0) return;
        SearchHits = Array.Empty<SearchHit>();
        SearchIndex = -1;
        SearchChanged?.Invoke(this, EventArgs.Empty);
    }

    private void IncrementRenderGeneration() => _renderGeneration++;

    private static LoadedDocument LoadCore(byte[] bytes, string? password)
    {
        if (bytes.Length == 0) throw new PdfException("El archivo está vacío.");

        IntPtr buffer;
        try
        {
            buffer = Marshal.AllocHGlobal(bytes.Length);
        }
        catch (OutOfMemoryException)
        {
            throw new PdfException("No hay memoria suficiente para abrir el documento.");
        }

        Marshal.Copy(bytes, 0, buffer, bytes.Length);
        IntPtr document;
        try
        {
            document = PdfiumNative.FPDF_LoadMemDocument64(buffer, (nuint)bytes.Length, password);
        }
        catch (DllNotFoundException)
        {
            Marshal.FreeHGlobal(buffer);
            throw new PdfException("No se encontró el motor PDFium (pdfium.dll).");
        }
        catch (BadImageFormatException)
        {
            Marshal.FreeHGlobal(buffer);
            throw new PdfException("La versión de PDFium no es compatible con este equipo (se requiere x64).");
        }

        if (document == IntPtr.Zero)
        {
            var error = PdfiumNative.FPDF_GetLastError();
            Marshal.FreeHGlobal(buffer);
            throw CreateException(error, password);
        }

        try
        {
            var count = PdfiumNative.FPDF_GetPageCount(document);
            var pages = new List<PageData>(Math.Max(count, 0));
            for (var i = 0; i < count; i++) pages.Add(ReadPage(document, i));
            return new LoadedDocument(document, buffer, pages);
        }
        catch
        {
            PdfiumNative.FPDF_CloseDocument(document);
            Marshal.FreeHGlobal(buffer);
            throw;
        }
    }

    private static PageData ReadPage(IntPtr document, int index)
    {
        var handle = PdfiumNative.FPDF_LoadPage(document, index);
        if (handle == IntPtr.Zero)
            throw new PdfException($"No se pudo leer la página {index + 1}.");

        try
        {
            var width = PdfiumNative.FPDF_GetPageWidthF(handle);
            var height = PdfiumNative.FPDF_GetPageHeightF(handle);
            var rotation = Math.Clamp(PdfiumNative.FPDFPage_GetRotation(handle), 0, 3);

            var unrotatedWidth = rotation % 2 == 0 ? width : height;
            var unrotatedHeight = rotation % 2 == 0 ? height : width;
            var annotations = ReadAnnotations(handle, unrotatedWidth, unrotatedHeight, rotation);
            var label = ReadUtf16Text((buffer, length) =>
                PdfiumNative.FPDF_GetPageLabel(document, index, buffer, length));

            return new PageData(Guid.NewGuid(), unrotatedWidth, unrotatedHeight, rotation, label,
                annotations);
        }
        finally
        {
            PdfiumNative.FPDF_ClosePage(handle);
        }
    }

    private static List<AnnotationModel> ReadAnnotations(IntPtr page, double width, double height, int rotation)
    {
        var annotations = new List<AnnotationModel>();
        var remove = new List<int>();
        var count = PdfiumNative.FPDFPage_GetAnnotCount(page);

        for (var i = 0; i < count; i++)
        {
            var handle = PdfiumNative.FPDFPage_GetAnnot(page, i);
            if (handle == IntPtr.Zero) continue;
            try
            {
                var subtype = PdfiumNative.FPDFAnnot_GetSubtype(handle);
                var kind = subtype switch
                {
                    PdfiumNative.FPDF_AnnotHighlight => AnnotationKind.Highlight,
                    PdfiumNative.FPDF_AnnotUnderline => AnnotationKind.Underline,
                    PdfiumNative.FPDF_AnnotStrikeout => AnnotationKind.Strikeout,
                    PdfiumNative.FPDF_AnnotSquare => AnnotationKind.Rectangle,
                    PdfiumNative.FPDF_AnnotInk => AnnotationKind.Ink,
                    _ => (AnnotationKind?)null
                };
                if (kind == null) continue;

                var rect = new PdfiumNative.FS_RECTF();
                if (!PdfiumNative.FPDFAnnot_GetRect(handle, ref rect)) continue;

                var color = Colors.Yellow;
                if (PdfiumNative.FPDFAnnot_GetColor(handle, PdfiumNative.FpdfAnnotColor,
                        out var r, out var g, out var b, out _))
                    color = Color.FromRgb((byte)r, (byte)g, (byte)b);

                var strokeWidth = 1.8;
                if (PdfiumNative.FPDFAnnot_GetBorder(handle, out _, out _, out var border) && border > 0)
                    strokeWidth = border;

                if (kind == AnnotationKind.Ink)
                {
                    var paths = PdfiumNative.FPDFAnnot_GetInkListCount(handle);
                    for (var pathIndex = 0; pathIndex < paths; pathIndex++)
                    {
                        var pointCount = PdfiumNative.FPDFAnnot_GetInkListPath(handle, (uint)pathIndex,
                            Array.Empty<PdfiumNative.FS_POINTF>(), 0);
                        if (pointCount < 2) continue;

                        var points = new PdfiumNative.FS_POINTF[pointCount];
                        var read = PdfiumNative.FPDFAnnot_GetInkListPath(handle, (uint)pathIndex, points, pointCount);
                        if (read < 2) continue;

                        var annotation = new AnnotationModel
                        {
                            Kind = AnnotationKind.Ink,
                            Color = color,
                            StrokeWidth = strokeWidth
                        };
                        for (var p = 0; p < read; p++)
                        {
                            var normalized = NormalizeFromPdfPoint(points[p].X, points[p].Y, width, height, rotation);
                            annotation.Points.Add(normalized);
                        }
                        annotations.Add(annotation);
                    }
                }
                else
                {
                    var normalized = PdfRectToNormalized(rect.Left, rect.Top, rect.Right, rect.Bottom,
                        width, height, rotation);
                    var annotation = new AnnotationModel { Kind = kind.Value, Color = color, StrokeWidth = strokeWidth };
                    annotation.Points.Add(normalized.TopLeft);
                    annotation.Points.Add(normalized.BottomRight);
                    annotations.Add(annotation);
                }

                remove.Add(i);
            }
            finally
            {
                PdfiumNative.FPDFPage_CloseAnnot(handle);
            }
        }

        for (var i = remove.Count - 1; i >= 0; i--)
            PdfiumNative.FPDFPage_RemoveAnnot(page, remove[i]);

        return annotations;
    }

    private void AddAnnotationsToDocument()
    {
        if (_document == IntPtr.Zero) return;

        foreach (var model in Pages)
        {
            if (model.Annotations.Count == 0) continue;
            var page = PdfiumNative.FPDF_LoadPage(_document, model.Index);
            if (page == IntPtr.Zero) continue;
            try
            {
                foreach (var annotation in model.Annotations) AddAnnotation(page, model, annotation);
            }
            finally
            {
                PdfiumNative.FPDF_ClosePage(page);
            }
        }
    }

    private static void AddAnnotation(IntPtr page, PageModel model, AnnotationModel annotation)
    {
        var subtype = annotation.Kind switch
        {
            AnnotationKind.Highlight => PdfiumNative.FPDF_AnnotHighlight,
            AnnotationKind.Underline => PdfiumNative.FPDF_AnnotUnderline,
            AnnotationKind.Strikeout => PdfiumNative.FPDF_AnnotStrikeout,
            AnnotationKind.Rectangle => PdfiumNative.FPDF_AnnotSquare,
            AnnotationKind.Ink => PdfiumNative.FPDF_AnnotInk,
            _ => -1
        };
        if (subtype < 0) return;

        var handle = PdfiumNative.FPDFPage_CreateAnnot(page, subtype);
        if (handle == IntPtr.Zero) return;
        try
        {
            var bounds = annotation.Bounds;
            if (bounds.IsEmpty) return;

            var pdfRect = NormalizedRectToPdf(bounds, model);
            var rect = new PdfiumNative.FS_RECTF
            {
                Left = (float)pdfRect.Left,
                Top = (float)pdfRect.Top,
                Right = (float)pdfRect.Right,
                Bottom = (float)pdfRect.Bottom
            };
            PdfiumNative.FPDFAnnot_SetRect(handle, ref rect);
            PdfiumNative.FPDFAnnot_SetColor(handle, PdfiumNative.FpdfAnnotColor,
                annotation.Color.R, annotation.Color.G, annotation.Color.B, 255);
            PdfiumNative.FPDFAnnot_SetFlags(handle, PdfiumNative.FpdfAnnotFlagPrint);

            switch (annotation.Kind)
            {
                case AnnotationKind.Highlight:
                case AnnotationKind.Underline:
                case AnnotationKind.Strikeout:
                {
                    var quad = new PdfiumNative.FS_QUADPOINTSF
                    {
                        X1 = rect.Left, Y1 = rect.Top,
                        X2 = rect.Right, Y2 = rect.Top,
                        X3 = rect.Left, Y3 = rect.Bottom,
                        X4 = rect.Right, Y4 = rect.Bottom
                    };
                    PdfiumNative.FPDFAnnot_AppendAttachmentPoints(handle, ref quad);
                    break;
                }
                case AnnotationKind.Ink:
                {
                    if (annotation.Points.Count < 2) return;
                    var points = annotation.Points
                        .Select(p => ToPdfPoint(p, model))
                        .Select(p => new PdfiumNative.FS_POINTF { X = (float)p.X, Y = (float)p.Y })
                        .ToArray();
                    PdfiumNative.FPDFAnnot_AddInkStroke(handle, points, (nuint)points.Length);
                    PdfiumNative.FPDFAnnot_SetBorder(handle, 0, 0, (float)annotation.StrokeWidth);
                    break;
                }
            }
        }
        finally
        {
            PdfiumNative.FPDFPage_CloseAnnot(handle);
        }
    }

    private void RemoveManagedAnnotations()
    {
        if (_document == IntPtr.Zero) return;
        var count = PdfiumNative.FPDF_GetPageCount(_document);
        for (var index = 0; index < count; index++)
        {
            var page = PdfiumNative.FPDF_LoadPage(_document, index);
            if (page == IntPtr.Zero) continue;
            try
            {
                var annotCount = PdfiumNative.FPDFPage_GetAnnotCount(page);
                for (var i = annotCount - 1; i >= 0; i--)
                {
                    var handle = PdfiumNative.FPDFPage_GetAnnot(page, i);
                    if (handle == IntPtr.Zero) continue;
                    int subtype;
                    try
                    {
                        subtype = PdfiumNative.FPDFAnnot_GetSubtype(handle);
                    }
                    finally
                    {
                        PdfiumNative.FPDFPage_CloseAnnot(handle);
                    }

                    if (subtype is PdfiumNative.FPDF_AnnotHighlight or PdfiumNative.FPDF_AnnotUnderline
                        or PdfiumNative.FPDF_AnnotStrikeout or PdfiumNative.FPDF_AnnotSquare
                        or PdfiumNative.FPDF_AnnotInk)
                    {
                        PdfiumNative.FPDFPage_RemoveAnnot(page, i);
                    }
                }
            }
            finally
            {
                PdfiumNative.FPDF_ClosePage(page);
            }
        }
    }

    private void AfterStamp(IReadOnlyCollection<PageModel> pages)
    {
        foreach (var page in pages) page.InvalidateAll();
        ClearSearchInternal();
        ClearHistory();
        MarkDirty();
        IncrementRenderGeneration();
    }

    private bool StampPage(IntPtr font, PageModel page, TextStamp stamp)
    {
        var handle = PdfiumNative.FPDF_LoadPage(_document, page.Index);
        if (handle == IntPtr.Zero) return false;

        var textObject = IntPtr.Zero;
        try
        {
            textObject = PdfiumNative.FPDFPageObj_CreateTextObj(_document, font, (float)stamp.FontSize);
            if (textObject == IntPtr.Zero) return false;
            if (!PdfiumNative.FPDFText_SetText(textObject, stamp.Text)) return false;

            PdfiumNative.FPDFPageObj_SetFillColor(textObject,
                stamp.Color.R, stamp.Color.G, stamp.Color.B, stamp.Alpha);

            if (!PdfiumNative.FPDFPageObj_GetBounds(textObject,
                    out var left, out var top, out var right, out var bottom))
                return false;

            var width = Math.Abs(right - left);
            var height = Math.Abs(bottom - top);
            if (width < 0.01 || height < 0.01) return false;

            // Deja el texto con su esquina inferior izquierda en (0, 0).
            PdfiumNative.FPDFPageObj_Transform(textObject, 1, 0, 0, 1, -left, -Math.Min(top, bottom));

            var angle = (page.Rotation * 90 + stamp.Angle) * Math.PI / 180.0;
            var cos = Math.Cos(angle);
            var sin = Math.Sin(angle);
            var rotatedWidth = Math.Abs(width * cos) + Math.Abs(height * sin);
            var rotatedHeight = Math.Abs(width * sin) + Math.Abs(height * cos);

            var displayWidth = page.DisplayWidthPoints;
            var displayHeight = page.DisplayHeightPoints;
            var scale = 1.0;
            if (stamp.FitWidthPercent > 0)
            {
                var targetWidth = displayWidth * stamp.FitWidthPercent / 100.0;
                var targetHeight = displayHeight * 0.85;
                scale = Math.Min(targetWidth / rotatedWidth, targetHeight / rotatedHeight);
                if (scale <= 0) return false;
            }

            var halfWidth = rotatedWidth * scale / 2.0;
            var halfHeight = rotatedHeight * scale / 2.0;
            var margin = stamp.Margin;
            var anchorX = stamp.Placement switch
            {
                StampPlacement.TopLeft or StampPlacement.MiddleLeft or StampPlacement.BottomLeft =>
                    margin + halfWidth,
                StampPlacement.TopRight or StampPlacement.MiddleRight or StampPlacement.BottomRight =>
                    displayWidth - margin - halfWidth,
                _ => displayWidth / 2.0
            };
            var anchorY = stamp.Placement switch
            {
                StampPlacement.TopLeft or StampPlacement.TopCenter or StampPlacement.TopRight =>
                    displayHeight - margin - halfHeight,
                StampPlacement.BottomLeft or StampPlacement.BottomCenter or StampPlacement.BottomRight =>
                    margin + halfHeight,
                _ => displayHeight / 2.0
            };

            var (contentX, contentY) = DisplayPointToContent(anchorX, anchorY, page);
            var offsetX = width / 2.0 * cos - height / 2.0 * sin;
            var offsetY = width / 2.0 * sin + height / 2.0 * cos;
            var translateX = contentX - scale * offsetX;
            var translateY = contentY - scale * offsetY;

            PdfiumNative.FPDFPageObj_Transform(textObject,
                cos * scale, sin * scale, -sin * scale, cos * scale, translateX, translateY);

            PdfiumNative.FPDFPage_InsertObject(handle, textObject);
            textObject = IntPtr.Zero;
            return PdfiumNative.FPDFPage_GenerateContent(handle);
        }
        finally
        {
            if (textObject != IntPtr.Zero) PdfiumNative.FPDFPageObj_Destroy(textObject);
            PdfiumNative.FPDF_ClosePage(handle);
        }
    }

    private static (double X, double Y) DisplayPointToContent(double x, double y, PageModel page)
    {
        var width = page.UnrotatedWidth;
        var height = page.UnrotatedHeight;
        return page.Rotation switch
        {
            1 => (width - y, x),
            2 => (width - x, height - y),
            3 => (y, height - x),
            _ => (x, y)
        };
    }

    private static byte[] EncodeImage(BitmapSource bitmap, bool jpeg)
    {
        BitmapEncoder encoder = jpeg
            ? new JpegBitmapEncoder { QualityLevel = 90 }
            : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static string SanitizeFileName(string? name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string((name ?? "").Select(c => invalid.Contains(c) ? '-' : c).ToArray()).Trim();
        return clean.Length == 0 ? "pagina" : clean;
    }

    private static BitmapSource? RenderPageCore(IntPtr document, int index, double targetWidth)
    {
        var page = PdfiumNative.FPDF_LoadPage(document, index);
        if (page == IntPtr.Zero) return null;
        try
        {
            var widthPoints = PdfiumNative.FPDF_GetPageWidthF(page);
            var heightPoints = PdfiumNative.FPDF_GetPageHeightF(page);
            if (widthPoints <= 0 || heightPoints <= 0) return null;

            var scale = targetWidth / widthPoints;
            var pixelWidth = Math.Max(1, (int)Math.Round(widthPoints * scale));
            var pixelHeight = Math.Max(1, (int)Math.Round(heightPoints * scale));

            const int maxPixels = 6000;
            if (pixelWidth > maxPixels || pixelHeight > maxPixels)
            {
                var factor = Math.Min(maxPixels / (double)pixelWidth, maxPixels / (double)pixelHeight);
                pixelWidth = Math.Max(1, (int)(pixelWidth * factor));
                pixelHeight = Math.Max(1, (int)(pixelHeight * factor));
            }

            var bitmap = PdfiumNative.FPDFBitmap_CreateEx(pixelWidth, pixelHeight,
                PdfiumNative.FPDFBitmap_Bgra, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero) return null;

            try
            {
                PdfiumNative.FPDFBitmap_FillRect(bitmap, 0, 0, pixelWidth, pixelHeight, 0xFFFFFFFF);
                PdfiumNative.FPDF_RenderPageBitmap(bitmap, page, 0, 0, pixelWidth, pixelHeight, 0,
                    PdfiumNative.FPDF_ANNOT | PdfiumNative.FPDF_LCD_TEXT);

                var stride = PdfiumNative.FPDFBitmap_GetStride(bitmap);
                var buffer = PdfiumNative.FPDFBitmap_GetBuffer(bitmap);
                if (buffer == IntPtr.Zero) return null;

                var length = stride * pixelHeight;
                var pixels = new byte[length];
                Marshal.Copy(buffer, pixels, 0, length);
                var source = BitmapSource.Create(pixelWidth, pixelHeight, 96, 96,
                    PixelFormats.Bgra32, null, pixels, stride);
                source.Freeze();
                return source;
            }
            finally
            {
                PdfiumNative.FPDFBitmap_Destroy(bitmap);
            }
        }
        finally
        {
            PdfiumNative.FPDF_ClosePage(page);
        }
    }

    private static byte[] SaveDocumentToBytes(IntPtr document)
    {
        using var stream = new MemoryStream();
        PdfiumNative.WriteBlockDelegate callback = (_, data, size) =>
        {
            if (size > int.MaxValue) return 0;
            var chunk = new byte[(int)size];
            Marshal.Copy(data, chunk, 0, (int)size);
            stream.Write(chunk, 0, chunk.Length);
            return 1;
        };

        var write = new PdfiumNative.FPDF_FILEWRITE
        {
            Version = 1,
            WriteBlock = Marshal.GetFunctionPointerForDelegate(callback)
        };

        var pointer = Marshal.AllocHGlobal(Marshal.SizeOf<PdfiumNative.FPDF_FILEWRITE>());
        try
        {
            Marshal.StructureToPtr(write, pointer, false);
            if (!PdfiumNative.FPDF_SaveAsCopy(document, pointer, 2))
                throw new PdfException("No se pudo guardar el documento.");
            return stream.ToArray();
        }
        finally
        {
            Marshal.FreeHGlobal(pointer);
            GC.KeepAlive(callback);
        }
    }

    private static void WriteFileAtomic(string path, byte[] bytes)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory)) directory = Environment.CurrentDirectory;
        Directory.CreateDirectory(directory);

        var temp = Path.Combine(directory, "." + Path.GetFileName(fullPath) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, fullPath, true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try { File.Delete(temp); } catch { }
            }
        }
    }

    private static int ReadPdfVersion(byte[] bytes)
    {
        var length = Math.Min(bytes.Length, 1024);
        if (length < 8) return 0;

        var header = Encoding.ASCII.GetString(bytes, 0, length);
        var index = header.IndexOf("%PDF-", StringComparison.Ordinal);
        if (index < 0 || index + 8 > header.Length) return 0;

        var major = header[index + 5] - '0';
        var minor = header[index + 7] - '0';
        if (major is < 1 or > 9 || minor is < 0 or > 9) return 0;
        return major * 10 + minor;
    }

    private static string ReadUtf16Text(Func<IntPtr, uint, uint> read)
    {
        var length = read(IntPtr.Zero, 0);
        if (length <= 2) return "";
        var buffer = Marshal.AllocHGlobal((int)length);
        try
        {
            read(buffer, length);
            return (Marshal.PtrToStringUni(buffer) ?? "").Trim();
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    private static DocumentInfo ReadDocumentInfo(IntPtr document, byte[] bytes)
    {
        string Read(string tag) => ReadUtf16Text((buffer, length) =>
            PdfiumNative.FPDF_GetMetaText(document, tag, buffer, length));

        return new DocumentInfo
        {
            Title = Read("Title"),
            Author = Read("Author"),
            Subject = Read("Subject"),
            Keywords = Read("Keywords"),
            Creator = Read("Creator"),
            Producer = Read("Producer"),
            CreationDate = Read("CreationDate"),
            ModificationDate = Read("ModDate"),
            FileVersion = ReadPdfVersion(bytes),
            Permissions = PdfiumNative.FPDF_GetDocPermissions(document),
            SecurityHandlerRevision = PdfiumNative.FPDF_GetSecurityHandlerRevision(document)
        };
    }

    private static IReadOnlyList<BookmarkNode> ReadBookmarks(IntPtr document)
    {
        var first = PdfiumNative.FPDFBookmark_GetFirstChild(document, IntPtr.Zero);
        return ReadBookmarkSiblings(document, first, 0);
    }

    private static List<BookmarkNode> ReadBookmarkSiblings(IntPtr document, IntPtr first, int depth)
    {
        var result = new List<BookmarkNode>();
        if (depth > 32 || first == IntPtr.Zero) return result;

        for (var node = first;
             node != IntPtr.Zero && result.Count < 2000;
             node = PdfiumNative.FPDFBookmark_GetNextSibling(document, node))
        {
            var title = ReadUtf16Text((buffer, length) =>
                PdfiumNative.FPDFBookmark_GetTitle(node, buffer, length));
            var dest = PdfiumNative.FPDFBookmark_GetDest(document, node);
            var page = dest == IntPtr.Zero
                ? -1
                : PdfiumNative.FPDFDest_GetDestPageIndex(document, dest);
            var child = PdfiumNative.FPDFBookmark_GetFirstChild(document, node);

            result.Add(new BookmarkNode(
                string.IsNullOrWhiteSpace(title) ? "(sin título)" : title,
                page,
                ReadBookmarkSiblings(document, child, depth + 1)));
        }

        return result;
    }

    private static IReadOnlyList<AttachmentInfo> ReadAttachments(IntPtr document)
    {
        var count = PdfiumNative.FPDFDoc_GetAttachmentCount(document);
        if (count <= 0) return Array.Empty<AttachmentInfo>();

        var result = new List<AttachmentInfo>(count);
        for (var i = 0; i < count; i++)
        {
            var handle = PdfiumNative.FPDFDoc_GetAttachment(document, i);
            if (handle == IntPtr.Zero) continue;

            var name = ReadUtf16Text((buffer, length) =>
                PdfiumNative.FPDFAttachment_GetName(handle, buffer, length));
            long size = 0;
            if (PdfiumNative.FPDFAttachment_GetFile(handle, IntPtr.Zero, 0, out var outLength))
                size = outLength;

            result.Add(new AttachmentInfo(i,
                string.IsNullOrWhiteSpace(name) ? $"Adjunto {i + 1}" : name, size));
        }

        return result;
    }

    private static string ExtractPageText(IntPtr document, int index)
    {
        var page = PdfiumNative.FPDF_LoadPage(document, index);
        if (page == IntPtr.Zero) return "";
        var textPage = IntPtr.Zero;
        try
        {
            textPage = PdfiumNative.FPDFText_LoadPage(page);
            if (textPage == IntPtr.Zero) return "";

            var count = PdfiumNative.FPDFText_CountChars(textPage);
            if (count <= 0) return "";

            var buffer = Marshal.AllocHGlobal((count + 1) * 2);
            try
            {
                var written = PdfiumNative.FPDFText_GetText(textPage, 0, count, buffer);
                if (written <= 0) return "";
                var length = Math.Min(written - 1, count);
                return length <= 0 ? "" : Marshal.PtrToStringUni(buffer, length) ?? "";
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        finally
        {
            if (textPage != IntPtr.Zero) PdfiumNative.FPDFText_ClosePage(textPage);
            PdfiumNative.FPDF_ClosePage(page);
        }
    }

    private static PdfException CreateException(uint error, string? password)
    {
        return error switch
        {
            PdfiumNative.ErrorPassword => new PdfException(password == null
                ? "El documento está protegido con contraseña."
                : "La contraseña no es correcta."),
            PdfiumNative.ErrorFormat => new PdfException("El archivo no es un PDF válido o está dañado."),
            PdfiumNative.ErrorFile => new PdfException("No se pudo leer el archivo."),
            PdfiumNative.ErrorSecurity => new PdfException("El documento tiene una protección de seguridad no admitida."),
            _ => new PdfException("No se pudo abrir el PDF (código " + error + ").")
        };
    }

    private static Point RotateNormalizedPoint(Point point, int delta)
    {
        if (delta == 0) return point;

        var clockwise = delta > 0;
        var steps = Math.Abs(delta) % 4;
        var result = point;
        for (var i = 0; i < steps; i++)
            result = clockwise
                ? new Point(1 - result.Y, result.X)
                : new Point(result.Y, 1 - result.X);
        return result;
    }

    private static (double X, double Y) Unrotate(double nx, double ny, double width, double height, int rotation)
    {
        return rotation switch
        {
            1 => (ny * width, (1 - nx) * height),
            2 => ((1 - nx) * width, (1 - ny) * height),
            3 => ((1 - ny) * width, nx * height),
            _ => (nx * width, ny * height)
        };
    }

    private static Point NormalizeFromPdfPoint(double x, double y, double width, double height, int rotation)
    {
        var top = height - y;
        var (nx, ny) = rotation switch
        {
            1 => (1 - top / height, x / width),
            2 => (1 - x / width, 1 - top / height),
            3 => (top / height, 1 - x / width),
            _ => (x / width, top / height)
        };
        return new Point(Math.Clamp(nx, 0, 1), Math.Clamp(ny, 0, 1));
    }

    private static Rect PdfRectToNormalized(double left, double top, double right, double bottom,
        double width, double height, int rotation)
    {
        var p1 = NormalizeFromPdfPoint(left, top, width, height, rotation);
        var p2 = NormalizeFromPdfPoint(right, bottom, width, height, rotation);
        return new Rect(
            new Point(Math.Min(p1.X, p2.X), Math.Min(p1.Y, p2.Y)),
            new Point(Math.Max(p1.X, p2.X), Math.Max(p1.Y, p2.Y)));
    }

    private static Point ToPdfPoint(Point normalized, PageModel page)
    {
        var (x, top) = Unrotate(normalized.X, normalized.Y, page.UnrotatedWidth, page.UnrotatedHeight, page.Rotation);
        return new Point(x, page.UnrotatedHeight - top);
    }

    private static Rect NormalizedRectToPdf(Rect rect, PageModel page)
    {
        var a = ToPdfPoint(rect.TopLeft, page);
        var b = ToPdfPoint(rect.BottomRight, page);
        return new Rect(
            new Point(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
            new Point(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
    }

    private sealed record LoadedDocument(IntPtr Document, IntPtr Buffer, List<PageData> Pages);

    private sealed record PageData(Guid Id, double Width, double Height, int Rotation, string Label,
        List<AnnotationModel> Annotations);

    private readonly record struct EditAction(Action Undo, Action Redo);

    private readonly record struct PageSnapshot(Guid Id, int Index, double UnrotatedWidth,
        double UnrotatedHeight, int Rotation);

    private readonly record struct TextStamp(string Text, double FontSize, Color Color, byte Alpha,
        double Angle, StampPlacement Placement, double Margin, double FitWidthPercent);
}
