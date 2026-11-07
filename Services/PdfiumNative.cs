using System.Runtime.InteropServices;

namespace Mpdf.Services;

internal static class PdfiumNative
{
    public const string Dll = "pdfium.dll";

    public const int FPDF_ANNOT = 0x01;
    public const int FPDF_LCD_TEXT = 0x02;
    public const int FPDFBitmap_Bgra = 4;

    public const int FPDF_AnnotSquare = 5;
    public const int FPDF_AnnotHighlight = 9;
    public const int FPDF_AnnotUnderline = 10;
    public const int FPDF_AnnotStrikeout = 12;
    public const int FPDF_AnnotInk = 15;

    public const int FpdfAnnotColor = 0;
    public const int FpdfAnnotFlagPrint = 4;

    public const uint ErrorSuccess = 0;
    public const uint ErrorUnknown = 1;
    public const uint ErrorFile = 2;
    public const uint ErrorFormat = 3;
    public const uint ErrorPassword = 4;
    public const uint ErrorSecurity = 5;
    public const uint ErrorPage = 6;

    [StructLayout(LayoutKind.Sequential)]
    public struct FS_SIZEF
    {
        public float Width;
        public float Height;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FS_RECTF
    {
        public float Left;
        public float Top;
        public float Right;
        public float Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FS_POINTF
    {
        public float X;
        public float Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FS_QUADPOINTSF
    {
        public float X1;
        public float Y1;
        public float X2;
        public float Y2;
        public float X3;
        public float Y3;
        public float X4;
        public float Y4;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FPDF_FILEWRITE
    {
        public int Version;
        public IntPtr WriteBlock;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    public delegate int WriteBlockDelegate(IntPtr self, IntPtr data, uint size);

    [DllImport(Dll)] public static extern void FPDF_InitLibrary();
    [DllImport(Dll)] public static extern void FPDF_DestroyLibrary();
    [DllImport(Dll)] public static extern uint FPDF_GetLastError();

    [DllImport(Dll)]
    public static extern IntPtr FPDF_LoadMemDocument64(IntPtr data, nuint size,
        [MarshalAs(UnmanagedType.LPStr)] string? password);

    [DllImport(Dll)] public static extern void FPDF_CloseDocument(IntPtr document);
    [DllImport(Dll)] public static extern int FPDF_GetPageCount(IntPtr document);
    [DllImport(Dll)] public static extern IntPtr FPDF_LoadPage(IntPtr document, int index);
    [DllImport(Dll)] public static extern void FPDF_ClosePage(IntPtr page);
    [DllImport(Dll)] public static extern float FPDF_GetPageWidthF(IntPtr page);
    [DllImport(Dll)] public static extern float FPDF_GetPageHeightF(IntPtr page);
    [DllImport(Dll)] public static extern int FPDFPage_GetRotation(IntPtr page);
    [DllImport(Dll)] public static extern void FPDFPage_SetRotation(IntPtr page, int rotate);
    [DllImport(Dll)] public static extern void FPDFPage_Delete(IntPtr document, int index);
    [DllImport(Dll)]
    public static extern IntPtr FPDFPage_New(IntPtr document, int pageIndex, double width, double height);
    [DllImport(Dll)] public static extern bool FPDFPage_GenerateContent(IntPtr page);

    [DllImport(Dll)]
    public static extern bool FPDF_MovePages(IntPtr document, int[] pageIndices, uint count, int destination);

    [DllImport(Dll)]
    public static extern bool FPDF_ImportPages(IntPtr destination, IntPtr source,
        [MarshalAs(UnmanagedType.LPStr)] string? pageRange, int index);

    [DllImport(Dll)] public static extern IntPtr FPDF_CreateNewDocument();

    [DllImport(Dll)]
    public static extern IntPtr FPDFBitmap_CreateEx(int width, int height, int format,
        IntPtr firstScan, int stride);
    [DllImport(Dll)]
    public static extern bool FPDFBitmap_FillRect(IntPtr bitmap, int left, int top, int width, int height, uint color);
    [DllImport(Dll)] public static extern IntPtr FPDFBitmap_GetBuffer(IntPtr bitmap);
    [DllImport(Dll)] public static extern int FPDFBitmap_GetStride(IntPtr bitmap);
    [DllImport(Dll)] public static extern void FPDFBitmap_Destroy(IntPtr bitmap);

    [DllImport(Dll)]
    public static extern void FPDF_RenderPageBitmap(IntPtr bitmap, IntPtr page,
        int startX, int startY, int sizeX, int sizeY, int rotate, int flags);

    [DllImport(Dll)] public static extern bool FPDF_SaveAsCopy(IntPtr document, IntPtr fileWrite, uint flags);

    [DllImport(Dll)] public static extern uint FPDF_GetDocPermissions(IntPtr document);
    [DllImport(Dll)] public static extern int FPDF_GetSecurityHandlerRevision(IntPtr document);

    [DllImport(Dll)]
    public static extern uint FPDF_GetMetaText(IntPtr document,
        [MarshalAs(UnmanagedType.LPStr)] string tag, IntPtr buffer, uint buflen);

    [DllImport(Dll)]
    public static extern uint FPDF_GetPageLabel(IntPtr document, int pageIndex, IntPtr buffer, uint buflen);

    [DllImport(Dll)] public static extern IntPtr FPDFBookmark_GetFirstChild(IntPtr document, IntPtr bookmark);
    [DllImport(Dll)] public static extern IntPtr FPDFBookmark_GetNextSibling(IntPtr document, IntPtr bookmark);
    [DllImport(Dll)] public static extern uint FPDFBookmark_GetTitle(IntPtr bookmark, IntPtr buffer, uint buflen);
    [DllImport(Dll)] public static extern IntPtr FPDFBookmark_GetDest(IntPtr document, IntPtr bookmark);
    [DllImport(Dll)] public static extern int FPDFDest_GetDestPageIndex(IntPtr document, IntPtr dest);

    [DllImport(Dll)] public static extern int FPDFDoc_GetAttachmentCount(IntPtr document);
    [DllImport(Dll)] public static extern IntPtr FPDFDoc_GetAttachment(IntPtr document, int index);
    [DllImport(Dll)] public static extern uint FPDFAttachment_GetName(IntPtr attachment, IntPtr buffer, uint buflen);
    [DllImport(Dll)]
    public static extern bool FPDFAttachment_GetFile(IntPtr attachment, IntPtr buffer, uint buflen,
        out uint outBuflen);

    [DllImport(Dll)] public static extern IntPtr FPDFPage_CreateAnnot(IntPtr page, int subtype);
    [DllImport(Dll)] public static extern int FPDFPage_GetAnnotCount(IntPtr page);
    [DllImport(Dll)] public static extern IntPtr FPDFPage_GetAnnot(IntPtr page, int index);
    [DllImport(Dll)] public static extern void FPDFPage_CloseAnnot(IntPtr annot);
    [DllImport(Dll)] public static extern bool FPDFPage_RemoveAnnot(IntPtr page, int index);
    [DllImport(Dll)] public static extern int FPDFAnnot_GetSubtype(IntPtr annot);
    [DllImport(Dll)] public static extern bool FPDFAnnot_SetRect(IntPtr annot, ref FS_RECTF rect);
    [DllImport(Dll)] public static extern bool FPDFAnnot_GetRect(IntPtr annot, ref FS_RECTF rect);
    [DllImport(Dll)]
    public static extern bool FPDFAnnot_SetColor(IntPtr annot, int colorType, uint r, uint g, uint b, uint a);
    [DllImport(Dll)]
    public static extern bool FPDFAnnot_GetColor(IntPtr annot, int colorType,
        out uint r, out uint g, out uint b, out uint a);
    [DllImport(Dll)]
    public static extern bool FPDFAnnot_AppendAttachmentPoints(IntPtr annot, ref FS_QUADPOINTSF quadPoints);
    [DllImport(Dll)]
    public static extern int FPDFAnnot_AddInkStroke(IntPtr annot, FS_POINTF[] points, nuint count);
    [DllImport(Dll)] public static extern int FPDFAnnot_GetInkListCount(IntPtr annot);
    [DllImport(Dll)]
    public static extern uint FPDFAnnot_GetInkListPath(IntPtr annot, uint pathIndex,
        [Out] FS_POINTF[] buffer, uint length);
    [DllImport(Dll)]
    public static extern bool FPDFAnnot_SetBorder(IntPtr annot, float horizontalRadius,
        float verticalRadius, float borderWidth);
    [DllImport(Dll)]
    public static extern bool FPDFAnnot_GetBorder(IntPtr annot, out float horizontalRadius,
        out float verticalRadius, out float borderWidth);
    [DllImport(Dll)] public static extern bool FPDFAnnot_SetFlags(IntPtr annot, int flags);

    [DllImport(Dll)] public static extern IntPtr FPDFText_LoadPage(IntPtr page);
    [DllImport(Dll)] public static extern void FPDFText_ClosePage(IntPtr textPage);
    [DllImport(Dll)]
    public static extern IntPtr FPDFText_FindStart(IntPtr textPage,
        [MarshalAs(UnmanagedType.LPWStr)] string findWhat, uint flags, int startIndex);
    [DllImport(Dll)] public static extern bool FPDFText_FindNext(IntPtr handle);
    [DllImport(Dll)] public static extern bool FPDFText_FindPrev(IntPtr handle);
    [DllImport(Dll)] public static extern int FPDFText_GetSchResultIndex(IntPtr handle);
    [DllImport(Dll)] public static extern int FPDFText_GetSchCount(IntPtr handle);
    [DllImport(Dll)] public static extern int FPDFText_CountChars(IntPtr textPage);
    [DllImport(Dll)]
    public static extern int FPDFText_GetText(IntPtr textPage, int startIndex, int count, IntPtr result);

    [DllImport(Dll)] public static extern int FPDFText_CountRects(IntPtr textPage, int startIndex, int count);
    [DllImport(Dll)]
    public static extern bool FPDFText_GetRect(IntPtr textPage, int rectIndex,
        out double left, out double top, out double right, out double bottom);
    [DllImport(Dll)] public static extern void FPDFText_FindClose(IntPtr handle);

    [DllImport(Dll)]
    public static extern IntPtr FPDFText_LoadStandardFont(IntPtr document,
        [MarshalAs(UnmanagedType.LPStr)] string name);
    [DllImport(Dll)] public static extern void FPDFFont_Close(IntPtr font);
    [DllImport(Dll)]
    public static extern IntPtr FPDFPageObj_CreateTextObj(IntPtr document, IntPtr font, float fontSize);
    [DllImport(Dll)]
    public static extern bool FPDFText_SetText(IntPtr textObject,
        [MarshalAs(UnmanagedType.LPWStr)] string text);
    [DllImport(Dll)]
    public static extern bool FPDFPageObj_GetBounds(IntPtr pageObject, out float left,
        out float top, out float right, out float bottom);
    [DllImport(Dll)]
    public static extern void FPDFPageObj_SetFillColor(IntPtr pageObject, uint r, uint g, uint b, uint a);
    [DllImport(Dll)]
    public static extern void FPDFPageObj_Transform(IntPtr pageObject,
        double a, double b, double c, double d, double e, double f);
    [DllImport(Dll)] public static extern void FPDFPage_InsertObject(IntPtr page, IntPtr pageObject);
    [DllImport(Dll)] public static extern void FPDFPageObj_Destroy(IntPtr pageObject);
}
