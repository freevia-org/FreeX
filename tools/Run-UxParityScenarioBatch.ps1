param(
    [string]$OutputRoot = "tools/ux-parity-runs",

    [string]$RunId,

    [string]$FreeXExe,

    [ValidateSet("smoke", "dialogs", "all")]
    [string]$Suite = "smoke",

    [switch]$SkipBuild,

    [ValidateRange(1, 5)]
    [int]$MaxAttempts = 2
)

$ErrorActionPreference = "Stop"

function Get-RepoRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Get-GitValue {
    param(
        [string]$RepoRoot,
        [string[]]$Arguments
    )

    try {
        $value = & git -C $RepoRoot @Arguments 2>$null
        if ($LASTEXITCODE -eq 0) {
            return ($value -join "`n").Trim()
        }
    }
    catch {
    }

    return $null
}

function Resolve-FreeXExe {
    param(
        [string]$RepoRoot,
        [string]$RequestedPath,
        [switch]$SkipBuild
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $resolved = (Resolve-Path $RequestedPath).Path
        if (-not (Test-Path $resolved)) {
            throw "FreeX executable was not found at $RequestedPath"
        }
        return $resolved
    }

    $candidate = Join-Path $RepoRoot "src/FreeX.App.Host/bin/Release/net10.0-windows10.0.19041.0/FreeX.App.Host.exe"
    if (-not (Test-Path $candidate) -and -not $SkipBuild) {
        $buildOutput = & dotnet build (Join-Path $RepoRoot "src/FreeX.App.Host/FreeX.App.Host.csproj") --configuration Release
        $buildOutput | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) {
            throw "FreeX host build failed with exit code $LASTEXITCODE"
        }
    }

    if (-not (Test-Path $candidate)) {
        throw "FreeX host executable was not found. Build Release or pass -FreeXExe. Expected: $candidate"
    }

    return (Resolve-Path $candidate).Path
}

function Resolve-ForegroundCaptureProject {
    param(
        [string]$RepoRoot,
        [switch]$SkipBuild
    )

    $project = Join-Path $RepoRoot "tools/FreeX.ForegroundCapture/FreeX.ForegroundCapture.csproj"

    if (-not (Test-Path $project)) {
        throw "FreeX.ForegroundCapture project was not found. Expected: $project"
    }

    if (-not $SkipBuild) {
        $buildOutput = & dotnet build $project --configuration Release
        $buildOutput | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) {
            throw "FreeX.ForegroundCapture build failed with exit code $LASTEXITCODE"
        }
    }

    return (Resolve-Path $project).Path
}

function Get-ScenarioPairs {
    param([string]$Suite)

    $pairs = @(
        [ordered]@{
            id = "format-cells-dialog"
            area = "Dialogs"
            excelScenario = "excel-format-cells-dialog"
            freexScenario = "freex-format-cells-dialog"
        },
        [ordered]@{
            id = "format-cells-context-dialog"
            area = "Dialogs"
            excelScenario = "excel-format-cells-context-dialog"
            freexScenario = "freex-format-cells-context-dialog"
        },
        [ordered]@{
            id = "open-dialog"
            area = "Native file dialogs"
            excelScenario = "excel-open-dialog"
            freexScenario = "freex-open-dialog"
        },
        [ordered]@{
            id = "save-as-dialog"
            area = "Native file dialogs"
            excelScenario = "excel-save-as-dialog"
            freexScenario = "freex-save-as-dialog"
        },
        [ordered]@{
            id = "sheet-tab-context-menu"
            area = "Sheet tabs"
            excelScenario = "excel-sheet-tab-context-menu"
            freexScenario = "freex-sheet-tab-context-menu"
        },
        [ordered]@{
            id = "sheet-tab-overflow-activate-dialog"
            area = "Sheet tabs"
            excelScenario = "excel-sheet-tab-overflow-activate-dialog"
            freexScenario = "freex-sheet-tab-overflow-activate-dialog"
        }
    )

    switch ($Suite) {
        "smoke" { return $pairs | Where-Object { $_["id"] -in @("format-cells-dialog", "sheet-tab-context-menu") } }
        "dialogs" { return $pairs | Where-Object { $_["area"] -in @("Dialogs", "Native file dialogs") } }
        default { return $pairs }
    }
}

function Read-ScenarioManifest {
    param(
        [string]$OutputDirectory,
        [string]$Scenario
    )

    $path = Join-Path $OutputDirectory (Join-Path $Scenario "$($Scenario)_manifest.json")
    if (-not (Test-Path $path)) {
        return [ordered]@{
            scenario = $Scenario
            captureStatus = "missing-manifest"
            manifestPath = $path
            blockReason = "Scenario did not write its manifest."
        }
    }

    $manifest = Get-Content -Raw $path | ConvertFrom-Json
    return [ordered]@{
        scenario = $Scenario
        captureStatus = $manifest.CaptureStatus
        captureMode = $manifest.CaptureMode
        screenshotPath = $manifest.ScreenshotPath
        continuationScreenshotPath = $manifest.ContinuationScreenshotPath
        resultValidation = $manifest.ResultValidation
        blockReason = $manifest.BlockReason
        manifestPath = $path
    }
}

function Invoke-ForegroundScenario {
    param(
        [string]$ForegroundCaptureProject,
        [string]$Scenario,
        [string]$OutputDirectory,
        [string]$FreeXExe,
        [switch]$NoBuild,
        [int]$MaxAttempts = 2
    )

    $attempts = New-Object System.Collections.Generic.List[object]
    $last = $null

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        if ($attempt -gt 1) {
            Start-Sleep -Seconds 2
        }

        $arguments = @(
            "run",
            "--project",
            $ForegroundCaptureProject,
            "--configuration",
            "Release"
        )

        if ($NoBuild) {
            $arguments += "--no-build"
        }

        $arguments += @(
            "--",
            "--scenario",
            $Scenario,
            "--output",
            $OutputDirectory
        )

        if ($Scenario.StartsWith("freex-", [StringComparison]::OrdinalIgnoreCase)) {
            $arguments += @("--freex-exe", $FreeXExe)
        }

        $rawOutput = & dotnet @arguments 2>&1
        $exitCode = $LASTEXITCODE
        $manifest = Read-ScenarioManifest $OutputDirectory $Scenario
        $last = [ordered]@{
            exitCode = $exitCode
            output = ($rawOutput -join "`n")
            manifest = $manifest
        }

        $attempts.Add([ordered]@{
            attempt = $attempt
            exitCode = $exitCode
            captureStatus = $manifest["captureStatus"]
            blockReason = $manifest["blockReason"]
            manifestPath = $manifest["manifestPath"]
        })

        if ($manifest["captureStatus"] -eq "complete") {
            break
        }

        if (-not ([string]$manifest["blockReason"]).StartsWith("foreground-guard-failed:", [StringComparison]::OrdinalIgnoreCase)) {
            break
        }
    }

    $result = [ordered]@{}
    foreach ($key in $last.Keys) {
        $result[$key] = $last[$key]
    }

    $result["attempts"] = $attempts.Count
    $result["attemptHistory"] = $attempts.ToArray()
    return $result
}

$repoRoot = Get-RepoRoot
if ([string]::IsNullOrWhiteSpace($RunId)) {
    $RunId = Get-Date -Format "yyyyMMdd-HHmmss"
}

$runRoot = Join-Path (Resolve-Path $repoRoot).Path $OutputRoot
$runDirectory = Join-Path $runRoot $RunId
$foregroundOutput = Join-Path $runDirectory "foreground-captures"
$batchManifestPath = Join-Path $runDirectory "ux-scenario-batch.json"
New-Item -ItemType Directory -Force -Path $foregroundOutput | Out-Null

$freeXPath = Resolve-FreeXExe $repoRoot $FreeXExe -SkipBuild:$SkipBuild
$foregroundProject = Resolve-ForegroundCaptureProject $repoRoot -SkipBuild:$SkipBuild
$pairs = @(Get-ScenarioPairs $Suite)
$records = New-Object System.Collections.Generic.List[object]

foreach ($pair in $pairs) {
    Write-Host "Running UX parity pair '$($pair["id"])'..."
    $excel = Invoke-ForegroundScenario $foregroundProject $pair["excelScenario"] $foregroundOutput $freeXPath -NoBuild:$SkipBuild -MaxAttempts $MaxAttempts
    $freex = Invoke-ForegroundScenario $foregroundProject $pair["freexScenario"] $foregroundOutput $freeXPath -NoBuild:$SkipBuild -MaxAttempts $MaxAttempts

    $excelStatus = $excel["manifest"]["captureStatus"]
    $freexStatus = $freex["manifest"]["captureStatus"]
    $pairStatus = if ($excelStatus -eq "complete" -and $freexStatus -eq "complete") {
        "paired-capture-complete"
    }
    elseif ($excelStatus -eq "complete" -or $freexStatus -eq "complete") {
        "partial-capture"
    }
    else {
        "blocked"
    }

    $records.Add([ordered]@{
        id = $pair["id"]
        area = $pair["area"]
        status = $pairStatus
        comparisonStatus = if ($pairStatus -eq "paired-capture-complete") { "needs-human-visual-review" } else { "needs-rerun-or-harness-fix" }
        excel = $excel
        freex = $freex
    })
}

$recordArray = @($records.ToArray())
$pairedCaptureComplete = @($recordArray | Where-Object { $_["status"] -eq "paired-capture-complete" }).Count
$partialCapture = @($recordArray | Where-Object { $_["status"] -eq "partial-capture" }).Count
$blocked = @($recordArray | Where-Object { $_["status"] -eq "blocked" }).Count

$summary = [ordered]@{
    schemaVersion = 1
    suite = $Suite
    status = if ($partialCapture -eq 0 -and $blocked -eq 0) { "ready-for-visual-review" } else { "needs-attention" }
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    machine = $env:COMPUTERNAME
    user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    repo = [ordered]@{
        root = $repoRoot
        branch = Get-GitValue $repoRoot @("rev-parse", "--abbrev-ref", "HEAD")
        commit = Get-GitValue $repoRoot @("rev-parse", "HEAD")
        status = Get-GitValue $repoRoot @("status", "--short", "--branch")
    }
    freexExe = $freeXPath
    foregroundCaptureProject = $foregroundProject
    outputDirectory = $foregroundOutput
    scenarioCount = $recordArray.Count
    pairedCaptureComplete = $pairedCaptureComplete
    partialCapture = $partialCapture
    blocked = $blocked
    records = $recordArray
}

$summary | ConvertTo-Json -Depth 20 | Set-Content -Path $batchManifestPath -Encoding UTF8

Write-Host "UX parity scenario batch manifest: $batchManifestPath"
if ($summary.status -ne "ready-for-visual-review") {
    exit 1
}
