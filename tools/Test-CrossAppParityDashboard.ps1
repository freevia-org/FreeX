param(
    [string]$DashboardPath = "docs\parity\avalonia-wpf-cross-app-dashboard.json",
    [switch]$BoundarySelfTest
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

$acceptanceRefreshTestedSourceCommit = "615b53f474dfa1849ae965018d890cba4a138d42"
$acceptanceRefreshNote = "This dashboard/report is an acceptance-only documentation/tooling refresh; it does not alter the tested source commit."
$acceptanceRefreshAllowedPaths = @(
    "docs/parity/avalonia-parity-wave193-integration-20260823.md",
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

if ($BoundarySelfTest) {
    Invoke-AcceptanceBoundaryMutationSelfTest
    return
}

$resolvedDashboardPath = Resolve-ToolRepoPath -Path $DashboardPath -RepoRoot $repoRoot
$changedAcceptancePaths = @(Test-AcceptanceRefreshGitBoundary -RepositoryRoot $repoRoot -TestedSourceCommit $acceptanceRefreshTestedSourceCommit -AllowedPaths $acceptanceRefreshAllowedPaths)
$dashboard = Read-ToolJson -Path $DashboardPath -RepoRoot $repoRoot -MissingMessage "Required generated cross-app dashboard is missing"

Assert-DashboardCondition ($dashboard.schema -eq "freex.parity.cross-app-dashboard.v3") "Cross-app dashboard schema must be v3."
Assert-DashboardCondition ($dashboard.wave -eq 193) "Cross-app dashboard must describe Wave193."
Assert-DashboardCondition ($dashboard.cumulativeAppSlices -eq 579) "Wave193 cumulative app-slice count must be 579."
Assert-DashboardCondition ([string]$dashboard.cumulativeAppSlicesStatus -eq "accepted-final-integration-gates") "Wave193 app-slice count must be accepted after final integration gates."
Assert-DashboardCondition ([string]$dashboard.integrationGateStatus -eq "accepted") "Wave193 integration gates must be accepted."
Assert-DashboardCondition (@($dashboard.pendingIntegrationGates).Count -eq 0) "Wave193 must not retain pending integration gates."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.testedSourceCommit -eq $acceptanceRefreshTestedSourceCommit) "Wave193 integration evidence must name tested source commit $acceptanceRefreshTestedSourceCommit."
Assert-DashboardCondition ($null -eq $dashboard.integrationGateEvidence.PSObject.Properties["integrationHead"]) "Wave193 integration evidence must not use a recursive current-HEAD claim."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.acceptanceRefreshNote -eq $acceptanceRefreshNote) "Wave193 acceptance evidence must state that the acceptance-only documentation/tooling refresh does not alter tested source."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.independentReview -eq "Passed: independent review found no findings after dashboard and source-guard remediations.") "Wave193 independent-review evidence must be exact."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.repositoryPreflight -eq "Passed at tested source commit ${acceptanceRefreshTestedSourceCommit}: 288 JSON, 306 XML-backed, and 13,845 text files conflict scanned.") "Wave193 repository-preflight evidence must name the tested source and exact authoritative file counts."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.fullReleaseBuild -eq "Passed at tested source commit ${acceptanceRefreshTestedSourceCommit}: dotnet build FreeX.slnx --configuration Release --disable-build-servers -p:UseSharedCompilation=false -p:NodeReuse=false /nr:false -m:1 passed with 0 warnings and 0 errors.") "Wave193 Release-build evidence must retain the final tested-source command and zero-warning/error result."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.defaultNonUiTestLane -eq "Passed: final default lane exit 0 with Core.IO 5,839 passed/56 skipped, Avalonia 2,182 passed, Host Logic 1,490 passed/4 skipped, FreeP Presentation 5,466 passed, and FreeP Avalonia 724 passed.") "Wave193 default-lane evidence must retain authoritative project totals."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.sourceTestRemediation -eq "The initial default lane exposed three source-test regressions; remediation fixed all three, and focused reruns passed.") "Wave193 source-test remediation evidence must be recorded."
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
    "docs/parity/avalonia-parity-wave192-freew-font-checkbox-20260823.md",
    "docs/parity/avalonia-parity-wave193-freew-font-checkbox-glyph-20260823.md",
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
    "docs/parity/evidence/avalonia-parity-wave193-freep-evidence-20260823/images.json"
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
Assert-DashboardCondition ($freeP.renderedEvidence.responsiveAppChrome.capturedPairCount -eq 24) "FreeP must report its 24 paired responsive app-chrome states."
Assert-DashboardCondition ($freeP.renderedEvidence.responsiveAppChrome.wpfCaptureCount -eq 24) "FreeP must report its 24 WPF responsive app-chrome captures."
Assert-DashboardCondition ($freeP.renderedEvidence.responsiveAppChrome.avaloniaCaptureCount -eq 24) "FreeP must report its 24 Avalonia responsive app-chrome captures."
Assert-DashboardCondition ([string]$freeP.renderedEvidence.responsiveAppChrome.captureStatus -eq "complete") "FreeP responsive app-chrome evidence must be complete."
Assert-DashboardCondition ($freeW.renderedEvidence.wave193.aggregateChangedPixels -eq 32861) "FreeW Wave193 Font aggregate must remain 32,861 changed pixels."
Assert-DashboardCondition ($freeW.renderedEvidence.wave193.aggregateDelta -eq -1335) "FreeW Wave193 Font delta must remain -1,335 changed pixels."
Assert-DashboardCondition ($freeW.renderedEvidence.wave193.relativeImprovement -eq 0.0390396537606738) "FreeW Wave193 Font relative improvement must remain 3.9040%."
Assert-DashboardCondition ($freeW.renderedEvidence.wave193.nonFontRowsCompared -eq 288 -and $freeW.renderedEvidence.wave193.nonFontRowsChanged -eq 0) "FreeW Wave193 non-Font row stability must remain 0/288 changes."
Assert-DashboardCondition ([string]$freeW.renderedEvidence.wave193.paintedBounds -eq "421 x 321") "FreeW Wave193 painted bounds must remain exact."
Assert-DashboardCondition ($freeP.renderedEvidence.wave193Integrity.status -eq "no-runtime-change-retained") "FreeP Wave193 must retain the no-runtime-change result."
Assert-DashboardCondition ($freeP.renderedEvidence.wave193Integrity.retainedRowCount -eq 53) "FreeP Wave193 retained row count must remain 53."
Assert-DashboardCondition ($freeP.renderedEvidence.wave193Integrity.retainedOfficeReferenceCount -eq 53) "FreeP Wave193 retained Office reference count must remain 53."
Assert-DashboardCondition ($freeP.renderedEvidence.wave193Integrity.retainedImageCount -eq 6) "FreeP Wave193 retained image count must remain 6."
Assert-DashboardCondition ($freeP.renderedEvidence.wave193Integrity.workerRunWpfAvaloniaRenderCount -eq 106 -and $freeP.renderedEvidence.wave193Integrity.workerRunComparisonCount -eq 159) "FreeP Wave193 worker-run full-render counts must remain explicit."
Assert-DashboardCondition ($freeP.renderedEvidence.wave193Integrity.fullRenderArtifactsRetained -eq $false) "FreeP Wave193 must distinguish worker-run renders from retained artifacts."
Assert-DashboardCondition ($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.avaloniaAverageMeanPercent -eq 0.9962) "FreeP Wave193 Avalonia/Office aggregate must remain explicit."
Assert-DashboardCondition ($freeP.renderedEvidence.authoritativeMicrosoftOfficeBaseline.avaloniaMaximumMeanPercent -eq 2.5815) "FreeP Wave193 Avalonia/Office maximum must remain explicit."

Write-Host "Cross-app parity dashboard schema and evidence aggregation guards passed."
