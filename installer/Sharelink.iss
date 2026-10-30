; Instalador do Sharelink (Inno Setup 6).
; Gerado pelo scripts\release.ps1:  ISCC /DAppVersion=1.2.3 installer\Sharelink.iss
; Instala só para o usuário atual (sem administrador) e já inclui o .NET.

#ifndef AppVersion
  #error Informe a versão: ISCC /DAppVersion=X.Y.Z installer\Sharelink.iss
#endif
#ifndef SourceDir
  #define SourceDir "..\artifacts\app"
#endif

#define AppName "Sharelink"
#define AppExe "Sharelink.exe"
#define RepoUrl "https://github.com/GabrielAlvesB/Sharelink"

[Setup]
; O AppId identifica o app entre versões: NUNCA mude, senão as atualizações instalam em duplicado.
AppId={{8F3C2A51-6B7D-4E2A-9C1F-3D5E7A9B0C42}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=Gabriel Alves
AppPublisherURL={#RepoUrl}
AppSupportURL={#RepoUrl}/issues
AppUpdatesURL={#RepoUrl}/releases
VersionInfoVersion={#AppVersion}
DefaultDirName={localappdata}\Programs\{#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=Sharelink-Setup-{#AppVersion}
SetupIconFile=..\assets\Sharelink.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
; Fecha o Sharelink aberto antes de substituir os arquivos (usado nas atualizações).
CloseApplications=force
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[InstallDelete]
; Remove arquivos de versões antigas antes de copiar a nova.
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Instalação normal: oferece abrir no final. Atualização silenciosa: reabre sozinho.
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#AppExe}"; Flags: nowait skipifnotsilent

[UninstallRun]
; Fecha o Sharelink antes de desinstalar (as capturas e configurações do usuário são mantidas).
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "FecharSharelink"
