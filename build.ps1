param(
    [string]$NinaPath = "C:\Program Files\N.I.N.A. - Nighttime Imaging 'N' Astronomy"
)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/cli-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$sdk = Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe'
if (!(Test-Path -LiteralPath $sdk)) { $sdk = 'dotnet' }

# Reference the exact release binaries used for this integration proof.
$pins = Get-Content docs/host-assemblies.lock.json -Raw | ConvertFrom-Json
foreach ($pin in $pins) {
    $file = Join-Path $NinaPath $pin.file
    if ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $pin.sha256) {
        throw "Host dependency differs from pinned baseline: $file. Review compatibility before updating pins."
    }
}
& $sdk build src/HistogramInspector.Plugin/HistogramInspector.Plugin.csproj -c Release "-p:NinaPath=$NinaPath"
if ($LASTEXITCODE -ne 0) { throw 'Plugin build failed.' }
& $sdk run --project tests/HistogramInspector.Checks -c Release
if ($LASTEXITCODE -ne 0) { throw 'Identity or histogram checks failed.' }
& $sdk run --project tests/HistogramInspector.HostChecks -c Release "-p:NinaPath=$NinaPath" -- $NinaPath
if ($LASTEXITCODE -ne 0) { throw 'Adapter checks failed.' }

$package = Join-Path $PSScriptRoot 'artifacts/HistogramInspector'
New-Item -ItemType Directory -Force $package | Out-Null
$output = 'src/HistogramInspector.Plugin/bin/Release/net8.0-windows'
# Explicit allowlist: never deploy host DLLs or the test harness.
Copy-Item "$output/HistogramInspector.dll", "$output/HistogramInspector.Core.dll" $package
Copy-Item README.md, docs/validation.md, docs/stage2-core.md, docs/stage3-chart.md $package
Write-Host "Release files: $package"
