# Script de compilación y empaquetado automático para ZoomBridge
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Compilando y Generando Instalador de ZoomBridge (win-x64)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$outputDir = Join-Path $PSScriptRoot "publish\win-x64"
$installerDir = Join-Path $PSScriptRoot "publish\installer"
$installerExe = Join-Path $installerDir "ZoomBridge-Setup-1.0.0.exe"

if (Test-Path $outputDir) {
    Remove-Item -Path $outputDir -Recurse -Force
}

# 1. Publicar ejecutable autocontenido
Write-Host "`n[1/2] Publicando binarios optimizados..." -ForegroundColor Yellow
dotnet publish "ZoomBridge.csproj" `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o $outputDir

if ($LASTEXITCODE -ne 0) {
    Write-Host "`n[ERROR] Falló la publicación de dotnet." -ForegroundColor Red
    exit 1
}

# 2. Compilar paquete instalador Inno Setup
Write-Host "`n[2/2] Generando instalador de distribución con Inno Setup..." -ForegroundColor Yellow

$localAppData = [Environment]::GetFolderPath('LocalApplicationData')
$isccCandidates = @(
    (Join-Path $localAppData "Programs\Inno Setup 6\ISCC.exe"),
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$isccPath = $null
foreach ($cand in $isccCandidates) {
    if (![string]::IsNullOrEmpty($cand) -and (Test-Path $cand)) {
        $isccPath = $cand
        break
    }
}

if ($null -ne $isccPath) {
    $issFile = Join-Path $PSScriptRoot "installer\zoombridge-setup.iss"
    & "$isccPath" "$issFile"

    if ($LASTEXITCODE -eq 0 -and (Test-Path $installerExe)) {
        Write-Host "`n==========================================================" -ForegroundColor Green
        Write-Host " [EXITO] Paquete Instalador creado correctamente:" -ForegroundColor Green
        Write-Host " Archivo: $installerExe" -ForegroundColor White
        Write-Host "==========================================================" -ForegroundColor Green
    } else {
        Write-Host "`n[ERROR] Inno Setup falló al empaquetar el instalador." -ForegroundColor Red
    }
} else {
    Write-Host "`n[ERROR] No se encontró ISCC.exe." -ForegroundColor Red
}
