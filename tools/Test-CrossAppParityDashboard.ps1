param(
    [string]$DashboardPath = "docs\parity\avalonia-wpf-cross-app-dashboard.json",
    [switch]$BoundarySelfTest
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

$acceptanceRefreshTestedSourceCommit = "e4f40ebcaadc624421b9c0a985330100f10af8df"
$acceptanceRefreshNote = "This dashboard/report is an acceptance-only documentation/tooling refresh; it does not alter the tested source commit."
$acceptanceRefreshAllowedPaths = @(
    "docs/parity/avalonia-parity-wave194-integration-20260824.md",
    "docs/parity/avalonia-wpf-cross-app-dashboard.json",
    "docs/parity/avalonia-wpf-cross-app-dashboard.md",
    "tests/FreeX.App.Host.Tests/CrossAppParityDashboardTests.cs",
    "tools/Generate-CrossAppParityDashboard.ps1",
    "tools/Test-CrossAppParityDashboard.ps1"
)

function Normalize-GitPath {
    param([AllowNull()][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return ""
    }

    return $Path.Trim().Replace('\', '/').TrimStart('/')
}

function Test-AcceptanceRefreshGitBoundary {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$TestedSourceCommit,
        [Parameter(Mandatory = $true)][string[]]$AllowedPaths,
        [string]$HeadRef = "HEAD"
    )

    $commitVerification = @(& git -C $RepositoryRoot rev-parse --verify "$TestedSourceCommit^{commit}" 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Acceptance boundary tested source commit '$TestedSourceCommit' does not exist in repository '$RepositoryRoot': $($commitVerification -join ' ')"
    }

    $null = @(& git -C $RepositoryRoot merge-base --is-ancestor $TestedSourceCommit $HeadRef 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Acceptance boundary tested source commit '$TestedSourceCommit' is not an ancestor of '$HeadRef'."
    }

    $changedPathOutput = @(& git -C $RepositoryRoot diff --name-only --no-renames $TestedSourceCommit $HeadRef 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Acceptance boundary could not obtain changed paths for '$TestedSourceCommit..$HeadRef': $($changedPathOutput -join ' ')"
    }

    $normalizedAllowedPaths = @($AllowedPaths | ForEach-Object { Normalize-GitPath $_ } | Sort-Object -Unique)
    $changedPaths = @($changedPathOutput | ForEach-Object { Normalize-GitPath $_ } | Where-Object { $_ })
    $unexpectedPaths = @($changedPaths | Where-Object { $normalizedAllowedPaths -notcontains $_ } | Sort-Object -Unique)
    if ($unexpectedPaths.Count -gt 0) {
        throw "Acceptance boundary changed paths outside the exact allowlist: $($unexpectedPaths -join ', '). Allowed paths: $($normalizedAllowedPaths -join ', ')."
    }

    return $changedPaths
}

function Invoke-AcceptanceBoundaryGit {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )

    $output = @(& git -C $RepositoryRoot @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "Git command failed in acceptance-boundary fixture: git $($Arguments -join ' ')`n$($output -join "`n")"
    }

    return $output
}

function Invoke-AcceptanceBoundaryMutationSelfTest {
    $fixture = New-ToolTemporaryDirectory -Prefix "freex-cross-app-boundary-"
    try {
        $fixturePath = $fixture
        $null = Invoke-AcceptanceBoundaryGit -RepositoryRoot $fixturePath -Arguments @("init", "-q")
        $null = Invoke-AcceptanceBoundaryGit -RepositoryRoot $fixturePath -Arguments @("config", "user.email", "acceptance-boundary@example.invalid")
        $null = Invoke-AcceptanceBoundaryGit -RepositoryRoot $fixturePath -Arguments @("config", "user.name", "Acceptance Boundary")

        Set-Content -LiteralPath (Join-Path $fixturePath "seed.txt") -Value "seed" -NoNewline
        $null = Invoke-AcceptanceBoundaryGit -RepositoryRoot $fixturePath -Arguments @("add", "seed.txt")
        $null = Invoke-AcceptanceBoundaryGit -RepositoryRoot $fixturePath -Arguments @("commit", "-q", "-m", "tested source")
        $testedCommit = ([string](Invoke-AcceptanceBoundaryGit -RepositoryRoot $fixturePath -Arguments @("rev-parse", "HEAD"))).Trim()

        foreach ($path in $acceptanceRefreshAllowedPaths) {
            $absolutePath = Join-Path $fixturePath ($path.Replace('/', '\'))
            $parent = Split-Path -Parent $absolutePath
            if (-not (Test-Path -LiteralPath $parent)) {
                New-Item -ItemType Directory -Path $parent -Force | Out-Null
            }
            Set-Content -LiteralPath $absolutePath -Value "acceptance" -NoNewline
        }
        $null = Invoke-AcceptanceBoundaryGit -RepositoryRoot $fixturePath -Arguments @("add", ".")
        $null = Invoke-AcceptanceBoundaryGit -RepositoryRoot $fixturePath -Arguments @("commit", "-q", "-m", "acceptance refresh")

        $null = Test-AcceptanceRefreshGitBoundary -RepositoryRoot $fixturePath -TestedSourceCommit $testedCommit -AllowedPaths $acceptanceRefreshAllowedPaths

        Set-Content -LiteralPath (Join-Path $fixturePath "unexpected.txt") -Value "unexpected" -NoNewline
        $null = Invoke-AcceptanceBoundaryGit -RepositoryRoot $fixturePath -Arguments @("add", "unexpected.txt")
        $null = Invoke-AcceptanceBoundaryGit -RepositoryRoot $fixturePath -Arguments @("commit", "-q", "-m", "unexpected path")
        $unexpectedRejected = $false
        try {
            $null = Test-AcceptanceRefreshGitBoundary -RepositoryRoot $fixturePath -TestedSourceCommit $testedCommit -AllowedPaths $acceptanceRefreshAllowedPaths
            throw "Acceptance boundary self-test did not reject an unexpected path."
        }
        catch {
            if ($_.Exception.Message -match "unexpected\.txt") {
                $unexpectedRejected = $true
            }
            else {
                throw
            }
        }
        if (-not $unexpectedRejected) {
            throw "Acceptance boundary self-test did not verify unexpected-path rejection."
        }

        $otherPath = Join-Path $fixturePath "other-root.txt"
        Set-Content -LiteralPath $otherPath -Value "other root" -NoNewline
        $otherBlob = ([string](Invoke-AcceptanceBoundaryGit -RepositoryRoot $fixturePath -Arguments @("hash-object", "-w", "--", $otherPath))).Trim()
        $null = Invoke-AcceptanceBoundaryGit -RepositoryRoot $fixturePath -Arguments @("update-index", "--add", "--cacheinfo", "100644,$otherBlob,other-root.txt")
        $otherTree = ([string](Invoke-AcceptanceBoundaryGit -RepositoryRoot $fixturePath -Arguments @("write-tree"))).Trim()
        $otherCommit = ([string](Invoke-AcceptanceBoundaryGit -RepositoryRoot $fixturePath -Arguments @("commit-tree", $otherTree, "-m", "unrelated root"))).Trim()
        $ancestryRejected = $false
        try {
            $null = Test-AcceptanceRefreshGitBoundary -RepositoryRoot $fixturePath -TestedSourceCommit $testedCommit -AllowedPaths $acceptanceRefreshAllowedPaths -HeadRef $otherCommit
            throw "Acceptance boundary self-test did not reject a non-ancestor tested source."
        }
        catch {
            if ($_.Exception.Message -match "not an ancestor") {
                $ancestryRejected = $true
            }
            else {
                throw
            }
        }
        if (-not $ancestryRejected) {
            throw "Acceptance boundary self-test did not verify ancestry rejection."
        }

        Write-Host "Acceptance boundary mutation coverage passed: unexpected-path and non-ancestor histories rejected."
    }
    finally {
        Remove-ToolTemporaryDirectory -Path $fixture
    }
}

function Assert-DashboardCondition {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Test-CrossAppDashboardGeneratorHosts {
    $generatorPath = Join-Path $PSScriptRoot "Generate-CrossAppParityDashboard.ps1"
    $hostCommands = @(
        [pscustomobject]@{
            Label = "pwsh"
            Path = (Get-Command pwsh.exe -ErrorAction Stop).Source
        }
    )

    if ($env:OS -eq "Windows_NT") {
        $hostCommands += [pscustomobject]@{
            Label = "powershell.exe"
            Path = (Get-Command powershell.exe -ErrorAction Stop).Source
        }
    }

    foreach ($hostCommand in $hostCommands) {
        $output = @(& $hostCommand.Path -NoProfile -ExecutionPolicy Bypass -File $generatorPath -Check 2>&1)
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            throw "Cross-app dashboard generator -Check failed under $($hostCommand.Label): $($output -join "`n")"
        }

        Write-Host "Cross-app dashboard generator -Check passed under $($hostCommand.Label)."
    }
}

if ($BoundarySelfTest) {
    Invoke-AcceptanceBoundaryMutationSelfTest
    return
}

Test-CrossAppDashboardGeneratorHosts

$resolvedDashboardPath = Resolve-ToolRepoPath -Path $DashboardPath -RepoRoot $repoRoot
$dashboard = Read-ToolJson -Path $DashboardPath -RepoRoot $repoRoot -MissingMessage "Required generated cross-app dashboard is missing"

Assert-DashboardCondition ($dashboard.schema -eq "freex.parity.cross-app-dashboard.v3") "Cross-app dashboard schema must be v3."
Assert-DashboardCondition ($dashboard.wave -eq 194) "Cross-app dashboard must describe Wave194."
Assert-DashboardCondition ($dashboard.cumulativeAppSlices -eq 582) "Wave194 cumulative app-slice count must be 582."
Assert-DashboardCondition ([string]$dashboard.cumulativeAppSlicesStatus -eq "accepted-final-integration-gates") "Wave194 app-slice count must be accepted after final integration gates pass."
Assert-DashboardCondition ([string]$dashboard.integrationGateStatus -eq "accepted") "Wave194 integration gates must be accepted after final results are recorded."
Assert-DashboardCondition (@($dashboard.pendingIntegrationGates).Count -eq 0) "Wave194 must not retain pending integration gates."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.testedSourceCommit -eq $acceptanceRefreshTestedSourceCommit) "Wave194 integration evidence must name tested source commit $acceptanceRefreshTestedSourceCommit."
Assert-DashboardCondition ($null -eq $dashboard.integrationGateEvidence.PSObject.Properties["integrationHead"]) "Wave194 integration evidence must not use a recursive current-HEAD claim."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.acceptanceRefreshNote -eq $acceptanceRefreshNote) "Wave194 acceptance evidence must state that the acceptance-only documentation/tooling refresh does not alter tested source."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.reintegration -eq "The current integration branch is anchored to tested source commit ${acceptanceRefreshTestedSourceCommit}; the acceptance refresh records only evidence from that tested source and does not claim that the documentation commit itself was rebuilt.") "Wave194 reintegration evidence must name the current tested-source boundary."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.focusedTests -eq "At tested source commit ${acceptanceRefreshTestedSourceCommit}: FreeX Avalonia Wave194 9/9; FreeX Presentation Wave194 1/1; FreeX Core.IO Wave194 plus five foreground-capture guards 8/8; FreeW Avalonia 2,175/2,175; FreeW host 1,835/1,835; FreeW Presentation 2,892/2,892; FreeW Ribbon definitions 62/62; FreeP Avalonia 724/724; FreeP host 2,418/2,418; FreeP Presentation 5,496/5,496; FreeP Ribbon definitions 34/34; FreeP responsive evidence 64/64; FreeP localization focused 1/1; FreeP resources 14/14; FreeP Hide Slide assertions 2/2; FreeP ChartRenderPlanner 264/264.") "Wave194 focused-test evidence must record each supplied current-source lane."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.initialReintegrationPreflight -eq "The current acceptance refresh uses the supplied repository-preflight result and the exact tested-source boundary; no additional source paths are allowlisted by this documentation-only change.") "Wave194 acceptance evidence must record the supplied preflight and exact boundary."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.initialIndependentReview -match "two P2 findings.*FreeX.*FreeP") "Wave194 initial independent-review findings must be recorded."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.reviewRemediation -match "one authoritative mixed-type geometry contract.*schema v3.*color-geometry guard") "Wave194 reviewer remediations must be recorded."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.independentReviewStatus -eq "remediation-awaiting-recheck") "Wave194 independent acceptance review must remain remediation-awaiting-recheck."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.independentReview -eq "Remediation-awaiting-recheck: a fresh independent final acceptance review of tested source commit ${acceptanceRefreshTestedSourceCommit} must be completed. The supplied current FreeP Surface3D static sign-off is scoped to that focused lane and does not satisfy the cross-app acceptance review.") "Wave194 independent acceptance review must remain remediation-awaiting-recheck until the fresh cross-app review completes."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.repositoryPreflight -eq "Passed at tested source commit ${acceptanceRefreshTestedSourceCommit}: powershell.exe -NoProfile -ExecutionPolicy Bypass -File tools\Test-RepositoryPreflight.ps1 exited 0; 294 JSON, 309 XML-backed, 125 PowerShell scripts, 11 GitHub workflows, 10 test gates/48 assigned projects, 13,951 conflict-marker files checked, and all generated docs/evidence current; elapsed 00:03:10.419.") "Wave194 repository-preflight evidence must be exact."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.fullReleaseBuildMsBuildElapsed -eq "00:08:44.31") "Wave194 Release-build MSBuild elapsed evidence must be exact."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.fullReleaseBuildWrapperElapsed -eq "00:08:44.581") "Wave194 Release-build wrapper elapsed evidence must be exact."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.fullReleaseBuild -eq "Passed at tested source commit ${acceptanceRefreshTestedSourceCommit}: dotnet build FreeX.slnx --configuration Release -m:1 passed with 0 warnings and 0 errors; MSBuild-retained Time Elapsed 00:08:44.31; wrapper stopwatch 00:08:44.581.") "Wave194 Release-build evidence must distinguish MSBuild and wrapper elapsed times."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.defaultNonUiTestLaneWrapperElapsed -eq "00:17:18.449") "Wave194 default-lane wrapper elapsed evidence must be exact."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.defaultNonUiTestLaneTrxTimestampSpan -eq "00:17:17.5738434") "Wave194 default-lane TRX timestamp span must be exact."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.defaultNonUiTestLane -eq "Passed at tested source commit ${acceptanceRefreshTestedSourceCommit}: final default non-UI lane produced 31 unique TRXs and matching console aggregation: 43,505 passed, 134 intentional skips, 0 failed, 43,639 total; wrapper stopwatch 00:17:18.449; independently parsed 31-TRX timestamp span 00:17:17.5738434.") "Wave194 default-lane evidence must distinguish wrapper and TRX elapsed times."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.initialDefaultLane -match "43,505 passed, 134 intentional skips, 0 failed, 43,639 total") "Wave194 current default-lane result must be recorded exactly."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.sliceAccounting -eq "582 cumulative app slices (194 per app) remain the processed Wave194 accounting; later wave feature commits are included in the tested source and do not add Wave194 slices.") "Wave194 slice accounting must remain explicit and non-inflated."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.sourceTestRemediation -match "generated inventory and visual manifests remain the authority") "Wave194 current evidence source boundary must be recorded."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.workerVerification -match "FreeW and FreeP.*Functional/source evidence and visual comparison evidence") "Wave194 focused evidence must distinguish source and visual claims."
Assert-DashboardCondition ($dashboard.scopeBoundary -match "visual parity") "Cross-app dashboard scope boundary must retain the no-visual-parity claim."

$requiredSources = @(
    "docs/parity/freew-dialog-harness/freew_dialog_route_inventory.json",
    "docs/parity/freew-dialog-harness/freew_dialog_visual_comparison.json",
    "docs/parity/freew-dialog-harness/freew_font_visual_provenance.json",
    "docs/parity/freew-word-baseline-2026-08-16/manifest.json",
    "docs/parity/freew-shell-visual-2026-08-16/freew_shell_visual_evidence.json",
    "docs/parity/freew-word-chrome-2026-08-16/manifest.json",
    "docs/parity/freex-excel-chrome-comparison.md",
    "docs/parity/freex-excel-com-baseline-2026-08-14/manifest.json",
    "docs/parity/freex-avalonia-grid-corpus-2026-08-16/manifest.json",
    "tools/screenshots/screenshot_manifest.json",
    "tools/screenshots_avalonia_ribbon/screenshot_manifest.json",
    "docs/parity/avalonia-parity-wave192-freex-autofilter-font-color-20260823.md",
    "docs/parity/evidence/wave192-freex-autofilter-font-color-20260823/manifest.json",
    "docs/parity/avalonia-parity-wave193-freex-autofilter-no-fill-20260823.md",
    "docs/parity/evidence/wave193-freex-autofilter-no-fill-20260823/physical-result.json",
    "docs/parity/evidence/wave193-freex-autofilter-no-fill-20260823/manifest.json",
    "docs/parity/avalonia-parity-wave194-freex-autofilter-mixed-type-20260823.md",
    "docs/parity/evidence/wave194-freex-autofilter-mixed-type-20260823/physical-result.json",
    "docs/parity/evidence/wave194-freex-autofilter-mixed-type-20260823/manifest.json",
    "docs/parity/avalonia-parity-wave192-freew-font-checkbox-20260823.md",
    "docs/parity/avalonia-parity-wave193-freew-font-checkbox-glyph-20260823.md",
    "docs/parity/avalonia-parity-wave194-freew-font-action-border-20260824.md",
    "docs/parity/freep-dialog-pane-visual-evidence/summary.json",
    "docs/parity/freep-dialog-pane-visual-evidence/artifact-manifest.json",
    "docs/parity/freep-whole-window-visual-evidence/summary.json",
    "docs/parity/freep-whole-window-visual-evidence/artifact-manifest.json",
    "docs/parity/freep-render-slideshow-media-parity-20260720.json",
    "docs/parity/freep-powerpoint-baseline-2026-08-14.json",
    "docs/parity/freep-powerpoint-recalibration-2026-08-15.json",
    "docs/parity/freep-powerpoint-chrome-2026-08-16/README.md",
    "docs/parity/freep-powerpoint-chrome-2026-08-16/manifest.json",
    "docs/parity/freep-responsive-chrome-2026-08-16/README.md",
    "docs/parity/freep-responsive-chrome-2026-08-16/manifest.json",
    "docs/parity/avalonia-parity-wave192-freep-render-residual-20260823.md",
    "docs/parity/evidence/avalonia-parity-wave192-freep-evidence-20260823/metrics.json",
    "docs/parity/avalonia-parity-wave193-freep-render-residual-20260823.md",
    "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/metrics.json",
    "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/references.json",
    "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/images.json",
    "docs/parity/freep-wave194-deck17-slide02-topology-20260823.md",
    "docs/parity/evidence/freep-wave194-deck17-slide02-topology-20260823/topology.json"
)
foreach ($source in $requiredSources) {
    Assert-DashboardCondition (@($dashboard.sources) -contains $source) "Cross-app dashboard is missing authoritative source '$source'."
}

$apps = @{}
foreach ($app in @($dashboard.apps)) {
    $apps[[string]$app.app] = $app
    foreach ($property in @("routeCoverage", "artifactCoverage", "pairedEvidence", "physicalEvidence", "authoritativeMicrosoftOfficeBaseline", "claimBoundary")) {
        Assert-DashboardCondition ($null -ne $app.renderedEvidence.PSObject.Properties[$property]) "$($app.app) rendered evidence is missing '$property'."
    }

    Assert-DashboardCondition ([bool]$app.renderedEvidence.authoritativeMicrosoftOfficeBaseline.PSObject.Properties["available"]) "$($app.app) baseline availability must be explicit."
    Assert-DashboardCondition ($app.renderedEvidence.claimBoundary -match "not|only") "$($app.app) rendered evidence must retain a coverage-only claim boundary."
}

Assert-DashboardCondition ($apps.ContainsKey("FreeX") -and $apps.ContainsKey("FreeW") -and $apps.ContainsKey("FreeP")) "Cross-app dashboard must contain FreeX, FreeW, and FreeP."

$freeX = $apps["FreeX"]
Assert-DashboardCondition ($freeX.renderedEvidence.routeCoverage.inventoryRouteCount -eq $freeX.dialogRoutes.totalRoutes) "FreeX route coverage must come from the dialog inventory."
Assert-DashboardCondition ($freeX.renderedEvidence.artifactCoverage.pairedManifestSurfaceCount -le $freeX.renderedEvidence.artifactCoverage.wpfManifestSurfaceCount) "FreeX paired manifest surfaces cannot exceed WPF manifest surfaces."
Assert-DashboardCondition ($freeX.renderedEvidence.artifactCoverage.pairedManifestSurfaceCount -le $freeX.renderedEvidence.artifactCoverage.avaloniaManifestSurfaceCount) "FreeX paired manifest surfaces cannot exceed Avalonia manifest surfaces."

$freeW = $apps["FreeW"]
$freeWArtifacts = $freeW.renderedEvidence.artifactCoverage
$freeWPaired = $freeW.renderedEvidence.pairedEvidence
Assert-DashboardCondition ([string]$freeW.renderedEvidence.canonicalComparison.kind -eq "canonical-inputs-only") "FreeW dashboard must expose the canonical comparison scope."
Assert-DashboardCondition ([string]$freeW.renderedEvidence.canonicalComparison.refreshInstruction -match "baseline.*refresh-route") "FreeW dashboard must expose the route refresh instruction."
Assert-DashboardCondition (($freeWArtifacts.pairedComparisonRowCount + $freeWArtifacts.avaloniaOnlyArtifactRowCount + $freeWArtifacts.stateNotApplicableRowCount + $freeWArtifacts.otherComparisonRowCount) -eq $freeWArtifacts.evidenceRowCount) "FreeW comparison rows must partition into paired, Avalonia-only, not-applicable, and non-paired classifications."
Assert-DashboardCondition ($freeWPaired.pairedScenarioCount -eq $freeWArtifacts.pairedComparisonRowCount) "FreeW paired evidence must use the paired comparison-row count."
Assert-DashboardCondition ($freeWPaired.mismatchCount -gt 0 -or $freeWPaired.passCount -gt 0) "FreeW paired evidence must retain comparison classifications."
Assert-DashboardCondition ($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.available -eq $true) "FreeW must report its committed Word reference baseline as available."
Assert-DashboardCondition ($freeW.renderedEvidence.authoritativeMicrosoftOfficeBaseline.artifactCount -eq 65) "FreeW Word baseline artifact count must remain explicit."
Assert-DashboardCondition ($freeW.renderedEvidence.shellChrome.pairedStaticCaptureCount -eq 40) "FreeW must report its 40 paired static shell captures."
Assert-DashboardCondition ($freeW.renderedEvidence.shellChrome.pairedContextualCaptureCount -eq 32) "FreeW must report its 32 paired contextual shell captures."
Assert-DashboardCondition ($freeW.renderedEvidence.shellChrome.avaloniaContextualMissingCount -eq 0) "FreeW must not retain contextual shell gaps after the paired capture."
Assert-DashboardCondition ($freeW.renderedEvidence.shellChrome.wordOfficeChromeReferenceCount -eq 36) "FreeW must report its 36 native Word chrome references."
Assert-DashboardCondition ([string]$freeW.renderedEvidence.shellChrome.wordOfficeChromeStatus -eq "complete") "FreeW Word chrome evidence must be complete."

Assert-DashboardCondition ($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.available -eq $true) "FreeX must report its committed Excel reference baseline as available."
Assert-DashboardCondition ($freeX.renderedEvidence.authoritativeMicrosoftOfficeBaseline.artifactCount -eq 45) "FreeX Excel baseline artifact count must remain explicit."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.status -eq "available-interactive-foreground") "FreeX must report its interactive foreground Excel evidence."
Assert-DashboardCondition ((@($freeX.renderedEvidence.physicalEvidence.limitations) -join " ") -match "36 Excel ribbon states") "FreeX dashboard must report the complete interactive ribbon capture."
Assert-DashboardCondition ((@($freeX.renderedEvidence.physicalEvidence.limitations) -join " ") -notmatch "unavailable during the 2026-08-16 refresh") "FreeX dashboard must not retain the resolved foreground-capture blocker."
Assert-DashboardCondition ($freeX.renderedEvidence.chromeCapture.wpfCaptureCount -eq 36) "FreeX must report its complete WPF chrome matrix."
Assert-DashboardCondition ($freeX.renderedEvidence.chromeCapture.avaloniaCaptureCount -eq 36) "FreeX must report its complete Avalonia chrome matrix."
Assert-DashboardCondition ($freeX.renderedEvidence.chromeCapture.fixedViewportComparisonCount -eq 27) "FreeX must report its fixed-viewport chrome comparison count."
Assert-DashboardCondition ([string]$freeX.renderedEvidence.gridCorpus.captureStatus -eq "complete") "FreeX Avalonia grid corpus must be complete."
Assert-DashboardCondition ($freeX.renderedEvidence.gridCorpus.totalAvaloniaCaptureCount -eq 35) "FreeX must report all 35 Avalonia grid corpus captures."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterFontColorPassed -eq 1) "FreeX must report its passing production font-color AutoFilter lane."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterFontColorTotal -eq 1) "FreeX font-color AutoFilter total must remain explicit."
Assert-DashboardCondition ([string]$freeX.renderedEvidence.physicalEvidence.linuxAutoFilterFontColorStatus -eq "passed-production-x11") "FreeX font-color AutoFilter status must remain production-X11."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNoFillPassed -eq 1) "FreeX No Fill AutoFilter lane must pass 1/1."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNoFillTotal -eq 1) "FreeX No Fill AutoFilter total must remain explicit."
Assert-DashboardCondition ([string]$freeX.renderedEvidence.physicalEvidence.linuxAutoFilterNoFillStatus -eq "passed-production-x11") "FreeX No Fill AutoFilter status must remain production-X11."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave193FocusedAvaloniaGuardPassed -eq 3) "FreeX Wave193 Avalonia guard count must remain 3/3."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave193FocusedCoreIoGuardPassed -eq 8) "FreeX Wave193 Core.IO guard count must remain 8/8."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave193EvidenceArtifactCount -eq 18) "FreeX Wave193 evidence artifact count must remain 18/18."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave193EvidenceArtifactExpectedCount -eq 18) "FreeX Wave193 expected evidence artifact count must remain 18."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave193EvidenceProvenanceFileCount -eq 9) "FreeX Wave193 provenance count must remain 9/9."
Assert-DashboardCondition ($null -ne $freeX.renderedEvidence.physicalEvidence.wave193PopupTransitions) "FreeX Wave193 popup transition evidence must be present."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave193PopupTransitions.popupOpen.minimumChangedPixels -eq 300) "FreeX Wave193 popup-open transition threshold must remain explicit."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave193PopupTransitions.popupOpen.changedPixels -eq 1905 -and $freeX.renderedEvidence.physicalEvidence.wave193PopupTransitions.popupOpen.passed -eq $true) "FreeX Wave193 popup-open transition must retain 1,905 changed pixels and pass."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave193PopupTransitions.popupDismissed.minimumChangedPixels -eq 300) "FreeX Wave193 popup-dismissed transition threshold must remain explicit."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave193PopupTransitions.popupDismissed.changedPixels -eq 1905 -and $freeX.renderedEvidence.physicalEvidence.wave193PopupTransitions.popupDismissed.passed -eq $true) "FreeX Wave193 popup-dismissed transition must retain 1,905 changed pixels and pass."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave193PopupTransitions.restoration.maximumChangedPixels -eq 100) "FreeX Wave193 popup restoration maximum must remain explicit."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave193PopupTransitions.restoration.changedPixels -eq 0 -and $freeX.renderedEvidence.physicalEvidence.wave193PopupTransitions.restoration.passed -eq $true) "FreeX Wave193 popup restoration must retain 0 changed pixels and pass."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave193PopupTransitions.clickAcknowledged -eq $true) "FreeX Wave193 popup click acknowledgement must remain explicit."
Assert-DashboardCondition ([string]$freeX.renderedEvidence.physicalEvidence.wave193PopupTransitions.summary -match "popup-open 1905.*popup-dismissed 1905.*restoration 0") "FreeX Wave193 popup transition summary must retain open/dismiss/restoration details."
Assert-DashboardCondition ([string]$freeX.renderedEvidence.physicalEvidence.wave193PackageSemantics -match "SourcePatch.*no-row-delta") "FreeX Wave193 package semantics must retain SourcePatch/no-row-delta coverage."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave194.physicalPassed -eq 1 -and $freeX.renderedEvidence.physicalEvidence.wave194.physicalTotal -eq 1) "FreeX Wave194 mixed-type physical lane must pass 1/1."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave194.focusedAvaloniaGuardPassed -eq 9 -and $freeX.renderedEvidence.physicalEvidence.wave194.focusedAvaloniaGuardTotal -eq 9) "FreeX Wave194 Avalonia guards must be 9/9."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave194.focusedPresentationPassed -eq 1 -and $freeX.renderedEvidence.physicalEvidence.wave194.focusedPresentationTotal -eq 1) "FreeX Wave194 Presentation guards must be 1/1."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave194.focusedCoreIoPassed -eq 8 -and $freeX.renderedEvidence.physicalEvidence.wave194.focusedCoreIoTotal -eq 8) "FreeX Wave194 Core.IO plus foreground-capture guards must be 8/8."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave194.evidenceArtifactCount -eq 20 -and $freeX.renderedEvidence.physicalEvidence.wave194.evidenceArtifactExpectedCount -eq 20) "FreeX Wave194 evidence artifact count must be 20/20."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave194.reachableProvenanceFileCount -eq 10 -and $freeX.renderedEvidence.physicalEvidence.wave194.validationFileCount -eq 2) "FreeX Wave194 provenance and validation counts must be 10 and 2."
Assert-DashboardCondition ([string]$freeX.renderedEvidence.physicalEvidence.wave194.geometry.bounds -eq "97,589,260,18" -and [string]$freeX.renderedEvidence.physicalEvidence.wave194.geometry.click -eq "103,598") "FreeX Wave194 geometry must retain the accepted bounds and click."
Assert-DashboardCondition ([string]$freeX.renderedEvidence.physicalEvidence.wave194.visibleReadback -eq "42,'42,") "FreeX Wave194 visible/readback value must remain exact."
Assert-DashboardCondition ([string]$freeX.renderedEvidence.physicalEvidence.wave194.semanticReadback -eq "Number,NumericText") "FreeX Wave194 semantic readback must remain exact."
Assert-DashboardCondition ([string]$freeX.renderedEvidence.physicalEvidence.wave194.recalculation -eq "5->2") "FreeX Wave194 SUBTOTAL recalculation must remain 5->2."
Assert-DashboardCondition ([string]$freeX.renderedEvidence.physicalEvidence.wave194.package -eq "ref=A1:B7|colId=0|filters=42|blank=|hidden=4,5,6,7|A2-type=n|A2=42|A3-type=inlineStr|A3=42|A6-style=1|A6=45292|C1-formula=SUBTOTAL(103,A2:A7)|C1=2") "FreeX Wave194 package semantics must remain exact."
Assert-DashboardCondition ([bool]$freeX.renderedEvidence.physicalEvidence.wave194.evidenceUnchangedAfterGeometryRemediation) "FreeX Wave194 physical evidence must remain byte-equivalent after geometry remediation."
Assert-DashboardCondition ([string]$freeX.renderedEvidence.physicalEvidence.wave194.claimBoundary -match "Bounded physical") "FreeX Wave194 claim boundary must remain bounded."

$freeP = $apps["FreeP"]
$freePLanes = @($freeP.renderedEvidence.routeCoverage.laneEntries)
Assert-DashboardCondition ($freePLanes.Count -eq 2) "FreeP rendered evidence must retain dialog-pane and whole-window lanes."
Assert-DashboardCondition ($freeP.renderedEvidence.routeCoverage.pairedScenarioCount -eq ($freePLanes | Measure-Object -Property pairedScenarioCount -Sum).Sum) "FreeP paired scenario total must equal the lane sum."
Assert-DashboardCondition ($freeP.renderedEvidence.artifactCoverage.wpfPngCount -gt 0 -and $freeP.renderedEvidence.artifactCoverage.avaloniaPngCount -gt 0) "FreeP artifact coverage must retain both WPF and Avalonia PNG counts."
Assert-DashboardCondition ($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.available -eq $true) "FreeP must report its committed PowerPoint reference baseline as available."
Assert-DashboardCondition ($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.artifactCount -eq 53) "FreeP tracked PowerPoint baseline artifact count must remain explicit."
Assert-DashboardCondition ($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.referenceReadyDecks -eq 27) "FreeP tracked PowerPoint ready-deck count must remain explicit."
Assert-DashboardCondition ($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.missingReferenceDecks -eq 0) "FreeP missing PowerPoint reference deck count must remain explicit."
Assert-DashboardCondition ($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.wpfAverageMeanPercent -gt 0) "FreeP current-source WPF recalibration must remain explicit."
Assert-DashboardCondition ($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.avaloniaAverageMeanPercent -gt 0) "FreeP current-source Avalonia recalibration must remain explicit."
Assert-DashboardCondition ($freeP.renderedEvidence.nativeOfficeChrome.expectedCaptureCount -eq 28) "FreeP must report its 28-state native PowerPoint chrome capture contract."
Assert-DashboardCondition ($freeP.renderedEvidence.nativeOfficeChrome.capturedReferenceCount -eq 28) "FreeP must report its 28 captured native PowerPoint chrome references."
Assert-DashboardCondition ([string]$freeP.renderedEvidence.nativeOfficeChrome.captureStatus -eq "complete") "FreeP native PowerPoint chrome evidence must be complete."
Assert-DashboardCondition ($freeP.renderedEvidence.responsiveAppChrome.capturedPairCount -eq 32) "FreeP must report its 32 paired responsive app-chrome states."
Assert-DashboardCondition ($freeP.renderedEvidence.responsiveAppChrome.wpfCaptureCount -eq 32) "FreeP must report its 32 WPF responsive app-chrome captures."
Assert-DashboardCondition ($freeP.renderedEvidence.responsiveAppChrome.avaloniaCaptureCount -eq 32) "FreeP must report its 32 Avalonia responsive app-chrome captures."
Assert-DashboardCondition ([string]$freeP.renderedEvidence.responsiveAppChrome.captureStatus -eq "complete") "FreeP responsive app-chrome evidence must be complete."
Assert-DashboardCondition ($freeW.renderedEvidence.wave193.aggregateChangedPixels -eq 32861) "FreeW Wave193 Font aggregate must remain 32,861 changed pixels."
Assert-DashboardCondition ($freeW.renderedEvidence.wave193.aggregateDelta -eq -1335) "FreeW Wave193 Font delta must remain -1,335 changed pixels."
Assert-DashboardCondition ($freeW.renderedEvidence.wave193.relativeImprovement -eq 0.0390396537606738) "FreeW Wave193 Font relative improvement must remain 3.9040%."
Assert-DashboardCondition ($freeW.renderedEvidence.wave193.nonFontRowsCompared -eq 288 -and $freeW.renderedEvidence.wave193.nonFontRowsChanged -eq 0) "FreeW Wave193 non-Font row stability must remain 0/288 changes."
Assert-DashboardCondition ([string]$freeW.renderedEvidence.wave193.paintedBounds -eq "421 x 321") "FreeW Wave193 painted bounds must remain exact."
Assert-DashboardCondition ($freeW.renderedEvidence.wave194.baselineAggregateChangedPixels -eq 32861 -and $freeW.renderedEvidence.wave194.aggregateChangedPixels -eq 32312) "FreeW Wave194 aggregate changed pixels must be 32,861 -> 32,312."
Assert-DashboardCondition ($freeW.renderedEvidence.wave194.aggregateDelta -eq -549 -and [math]::Abs([double]$freeW.renderedEvidence.wave194.relativeImprovement - 0.016712006337408244) -lt 0.000000000000001) "FreeW Wave194 aggregate improvement must remain exact."
Assert-DashboardCondition ($freeW.renderedEvidence.wave194.changedPixelsByState.initial -eq 10599 -and $freeW.renderedEvidence.wave194.changedPixelsByState.populated -eq 10756 -and $freeW.renderedEvidence.wave194.changedPixelsByState.validationError -eq 10957) "FreeW Wave194 state changed-pixel counts must remain exact."
Assert-DashboardCondition ($freeW.renderedEvidence.wave194.nonFontRowsCompared -eq 288 -and $freeW.renderedEvidence.wave194.nonFontRowsChanged -eq 0 -and [string]$freeW.renderedEvidence.wave194.paintedBounds -eq "421 x 321") "FreeW Wave194 non-Font stability and bounds must remain exact."
Assert-DashboardCondition ([string]$freeW.renderedEvidence.wave194.correction -match "#C8C8C8") "FreeW Wave194 correction must record the WPF-style action border."
Assert-DashboardCondition ($freeP.renderedEvidence.wave193Integrity.status -eq "no-runtime-change-retained") "FreeP Wave193 must retain the no-runtime-change result."
Assert-DashboardCondition ($freeP.renderedEvidence.wave193Integrity.retainedRowCount -eq 53) "FreeP Wave193 retained row count must remain 53."
Assert-DashboardCondition ($freeP.renderedEvidence.wave193Integrity.retainedOfficeReferenceCount -eq 53) "FreeP Wave193 retained Office reference count must remain 53."
Assert-DashboardCondition ($freeP.renderedEvidence.wave193Integrity.retainedImageCount -eq 6) "FreeP Wave193 retained image count must remain 6."
Assert-DashboardCondition ($freeP.renderedEvidence.wave193Integrity.workerRunWpfAvaloniaRenderCount -eq 106 -and $freeP.renderedEvidence.wave193Integrity.workerRunComparisonCount -eq 159) "FreeP Wave193 worker-run full-render counts must remain explicit."
Assert-DashboardCondition ($freeP.renderedEvidence.wave193Integrity.fullRenderArtifactsRetained -eq $false) "FreeP Wave193 must distinguish worker-run renders from retained artifacts."
Assert-DashboardCondition ($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.avaloniaAverageMeanPercent -eq 0.9962) "FreeP Wave193 Avalonia/Office aggregate must remain explicit."
Assert-DashboardCondition ($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.avaloniaMaximumMeanPercent -eq 2.5815) "FreeP Wave193 Avalonia/Office maximum must remain explicit."
Assert-DashboardCondition ([string]$freeP.renderedEvidence.wave194Topology.schema -eq "freep.parity.wave194.deck17-slide02.topology.v3") "FreeP Wave194 topology schema must be v3."
Assert-DashboardCondition ([string]$freeP.renderedEvidence.wave194Topology.sourceCorpusSha256 -eq "f4fc0c9e3d048cac3e0c7fe3d929029238448ff05281be542df105a46c6c88ea") "FreeP Wave194 topology must pin the complete source PPTX SHA-256."
Assert-DashboardCondition ([string]$freeP.renderedEvidence.wave194Topology.sourceCorpusHashScope -eq "entire raw file bytes") "FreeP Wave194 topology hash scope must cover the entire raw source file."
Assert-DashboardCondition ([string]$freeP.renderedEvidence.wave194Topology.title.autoFitKind -eq "Shape" -and [string]$freeP.renderedEvidence.wave194Topology.title.effectiveFontFamily -eq "Aptos Display" -and $freeP.renderedEvidence.wave194Topology.title.effectiveFontSizePt -eq 28) "FreeP Wave194 title topology must remain exact."
Assert-DashboardCondition ([string]$freeP.renderedEvidence.wave194Topology.body.autoFitKind -eq "None" -and [string]$freeP.renderedEvidence.wave194Topology.body.effectiveFontFamily -eq "Aptos" -and $freeP.renderedEvidence.wave194Topology.body.effectiveFontSizePt -eq 18 -and $freeP.renderedEvidence.wave194Topology.body.paragraphCount -eq 8) "FreeP Wave194 body topology must remain exact."
Assert-DashboardCondition ([string]$freeP.renderedEvidence.wave194Topology.residualClaim -match "unresolved.*not attributed") "FreeP Wave194 residual claim must remain unresolved and non-attributed."

Write-Host "Cross-app parity dashboard schema and evidence aggregation guards passed."
