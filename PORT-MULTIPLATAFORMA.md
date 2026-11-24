# Mpdf multiplataforma — estado real

## Resultado

El editor WPF de Windows sigue siendo la versión completa. Junto a él existe una edición
portable reducida que comparte interfaz Avalonia y un núcleo PDF sin dependencias de WPF.

| Plataforma | Artefacto | Estado |
| --- | --- | --- |
| Windows x64 (WPF completa) | `publish\Mpdf.exe`, `publish\MpdfSetup.exe` | Compilado con «Copiar imagen de esta página» |
| Windows x64 / ARM64 (Avalonia) | `dist\desktop\win-x64\Mpdf.Portable.exe`, `dist\desktop\win-arm64\Mpdf.Portable.exe` | Compilado |
| Linux x64 / ARM64 (Avalonia) | `dist\desktop\linux-x64\Mpdf.Portable`, `dist\desktop\linux-arm64\Mpdf.Portable` | Compilado |
| macOS Intel / Apple Silicon (Avalonia) | `dist\desktop\osx-x64\Mpdf.Portable`, `dist\desktop\osx-arm64\Mpdf.Portable` | Compilado |
| CLI Windows / Linux / macOS | `dist\cli\<rid>\mpdf(.exe)` | Compilado |
| Android | `dist\android-apk\*.apk`, `dist\android-aab\*.aab` | Compilado; firmado con certificado de depuración |
| iOS (dispositivo) | `Mpdf.iOS.csproj` con RID `ios-arm64` | Compila; el IPA requiere firma Apple |

## Arquitectura

- `Mpdf.Core` (`net8.0`): `PortablePdf`, enlaces P/Invoke a PDFium y render con SkiaSharp.
  No usa WPF. En iOS carga `@rpath/pdfium.framework/pdfium` en lugar de `pdfium`.
- `Mpdf.App` (`net8.0`): interfaz Avalonia compartida (`MainView`), reutilizada por
  escritorio, Android e iOS.
- `Mpdf.Desktop` (`net8.0` + Avalonia.Desktop): cabeza de escritorio publicable para
  `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64` y `osx-arm64`.
- `Mpdf.Cli` (`net8.0`): utilidades de línea de comandos sobre el mismo núcleo.
- `Mpdf.Android` (`net11.0-android`): `MainActivity` con Avalonia.Android.
- `Mpdf.iOS` (`net11.0-ios`): `AppDelegate` con Avalonia.iOS.

PDFium se incluye por plataforma (`bblanchon.PDFium.Win32`, `.Linux`, `.macOS`,
`.Android`, `.iOS`). El paquete paraguas `bblanchon.PDFium` no se usa porque arrastra
dependencias iOS incompatibles con un proyecto `net8.0` genérico (`NU1202`).

## Funciones de la edición portable

- Abrir PDF desde ruta o selector de archivos.
- Ver páginas y desplazarse entre ellas.
- Renderizar a PNG, exportar texto de todo el documento.
- Girar la página actual y guardar el PDF resultante.
- CLI: `info`, `render`, `text`, `extract`, `merge`, `rotate`, `images`.

## Funciones aún no portadas

Estas funciones siguen exclusivas en la edición WPF de Windows:

- Anotaciones (resaltar, subrayar, tachar, lápiz, rectángulos, borrador) y deshacer/rehacer.
- Búsqueda con resaltado, marcadores y adjuntos.
- Estampado de numeración y marcas de agua.
- Dividir documentos, insertar páginas y diálogos de propiedades.
- Exportar imágenes por rangos y la orden «Copiar imagen de esta página».

## Compilar

```powershell
powershell -ExecutionPolicy Bypass -File build-all.ps1
```

El script genera todos los artefactos posibles desde Windows. Requisitos:

- SDK .NET 8 y 11, cargas `android` e `ios` instaladas.
- Android SDK y JDK para los paquetes APK/AAB.

Para publicar en iOS de forma real hace falta macOS, Xcode, un certificado de
distribución y un perfil de aprovisionamiento. Para Android, el paquete está firmado con
la clave de depuración: hay que firmarlo con la clave de MSoft antes de subirlo a una tienda.

## Verificación realizada

- `dotnet build Mpdf.csproj -c Release`: 0 errores, 0 advertencias.
- 138 comprobaciones del motor WPF: 0 fallos.
- 39 comprobaciones del instalador WPF: 0 fallos (versión 1.1.0).
- Render y portapapeles de «Copiar imagen de esta página»: imagen `1240x1755` copiada y
  recuperada desde `Clipboard.GetImage()`.
- `dotnet build Mpdf.Desktop\Mpdf.Desktop.csproj -c Release`: 0 errores, 0 advertencias.
- CLI portable probada con `test\sample.pdf`: `info`, `render`, `text`, `extract`,
  `rotate` y `merge`.
- `dotnet build Mpdf.Android\Mpdf.Android.csproj -c Release`: 0 errores; avisos XA0141
  de SkiaSharp sobre páginas de 16 KB en Android 16.
- `dotnet build Mpdf.iOS\Mpdf.iOS.csproj -c Release`: 0 errores.
