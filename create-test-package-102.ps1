# ==============================================================================
# Script de Preparacao do Ambiente de Teste Controlado - CobraPix PC v1.0.2
# ==============================================================================
# Este script gera um pacote e manifesto de teste 1.0.2 exclusivamente dentro de
# %TEMP%\CobraPix\UpdateTest. Ele NAO publica nada no GitHub e NAO altera o Release v1.0.1.
# ==============================================================================

[CmdletBinding()]
param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName "System.IO.Compression.FileSystem"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " COBRAPIX PC - AMBIENTE CONTROLADO DE TESTE DE ATUALIZACAO" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$scriptDir = $PSScriptRoot
$tempRoot = Join-Path $env:TEMP "CobraPix\UpdateTest"
$packagesDir = Join-Path $tempRoot "Packages"
$zipPath = Join-Path $packagesDir "CobraPix-PC-1.0.2-TEST-win-x64.zip"
$manifestPath = Join-Path $tempRoot "manifest_test_1.0.2.json"
$sourceBinDir = Join-Path $scriptDir "bin\$Configuration\net10.0-windows"

Write-Host "[1/6] Verificando diretorios do ambiente de teste..." -ForegroundColor Yellow
if (-not (Test-Path $tempRoot)) { New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null }
if (-not (Test-Path $packagesDir)) { New-Item -ItemType Directory -Path $packagesDir -Force | Out-Null }

Write-Host "[2/6] Localizando binarios de origem..." -ForegroundColor Yellow
if (-not (Test-Path $sourceBinDir)) {
    Write-Host "Binarios em $sourceBinDir nao encontrados. Compilando projeto..." -ForegroundColor Yellow
    & dotnet build "$scriptDir\CobraPix PC.csproj" -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Falha na compilacao de Release."
        exit $LASTEXITCODE
    }
}

# Staging isolado
$stagingDir = Join-Path $tempRoot ("Staging_1.0.2_" + [guid]::NewGuid().ToString("N"))
if (Test-Path $stagingDir) { Remove-Item -Recurse -Force $stagingDir }
New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null

try {
    Write-Host "[3/6] Copiando arquivos essenciais para staging..." -ForegroundColor Yellow
    $essentialFiles = @(
        "CobraPix PC.exe",
        "CobraPix PC.dll",
        "CobraPix PC.runtimeconfig.json",
        "CobraPix PC.deps.json",
        "CobraPix.Updater.exe",
        "CobraPix.Updater.dll",
        "CobraPix.Updater.runtimeconfig.json",
        "CobraPix.Updater.deps.json"
    )

    foreach ($file in $essentialFiles) {
        $src = Join-Path $sourceBinDir $file
        $dst = Join-Path $stagingDir $file
        if (Test-Path $src) {
            Copy-Item -Path $src -Destination $dst -Force
        } else {
            Set-Content -Path $dst -Value "SIMULATED_$($file.ToUpper())_V1.0.2" -Encoding UTF8
        }
    }

    # Copia DLLs dependentes da aplicacao se disponiveis
    Get-ChildItem -Path $sourceBinDir -Filter "*.dll" | ForEach-Object {
        $dest = Join-Path $stagingDir $_.Name
        if (-not (Test-Path $dest)) {
            Copy-Item -Path $_.FullName -Destination $dest -Force
        }
    }

    # Marcador de teste explicito
    $marker = @{
        packageType = "CONTROLLED_TEST_PACKAGE"
        simulatedVersion = "1.0.2"
        baseVersion = "1.0.1"
        isTestPackage = $true
        generatedAtUtc = (Get-Date).ToUniversalTime().ToString("o")
        purpose = "Ambiente controlado para validacao isolada do CobraPix.Updater"
        officialRelease = $false
    }
    $markerJson = $marker | ConvertTo-Json -Depth 4
    Set-Content -Path (Join-Path $stagingDir "test_version_marker.json") -Value $markerJson -Encoding UTF8

    Write-Host "[4/6] Verificando ausencia estrita de bancos de dados ou backups..." -ForegroundColor Yellow
    $forbiddenFiles = Get-ChildItem -Path $stagingDir -Recurse -File | Where-Object {
        $ext = $_.Extension.ToLowerInvariant()
        $ext -in @(".db", ".db-wal", ".db-shm", ".cpb", ".sqlite", ".sqlite3")
    }

    if ($forbiddenFiles.Count -gt 0) {
        Write-Error "VIOLACAO DE SEGURANCA: Arquivo de dados proibido no pacote de teste!"
        exit 1
    }

    Write-Host "[5/6] Compactando pacote de teste ZIP..." -ForegroundColor Yellow
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stagingDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    # Calculo SHA-256
    $fileHash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $fileSize = (Get-Item $zipPath).Length

    Write-Host "[6/6] Gerando manifesto de teste 1.0.2..." -ForegroundColor Yellow
    $zipUri = "file:///" + $zipPath.Replace("\", "/")
    $manifestData = [ordered]@{
        version = "1.0.2"
        downloadUrl = $zipUri
        releaseNotes = @(
            "Versao 1.0.2 de TESTE CONTROLADO",
            "Ambiente isolado para validacao do fluxo do atualizador",
            "Validacao de substituicao segura de binarios e reinicio",
            "Pacote nao publicado no canal oficial de producao"
        )
        sha256 = $fileHash
        mandatory = $false
    }

    $manifestJson = $manifestData | ConvertTo-Json -Depth 4
    Set-Content -Path $manifestPath -Value $manifestJson -Encoding UTF8

    Write-Host "`nPACOTE E MANIFESTO DE TESTE GERADOS COM SUCESSO!" -ForegroundColor Green
    Write-Host "Local do Manifesto: $manifestPath" -ForegroundColor Cyan
    Write-Host "Local do Pacote:    $zipPath" -ForegroundColor Cyan
    Write-Host "Tamanho do Pacote:  $fileSize bytes" -ForegroundColor Cyan
    Write-Host "SHA-256 Calculado:  $fileHash" -ForegroundColor Cyan
    Write-Host "Versao Simulada:    1.0.2" -ForegroundColor Cyan
}
finally {
    if (Test-Path $stagingDir) {
        Remove-Item -Recurse -Force $stagingDir -ErrorAction SilentlyContinue
    }
}
