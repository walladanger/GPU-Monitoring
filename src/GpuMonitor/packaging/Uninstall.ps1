$ErrorActionPreference = 'Stop'
$expected = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\GpuMonitor'))
$actual = [IO.Path]::GetFullPath($PSScriptRoot)
if ($actual -ne $expected) { throw 'Uninstall must run from the GPU Monitor installation folder.' }
if (Get-Process GpuMonitor -ErrorAction SilentlyContinue) { throw 'Close GPU Monitor (including its tray icon) before uninstalling.' }
if ((Read-Host 'Remove GPU Monitor? Type YES to confirm') -ne 'YES') { exit }
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'GPU Monitor.lnk'
if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut }
Remove-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\GpuMonitor' -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $expected -Recurse -Force
Write-Host 'GPU Monitor removed. Your saved preferences were kept.'
