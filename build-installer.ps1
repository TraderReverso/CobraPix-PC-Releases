# =====================================================================
# Script oficial de geracao do instalador CobraPix PC via Inno Setup 6
# Versao: 1.0.7
# =====================================================================
[CmdletBinding()]
param(
    [Parameter(Position=0)]
    [string]$Version = "1.0.7"
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " COBRAPIX PC - GERACAO DO INSTALADOR INICIAL ($Version)   " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Resolucao de caminhos
$scriptDir = $PSScriptRoot
$publishDir = Join-Path $scriptDir "artifacts\publish\$Version"
$packageDir = Join-Path $scriptDir "artifacts\package\$Version"
$issFile = Join-Path $scriptDir "installer\cobrapix-setup.iss"
$iconFile = Join-Path $scriptDir "Assets\app_icon.ico"
$outputExeName = "CobraPix PC Setup $Version.exe"
$outputExePath = Join-Path $packageDir $outputExeName

Write-Host "[1/6] Verificando arquivos da publicacao..." -ForegroundColor Yellow
if (-not (Test-Path $publishDir)) {
    Write-Error "Diretorio de publicacao nao encontrado: $publishDir. Execute publish-release.ps1 primeiro."
    exit 1
}

$mainExe = Join-Path $publishDir "CobraPix PC.exe"
$updaterExe = Join-Path $publishDir "CobraPix.Updater.exe"

if (-not (Test-Path $mainExe)) {
    Write-Error "Executavel principal ausente na publicacao: $mainExe"
    exit 1
}
if (-not (Test-Path $updaterExe)) {
    Write-Error "Executavel do Updater ausente na publicacao: $updaterExe"
    exit 1
}
if (-not (Test-Path $iconFile)) {
    Write-Error "Icone oficial nao encontrado: $iconFile"
    exit 1
}
if (-not (Test-Path $issFile)) {
    Write-Error "Script Inno Setup nao encontrado: $issFile"
    exit 1
}

# 2. Inspecao de seguranca da pasta de publicacao
Write-Host "[2/6] Auditando ausencia estrita de bancos de dados no publish..." -ForegroundColor Yellow
$forbiddenFiles = Get-ChildItem -Path $publishDir -Recurse -File | Where-Object {
    $ext = $_.Extension.ToLowerInvariant()
    $ext -in @(".db", ".db-wal", ".db-shm", ".cpb", ".sqlite", ".sqlite3")
}

if ($forbiddenFiles -and $forbiddenFiles.Count -gt 0) {
    Write-Error "VIOLACAO DE SEGURANCA: Arquivos de banco ou backup encontrados no publish!"
    exit 1
}
Write-Host "OK: Nenhum banco de dados ou backup encontrado." -ForegroundColor Green

# 3. Localizacao do compilador Inno Setup 6 (ISCC.exe)
Write-Host "[3/6] Localizando Inno Setup 6 (ISCC.exe)..." -ForegroundColor Yellow
$isccCandidates = @(
    (Get-Command ISCC.exe -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source),
    (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe"
)

$isccPath = $null
foreach ($cand in $isccCandidates) {
    if ($cand -and (Test-Path $cand)) {
        $isccPath = $cand
        break
    }
}

if (-not $isccPath) {
    Write-Error "ISCC.exe (Inno Setup 6) nao foi encontrado. Instale via 'winget install JRSoftware.InnoSetup'."
    exit 1
}
Write-Host "OK: Compilador localizado em: $isccPath" -ForegroundColor Green

# 4. Garantir diretorio de saida
if (-not (Test-Path $packageDir)) {
    New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
}

# 5. Compilacao do Instalador
Write-Host "[4/6] Compilando instalador comercial..." -ForegroundColor Yellow
& "$isccPath" "/Q" "$issFile"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Falha na compilacao do instalador pelo Inno Setup. Codigo de saida: $LASTEXITCODE"
    exit $LASTEXITCODE
}

# 6. Validacao e auditoria do arquivo de saida
Write-Host "[5/6] Validando instalador gerado..." -ForegroundColor Yellow
if (-not (Test-Path $outputExePath)) {
    Write-Error "O instalador esperado nao foi gerado em: $outputExePath"
    exit 1
}

$fileInfo = Get-Item $outputExePath
$sha256 = (Get-FileHash -Path $outputExePath -Algorithm SHA256).Hash.ToUpperInvariant()
$sizeMb = [Math]::Round($fileInfo.Length / 1048576, 2)

Write-Host "OK: Instalador gerado e validado com sucesso!" -ForegroundColor Green

Write-Host "[6/6] Resumo do instalador comercial:" -ForegroundColor Yellow
Write-Host "Arquivo     : $outputExePath"
Write-Host "Tamanho     : $($fileInfo.Length) bytes ($sizeMb MB)"
Write-Host "SHA-256     : $sha256"
Write-Host "==========================================================" -ForegroundColor Green

return [PSCustomObject]@{
    Version = $Version
    InstallerPath = $outputExePath
    InstallerName = $outputExeName
    Sha256 = $sha256
    SizeBytes = $fileInfo.Length
    SizeMb = $sizeMb
}
