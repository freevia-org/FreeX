[CmdletBinding()]
param(
    [ValidateSet("commit", "release")]
    [string]$Gate = "commit",

    [ValidateSet("FreeX", "FreeW", "FreeP", "all")]
    [string]$App = "all",

    [ValidateSet("windows", "linux", "macos")]
    [string]$Platform = "windows",

    [string]$GateId = "",

    [string]$Configuration = "Release",

    [switch]$NoBuild,

    [switch]$NoRestore,

    [ValidateRange(0, 3)]
    [int]$RetryFailedProjectCount = 0,

    [string]$ResultsDirectory = "artifacts/test-gates"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

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

$seenProjects = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$seenBuildProjects = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($testGate in $gates) {
    Write-Host ""
    Write-Host "Gate $($testGate.id) ($($testGate.app), $($testGate.gate), $Platform)"
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

    foreach ($projectPath in @($testGate.projects)) {
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
            if (-not [string]::IsNullOrWhiteSpace($ResultsDirectory)) {
                $outputDirectory = Join-Path $ResultsDirectory $testGate.id
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
