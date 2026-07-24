; Inno Setup script for VideoAnalysisDesktop
; Generates a per-machine x64 installer requiring admin privileges.
;
; Usage (normally via desktop\build\build_all.bat):
;   iscc installer.iss
;
; Prerequisites:
;   - Build the WPF app:      dotnet publish -r win-x64 --self-contained true
;   - Stage the engine:        python build/stage_engine.py --output-dir engine ...
;     (engine.manifest.json and engine_check.py must be present)

#define MyAppName "VideoAnalysisDesktop"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "Company"
#define MyAppURL "https://company.com"
#define MyAppExeName "VideoAnalysisDesktop.exe"

; Build defaults.  A release operator may override either directory with:
;   iscc /DAppPublishDir="F:\Publish\VideoAnalysisDesktop" /DEngineDir="...\engine" installer.iss
#ifndef AppPublishDir
  #define AppPublishDir AddBackslash(SourcePath) + "..\VideoAnalysisDesktop.App\bin\Release\net10.0-windows\win-x64\publish"
#endif
#ifndef EngineDir
  #define EngineDir AddBackslash(SourcePath) + "..\engine"
#endif

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}

; Per-machine install (requires admin)
PrivilegesRequired=admin
ArchitecturesInstallIn64BitMode=x64compatible

DefaultDirName={commonpf}\Company\VideoAnalysisDesktop
DefaultGroupName=VideoAnalysisDesktop
AllowNoIcons=yes

; Output
; SourcePath is the directory that contains this .iss file.  Keeping output
; and input paths rooted here avoids dependence on the caller's working dir.
OutputDir={#SourcePath}\output
OutputBaseFilename=VideoAnalysisDesktop-Setup-x64-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile={#SourcePath}\..\VideoAnalysisDesktop.App\Assets\app-icon.ico
UninstallDisplayIcon={app}\app\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no

; Signing (uncomment for production)
; SignTool=mycert

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Dirs]
; The application runs as a normal user after this per-machine installation.
; Only mutable job/model/cache locations are writable by standard users;
; the configuration directory remains read-only to them.
Name: "{commonappdata}\Company\VideoAnalysisDesktop\jobs"; Permissions: users-modify
Name: "{commonappdata}\Company\VideoAnalysisDesktop\cache"; Permissions: users-modify
Name: "{commonappdata}\Company\VideoAnalysisDesktop\models"; Permissions: users-modify
Name: "{commonappdata}\Company\VideoAnalysisDesktop\config"; Permissions: users-readexec

[Files]
; WPF Application (self-contained publish output)
Source: "{#AppPublishDir}\*"; DestDir: "{app}\app"; Flags: ignoreversion recursesubdirs createallsubdirs

; Immutable engine runtime. It includes Python, extracted dependency wheels,
; and the repository's Python packages under engine\python\app.
Source: "{#EngineDir}\python\*"; DestDir: "{app}\engine\python"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#EngineDir}\engine.manifest.json"; DestDir: "{app}\engine"; Flags: ignoreversion
Source: "{#EngineDir}\engine_check.py"; DestDir: "{app}\engine"; Flags: ignoreversion
Source: "{#EngineDir}\requirements-lock.txt"; DestDir: "{app}\engine"; Flags: ignoreversion

; FFmpeg binaries
; Copy adjacent DLLs too; some FFmpeg distributions are not fully static.
Source: "{#EngineDir}\ffmpeg\*"; DestDir: "{app}\engine\ffmpeg"; Flags: ignoreversion recursesubdirs createallsubdirs

; Offline ML models. build_all.bat refuses to build an installer from an
; engine staged with --skip-models.
Source: "{#EngineDir}\models\*"; DestDir: "{commonappdata}\Company\VideoAnalysisDesktop\models"; Flags: ignoreversion recursesubdirs createallsubdirs uninsneveruninstall

; Licenses
Source: "{#EngineDir}\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion recursesubdirs createallsubdirs

; Non-technical operator documentation, available without opening a browser.
Source: "{#SourcePath}\..\docs\用户使用手册.html"; DestDir: "{app}\docs"; Flags: ignoreversion

; VC++ Redistributable (check if needed)
; Source: "redist\VC_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{group}\VideoAnalysisDesktop"; Filename: "{app}\app\{#MyAppExeName}"
Name: "{group}\用户使用手册"; Filename: "{app}\docs\用户使用手册.html"
Name: "{group}\Uninstall VideoAnalysisDesktop"; Filename: "{uninstallexe}"
Name: "{commondesktop}\VideoAnalysisDesktop"; Filename: "{app}\app\{#MyAppExeName}"

[Run]
Filename: "{app}\app\{#MyAppExeName}"; Description: "Launch VideoAnalysisDesktop"; Flags: nowait postinstall skipifsilent

; VC++ Runtime check (uncomment for production)
; Filename: "{tmp}\VC_redist.x64.exe"; Parameters: "/quiet /norestart"; StatusMsg: "Installing VC++ Runtime..."; Check: IsVCRuntimeMissing

[Code]
function IsVCRuntimeMissing: Boolean;
begin
  Result := not RegKeyExists(HKLM, 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64');
end;
