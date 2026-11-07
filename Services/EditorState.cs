using System.Windows.Media;

namespace Mpdf.Services;

public enum EditorTool
{
    Select,
    Highlight,
    Underline,
    Strikeout,
    Pen,
    Rectangle,
    Eraser
}

public static class EditorState
{
    private static EditorTool _tool = EditorTool.Select;
    private static Color _penColor = Color.FromRgb(0xE1, 0x1D, 0x48);
    private static Color _highlightColor = Color.FromRgb(0xFF, 0xD5, 0x4F);

    public static event EventHandler? ToolChanged;

    public static EditorTool CurrentTool
    {
        get => _tool;
        set
        {
            if (_tool == value) return;
            _tool = value;
            ToolChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static Color PenColor
    {
        get => _penColor;
        set
        {
            if (_penColor == value) return;
            _penColor = value;
            ToolChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static Color HighlightColor
    {
        get => _highlightColor;
        set
        {
            if (_highlightColor == value) return;
            _highlightColor = value;
            ToolChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    public static double PenWidth { get; set; } = 2.2;

    public static bool IsDrawingTool => _tool is EditorTool.Highlight or EditorTool.Underline
        or EditorTool.Strikeout or EditorTool.Pen or EditorTool.Rectangle;
}
