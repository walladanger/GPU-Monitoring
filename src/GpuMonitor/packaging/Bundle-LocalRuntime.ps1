param([string]$Destination = 'publish')
$ErrorActionPreference = 'Stop'
$dotnetRoot = Split-Path (Get-Command dotnet).Source
$runtimeVersion = '10.0.12'
$runtime = Join-Path $Destination 'runtime'
foreach ($relative in @("host\fxr\$runtimeVersion", "shared\Microsoft.NETCore.App\$runtimeVersion", "shared\Microsoft.WindowsDesktop.App\$runtimeVersion")) {
    $source = Join-Path $dotnetRoot $relative
    if (-not (Test-Path -LiteralPath $source)) { throw "Missing installed runtime: $source" }
    $target = Join-Path $runtime $relative
    New-Item -ItemType Directory -Path $target -Force | Out-Null
    Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $dotnetRoot 'LICENSE.txt') -Destination $runtime
Copy-Item -LiteralPath (Join-Path $dotnetRoot 'ThirdPartyNotices.txt') -Destination $runtime
