# Script oficial de publicacao e empacotamento deterministico do CobraPix PC.
[CmdletBinding()]
param(
    [Parameter(Position=0)]
    [string]$Version = "1.0.7",

    [Parameter()]
    [string]$Configuration = "Release",

    [Parameter()]
    [string]$Runtime = "win-x64",

    [Parameter()]
    [bool]$SelfContained = $true
)

$ErrorActionPreference = "Stop"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " COBRAPIX PC - PROCESSO OFICIAL DE PUBLICACAO ($Version)  " -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Validacao da versao
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Error "Versao invalida: '$Version'. O formato obrigatorio e SemVer MAJOR.MINOR.PATCH (ex: 1.0.1)."
    exit 1
}

# 2. Resolucao de caminhos
$scriptDir = $PSScriptRoot
$projectFile = Join-Path $scriptDir "CobraPix PC.csproj"
$updaterProject = Join-Path $scriptDir "CobraPix.Updater\CobraPix.Updater.csproj"
$artifactsDir = Join-Path $scriptDir "artifacts"
$publishDir = Join-Path $artifactsDir "publish\$Version"
$packageDir = Join-Path $artifactsDir "package\$Version"
$zipName = "CobraPix-PC-$Version-$Runtime.zip"
$zipPath = Join-Path $packageDir $zipName
$manifestPath = Join-Path $packageDir "manifest.json"
$releaseInfoPath = Join-Path $packageDir "release-info.json"

Write-Host "[1/8] Validando projetos e caminhos..." -ForegroundColor Yellow
if (-not (Test-Path $projectFile)) {
    Write-Error "Arquivo de projeto principal nao encontrado: $projectFile"
    exit 1
}
if (-not (Test-Path $updaterProject)) {
    Write-Error "Arquivo de projeto do atualizador nao encontrado: $updaterProject"
    exit 1
}

# 3. Limpeza restrita aos artefatos da versao
Write-Host "[2/8] Limpando diretorios de artefatos da versao $Version..." -ForegroundColor Yellow
if (Test-Path $publishDir) {
    Remove-Item -Recurse -Force $publishDir
}
if (Test-Path $packageDir) {
    Remove-Item -Recurse -Force $packageDir
}
New-Item -ItemType Directory -Path $publishDir -Force | Out-Null
New-Item -ItemType Directory -Path $packageDir -Force | Out-Null

# 4. Compilacao e Publicacao via dotnet publish
Write-Host "[3/8] Executando dotnet publish..." -ForegroundColor Yellow
$scStr = if ($SelfContained) { "true" } else { "false" }
& dotnet publish "$projectFile" -c "$Configuration" -r "$Runtime" --self-contained $scStr -o "$publishDir" "-p:Version=$Version" "-p:AssemblyVersion=$Version.0" "-p:FileVersion=$Version.0" "-p:InformationalVersion=$Version"
if ($LASTEXITCODE -ne 0) {
    Write-Error "Falha na execucao do dotnet publish. Codigo de saida: $LASTEXITCODE"
    exit $LASTEXITCODE
}

# 5. Verificacao dos binarios publicados
Write-Host "[4/8] Inspecionando arquivos essenciais publicados..." -ForegroundColor Yellow
$mainExe = Join-Path $publishDir "CobraPix PC.exe"
$updaterExe = Join-Path $publishDir "CobraPix.Updater.exe"
$mainDll = Join-Path $publishDir "CobraPix PC.dll"
$updaterDll = Join-Path $publishDir "CobraPix.Updater.dll"

if (-not (Test-Path $mainExe)) {
    Write-Error "ERRO CRITICO: Executavel principal nao encontrado em: $mainExe"
    exit 1
}
if (-not (Test-Path $updaterExe)) {
    Write-Error "ERRO CRITICO: Executavel do Updater nao encontrado em: $updaterExe"
    exit 1
}
if (-not (Test-Path $mainDll)) {
    Write-Error "ERRO CRITICO: DLL principal nao encontrada em: $mainDll"
    exit 1
}
if (-not (Test-Path $updaterDll)) {
    Write-Error "ERRO CRITICO: DLL do Updater nao encontrada em: $updaterDll"
    exit 1
}

# 6. REGRA ABSOLUTA DE SEGURANCA: Ausencia total de arquivos de banco de dados
Write-Host "[5/8] Verificando ausencia estrita de bancos de dados..." -ForegroundColor Yellow
$forbiddenFiles = Get-ChildItem -Path $publishDir -Recurse -File | Where-Object {
    $ext = $_.Extension.ToLowerInvariant()
    $ext -in @(".db", ".db-wal", ".db-shm", ".cpb", ".sqlite", ".sqlite3")
}

if ($forbiddenFiles -and $forbiddenFiles.Count -gt 0) {
    Write-Host "VIOLACAO DE SEGURANCA DETECTADA! Arquivos proibidos encontrados:" -ForegroundColor Red
    $forbiddenFiles | ForEach-Object { Write-Host " - $($_.FullName)" -ForegroundColor Red }
    Remove-Item -Recurse -Force $publishDir
    Write-Error "A publicacao foi ABORTADA para proteger a integridade dos dados."
    exit 1
}
Write-Host "OK: Nenhum arquivo de dados ou banco presente na publicacao." -ForegroundColor Green

# 7. Empacotamento ZIP deterministico
Write-Host "[6/8] Gerando pacote ZIP deterministico: $zipName..." -ForegroundColor Yellow
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$fixedTimestamp = [DateTimeOffset]::new(2026, 10, 1, 0, 0, 0, [TimeSpan]::Zero)
$zipStream = [System.IO.File]::Create($zipPath)
$archive = [System.IO.Compression.ZipArchive]::new($zipStream, [System.IO.Compression.ZipArchiveMode]::Create)

try {
    $publishedFiles = Get-ChildItem -Path $publishDir -Recurse -File | Sort-Object FullName
    foreach ($file in $publishedFiles) {
        $relPath = $file.FullName.Substring($publishDir.Length).TrimStart('\', '/').Replace('\', '/')
        $entry = $archive.CreateEntry($relPath, [System.IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = $fixedTimestamp

        $entryStream = $entry.Open()
        $fileStream = [System.IO.File]::OpenRead($file.FullName)
        try {
            $fileStream.CopyTo($entryStream)
        } finally {
            $entryStream.Dispose()
            $fileStream.Dispose()
        }
    }
} finally {
    $archive.Dispose()
    $zipStream.Dispose()
}

$zipFileInfo = Get-Item $zipPath
$zipSizeMb = [Math]::Round($zipFileInfo.Length / 1048576, 2)
Write-Host "OK: Pacote ZIP criado com sucesso: $($zipFileInfo.Length) bytes ($zipSizeMb MB)" -ForegroundColor Green

# 8. Validacao e inspecao pos-criacao do ZIP
Write-Host "[7/8] Validando integridade interna do pacote ZIP..." -ForegroundColor Yellow
$verifyZip = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if ($verifyZip.Entries.Count -eq 0) {
        Write-Error "O pacote ZIP gerado esta vazio!"
        exit 1
    }

    $foundMainExe = $false
    $foundUpdaterExe = $false

    foreach ($entry in $verifyZip.Entries) {
        $name = $entry.FullName

        # Protecao contra Path Traversal
        if ($name.Contains("..") -or [System.IO.Path]::IsPathRooted($name)) {
            Write-Error "Tentativa de Path Traversal detectada no pacote ZIP: $name"
            exit 1
        }

        # Extensoes proibidas
        $entryExt = [System.IO.Path]::GetExtension($name).ToLowerInvariant()
        if ($entryExt -in @(".db", ".db-wal", ".db-shm", ".cpb", ".sqlite")) {
            Write-Error "Arquivo de banco de dados proibido encontrado dentro do ZIP: $name"
            exit 1
        }

        if ($name -eq "CobraPix PC.exe") { $foundMainExe = $true }
        if ($name -eq "CobraPix.Updater.exe") { $foundUpdaterExe = $true }
    }

    if (-not $foundMainExe) {
        Write-Error "Executavel principal ('CobraPix PC.exe') ausente no pacote ZIP!"
        exit 1
    }
    if (-not $foundUpdaterExe) {
        Write-Error "Executavel do Updater ('CobraPix.Updater.exe') ausente no pacote ZIP!"
        exit 1
    }
} finally {
    $verifyZip.Dispose()
}
Write-Host "OK: Integridade e seguranca interna do ZIP 100% verificadas." -ForegroundColor Green

# 9. Calculo do SHA-256 e geracao de manifest.json e release-info.json
Write-Host "[8/8] Calculando SHA-256 e gerando manifestos..." -ForegroundColor Yellow
$sha256 = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash.ToUpperInvariant()

$releaseNotes = if ($Version -eq "1.0.7") {
    @(
        "Versao comercial 1.0.7 do CobraPix PC",
        "Estorno de pagamentos totais e parciais com auditoria completa e recomposicao automatica de divida",
        "Comprovante de pagamento e estorno em PDF com discriminacao da divida global inicial, parcela e saldo restante",
        "Paridade funcional com Android: juros contratuais, periodicidades flexiveis, agendamento por dia da semana e 5o dia util",
        "Geracao de Pix Copia e Cola EMVCo e mensagens de cobranca otimizadas para WhatsApp",
        "Modulo Lembretes com prioridades e recorrencia diaria, semanal e mensal",
        "Preservacao integral de dados e bancos de dados locais SQLite"
    )
} elseif ($Version -eq "1.0.5") {
    @(
        "Versao comercial 1.0.5 do CobraPix PC - cinco grandes melhorias de produtividade e controle",
        "Painel Inicial (Dashboard) com resumo de cobrancas pendentes/vencidas, proximas cobrancas, lembretes urgentes e atalhos rapidos",
        "Modulo Lembretes completo com prioridades, adiantamento rapido (snooze), historico e recorrencias diaria, semanal e mensal",
        "Pesquisa, filtros avancados e exportacao de relatorios financeiros para CSV e Excel (.xlsx)",
        "Diagnostico e registro de erros resiliente com sanitizacao de dados sensiveis e controle rotativo de logs",
        "Processo seguro de atualizacao com preservacao integral garantida do banco de dados e configuracoes locais"
    )
} else {
    @(
        "Versao comercial $Version do CobraPix PC",
        "Melhorias gerais de estabilidade, usabilidade e correcoes",
        "Preservacao integral de dados e bancos de dados SQLite locais",
        "Antes de atualizar, recomendamos fazer um backup preventivo dos seus dados."
    )
}

$manifestData = [ordered]@{
    version = $Version
    downloadUrl = "https://github.com/TraderReverso/CobraPix-PC-Releases/releases/download/v$Version/$zipName"
    sha256 = $sha256
    releaseNotes = $releaseNotes
    mandatory = $false
}

$releaseInfoData = [ordered]@{
    version = $Version
    package = $zipName
    sha256 = $sha256
    runtime = $Runtime
    selfContained = $SelfContained
    filesCount = $publishedFiles.Count
    packageSizeBytes = $zipFileInfo.Length
    packageSizeMb = $zipSizeMb
    createdAt = (Get-Date -Format "yyyy-MM-ddTHH:mm:ssZ")
}

$jsonSettings = @{ Depth = 10 }
$manifestJson = $manifestData | ConvertTo-Json @jsonSettings
$releaseInfoJson = $releaseInfoData | ConvertTo-Json @jsonSettings

$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
[System.IO.File]::WriteAllText($manifestPath, $manifestJson, $utf8NoBom)
[System.IO.File]::WriteAllText($releaseInfoPath, $releaseInfoJson, $utf8NoBom)

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "         PUBLICACAO $Version CONCLUIDA COM SUCESSO!          " -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
Write-Host "Versao           : $Version"
Write-Host "Runtime          : $Runtime (Self-Contained: $SelfContained)"
Write-Host "Pasta Publicacao : $publishDir"
Write-Host "Pacote ZIP       : $zipPath"
Write-Host "Tamanho ZIP      : $($zipFileInfo.Length) bytes ($zipSizeMb MB)"
Write-Host "SHA-256          : $sha256"
Write-Host "Manifesto        : $manifestPath"
Write-Host "Release Info     : $releaseInfoPath"
Write-Host "==========================================================" -ForegroundColor Green

return [PSCustomObject]@{
    Version = $Version
    PackagePath = $zipPath
    PackageName = $zipName
    Sha256 = $sha256
    SizeBytes = $zipFileInfo.Length
    ManifestPath = $manifestPath
    ReleaseInfoPath = $releaseInfoPath
    PublishDirectory = $publishDir
}
