using System;

namespace Mpdf.Installer.Core
{
    public static class ProductInfo
    {
        public const string Name = "Mpdf";
        public const string Publisher = "MSoft";
        public const string Url = "https://msoft.com.es";
        public const string Description = "Editor de PDF gratuito y de código abierto";
        public const string ExeName = "Mpdf.exe";
        public const string UninstallerName = "Mpdf.Uninstall.exe";
        public const string ProgId = "Mpdf.Document";
        public const string FileTypeName = "Documento PDF de Mpdf";

        public static string Version
        {
            get
            {
                var version = typeof(ProductInfo).Assembly.GetName().Version;
                return version == null ? "0.1.0" : version.ToString(3);
            }
        }
    }

    public sealed class InstallerException : Exception
    {
        public InstallerException(string message) : base(message)
        {
        }

        public InstallerException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
