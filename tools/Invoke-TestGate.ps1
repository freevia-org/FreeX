[CmdletBinding()]
param(
    [ValidateSet("commit", "release")]
    [string]$Gate = "commit",

    [ValidateSet("FreeX", "FreeW", "FreeP", "all")]
    [string]$App = "all",

    [ValidateSet("windows", "linux", "macos")]
    [string]$Platform = "windows",

    [string]$Configuration = "Release",

    [switch]$NoBuild,

    [switch]$NoRestore,

    [string]$ResultsDirectory = "artifacts/test-gates"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repoRoot "eng/test-gates.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) {
    throw "Unsupported test-gates schema version '$($manifest.schemaVersion)'."
}

$gates = @($manifest.gates | Where-Object {
    ($App -eq "all" -or $_.app -eq $App) -and
    $_.platforms -contains $Platform -and
    ($_.gate -eq "commit" -or $Gate -eq "release")
})
if ($gates.Count -eq 0) {
    throw "No $Gate test gates match app '$App' on platform '$Platform'."
}

$seenProjects = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($testGate in $gates) {
    Write-Host ""
    Write-Host "Gate $($testGate.id) ($($testGate.app), $($testGate.gate), $Platform)"
    foreach ($projectPath in @($testGate.projects)) {
        if (-not $seenProjects.Add($projectPath)) {
            continue
        }

        $projectFullPath = Join-Path $repoRoot $projectPath
        if (-not (Test-Path -LiteralPath $projectFullPath -PathType Leaf)) {
            throw "Gate '$($testGate.id)' references missing test project '$projectPath'."
        }

        $arguments = @("test", $projectFullPath, "--configuration", $Configuration)
        if ($NoBuild) { $arguments += "--no-build" }
        if ($NoRestore) { $arguments += "--no-restore" }
        if (-not [string]::IsNullOrWhiteSpace($ResultsDirectory)) {
            $outputDirectory = Join-Path $ResultsDirectory $testGate.id
            $fileName = [System.IO.Path]::GetFileNameWithoutExtension($projectPath) + ".trx"
            $arguments += @("--results-directory", $outputDirectory, "--logger", "trx;LogFileName=$fileName")
        }

        Write-Host "dotnet $($arguments -join ' ')"
        & dotnet @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "Gate '$($testGate.id)' failed for '$projectPath' with exit code $LASTEXITCODE."
        }
    }
}
