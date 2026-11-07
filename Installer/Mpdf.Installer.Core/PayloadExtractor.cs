using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Mpdf.Installer.Core
{
    public static class PayloadExtractor
    {
        public static long GetUncompressedSize(Stream zipStream)
        {
            if (zipStream == null)
            {
                return 0;
            }

            var position = zipStream.CanSeek ? zipStream.Position : 0;
            try
            {
                using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, true))
                {
                    return archive.Entries.Where(IsPayloadFile).Sum(entry => entry.Length);
                }
            }
            finally
            {
                if (zipStream.CanSeek)
                {
                    zipStream.Position = position;
                }
            }
        }

        public static void Extract(Stream zipStream, string targetDirectory, Action<int, string> report,
            int startPercent, int endPercent)
        {
            if (zipStream == null)
            {
                throw new InstallerException("No se encontraron los archivos de Mpdf dentro del instalador.");
            }

            Directory.CreateDirectory(targetDirectory);
            zipStream.Position = 0;

            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Read, true))
            {
                var entries = archive.Entries.Where(IsPayloadFile).ToList();
                if (entries.Count == 0)
                {
                    throw new InstallerException("El instalador no contiene los archivos de Mpdf.");
                }

                var total = entries.Sum(entry => Math.Max(1, entry.Length));
                long processed = 0;
                var buffer = new byte[262144];
                var lastPercent = -1;

                foreach (var entry in entries)
                {
                    var name = Path.GetFileName(entry.FullName);
                    var target = Path.Combine(targetDirectory, name);
                    using (var input = entry.Open())
                    using (var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, read);
                            processed += read;
                            var ratio = Math.Min(1.0, (double)processed / total);
                            var percent = startPercent + (int)(ratio * (endPercent - startPercent));
                            if (percent != lastPercent)
                            {
                                lastPercent = percent;
                                report?.Invoke(percent, "Extrayendo archivos…");
                            }
                        }
                    }

                    File.SetLastWriteTimeUtc(target, entry.LastWriteTime.UtcDateTime);
                }
            }
        }

        private static bool IsPayloadFile(ZipArchiveEntry entry)
        {
            return entry.Length > 0 &&
                   !string.IsNullOrEmpty(entry.Name) &&
                   entry.FullName.IndexOf('/') < 0 &&
                   entry.FullName.IndexOf('\\') < 0;
        }
    }
}
