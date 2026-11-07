using System.Windows;

namespace Mpdf.Models;

public sealed record SearchHit(Guid PageId, int PageIndex, Rect Rect);
