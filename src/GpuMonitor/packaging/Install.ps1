$ErrorActionPreference = 'Stop'
$target = Join-Path $env:LOCALAPPDATA 'Programs\GpuMonitor'
$source = $PSScriptRoot
if (-not (Test-Path -LiteralPath (Join-Path $source 'GpuMonitor.exe'))) { throw 'Extract the complete app ZIP before installing.' }
if (Get-Process GpuMonitor -ErrorAction SilentlyContinue) { throw 'Close GPU Monitor (including its tray icon) before installing or updating.' }
New-Item -ItemType Directory -Path $target -Force | Out-Null
Get-ChildItem -LiteralPath $source -File | Where-Object { $_.Name -ne 'Install.cmd' -and $_.Name -ne 'Install.ps1' } | Copy-Item -Destination $target -Force
if (Test-Path -LiteralPath (Join-Path $source 'runtime')) { Copy-Item -LiteralPath (Join-Path $source 'runtime') -Destination $target -Recurse -Force }
$shell = New-Object -ComObject WScript.Shell
$link = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Programs')) 'GPU Monitor.lnk'))
$link.TargetPath = Join-Path $target 'GpuMonitor.exe'
$link.WorkingDirectory = $target
$link.Save()
$key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\GpuMonitor'
New-Item -Path $key -Force | Out-Null
New-ItemProperty -Path $key -Name DisplayName -Value 'GPU Monitor' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $key -Name DisplayVersion -Value '1.0.0' -PropertyType String -Force | Out-Null
New-ItemProperty -Path $key -Name InstallLocation -Value $target -PropertyType String -Force | Out-Null
New-ItemProperty -Path $key -Name DisplayIcon -Value (Join-Path $target 'GpuMonitor.exe') -PropertyType String -Force | Out-Null
$uninstall = 'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "' + (Join-Path $target 'Uninstall.ps1') + '"'
New-ItemProperty -Path $key -Name UninstallString -Value $uninstall -PropertyType String -Force | Out-Null
Write-Host 'GPU Monitor installed. Launch it from the Start menu.'
