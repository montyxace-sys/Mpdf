# Mpdf

Editor de PDF gratuito y de código abierto para Windows, con una interfaz moderna inspirada en Adobe Acrobat. Usa [PDFium](https://pdfium.googlesource.com/pdfium/) como motor de renderizado y escritura.

![Mpdf](docs/screenshot.png)

## Funciones

### Visor y navegación
- Abrir PDF (también protegidos con contraseña) y arrastrar archivos sobre la ventana.
- Zoom, ajustar al ancho, miniaturas y etiquetas de página del documento.
- Pantalla completa sin distracciones (F11).
- Búsqueda incremental con resaltado de coincidencias; F3 y Mayús+F3 para navegar.
- Panel lateral con páginas, marcadores anidados, comentarios y adjuntos.

### Anotaciones
- Resaltar, subrayar, tachar, lápiz, rectángulos y borrador.
- Paleta de colores y grosor de trazo.
- Las marcas se guardan como anotaciones reales del PDF y se pueden deshacer y rehacer.
- Guardado atómico (archivo temporal y reemplazo) para no corromper el original.

### Páginas
- Insertar página en blanco del mismo tamaño que la actual.
- Insertar las páginas de otro PDF en la posición elegida.
- Mover, girar y eliminar páginas.
- Extraer un rango de páginas a un PDF nuevo.
- Dividir el documento en varios PDFs cada N páginas.
- Combinar varios PDFs en uno.
- Girar todas las páginas a la vez en cualquier dirección.

### Estampado y exportación
- Numeración de páginas con formato personalizable, número inicial, posición, tamaño y color.
- Marca de agua de texto con tamaño, opacidad, ángulo y color; se adapta a páginas verticales, giradas y de distinto tamaño.
- Exportar páginas o rangos como imágenes PNG o JPEG a 72, 150 o 300 ppp, incluyendo marcas y anotaciones.

### Documento
- Propiedades del documento: tamaño, versión, metadatos, cifrado, permisos, marcadores y adjuntos.
- Abrir o guardar archivos adjuntos incrustados.
- Copiar el texto de una página o exportar el texto de todo el documento.

### Productividad
- Paleta de comandos con Ctrl+K.
- Atajos: Ctrl+O, Ctrl+S, Ctrl+Mayús+S, Ctrl+Z, Ctrl+Y, Ctrl+F, Ctrl+0, Ctrl+«+», Ctrl+«-», F11, Esc.

## Requisitos

- Windows 10 u 11 de 64 bits.
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) para compilar.
- Node.js solo si quieres regenerar los PDFs de prueba.

## Compilar y ejecutar

```powershell
dotnet build Mpdf.csproj -c Release
dotnet run --project Mpdf.csproj
```

Para generar un ejecutable autocontenido:

```powershell
dotnet publish Mpdf.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
```

## Pruebas del motor

Las pruebas cubren renderizado, búsqueda, anotaciones, rotación, combinación, extracción, división, deshacer/rehacer, numeración, marcas de agua (también en páginas giradas), exportación de imágenes, metadatos, marcadores, adjuntos y extracción de texto.

```powershell
node test/make-test-pdfs.js
dotnet run --project tests/Mpdf.EngineTests
```

Resultado actual: 138 comprobaciones, 0 fallos.

## Estructura

```
Controls/    Vistas, diálogos y la página interactiva
Models/      Modelos de página, anotaciones e información del documento
Services/    Sesión del documento y enlaces P/Invoke con PDFium
Styles/      Tema y estilos WPF
test/        Generador de PDFs de prueba y utilidades de verificación
tests/       Pruebas del motor
```

## Limitaciones conocidas

- Las propiedades del documento se muestran en modo lectura; esta integración de PDFium no permite escribir metadatos.
- No se pueden guardar documentos cifrados ni cambiar contraseñas o permisos.
- Sin OCR, formularios, capas, firmas digitales ni redacción real.
- Sin vista de dos páginas, rotación de vista, temas oscuros ni tamaño de miniatura ajustable.
- Sin recuperación automática tras un cierre inesperado.
- Solo Windows x64.

## Agradecimientos

- **PDFium** (Chromium Project, licencia BSD-3) por el motor PDF.
- **PDFCraft** (MIT OR Apache-2.0) y **Stirling PDF** por servir de referencia en la organización de herramientas.
- **Adobe Acrobat** por la inspiración visual.

Mpdf no está afiliado a Adobe, PDFCraft ni Stirling PDF.

## Licencia

MIT © 2026 MSoft. Consulta [LICENSE](LICENSE).
