<#
.SYNOPSIS
  Run the reusable physical X11 smoke baseline for FreeW or FreeP in Docker.

.DESCRIPTION
  Starts one harness-owned Avalonia application using the generic interactive Linux
  desktop, injects the family-parameterized X11 probe, retains screenshots and a
  strictly contract-checked manifest, and stops only the container it started unless
  -KeepContainer is supplied.

  This is a deterministic baseline, not exhaustive command or dialog parity. FreeX
  continues to use Run-FreeXLinuxInteractionValidation.ps1 for its exhaustive lane.
#>
[CmdletBinding()]
param(
    [ValidateSet("FreeW", "FreeP")]
    [string]$App = "FreeW",

    [ValidateRange(1024, 65535)]
    [int]$Port = 6090,

    [ValidateRange(640, 7680)]
    [int]$Width = 1280,

    [ValidateRange(480, 4320)]
    [int]$Height = 820,

    [ValidateRange(72, 240)]
    [int]$Dpi = 96,

    [ValidateSet("2g", "4g", "6g", "8g")]
    [string]$MemoryLimit = "4g",

    [string]$OutputDir = "artifacts/linux-family-interactive",
    [string]$PublishDir = "",
    [switch]$SkipPublish,
    [switch]$SkipImageBuild,
    [switch]$Replace,
    [switch]$KeepContainer
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "VisualEvidenceScriptSupport.ps1")
$manifestEvidenceHelper = Join-Path $PSScriptRoot "LinuxInteractiveDocker/ManifestEvidence.ps1"
$null = . $manifestEvidenceHelper
$genericRunner = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$probeSource = Join-Path $PSScriptRoot "LinuxInteractiveDocker/run-family-input-probes.sh"
$schemaPath = Join-Path $PSScriptRoot "LinuxInteractiveDocker/family-x11-validation.schema.json"
$resolvedOutputRoot = Resolve-VisualEvidenceOutputDirectory -OutputDirectory $OutputDir -RepoRoot $repoRoot
$appKey = $App.ToLowerInvariant()

$probeParameters = @{
    FreeW = [ordered]@{
        WindowPattern = "FreeW"
        RibbonTabKey = "I"
        FileKey = "F"
        FileSurface = "top-level-backstage-window"
    }
    FreeP = [ordered]@{
        WindowPattern = "FreeP"
        RibbonTabKey = "N"
        FileKey = "F"
        FileSurface = "in-window-backstage-overlay"
    }
}[$App]

function Assert-ManifestContract {
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$EvidenceDirectory
    )

    $manifest = Read-ManifestContract -ManifestPath $ManifestPath -SchemaPath $schemaPath
    Assert-ManifestIdentity -Manifest $manifest -Expected ([ordered]@{
        schemaVersion = 1; suite = "family-linux-physical-baseline"; platform = "linux"
        shell = "avalonia"; app = $App; baseline = $true
    }) -FailureMessage "Manifest header does not satisfy the family physical-baseline contract."
    if ($manifest.coverage.exhaustive -ne $false -or
        $manifest.parameters.fileSurface -ne $probeParameters.FileSurface -or
        $manifest.parameters.ribbonTabKey -ne $probeParameters.RibbonTabKey -or
        $manifest.parameters.fileKey -ne $probeParameters.FileKey -or
        $manifest.appSurface -ne $probeParameters.FileSurface) {
        throw "Manifest header does not satisfy the family physical-baseline contract."
    }

    $requiredIds = @(
        "visible-window-discovery",
        "alt-keytips-appearance",
        "alt-keytips-dismissal",
        "f10-keytips-appearance",
        "f10-keytips-dismissal",
        "ribbon-tab-keytip-switch",
        "file-surface-open",
        "file-surface-dismissal"
    )
    if ($App -eq "FreeW") {
        $requiredIds += @(
            "editor-sentinel-copy",
            "editor-autocorrect-typing",
            "editor-undo-restores-clipboard",
            "editor-redo-restores-clipboard",
            "editor-cut-undo-restores",
            "editor-paste-text-only",
            "editor-find-open",
            "editor-find-dismissal",
            "editor-replace-open",
            "editor-replace-dismissal",
            "editor-reveal-formatting-open",
            "editor-reveal-formatting-dismissal",
            "editor-thesaurus-open",
            "editor-thesaurus-dismissal",
            "editor-keyboard-context-open",
            "editor-keyboard-context-dismissal",
            "editor-pointer-context-open",
            "editor-pointer-context-dismissal",
            "file-open-shortcut-dialog-open",
            "file-open-shortcut-dialog-dismissal",
            "file-save-shortcut-dialog-open",
            "file-save-shortcut-dialog-dismissal",
            "file-save-as-shortcut-dialog-open",
            "file-save-as-shortcut-dialog-dismissal",
            "file-print-shortcut-dialog-open",
            "file-print-shortcut-dialog-dismissal",
            "file-new-shortcut-dirty-prompt-open",
            "file-new-shortcut-cancel-preserves",
            "file-new-shortcut-discard-creates-clean",
            "backstage-print-open",
            "backstage-print-dismissal",
            "backstage-export-open",
            "backstage-export-dismissal",
            "options-open",
            "options-tab-navigation",
            "options-focus",
            "options-close"
        )
    } else {
        $requiredIds += @(
            "nested-keytip-prefix-deferral",
            "animation-pane-physical-workflow",
            "slide-pane-new-slide-create",
            "slide-pane-new-slide-undo",
            "slide-pane-new-slide-redo",
            "slide-pane-keyboard-context-open",
            "slide-pane-keyboard-context-dismissal",
            "slide-pane-pointer-context-open",
            "slide-pane-pointer-context-dismissal",
            "slide-pane-pointer-select-second",
            "slide-pane-keyboard-up-first",
            "slide-pane-duplicate-create",
            "slide-pane-duplicate-undo",
            "slide-pane-duplicate-redo",
            "slide-pane-delete-selected",
            "slide-pane-delete-undo"
        )
    }
    $results = @($manifest.results)
    $ids = @($results | ForEach-Object { [string]$_.id })
    if ($ids.Count -ne ($ids | Select-Object -Unique).Count) {
        throw "Manifest contains duplicate result IDs."
    }
    $expectedResultCount = if ($App -eq "FreeP") { 24 } else { 45 }
    if ($results.Count -ne $expectedResultCount) {
        throw "$App family baseline must contain exactly $expectedResultCount result rows."
    }
    foreach ($requiredId in $requiredIds) {
        if ($ids -notcontains $requiredId) {
            throw "Manifest is missing required result '$requiredId'."
        }
    }

    Assert-ManifestResultSummary -Manifest $manifest -Results $results -ExpectedTotal $results.Count

    $fileMap = Get-ManifestEvidenceFileMap -EvidenceDirectory $EvidenceDirectory
    Assert-ManifestResultEvidence -Results $results -FileMap $fileMap `
        -Category "physical-x11-smoke" -EvidenceLevel "physical-x11-input"
    Assert-ManifestScreenshotEvidence -Screenshots @($manifest.screenshots) -FileMap $fileMap

    return Complete-ManifestContract -Manifest $manifest -ManifestPath $ManifestPath `
        -Validator "tools/Run-FamilyLinuxInteractionValidation.ps1" `
        -ContractReference "tools/LinuxInteractiveDocker/family-x11-validation.schema.json"
}

if (-not (Test-Path -LiteralPath $probeSource -PathType Leaf)) {
    throw "Family probe is missing: $probeSource"
}

$sessionDirectory = $null
$started = $false
try {
    $startArguments = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
        "-Action", "Start", "-App", $App, "-Port", "$Port",
        "-Width", "$Width", "-Height", "$Height", "-Dpi", "$Dpi",
        "-MemoryLimit", $MemoryLimit, "-OutputDir", $resolvedOutputRoot
    )
    if (-not [string]::IsNullOrWhiteSpace($PublishDir)) {
        $startArguments += @("-PublishDir", $PublishDir)
    }
    if ($App -eq "FreeP") {
        $startArguments += @(
            "-Host", "Validation",
            "-AppArgument", "--physical-animation-pane-fixture")
    }
    if ($SkipPublish) { $startArguments += "-SkipPublish" }
    if ($SkipImageBuild) { $startArguments += "-SkipImageBuild" }
    if ($Replace) { $startArguments += "-Replace" }
    if ($App -eq "FreeW") { $startArguments += "-CupsDryRun" }

    Invoke-VisualEvidenceProcess -FilePath "powershell.exe" -Arguments $startArguments -WorkingDirectory $repoRoot
    $started = $true

    $currentSessionPath = Join-Path $resolvedOutputRoot "$appKey/current-session.json"
    if (-not (Test-Path -LiteralPath $currentSessionPath -PathType Leaf)) {
        throw "Generic runner did not write current session metadata: $currentSessionPath"
    }
    $session = Get-Content -LiteralPath $currentSessionPath -Raw | ConvertFrom-Json
    $sessionDirectory = [IO.Path]::GetFullPath([string]$session.sessionDirectory)
    if (-not (Test-Path -LiteralPath $sessionDirectory -PathType Container)) {
        throw "Session directory does not exist: $sessionDirectory"
    }

    $probeInWork = Join-Path $sessionDirectory "family-input-probes.sh"
    Copy-Item -LiteralPath $probeSource -Destination $probeInWork -Force
    $probeLog = Join-Path $sessionDirectory "family-validation/probe.log"
    New-Item -ItemType Directory -Path (Split-Path -Parent $probeLog) -Force | Out-Null
    New-Item -ItemType File -Path $probeLog -Force | Out-Null

    $dockerArguments = @(
        "exec", "--env", "FAMILY_APP=$App",
        "--env", "FAMILY_WINDOW_PATTERN=$($probeParameters.WindowPattern)",
        "--env", "FAMILY_TAB_KEY=$($probeParameters.RibbonTabKey)",
        "--env", "FAMILY_FILE_KEY=$($probeParameters.FileKey)",
        "--env", "FAMILY_FILE_SURFACE=$($probeParameters.FileSurface)",
        [string]$session.containerName, "bash", "/work/family-input-probes.sh", "/work/family-validation"
    )
    Push-Location $repoRoot
    try {
        $probeOutput = @(& docker @dockerArguments 2>&1)
        $probeExitCode = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    if ($probeOutput.Count -gt 0) {
        $probeOutput | Set-Content -LiteralPath $probeLog -Encoding utf8
    } else {
        "docker exec produced no stdout/stderr; inspect family-x11-results.json for probe evidence." |
            Set-Content -LiteralPath $probeLog -Encoding utf8
    }

    $manifestPath = Join-Path $sessionDirectory "family-validation/family-x11-results.json"
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        $failureEvidenceName = "probe-runner-failure.txt"
        $failureEvidencePath = Join-Path (Split-Path -Parent $manifestPath) $failureEvidenceName
        @(
            "The family probe exited without writing its manifest.",
            "docker-exit-code=$probeExitCode",
            "probe-log=$probeLog",
            "probe-output=$([string]::Join([Environment]::NewLine, $probeOutput))"
        ) | Set-Content -LiteralPath $failureEvidencePath -Encoding utf8

        $failureIds = @(
            "visible-window-discovery",
            "alt-keytips-appearance",
            "alt-keytips-dismissal",
            "f10-keytips-appearance",
            "f10-keytips-dismissal",
            "ribbon-tab-keytip-switch",
            "file-surface-open",
            "file-surface-dismissal"
        )
        if ($App -eq "FreeW") {
            $failureIds += @(
                "editor-sentinel-copy",
                "editor-autocorrect-typing",
                "editor-undo-restores-clipboard",
                "editor-redo-restores-clipboard",
                "editor-cut-undo-restores",
                "editor-paste-text-only",
                "editor-find-open",
                "editor-find-dismissal",
                "editor-replace-open",
                "editor-replace-dismissal",
                "editor-reveal-formatting-open",
                "editor-reveal-formatting-dismissal",
                "editor-thesaurus-open",
                "editor-thesaurus-dismissal",
                "editor-keyboard-context-open",
                "editor-keyboard-context-dismissal",
                "editor-pointer-context-open",
                "editor-pointer-context-dismissal",
                "file-open-shortcut-dialog-open",
                "file-open-shortcut-dialog-dismissal",
                "file-save-shortcut-dialog-open",
                "file-save-shortcut-dialog-dismissal",
                "file-save-as-shortcut-dialog-open",
                "file-save-as-shortcut-dialog-dismissal",
                "file-print-shortcut-dialog-open",
                "file-print-shortcut-dialog-dismissal",
                "file-new-shortcut-dirty-prompt-open",
                "file-new-shortcut-cancel-preserves",
                "file-new-shortcut-discard-creates-clean",
                "backstage-print-open",
                "backstage-print-dismissal",
                "backstage-export-open",
                "backstage-export-dismissal",
                "options-open",
                "options-tab-navigation",
                "options-focus",
                "options-close"
            )
        } else {
            $failureIds += @(
                "animation-pane-physical-workflow",
                "slide-pane-new-slide-create",
                "slide-pane-new-slide-undo",
                "slide-pane-new-slide-redo",
                "slide-pane-keyboard-context-open",
                "slide-pane-keyboard-context-dismissal",
                "slide-pane-pointer-context-open",
                "slide-pane-pointer-context-dismissal",
                "slide-pane-pointer-select-second",
                "slide-pane-keyboard-up-first",
                "slide-pane-duplicate-create",
                "slide-pane-duplicate-undo",
                "slide-pane-duplicate-redo",
                "slide-pane-delete-selected",
                "slide-pane-delete-undo"
            )
        }
        $failureResults = @($failureIds | ForEach-Object {
            [ordered]@{
                id = $_
                category = "physical-x11-smoke"
                status = "failed"
                evidenceLevel = "physical-x11-input"
                evidence = @($failureEvidenceName)
                note = "Probe runner exited before producing row-specific evidence."
            }
        })
        $failureScreenshotName = $null
        $initialScreenshotPath = Join-Path $sessionDirectory "screenshots/initial.png"
        if (Test-Path -LiteralPath $initialScreenshotPath -PathType Leaf) {
            $failureScreenshotName = "probe-runner-failure.png"
            Copy-Item -LiteralPath $initialScreenshotPath -Destination (Join-Path (Split-Path -Parent $manifestPath) $failureScreenshotName) -Force
        }
        $failureScreenshots = @()
        if ($null -ne $failureScreenshotName) {
            $failureScreenshots = @([ordered]@{ name = $failureScreenshotName; kind = "screenshot" })
        }
        $failureManifest = [ordered]@{
            schemaVersion = 1
            suite = "family-linux-physical-baseline"
            platform = "linux"
            shell = "avalonia"
            app = $App
            baseline = $true
            appSurface = $probeParameters.FileSurface
            window = [ordered]@{
                id = [string]$session.windowId
                title = [string]$session.windowTitle
                pattern = $probeParameters.WindowPattern
                visible = $true
            }
            parameters = [ordered]@{
                ribbonTabKey = $probeParameters.RibbonTabKey
                fileKey = $probeParameters.FileKey
                fileSurface = $probeParameters.FileSurface
            }
            coverage = [ordered]@{
                scope = "deterministic physical X11 smoke baseline"
                exhaustive = $false
                exhaustiveFreeXRunner = "tools/Run-FreeXLinuxInteractionValidation.ps1"
            }
            contractValidation = [ordered]@{
                status = "pending"
                validator = "tools/Run-FamilyLinuxInteractionValidation.ps1"
                contractReference = "tools/LinuxInteractiveDocker/family-x11-validation.schema.json"
            }
            screenshots = $failureScreenshots
            summary = [ordered]@{
                passed = 0
                failed = $failureResults.Count
                total = $failureResults.Count
            }
            results = $failureResults
        }
        $failureManifest | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath -Encoding utf8
        Write-Warning "Family probe did not write a manifest; durable failure manifest was created at $manifestPath"
    }
    $evidenceDirectory = Split-Path -Parent $manifestPath
    Wait-ForManifestEvidence -ManifestPath $manifestPath -EvidenceDirectory $evidenceDirectory
    $manifest = Assert-ManifestContract -ManifestPath $manifestPath -EvidenceDirectory $evidenceDirectory
    Write-Host "Manifest contract validation: $($manifest.contractValidation.status)"
    Write-Host "Results: $($manifest.summary.passed) passed, $($manifest.summary.failed) failed, $($manifest.summary.total) total"
    Write-Host "Manifest: $manifestPath"
    Write-Host "Screenshots: $(Split-Path -Parent $manifestPath)"
    if ($probeExitCode -ne 0 -or $manifest.summary.failed -gt 0) {
        throw "Family physical probe failed with exit code $probeExitCode and $($manifest.summary.failed) failed result(s). Evidence was retained at $manifestPath; probe log: $probeLog"
    }
} finally {
    if ($started -and -not $KeepContainer) {
        try {
            Invoke-VisualEvidenceProcess -FilePath "powershell.exe" -Arguments @(
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $genericRunner,
                "-Action", "Stop", "-App", $App, "-Port", "$Port",
                "-OutputDir", $resolvedOutputRoot
            ) -WorkingDirectory $repoRoot
        } catch {
            Write-Warning "Could not stop harness-owned $App container on port ${Port}: $($_.Exception.Message)"
        }
    } elseif ($started) {
        Write-Host "Container retained by request on port $Port."
    }
}
