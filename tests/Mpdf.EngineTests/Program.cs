using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Mpdf.Models;
using Mpdf.Services;

namespace Mpdf.EngineTests;

internal static class Program
{
    private static int _checks;
    private static int _failures;
    private static string _testDir = "";

    [STAThread]
    private static async Task<int> Main()
    {
        _testDir = Path.Combine(FindProjectRoot(AppContext.BaseDirectory), "test");
        var outDir = Path.Combine(_testDir, "out");
        Directory.CreateDirectory(outDir);

        var samplePath = Path.Combine(_testDir, "sample.pdf");
        var mergeAPath = Path.Combine(_testDir, "merge-a.pdf");
        var mergeBPath = Path.Combine(_testDir, "merge-b.pdf");
        var richPath = Path.Combine(_testDir, "rich.pdf");

        try
        {
            await TestOpenAndRender(samplePath, outDir);
            await TestSearch(samplePath);
            await TestAnnotationRoundTrip(samplePath, outDir);
            await TestRotation(samplePath, outDir);
            await TestMerge(samplePath, mergeAPath, mergeBPath, outDir);
            await TestMoveDeleteAndZoom(samplePath);
            await TestUndoRedo(samplePath);
            await TestInsertAndSplit(samplePath, mergeAPath, outDir);
            await TestRotateAll(samplePath);
            await TestStamps(samplePath, outDir);
            await TestImageExport(samplePath, outDir);
            await TestRichDocument(richPath, outDir);
        }
        catch (Exception ex)
        {
            Console.WriteLine("ERROR NO CONTROLADO: " + ex);
            _failures++;
        }

        Console.WriteLine();
        Console.WriteLine($"{_checks} comprobaciones, {_failures} fallos.");
        return _failures == 0 ? 0 : 1;
    }

    private static async Task TestOpenAndRender(string samplePath, string outDir)
    {
        Console.WriteLine("== Abrir y renderizar ==");
        using var session = await DocumentSession.OpenAsync(samplePath);

        Check(session.HasDocument, "El documento está abierto");
        Check(session.Pages.Count == 4, $"4 páginas (obtenidas {session.Pages.Count})");
        CheckClose(session.Pages[0].UnrotatedWidth, 595, "Ancho de la página 1");
        CheckClose(session.Pages[0].UnrotatedHeight, 842, "Alto de la página 1");
        CheckClose(session.Pages[3].UnrotatedWidth, 420, "Ancho de la página 4");
        Check(session.Pages[2].Rotation == 1, $"La página 3 tiene rotación 90 (valor {session.Pages[2].Rotation})");
        CheckClose(session.Pages[2].DisplayWidthPoints, 842, "Ancho mostrado de la página girada");
        CheckClose(session.Pages[2].DisplayHeightPoints, 595, "Alto mostrado de la página girada");
        Check(!session.IsDirty, "Un documento recién abierto no está modificado");

        var bitmap = await session.RenderPageAsync(session.Pages[0].Id, 800);
        Check(bitmap != null, "La página 1 se renderiza");
        if (bitmap != null)
        {
            Check(bitmap.PixelWidth is > 780 and < 820, $"Ancho renderizado {bitmap.PixelWidth}px");
            Check(bitmap.PixelHeight > bitmap.PixelWidth, "La página renderizada es vertical");
            SavePng(bitmap, Path.Combine(outDir, "render-page1.png"));
        }

        var rotatedBitmap = await session.RenderPageAsync(session.Pages[2].Id, 800);
        Check(rotatedBitmap != null, "La página girada se renderiza");
        if (rotatedBitmap != null)
        {
            Check(rotatedBitmap.PixelWidth > rotatedBitmap.PixelHeight, "La página girada se renderiza apaisada");
            SavePng(rotatedBitmap, Path.Combine(outDir, "render-page3-rotated.png"));
        }
    }

    private static async Task TestSearch(string samplePath)
    {
        Console.WriteLine("== Búsqueda ==");
        using var session = await DocumentSession.OpenAsync(samplePath);

        var hits = await session.SearchAsync("Mpdf");
        Check(hits.Count >= 3, $"Se encuentran coincidencias de «Mpdf» (obtenidas {hits.Count})");
        Check(session.CurrentSearchHit != null, "Hay una coincidencia actual");
        Check(hits.All(h => h.Rect.Width >= 0 && h.Rect.Right <= 1.001 && h.Rect.Bottom <= 1.001),
            "Las coincidencias están dentro de la página normalizada");
        Check(hits.Select(h => h.PageId).Distinct().Count() >= 2, "Hay coincidencias en varias páginas");

        var before = session.SearchIndex;
        session.NextSearchHit();
        Check(session.SearchIndex == (before + 1) % session.SearchHits.Count, "Siguiente coincidencia");
        session.PreviousSearchHit();
        Check(session.SearchIndex == before, "Coincidencia anterior");

        var rotatedHits = await session.SearchAsync("rotacion");
        Check(rotatedHits.Count > 0, "Se busca texto en una página girada");
        if (rotatedHits.Count > 0)
        {
            var rect = rotatedHits[0].Rect;
            Check(rect.Width > 0 && rect.Height > 0 && rect.Right <= 1.001 && rect.Bottom <= 1.001,
                "La coincidencia girada está dentro de la página");
        }

        var empty = await session.SearchAsync("   ");
        Check(empty.Count == 0, "Una búsqueda vacía no devuelve resultados");
    }

    private static async Task TestAnnotationRoundTrip(string samplePath, string outDir)
    {
        Console.WriteLine("== Anotaciones y guardado ==");
        var annotatedPath = Path.Combine(outDir, "annotated.pdf");
        var resavedPath = Path.Combine(outDir, "annotated-resaved.pdf");

        using (var session = await DocumentSession.OpenAsync(samplePath))
        {
            var page = session.Pages[0];
            session.AddAnnotation(page, Area(AnnotationKind.Highlight, Color.FromRgb(0xE1, 0x1D, 0x48),
                0.10, 0.10, 0.60, 0.16));
            session.AddAnnotation(page, Area(AnnotationKind.Underline, Color.FromRgb(0x25, 0x63, 0xEB),
                0.10, 0.20, 0.55, 0.24));
            session.AddAnnotation(page, Area(AnnotationKind.Strikeout, Color.FromRgb(0x05, 0x96, 0x69),
                0.12, 0.30, 0.52, 0.34));
            session.AddAnnotation(page, Area(AnnotationKind.Rectangle, Color.FromRgb(0x11, 0x18, 0x27),
                0.20, 0.40, 0.70, 0.55));

            var ink = new AnnotationModel
            {
                Kind = AnnotationKind.Ink,
                Color = Color.FromRgb(0xE1, 0x1D, 0x48),
                StrokeWidth = 2.2
            };
            ink.Points.Add(new Point(0.15, 0.70));
            ink.Points.Add(new Point(0.30, 0.74));
            ink.Points.Add(new Point(0.45, 0.68));
            ink.Points.Add(new Point(0.60, 0.73));
            session.AddAnnotation(page, ink);

            Check(session.IsDirty, "Añadir marcas marca el documento como modificado");
            Check(session.Pages[0].Annotations.Count == 5, "Hay 5 marcas en la página 1");
            Check(session.CanUndo, "La marca se puede deshacer");

            await session.SaveAsync(annotatedPath);
            Check(!session.IsDirty, "Tras guardar, el documento queda limpio");
            Check(File.Exists(annotatedPath), "Se ha creado el PDF anotado");

            session.Undo();
            Check(session.Pages[0].Annotations.Count == 4, "Deshacer quita la última marca");
            session.Undo();
            Check(session.Pages[0].Annotations.Count == 3, "Deshacer quita la penúltima marca");
        }

        using (var reopened = await DocumentSession.OpenAsync(annotatedPath))
        {
            var annotations = reopened.Pages[0].Annotations;
            Check(annotations.Count == 5, $"Al reabrir se recuperan las 5 marcas (obtenidas {annotations.Count})");
            Check(annotations.Count(a => a.Kind == AnnotationKind.Highlight) == 1, "Se recupera el resaltado");
            Check(annotations.Count(a => a.Kind == AnnotationKind.Underline) == 1, "Se recupera el subrayado");
            Check(annotations.Count(a => a.Kind == AnnotationKind.Strikeout) == 1, "Se recupera el tachado");
            Check(annotations.Count(a => a.Kind == AnnotationKind.Rectangle) == 1, "Se recupera el rectángulo");
            Check(annotations.Count(a => a.Kind == AnnotationKind.Ink) == 1, "Se recupera el trazo de lápiz");

            var highlight = annotations.First(a => a.Kind == AnnotationKind.Highlight);
            Check(highlight.Color.R > 200 && highlight.Color.G < 80, "El color del resaltado se conserva");
            Check(highlight.Bounds.Left is > 0.08 and < 0.13 && highlight.Bounds.Right is > 0.57 and < 0.63,
                $"Los límites del resaltado se conservan ({highlight.Bounds})");

            var ink = annotations.First(a => a.Kind == AnnotationKind.Ink);
            Check(ink.Points.Count >= 4, $"El trazo conserva sus puntos ({ink.Points.Count})");

            await reopened.SaveAsync(resavedPath);
        }

        using (var resaved = await DocumentSession.OpenAsync(resavedPath))
        {
            Check(resaved.Pages[0].Annotations.Count == 5,
                $"Guardar dos veces no duplica marcas (obtenidas {resaved.Pages[0].Annotations.Count})");
        }
    }

    private static async Task TestRotation(string samplePath, string outDir)
    {
        Console.WriteLine("== Rotación ==");
        var rotatedPath = Path.Combine(outDir, "rotated.pdf");

        using (var session = await DocumentSession.OpenAsync(samplePath))
        {
            var page = session.Pages[0];
            session.AddAnnotation(page, Area(AnnotationKind.Highlight, Colors.Yellow, 0.10, 0.10, 0.40, 0.16));
            var before = page.Annotations[0].Bounds;

            Check(session.RotatePage(page, 1), "Girar la página a la derecha");
            Check(page.Rotation == 1, $"La rotación pasa a 90 (valor {page.Rotation})");
            CheckClose(page.DisplayWidthPoints, 842, "La página girada es apaisada");

            var after = page.Annotations[0].Bounds;
            Check(after.Width > 0.02 && after.Height > 0.2,
                $"La marca se transforma al girar ({before} -> {after})");
            Check(after.Left >= 0 && after.Top >= 0 && after.Right <= 1.001 && after.Bottom <= 1.001,
                "La marca sigue dentro de la página tras girar");

            Check(session.RotatePage(page, 3), "Girar tres veces más (vuelta completa)");
            Check(page.Rotation == 0, "La rotación vuelve a 0");

            Check(session.RotatePage(page, 1), "Volver a girar a 90 para guardar");
            await session.SaveAsync(rotatedPath);
        }

        using (var reopened = await DocumentSession.OpenAsync(rotatedPath))
        {
            Check(reopened.Pages[0].Rotation == 1,
                $"La rotación se conserva al reabrir (valor {reopened.Pages[0].Rotation})");
            Check(reopened.Pages[0].Annotations.Count == 1, "La marca sobrevive al guardado con rotación");
            var bounds = reopened.Pages[0].Annotations[0].Bounds;
            Check(bounds.Left > 0.7 && bounds.Top < 0.2 && bounds.Height > 0.2 && bounds.Right <= 1.001,
                $"La marca recuperada está bien situada ({bounds})");
        }
    }

    private static async Task TestMerge(string samplePath, string mergeAPath, string mergeBPath, string outDir)
    {
        Console.WriteLine("== Combinar y extraer ==");
        var mergedPath = Path.Combine(outDir, "merged.pdf");
        var extractedPath = Path.Combine(outDir, "extracted.pdf");

        using (var session = await DocumentSession.OpenAsync(samplePath))
        {
            var addedA = await session.AppendDocumentAsync(await File.ReadAllBytesAsync(mergeAPath));
            Check(addedA.Count == 2, $"Se añaden 2 páginas del documento A ({addedA.Count})");
            var addedB = await session.AppendDocumentAsync(await File.ReadAllBytesAsync(mergeBPath));
            Check(addedB.Count == 1, $"Se añade 1 página del documento B ({addedB.Count})");
            Check(session.Pages.Count == 7, $"El documento combinado tiene 7 páginas ({session.Pages.Count})");
            Check(session.IsDirty, "Combinar marca el documento como modificado");

            await session.SaveAsync(mergedPath);
        }

        using (var merged = await DocumentSession.OpenAsync(mergedPath))
        {
            Check(merged.Pages.Count == 7, $"El PDF combinado se reabre con 7 páginas ({merged.Pages.Count})");
            await merged.ExportPageAsync(merged.Pages[4], extractedPath);
        }

        using (var extracted = await DocumentSession.OpenAsync(extractedPath))
        {
            Check(extracted.Pages.Count == 1, $"La página extraída forma un PDF de 1 página ({extracted.Pages.Count})");
            CheckClose(extracted.Pages[0].UnrotatedWidth, 595, "La página extraída conserva su tamaño");
        }
    }

    private static async Task TestMoveDeleteAndZoom(string samplePath)
    {
        Console.WriteLine("== Mover, eliminar y zoom ==");
        using var session = await DocumentSession.OpenAsync(samplePath);

        var first = session.Pages[0].Id;
        var second = session.Pages[1].Id;
        Check(session.MovePage(session.Pages[0], 1), "Mover la primera página hacia abajo");
        Check(session.Pages[0].Id == second && session.Pages[1].Id == first, "El orden de páginas cambia");
        Check(session.Pages[0].Number == 1 && session.Pages[1].Number == 2, "La numeración se actualiza");

        var hits = await session.SearchAsync("Busqueda");
        Check(hits.Count > 0 && hits[0].PageIndex == 0, "La búsqueda usa el orden nuevo");

        var beforeZoom = session.Pages[0].DisplayWidth;
        session.SetZoom(2.0);
        CheckClose(session.Pages[0].DisplayWidth, beforeZoom * 2, "El zoom duplica el tamaño mostrado");
        var zoomed = await session.RenderPageAsync(session.Pages[0].Id, session.Pages[0].DisplayWidth);
        Check(zoomed != null && zoomed.PixelWidth > 1500, "La página se renderiza con el nuevo zoom");

        session.SetZoom(1.0);
        Check(session.DeletePage(session.Pages[^1]), "Eliminar la última página");
        Check(session.Pages.Count == 3, $"Quedan 3 páginas ({session.Pages.Count})");
        Check(session.Pages[^1].Number == 3, "La numeración se actualiza tras eliminar");
        Check(!session.DeletePage(session.Pages[0]) || session.Pages.Count >= 1,
            "Nunca se puede quedar sin páginas");

        session.ClearAnnotations(session.Pages[0]);
        Check(session.Pages[0].Annotations.Count == 0, "Se pueden quitar todas las marcas de una página");
    }

    private static async Task TestUndoRedo(string samplePath)
    {
        Console.WriteLine("== Deshacer y rehacer ==");
        using var session = await DocumentSession.OpenAsync(samplePath);
        var page = session.Pages[0];

        Check(!session.CanUndo && !session.CanRedo, "Un documento nuevo no tiene historial");
        session.AddAnnotation(page, Area(AnnotationKind.Highlight, Colors.Yellow, 0.1, 0.1, 0.4, 0.2));
        Check(session.CanUndo && !session.CanRedo, "Añadir una marca permite deshacer");

        session.Undo();
        Check(page.Annotations.Count == 0 && session.CanRedo, "Deshacer quita la marca y permite rehacer");

        session.Redo();
        Check(page.Annotations.Count == 1 && !session.CanRedo, "Rehacer recupera la marca");

        session.RemoveAnnotation(page, page.Annotations[0]);
        Check(page.Annotations.Count == 0, "La marca se elimina");
        session.Undo();
        Check(page.Annotations.Count == 1, "Deshacer recupera la marca eliminada");

        session.AddAnnotation(page, Area(AnnotationKind.Rectangle, Colors.Red, 0.2, 0.2, 0.5, 0.5));
        Check(!session.CanRedo, "Una edición nueva borra la pila de rehacer");
    }

    private static async Task TestInsertAndSplit(string samplePath, string mergeAPath, string outDir)
    {
        Console.WriteLine("== Insertar páginas y dividir ==");
        using var session = await DocumentSession.OpenAsync(samplePath);

        var blank = session.InsertBlankPage(1, 500, 700);
        Check(session.Pages.Count == 5, $"Insertar una página en blanco deja 5 páginas ({session.Pages.Count})");
        Check(blank.Index == 1 && session.Pages[1].Id == blank.Id, "La página en blanco queda en la posición elegida");
        CheckClose(blank.UnrotatedWidth, 500, "Ancho de la página en blanco");
        CheckClose(blank.UnrotatedHeight, 700, "Alto de la página en blanco");
        Check(!session.CanUndo, "Las operaciones estructurales reinician el historial");

        var blankBitmap = await session.RenderPageAsync(blank.Id, 300);
        Check(blankBitmap != null, "La página en blanco se renderiza");

        var added = await session.InsertDocumentAsync(await File.ReadAllBytesAsync(mergeAPath), 2);
        Check(added.Count == 2, $"Se insertan 2 páginas en medio ({added.Count})");
        Check(session.Pages.Count == 7, $"El documento tiene 7 páginas ({session.Pages.Count})");
        Check(session.Pages[2].Id == added[0].Id && session.Pages[3].Id == added[1].Id,
            "Las páginas importadas ocupan la posición solicitada");
        Check(session.Pages[4].Index == 4 && session.Pages[4].Number == 5, "La numeración se actualiza tras insertar");

        var rangePath = Path.Combine(outDir, "range.pdf");
        await session.ExportRangeAsync("1,3-4", rangePath);
        using (var range = await DocumentSession.OpenAsync(rangePath))
        {
            Check(range.Pages.Count == 3, $"El rango 1,3-4 exporta 3 páginas ({range.Pages.Count})");
        }

        var splitDir = Path.Combine(outDir, "split");
        Directory.CreateDirectory(splitDir);
        foreach (var file in Directory.GetFiles(splitDir, "*.pdf")) File.Delete(file);

        var parts = await session.SplitAsync(splitDir, "parte", 3);
        Check(parts.Count == 3, $"7 páginas en grupos de 3 generan 3 archivos ({parts.Count})");

        var expected = new[] { 3, 3, 1 };
        for (var i = 0; i < parts.Count; i++)
        {
            Check(Path.GetFileName(parts[i]) == $"parte-{i + 1:00}.pdf",
                $"El archivo dividido se llama parte-{i + 1:00}.pdf");
            using var part = await DocumentSession.OpenAsync(parts[i]);
            Check(part.Pages.Count == expected[i],
                $"La parte {i + 1} tiene {expected[i]} páginas ({part.Pages.Count})");
        }
    }

    private static async Task TestRotateAll(string samplePath)
    {
        Console.WriteLine("== Rotación global ==");
        using var session = await DocumentSession.OpenAsync(samplePath);
        Check(session.Pages[2].Rotation == 1, "La página 3 parte con rotación 90");

        session.RotateAllPages(1);
        Check(session.Pages[0].Rotation == 1 && session.Pages[2].Rotation == 2
              && session.Pages[3].Rotation == 1,
            "Girar todo 90° suma una rotación a cada página");
        CheckClose(session.Pages[0].DisplayWidthPoints, 842, "La primera página pasa a apaisada");

        session.RotateAllPages(-1);
        Check(session.Pages[0].Rotation == 0 && session.Pages[2].Rotation == 1,
            "Girar todo en sentido contrario restaura las rotaciones");
        Check(session.IsDirty, "La rotación global marca el documento como modificado");
    }

    private static async Task TestStamps(string samplePath, string outDir)
    {
        Console.WriteLine("== Numeración y marca de agua ==");
        var stampedPath = Path.Combine(outDir, "stamped.pdf");

        using (var session = await DocumentSession.OpenAsync(samplePath))
        {
            var blank = session.InsertBlankPage(session.Pages.Count, 595, 842);

            await session.AddPageNumbersAsync(new PageNumberOptions("Pag. {n}", 1, 12,
                Color.FromRgb(0x44, 0x44, 0x44), StampPlacement.BottomCenter, 24));
            Check(session.IsDirty, "La numeración marca el documento como modificado");

            await session.AddWatermarkAsync(new WatermarkOptions("BORRADOR", 70, 0.25, 45,
                Color.FromRgb(0xB0, 0x1C, 0x1C)));
            Check(session.IsDirty, "La marca de agua marca el documento como modificado");
            Check(!session.CanUndo, "Las estampas reinician el historial");

            var blankBitmap = await session.RenderPageAsync(blank.Id, 700);
            Check(blankBitmap != null, "La página en blanco se renderiza con las estampas");
            if (blankBitmap != null)
            {
                SavePng(blankBitmap, Path.Combine(outDir, "stamped-blank.png"));
                var (dark, red) = CountInk(blankBitmap);
                Check(dark > 100, $"La numeración se dibuja en la página ({dark} px oscuros)");
                Check(red > 100, $"La marca de agua se dibuja con su color ({red} px rojizos)");
            }

            var rotatedBitmap = await session.RenderPageAsync(session.Pages[2].Id, 700);
            if (rotatedBitmap != null)
                SavePng(rotatedBitmap, Path.Combine(outDir, "stamped-rotated.png"));

            await session.SaveAsync(stampedPath);
            await session.SaveAsync(stampedPath);
        }

        using (var reopened = await DocumentSession.OpenAsync(stampedPath))
        {
            Check(reopened.Pages.Count == 5, $"El PDF estampado conserva sus páginas ({reopened.Pages.Count})");

            var text = await reopened.GetDocumentTextAsync();
            Check(text.Contains("BORRADOR"), "La marca de agua se conserva al reabrir");
            Check(text.Contains("Pag. 1") && text.Contains("Pag. 5"),
                "La numeración se conserva al reabrir");

            var occurrences = CountOccurrences(text, "BORRADOR");
            Check(occurrences == 5, $"La marca aparece una vez por página ({occurrences})");

            var hits = await reopened.SearchAsync("BORRADOR");
            Check(hits.Select(h => h.PageIndex).Distinct().Count() == 5,
                "El buscador localiza la marca en las 5 páginas");

            var original = await reopened.SearchAsync("Mpdf");
            Check(original.Count > 0, "El contenido original sigue siendo buscable tras estampar");

            var rotatedText = await reopened.GetPageTextAsync(reopened.Pages[2]);
            Check(rotatedText.Contains("BORRADOR") && rotatedText.Contains("Pag. 3"),
                "La página girada recibe numeración y marca de agua");
        }
    }

    private static async Task TestImageExport(string samplePath, string outDir)
    {
        Console.WriteLine("== Exportar imágenes ==");
        var imagesDir = Path.Combine(outDir, "images");
        Directory.CreateDirectory(imagesDir);
        foreach (var file in Directory.GetFiles(imagesDir)) File.Delete(file);

        using var session = await DocumentSession.OpenAsync(samplePath);
        var paths = await session.ExportImagesAsync(new ImageExportOptions(
            imagesDir, "pagina", "png", 144, "1-2,4"));
        Check(paths.Count == 3, $"El rango 1-2,4 exporta 3 imágenes ({paths.Count})");
        Check(paths.All(File.Exists), "Todos los archivos de imagen existen");
        Check(Path.GetFileName(paths[0]) == "pagina-001.png",
            $"El nombre incluye el número de página ({Path.GetFileName(paths[0])})");

        using (var stream = File.OpenRead(paths[0]))
        {
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            CheckClose(frame.PixelWidth, 1190, "La imagen se exporta a 144 ppp", 6);
            Check(frame.PixelHeight > frame.PixelWidth, "La imagen mantiene la orientación de la página");
        }

        var jpgPaths = await session.ExportImagesAsync(new ImageExportOptions(
            imagesDir, "foto", "jpg", 96, "1"));
        Check(jpgPaths.Count == 1 && File.Exists(jpgPaths[0]), "Se puede exportar en JPEG");

        var invalid = false;
        try
        {
            await session.ExportImagesAsync(new ImageExportOptions(imagesDir, "malo", "png", 96, "9"));
        }
        catch (PdfException)
        {
            invalid = true;
        }
        Check(invalid, "Un rango fuera del documento da un error claro");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }
        return count;
    }

    private static (int Dark, int Red) CountInk(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);

        var dark = 0;
        var red = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var b = pixels[i];
            var g = pixels[i + 1];
            var r = pixels[i + 2];
            if (r < 160 && g < 160 && b < 160) dark++;
            if (r > 180 && r - g > 20 && r - b > 20) red++;
        }
        return (dark, red);
    }

    private static async Task TestRichDocument(string richPath, string outDir)
    {
        Console.WriteLine("== Metadatos, marcadores, adjuntos y texto ==");
        using var session = await DocumentSession.OpenAsync(richPath);

        Check(session.Pages.Count == 3, $"El documento enriquecido tiene 3 páginas ({session.Pages.Count})");
        Check(session.Info.Title == "Documento enriquecido", $"Se lee el título ({session.Info.Title})");
        Check(session.Info.Author == "MSoft", $"Se lee el autor ({session.Info.Author})");
        Check(session.Info.Keywords.Contains("pdfium"), "Se leen las palabras clave");
        Check(session.Info.FileVersion >= 17, $"Se lee la versión del PDF ({session.Info.FileVersion})");
        Check(!session.Info.IsEncrypted, "El documento no está cifrado");
        Check(session.Info.CanPrint && session.Info.CanCopy, "Un PDF sin cifrar permite imprimir y copiar");

        Check(session.Pages[0].DisplayLabel == "1", $"Etiqueta de la página 1 ({session.Pages[0].DisplayLabel})");
        Check(session.Pages[1].DisplayLabel == "2", $"Etiqueta de la página 2 ({session.Pages[1].DisplayLabel})");
        Check(session.Pages[2].DisplayLabel == "i", $"Etiqueta romana de la página 3 ({session.Pages[2].DisplayLabel})");

        Check(session.Bookmarks.Count == 2, $"Se leen 2 marcadores ({session.Bookmarks.Count})");
        if (session.Bookmarks.Count == 2)
        {
            Check(session.Bookmarks[0].Title == "Inicio del documento", "Título del primer marcador");
            Check(session.Bookmarks[1].PageIndex == 1, "El segundo marcador apunta a la página 2");
        }

        Check(session.Attachments.Count == 1, $"Se lee 1 adjunto ({session.Attachments.Count})");
        if (session.Attachments.Count == 1)
        {
            var attachment = session.Attachments[0];
            Check(attachment.Name == "nota.txt", $"Nombre del adjunto ({attachment.Name})");
            Check(attachment.Size > 0, $"Tamaño del adjunto ({attachment.Size})");

            var attachmentPath = Path.Combine(outDir, "nota.txt");
            await session.SaveAttachmentAsync(attachment, attachmentPath);
            var content = await File.ReadAllTextAsync(attachmentPath);
            Check(content.Contains("Adjunto de prueba"), "El adjunto se guarda con su contenido");
        }

        var pageText = await session.GetPageTextAsync(session.Pages[0]);
        Check(pageText.Contains("Mpdf"), "Se extrae el texto de una página");
        var documentText = await session.GetDocumentTextAsync();
        Check(documentText.Contains("PDFium") && documentText.Contains("Etiquetas"),
            "Se extrae el texto de todo el documento");
    }

    private static AnnotationModel Area(AnnotationKind kind, Color color,
        double left, double top, double right, double bottom)
    {
        var annotation = new AnnotationModel { Kind = kind, Color = color, StrokeWidth = 2.0 };
        annotation.Points.Add(new Point(left, top));
        annotation.Points.Add(new Point(right, bottom));
        return annotation;
    }

    private static void SavePng(BitmapSource bitmap, string path)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    private static void Check(bool condition, string description)
    {
        _checks++;
        if (condition) return;
        _failures++;
        Console.WriteLine("  FALLO: " + description);
    }

    private static void CheckClose(double actual, double expected, string description, double tolerance = 1.0)
    {
        Check(Math.Abs(actual - expected) <= tolerance, $"{description} ({actual:0.##}, esperado {expected:0.##})");
    }

    private static string FindProjectRoot(string start)
    {
        var directory = new DirectoryInfo(start);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Mpdf.csproj")))
                return directory.FullName;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("No se encuentra la raíz del proyecto Mpdf.");
    }
}
