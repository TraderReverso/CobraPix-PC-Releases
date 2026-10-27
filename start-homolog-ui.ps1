# ==============================================================================
# Script: start-homolog-ui.ps1
# Finalidade: Execucao Segura da Interface Grafica WPF do CobraPix PC em Modo
#             de Homologacao do Modulo Credito & Giro com Base Sintetica Temporaria
# ==============================================================================
# RESTRICOES RIGOROSAS:
# 1. Base real (%LOCALAPPDATA%\CobraPix\cobrapix.db) NUNCA e acessada para escrita.
# 2. Arquivo sentinela 'credit_production.sentinel' NUNCA e criado ou modificado.
# 3. Travas de seguranca (CreditProductionSafetyGuard) permanecem 100% ativas.
# 4. Hash SHA-256 e tamanho da base real sao auditados antes e apos a sessao.
#
# CODIGOS DE SAIDA RETORNADOS:
#   0 : Sucesso - Teste concluido e auditoria da producao 100% aprovada (intacta).
#   1 : Falha Operacional - Instancia preexistente, inicializacao falhou, saida
#       prematura da interface ou erro operacional (producao intacta, teste incompleto).
#   2 : Incidente de Seguranca - Divergencia detectada no hash/tamanho da base real.
#   3 : Inconclusivo - Falha ao auditar a base de producao pos-execucao.
#
# LIMITES DE RESILIENCIA DO BLOCO FINALLY:
# O bloco 'finally' do PowerShell garante execucao sob fluxos normais, excecoes
# tratadas e interrupcoes via Ctrl+C no console. Porem, limites da plataforma
# Windows impedem execucao de 'finally' sob:
# - Encerramento forcado por 'taskkill /F' ou 'Process.Kill()';
# - Fechamento abrupto da janela do terminal pelo botao 'X' do Windows;
# - Queda de energia ou encerramento do processo hospedeiro do OS.
# ==============================================================================

[CmdletBinding()]
param(
    [switch]$KeepSandbox = $false
)

$ErrorActionPreference = "Stop"

Write-Host "`n======================================================================" -ForegroundColor Cyan
Write-Host " COBRAPIX PC - HOMOLOGACAO SEGURA (MODULO CREDITO & GIRO)" -ForegroundColor Cyan
Write-Host "======================================================================`n" -ForegroundColor Cyan

# ------------------------------------------------------------------------------
# 0. CAPTURA DO AMBIENTE ORIGINAL (Para restauracao garantida)
# ------------------------------------------------------------------------------
$origExecutionMode = [System.Environment]::GetEnvironmentVariable("COBRAPIX_EXECUTION_MODE", "Process")
$origAllowTestDb   = [System.Environment]::GetEnvironmentVariable("COBRAPIX_ALLOW_TEST_DATABASE", "Process")
$origDatabasePath  = [System.Environment]::GetEnvironmentVariable("COBRAPIX_DATABASE_PATH", "Process")
$origTestToken     = [System.Environment]::GetEnvironmentVariable("COBRAPIX_SYNTHETIC_TEST_TOKEN", "Process")

function Restore-Environment {
    [System.Environment]::SetEnvironmentVariable("COBRAPIX_EXECUTION_MODE", $script:origExecutionMode, "Process")
    [System.Environment]::SetEnvironmentVariable("COBRAPIX_ALLOW_TEST_DATABASE", $script:origAllowTestDb, "Process")
    [System.Environment]::SetEnvironmentVariable("COBRAPIX_DATABASE_PATH", $script:origDatabasePath, "Process")
    [System.Environment]::SetEnvironmentVariable("COBRAPIX_SYNTHETIC_TEST_TOKEN", $script:origTestToken, "Process")
    Write-Host "[RESTAURACAO] Variaveis de ambiente restauradas ao estado original." -ForegroundColor DarkGray
}

$prodDb              = [System.IO.Path]::Combine($env:LOCALAPPDATA, "CobraPix", "cobrapix.db")
$canonicalProdDb     = $null
$prodHashBefore      = $null
$prodItemBefore      = $null
$canonicalSandbox    = $null
$sessionToken        = $null
$specificSessionFiles = @()
$appProcess          = $null
$auditAttempted      = $false

# Funcao de auditoria forense da base de producao
function Invoke-ProductionAudit {
    param(
        [string]$ContextReason = "Encerramento da sessao"
    )

    $script:auditAttempted = $true
    Write-Host "`n[AUDITORIA] Executando auditoria forense da base real ($ContextReason)..." -ForegroundColor Yellow

    if (-not (Test-Path -LiteralPath $script:prodDb)) {
        Write-Host "[ALERTA CRITICO] Base real nao encontrada durante auditoria: $($script:prodDb)" -ForegroundColor Red
        return "INCONCLUSIVE"
    }

    try {
        $prodItemAfter = Get-Item -LiteralPath $script:prodDb
        $prodHashAfter = (Get-FileHash -LiteralPath $script:prodDb -Algorithm SHA256).Hash

        Write-Host "      Tamanho Real Inicial: $($script:prodItemBefore.Length) bytes | Final: $($prodItemAfter.Length) bytes" -ForegroundColor Gray
        Write-Host "      Data Modif. Inicial:  $($script:prodItemBefore.LastWriteTimeUtc) UTC" -ForegroundColor Gray
        Write-Host "      Data Modif. Final:    $($prodItemAfter.LastWriteTimeUtc) UTC" -ForegroundColor Gray
        Write-Host "      Hash SHA-256 Inicial: $($script:prodHashBefore)" -ForegroundColor Gray
        Write-Host "      Hash SHA-256 Final:   $prodHashAfter" -ForegroundColor Gray

        $isHashIdentical   = ($prodHashAfter -eq $script:prodHashBefore)
        $isLengthIdentical = ($prodItemAfter.Length -eq $script:prodItemBefore.Length)
        $isTimeIdentical   = ($prodItemAfter.LastWriteTimeUtc -eq $script:prodItemBefore.LastWriteTimeUtc)

        if (-not $isHashIdentical -or -not $isLengthIdentical -or -not $isTimeIdentical) {
            # Geracao de relatorio de incidente
            $projectDir = $PSScriptRoot
            if ([string]::IsNullOrWhiteSpace($projectDir)) { $projectDir = (Get-Location).Path }
            $incidentLog = [System.IO.Path]::Combine($projectDir, "audit_divergence_incident_$(Get-Date -Format 'yyyyMMdd_HHmmss').log")

            $reportContent = @(
                "=== RELATORIO DE DIVERGENCIA DE AUDITORIA ===",
                "Data/Hora UTC: $(Get-Date -AsUtc -Format 'o')",
                "Motivo: $ContextReason",
                "Base Real: $($script:canonicalProdDb)",
                "Hash Inicial: $($script:prodHashBefore)",
                "Hash Final:   $prodHashAfter",
                "Tamanho Inicial: $($script:prodItemBefore.Length)",
                "Tamanho Final:   $($prodItemAfter.Length)",
                "Data Modif. Inicial: $($script:prodItemBefore.LastWriteTimeUtc)",
                "Data Modif. Final:   $($prodItemAfter.LastWriteTimeUtc)",
                "Sandbox Path: $($script:canonicalSandbox)",
                "Session Token: $($script:sessionToken)",
                "Evidencia da Sandbox: PRESERVADA PARA PERICIA"
            )
            Set-Content -LiteralPath $incidentLog -Value $reportContent -Encoding UTF8

            Write-Host "`n======================================================================" -ForegroundColor Red
            Write-Host " [ALERTA DE SEGURANCA] DIVERGENCIA DETECTADA NA BASE REAL!" -ForegroundColor Red
            Write-Host " Relatorio forense gerado em: $incidentLog" -ForegroundColor Red
            Write-Host " A base da sandbox foi preservada em: $($script:canonicalSandbox)" -ForegroundColor Red
            Write-Host "======================================================================`n" -ForegroundColor Red

            return "DIVERGENCE"
        }

        Write-Host "      Auditoria concluida: Base real 100% intacta e preservada." -ForegroundColor Green
        return "APPROVED"
    }
    catch {
        Write-Host "[ALERTA] Falha de I/O ao auditar a base de producao: $_" -ForegroundColor Red
        return "INCONCLUSIVE"
    }
}

# Funcao de limpeza controlada da sandbox (somente arquivos da sessao)
function Remove-SandboxFilesSafe {
    if ($KeepSandbox) {
        Write-Host "Base temporaria preservada para inspecao em: $($script:canonicalSandbox)" -ForegroundColor Cyan
        return
    }

    $tempDir = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\', '/')
    Write-Host "Removendo arquivos exclusivos da sandbox da sessao..." -ForegroundColor Gray

    foreach ($file in $script:specificSessionFiles) {
        if (Test-Path -LiteralPath $file) {
            $fCanonical = [System.IO.Path]::GetFullPath($file)
            if ($fCanonical.StartsWith($tempDir + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -and
                [System.IO.Path]::GetFileName($fCanonical).StartsWith("test_cobrapix_homolog_$($script:sessionToken)")) {
                Remove-Item -LiteralPath $fCanonical -Force -ErrorAction SilentlyContinue
            }
        }
    }
    Write-Host "Arquivos da sandbox temporaria removidos com sucesso." -ForegroundColor Gray
}

try {
    # --------------------------------------------------------------------------
    # 1. VERIFICACAO DE INSTANCIA PREEXISTENTE DO COBRAPIX PC
    # --------------------------------------------------------------------------
    Write-Host "[1/7] Verificando processos preexistentes do CobraPix PC..." -ForegroundColor Yellow
    $runningProcesses = Get-Process -Name "CobraPix PC", "CobraPix PC.exe" -ErrorAction SilentlyContinue
    if ($runningProcesses -and $runningProcesses.Count -gt 0) {
        $pids = ($runningProcesses | ForEach-Object { $_.Id }) -join ", "
        Write-Host "`n[ERRO DE CONCORRENCIA] Ja existe uma instancia do CobraPix PC em execucao (PID(s): $pids)." -ForegroundColor Red
        Write-Host "O CobraPix PC utiliza um Mutex global de instancia unica. Se executado agora, o processo de homologacao" -ForegroundColor Yellow
        Write-Host "encerraria silenciosamente em menos de 1 segundo sem abrir nenhuma interface grafica." -ForegroundColor Yellow
        Write-Host "Feche a instancia existente antes de executar o script de homologacao.`n" -ForegroundColor Yellow

        Restore-Environment
        exit 1
    }
    Write-Host "      Nenhuma instancia preexistente em execucao. Prosseguindo." -ForegroundColor Green

    # --------------------------------------------------------------------------
    # 2. AUDITORIA PREVIA DA BASE REAL DE PRODUCAO
    # --------------------------------------------------------------------------
    $prodDir = [System.IO.Path]::Combine($env:LOCALAPPDATA, "CobraPix")
    $prodDb  = [System.IO.Path]::Combine($prodDir, "cobrapix.db")

    Write-Host "`n[2/7] Auditando estado inicial da base real de producao..." -ForegroundColor Yellow
    if (-not (Test-Path -LiteralPath $prodDb)) {
        throw "Base de producao nao encontrada em '$prodDb'. Abortando por precaucao."
    }

    $prodItemBefore  = Get-Item -LiteralPath $prodDb
    $prodHashBefore  = (Get-FileHash -LiteralPath $prodDb -Algorithm SHA256).Hash
    $canonicalProdDir = [System.IO.Path]::GetFullPath($prodDir).TrimEnd('\', '/')
    $canonicalProdDb  = [System.IO.Path]::GetFullPath($prodDb)

    Write-Host "      Caminho Real:      $canonicalProdDb" -ForegroundColor Gray
    Write-Host "      Tamanho Inicial:   $($prodItemBefore.Length) bytes" -ForegroundColor Gray
    Write-Host "      Ultima Modificacao: $($prodItemBefore.LastWriteTimeUtc) UTC" -ForegroundColor Gray
    Write-Host "      Hash SHA-256:      $prodHashBefore" -ForegroundColor Green

    # --------------------------------------------------------------------------
    # 3. DEFINICAO DA BASE SINTETICA TEMPORARIA E VALIDACOES CANONICAS ESTRITAS
    # --------------------------------------------------------------------------
    Write-Host "`n[3/7] Validando isolamento canonico da base de homologacao..." -ForegroundColor Yellow

    $sessionToken    = [System.Guid]::NewGuid().ToString("N")
    $sandboxFileName = "test_cobrapix_homolog_${sessionToken}.db"
    $tempDir         = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\', '/')
    $rawSandboxPath  = [System.IO.Path]::Combine($tempDir, $sandboxFileName)
    $canonicalSandbox = [System.IO.Path]::GetFullPath($rawSandboxPath)

    if (-not $canonicalSandbox.StartsWith($tempDir + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "[SEGURANCA] O caminho resolvido da sandbox ('$canonicalSandbox') nao reside dentro de TEMP ('$tempDir'). Abortando."
    }
    if ($canonicalSandbox -eq $canonicalProdDb) {
        throw "[SEGURANCA] O caminho da sandbox coincide com o banco de producao! Abortando imediatamente."
    }
    if ($canonicalSandbox.StartsWith($canonicalProdDir + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or
        $canonicalSandbox -eq $canonicalProdDir) {
        throw "[SEGURANCA] O caminho da sandbox reside dentro do diretorio de producao! Abortando imediatamente."
    }
    if ([System.IO.Path]::GetFileName($canonicalSandbox) -ne $sandboxFileName) {
        throw "[SEGURANCA] Nome do arquivo da sandbox nao coincide com o token da sessao ($sandboxFileName). Abortando."
    }

    Write-Host "      Caminho da Sandbox: $canonicalSandbox" -ForegroundColor Gray
    Write-Host "      Token da Sessao:    $sessionToken" -ForegroundColor Gray

    $specificSessionFiles = @(
        $canonicalSandbox,
        "$canonicalSandbox-wal",
        "$canonicalSandbox-shm",
        "$canonicalSandbox-journal"
    )

    # --------------------------------------------------------------------------
    # 4. INICIALIZACAO DA BASE SINTETICA ISOLADA (CLI COM CONFERENCIA DE PRODUCAO)
    # --------------------------------------------------------------------------
    Write-Host "`n[4/7] Inicializando base sintetica com migracao e licenca PRO de teste..." -ForegroundColor Yellow

    [System.Environment]::SetEnvironmentVariable("COBRAPIX_EXECUTION_MODE", "TEST", "Process")
    [System.Environment]::SetEnvironmentVariable("COBRAPIX_ALLOW_TEST_DATABASE", "1", "Process")
    [System.Environment]::SetEnvironmentVariable("COBRAPIX_DATABASE_PATH", $canonicalSandbox, "Process")
    [System.Environment]::SetEnvironmentVariable("COBRAPIX_SYNTHETIC_TEST_TOKEN", $sessionToken, "Process")

    $initOutput   = dotnet run --no-build -- --init-homolog-sandbox "$canonicalSandbox"
    $initExitCode = $LASTEXITCODE

    if ($initExitCode -ne 0 -or $initOutput -notmatch "HOMOLOG_SANDBOX_INITIALIZED") {
        throw "Falha ao inicializar a base sintetica de homologacao. Codigo: $initExitCode. Saida: $initOutput"
    }

    # Auditoria imediata pos-inicializacao
    $auditMid = Invoke-ProductionAudit -ContextReason "Pos-inicializacao da base sintetica"
    if ($auditMid -ne "APPROVED") {
        Restore-Environment
        exit 2
    }
    Write-Host "      Base sintetica inicializada. Producao confirmada intacta." -ForegroundColor Green

    # --------------------------------------------------------------------------
    # 5. EXECUCAO DO APLICATIVO EM PROCESSO FILHO COM VARIAVEIS EXPLICITAS
    # --------------------------------------------------------------------------
    Write-Host "`n[5/7] Configurando processo filho isolado para execucao da interface WPF..." -ForegroundColor Yellow

    $projectDir = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($projectDir)) { $projectDir = (Get-Location).Path }

    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "dotnet"
    $psi.Arguments = "run --no-build"
    $psi.WorkingDirectory = $projectDir
    $psi.UseShellExecute = $false

    $psi.EnvironmentVariables["COBRAPIX_EXECUTION_MODE"]       = "TEST"
    $psi.EnvironmentVariables["COBRAPIX_ALLOW_TEST_DATABASE"]  = "1"
    $psi.EnvironmentVariables["COBRAPIX_DATABASE_PATH"]        = $canonicalSandbox
    $psi.EnvironmentVariables["COBRAPIX_SYNTHETIC_TEST_TOKEN"] = $sessionToken

    Write-Host "      Processo configurado com variaveis de teste explicitas." -ForegroundColor Gray
    Write-Host "`n[6/7] Abrindo interface grafica WPF do CobraPix PC..." -ForegroundColor Green
    Write-Host "----------------------------------------------------------------------" -ForegroundColor DarkGray
    Write-Host " ROTEIRO DE TESTE NO MODULO CREDITO & GIRO:" -ForegroundColor Cyan
    Write-Host " 1. Acesse 'Credito & Giro' no menu lateral e confirme badge 'PRO ATIVO'." -ForegroundColor White
    Write-Host " 2. Cliente 'Vitor0 - Cliente Teste Sintetico' vira selecionado." -ForegroundColor White
    Write-Host " 3. Teste preencher o contrato com periodicidades (Mensal, Semanal, etc.)." -ForegroundColor White
    Write-Host " 4. Clique em 'Salvar Contrato (Rascunho)' -> confira inclusao na grade." -ForegroundColor White
    Write-Host " 5. Selecione o contrato e clique em 'Desembolsar' para ativalo." -ForegroundColor White
    Write-Host " 6. Na aba 'Pagamentos', selecione o contrato no ComboBox e registre pagamento." -ForegroundColor White
    Write-Host " 7. Feche a janela principal do CobraPix PC para concluir o teste." -ForegroundColor Yellow
    Write-Host "----------------------------------------------------------------------`n" -ForegroundColor DarkGray

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $appProcess = [System.Diagnostics.Process]::Start($psi)
    if ($null -eq $appProcess) {
        throw "Falha ao iniciar o processo filho da aplicacao WPF."
    }

    # Aguarda 3 segundos para validar se o processo inicializou e nao encerrou prematuramente
    Start-Sleep -Seconds 3
    if ($appProcess.HasExited) {
        $fastExitCode = $appProcess.ExitCode
        $elapsedFast  = [math]::Round($stopwatch.Elapsed.TotalSeconds, 2)

        Write-Host "`n[ERRO DE INICIALIZACAO] O processo da interface encerrou prematuramente apos ${elapsedFast}s com codigo $fastExitCode." -ForegroundColor Red
        Write-Host "A interface grafica nao permaneceu ativa para interacao humana. O teste NAO foi concluido." -ForegroundColor Yellow

        $auditPremature = Invoke-ProductionAudit -ContextReason "Encerramento prematuro do processo filho"
        if ($auditPremature -eq "DIVERGENCE") {
            Restore-Environment
            exit 2
        }
        if ($auditPremature -eq "INCONCLUSIVE") {
            Restore-Environment
            exit 3
        }

        Remove-SandboxFilesSafe
        Restore-Environment
        exit 1
    }

    Write-Host "Interface grafica ativa e em execucao. Aguardando encerramento pelo usuario..." -ForegroundColor Cyan

    # Aguarda o usuario fechar a janela do aplicativo
    $appProcess.WaitForExit()
    $stopwatch.Stop()
    $appExitCode = $appProcess.ExitCode
    $totalElapsedSeconds = [math]::Round($stopwatch.Elapsed.TotalSeconds, 1)

    Write-Host "`nJanela do CobraPix PC encerrada pelo usuario apos ${totalElapsedSeconds}s (Codigo de Saida: $appExitCode)." -ForegroundColor Gray

    # Se a sessao durou menos de 5 segundos, nao pode ser considerada conclusiva para interacao humana
    if ($totalElapsedSeconds -lt 5.0) {
        Write-Host "[ALERTA] Tempo de execucao inferior a 5 segundos (${totalElapsedSeconds}s). Teste classificado como incompleto." -ForegroundColor Yellow
        $auditShort = Invoke-ProductionAudit -ContextReason "Execucao abreviada (< 5s)"
        if ($auditShort -eq "DIVERGENCE") {
            Restore-Environment
            exit 2
        }
        if ($auditShort -eq "INCONCLUSIVE") {
            Restore-Environment
            exit 3
        }
        Remove-SandboxFilesSafe
        Restore-Environment
        exit 1
    }

    # --------------------------------------------------------------------------
    # 6. AUDITORIA FINAL FORENSE DE INTEGRIDADE DA BASE REAL
    # --------------------------------------------------------------------------
    Write-Host "`n[7/7] Validando resultado da auditoria final..." -ForegroundColor Yellow
    $finalAuditStatus = Invoke-ProductionAudit -ContextReason "Conclusao do teste da interface WPF"

    if ($finalAuditStatus -eq "DIVERGENCE") {
        Restore-Environment
        exit 2
    }

    if ($finalAuditStatus -eq "INCONCLUSIVE") {
        Write-Host "[RESULTADO] Auditoria final INCONCLUSIVA. A sandbox foi preservada em: $canonicalSandbox" -ForegroundColor Yellow
        Restore-Environment
        exit 3
    }

    # Se a auditoria foi aprovada mas a aplicacao WPF retornou erro:
    if ($appExitCode -ne 0) {
        Write-Host "[ALERTA] A interface encerrou com codigo de saida nao-zero ($appExitCode), porem a producao esta 100% intacta." -ForegroundColor Yellow
        Remove-SandboxFilesSafe
        Restore-Environment
        exit 1
    }

    # Sucesso completo: teste concluido, interface encerrou normalmente e producao 100% intacta
    Write-Host "`n======================================================================" -ForegroundColor Green
    Write-Host " HOMOLOGACAO CONCLUIDA COM SUCESSO! (CODIGO 0)" -ForegroundColor Green
    Write-Host " A base real de producao permaneceu TOTALMENTE INTACTA." -ForegroundColor Green
    Write-Host " Bytes e Hash SHA-256 identicos. Nenhuma escrita ocorreu na producao." -ForegroundColor Green
    Write-Host "======================================================================`n" -ForegroundColor Green

    Remove-SandboxFilesSafe
    Restore-Environment
    exit 0
}
catch {
    Write-Host "`n[EXCECAO CAPTURADA]: $_" -ForegroundColor Red

    # Tenta encerrar o processo filho se ainda estiver rodando
    if ($appProcess -and -not $appProcess.HasExited) {
        try {
            Write-Host "Encerrando processo filho ativo de forma controlada..." -ForegroundColor Yellow
            $appProcess.CloseMainWindow() | Out-Null
            if (-not $appProcess.WaitForExit(3000)) {
                $appProcess.Kill()
            }
        } catch { }
    }

    # Sempre tenta auditar a base real caso tenha ocorrido preparo
    if ($prodItemBefore -and $prodHashBefore) {
        $catchAudit = Invoke-ProductionAudit -ContextReason "Excecao no fluxo de execucao"
        if ($catchAudit -eq "DIVERGENCE") {
            Write-Host "[SEGURANCA] Sandbox preservada para investigacao: $canonicalSandbox" -ForegroundColor Yellow
            Restore-Environment
            exit 2
        }
        if ($catchAudit -eq "INCONCLUSIVE") {
            Write-Host "[SEGURANCA] Sandbox preservada para investigacao: $canonicalSandbox" -ForegroundColor Yellow
            Restore-Environment
            exit 3
        }
    }

    # Se a base real esta intacta, remove a sandbox temporaria (se nao foi pedido KeepSandbox) e sai com codigo 1
    Remove-SandboxFilesSafe
    Restore-Environment
    exit 1
}
finally {
    # Garantia de restauracao no caso de saidas anormais
    Restore-Environment
}
