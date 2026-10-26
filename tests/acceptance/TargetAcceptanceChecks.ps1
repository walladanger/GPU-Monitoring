$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../../src/GpuMonitor/packaging/Validate-GpuMonitor.ps1')
function Require($value, $message) { if (-not $value) { throw $message } }
$gpuA = [pscustomobject]@{ Id = 'GPU-A'; Name = 'NVIDIA GeForce RTX 3090'; Kind = 'Gpu' }
$gpuB = [pscustomobject]@{ Id = 'GPU-B'; Name = 'NVIDIA GeForce RTX 3090'; Kind = 'Gpu' }
$samples = @()
foreach ($gpu in @($gpuA, $gpuB)) { foreach ($metric in @('utilization.gpu', 'memory.used')) { 1..2 | ForEach-Object { $samples += [pscustomobject]@{ Device = $gpu; MetricKey = $metric; Value = 1; Time = "2026-10-24T12:00:0${_}Z" } } } }
$nvidia = [pscustomobject]@{ Source = 'nvidia-smi'; Health = 'Live'; Current = [pscustomobject]@{ Devices = @($gpuA, $gpuB) }; History = $samples }
$system = [pscustomobject]@{ Source = 'windows-system'; Health = 'Live'; History = @([pscustomobject]@{ MetricKey = 'cpu.usage'; Value = 1 }, [pscustomobject]@{ MetricKey = 'ram.total'; Value = 68719476736 }) }
$checks = @(Get-CollectionChecks -Sources @($nvidia, $system) -ExpectedGpuCount 2 -ExpectedGpuName 'RTX 3090')
Require (-not ($checks | Where-Object Status -eq 'FAIL')) 'Measured dual-3090 input was rejected'
Require (-not (Get-CollectionChecks @($nvidia, $system) 2 'RTX3090' | Where-Object Status -eq 'FAIL')) 'Whitespace in the expected model prevented matching'
$nvidia.Health = 'Stale'
Require (@(Get-CollectionChecks @($nvidia, $system) 2 'RTX 3090' | Where-Object Status -eq 'FAIL').Count -gt 0) 'Stale NVIDIA source was accepted'
$nvidia.Health = 'Live'; $gpuB.Id = 'GPU-A'
Require (@(Get-CollectionChecks @($nvidia, $system) 2 'RTX 3090' | Where-Object Status -eq 'FAIL').Count -gt 0) 'Duplicate UUIDs were accepted'
$gpuB.Id = 'GPU-B'; $nvidia.History = @()
Require (@(Get-CollectionChecks @($nvidia, $system) 2 'RTX 3090' | Where-Object Status -eq 'FAIL').Count -gt 0) 'Inventory without measured history was accepted'
$nvidia.History = $samples
$oldTime = $samples[1].Time; $samples[1].Time = $samples[0].Time
Require (@(Get-CollectionChecks @($nvidia, $system) 2 'RTX 3090' | Where-Object Status -eq 'FAIL').Count -gt 0) 'Repeated identical timestamps counted as independent sampling'
$samples[1].Time = $oldTime
Require (@(Get-CollectionChecks @($nvidia, $system) 1 'RTX 3090' | Where-Object Status -eq 'FAIL').Count -gt 0) 'Wrong expected device count was accepted'
$system.Health = 'Stale'
Require (@(Get-CollectionChecks @($nvidia, $system) 2 'RTX 3090' | Where-Object Status -eq 'FAIL').Count -gt 0) 'Stale system data was accepted'
$system.Health = 'Live'
$gpuB.Name = 'Another GPU'
Require (@(Get-CollectionChecks @($nvidia, $system) 2 'RTX 3090' | Where-Object Status -eq 'FAIL').Count -gt 0) 'Wrong target model was accepted'
Require (@(Get-CollectionChecks @($system) 0 '' | Where-Object Status -eq 'FAIL').Count -eq 0) 'GPU-free CI diagnostic path was rejected'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('gpu-validation-check-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temporary | Out-Null
try {
    $blocked = Join-Path $temporary 'blocked'; 'file blocking directory creation' | Set-Content -LiteralPath $blocked
    $fallback = Join-Path $temporary 'fallback'
    $saved = New-AcceptanceDirectory $blocked $fallback
    Require ($saved.StartsWith($fallback) -and (Test-Path -LiteralPath (Join-Path $saved 'output-location-note.txt'))) 'Unwritable preferred output lost the hidden launch report instead of falling back'
} finally { Remove-Item -LiteralPath $temporary -Recurse -Force }
Write-Output 'PASS: target acceptance rejects stale sources, duplicate UUIDs, missing samples and wrong model; accepts measured two-GPU and explicit zero-GPU checks.'
