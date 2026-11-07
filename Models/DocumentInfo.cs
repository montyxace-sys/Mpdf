namespace Mpdf.Models;

public sealed class DocumentInfo
{
    public string Title { get; init; } = "";
    public string Author { get; init; } = "";
    public string Subject { get; init; } = "";
    public string Keywords { get; init; } = "";
    public string Creator { get; init; } = "";
    public string Producer { get; init; } = "";
    public string CreationDate { get; init; } = "";
    public string ModificationDate { get; init; } = "";
    public int FileVersion { get; init; }
    public uint Permissions { get; init; }
    public int SecurityHandlerRevision { get; init; } = -1;

    public bool IsEncrypted => SecurityHandlerRevision >= 0;
    public bool CanPrint => (Permissions & 0x0004) != 0;
    public bool CanModify => (Permissions & 0x0008) != 0;
    public bool CanCopy => (Permissions & 0x0010) != 0;
    public bool CanAnnotate => (Permissions & 0x0020) != 0;
    public bool CanFillForms => (Permissions & 0x0100) != 0;
    public bool CanExtractForAccessibility => (Permissions & 0x0200) != 0;
    public bool CanAssemble => (Permissions & 0x0400) != 0;
    public bool CanPrintHighQuality => (Permissions & 0x0800) != 0;

    public string VersionText => FileVersion > 0 ? $"PDF {FileVersion / 10}.{FileVersion % 10}" : "PDF";
}

public sealed class BookmarkNode
{
    public BookmarkNode(string title, int pageIndex, IReadOnlyList<BookmarkNode> children)
    {
        Title = title;
        PageIndex = pageIndex;
        Children = children;
    }

    public string Title { get; }
    public int PageIndex { get; }
    public IReadOnlyList<BookmarkNode> Children { get; }
    public bool HasPage => PageIndex >= 0;
}

public sealed class AttachmentInfo
{
    public AttachmentInfo(int index, string name, long size)
    {
        Index = index;
        Name = name;
        Size = size;
    }

    public int Index { get; }
    public string Name { get; }
    public long Size { get; }

    public string SizeText => Size switch
    {
        < 1024 => $"{Size} B",
        < 1024 * 1024 => $"{Size / 1024.0:0.#} KB",
        _ => $"{Size / (1024.0 * 1024.0):0.#} MB"
    };
}
