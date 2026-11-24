# Compila todos los artefactos de Mpdf que se pueden generar en Windows.
# Ejecutar desde la raíz del repo: powershell -ExecutionPolicy Bypass -File build-all.ps1
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $MyInvocation.MyCommand.Path)

function Step([string]$title, [scriptblock]$action) {
    Write-Host "`n===== $title =====" -ForegroundColor Cyan
    & $action
}

Step 'WPF Windows x64 (exe + instalador)' {
    & .\build-setup.ps1
    if (-not (Test-Path 'publish\Mpdf.exe')) { throw 'Falta publish\Mpdf.exe' }
    if (-not (Test-Path 'publish\MpdfSetup.exe')) { throw 'Falta publish\MpdfSetup.exe' }
}

$desktops = @('win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')
foreach ($rid in $desktops) {
    Step "Avalonia escritorio $rid" {
        dotnet publish Mpdf.Desktop\Mpdf.Desktop.csproj -c Release -r $rid `
            --self-contained true -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true -o "dist\desktop\$rid"
        if ($LASTEXITCODE -ne 0) { throw "No se pudo publicar Avalonia $rid" }
    }
}

foreach ($rid in $desktops) {
    Step "CLI $rid" {
        dotnet publish Mpdf.Cli\Mpdf.Cli.csproj -c Release -r $rid `
            --self-contained true -p:PublishSingleFile=true `
            -p:IncludeNativeLibrariesForSelfExtract=true -o "dist\cli\$rid"
        if ($LASTEXITCODE -ne 0) { throw "No se pudo publicar la CLI $rid" }
    }
}

Step 'Android (APK)' {
    dotnet publish Mpdf.Android\Mpdf.Android.csproj -c Release -f net11.0-android `
        -p:AndroidPackageFormat=apk -o dist\android-apk
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el APK' }
}

Step 'Android (AAB)' {
    dotnet publish Mpdf.Android\Mpdf.Android.csproj -c Release -f net11.0-android `
        -p:AndroidPackageFormat=aab -o dist\android-aab
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo publicar el AAB' }
}

Step 'iOS (binario de dispositivo; el IPA requiere firma Apple)' {
    dotnet build Mpdf.iOS\Mpdf.iOS.csproj -c Release -r ios-arm64
    if ($LASTEXITCODE -ne 0) { throw 'No se pudo compilar el binario iOS' }
}

Write-Host "`nArtefactos en publish\ y dist\" -ForegroundColor Green
Get-ChildItem publish,dist -Recurse -File |
    Where-Object { $_.Extension -in '.exe', '.apk', '.aab', '.ipa', '.zip', '.gz' -or $_.Name -in 'Mpdf.Portable', 'mpdf' } |
    Select-Object FullName, Length
