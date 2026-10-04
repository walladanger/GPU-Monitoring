[Setup]
AppId=GpuMonitor
AppName=GPU Monitor
AppVersion=1.0.0
DefaultDirName={localappdata}\Programs\GpuMonitor
DefaultGroupName=GPU Monitor
PrivilegesRequired=lowest
OutputDir=..\artifacts
OutputBaseFilename=GPU-Monitor-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
AppMutex=Local\GpuMonitor
UninstallDisplayIcon={app}\GpuMonitor.exe

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Excludes: "Install.cmd,Install.ps1,Uninstall.ps1,*.pdb,self-test-result.txt,ui-check-result.txt,dashboard-preview.png"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\GPU Monitor"; Filename: "{app}\GpuMonitor.exe"

[Run]
Filename: "{app}\GpuMonitor.exe"; Description: "Launch GPU Monitor"; Flags: nowait postinstall skipifsilent
