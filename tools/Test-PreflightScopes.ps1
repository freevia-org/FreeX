[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Contract + table tests for the diff-scoped LOCAL preflight (eng/preflight-scopes.json). Invoked by
# tools/Test-TestGateContract.ps1; also runnable on its own. Fails when a preflight check exists
# without a scope entry (and the reverse), when a sample path does not select its own check, or when
# selection stops failing safe.

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'PreflightScopeSupport.ps1')

$errors = [System.Collections.Generic.List[string]]::new()
function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { $script:errors.Add($Message) }
}

$scopes = Get-Content -LiteralPath (Join-Path $repoRoot 'eng/preflight-scopes.json') -Raw | ConvertFrom-Json
Assert-True ($scopes.schemaVersion -eq 1) 'eng/preflight-scopes.json must have schemaVersion 1.'

# --- every preflight check has a scope entry (and vice versa) -----------------------------------
$preflightSource = Get-Content -LiteralPath (Join-Path $repoRoot 'tools/Test-RepositoryPreflight.ps1') -Raw
$labels = @([regex]::Matches($preflightSource, '(?m)Resolve-RepositoryPreflightEntry\s+-Label\s+"([^"]+)"') | ForEach-Object { $_.Groups[1].Value } | Sort-Object -Unique)
Assert-True ($labels.Count -ge 10) "Could not read the preflight check labels from Test-RepositoryPreflight.ps1 (found $($labels.Count))."
$entryNames = @($scopes.checks.PSObject.Properties.Name)
foreach ($label in $labels) {
    Assert-True ($entryNames -contains $label) "Preflight check '$label' has no entry in eng/preflight-scopes.json; add one (an unmapped check always runs locally, which defeats scoping)."
}
foreach ($name in $entryNames) {
    Assert-True ($labels -contains $name) "eng/preflight-scopes.json has an entry for '$name', which is not a check in Test-RepositoryPreflight.ps1."
}

# --- shape --------------------------------------------------------------------------------------
$allPatterns = @($scopes.globalTriggers) + @($scopes.ignorablePaths)
foreach ($entry in @($scopes.checks.PSObject.Properties)) {
    $allPatterns += @($entry.Value.triggers)
    if ($entry.Value.PSObject.Properties.Name -contains 'structuralTriggers') { $allPatterns += @($entry.Value.structuralTriggers) }
    Assert-True (@($entry.Value.triggers).Count -gt 0) "Scope entry '$($entry.Name)' must declare at least one trigger."
    Assert-True ($entry.Value.PSObject.Properties.Name -contains 'sample') "Scope entry '$($entry.Name)' must declare a sample path that selects it."
}
foreach ($pattern in $allPatterns) {
    Assert-True (-not [string]::IsNullOrWhiteSpace([string]$pattern) -and -not ([string]$pattern).Contains('\')) "Invalid scope pattern '$pattern': use non-empty forward-slash globs."
}

$inertDirectories = @((Get-Content -LiteralPath (Join-Path $repoRoot 'eng/test-gates.json') -Raw | ConvertFrom-Json).ciImpact.inertDocsDirectories | ForEach-Object { [string]$_ })
$inert = [string]$inertDirectories[0]

function Get-Selected([object[]]$Changes, [object]$ScopeSet = $scopes, [string[]]$LabelSet = $labels) {
    return Resolve-PreflightScope -Changes $Changes -Labels $LabelSet -Scopes $ScopeSet -CiInertDocsDirectories $inertDirectories
}
function New-Change([string]$Status, [string]$Path) { return [pscustomobject]@{ Status = $Status; Path = $Path } }
function Join-Names($selection) { return (@($selection.RunLabels | Sort-Object) -join ', ') }

# --- every sample path selects its own check ----------------------------------------------------
foreach ($entry in @($scopes.checks.PSObject.Properties)) {
    $selection = Get-Selected @(New-Change 'M' ([string]$entry.Value.sample))
    Assert-True ($selection.RunLabels.Contains($entry.Name)) "Sample path '$($entry.Value.sample)' does not select '$($entry.Name)' (selected: $(Join-Names $selection))."
}

# --- table: change set -> exactly which checks run ----------------------------------------------
function Assert-Selection([string]$Name, [object[]]$Changes, [string[]]$Expected, [bool]$ExpectFull = $false) {
    $selection = Get-Selected $Changes
    if ($ExpectFull) {
        Assert-True $selection.Full "[$Name] expected a FULL selection but got: $(Join-Names $selection)."
        return
    }
    Assert-True (-not $selection.Full) "[$Name] unexpectedly selected everything ($($selection.Reason))."
    $actual = @($selection.RunLabels | Sort-Object)
    $wanted = @($Expected | Sort-Object)
    Assert-True (($actual -join '|') -ceq ($wanted -join '|')) "[$Name] expected [$($wanted -join ', ')] but got [$($actual -join ', ')]."
}

Assert-Selection 'C# source edit' @((New-Change 'M' 'src/FreeX.Core.Model/Workbook.cs')) @('cross-platform portability', 'Git conflict markers', 'test-gate contract')
Assert-Selection 'test source edit' @((New-Change 'M' 'tests/FreeX.Core.Model.Tests/WorkbookTests.cs')) @('GitHub workflows', 'cross-platform portability', 'Git conflict markers', 'test-gate contract')
Assert-Selection 'tool script edit' @((New-Change 'M' 'tools/Generate-Something.ps1')) @('cross-platform portability', 'Git conflict markers', 'PowerShell tools', 'test-gate contract')
Assert-Selection 'inert markdown' @((New-Change 'M' "docs/$inert/note.md")) @('Git conflict markers')
Assert-Selection 'inert binary' @((New-Change 'M' "docs/$inert/figure.png")) @()
Assert-Selection 'inert file added' @((New-Change 'A' "docs/$inert/figure.png")) @('cross-platform portability')
Assert-Selection 'avalonia macOS surface' @((New-Change 'M' 'src/FreeX.App.Avalonia/Program.cs')) @('cross-platform portability', 'Git conflict markers', 'macOS app readiness', 'test-gate contract')
Assert-Selection 'linux packaging asset' @((New-Change 'M' 'tools/packaging/linux/freex.conf')) @('Linux packaging scripts', 'PowerShell tools', 'cross-platform portability', 'test-gate contract')
Assert-Selection 'json edit' @((New-Change 'M' 'eng/something.json')) @('JSON files', 'cross-platform portability', 'Git conflict markers', 'test-gate contract')
Assert-Selection 'project reference edit' @((New-Change 'M' 'src/FreeX.Core.Model/FreeX.Core.Model.csproj')) @('XML files', '.NET SDK readiness', '.NET project references', 'solution projects', 'CodeQL production solution', 'cross-platform portability', 'default test solution projects', 'FreeW solution projects', 'FreeP solution projects', 'Git conflict markers', 'test-gate contract')
Assert-Selection 'rename = delete + add' @((New-Change 'D' 'src/A/Old.csproj'), (New-Change 'A' 'src/B/New.csproj')) @('XML files', '.NET SDK readiness', '.NET project references', 'solution projects', 'CodeQL production solution', 'cross-platform portability', 'default test solution projects', 'FreeW solution projects', 'FreeP solution projects', 'Git conflict markers', 'test-gate contract')
Assert-Selection 'deleted source file' @((New-Change 'D' 'src/FreeX.Core.Model/Old.cs')) @('cross-platform portability', 'Git conflict markers', 'test-gate contract')
Assert-Selection 'no changes' @() @()
foreach ($globalPath in @('Directory.Build.props', 'Directory.Packages.props', 'global.json', 'tools/ToolScriptSupport.ps1', 'tools/Test-RepositoryPreflight.ps1', 'eng/preflight-scopes.json', '.gitattributes')) {
    Assert-Selection "global trigger $globalPath" @((New-Change 'M' $globalPath)) @() $true
}
Assert-Selection 'dot-dot path' @((New-Change 'M' '../outside.txt')) @() $true
Assert-Selection 'absolute path' @((New-Change 'M' '/etc/passwd')) @() $true
Assert-Selection 'control character path' @((New-Change 'M' "src/a`nb.cs")) @() $true

# Unmapped path => everything. (The real manifest maps almost everything, so prove the rule on a
# deliberately narrow synthetic one.)
$narrow = [pscustomobject]@{
    globalTriggers = @()
    ignorablePaths = @('ignorable/**')
    checks = [pscustomobject]@{ 'only check' = [pscustomobject]@{ triggers = @('known/**') } }
}
$unmapped = Get-Selected @((New-Change 'M' 'unknown/file.txt')) $narrow @('only check')
Assert-True $unmapped.Full 'A changed path that matches no check and is not ignorable must select every check.'
$ignored = Get-Selected @((New-Change 'M' 'ignorable/file.txt')) $narrow @('only check')
Assert-True ((-not $ignored.Full) -and $ignored.RunLabels.Count -eq 0) 'A declared-ignorable path must select nothing.'
$known = Get-Selected @((New-Change 'M' 'known/file.txt')) $narrow @('only check')
Assert-True ((-not $known.Full) -and $known.RunLabels.Contains('only check')) 'A mapped path must select its check.'

# A check the manifest does not know always runs, whatever changed.
$unknownCheck = Get-Selected @((New-Change 'M' "docs/$inert/figure.png")) $scopes (@($labels) + @('brand new check'))
Assert-True $unknownCheck.RunLabels.Contains('brand new check') 'A check with no scope entry must always run.'

# --- glob semantics ------------------------------------------------------------------------------
$globs = @(
    @{ Glob = '**/*.json'; Path = 'a.json'; Match = $true }
    @{ Glob = '**/*.json'; Path = 'x/y/a.json'; Match = $true }
    @{ Glob = '**/*.json'; Path = 'x/a.jsonx'; Match = $false }
    @{ Glob = 'src/**'; Path = 'src/a/b.cs'; Match = $true }
    @{ Glob = 'src/**'; Path = 'srcx/a.cs'; Match = $false }
    @{ Glob = '*'; Path = 'README.md'; Match = $true }
    @{ Glob = '*'; Path = 'a/README.md'; Match = $false }
    @{ Glob = 'a/*.cs'; Path = 'a/b/c.cs'; Match = $false }
    @{ Glob = '**/Packaging/**'; Path = 'src/App/Packaging/linux/x.sh'; Match = $true }
)
foreach ($row in $globs) {
    Assert-True ((Test-PreflightGlobMatch -Path $row.Path -Patterns @($row.Glob)) -eq $row.Match) "Glob '$($row.Glob)' vs '$($row.Path)': expected $($row.Match)."
}
Assert-True (-not (Test-PreflightGlobMatch -Path 'docs/x.md' -Patterns @('**', '!docs/**'))) "'!' must remove a previously matched path."
Assert-True (Test-PreflightGlobMatch -Path 'docs/testing/test-gates.md' -Patterns @('**', '!docs/**', 'docs/testing/test-gates.md')) 'A later pattern must be able to re-add a path.'

# --- CI and release paths never scope ------------------------------------------------------------
foreach ($workflow in @(Get-ChildItem -LiteralPath (Join-Path $repoRoot '.github/workflows') -File)) {
    $content = Get-Content -LiteralPath $workflow.FullName -Raw
    Assert-True ($content -notmatch 'ScopeBaseRef') "$($workflow.Name) must not scope the preflight: hosted CI and release workflows always run every check."
}
$integration = Get-Content -LiteralPath (Join-Path $repoRoot 'tools/Test-BranchForIntegration.ps1') -Raw
Assert-True ($integration -match '\[switch\]\$Full') 'Test-BranchForIntegration.ps1 must expose -Full.'
Assert-True ($integration -match 'ScopeBaseRef') 'Test-BranchForIntegration.ps1 must pass -ScopeBaseRef to the preflight unless -Full.'
$ciYml = Get-Content -LiteralPath (Join-Path $repoRoot '.github/workflows/ci.yml') -Raw
Assert-True ($ciYml -match 'Test-RepositoryPreflight\.ps1 -Mode Static') 'ci.yml must keep running the full static preflight.'
Assert-True ($ciYml -match 'Test-RepositoryPreflight\.ps1 -Mode Platform') 'ci.yml must keep running the full platform preflight.'

if ($errors.Count -gt 0) {
    throw ("Preflight scope checks failed:`n - " + ($errors -join "`n - "))
}
Write-Host "Preflight scope checks passed: $($labels.Count) checks mapped, selection tables and fail-safe probes."
