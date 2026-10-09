<#
.SYNOPSIS
  Launches GPU Monitor and creates a desktop shortcut for it.
.DESCRIPTION
  Uses the first native GpuMonitor.exe found (installed copy, next to this script,
  or a local build). If none exists, falls back to the Python dashboard
  (gpu_dashboard.py), which opens http://127.0.0.1:8765/ itself.
  The desktop shortcut is created on first run and refreshed on later runs.
.PARAMETER ShortcutOnly
  Create/update the desktop shortcut without launching the app.
.PARAMETER NoShortcut
  Launch without touching the desktop shortcut (used by the shortcut itself).
#>
param([switch]$ShortcutOnly, [switch]$NoShortcut)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

$candidates = @(
    (Join-Path $env:LOCALAPPDATA 'Programs\GpuMonitor\GpuMonitor.exe'),
    (Join-Path $root 'GpuMonitor.exe'),
    (Join-Path $root 'src\GpuMonitor\publish\GpuMonitor.exe'),
    (Join-Path $root 'src\GpuMonitor\bin\Release\net10.0-windows\win-x64\GpuMonitor.exe')
)
$exe = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1

function Test-Python($path) {
    # A real interpreter exits 0; the Microsoft Store "python.exe" stub (not installed) does not.
    try { & $path -c 'import sys' 2>$null | Out-Null; return ($LASTEXITCODE -eq 0) } catch { return $false }
}

function Get-WindowedPython($path) {
    # pythonw.exe runs without a console window; use it when it sits beside the interpreter.
    $w = Join-Path (Split-Path -Parent $path) 'pythonw.exe'
    if (Test-Path -LiteralPath $w) { $w } else { $path }
}

function Find-Python {
    foreach ($cmd in @(Get-Command python.exe -All -ErrorAction SilentlyContinue)) {
        if (Test-Python $cmd.Source) { return Get-WindowedPython $cmd.Source }
    }
    # python.org installs may provide only the py launcher on PATH; ask it for the real interpreter.
    $launcher = Get-Command py.exe -ErrorAction SilentlyContinue
    if ($launcher) {
        try {
            $out = @(& $launcher.Source -3 -c 'import sys; print(sys.executable)' 2>$null)
            $real = $out | Select-Object -First 1
            if ($LASTEXITCODE -eq 0 -and $real -and (Test-Path -LiteralPath $real)) { return Get-WindowedPython $real }
        } catch { }
    }
    $null
}

$python = $null
if (-not $exe) {
    $python = Find-Python
    if (-not $python) {
        throw 'GpuMonitor.exe was not found and Python is not installed. Install GPU-Monitor-Setup.exe from the latest Windows workflow run, or install Python. No desktop shortcut was created.'
    }
}

if (-not $NoShortcut) {
    $shell = New-Object -ComObject WScript.Shell
    $link = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath('Desktop')) 'GPU Monitor.lnk'))
    if ($exe) {
        $link.TargetPath = $exe
        $link.Arguments = ''
        $link.WorkingDirectory = Split-Path -Parent $exe
        $link.IconLocation = "$exe,0"
    } else {
        $link.TargetPath = (Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe')
        $link.Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$PSCommandPath`" -NoShortcut"
        $link.WorkingDirectory = $root
        $link.IconLocation = (Join-Path $env:SystemRoot 'System32\perfmon.exe') + ',0'
    }
    $link.Description = 'GPU Monitor'
    $link.Save()
    Write-Host "Desktop shortcut created: $($link.FullName)"
}

if ($ShortcutOnly) { return }

if ($exe) {
    if (Get-Process GpuMonitor -ErrorAction SilentlyContinue) {
        Write-Host 'GPU Monitor is already running (check the system tray).'
        return
    }
    Start-Process -FilePath $exe -WorkingDirectory (Split-Path -Parent $exe)
    return
}

Start-Process -FilePath $python -ArgumentList "`"$(Join-Path $root 'gpu_dashboard.py')`"" -WorkingDirectory $root -WindowStyle Hidden
