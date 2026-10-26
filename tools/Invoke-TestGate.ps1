[CmdletBinding()]
param(
    [ValidateSet("commit", "release")]
    [string]$Gate = "commit",

    [ValidateSet("FreeX", "FreeW", "FreeP", "all")]
    [string]$App = "all",

    [ValidateSet("windows", "linux", "macos")]
    [string]$Platform = "windows",

    [string]$GateId = "",

    [ValidateRange(0, 63)]
    [int]$PartitionIndex = 0,

    [ValidateRange(1, 64)]
    [int]$PartitionCount = 1,

    [string]$Configuration = "Release",

    [switch]$NoBuild,

    [switch]$NoRestore,

    [ValidateRange(0, 3)]
    [int]$RetryFailedProjectCount = 0,

    [ValidatePattern("^\d+[smh]$")]
    [string]$HangTimeout = "15m",

    [string]$ResultsDirectory = "artifacts/test-gates"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

# Whole-project partitioning: used by gates that declare "partitions" without a
# "partitionProjects" class-filter list (i.e. no single project dominates the gate's
# runtime). Each project is assigned to exactly one partition — via deterministic
# largest-first bin packing on a statically discovered [Fact]/[Theory] weight — so every
# project still builds and runs exactly once overall, just spread across parallel runners,
# rather than being rebuilt/retested (partially) in every partition like the class-filter
# strategy does.
function Get-TestProjectWeight {
    param([Parameter(Mandatory = $true)][string]$ProjectFullPath)

    $projectDirectory = Split-Path -Parent $ProjectFullPath
    # Match custom xUnit attributes ([StaFact], [UiE2eFact], ...) as well as the bare spelling: a
    # project whose tests ALL use a custom attribute would otherwise weigh 1 and wreck the bin
    # packing. Same reasoning as Get-TestProjectPartitionFilter.ps1; requiring "(" or "]" straight
    # after the suffix still rejects unrelated names like [Factory].
    $factPattern = '\[[A-Za-z0-9_]*Fact(?:Attribute)?(?:\(|\])'
    $theoryPattern = '\[[A-Za-z0-9_]*Theory(?:Attribute)?(?:\(|\])'
    $inlineDataPattern = '\[InlineData(?:Attribute)?(?:\(|\])'

    $weight = 0
    foreach ($sourceFile in @(Get-ChildItem -LiteralPath $projectDirectory -Recurse -File -Filter '*.cs' |
        Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' })) {
        $source = Get-Content -LiteralPath $sourceFile.FullName -Raw
        $factCount = [regex]::Matches($source, $factPattern).Count
        $theoryCount = [regex]::Matches($source, $theoryPattern).Count
        $inlineDataCount = [regex]::Matches($source, $inlineDataPattern).Count
        $weight += $factCount + [Math]::Max($theoryCount, $inlineDataCount)
    }

    return [Math]::Max($weight, 1)
}

function Get-WholeProjectPartitionAssignment {
    param(
        [Parameter(Mandatory = $true)][string[]]$ProjectPaths,
        [Parameter(Mandatory = $true)][int]$PartitionCount,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $weighted = @(
        foreach ($projectPath in $ProjectPaths) {
            $projectFullPath = Join-Path $RepoRoot $projectPath
            [pscustomobject]@{
                Path = $projectPath
                Weight = Get-TestProjectWeight -ProjectFullPath $projectFullPath
            }
        }
    )

    $partitionWeights = [int[]]::new($PartitionCount)
    $assignment = @{}
    foreach ($item in @($weighted | Sort-Object @{ Expression = 'Weight'; Descending = $true }, @{ Expression = 'Path'; Descending = $false })) {
        $target = 0
        for ($index = 1; $index -lt $PartitionCount; $index++) {
            if ($partitionWeights[$index] -lt $partitionWeights[$target]) {
                $target = $index
            }
        }
        $assignment[$item.Path] = $target
        $partitionWeights[$target] += $item.Weight
    }

    return $assignment
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repoRoot "eng/test-gates.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) {
    throw "Unsupported test-gates schema version '$($manifest.schemaVersion)'."
}

$gates = @($manifest.gates | Where-Object {
    ($App -eq "all" -or $_.app -eq $App) -and
    $_.platforms -contains $Platform -and
    ($_.gate -eq "commit" -or $Gate -eq "release") -and
    ([string]::IsNullOrWhiteSpace($GateId) -or $_.id -eq $GateId)
})
if ($gates.Count -eq 0) {
    $gateIdDescription = if ([string]::IsNullOrWhiteSpace($GateId)) { "" } else { " with id '$GateId'" }
    throw "No $Gate test gates$gateIdDescription match app '$App' on platform '$Platform'."
}
if ($PartitionIndex -ge $PartitionCount) {
    throw "Partition index $PartitionIndex must be less than partition count $PartitionCount."
}

$seenProjects = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$seenBuildProjects = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($testGate in $gates) {
    $declaredPartitionCount = if ($testGate.PSObject.Properties.Name -contains "partitions") {
        [int]$testGate.partitions
    }
    else {
        1
    }
    # "platformPartitions" optionally overrides "partitions" for a single platform (e.g. running
    # fewer parallel jobs on scarce macOS capacity); resolve it for the platform actually being run.
    if ($testGate.PSObject.Properties.Name -contains "platformPartitions") {
        $platformPartitionProperty = $testGate.platformPartitions.PSObject.Properties |
            Where-Object Name -EQ $Platform |
            Select-Object -First 1
        if ($null -ne $platformPartitionProperty) {
            $declaredPartitionCount = [int]$platformPartitionProperty.Value
        }
    }
    if ($PartitionCount -gt 1 -and $PartitionCount -ne $declaredPartitionCount) {
        throw "Gate '$($testGate.id)' declares $declaredPartitionCount partition(s) on '$Platform', but the runner requested $PartitionCount."
    }

    $partitionProjects = if ($testGate.PSObject.Properties.Name -contains "partitionProjects") {
        @($testGate.partitionProjects)
    }
    else {
        @()
    }
    $partitionDescription = if ($PartitionCount -eq 1) { "" } else { ", partition $($PartitionIndex + 1)/$PartitionCount" }
    Write-Host ""
    Write-Host "Gate $($testGate.id) ($($testGate.app), $($testGate.gate), $Platform$partitionDescription)"
    if (-not $NoBuild) {
        $buildProjects = if ($testGate.PSObject.Properties.Name -contains "buildProjects") {
            @($testGate.buildProjects)
        }
        else {
            @()
        }
        foreach ($projectPath in $buildProjects) {
            if (-not $seenBuildProjects.Add($projectPath)) {
                continue
            }

            $projectFullPath = Join-Path $repoRoot $projectPath
            if (-not (Test-Path -LiteralPath $projectFullPath -PathType Leaf)) {
                throw "Gate '$($testGate.id)' references missing build prerequisite '$projectPath'."
            }

            $arguments = @("build", $projectFullPath, "--configuration", $Configuration)
            if ($NoRestore) { $arguments += "--no-restore" }

            Write-Host "dotnet $($arguments -join ' ')"
            & dotnet @arguments
            if ($LASTEXITCODE -ne 0) {
                throw "Gate '$($testGate.id)' build prerequisite '$projectPath' failed with exit code $LASTEXITCODE."
            }
        }
    }

    $platformProjects = if (
        $testGate.PSObject.Properties.Name -contains "platformProjects" -and
        $testGate.platformProjects.PSObject.Properties.Name -contains $Platform) {
        @($testGate.platformProjects.$Platform)
    }
    else {
        @()
    }

    $allGateProjects = @($testGate.projects) + $platformProjects
    $useWholeProjectPartitioning = $PartitionCount -gt 1 -and @($partitionProjects).Count -eq 0
    $wholeProjectAssignment = if ($useWholeProjectPartitioning) {
        Get-WholeProjectPartitionAssignment -ProjectPaths $allGateProjects -PartitionCount $PartitionCount -RepoRoot $repoRoot
    }
    else {
        $null
    }

    foreach ($projectPath in $allGateProjects) {
        $isPartitioned = $PartitionCount -gt 1 -and $partitionProjects -contains $projectPath
        if ($useWholeProjectPartitioning) {
            if ($wholeProjectAssignment[$projectPath] -ne $PartitionIndex) {
                continue
            }
        }
        elseif ($PartitionCount -gt 1 -and -not $isPartitioned -and $PartitionIndex -gt 0) {
            continue
        }
        if (-not $seenProjects.Add($projectPath)) {
            continue
        }

        $projectFullPath = Join-Path $repoRoot $projectPath
        if (-not (Test-Path -LiteralPath $projectFullPath -PathType Leaf)) {
            throw "Gate '$($testGate.id)' references missing test project '$projectPath'."
        }

        $attempt = 0
        do {
            $arguments = @("test", $projectFullPath, "--configuration", $Configuration)
            if ($NoBuild) { $arguments += "--no-build" }
            if ($NoRestore) { $arguments += "--no-restore" }
            if ($isPartitioned) {
                $partitionFilter = & (Join-Path $PSScriptRoot "Get-TestProjectPartitionFilter.ps1") `
                    -ProjectPath $projectPath `
                    -PartitionIndex $PartitionIndex `
                    -PartitionCount $PartitionCount
                $arguments += @("--filter", $partitionFilter)
            }
            if (-not [string]::IsNullOrWhiteSpace($HangTimeout)) {
                $arguments += @("--blame-hang-timeout", $HangTimeout)
            }
            if (-not [string]::IsNullOrWhiteSpace($ResultsDirectory)) {
                $resultGateId = if ($PartitionCount -eq 1) {
                    [string]$testGate.id
                }
                else {
                    "$($testGate.id)-$($PartitionIndex + 1)of$PartitionCount"
                }
                $outputDirectory = Join-Path $ResultsDirectory $resultGateId
                $baseName = [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
                $attemptSuffix = if ($attempt -eq 0) { "" } else { ".retry$attempt" }
                $arguments += @(
                    "--results-directory", $outputDirectory,
                    "--logger", "trx;LogFileName=$baseName$attemptSuffix.trx"
                )
            }

            Write-Host "dotnet $($arguments -join ' ')"
            & dotnet @arguments
            $testExitCode = $LASTEXITCODE
            if ($testExitCode -eq 0) {
                if ($attempt -gt 0) {
                    Write-Warning "Gate '$($testGate.id)' project '$projectPath' passed on retry $attempt; the initial TRX is retained."
                }
                break
            }

            if ($attempt -ge $RetryFailedProjectCount) {
                throw "Gate '$($testGate.id)' failed for '$projectPath' after $($attempt + 1) attempt(s) with exit code $testExitCode."
            }

            $attempt++
            Write-Warning "Gate '$($testGate.id)' project '$projectPath' failed with exit code $testExitCode; retrying only this project ($attempt/$RetryFailedProjectCount)."
        } while ($true)
    }
}
