using System.Runtime.InteropServices;
using SkiaSharp;

namespace Mpdf.Core;

public sealed class PdfException : Exception
{
    public PdfException(string message) : base(message) { }
}

public sealed class PdfPageInfo
{
    public int Index { get; init; }
    public double WidthPoints { get; init; }
    public double HeightPoints { get; init; }
    public int Rotation { get; init; }
    public string Label => (Index + 1).ToString();
}

/// <summary>PDFium portable: abre, muestra, extrae texto y guarda PDF sin depender de WPF.</summary>
public sealed class PortablePdf : IDisposable
{
    private static readonly object Gate = new();
    private static bool _initialized;

    private IntPtr _document;
    private byte[]? _buffer;
    private GCHandle _pin;
    private bool _disposed;

    private PortablePdf(IntPtr document, byte[] buffer, GCHandle pin)
    {
        _document = document;
        _buffer = buffer;
        _pin = pin;
    }

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_initialized) return;
            PdfiumNative.FPDF_InitLibrary();
            _initialized = true;
        }
    }

    public static PortablePdf Open(string path, string? password = null)
        => Open(File.ReadAllBytes(path), password);

    public static PortablePdf Open(byte[] data, string? password = null)
    {
        Initialize();
        if (data == null || data.Length == 0) throw new PdfException("El archivo está vacío.");

        var pin = GCHandle.Alloc(data, GCHandleType.Pinned);
        IntPtr doc;
        lock (Gate)
        {
            doc = PdfiumNative.FPDF_LoadMemDocument64(pin.AddrOfPinnedObject(), (nuint)data.Length, password);
        }
        if (doc == IntPtr.Zero)
        {
            pin.Free();
            var error = PdfiumNative.FPDF_GetLastError();
            throw new PdfException(error == PdfiumNative.ErrorPassword
                ? "El PDF está protegido con contraseña."
                : $"No se pudo abrir el PDF (error {error}).");
        }
        return new PortablePdf(doc, data, pin);
    }

    public int PageCount
    {
        get
        {
            EnsureOpen();
            lock (Gate) return PdfiumNative.FPDF_GetPageCount(_document);
        }
    }

    public IReadOnlyList<PdfPageInfo> Pages
    {
        get
        {
            var list = new List<PdfPageInfo>();
            for (var i = 0; i < PageCount; i++)
            {
                lock (Gate)
                {
                    var page = PdfiumNative.FPDF_LoadPage(_document, i);
                    if (page == IntPtr.Zero) continue;
                    try
                    {
                        list.Add(new PdfPageInfo
                        {
                            Index = i,
                            WidthPoints = PdfiumNative.FPDF_GetPageWidthF(page),
                            HeightPoints = PdfiumNative.FPDF_GetPageHeightF(page),
                            Rotation = PdfiumNative.FPDFPage_GetRotation(page)
                        });
                    }
                    finally
                    {
                        PdfiumNative.FPDF_ClosePage(page);
                    }
                }
            }
            return list;
        }
    }

    public string GetMetadata(string tag)
    {
        EnsureOpen();
        lock (Gate)
        {
            var size = PdfiumNative.FPDF_GetMetaText(_document, tag, IntPtr.Zero, 0);
            if (size <= 2) return string.Empty;
            var buffer = Marshal.AllocHGlobal((int)size * 2);
            try
            {
                PdfiumNative.FPDF_GetMetaText(_document, tag, buffer, size);
                return Marshal.PtrToStringUni(buffer)?.TrimEnd('\0') ?? string.Empty;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    public byte[] RenderPage(int index, double dpi = 150)
    {
        EnsureOpen();
        var pages = Pages;
        if (index < 0 || index >= pages.Count) throw new PdfException("Número de página fuera de rango.");
        dpi = Math.Clamp(dpi, 36, 600);

        lock (Gate)
        {
            var page = PdfiumNative.FPDF_LoadPage(_document, index);
            if (page == IntPtr.Zero) throw new PdfException("No se pudo cargar la página.");
            try
            {
                var widthPoints = PdfiumNative.FPDF_GetPageWidthF(page);
                var heightPoints = PdfiumNative.FPDF_GetPageHeightF(page);
                var scale = dpi / 72.0;
                var width = Math.Max(1, (int)Math.Round(widthPoints * scale));
                var height = Math.Max(1, (int)Math.Round(heightPoints * scale));
                const int maxPixels = 6000;
                if (width > maxPixels || height > maxPixels)
                {
                    var factor = Math.Min(maxPixels / (double)width, maxPixels / (double)height);
                    width = Math.Max(1, (int)(width * factor));
                    height = Math.Max(1, (int)(height * factor));
                }

                var bitmap = PdfiumNative.FPDFBitmap_CreateEx(width, height,
                    PdfiumNative.FPDFBitmap_Bgra, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero) throw new PdfException("No se pudo crear el bitmap.");
                try
                {
                    PdfiumNative.FPDFBitmap_FillRect(bitmap, 0, 0, width, height, 0xFFFFFFFF);
                    PdfiumNative.FPDF_RenderPageBitmap(bitmap, page, 0, 0, width, height, 0,
                        PdfiumNative.FPDF_ANNOT | PdfiumNative.FPDF_LCD_TEXT);

                    var stride = PdfiumNative.FPDFBitmap_GetStride(bitmap);
                    var source = PdfiumNative.FPDFBitmap_GetBuffer(bitmap);
                    if (source == IntPtr.Zero) throw new PdfException("No se pudo leer el bitmap.");

                    var pixels = new byte[stride * height];
                    Marshal.Copy(source, pixels, 0, pixels.Length);
                    using var skBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
                    Marshal.Copy(pixels, 0, skBitmap.GetPixels(), pixels.Length);
                    using var image = SKImage.FromBitmap(skBitmap);
                    using var data = image.Encode(SKEncodedImageFormat.Png, 100);
                    return data.ToArray();
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
    }

    public string GetPageText(int index)
    {
        EnsureOpen();
        lock (Gate)
        {
            var page = PdfiumNative.FPDF_LoadPage(_document, index);
            if (page == IntPtr.Zero) return string.Empty;
            var textPage = PdfiumNative.FPDFText_LoadPage(page);
            if (textPage == IntPtr.Zero)
            {
                PdfiumNative.FPDF_ClosePage(page);
                return string.Empty;
            }
            try
            {
                var count = PdfiumNative.FPDFText_CountChars(textPage);
                if (count <= 0) return string.Empty;
                var buffer = Marshal.AllocHGlobal((count + 1) * 2);
                try
                {
                    PdfiumNative.FPDFText_GetText(textPage, 0, count, buffer);
                    return Marshal.PtrToStringUni(buffer)?.TrimEnd('\0') ?? string.Empty;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            finally
            {
                PdfiumNative.FPDFText_ClosePage(textPage);
                PdfiumNative.FPDF_ClosePage(page);
            }
        }
    }

    public string GetDocumentText()
    {
        var text = new List<string>();
        for (var i = 0; i < PageCount; i++)
        {
            var pageText = GetPageText(i);
            if (!string.IsNullOrWhiteSpace(pageText)) text.Add(pageText);
        }
        return string.Join(Environment.NewLine + Environment.NewLine, text);
    }

    public void RotatePage(int index, int delta)
    {
        EnsureOpen();
        lock (Gate)
        {
            var page = PdfiumNative.FPDF_LoadPage(_document, index);
            if (page == IntPtr.Zero) throw new PdfException("No se pudo cargar la página.");
            try
            {
                var rotation = PdfiumNative.FPDFPage_GetRotation(page);
                PdfiumNative.FPDFPage_SetRotation(page, ((rotation + delta) % 4 + 4) % 4);
            }
            finally
            {
                PdfiumNative.FPDF_ClosePage(page);
            }
        }
    }

    public byte[] SaveToBytes()
    {
        EnsureOpen();
        lock (Gate)
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
                if (!PdfiumNative.FPDF_SaveAsCopy(_document, pointer, 2))
                    throw new PdfException("No se pudo guardar el documento.");
                return stream.ToArray();
            }
            finally
            {
                Marshal.FreeHGlobal(pointer);
                GC.KeepAlive(callback);
            }
        }
    }

    public void SaveAs(string path) => File.WriteAllBytes(path, SaveToBytes());

    public byte[] ExtractRange(int first, int last)
    {
        EnsureOpen();
        if (first < 1 || last < first || last > PageCount)
            throw new PdfException("Rango de páginas no válido.");

        Initialize();
        lock (Gate)
        {
            var target = PdfiumNative.FPDF_CreateNewDocument();
            if (target == IntPtr.Zero) throw new PdfException("No se pudo crear el documento.");
            try
            {
                if (!PdfiumNative.FPDF_ImportPages(target, _document, $"{first}-{last}", 0))
                    throw new PdfException("No se pudo extraer el rango.");
                return SaveFrom(target);
            }
            finally
            {
                PdfiumNative.FPDF_CloseDocument(target);
            }
        }
    }

    public static byte[] Merge(IEnumerable<PortablePdf> documents)
    {
        Initialize();
        var list = documents.ToList();
        if (list.Count == 0) throw new PdfException("No hay documentos para combinar.");
        lock (Gate)
        {
            var target = PdfiumNative.FPDF_CreateNewDocument();
            if (target == IntPtr.Zero) throw new PdfException("No se pudo crear el documento.");
            try
            {
                var insertAt = PdfiumNative.FPDF_GetPageCount(target);
                foreach (var doc in list)
                {
                    if (!PdfiumNative.FPDF_ImportPages(target, doc._document, null, insertAt))
                        throw new PdfException("No se pudo combinar los documentos.");
                    insertAt = PdfiumNative.FPDF_GetPageCount(target);
                }
                return SaveFrom(target);
            }
            finally
            {
                PdfiumNative.FPDF_CloseDocument(target);
            }
        }
    }

    private static byte[] SaveFrom(IntPtr document)
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

    private void EnsureOpen()
    {
        if (_disposed || _document == IntPtr.Zero) throw new ObjectDisposedException(nameof(PortablePdf));
    }

    public void Dispose()
    {
        if (_disposed) return;
        lock (Gate)
        {
            if (_document != IntPtr.Zero) PdfiumNative.FPDF_CloseDocument(_document);
        }
        _document = IntPtr.Zero;
        if (_pin.IsAllocated) _pin.Free();
        _buffer = null;
        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
