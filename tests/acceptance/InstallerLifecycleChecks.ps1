param([Parameter(Mandatory = $true)][string]$Installer)
$ErrorActionPreference = 'Stop'
$installRoot = Join-Path $env:RUNNER_TEMP ('GPU Monitor install QA ' + [Guid]::NewGuid().ToString('N'))
$settingsPath = Join-Path $env:LOCALAPPDATA 'GpuMonitor/settings.json'
$original = if (Test-Path -LiteralPath $settingsPath) { [IO.File]::ReadAllBytes($settingsPath) } else { $null }
$desktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) 'GPU Monitor.lnk'
$startLink = Join-Path ([Environment]::GetFolderPath('Programs')) 'GPU Monitor/GPU Monitor.lnk'
if ((Test-Path -LiteralPath $desktopLink) -or (Test-Path -LiteralPath $startLink)) { throw 'Installer validation requires an isolated Windows runner without existing GPU Monitor shortcuts.' }
function Run-Bounded([string]$File, [string[]]$Arguments) {
    $process = Start-Process -FilePath $File -ArgumentList $Arguments -PassThru
    if (-not $process.WaitForExit(90000)) { $process.Kill(); throw "Timed out: $File" }
    if ($process.ExitCode -ne 0) { throw "Failed with exit $($process.ExitCode): $File" }
}
try {
    New-Item -ItemType Directory -Path (Split-Path $settingsPath) -Force | Out-Null
    $sentinel = '{"RefreshSeconds":5,"WindowMinutes":15,"MinimizeToTray":false,"HistoryEnabled":false}'
    [IO.File]::WriteAllText($settingsPath, $sentinel)
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$installRoot`"", '/TASKS=desktopicon')
    Write-Output 'Lifecycle: starting silent install'
    Write-Output 'Lifecycle: starting reinstall/upgrade'
    Run-Bounded (Resolve-Path $Installer) $arguments
    Write-Output 'Lifecycle: reinstall/upgrade completed'
    Write-Output 'Lifecycle: first install completed'
    $exe = Join-Path $installRoot 'GpuMonitor.exe'
    if (-not (Test-Path -LiteralPath $exe) -or -not (Test-Path -LiteralPath $desktopLink) -or -not (Test-Path -LiteralPath $startLink)) { throw 'Installer executable or requested shortcuts are missing.' }
    Write-Output 'Lifecycle: checking shortcut targets'
    $shell = New-Object -ComObject WScript.Shell
    foreach ($link in @($desktopLink, $startLink)) {
        if ($shell.CreateShortcut($link).TargetPath -ne $exe) { throw "Shortcut targets the wrong executable: $link" }
    }
    Write-Output 'Lifecycle: starting installed self-test'
    Run-Bounded $exe @('--self-test')
    Write-Output 'Lifecycle: installed self-test completed'
    Run-Bounded (Resolve-Path $Installer) $arguments
    if ([IO.File]::ReadAllText($settingsPath) -ne $sentinel) { throw 'Reinstall/upgrade modified existing preferences.' }
    Write-Output 'Lifecycle: starting uninstall'
    Run-Bounded (Join-Path $installRoot 'unins000.exe') @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART')
    Write-Output 'Lifecycle: uninstall completed'
    if ((Test-Path -LiteralPath $exe) -or (Test-Path -LiteralPath $desktopLink) -or (Test-Path -LiteralPath $startLink)) { throw 'Uninstall left an executable or shortcut registered.' }
    if ([IO.File]::ReadAllText($settingsPath) -ne $sentinel) { throw 'Uninstall removed or changed saved preferences.' }
    Write-Output 'PASS: Silent per-user install, installed native self-test, desktop/Start menu targets, reinstall/upgrade and uninstall preserving preferences. Ephemeral Windows CI only; target-machine lifecycle and human signoff remain pending.'
} finally {
    if ($null -ne $original) { [IO.File]::WriteAllBytes($settingsPath, $original) }
    elseif (Test-Path -LiteralPath $settingsPath) { Remove-Item -LiteralPath $settingsPath -Force }
}
