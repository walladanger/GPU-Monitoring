param([switch]$UseInstalledRuntime)
$ErrorActionPreference = 'Stop'
$project = Join-Path (Split-Path $PSScriptRoot) 'GpuMonitor.csproj'
$publish = Join-Path (Split-Path $PSScriptRoot) 'publish'
# This fixed directory contains generated build output only. Never merge package modes.
$projectRoot = [IO.Path]::GetFullPath((Split-Path $project))
$publish = [IO.Path]::GetFullPath($publish)
if ((Split-Path $publish) -ne $projectRoot -or (Split-Path $publish -Leaf) -ne 'publish') { throw 'Unexpected publish directory' }
if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
if ($UseInstalledRuntime) {
    dotnet publish $project -c Release --self-contained false -p:AppHostDotNetSearch=AppRelative -p:AppHostRelativeDotNet=runtime -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
    & (Join-Path $PSScriptRoot 'Bundle-LocalRuntime.ps1') -Destination $publish
} else {
    dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
}
Copy-Item -Path (Join-Path $PSScriptRoot 'Install.*'),(Join-Path $PSScriptRoot 'Uninstall.ps1') -Destination $publish
Copy-Item -LiteralPath (Join-Path (Split-Path $PSScriptRoot) 'README.md') -Destination $publish
$packageFiles = Get-ChildItem -LiteralPath $publish | Where-Object { $_.Name -notin @('GpuMonitor.pdb','self-test-result.txt','ui-check-result.txt','dashboard-preview.png') }
Compress-Archive -LiteralPath $packageFiles.FullName -DestinationPath (Join-Path (Split-Path $PSScriptRoot) 'GPU-Monitor-Windows-x64.zip') -Force
