param(
    [string]$DashboardPath = "docs/parity/avalonia-wpf-cross-app-dashboard.json",
    [switch]$BoundarySelfTest,
    [switch]$AcceptanceRefresh,
    [string]$TestedSourceCommit,
    [string]$HeadRef = "HEAD"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

$acceptanceRefreshTestedSourceCommit = "f7cbd8cbe3f1ac5fbaf14da1c2cacc1a3fb7bf3f"
$acceptanceRefreshReviewedIntegrationHead = "2ee42a45efd651ad9ad1c015403d788570ae02d9"
$acceptanceRefreshNote = "This dashboard/report is an acceptance-only documentation/tooling refresh; it does not alter the tested source commit."
$acceptanceRefreshWave196TestedSourceCommit = "100f4aea399e3bc9d194c15cf962ded7d0cf3772"
$acceptanceRefreshWave197TestedSourceCommit = "a6b1f27e02d15a7495644db64c9bda3a839f126a"
$acceptanceRefreshAllowedPaths = @(
    "tools/Generate-CrossAppParityDashboard.ps1",
    "tools/Test-CrossAppParityDashboard.ps1",
    "tests/FreeX.App.Host.Tests/CrossAppParityDashboardTests.cs",
    "docs/parity/avalonia-wpf-cross-app-dashboard.json",
    "docs/parity/avalonia-wpf-cross-app-dashboard.md",
    "docs/parity/avalonia-parity-wave197-cross-app-integration-20260829.md"
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
        throw "Acceptance boundary could not obtain committed changed paths for '$TestedSourceCommit..$HeadRef': $($changedPathOutput -join ' ')"
    }

    # Refreshes are often generated before their documentation commit exists.
    # Include all working-tree forms so an uncommitted out-of-scope edit cannot
    # bypass the same allowlist enforced for committed refresh history.
    $workingTreePathOutput = @(
        @(& git -C $RepositoryRoot diff --name-only --no-renames 2>$null)
        @(& git -C $RepositoryRoot diff --cached --name-only --no-renames 2>$null)
        @(& git -C $RepositoryRoot ls-files --others --exclude-standard 2>$null)
    )
    if ($LASTEXITCODE -ne 0) {
        throw "Acceptance boundary could not obtain working-tree changed paths: $($workingTreePathOutput -join ' ')"
    }

    $normalizedAllowedPaths = @($AllowedPaths | ForEach-Object { Normalize-GitPath $_ } | Sort-Object -Unique)
    $changedPaths = @($changedPathOutput + $workingTreePathOutput | ForEach-Object { Normalize-GitPath $_ } | Where-Object { $_ } | Sort-Object -Unique)
    $unexpectedPaths = @($changedPaths | Where-Object { $normalizedAllowedPaths -notcontains $_ } | Sort-Object -Unique)
    if ($unexpectedPaths.Count -gt 0) {
        throw "Acceptance boundary changed paths outside the exact allowlist: $($unexpectedPaths -join ', '). Allowed paths: $($normalizedAllowedPaths -join ', ')."
    }

    return $changedPaths
}

function Invoke-RealAcceptanceRefreshBoundary {
    if ([string]::IsNullOrWhiteSpace($TestedSourceCommit)) {
        throw "-AcceptanceRefresh requires -TestedSourceCommit; the parent must supply the exact tested source head."
    }
    if ($TestedSourceCommit -ne $acceptanceRefreshWave197TestedSourceCommit) {
        throw "-AcceptanceRefresh requires the exact Wave197 tested source commit '$acceptanceRefreshWave197TestedSourceCommit'; received '$TestedSourceCommit'."
    }

    $changedPaths = @(Test-AcceptanceRefreshGitBoundary `
        -RepositoryRoot $repoRoot `
        -TestedSourceCommit $TestedSourceCommit `
        -AllowedPaths $acceptanceRefreshAllowedPaths `
        -HeadRef $HeadRef)

    Write-Host "Acceptance refresh real-repository boundary passed: tested source '$TestedSourceCommit', head '$HeadRef', $($changedPaths.Count) changed paths within the exact allowlist."
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
            $absolutePath = Join-Path $fixturePath $path
            $parent = Split-Path -Parent $absolutePath
            if (-not (Test-Path -LiteralPath $parent)) {
                New-Item -ItemType Directory -Path $parent -Force | Out-Null
            }
            Set-Content -LiteralPath $absolutePath -Value "acceptance" -NoNewline
        }
        $null = Invoke-AcceptanceBoundaryGit -RepositoryRoot $fixturePath -Arguments @("add", ".")
        $null = Invoke-AcceptanceBoundaryGit -RepositoryRoot $fixturePath -Arguments @("commit", "-q", "-m", "acceptance refresh")

        $null = Test-AcceptanceRefreshGitBoundary -RepositoryRoot $fixturePath -TestedSourceCommit $testedCommit -AllowedPaths $acceptanceRefreshAllowedPaths

        Set-Content -LiteralPath (Join-Path $fixturePath $acceptanceRefreshAllowedPaths[0]) -Value "working-tree acceptance" -NoNewline
        $null = Test-AcceptanceRefreshGitBoundary -RepositoryRoot $fixturePath -TestedSourceCommit $testedCommit -AllowedPaths $acceptanceRefreshAllowedPaths
        Remove-Item -LiteralPath (Join-Path $fixturePath $acceptanceRefreshAllowedPaths[0]) -Force

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
            Path = (Get-Command pwsh -ErrorAction Stop).Source
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

    # Linux release runners can use globalization-invariant mode. Exercise that
    # exact formatting environment even when this preflight runs on Windows.
    $previousInvariantMode = [Environment]::GetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "Process")
    try {
        [Environment]::SetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "1", "Process")
        $output = @(& $hostCommands[0].Path -NoProfile -ExecutionPolicy Bypass -File $generatorPath -Check 2>&1)
        $exitCode = $LASTEXITCODE
        if ($exitCode -ne 0) {
            throw "Cross-app dashboard generator -Check failed under invariant globalization: $($output -join "`n")"
        }

        Write-Host "Cross-app dashboard generator -Check passed under invariant globalization."
    }
    finally {
        [Environment]::SetEnvironmentVariable("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", $previousInvariantMode, "Process")
    }
}

if ($BoundarySelfTest) {
    Invoke-AcceptanceBoundaryMutationSelfTest
    return
}

if ($AcceptanceRefresh) {
    Invoke-RealAcceptanceRefreshBoundary
    return
}

Test-CrossAppDashboardGeneratorHosts

$resolvedDashboardPath = Resolve-ToolRepoPath -Path $DashboardPath -RepoRoot $repoRoot
$dashboard = Read-ToolJson -Path $DashboardPath -RepoRoot $repoRoot -MissingMessage "Required generated cross-app dashboard is missing"

Assert-DashboardCondition ($dashboard.schema -eq "freex.parity.cross-app-dashboard.v3") "Cross-app dashboard schema must be v3."
Assert-DashboardCondition ($dashboard.wave -eq 197) "Cross-app dashboard must describe Wave197."
Assert-DashboardCondition ($dashboard.cumulativeAppSlices -eq 591) "Wave197 cumulative app-slice count must be 591."
Assert-DashboardCondition ([string]$dashboard.cumulativeAppSlicesStatus -eq "accepted-local-gates") "Wave197 app-slice count must record accepted local gates."
Assert-DashboardCondition ([string]$dashboard.integrationGateStatus -eq "accepted-local-gates") "Wave197 integration status must record accepted local gates."
Assert-DashboardCondition (@($dashboard.pendingIntegrationGates).Count -eq 0) "Wave197 must have zero pending local gates."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.status -eq "accepted-local-gates") "Wave197 integration evidence must record accepted local gates."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.acceptanceStatus -eq "accepted-local-gates") "Wave197 acceptance status must record accepted local gates."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.testedSourceCommit -eq $acceptanceRefreshWave197TestedSourceCommit) "Wave197 must record the exact tested source SHA."
Assert-DashboardCondition (@($dashboard.integrationGateEvidence.pendingIntegrationGates).Count -eq 0) "Wave197 integration evidence must have zero pending local gates."
Assert-DashboardCondition (@($dashboard.integrationGateEvidence.acceptedLocalGates) -contains "repository-preflight") "Wave197 must record accepted repository preflight."
Assert-DashboardCondition (@($dashboard.integrationGateEvidence.acceptedLocalGates) -contains "full-release-build") "Wave197 must record accepted Release build."
Assert-DashboardCondition (@($dashboard.integrationGateEvidence.acceptanceRefreshAllowedPaths).Count -eq 6) "Wave197 acceptance refresh must allow exactly six paths."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.sliceAccounting -eq "Wave 197 is three app slices, one each for FreeX, FreeW, and FreeP; cumulative accounting is 591 app slices (197 per app).") "Wave197 slice accounting must be exact."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.gateBoundary -match "a6b1f27e02d15a7495644db64c9bda3a839f126a.*six allowlisted") "Wave197 gate boundary must retain the exact tested-source and six-path boundary."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.gateBoundary -match "full Avalonia/WPF parity is not claimed") "Wave197 gate boundary must retain the no-full-parity claim."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.localGatePolicy -match "repository preflight and the full Release build.*delegated to GitHub") "Wave197 gate policy must match AGENTS.md."
Assert-DashboardCondition (@($dashboard.integrationGateEvidence.delegatedGitHubGates).Count -eq 2) "Wave197 delegated GitHub gates must remain explicit."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.delegatedGitHubGateStatus -eq "not-run-locally") "Wave197 must not claim delegated GitHub gates ran locally."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.focusedTests -match "FreeX 16/16.*FreeW 20/20.*FreeP 4/4") "Wave197 focused/evidence facts must remain recorded."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.fullReleaseBuild -match "MSBuild 00:07:04.93; wrapper 00:07:05.2629619") "Wave197 Release-build timings must remain exact."
Assert-DashboardCondition ([string]$dashboard.integrationGateEvidence.independentReview -match "no P1/P2 findings after all remediation") "Wave197 final independent review must remain clear."
$historicalWave196 = $dashboard.integrationGateEvidence.historicalWave196Acceptance
Assert-DashboardCondition ($null -ne $historicalWave196) "Wave196 acceptance history must remain available."
Assert-DashboardCondition ([string]$historicalWave196.testedSourceCommit -eq $acceptanceRefreshWave196TestedSourceCommit) "Wave196 historical acceptance must retain its exact tested source commit."
Assert-DashboardCondition ([string]$historicalWave196.sliceAccounting -eq "Wave 196 is three app slices, one each for FreeX, FreeW, and FreeP; cumulative accounting is 588 app slices (196 per app).") "Wave196 historical slice accounting must remain exact."
Assert-DashboardCondition ([string]$historicalWave196.focusedTests -match "FreeX focused 22/22.*FreeW focused 2/2.*FreeP renderer/evidence 10/10 and resolved model 1/1") "Wave196 historical focused/evidence facts must remain recorded."
$historicalWave195 = $historicalWave196.historicalWave195Acceptance
Assert-DashboardCondition ($null -ne $historicalWave195) "Wave195 acceptance history must remain available."
Assert-DashboardCondition ([string]$historicalWave195.status -eq "accepted-local-gates") "Wave195 historical status must remain accepted."
Assert-DashboardCondition ([string]$historicalWave195.testedSourceCommit -eq "feff4d47c02d57112c6cb191bcc85e1d60ea4e06") "Wave195 historical acceptance must retain its exact tested source commit."
Assert-DashboardCondition (@($historicalWave195.pendingIntegrationGates).Count -eq 0) "Wave195 historical acceptance must retain no pending gates."
Assert-DashboardCondition ([string]$historicalWave195.sliceAccounting -eq "Wave 195 is three app slices, one each for FreeX, FreeW, and FreeP; cumulative accounting is 585 app slices (195 per app).") "Wave195 historical slice accounting must remain exact."
Assert-DashboardCondition ([string]$historicalWave195.focusedTests -match "FreeX Wave195 physical 2/2.*FreeW canonical 291 rows.*FreeP whole-window 36/36 and combined 64/64") "Wave195 historical focused/evidence facts must remain recorded."
$historicalWave194 = $historicalWave195.historicalWave194Acceptance
Assert-DashboardCondition ($null -ne $historicalWave194) "Wave194 acceptance history must remain available."
Assert-DashboardCondition ([string]$historicalWave194.testedSourceCommit -eq $acceptanceRefreshTestedSourceCommit) "Wave194 historical evidence must name tested source commit $acceptanceRefreshTestedSourceCommit."
Assert-DashboardCondition ($null -eq $historicalWave194.PSObject.Properties["integrationHead"]) "Wave194 historical evidence must not use a recursive current-HEAD claim."
Assert-DashboardCondition ([string]$historicalWave194.acceptanceRefreshNote -eq $acceptanceRefreshNote) "Wave194 historical evidence must retain its acceptance-only note."
Assert-DashboardCondition ([string]$historicalWave194.reintegration -eq "The current integration branch is anchored to tested source commit ${acceptanceRefreshTestedSourceCommit}; the acceptance refresh records only evidence from that tested source and does not claim that the documentation commit itself was rebuilt.") "Wave194 historical reintegration evidence must retain the tested-source boundary."
Assert-DashboardCondition ([string]$historicalWave194.focusedTests -match "FreeX Avalonia Wave194 9/9.*FreeP ChartRenderPlanner 264/264") "Wave194 historical focused-test evidence must remain available."
Assert-DashboardCondition ([string]$historicalWave194.initialReintegrationPreflight -match "supplied repository-preflight") "Wave194 historical preflight evidence must remain available."
Assert-DashboardCondition ([string]$historicalWave194.initialIndependentReview -match "two P2 findings.*FreeX.*FreeP") "Wave194 historical review findings must remain available."
Assert-DashboardCondition ([string]$historicalWave194.reviewRemediation -match "one authoritative mixed-type geometry contract.*schema v3.*color-geometry guard") "Wave194 historical remediations must remain available."
Assert-DashboardCondition ([string]$historicalWave194.independentReviewStatus -eq "passed") "Wave194 historical review must remain marked passed."
Assert-DashboardCondition ([string]$historicalWave194.repositoryPreflight -match "Passed at tested source commit") "Wave194 historical preflight result must remain available."
Assert-DashboardCondition ([string]$historicalWave194.fullReleaseBuild -match "Passed at tested source commit.*0 warnings and 0 errors") "Wave194 historical Release-build result must remain available."
Assert-DashboardCondition ([string]$historicalWave194.defaultNonUiTestLane -match "43,548 passed, 134 intentional skips, 0 failed, 43,682 total") "Wave194 historical default-lane result must remain available."
Assert-DashboardCondition ([string]$historicalWave194.sliceAccounting -match "582 cumulative app slices") "Wave194 historical slice accounting must remain explicit."
Assert-DashboardCondition ([string]$historicalWave194.sourceTestRemediation -match "generated inventory and visual manifests remain the authority") "Wave194 historical source boundary must remain available."
Assert-DashboardCondition ([string]$historicalWave194.workerVerification -match "FreeW and FreeP.*Functional/source evidence and visual comparison evidence") "Wave194 historical evidence must distinguish source and visual claims."
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
    "docs/parity/evidence/wave195-freex-autofilter-criteria-workflows-20260828/manifest.json",
    "docs/parity/avalonia-parity-wave195-cross-app-integration-20260828.md",
    "docs/parity/freex-wave196-ribbon-formatting/README.md",
    "tests/FreeX.App.Avalonia.Tests/Wave196RibbonFormattingPhysicalSourceTests.cs",
    "freew/docs/parity/avalonia-parity-wave196-freew-paged-caret-boundary-20260829.md",
    "freew/FreeW.App.Avalonia.Tests/DocumentViewHeadlessTests.cs",
    "docs/parity/freep-wave196-deck17-light-hinting-20260829.md",
    "docs/parity/evidence/freep-wave196-deck17-light-hinting-20260829/metrics.json",
    "docs/parity/evidence/freep-wave196-deck17-light-hinting-20260829/images.json",
    "freep/FreeP.App.Rendering.Avalonia.Tests/Wave196Deck17LightHintingEvidenceTests.cs",
    "freep/FreeP.App.Presentation.Tests/Wave196Deck17Slide02ResolvedModelTests.cs",
    "docs/parity/avalonia-parity-wave196-cross-app-integration-20260829.md",
    "tools/Generate-CrossAppParityDashboard.ps1",
    "tests/FreeX.App.Host.Tests/CrossAppParityDashboardTests.cs",
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
    "docs/parity/evidence/freep-wave194-deck17-slide02-topology-20260823/topology.json",
    "docs/parity/freex-wave197-ribbon-number-format/README.md",
    "tests/FreeX.App.Avalonia.Tests/Wave197RibbonNumberFormatPhysicalSourceTests.cs",
    "freew/docs/parity/avalonia-parity-wave197-freew-legal-notices-template-candidates-20260829.md",
    "freew/FreeW.App.Avalonia.Tests/Wave197LegalNoticesEvidenceTests.cs",
    "freew/docs/parity/evidence/wave197-freew-legal-notices-raw-evidence.json",
    "freew/docs/parity/evidence/SHA256SUMS.txt",
    "docs/parity/freep-wave197-deck17-leading-residual-20260829.md",
    "docs/parity/evidence/freep-wave197-deck17-leading-residual-20260829/metrics.json",
    "docs/parity/freep-wave197-deck17-baseline-alignment-20260829.md",
    "docs/parity/evidence/freep-wave197-deck17-baseline-alignment-20260829/metrics.json",
    "docs/parity/evidence/freep-wave197-deck17-baseline-alignment-20260829/images.json",
    "docs/parity/avalonia-parity-wave197-cross-app-integration-20260829.md"
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
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave195.status -eq "passed") "FreeX Wave195 physical evidence must be passed."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave195.validationMode -eq "physical-only") "FreeX Wave195 evidence must remain physical-only."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave195.physicalPassed -eq 2 -and $freeX.renderedEvidence.physicalEvidence.wave195.physicalTotal -eq 2) "FreeX Wave195 physical sessions must pass 2/2."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave195.evidenceArtifactCount -eq 75 -and $freeX.renderedEvidence.physicalEvidence.wave195.screenshotCount -eq 58) "FreeX Wave195 artifact counts must be 75 and 58 screenshots."
Assert-DashboardCondition ($freeX.renderedEvidence.physicalEvidence.wave195.reloadWitnessPassed -eq 2 -and $freeX.renderedEvidence.physicalEvidence.wave195.reloadWitnessTotal -eq 2) "FreeX Wave195 reload witnesses must pass 2/2."
Assert-DashboardCondition ([string]$freeX.renderedEvidence.physicalEvidence.wave195.claimBoundary -match "bounded.*not exhaustive parity or WPF evidence") "FreeX Wave195 claim boundary must remain bounded."
$freeXWave196 = $freeX.renderedEvidence.physicalEvidence.wave196
Assert-DashboardCondition ([string]$freeXWave196.status -eq "evidence-recorded") "FreeX Wave196 evidence must be recorded without acceptance."
Assert-DashboardCondition ($freeXWave196.physicalPassed -eq 1 -and $freeXWave196.physicalTotal -eq 1) "FreeX Wave196 production probe must be 1/1."
Assert-DashboardCondition ($freeXWave196.focusedSourceTestsPassed -eq 22 -and $freeXWave196.focusedSourceTestsTotal -eq 22) "FreeX Wave196 focused source tests must be 22/22."
Assert-DashboardCondition ([string]$freeXWave196.persistedStyle -eq "style-id=1|font-id=1|bold=true" -and $freeXWave196.saveClean -eq $true) "FreeX Wave196 saved Bold evidence must remain exact."
$freeXWave197 = $freeX.renderedEvidence.physicalEvidence.wave197
Assert-DashboardCondition ([string]$freeXWave197.status -eq "evidence-recorded") "FreeX Wave197 evidence must be recorded without acceptance."
Assert-DashboardCondition ($freeXWave197.physicalPassed -eq 1 -and $freeXWave197.physicalTotal -eq 1) "FreeX Wave197 production probe must be 1/1."
Assert-DashboardCondition ($freeXWave197.focusedSourceTestsPassed -eq 16 -and $freeXWave197.focusedSourceTestsTotal -eq 16) "FreeX Wave197 focused source tests must be 16/16."
Assert-DashboardCondition ([string]$freeXWave197.productionDockerX11Report -eq "20260829T013532Z" -and [string]$freeXWave197.persistedStyle -eq "style-id=1|numFmtId=2|number-format=true" -and $freeXWave197.saveClean -eq $true) "FreeX Wave197 number-format package evidence must remain exact."
Assert-DashboardCondition ([string]$freeXWave197.ordinaryBubbleKeyRouting -eq "retained" -and [string]$freeXWave197.deferredComboDismissFocusRestore -match "rechecks focus immediately and synchronously restores worksheet focus") "FreeX Wave197 focus-routing boundary must remain explicit."

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
$freeWWave195 = $freeW.renderedEvidence.wave195
Assert-DashboardCondition ($freeWWave195.catalogRowCount -eq $freeWArtifacts.evidenceRowCount) "FreeW Wave195 catalog count must equal the canonical evidence row count."
Assert-DashboardCondition (($freeWWave195.passCount + $freeWWave195.genuineVisualMismatchCount + $freeWWave195.avaloniaExtensionCount) -eq $freeWWave195.catalogRowCount) "FreeW Wave195 classifications must partition the canonical catalog."
Assert-DashboardCondition ($freeWWave195.legalNoticesStateCount -gt 0) "FreeW Wave195 must retain canonical Legal Notices states."
Assert-DashboardCondition ($freeWWave195.legalNoticesChangedPixels - $freeWWave195.legalNoticesBaselineChangedPixels -eq $freeWWave195.legalNoticesAggregateDelta) "FreeW Wave195 Legal Notices delta must be derived from before/after metrics."
Assert-DashboardCondition ($freeWWave195.nonLegalRowsStructurallyUnchanged + $freeWWave195.legalNoticesStateCount -eq $freeWWave195.catalogRowCount) "FreeW Wave195 row counts must partition Legal Notices and non-Legal rows."
$freeWWave196 = $freeW.renderedEvidence.wave196
Assert-DashboardCondition ([string]$freeWWave196.status -eq "evidence-recorded") "FreeW Wave196 evidence must be recorded without acceptance."
Assert-DashboardCondition (@($freeWWave196.scenarios) -contains "ConsecutiveTrailingInlineFlowBreaks_PlaceCaretAtTheFinalPostBreakBoundary") "FreeW Wave196 must include consecutive-break coverage."
Assert-DashboardCondition ($freeWWave196.focusedSourceTestsPassed -eq 2 -and $freeWWave196.focusedSourceTestsTotal -eq 2) "FreeW Wave196 focused source tests must be 2/2."
Assert-DashboardCondition ($freeWWave196.consecutiveBreakCoverage -eq $true) "FreeW Wave196 consecutive-break coverage must remain explicit."
$freeWWave197 = $freeW.renderedEvidence.wave197
Assert-DashboardCondition ([string]$freeWWave197.status -eq "candidate-refuted") "FreeW Wave197 candidate review must be recorded as rejected."
Assert-DashboardCondition ($freeWWave197.scenarioCount -eq 6 -and $freeWWave197.uniqueScenarioCount -eq 6) "FreeW Wave197 must retain exactly six unique scenarios."
Assert-DashboardCondition ($freeWWave197.focusedSourceTestsPassed -eq 20 -and $freeWWave197.focusedSourceTestsTotal -eq 20) "FreeW Wave197 focused tests must be 20/20."
Assert-DashboardCondition ([string]$freeWWave197.surfaceMarginCandidate -match "regressed all six") "FreeW Wave197 surface-margin candidate must remain rejected."
Assert-DashboardCondition ([string]$freeWWave197.lineBoxCandidate -match "improved two long rows and regressed two") "FreeW Wave197 line-box candidate disposition must remain exact."
Assert-DashboardCondition ($freeWWave197.productionCandidateRetained -eq $false) "FreeW Wave197 must retain no production candidate."
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
$freePWave195 = $freeP.renderedEvidence.wave195
$freePPairedEvidence = $freeP.renderedEvidence.pairedEvidence
Assert-DashboardCondition ($freePWave195.wholeWindowScenarioCount -eq 36 -and $freePWave195.wholeWindowPassCount -eq 36 -and $freePWave195.wholeWindowMismatchCount -eq 0) "FreeP Wave195 whole-window evidence must be 36/36 pass."
Assert-DashboardCondition ($freePWave195.combinedRenderedEvidenceCount -eq $freePPairedEvidence.pairedScenarioCount -and $freePWave195.combinedRenderedEvidencePassCount -eq $freePPairedEvidence.passCount -and $freePWave195.combinedRenderedEvidenceMismatchCount -eq $freePPairedEvidence.mismatchCount) "FreeP Wave195 combined evidence must track the computed paired-evidence totals."
Assert-DashboardCondition ($freePWave195.combinedRenderedEvidenceCount -eq 64 -and $freePWave195.combinedRenderedEvidencePassCount -eq 64 -and $freePWave195.combinedRenderedEvidenceMismatchCount -eq 0) "FreeP Wave195 current combined evidence fact must remain 64/64 pass with zero mismatches."
Assert-DashboardCondition ($freeP.renderedEvidence.wave195.richTextSelection.changedPixelRatioBefore -eq 0.2185757 -and $freeP.renderedEvidence.wave195.richTextSelection.changedPixelRatioAfter -eq 0.1809518682) "FreeP Wave195 selection ratios must remain exact."
Assert-DashboardCondition ($freeP.renderedEvidence.wave195.richTextSelection.meanChannelDelta -eq 9.7919313736 -and $freeP.renderedEvidence.wave195.richTextSelection.perceptualHashDistance -eq 11 -and [string]$freeP.renderedEvidence.wave195.richTextSelection.cropDimensions -eq "251x74") "FreeP Wave195 selection metrics must remain exact."
Assert-DashboardCondition ([string]$freeP.renderedEvidence.wave195.claimBoundary -match "Office deck17 slide02 residual remains unresolved") "FreeP Wave195 must retain the native Office residual boundary."
$freePWave196 = $freeP.renderedEvidence.wave196
Assert-DashboardCondition ([string]$freePWave196.status -eq "evidence-recorded") "FreeP Wave196 evidence must be recorded without acceptance."
Assert-DashboardCondition ([string]$freePWave196.target -eq "17-bullets-autofit / slide-02") "FreeP Wave196 target must remain deck17 slide02."
Assert-DashboardCondition ([string]$freePWave196.textHintingModeAfter -eq "Light") "FreeP Wave196 must retain the Light hinting correction."
Assert-DashboardCondition ($freePWave196.controlUnchanged -eq $true -and $freePWave196.imageHashCount -eq 4) "FreeP Wave196 control and image-hash evidence must remain explicit."
$freePWave197 = $freeP.renderedEvidence.wave197
Assert-DashboardCondition ([string]$freePWave197.status -eq "candidate-refuted") "FreeP Wave197 candidate review must be recorded as rejected."
Assert-DashboardCondition ($freePWave197.focusedSourceTestsPassed -eq 4 -and $freePWave197.focusedSourceTestsTotal -eq 4) "FreeP Wave197 focused tests must be 4/4."
Assert-DashboardCondition ($freePWave197.productionCandidateRetained -eq $false) "FreeP Wave197 must retain no production candidate."
Assert-DashboardCondition ([string]$freePWave197.trackedImageBytesAndHashes -match "verified.*generation linkage is explicitly unproven") "FreeP Wave197 image provenance boundary must remain explicit."
Assert-DashboardCondition ([string]$freePWave197.residualBoundary -match "unresolved text-raster residual.*not a fallback-font diagnosis") "FreeP Wave197 residual boundary must remain explicit."

Write-Host "Cross-app parity dashboard schema and evidence aggregation guards passed."
