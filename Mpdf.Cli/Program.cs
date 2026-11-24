using Mpdf.Core;

if (args.Length == 0)
{
    Console.WriteLine("Mpdf CLI 1.1.0 — utilidades PDF portables");
    Console.WriteLine();
    Console.WriteLine("  mpdf info <archivo.pdf>");
    Console.WriteLine("  mpdf render <archivo.pdf> <pagina> <dpi> <salida.png>");
    Console.WriteLine("  mpdf text <archivo.pdf> <salida.txt>");
    Console.WriteLine("  mpdf extract <archivo.pdf> <desde> <hasta> <salida.pdf>");
    Console.WriteLine("  mpdf merge <salida.pdf> <entrada1.pdf> [entrada2.pdf ...]");
    Console.WriteLine("  mpdf rotate <archivo.pdf> <pagina> <1|-1> <salida.pdf>");
    Console.WriteLine("  mpdf images <archivo.pdf> <directorio> [dpi] [png|jpg] [rango]");
    return 0;
}

try
{
    var command = args[0].ToLowerInvariant();
    switch (command)
    {
        case "info":
        {
            using var pdf = PortablePdf.Open(args[1]);
            Console.WriteLine($"Archivo: {args[1]}");
            Console.WriteLine($"Páginas: {pdf.PageCount}");
            Console.WriteLine($"Título: {pdf.GetMetadata("Title")}");
            Console.WriteLine($"Autor: {pdf.GetMetadata("Author")}");
            foreach (var p in pdf.Pages.Take(5))
                Console.WriteLine($"  Página {p.Label}: {p.WidthPoints:0}x{p.HeightPoints:0} pt, rotación {p.Rotation * 90}°");
            if (pdf.PageCount > 5) Console.WriteLine($"  ... y {pdf.PageCount - 5} páginas más");
            break;
        }
        case "render":
        {
            using var pdf = PortablePdf.Open(args[1]);
            var page = int.Parse(args[2]);
            var dpi = double.Parse(args[3]);
            File.WriteAllBytes(args[4], pdf.RenderPage(page - 1, dpi));
            Console.WriteLine($"Página {page} renderizada a {args[4]} ({dpi} ppp).");
            break;
        }
        case "text":
        {
            using var pdf = PortablePdf.Open(args[1]);
            File.WriteAllText(args[2], pdf.GetDocumentText());
            Console.WriteLine($"Texto extraído a {args[2]}.");
            break;
        }
        case "extract":
        {
            using var pdf = PortablePdf.Open(args[1]);
            File.WriteAllBytes(args[4], pdf.ExtractRange(int.Parse(args[2]), int.Parse(args[3])));
            Console.WriteLine($"Rango extraído a {args[4]}.");
            break;
        }
        case "merge":
        {
            var documents = args.Skip(2).Select(f => PortablePdf.Open(f)).ToList();
            try
            {
                File.WriteAllBytes(args[1], PortablePdf.Merge(documents));
                Console.WriteLine($"Combinados {documents.Count} PDFs en {args[1]}.");
            }
            finally
            {
                foreach (var doc in documents) doc.Dispose();
            }
            break;
        }
        case "rotate":
        {
            using var pdf = PortablePdf.Open(args[1]);
            pdf.RotatePage(int.Parse(args[2]) - 1, int.Parse(args[3]));
            pdf.SaveAs(args[4]);
            Console.WriteLine($"Página girada y guardada en {args[4]}.");
            break;
        }
        case "images":
        {
            using var pdf = PortablePdf.Open(args[1]);
            var directory = args[2];
            var dpi = args.Length > 3 ? double.Parse(args[3]) : 150;
            Directory.CreateDirectory(directory);
            var indices = args.Length > 5 ? ParseRange(args[5], pdf.PageCount) : Enumerable.Range(0, pdf.PageCount).ToList();
            foreach (var i in indices)
            {
                var bytes = pdf.RenderPage(i, dpi);
                File.WriteAllBytes(Path.Combine(directory, $"pagina-{i + 1:000}.png"), bytes);
            }
            Console.WriteLine($"{indices.Count} imágenes exportadas en {directory}.");
            break;
        }
        default:
            Console.Error.WriteLine($"Comando desconocido: {command}");
            return 2;
    }
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

static List<int> ParseRange(string range, int count)
{
    var result = new List<int>();
    foreach (var part in range.Split(',', StringSplitOptions.RemoveEmptyEntries))
    {
        var pieces = part.Split('-');
        if (pieces.Length == 2)
        {
            var from = int.Parse(pieces[0]);
            var to = int.Parse(pieces[1]);
            for (var i = from; i <= to; i++) if (i >= 1 && i <= count) result.Add(i - 1);
        }
        else if (pieces.Length == 1)
        {
            var page = int.Parse(pieces[0]);
            if (page >= 1 && page <= count) result.Add(page - 1);
        }
    }
    return result.Distinct().OrderBy(i => i).ToList();
}
