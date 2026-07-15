param(
    [string]$JsonPath = "docs\parity\conditional-format-opened-state-evidence.json",
    [string]$MarkdownPath = "docs\parity\conditional-format-opened-state-evidence.md",
    [switch]$Check
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Resolve-ScreenshotPath {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$ManifestPath
    )

    if ($null -eq $Manifest -or [string]::IsNullOrWhiteSpace($Manifest.ScreenshotPath)) {
        return ""
    }

    if (Test-Path -LiteralPath $Manifest.ScreenshotPath -PathType Leaf) {
        return ConvertTo-ToolRepoRelativePath -Path $Manifest.ScreenshotPath -RepoRoot $repoRoot
    }

    $manifestDirectory = Split-Path -Parent $ManifestPath
    $localCandidate = Join-Path $manifestDirectory ([System.IO.Path]::GetFileName($Manifest.ScreenshotPath))
    if (Test-Path -LiteralPath $localCandidate -PathType Leaf) {
        return ConvertTo-ToolRepoRelativePath -Path $localCandidate -RepoRoot $repoRoot
    }

    return $Manifest.ScreenshotPath
}

function Get-ManifestString {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    if ($null -eq $Manifest -or -not ($Manifest.PSObject.Properties.Name -contains $PropertyName)) {
        return ""
    }

    $value = $Manifest.$PropertyName
    if ($null -eq $value) {
        return ""
    }

    return [string]$value
}

function Get-BlockerCategory {
    param(
        [Parameter(Mandatory = $true)][string]$CaptureStatus,
        [Parameter(Mandatory = $true)][bool]$ScreenshotExists,
        [Parameter(Mandatory = $true)][bool]$ManifestMatchesTarget,
        [string]$BlockReason = "",
        [string[]]$StructuralManifestErrors = @()
    )

    if (-not $ManifestMatchesTarget) {
        return "manifest-target-mismatch"
    }

    if ($StructuralManifestErrors.Count -gt 0) {
        return "manifest-invalid"
    }

    if ($CaptureStatus -eq "complete" -and $ScreenshotExists) {
        return "none"
    }

    if ($CaptureStatus -eq "missing-manifest") {
        return "missing-manifest"
    }

    if ($CaptureStatus -eq "complete") {
        return "manifest-missing-screenshot"
    }

    if ($BlockReason -match "Excel\.Application COM ProgID is not available") {
        return "excel-com-unavailable"
    }

    if ($BlockReason -match "foreground-guard-failed|No foreground window detected") {
        return "foreground-focus-unavailable"
    }

    if ($BlockReason -match "popup-not-found") {
        return "popup-not-found"
    }

    if ($BlockReason -match "^(launch-failed|window-not-found):") {
        return "app-window-unavailable"
    }

    if ($BlockReason -match "^exception:") {
        return "scenario-exception"
    }

    return "blocked-or-incomplete"
}

function Get-ManifestValidationErrors {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][bool]$ManifestExists,
        [Parameter(Mandatory = $true)][bool]$ManifestMatchesTarget,
        [Parameter(Mandatory = $true)][string]$CaptureStatus,
        [Parameter(Mandatory = $true)][bool]$ScreenshotExists,
        [string]$ScreenshotPath = "",
        [string]$BlockReason = ""
    )

    $errors = @()

    if (-not $ManifestExists) {
        return @("manifest-file-missing")
    }

    if (-not $ManifestMatchesTarget) {
        $errors += "manifest-target-mismatch"
    }

    if (-not ($Manifest.PSObject.Properties.Name -contains "EnvironmentSnapshot") -or $null -eq $Manifest.EnvironmentSnapshot) {
        $errors += "environment-snapshot-missing"
    }
    else {
        foreach ($propertyName in @("OperatingSystem", "IsWindows", "UserInteractive", "SessionId", "ProcessId", "ProcessArchitecture")) {
            if (-not ($Manifest.EnvironmentSnapshot.PSObject.Properties.Name -contains $propertyName)) {
                $errors += "environment-snapshot-$($propertyName.ToLowerInvariant())-missing"
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($CaptureStatus)) {
        $errors += "capture-status-missing"
    }
    elseif ($CaptureStatus -notin @("complete", "blocked")) {
        $errors += "capture-status-unknown"
    }

    if ($CaptureStatus -eq "complete") {
        if ([string]::IsNullOrWhiteSpace($ScreenshotPath)) {
            $errors += "screenshot-path-missing"
        }
        elseif (-not $ScreenshotExists) {
            $errors += "screenshot-file-missing"
        }
    }
    elseif ($CaptureStatus -eq "blocked") {
        if ([string]::IsNullOrWhiteSpace($BlockReason)) {
            $errors += "block-reason-missing"
        }

        $capturedAtUtc = Get-ManifestString -Manifest $Manifest -PropertyName "CapturedAtUtc"
        if ([string]::IsNullOrWhiteSpace($capturedAtUtc)) {
            $errors += "captured-at-utc-missing"
        }

        if ($BlockReason -match "foreground-guard-failed") {
            if (-not ($Manifest.PSObject.Properties.Name -contains "ForegroundGuard") -or $null -eq $Manifest.ForegroundGuard) {
                $errors += "foreground-guard-diagnostics-missing"
            }
            else {
                $guardReason = Get-ManifestString -Manifest $Manifest.ForegroundGuard -PropertyName "Reason"
                if ([string]::IsNullOrWhiteSpace($guardReason)) {
                    $errors += "foreground-guard-reason-missing"
                }
            }
        }
    }

    return @($errors)
}

function Get-StructuralManifestErrors {
    param([string[]]$ManifestValidationErrors)

    return @($ManifestValidationErrors | Where-Object {
            $_ -notin @(
                "manifest-file-missing",
                "manifest-target-mismatch",
                "screenshot-path-missing",
                "screenshot-file-missing"
            )
        })
}

function Get-NextCaptureAction {
    param(
        [Parameter(Mandatory = $true)][string]$BlockerCategory,
        [Parameter(Mandatory = $true)][string]$RunnerCommand,
        [Parameter(Mandatory = $true)][string]$RequiredEnvironment
    )

    switch ($BlockerCategory) {
        "none" { return "No action required; retained PNG resolves in the repo." }
        "missing-manifest" { return "Run $RunnerCommand in $RequiredEnvironment Commit the manifest and PNG, then rerun this generator." }
        "manifest-invalid" { return "Inspect the committed manifest diagnostics, then rerun $RunnerCommand in $RequiredEnvironment Preserve the blocked manifest if the environment still cannot produce a real PNG." }
        "manifest-missing-screenshot" { return "Rerun $RunnerCommand and commit both the complete manifest and the referenced PNG." }
        "manifest-target-mismatch" { return "Discard the stale manifest and rerun $RunnerCommand so Scenario and Subject match this target." }
        "excel-com-unavailable" { return "Rerun $RunnerCommand on a Windows desktop where Microsoft Excel COM is installed and registered." }
        "foreground-focus-unavailable" { return "Rerun $RunnerCommand from an unlocked interactive desktop where the launched window can become foreground." }
        "app-window-unavailable" { return "Rerun $RunnerCommand after verifying the Release app starts manually in the same desktop session and exposes a visible main window." }
        "popup-not-found" { return "Rerun $RunnerCommand from foreground; if it still blocks, inspect the UIA/keytip route for the Conditional Formatting popup." }
        default { return "Rerun $RunnerCommand in $RequiredEnvironment Preserve any blocked manifest with its BlockReason if a real PNG cannot be produced." }
    }
}

function Get-CaptureTargetStatus {
    param(
        [Parameter(Mandatory = $true)][string]$Id,
        [Parameter(Mandatory = $true)][string]$Subject,
        [Parameter(Mandatory = $true)][string]$Scenario,
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$ExpectedOpenedState,
        [Parameter(Mandatory = $true)][string]$RunnerCommand,
        [Parameter(Mandatory = $true)][string]$RequiredEnvironment,
        [string]$ManifestSubject = "",
        [string]$FallbackEvidencePath = "",
        [string]$FallbackEvidenceNote = ""
    )

    $resolvedManifestPath = Resolve-ToolRepoPath -Path $ManifestPath -RepoRoot $repoRoot
    $fallbackEvidence = @()
    if (-not [string]::IsNullOrWhiteSpace($FallbackEvidencePath)) {
        $resolvedFallbackPath = Resolve-ToolRepoPath -Path $FallbackEvidencePath -RepoRoot $repoRoot
        if (Test-Path -LiteralPath $resolvedFallbackPath -PathType Leaf) {
            $fallbackEvidence += ConvertTo-ToolRepoRelativePath -Path $resolvedFallbackPath -RepoRoot $repoRoot
        }
    }

    if (-not (Test-Path -LiteralPath $resolvedManifestPath -PathType Leaf)) {
        return [ordered]@{
            id = $Id
            subject = $Subject
            scenario = $Scenario
            expectedOpenedState = $ExpectedOpenedState
            runnerCommand = $RunnerCommand
            requiredEnvironment = $RequiredEnvironment
            manifestPath = $ManifestPath
            captureStatus = "missing-manifest"
            screenshotPath = ""
            screenshotExists = $false
            retentionStatus = "needs-capture"
            blockReason = "No committed foreground capture manifest exists for this opened-state target."
            blockerCategory = "missing-manifest"
            manifestValidationStatus = "missing"
            manifestValidationErrors = @("manifest-file-missing")
            nextCaptureAction = Get-NextCaptureAction -BlockerCategory "missing-manifest" -RunnerCommand $RunnerCommand -RequiredEnvironment $RequiredEnvironment
            lastAttemptedAtUtc = ""
            manifestSubject = ""
            manifestScenario = ""
            manifestMatchesTarget = $false
            environmentSnapshotStatus = "missing"
            environmentSummary = ""
            fallbackEvidence = @($fallbackEvidence)
            fallbackEvidenceNote = $FallbackEvidenceNote
        }
    }

    $manifest = Read-ToolJson -Path $resolvedManifestPath -RepoRoot $repoRoot -MissingMessage "Capture manifest was not found"
    $expectedManifestSubject = if ([string]::IsNullOrWhiteSpace($ManifestSubject)) { $Subject } else { $ManifestSubject }
    $manifestSubject = Get-ManifestString -Manifest $manifest -PropertyName "Subject"
    $manifestScenario = Get-ManifestString -Manifest $manifest -PropertyName "Scenario"
    $manifestMatchesTarget = $manifestSubject -eq $expectedManifestSubject -and $manifestScenario -eq $Scenario
    $resolvedScreenshot = Resolve-ScreenshotPath -Manifest $manifest -ManifestPath $resolvedManifestPath
    $screenshotExists = -not [string]::IsNullOrWhiteSpace($resolvedScreenshot) -and
        (Test-Path -LiteralPath (Resolve-ToolRepoPath -Path $resolvedScreenshot -RepoRoot $repoRoot) -PathType Leaf -ErrorAction SilentlyContinue)
    if (-not $screenshotExists -and -not [string]::IsNullOrWhiteSpace($resolvedScreenshot)) {
        $screenshotExists = Test-Path -LiteralPath $resolvedScreenshot -PathType Leaf -ErrorAction SilentlyContinue
    }

    $captureStatus = if ([string]::IsNullOrWhiteSpace($manifest.CaptureStatus)) { "unknown" } else { [string]$manifest.CaptureStatus }
    $retentionStatus = if ($captureStatus -eq "complete" -and $screenshotExists) {
        "retained-opened-state-capture"
    }
    elseif ($captureStatus -eq "complete") {
        "manifest-missing-screenshot"
    }
    else {
        "blocked-or-incomplete"
    }

    $blockReason = if ([string]::IsNullOrWhiteSpace($manifest.BlockReason)) { "" } else { [string]$manifest.BlockReason }
    $manifestValidationErrors = Get-ManifestValidationErrors `
        -Manifest $manifest `
        -ManifestExists $true `
        -ManifestMatchesTarget $manifestMatchesTarget `
        -CaptureStatus $captureStatus `
        -ScreenshotPath $resolvedScreenshot `
        -ScreenshotExists ([bool]$screenshotExists) `
        -BlockReason $blockReason
    $structuralManifestErrors = Get-StructuralManifestErrors -ManifestValidationErrors $manifestValidationErrors
    $manifestValidationStatus = if ($manifestValidationErrors.Count -eq 0) { "valid" } else { "invalid" }
    $blockerCategory = Get-BlockerCategory `
        -CaptureStatus $captureStatus `
        -ScreenshotExists ([bool]$screenshotExists) `
        -ManifestMatchesTarget $manifestMatchesTarget `
        -BlockReason $blockReason `
        -StructuralManifestErrors $structuralManifestErrors
    $environmentSnapshotStatus = if (($manifest.PSObject.Properties.Name -contains "EnvironmentSnapshot") -and $null -ne $manifest.EnvironmentSnapshot) {
        "captured"
    }
    else {
        "missing"
    }
    $environmentSummary = if ($environmentSnapshotStatus -eq "captured") {
        "windows=$($manifest.EnvironmentSnapshot.IsWindows); interactive=$($manifest.EnvironmentSnapshot.UserInteractive); session=$($manifest.EnvironmentSnapshot.SessionId); arch=$($manifest.EnvironmentSnapshot.ProcessArchitecture)"
    }
    else {
        ""
    }

    [ordered]@{
        id = $Id
        subject = $Subject
        scenario = $Scenario
        expectedOpenedState = $ExpectedOpenedState
        runnerCommand = $RunnerCommand
        requiredEnvironment = $RequiredEnvironment
        manifestPath = ConvertTo-ToolRepoRelativePath -Path $resolvedManifestPath -RepoRoot $repoRoot
        captureStatus = $captureStatus
        screenshotPath = $resolvedScreenshot
        screenshotExists = [bool]$screenshotExists
        retentionStatus = $retentionStatus
        blockReason = $blockReason
        blockerCategory = $blockerCategory
        manifestValidationStatus = $manifestValidationStatus
        manifestValidationErrors = @($manifestValidationErrors)
        nextCaptureAction = Get-NextCaptureAction -BlockerCategory $blockerCategory -RunnerCommand $RunnerCommand -RequiredEnvironment $RequiredEnvironment
        lastAttemptedAtUtc = Get-ManifestString -Manifest $manifest -PropertyName "CapturedAtUtc"
        manifestSubject = $manifestSubject
        manifestScenario = $manifestScenario
        manifestMatchesTarget = $manifestMatchesTarget
        environmentSnapshotStatus = $environmentSnapshotStatus
        environmentSummary = $environmentSummary
        fallbackEvidence = @($fallbackEvidence)
        fallbackEvidenceNote = $FallbackEvidenceNote
    }
}

function New-OperatorChecklistItem {
    param(
        [Parameter(Mandatory = $true)][string]$Phase,
        [Parameter(Mandatory = $true)][string]$Command,
        [Parameter(Mandatory = $true)][string]$Purpose
    )

    [ordered]@{
        phase = $Phase
        command = $Command
        purpose = $Purpose
    }
}

function Get-OperatorChecklist {
    param([Parameter(Mandatory = $true)]$CaptureTargets)

    $items = @(
        New-OperatorChecklistItem `
            -Phase "build" `
            -Command "dotnet build FreeX.slnx --configuration Release" `
            -Purpose "Produces the Release WPF and Avalonia executables referenced by the FreeX capture commands."
        New-OperatorChecklistItem `
            -Phase "preflight" `
            -Command ".\tools\Invoke-ForegroundCapture.ps1 -EnvironmentPreflight" `
            -Purpose "Emits machine-readable readiness diagnostics for Windows foreground focus, Release executables, and Microsoft Excel COM before foreground input."
    )

    foreach ($target in $CaptureTargets) {
        $items += New-OperatorChecklistItem `
            -Phase "capture:$($target.subject)" `
            -Command $target.runnerCommand `
            -Purpose "Run from an unlocked foreground desktop; keep blocked manifests if the environment cannot produce a real opened-state PNG."
    }

    $items += New-OperatorChecklistItem `
        -Phase "refresh" `
        -Command ".\tools\Generate-ConditionalFormatOpenedStateEvidence.ps1; .\tools\Generate-ConditionalFormatOpenedStateEvidence.ps1 -Check" `
        -Purpose "Refreshes and verifies this report after the real manifests and PNGs are committed under tools/foreground-captures/<scenario>/."

    return @($items)
}

$classification = Read-ToolJson -Path "docs\parity\functional-parity-classification.json" -RepoRoot $repoRoot -MissingMessage "Required generated parity input is missing"
$conditionalRows = @($classification.rows | Where-Object {
        $_.classification -eq "pseudo-command-gallery-item" -and
        $_.tab -eq "Home" -and
        $_.group -eq "Styles" -and
        $_.nextAction -eq "Use the conditional-format popup/gallery parity lane for richer evidence instead of adding placeholder handlers."
    } | Sort-Object -Property id)

$expectedConditionalRows = [int]$classification.summary.'conditional-format-popup-gallery-row'
$runtimeCatalogItems = [int]$classification.summary.'conditional-format-popup-catalog-item'
if ($conditionalRows.Count -ne $expectedConditionalRows) {
    throw "Conditional-format popup/gallery row count mismatch: summary says $expectedConditionalRows, found $($conditionalRows.Count)."
}

$captureTargets = @(
    Get-CaptureTargetStatus `
        -Id "excel.conditional-formatting-gallery.opened" `
        -Subject "excel" `
        -Scenario "excel-conditional-formatting-gallery" `
        -ManifestPath "tools\foreground-captures\excel-conditional-formatting-gallery\excel-conditional-formatting-gallery_manifest.json" `
        -ExpectedOpenedState "Excel Home > Conditional Formatting opened popup/gallery" `
        -RunnerCommand ".\tools\Invoke-ForegroundCapture.ps1 -Scenario excel-conditional-formatting-gallery" `
        -RequiredEnvironment "Windows desktop session with Microsoft Excel COM registered and foreground focus allowed."
    Get-CaptureTargetStatus `
        -Id "wpf.conditional-formatting-gallery.opened" `
        -Subject "wpf" `
        -Scenario "freex-conditional-formatting-gallery" `
        -ManifestPath "tools\foreground-captures\freex-conditional-formatting-gallery\freex-conditional-formatting-gallery_manifest.json" `
        -ExpectedOpenedState "FreeX WPF Home > Conditional Formatting opened popup/gallery" `
        -RunnerCommand ".\tools\Invoke-ForegroundCapture.ps1 -Scenario freex-conditional-formatting-gallery -FreeXExe <Release FreeX.App.Host.exe>" `
        -RequiredEnvironment "Windows desktop session with built Release WPF host and foreground focus allowed." `
        -ManifestSubject "freex"
    Get-CaptureTargetStatus `
        -Id "avalonia.conditional-formatting-gallery.opened" `
        -Subject "avalonia" `
        -Scenario "avalonia-conditional-formatting-gallery" `
        -ManifestPath "tools\foreground-captures\avalonia-conditional-formatting-gallery\avalonia-conditional-formatting-gallery_manifest.json" `
        -ExpectedOpenedState "FreeX Avalonia Home > Conditional Formatting opened popup/gallery" `
        -RunnerCommand ".\tools\Invoke-ForegroundCapture.ps1 -Scenario avalonia-conditional-formatting-gallery -AvaloniaExe <Release FreeX.exe>" `
        -RequiredEnvironment "Windows desktop session with built Release Avalonia app and foreground focus allowed." `
        -FallbackEvidencePath "docs\parity\dialog-visual-assets\avalonia-capture\dialog.ConditionalFormatNewRule.png" `
        -FallbackEvidenceNote "Existing Avalonia dialog route evidence is retained for dialog parity, but it is not opened popup/gallery evidence."
)

$completeOpenedStateTargets = @($captureTargets | Where-Object {
        $_.captureStatus -eq "complete" -and $_.screenshotExists -eq $true
    }).Count
$openedStateStatus = if ($completeOpenedStateTargets -eq $captureTargets.Count) {
    "paired-opened-state-captured"
}
else {
    "needs-paired-opened-state-capture"
}
$blockerCategories = @($captureTargets |
    Group-Object -Property { $_.blockerCategory } |
    Sort-Object -Property Name |
    ForEach-Object {
        [ordered]@{
            category = [string]$_.Name
            count = [int]$_.Count
        }
    })
$operatorChecklist = Get-OperatorChecklist -CaptureTargets $captureTargets

$rows = foreach ($row in $conditionalRows) {
    [ordered]@{
        id = [string]$row.id
        tab = [string]$row.tab
        group = [string]$row.group
        classification = [string]$row.classification
        runtimeCatalogBacked = $true
        openedStateEvidenceStatus = $openedStateStatus
    }
}

$report = [ordered]@{
    schema = "freex.parity.conditional-format-opened-state-evidence.v1"
    generatedBy = "tools/Generate-ConditionalFormatOpenedStateEvidence.ps1"
    source = "docs/parity/functional-parity-classification.json"
    summary = [ordered]@{
        conditionalFormatPopupGalleryRows = $conditionalRows.Count
        conditionalFormatPopupCatalogItems = $runtimeCatalogItems
        captureTargets = $captureTargets.Count
        completeOpenedStateCaptureTargets = $completeOpenedStateTargets
        missingOrIncompleteOpenedStateCaptureTargets = $captureTargets.Count - $completeOpenedStateTargets
        openedStateStatus = $openedStateStatus
    }
    captureTargets = @($captureTargets)
    blockerCategories = @($blockerCategories)
    operatorChecklist = @($operatorChecklist)
    rows = @($rows)
}

$json = ($report | ConvertTo-Json -Depth 8) + [Environment]::NewLine

$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("# Conditional-format opened-state evidence")
[void]$md.AppendLine()
[void]$md.AppendLine("Generated by tools/Generate-ConditionalFormatOpenedStateEvidence.ps1. Do not edit by hand.")
[void]$md.AppendLine()
[void]$md.AppendLine("This report retains real opened-state capture evidence for the conditional-format popup/gallery lane. A target only counts as retained when its foreground manifest is complete and the referenced PNG resolves in the repo. Missing or blocked manifests stay visible as blockers; dialog-route fallback evidence is listed separately and is not counted as opened popup/gallery evidence.")
[void]$md.AppendLine()
[void]$md.AppendLine("Completion contract: run the target command in a foreground-capable Windows desktop session, commit the resulting manifest and PNG under ``tools/foreground-captures/<scenario>/``, then rerun this generator. A target is complete only when ``CaptureStatus`` is ``complete`` and ``ScreenshotPath`` resolves to a committed PNG; blocked manifests must remain blocked and must not use fallback dialog-route images as opened-state evidence.")
[void]$md.AppendLine()
[void]$md.AppendLine("Manifest validation contract: every committed manifest must match the expected Scenario and Subject, carry a known CaptureStatus, and include enough diagnostics for its state. Complete manifests must resolve a PNG; blocked manifests must retain BlockReason and CapturedAtUtc, every manifest must include EnvironmentSnapshot diagnostics, and foreground guard failures must include ForegroundGuard diagnostics.")
[void]$md.AppendLine()
[void]$md.AppendLine("## Summary")
[void]$md.AppendLine()
[void]$md.AppendLine("| Metric | Count |")
[void]$md.AppendLine("|---|---:|")
[void]$md.AppendLine("| Conditional-format popup/gallery classifier rows | $($conditionalRows.Count) |")
[void]$md.AppendLine("| Shared runtime popup catalog items | $runtimeCatalogItems |")
[void]$md.AppendLine("| Opened-state capture targets | $($captureTargets.Count) |")
[void]$md.AppendLine("| Complete opened-state capture targets | $completeOpenedStateTargets |")
[void]$md.AppendLine("| Missing or incomplete opened-state capture targets | $($captureTargets.Count - $completeOpenedStateTargets) |")
[void]$md.AppendLine()
[void]$md.AppendLine("## Blocker Categories")
[void]$md.AppendLine()
[void]$md.AppendLine("| Category | Count |")
[void]$md.AppendLine("|---|---:|")
foreach ($category in $blockerCategories) {
    [void]$md.AppendLine("| $(ConvertTo-ToolMarkdownCell $category.category) | $($category.count) |")
}
[void]$md.AppendLine()
[void]$md.AppendLine("## Capture Targets")
[void]$md.AppendLine()
[void]$md.AppendLine("| Target | Subject | Scenario | Status | Category | Manifest validation | Environment snapshot | Last attempt UTC | PNG | Blocker | Next action |")
[void]$md.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|")
foreach ($target in $captureTargets) {
    $png = if ($target.screenshotExists) { $target.screenshotPath } else { "" }
    $validation = $target.manifestValidationStatus
    if ($target.manifestValidationErrors.Count -gt 0) {
        $validation = "$validation ($($target.manifestValidationErrors -join ', '))"
    }

    [void]$md.AppendLine("| $(ConvertTo-ToolMarkdownCell $target.id) | $(ConvertTo-ToolMarkdownCell $target.subject) | $(ConvertTo-ToolMarkdownCell $target.scenario) | $(ConvertTo-ToolMarkdownCell $target.retentionStatus) | $(ConvertTo-ToolMarkdownCell $target.blockerCategory) | $(ConvertTo-ToolMarkdownCell $validation) | $(ConvertTo-ToolMarkdownCell $target.environmentSummary) | $(ConvertTo-ToolMarkdownCell $target.lastAttemptedAtUtc) | $(ConvertTo-ToolMarkdownCell $png) | $(ConvertTo-ToolMarkdownCell $target.blockReason) | $(ConvertTo-ToolMarkdownCell $target.nextCaptureAction) |")
}
[void]$md.AppendLine()
[void]$md.AppendLine("## Capture Commands")
[void]$md.AppendLine()
[void]$md.AppendLine("| Target | Command | Required environment |")
[void]$md.AppendLine("|---|---|---|")
foreach ($target in $captureTargets) {
    $command = ConvertTo-ToolMarkdownCell $target.runnerCommand
    [void]$md.AppendLine("| $(ConvertTo-ToolMarkdownCell $target.id) | ``$command`` | $(ConvertTo-ToolMarkdownCell $target.requiredEnvironment) |")
}
[void]$md.AppendLine()
[void]$md.AppendLine("## Operator Checklist")
[void]$md.AppendLine()
[void]$md.AppendLine("| Phase | Command | Purpose |")
[void]$md.AppendLine("|---|---|---|")
foreach ($item in $operatorChecklist) {
    $command = ConvertTo-ToolMarkdownCell $item.command
    [void]$md.AppendLine("| $(ConvertTo-ToolMarkdownCell $item.phase) | ``$command`` | $(ConvertTo-ToolMarkdownCell $item.purpose) |")
}
[void]$md.AppendLine()
[void]$md.AppendLine("## Classifier Rows")
[void]$md.AppendLine()
[void]$md.AppendLine("All rows below remain runtime-catalog backed and await the paired Excel/WPF/Avalonia opened-state capture set before the lane can claim opened-state evidence.")
[void]$md.AppendLine()
[void]$md.AppendLine("| Command | Status |")
[void]$md.AppendLine("|---|---|")
foreach ($row in $rows) {
    [void]$md.AppendLine("| $(ConvertTo-ToolMarkdownCell $row.id) | $(ConvertTo-ToolMarkdownCell $row.openedStateEvidenceStatus) |")
}

$markdown = $md.ToString()

$resolvedJsonPath = Resolve-ToolRepoPath -Path $JsonPath -RepoRoot $repoRoot
$resolvedMarkdownPath = Resolve-ToolRepoPath -Path $MarkdownPath -RepoRoot $repoRoot

if ($Check) {
    Test-ToolGeneratedContentMatches -ExpectedContent $json -ActualPath $resolvedJsonPath -Label "Conditional-format opened-state evidence JSON" -GeneratorScriptName "tools\Generate-ConditionalFormatOpenedStateEvidence.ps1" -NormalizeNewlines
    Test-ToolGeneratedContentMatches -ExpectedContent $markdown -ActualPath $resolvedMarkdownPath -Label "Conditional-format opened-state evidence Markdown" -GeneratorScriptName "tools\Generate-ConditionalFormatOpenedStateEvidence.ps1" -NormalizeNewlines
    Write-Host "Conditional-format opened-state evidence is up to date."
    return
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedJsonPath) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $resolvedMarkdownPath) | Out-Null
Set-Content -LiteralPath $resolvedJsonPath -Value $json -Encoding utf8 -NoNewline
Set-Content -LiteralPath $resolvedMarkdownPath -Value $markdown -Encoding utf8 -NoNewline

Write-Host "Conditional-format popup/gallery rows: $($conditionalRows.Count)"
Write-Host "Complete opened-state capture targets: $completeOpenedStateTargets/$($captureTargets.Count)"
Write-Host "Wrote $(ConvertTo-ToolRepoRelativePath -Path $resolvedJsonPath -RepoRoot $repoRoot)"
Write-Host "Wrote $(ConvertTo-ToolRepoRelativePath -Path $resolvedMarkdownPath -RepoRoot $repoRoot)"
