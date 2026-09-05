[CmdletBinding()]
param(
    [string]$ManifestPath = 'eng/test-gates.json',

    [ValidateSet('commit', 'release', 'all')]
    [string]$Gate = 'all',

    [string]$GitHubOutputPath = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolvedManifestPath = Join-Path $repoRoot $ManifestPath
$manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) {
    throw "Unsupported test-gates schema version '$($manifest.schemaVersion)'."
}

$runnerByPlatform = @{
    windows = 'windows-latest'
    linux = 'ubuntu-latest'
    macos = 'macos-15'
}

$entries = @(
    foreach ($testGate in @($manifest.gates)) {
        if ($Gate -ne 'all' -and [string]$testGate.gate -ne $Gate) {
            continue
        }

        $requiresFullHistory =
            $testGate.PSObject.Properties.Name -contains 'requiresFullHistory' -and
            [bool]$testGate.requiresFullHistory
        $defaultPartitionCount = if ($testGate.PSObject.Properties.Name -contains 'partitions') {
            [int]$testGate.partitions
        }
        else {
            1
        }
        foreach ($platform in @($testGate.platforms)) {
            if (-not $runnerByPlatform.ContainsKey([string]$platform)) {
                throw "Gate '$($testGate.id)' uses unsupported platform '$platform'."
            }

            # "platformPartitions" is an optional per-platform override of "partitions" — it
            # exists so a gate can run fewer parallel jobs on scarce-capacity runners (e.g.
            # macOS) than it does on other platforms, without splitting into a separate gate.
            $partitionCount = $defaultPartitionCount
            if ($testGate.PSObject.Properties.Name -contains 'platformPartitions') {
                $platformPartitionProperty = $testGate.platformPartitions.PSObject.Properties |
                    Where-Object Name -EQ ([string]$platform) |
                    Select-Object -First 1
                if ($null -ne $platformPartitionProperty) {
                    $partitionCount = [int]$platformPartitionProperty.Value
                }
            }

            # Approximate minutes for ONE job of this gate as currently partitioned. It exists
            # only to order the matrix (below), never to change what runs.
            $costHintMinutes = if ($testGate.PSObject.Properties.Name -contains 'costHintMinutes') {
                [double]$testGate.costHintMinutes
            }
            else {
                0.0
            }

            $preflightModes = @()
            if ($testGate.PSObject.Properties.Name -contains 'preflightModes') {
                $platformPreflightProperty = $testGate.preflightModes.PSObject.Properties |
                    Where-Object Name -EQ ([string]$platform) |
                    Select-Object -First 1
                if ($null -ne $platformPreflightProperty) {
                    $preflightModes = @($platformPreflightProperty.Value)
                }
            }

            for ($partitionIndex = 0; $partitionIndex -lt $partitionCount; $partitionIndex++) {
                $displayGateId = if ($partitionCount -eq 1) {
                    [string]$testGate.id
                }
                else {
                    "$($testGate.id)-$($partitionIndex + 1)of$partitionCount"
                }

                [ordered]@{
                    costHintMinutes = $costHintMinutes
                    gateId = [string]$testGate.id
                    displayGateId = $displayGateId
                    gate = [string]$testGate.gate
                    app = [string]$testGate.app
                    platform = [string]$platform
                    runner = $runnerByPlatform[[string]$platform]
                    fetchDepth = if ($requiresFullHistory) { 0 } else { 1 }
                    runStaticPreflight = $preflightModes -contains 'static'
                    runPlatformPreflight = $preflightModes -contains 'platform'
                    partitionIndex = $partitionIndex
                    partitionCount = $partitionCount
                }
            }
        }
    }
)

if ($entries.Count -eq 0) {
    throw "No '$Gate' test-gate matrix entries were selected."
}

# Dispatch the longest jobs first. GitHub starts matrix entries in array order, and the runner
# pool saturates (measured: 19 concurrent, and the commit matrix already fills it), so whichever
# jobs are dispatched last are the ones that queue. Emitting them longest-first means only SHORT
# jobs absorb the queueing delay instead of a long one landing on the critical path -- in run
# 33969021055 the 6.3m macOS avalonia job queued 2.6m and set the whole wall-clock. Ties break on
# the display id so the matrix stays deterministic.
$entries = @($entries | Sort-Object @{ Expression = { [double]$_.costHintMinutes }; Descending = $true }, @{ Expression = { [string]$_.displayGateId }; Descending = $false })
foreach ($entry in $entries) {
    $entry.Remove('costHintMinutes')
}

$matrixJson = @{ include = $entries } | ConvertTo-Json -Depth 4 -Compress
if (-not [string]::IsNullOrWhiteSpace($GitHubOutputPath)) {
    "matrix=$matrixJson" | Add-Content -LiteralPath $GitHubOutputPath -Encoding utf8
}

$matrixJson
