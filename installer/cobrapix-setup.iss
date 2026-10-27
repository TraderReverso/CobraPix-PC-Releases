; =====================================================================
; Script Inno Setup 6 oficial para Instalador do CobraPix PC
; Versão: 1.0.7 (x64)
; =====================================================================

#define MyAppName "CobraPix PC"
#define MyAppVersion "1.0.7"
#define MyAppPublisher "Trader Reverso"
#define MyAppURL "https://github.com/TraderReverso/CobraPix-PC-Releases"
#define MyAppExeName "CobraPix PC.exe"
#define MyAppId "{{B7F2990B-4D2A-4F8C-9C9D-9F8C62BC4815}}"

[Setup]
; Identificação da Aplicação
AppId={#MyAppId}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Diretório de Instalação e Arquitetura x64
DefaultDirName={autopf}\CobraPix
DefaultGroupName=CobraPix
AllowNoIcons=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline dialog

; Saída do Instalador
OutputDir=..\artifacts\package\1.0.7
OutputBaseFilename=CobraPix PC Setup 1.0.7
SetupIconFile=..\Assets\app_icon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}

; Compressão e Visual Moderno
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
DisableWelcomePage=no
DisableDirPage=no

; Fechamento gracioso se a aplicação estiver em execução
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName},CobraPix.Updater.exe

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Binários publicados da versão 1.0.7 (exclusão estrita de bancos, backups, PDBs e arquivos de teste)
Source: "..\artifacts\publish\1.0.7\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.db,*.db-wal,*.db-shm,*.sqlite,*.sqlite3,*.cpb,*.bak,*.pdb,test_*,version_info.txt,*.ps1,manifest_test*.json"

[Icons]
; Atalho no Menu Iniciar
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"
; Atalho opcional na Área de Trabalho
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; Inicialização da aplicação ao término da instalação / atualização
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: WizardSilent
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent; Check: not WizardSilent

; NOTA DE SEGURANÇA ABSOLUTA:
; Nenhuma diretiva [UninstallDelete] é adicionada para a pasta de dados do usuário (%LOCALAPPDATA%\CobraPix).
; O banco de dados cobrapix.db e os backups .cpb são 100% preservados durante qualquer desinstalação.
