# Prepara la carpeta MEGA de Mpdf 1.1 con los artefactos compilados por build-all.ps1.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $MyInvocation.MyCommand.Path)
Add-Type -AssemblyName System.IO.Compression.FileSystem

$version = '1.1'
$target = 'C:\Users\Monty\Documents\MEGA\Mpdf-1.1'
$stageRoot = Join-Path $env:TEMP 'mpdf-mega-stage'

if (Test-Path $target) { throw "Ya existe $target. Revisa su contenido antes de sobrescribirlo." }
New-Item -ItemType Directory -Path $target -Force | Out-Null
if (Test-Path $stageRoot) { Remove-Item $stageRoot -Recurse -Force }
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null

function Copy-Artifact([string]$source, [string]$name) {
    if (-not (Test-Path $source)) { throw "Falta el artefacto: $source" }
    Copy-Item $source (Join-Path $target $name) -Force
    Write-Host ("  {0}" -f $name) -ForegroundColor DarkGray
}

function New-Zip([string[]]$sources, [string]$name) {
    $stage = Join-Path $stageRoot ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    foreach ($source in $sources) {
        if (-not (Test-Path $source)) { throw "Falta el artefacto: $source" }
        Copy-Item $source $stage -Force
    }
    $destination = Join-Path $target $name
    if (Test-Path $destination) { Remove-Item $destination -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $stage, $destination, [System.IO.Compression.CompressionLevel]::Optimal, $false)
    Write-Host ("  {0}" -f $name) -ForegroundColor DarkGray
}

function New-TarGz([string[]]$sources, [string]$name) {
    $stage = Join-Path $stageRoot ([Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage -Force | Out-Null
    foreach ($source in $sources) {
        if (-not (Test-Path $source)) { throw "Falta el artefacto: $source" }
        Copy-Item $source $stage -Force
    }
    $destination = Join-Path $target $name
    if (Test-Path $destination) { Remove-Item $destination -Force }
    & tar.exe -czf $destination -C $stage (Get-ChildItem $stage -File | Select-Object -First 1 -ExpandProperty Name)
    if ($LASTEXITCODE -ne 0) { throw "No se pudo comprimir $name" }
    Write-Host ("  {0}" -f $name) -ForegroundColor DarkGray
}

Write-Host 'Windows WPF' -ForegroundColor Cyan
Copy-Artifact 'publish\Mpdf.exe' "Mpdf-$version-Windows-x64-Portable.exe"
Copy-Artifact 'publish\MpdfSetup.exe' "Mpdf-$version-Windows-x64-Setup.exe"
New-Zip @('publish\Mpdf.exe') "Mpdf-$version-Windows-x64-Portable.zip"

Write-Host 'Avalonia escritorio' -ForegroundColor Cyan
New-Zip  @('dist\desktop\win-x64\Mpdf.Portable.exe')   "Mpdf-$version-Windows-x64-Avalonia.zip"
New-Zip  @('dist\desktop\win-arm64\Mpdf.Portable.exe') "Mpdf-$version-Windows-ARM64-Avalonia.zip"
New-TarGz @('dist\desktop\linux-x64\Mpdf.Portable')    "Mpdf-$version-Linux-x64-Avalonia.tar.gz"
New-TarGz @('dist\desktop\linux-arm64\Mpdf.Portable')  "Mpdf-$version-Linux-ARM64-Avalonia.tar.gz"
New-TarGz @('dist\desktop\osx-x64\Mpdf.Portable')      "Mpdf-$version-macOS-Intel-Avalonia.tar.gz"
New-TarGz @('dist\desktop\osx-arm64\Mpdf.Portable')    "Mpdf-$version-macOS-AppleSilicon-Avalonia.tar.gz"

Write-Host 'CLI' -ForegroundColor Cyan
$cliRids = @(
    @{ Rid = 'win-x64';    File = 'mpdf.exe'; Ext = 'zip' },
    @{ Rid = 'win-arm64';  File = 'mpdf.exe'; Ext = 'zip' },
    @{ Rid = 'linux-x64';  File = 'mpdf';     Ext = 'tar.gz' },
    @{ Rid = 'linux-arm64';File = 'mpdf';     Ext = 'tar.gz' },
    @{ Rid = 'osx-x64';    File = 'mpdf';     Ext = 'tar.gz' },
    @{ Rid = 'osx-arm64';  File = 'mpdf';     Ext = 'tar.gz' }
)
foreach ($item in $cliRids) {
    $source = Join-Path "dist\cli\$($item.Rid)" $item.File
    $name = "Mpdf-$version-CLI-$($item.Rid).$($item.Ext)"
    if ($item.Ext -eq 'zip') { New-Zip @($source) $name } else { New-TarGz @($source) $name }
}

Write-Host 'Móvil' -ForegroundColor Cyan
$apk = Get-ChildItem 'dist\android-apk' -Recurse -File -Filter '*.apk' | Select-Object -First 1
$aab = Get-ChildItem 'dist\android-aab' -Recurse -File -Filter '*.aab' | Select-Object -First 1
if ($null -eq $apk) { throw 'No se encontró el APK.' }
if ($null -eq $aab) { throw 'No se encontró el AAB.' }
Copy-Artifact $apk.FullName "Mpdf-$version-Android.apk"
Copy-Artifact $aab.FullName "Mpdf-$version-Android.aab"

$readme = @"
Mpdf $version — paquetes para MEGA
================================

Windows (edición completa WPF)
- Mpdf-$version-Windows-x64-Setup.exe: instalador para Windows 10/11 x64.
- Mpdf-$version-Windows-x64-Portable.exe: ejecutable portable, sin instalación.
- Mpdf-$version-Windows-x64-Portable.zip: el mismo ejecutable comprimido.
Novedad: «Copiar imagen de esta página» en Herramientas y en la paleta Ctrl+K.

Escritorio portable (Avalonia)
- Windows x64/ARM64, Linux x64/ARM64 y macOS Intel/Apple Silicon.
- Funciones: abrir y ver PDF, navegar, exportar imagen/texto, girar y guardar.
- No incluye anotaciones, búsqueda, marcadores, adjuntos, estampado ni deshacer/rehacer.

CLI
- Mpdf-$version-CLI-<plataforma>: informaciones, render, texto, extracción,
  combinación, giro y exportación de imágenes por línea de comandos.

Android
- APK para instalar y AAB para tiendas. Firmados con certificado de depuración:
  hay que firmarlos con la clave de MSoft antes de publicarlos en Play Store.

iOS
- No se incluye IPA: requiere macOS/Xcode, certificado de distribución y perfil
  de aprovisionamiento de Apple. El proyecto Mpdf.iOS compila para ios-arm64.

Verificación
- SHA256SUMS.txt contiene el hash de todos los archivos de esta carpeta.
- Las descargas de la web deben usar los enlaces definitivos de MEGA.

© 2026 MSoft. Licencia MIT. PDFium © Chromium Project (BSD-3).
"@
Set-Content -Path (Join-Path $target 'LEEME.txt') -Value $readme -Encoding UTF8

$hashes = Get-ChildItem $target -File |
    Where-Object { $_.Name -ne 'SHA256SUMS.txt' } |
    Sort-Object Name |
    ForEach-Object {
        $hash = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToUpperInvariant()
        "{0}  {1}" -f $hash, $_.Name
    }
Set-Content -Path (Join-Path $target 'SHA256SUMS.txt') -Value $hashes -Encoding UTF8
Remove-Item $stageRoot -Recurse -Force -ErrorAction SilentlyContinue

Write-Host "`nCarpeta lista: $target" -ForegroundColor Green
Get-ChildItem $target -File | Select-Object Name, Length | Format-Table -AutoSize
