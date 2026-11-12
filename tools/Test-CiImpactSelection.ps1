[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Table-driven proof that CI impact selection fails SAFE. Invoked by tools/Test-TestGateContract.ps1
# (and therefore by the static preflight on every push); also runnable on its own.
#   1. the inert-directory list is mechanically true (nothing that can affect a commit gate reads it)
#   2. path -> mode table: only paths inside an inert directory ever reduce anything
#   3. decision table: every way of failing to establish a fact selects the full matrix
#   4. the reduced matrix keeps every preflight owner, and the full matrix is unchanged
#   5. a reduced run can never be mistaken for a full attestation by release resolution
#   6. ci.yml wiring pins

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'CiImpactSupport.ps1')

$errors = [System.Collections.Generic.List[string]]::new()
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { $script:errors.Add($Message) }
}

$settings = Get-CiImpactSettings -RepoRoot $repoRoot
Assert-True ($settings.InertDocsDirectories.Count -gt 0) 'ciImpact.inertDocsDirectories must not be empty.'
$inertDirectory = [string]$settings.InertDocsDirectories[0]
$inert = "docs/$inertDirectory"

# --- 1. The inert list is true ------------------------------------------------------------------
foreach ($finding in @(Test-CiInertDirectoryReferences -RepoRoot $repoRoot -InertDirectories $settings.InertDocsDirectories -Exemptions $settings.Exemptions)) {
    $errors.Add("docs/$($finding.Directory) is listed as inert but '$($finding.File)' $($finding.Reason). Remove it from ciImpact.inertDocsDirectories in eng/test-gates.json, or stop reading it.")
}
foreach ($problem in @(Test-CiInertExemptions -RepoRoot $repoRoot -Exemptions $settings.Exemptions)) {
    $errors.Add($problem)
}

# --- 2. Path -> mode table ----------------------------------------------------------------------
$pathTable = @(
    @{ Paths = @("$inert/a.md"); Mode = 'inert' }
    @{ Paths = @("$inert/deep/er/a.png", "$inert/b.json"); Mode = 'inert' }
    @{ Paths = @($settings.InertDocsDirectories | ForEach-Object { "docs/$_/x.md" }); Mode = 'inert' }
    @{ Paths = @(); Mode = 'inert' }
    @{ Paths = @("$inert/a.md", 'src/FreeX.Core.Model/Workbook.cs'); Mode = 'full' }
    @{ Paths = @('docs/parity/matrix.md'); Mode = 'full' }
    @{ Paths = @('docs/README.md'); Mode = 'full' }
    @{ Paths = @('docs/testing/test-gates.md'); Mode = 'full' }
    @{ Paths = @('README.md'); Mode = 'full' }
    @{ Paths = @('AGENTS.md'); Mode = 'full' }
    @{ Paths = @('THIRD_PARTY_NOTICES.md'); Mode = 'full' }
    @{ Paths = @('LICENSE'); Mode = 'full' }
    @{ Paths = @('.github/workflows/ci.yml'); Mode = 'full' }
    @{ Paths = @('.github/workflows/release-readiness.yml'); Mode = 'full' }
    @{ Paths = @('.github/CODEOWNERS'); Mode = 'full' }
    @{ Paths = @('Directory.Build.props'); Mode = 'full' }
    @{ Paths = @('Directory.Build.targets'); Mode = 'full' }
    @{ Paths = @('Directory.Packages.props'); Mode = 'full' }
    @{ Paths = @('global.json'); Mode = 'full' }
    @{ Paths = @('NuGet.config'); Mode = 'full' }
    @{ Paths = @('FreeX.slnx'); Mode = 'full' }
    @{ Paths = @('FreeW.slnx'); Mode = 'full' }
    @{ Paths = @('eng/test-gates.json'); Mode = 'full' }
    @{ Paths = @('eng/preflight-scopes.json'); Mode = 'full' }
    @{ Paths = @('tools/Test-ToolScripts.ps1'); Mode = 'full' }
    @{ Paths = @('tools/CiImpactSupport.ps1'); Mode = 'full' }
    @{ Paths = @('tools/FreeX.FidelityCompare/Program.cs'); Mode = 'full' }
    @{ Paths = @('src/FreeX.App.Host/MainWindow.xaml'); Mode = 'full' }
    @{ Paths = @('src/FreeX.Core.Model/FreeX.Core.Model.csproj'); Mode = 'full' }
    @{ Paths = @('shared/Free.Shared.Ribbon/RibbonModel.cs'); Mode = 'full' }
    @{ Paths = @('freew/FreeW.Core.IO/DocxReader.cs'); Mode = 'full' }
    @{ Paths = @('freep/FreeP.App.Host/Window.cs'); Mode = 'full' }
    @{ Paths = @('tests/FreeX.Core.Model.Tests/X.cs'); Mode = 'full' }
    @{ Paths = @('brand-new-top-level/file.txt'); Mode = 'full' }
    @{ Paths = @('somefile'); Mode = 'full' }
    # Path shapes that look inert but are not: traversal, case, prefix without separator, bare
    # directory, absolute, quoting, control characters, drive letters.
    @{ Paths = @("$inert/../../src/FreeX.Core.Model/Workbook.cs"); Mode = 'full' }
    @{ Paths = @("$inert/.."); Mode = 'full' }
    @{ Paths = @("$inert/./a.md"); Mode = 'full' }
    @{ Paths = @("Docs/$inertDirectory/a.md"); Mode = 'full' }
    @{ Paths = @("docs/${inertDirectory}x/a.md"); Mode = 'full' }
    @{ Paths = @("docs/${inertDirectory}-copy/a.md"); Mode = 'full' }
    @{ Paths = @($inert); Mode = 'full' }
    @{ Paths = @("$inert/"); Mode = 'full' }
    @{ Paths = @("/$inert/a.md"); Mode = 'full' }
    @{ Paths = @("C:/$inert/a.md"); Mode = 'full' }
    @{ Paths = @("$inert//a.md"); Mode = 'full' }
    @{ Paths = @("$inert/a`"b.md"); Mode = 'full' }
    @{ Paths = @("$inert/a`nb.md"); Mode = 'full' }
)
foreach ($row in $pathTable) {
    $classification = Get-CiImpactClassification -ChangedPaths $row.Paths -InertPrefixes $settings.InertPrefixes
    Assert-True ($classification.Mode -eq $row.Mode) "Path table: [$(($row.Paths | ForEach-Object { $_ -replace "`n", '\n' }) -join ', ')] expected '$($row.Mode)' but selected '$($classification.Mode)'."
}

# Rename out of an inert directory arrives as delete + add; the add side is not inert.
$renameOut = Get-CiImpactClassification -ChangedPaths @("$inert/old.md", 'src/FreeX.Core.Model/old.md') -InertPrefixes $settings.InertPrefixes
Assert-True ($renameOut.Mode -eq 'full') 'A rename out of an inert directory must select the full matrix.'

# --- 3. Decision table --------------------------------------------------------------------------
$a1 = '1' * 40
$a2 = '2' * 40
$a3 = '3' * 40
$evidenceFor = { param([string[]]$Have) $have = $Have; return { param($sha) if ($have -contains $sha) { [pscustomobject]@{ Sha = $sha } } else { $null } }.GetNewClosure() }
$diffOf = { param([string[]]$Paths) $paths = $Paths; return { param($anchor) $paths }.GetNewClosure() }
$inertPrefixes = $settings.InertPrefixes
$decisions = @(
    @{ Name = 'inert diff against nearest full ancestor'; Event = 'push'; Force = $false; Ancestors = @($a1, $a2); Have = @($a1); Diff = @("$inert/a.md"); Mode = 'inert'; Anchor = $a1 }
    @{ Name = 'anchor skips ancestors without a full run'; Event = 'push'; Force = $false; Ancestors = @($a1, $a2); Have = @($a2); Diff = @("$inert/a.md"); Mode = 'inert'; Anchor = $a2 }
    @{ Name = 'code diff'; Event = 'push'; Force = $false; Ancestors = @($a1); Have = @($a1); Diff = @('src/FreeX.Core.Model/A.cs'); Mode = 'full'; Anchor = $a1 }
    @{ Name = 'mixed diff'; Event = 'push'; Force = $false; Ancestors = @($a1); Have = @($a1); Diff = @("$inert/a.md", 'tools/X.ps1'); Mode = 'full'; Anchor = $a1 }
    @{ Name = 'no ancestor has a full run'; Event = 'push'; Force = $false; Ancestors = @($a1, $a2, $a3); Have = @(); Diff = @("$inert/a.md"); Mode = 'full'; Anchor = '' }
    @{ Name = 'no ancestors (first push / force push to unrelated history)'; Event = 'push'; Force = $false; Ancestors = @(); Have = @(); Diff = @("$inert/a.md"); Mode = 'full'; Anchor = '' }
    @{ Name = 'scheduled drift run'; Event = 'schedule'; Force = $true; Ancestors = @($a1); Have = @($a1); Diff = @("$inert/a.md"); Mode = 'full'; Anchor = '' }
    @{ Name = 'schedule event without the force flag'; Event = 'schedule'; Force = $false; Ancestors = @($a1); Have = @($a1); Diff = @("$inert/a.md"); Mode = 'full'; Anchor = '' }
    @{ Name = 'manual dispatch'; Event = 'workflow_dispatch'; Force = $false; Ancestors = @($a1); Have = @($a1); Diff = @("$inert/a.md"); Mode = 'full'; Anchor = '' }
    @{ Name = 'push with full requested'; Event = 'push'; Force = $true; Ancestors = @($a1); Have = @($a1); Diff = @("$inert/a.md"); Mode = 'full'; Anchor = '' }
    @{ Name = 'unknown event'; Event = 'pull_request'; Force = $false; Ancestors = @($a1); Have = @($a1); Diff = @("$inert/a.md"); Mode = 'full'; Anchor = '' }
)
foreach ($row in $decisions) {
    $decision = Resolve-CiCoverageDecision -EventName $row.Event -ForceFull $row.Force -Ancestors $row.Ancestors -InertPrefixes $inertPrefixes `
        -FindFullEvidence (& $evidenceFor $row.Have) -GetChangedPaths (& $diffOf $row.Diff)
    Assert-True ($decision.Mode -eq $row.Mode) "Decision table '$($row.Name)': expected '$($row.Mode)' but got '$($decision.Mode)' ($($decision.Reason))."
    if ($row.Mode -eq 'inert') {
        Assert-True ($decision.Anchor -eq $row.Anchor) "Decision table '$($row.Name)': expected anchor $($row.Anchor.Substring(0, 3))... but got '$($decision.Anchor)'."
    }
}
$throwingEvidence = Resolve-CiCoverageDecision -EventName 'push' -ForceFull $false -Ancestors @($a1) -InertPrefixes $inertPrefixes `
    -FindFullEvidence { param($sha) throw 'API unavailable' } -GetChangedPaths (& $diffOf @("$inert/a.md"))
Assert-True ($throwingEvidence.Mode -eq 'full') 'An error while reading ancestor run evidence must select the full matrix.'
$throwingDiff = Resolve-CiCoverageDecision -EventName 'push' -ForceFull $false -Ancestors @($a1) -InertPrefixes $inertPrefixes `
    -FindFullEvidence (& $evidenceFor @($a1)) -GetChangedPaths { param($anchor) throw 'history not fetched' }
Assert-True ($throwingDiff.Mode -eq 'full') 'An error while diffing against the anchor must select the full matrix.'

# --- 4. Matrices ---------------------------------------------------------------------------------
$fullMatrix = (& (Join-Path $PSScriptRoot 'Get-TestGateMatrix.ps1') -Gate commit -Coverage full) | ConvertFrom-Json
$inertMatrix = (& (Join-Path $PSScriptRoot 'Get-TestGateMatrix.ps1') -Gate commit -Coverage inert) | ConvertFrom-Json
$defaultMatrix = (& (Join-Path $PSScriptRoot 'Get-TestGateMatrix.ps1') -Gate commit) | ConvertFrom-Json
Assert-True (@($defaultMatrix.include).Count -eq @($fullMatrix.include).Count) 'The default matrix must be the full matrix.'
Assert-True (@($fullMatrix.include | Where-Object { -not $_.runGate }).Count -eq 0) 'Every leg of the full matrix must run its test gate.'
Assert-True (@($inertMatrix.include).Count -gt 0 -and @($inertMatrix.include).Count -lt @($fullMatrix.include).Count) 'The inert matrix must be a non-empty strict subset of the full matrix.'
Assert-True (@($inertMatrix.include | Where-Object { $_.runGate }).Count -eq 0) 'No leg of the inert matrix may run a test gate.'
Assert-True (@($inertMatrix.include | Where-Object { $_.runStaticPreflight }).Count -eq 1) 'The inert matrix must keep exactly one static preflight owner.'
foreach ($platform in @('windows', 'linux', 'macos')) {
    Assert-True (@($inertMatrix.include | Where-Object { $_.runPlatformPreflight -and $_.platform -eq $platform }).Count -eq 1) "The inert matrix must keep exactly one '$platform' platform preflight owner."
}
foreach ($leg in @($fullMatrix.include | Where-Object { $_.runStaticPreflight -or $_.runPlatformPreflight })) {
    $kept = @($inertMatrix.include | Where-Object { $_.gateId -eq $leg.gateId -and $_.platform -eq $leg.platform })
    Assert-True ($kept.Count -eq 1) "The inert matrix dropped preflight-owning leg $($leg.gateId)/$($leg.platform)."
}
$refused = $false
try { & (Join-Path $PSScriptRoot 'Get-TestGateMatrix.ps1') -Gate release -Coverage inert | Out-Null } catch { $refused = $true }
Assert-True $refused 'Inert coverage must be refused for the release gate.'

# --- 5. Coverage evidence and release resolution ------------------------------------------------
Assert-True ((ConvertFrom-CiCoverageJobName 'CI coverage: full').Mode -eq 'full') 'The full coverage job name must parse.'
$inertName = Get-CiCoverageJobName -Mode inert -Anchor $a1
Assert-True ((ConvertFrom-CiCoverageJobName $inertName).Anchor -eq $a1) 'The inert coverage job name must round-trip its anchor.'
foreach ($bad in @('CI coverage: inert anchor=abc', 'CI coverage: inert', 'CI coverage: partial', 'CI coverage:  full', "CI coverage: inert anchor=$('G' * 40)", 'CI coverage: full extra')) {
    Assert-True ($null -eq (ConvertFrom-CiCoverageJobName $bad)) "Malformed coverage job name '$bad' must not parse."
}
function New-JobsResponse([object[]]$Jobs) { return [pscustomobject]@{ jobs = $Jobs } }
function New-Job([string]$Name, [string]$Conclusion = 'success') { return [pscustomobject]@{ name = $Name; status = 'completed'; conclusion = $Conclusion } }
Assert-True ((Get-CiRunCoverage (New-JobsResponse @((New-Job 'FreeX commit gate')))).Mode -eq 'legacy') 'A run without a coverage job predates impact selection and counts as legacy-full.'
Assert-True ((Get-CiRunCoverage (New-JobsResponse @((New-Job 'CI coverage: full')))).Mode -eq 'full') 'A full coverage job must read as full.'
Assert-True ((Get-CiRunCoverage (New-JobsResponse @((New-Job 'CI coverage: full' 'failure')))).Mode -eq 'invalid') 'A failed coverage job must not read as full.'
Assert-True ((Get-CiRunCoverage (New-JobsResponse @((New-Job 'CI coverage: full'), (New-Job 'CI coverage: full')))).Mode -eq 'invalid') 'Two coverage jobs are ambiguous and must not read as full.'
Assert-True ((Get-CiRunCoverage (New-JobsResponse @((New-Job 'CI coverage: garbage')))).Mode -eq 'invalid') 'An unparsable coverage job must not read as full.'

$fixtureRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ci-impact-" + [guid]::NewGuid().ToString('N'))
try {
    $head = 'a' * 40
    $mid = 'b' * 40
    $anchor = 'c' * 40
    function Write-RunFixture([string]$Sha, [string]$Workflow, [string]$Conclusion, [long]$RunId, [object]$Jobs) {
        $directory = Join-Path $fixtureRoot $Sha
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
        $payload = @{ workflow_runs = @(@{ id = $RunId; head_sha = $Sha; status = 'completed'; conclusion = $Conclusion; updated_at = '2026-11-12T00:00:00Z'; html_url = "https://github.example/runs/$RunId" }) }
        [System.IO.File]::WriteAllText((Join-Path $directory "$Workflow.json"), ($payload | ConvertTo-Json -Depth 5))
        if ($null -ne $Jobs) {
            [System.IO.File]::WriteAllText((Join-Path $directory "$Workflow.$RunId.jobs.json"), ($Jobs | ConvertTo-Json -Depth 5))
        }
    }
    function Test-Candidate([string]$Sha, [string[]]$Ancestors) {
        try {
            & (Join-Path $PSScriptRoot 'Test-GitHubReleaseCandidate.ps1') -Repository 'owner/repo' -CommitSha $Sha `
                -RunMetadataDirectory (Join-Path $fixtureRoot $Sha) -AncestorShas $Ancestors -RequiredWorkflows @('ci.yml') | Out-Null
            return $true
        }
        catch {
            return $false
        }
    }

    # Legacy run (no coverage job): accepted exactly as before.
    Write-RunFixture $head 'ci.yml' 'success' 11 $null
    Assert-True (Test-Candidate $head @($head, $mid, $anchor)) 'A pre-selection (legacy) green ci.yml run must still attest its commit.'

    # Full run: accepted.
    Write-RunFixture $head 'ci.yml' 'success' 12 (New-JobsResponse @((New-Job 'CI coverage: full')))
    Assert-True (Test-Candidate $head @($head, $mid, $anchor)) 'A full-coverage ci.yml run must attest its commit.'

    # Inert run with a proven, older, ancestor anchor: accepted.
    Write-RunFixture $anchor 'ci.yml' 'success' 20 (New-JobsResponse @((New-Job 'CI coverage: full')))
    Write-RunFixture $head 'ci.yml' 'success' 13 (New-JobsResponse @((New-Job (Get-CiCoverageJobName -Mode inert -Anchor $anchor))))
    Assert-True (Test-Candidate $head @($head, $mid, $anchor)) 'An inert-only run anchored on a green full run in its ancestry must attest its commit.'

    # ...but never without ancestry proof (offline mode has no compare API to fall back to).
    Assert-True (-not (Test-Candidate $head @($head, $mid))) 'An inert-only run whose anchor is not shown to be an ancestor must NOT attest its commit.'
    Assert-True (-not (Test-Candidate $head @($anchor, $mid, $head))) 'An inert-only run whose anchor is NEWER than the commit must NOT attest its commit.'

    # Anchor whose own run failed.
    Write-RunFixture $anchor 'ci.yml' 'failure' 21 (New-JobsResponse @((New-Job 'CI coverage: full')))
    Assert-True (-not (Test-Candidate $head @($head, $mid, $anchor))) 'An inert-only run whose anchor has no successful run must NOT attest its commit.'

    # Anchor that was itself an inert-only run (no full evidence behind it).
    Write-RunFixture $anchor 'ci.yml' 'success' 22 (New-JobsResponse @((New-Job (Get-CiCoverageJobName -Mode inert -Anchor $mid))))
    Assert-True (-not (Test-Candidate $head @($head, $mid, $anchor))) 'An inert-only run anchored on another inert-only run must NOT attest its commit.'

    # Unreadable coverage record.
    Write-RunFixture $head 'ci.yml' 'success' 14 (New-JobsResponse @((New-Job 'CI coverage: whatever')))
    Assert-True (-not (Test-Candidate $head @($head, $mid, $anchor))) 'A run with an unreadable coverage record must NOT attest its commit.'

    # Self-anchored.
    Write-RunFixture $head 'ci.yml' 'success' 15 (New-JobsResponse @((New-Job (Get-CiCoverageJobName -Mode inert -Anchor $head))))
    Assert-True (-not (Test-Candidate $head @($head, $mid, $anchor))) 'A run anchored on its own commit must NOT attest anything.'

    # Resolver: head is an unproven inert run, so the walk must fall through to the older full candidate.
    Write-RunFixture $anchor 'ci.yml' 'success' 23 (New-JobsResponse @((New-Job 'CI coverage: full')))
    Write-RunFixture $head 'ci.yml' 'success' 16 (New-JobsResponse @((New-Job (Get-CiCoverageJobName -Mode inert -Anchor ('d' * 40)))))
    $ancestorFile = Join-Path $fixtureRoot 'ancestors.json'
    [System.IO.File]::WriteAllText($ancestorFile, (@(@{ sha = $head }, @{ sha = $mid }, @{ sha = $anchor }) | ConvertTo-Json -Depth 3))
    $resolved = & (Join-Path $PSScriptRoot 'Resolve-GitHubReleaseCandidate.ps1') -Repository 'owner/repo' -CommitSha $head `
        -RequiredWorkflows @('ci.yml') -AncestorMetadataPath $ancestorFile -RunMetadataRoot $fixtureRoot 3>$null | ConvertFrom-Json
    Assert-True ($resolved.attestedSha -eq $anchor) 'Release resolution must skip an unproven inert-only head and attest the newest commit with real evidence.'
}
finally {
    if (Test-Path -LiteralPath $fixtureRoot) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}

# --- 6. ci.yml wiring ----------------------------------------------------------------------------
$ci = Get-Content -LiteralPath (Join-Path $repoRoot '.github/workflows/ci.yml') -Raw
foreach ($pin in @(
    'tools/Select-CiCoverage.ps1',
    'tools/Get-TestGateMatrix.ps1 -Gate commit -Coverage ${{ steps.coverage.outputs.mode || ''full'' }}',
    'name: "CI coverage: ${{ needs.prepare.outputs.coverage }}"',
    'needs: [prepare, coverage, tests]',
    'needs.coverage.result',
    'schedule:',
    'inputs.full',
    'filter: blob:none',
    'actions: read'
)) {
    Assert-True ($ci.Contains($pin)) "ci.yml must contain '$pin'."
}
Assert-True (([regex]::Matches($ci, '(?m)^\s+if: matrix\.runGate\s*$')).Count -eq 4) 'ci.yml must gate exactly the cache restore, timestamp, cache record and test-gate steps on matrix.runGate.'
Assert-True ($ci -match '(?m)^\s+if: matrix\.runStaticPreflight\s*$') 'The static preflight step must depend only on matrix.runStaticPreflight (never on impact selection).'
Assert-True ($ci -match '(?m)^\s+if: matrix\.runPlatformPreflight\s*$') 'The platform preflight step must depend only on matrix.runPlatformPreflight (never on impact selection).'
Assert-True (-not ($ci -match 'runStaticPreflight\s*&&|&&\s*matrix\.runGate|runPlatformPreflight\s*&&')) 'Preflight steps must not be combined with other conditions.'

if ($errors.Count -gt 0) {
    throw ("CI impact selection checks failed:`n - " + ($errors -join "`n - "))
}
Write-Host "CI impact selection checks passed: $($pathTable.Count) path rows, $($decisions.Count + 2) decision rows, matrix and release-resolution probes."
