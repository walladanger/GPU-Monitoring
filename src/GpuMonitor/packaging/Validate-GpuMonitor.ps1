param(
    [string]$Executable = (Join-Path $PSScriptRoot 'GpuMonitor.exe'),
    [ValidateRange(0,64)][int]$ExpectedGpuCount = 2,
    [string]$ExpectedGpuName = 'RTX 3090',
    [ValidateRange(6,120)][int]$DurationSeconds = 12,
    [string]$OutputRoot = '',
    [switch]$OpenReport
)

# A zero-GPU CI run is explicit and never counts as target hardware acceptance.
function Get-CollectionChecks($Sources, [int]$ExpectedGpuCount, [string]$ExpectedGpuName) {
    $gpu = @($Sources | Where-Object Source -eq 'nvidia-smi') | Select-Object -First 1
    $devices = @($gpu.Current.Devices | Where-Object Kind -eq 'Gpu')
    $identityOk = $devices.Count -eq $ExpectedGpuCount -and @($devices | Select-Object -ExpandProperty Id -Unique).Count -eq $ExpectedGpuCount
    if ($devices | Where-Object { [string]::IsNullOrWhiteSpace($_.Id) -or -not $_.Id.StartsWith('GPU-') }) { $identityOk = $false }
    [pscustomobject]@{ Name = 'Actual GPU identities'; Status = $(if ($identityOk) { if ($ExpectedGpuCount -eq 0) { 'N/A' } else { 'PASS' } } else { 'FAIL' }); Detail = "$($devices.Count) actual GPUs; expected $ExpectedGpuCount; unique UUIDs required" }
    if ($ExpectedGpuCount -gt 0) {
        [pscustomobject]@{ Name = 'NVIDIA source health'; Status = $(if ($gpu.Health -eq 'Live') { 'PASS' } else { 'FAIL' }); Detail = "Actual NVIDIA source: $($gpu.Health)" }
        foreach ($device in $devices) {
            $nameOk = -not $ExpectedGpuName -or ($device.Name -replace '\s','').IndexOf(($ExpectedGpuName -replace '\s',''), [StringComparison]::OrdinalIgnoreCase) -ge 0
            [pscustomobject]@{ Name = "GPU model $($device.Id)"; Status = $(if ($nameOk) { 'PASS' } else { 'FAIL' }); Detail = $device.Name }
            foreach ($metric in @('utilization.gpu', 'memory.used')) {
                $measured = @($gpu.History | Where-Object { $_.Device.Id -eq $device.Id -and $_.MetricKey -eq $metric -and $null -ne $_.Value })
                $times = @($measured | Select-Object -ExpandProperty Time -Unique)
                [pscustomobject]@{ Name = "$($device.Id) $metric"; Status = $(if ($times.Count -ge 2) { 'PASS' } else { 'FAIL' }); Detail = "$($times.Count) distinct measured timestamps" }
            }
        }
    }
    $system = @($Sources | Where-Object Source -eq 'windows-system') | Select-Object -First 1
    foreach ($metric in @('cpu.usage', 'ram.total')) {
        $measured = @($system.History | Where-Object { $_.MetricKey -eq $metric -and $null -ne $_.Value })
        [pscustomobject]@{ Name = "Actual $metric"; Status = $(if ($system.Health -eq 'Live' -and $measured.Count -gt 0) { 'PASS' } else { 'FAIL' }); Detail = "$($measured.Count) measured samples; source $($system.Health)" }
    }
}
function New-AcceptanceDirectory([string]$PreferredRoot, [string]$FallbackRoot) {
    $id = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0,8)
    $preferredFailure = ''
    foreach ($root in @($PreferredRoot, $FallbackRoot)) {
        try {
            $path = Join-Path ([IO.Path]::GetFullPath($root)) $id
            New-Item -ItemType Directory -Path $path -Force -ErrorAction Stop | Out-Null
            $probe = Join-Path $path 'write-probe.tmp'
            [IO.File]::WriteAllText($probe, 'GPU Monitor validation output check')
            Remove-Item -LiteralPath $probe -Force -ErrorAction Stop
            if ($preferredFailure) { [IO.File]::WriteAllText((Join-Path $path 'output-location-note.txt'), "Preferred output unavailable: $preferredFailure`r`nResults saved here instead.") }
            return $path
        } catch { $preferredFailure = $_.Exception.Message }
    }
    throw "No writable validation output directory: $preferredFailure"
}
if ($MyInvocation.InvocationName -eq '.') { return }

$ErrorActionPreference = 'Stop'
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) { throw 'This acceptance runner needs Windows; fixture-only PowerShell checks can run elsewhere.' }
if (-not $OutputRoot) {
    $OutputRoot = if (Test-Path -LiteralPath 'D:\') { 'D:\DEBUGGING LOGS\GPU-Monitor' } else { Join-Path $env:LOCALAPPDATA 'GpuMonitor\validation' }
}
$localOutput = Join-Path $env:LOCALAPPDATA 'GpuMonitor\validation'
$fallbackOutput = if ([IO.Path]::GetFullPath($OutputRoot) -eq [IO.Path]::GetFullPath($localOutput)) { Join-Path ([IO.Path]::GetTempPath()) 'GpuMonitor-validation' } else { $localOutput }
$run = New-AcceptanceDirectory $OutputRoot $fallbackOutput
$reportPath = Join-Path $run 'acceptance.txt'
$results = [Collections.Generic.List[object]]::new()
$performance = [Collections.Generic.List[object]]::new()
$exe = [IO.Path]::GetFullPath($Executable)
function Add-Result($name, $status, $detail) { $results.Add([pscustomobject]@{ Name = $name; Status = $status; Detail = $detail }) }
function Invoke-BoundedCheck([string[]]$Arguments, [int]$TimeoutSeconds) {
    # Windows paths cannot contain a double quote; quote each argument for Start-Process's joined command line.
    $quoted = @($Arguments | ForEach-Object { if ($_.Contains('"')) { throw 'Unexpected quote in verification argument' }; '"' + $_ + '"' })
    $process = Start-Process -FilePath $exe -ArgumentList $quoted -WindowStyle Hidden -PassThru
    $watch = [Diagnostics.Stopwatch]::StartNew(); $peak = 0; $cpuMs = 0
    try {
        while (-not $process.WaitForExit(100)) {
            $process.Refresh()
            try { $peak = [Math]::Max($peak, $process.PeakWorkingSet64); $cpuMs = $process.TotalProcessorTime.TotalMilliseconds } catch { }
            if ($watch.Elapsed.TotalSeconds -gt $TimeoutSeconds) { $process.Kill(); $process.WaitForExit(5000) | Out-Null; throw 'Verification process exceeded its time limit' }
        }
        $process.Refresh()
        try { $peak = [Math]::Max($peak, $process.PeakWorkingSet64); $cpuMs = $process.TotalProcessorTime.TotalMilliseconds } catch { }
        $performance.Add([pscustomobject]@{ Mode = $Arguments[0]; ElapsedSeconds = $watch.Elapsed.TotalSeconds; PeakWorkingSetBytes = $peak; AverageCpuPercentOfMachine = $cpuMs / [Math]::Max(1,$watch.Elapsed.TotalMilliseconds) / [Environment]::ProcessorCount * 100 })
        if ($process.ExitCode -ne 0) { throw "Verification exited with code $($process.ExitCode)" }
    } finally { if (-not $process.HasExited) { $process.Kill() }; $process.Dispose() }
}
try {
    if (-not (Test-Path -LiteralPath $exe -PathType Leaf)) { throw "Executable not found: $exe" }
    $appDirectory = Split-Path $exe
    $config = Join-Path $run 'validation.sources.json'
    @{ Version = 1; CollectCpuCores = $true } | ConvertTo-Json | Set-Content -LiteralPath $config -Encoding UTF8
    $collection = Join-Path $run 'actual-collection'
    Invoke-BoundedCheck -Arguments @('--collect', $collection, '--duration', "$DurationSeconds", '--interval', '1', '--sources', $config) -TimeoutSeconds ($DurationSeconds + 20)
    $sources = Get-Content -LiteralPath (Join-Path $collection 'telemetry.json') -Raw | ConvertFrom-Json
    foreach ($check in @(Get-CollectionChecks $sources $ExpectedGpuCount $ExpectedGpuName)) { $results.Add($check) }
    $fixtureDirectory = Join-Path $appDirectory 'overview-verification'
    if (Test-Path -LiteralPath $fixtureDirectory) { Remove-Item -LiteralPath $fixtureDirectory -Recurse -Force }
    foreach ($gate in @(
        @{ Flag = '--verify-overview'; Result = 'overview-verification\result.txt'; Timeout = 120 },
        @{ Flag = '--verify-monitoring'; Result = 'monitoring-verification.txt'; Timeout = 30 },
        @{ Flag = '--verify-workspace'; Result = 'overview-verification\workspace-result.txt'; Timeout = 45 },
        @{ Flag = '--verify-charts'; Result = 'overview-verification\charts-result.txt'; Timeout = 30 },
        @{ Flag = '--verify-persistence'; Result = 'overview-verification\persistence-result.txt'; Timeout = 45 },
        @{ Flag = '--verify-tray'; Result = 'overview-verification\tray-result.txt'; Timeout = 30 }
    )) {
        $path = Join-Path $appDirectory $gate.Result
        try {
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
            Invoke-BoundedCheck -Arguments @($gate.Flag) -TimeoutSeconds $gate.Timeout
            $text = Get-Content -LiteralPath $path -Raw
            if (-not $text.StartsWith('PASS:')) { throw $text }
            Add-Result $gate.Flag 'PASS' $text
        } catch { Add-Result $gate.Flag 'FAIL' $_.Exception.Message }
    }
    $fixtures = Join-Path $appDirectory 'overview-verification'
    if (Test-Path -LiteralPath $fixtures) { Copy-Item -LiteralPath $fixtures -Destination (Join-Path $run 'synthetic-fixture-results') -Recurse }
    if ($ExpectedGpuCount -gt 0) {
        $live = Join-Path $appDirectory 'target-verification'
        if (Test-Path -LiteralPath $live) { Remove-Item -LiteralPath $live -Recurse -Force }
        $liveResult = Join-Path $live 'live-overview-result.txt'
        try {
            if (Test-Path -LiteralPath $liveResult) { Remove-Item -LiteralPath $liveResult -Force }
            Invoke-BoundedCheck -Arguments @('--verify-live-overview', '--expected-gpus', "$ExpectedGpuCount") -TimeoutSeconds 45
            $text = Get-Content -LiteralPath $liveResult -Raw
            if (-not $text.StartsWith('PASS:')) { throw $text }
            Add-Result 'Actual native GPU Overview' 'PASS' $text
        } catch { Add-Result 'Actual native GPU Overview' 'FAIL' $_.Exception.Message }
        if (Test-Path -LiteralPath $live) { Copy-Item -LiteralPath $live -Destination (Join-Path $run 'actual-native-results') -Recurse }
    } else { Add-Result 'Target hardware acceptance' 'N/A' 'Explicit zero-GPU diagnostic run; no dual-3090 acceptance.' }
} catch { Add-Result 'Acceptance runner' 'FAIL' $_.Exception.Message }
finally {
    Add-Result 'Physical pointer / keyboard' 'PENDING' 'On the live app: tray hover/dismiss/configure, title-drag reorder, resize preview/Escape/commit, click a chart then Ctrl+Space/Left/Right/Home, Ctrl+wheel; confirm undo and presets.'
    Add-Result 'DPI / monitors / suspend' 'PENDING' 'Move the live app across monitors at their actual scaling, resize ultrawide/fullscreen, suspend/resume and confirm precise hit targets and readable labels.'
    Add-Result 'Human visual approval / install-uninstall' 'PENDING' 'Compare against approved references and approve tray backdrop/readability; verify shortcut, tray, clean close and installer/uninstall on the target machine. No automatic signoff.'
    $performance | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $run 'process-measurements.json') -Encoding UTF8
    $results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $run 'acceptance.json') -Encoding UTF8
    $failed = @($results | Where-Object Status -eq 'FAIL').Count
    $lines = @('GPU Monitor target acceptance', "UTC: $((Get-Date).ToUniversalTime().ToString('o'))", "Executable: $exe", "OS: $([Environment]::OSVersion.VersionString)")
    if (Test-Path -LiteralPath $exe) { $lines += "Executable SHA256: $((Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash)" }
    $lines += @("Automated failures: $failed", 'Manual gates remain PENDING; this report is not release/visual signoff.', '')
    foreach ($result in $results) { $lines += "[$($result.Status)] $($result.Name): $($result.Detail)" }
    $lines += @('', 'Actual collection and synthetic fixture results are in separate folders.', 'Process measurements cover these bounded checks, not sustained idle/performance acceptance.', "Results: $run")
    $lines | Set-Content -LiteralPath $reportPath -Encoding UTF8
    if ($OpenReport) { Start-Process -FilePath notepad.exe -ArgumentList ('"' + $reportPath + '"') }
}
if (@($results | Where-Object Status -eq 'FAIL').Count -gt 0) { exit 1 }
exit 0
