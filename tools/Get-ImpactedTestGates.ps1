[CmdletBinding()]
param(
    [string]$BaseRef = 'origin/main',

    [string]$HeadRef = 'HEAD',

    [string[]]$ChangedPaths = @(),

    [ValidateSet('Json', 'Text')]
    [string]$OutputFormat = 'Text'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$manifestPath = Join-Path $repoRoot 'eng/test-gates.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1) {
    throw "Unsupported test-gates schema version '$($manifest.schemaVersion)'."
}

function ConvertTo-RepositoryPath([string]$Path) {
    return $Path.Replace('\', '/').TrimStart('./')
}

function Test-ImpactPattern([string]$Path, [string]$Pattern) {
    $normalizedPattern = ConvertTo-RepositoryPath $Pattern
    return $Path -like $normalizedPattern
}

if ($ChangedPaths.Count -eq 0) {
    & git -C $repoRoot rev-parse --verify "$BaseRef^{commit}" 2>$null | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Base ref '$BaseRef' does not resolve to a commit. Fetch origin/main before selecting gates."
    }

    $ChangedPaths = @(& git -C $repoRoot diff --name-only --diff-filter=ACMRT "$BaseRef...$HeadRef")
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to compare '$BaseRef' with '$HeadRef'."
    }
}

$normalizedChangedPaths = @(
    $ChangedPaths |
        ForEach-Object { ConvertTo-RepositoryPath ([string]$_) } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Sort-Object -Unique
)

$windowsCommitGates = @($manifest.gates | Where-Object {
    $_.gate -eq 'commit' -and $_.platforms -contains 'windows'
})
$selectedGateIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$reasons = [System.Collections.Generic.List[object]]::new()

function Add-Gate([object]$Gate, [string]$Reason) {
    if ($selectedGateIds.Add([string]$Gate.id)) {
        $reasons.Add([ordered]@{ gateId = [string]$Gate.id; reason = $Reason })
    }
}

$globalImpactPaths = @(
    'Directory.Build.props',
    'Directory.Build.targets',
    'Directory.Packages.props',
    'global.json',
    'NuGet.config',
    'eng/test-gates.json'
)

foreach ($changedPath in $normalizedChangedPaths) {
    if ($globalImpactPaths -contains $changedPath) {
        foreach ($gate in $windowsCommitGates) {
            Add-Gate $gate "global build/test contract changed: $changedPath"
        }
    }

    foreach ($gate in $windowsCommitGates) {
        if ($gate.PSObject.Properties.Name -notcontains 'impactPaths') {
            continue
        }

        foreach ($pattern in @($gate.impactPaths)) {
            if (Test-ImpactPattern $changedPath ([string]$pattern)) {
                Add-Gate $gate "manifest impact path '$pattern' matched '$changedPath'"
                break
            }
        }
    }
}

# Follow ProjectReference edges so a production-library edit selects every manifest gate whose
# test project transitively consumes it. This handles ordinary dependencies without maintaining a
# duplicate path map in the manifest.
$trackedProjects = @(& git -C $repoRoot ls-files '*.csproj')
if ($LASTEXITCODE -ne 0) {
    throw 'Unable to enumerate tracked .NET projects.'
}
$projectPaths = @($trackedProjects | ForEach-Object { ConvertTo-RepositoryPath $_ })
$projectByFullPath = @{}
$projectByDirectory = @{}
foreach ($projectPath in $projectPaths) {
    $fullPath = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $projectPath))
    $projectByFullPath[$fullPath] = $projectPath
    $directory = (Split-Path -Parent $projectPath).Replace('\', '/')
    $projectByDirectory[$directory] = $projectPath
}

$reverseReferences = @{}
foreach ($projectPath in $projectPaths) {
    $projectFullPath = Join-Path $repoRoot $projectPath
    [xml]$projectXml = Get-Content -LiteralPath $projectFullPath -Raw
    foreach ($reference in @($projectXml.SelectNodes("//*[local-name()='ProjectReference']"))) {
        $include = [string]$reference.GetAttribute('Include')
        if ([string]::IsNullOrWhiteSpace($include)) {
            continue
        }

        $referenceFullPath = [System.IO.Path]::GetFullPath((Join-Path (Split-Path -Parent $projectFullPath) $include))
        if (-not $projectByFullPath.ContainsKey($referenceFullPath)) {
            continue
        }

        $referencedProject = [string]$projectByFullPath[$referenceFullPath]
        if (-not $reverseReferences.ContainsKey($referencedProject)) {
            $reverseReferences[$referencedProject] = [System.Collections.Generic.List[string]]::new()
        }
        $reverseReferences[$referencedProject].Add($projectPath)
    }
}

$impactedProjects = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
$pendingProjects = [System.Collections.Generic.Queue[string]]::new()
foreach ($changedPath in $normalizedChangedPaths) {
    $owningProject = $null
    foreach ($directory in @($projectByDirectory.Keys | Sort-Object Length -Descending)) {
        if ($changedPath -eq $projectByDirectory[$directory] -or $changedPath.StartsWith("$directory/", [System.StringComparison]::OrdinalIgnoreCase)) {
            $owningProject = [string]$projectByDirectory[$directory]
            break
        }
    }

    if ($null -ne $owningProject -and $impactedProjects.Add($owningProject)) {
        $pendingProjects.Enqueue($owningProject)
    }
}

while ($pendingProjects.Count -gt 0) {
    $project = $pendingProjects.Dequeue()
    if (-not $reverseReferences.ContainsKey($project)) {
        continue
    }

    foreach ($consumer in @($reverseReferences[$project])) {
        if ($impactedProjects.Add($consumer)) {
            $pendingProjects.Enqueue($consumer)
        }
    }
}

foreach ($gate in $windowsCommitGates) {
    $gateProjects = @($gate.projects)
    if ($gate.PSObject.Properties.Name -contains 'buildProjects') {
        $gateProjects += @($gate.buildProjects)
    }
    if ($gate.PSObject.Properties.Name -contains 'platformProjects' -and
        $gate.platformProjects.PSObject.Properties.Name -contains 'windows') {
        $gateProjects += @($gate.platformProjects.windows)
    }

    $matchedProject = @($gateProjects | Where-Object { $impactedProjects.Contains([string]$_) } | Select-Object -First 1)
    if ($matchedProject.Count -gt 0) {
        Add-Gate $gate "project dependency reaches '$($matchedProject[0])'"
    }
}

# A source file outside a project directory is unusual but should fail safe, not silently skip an
# app. Shared source can feed every app; app roots select their local Windows commit gates.
foreach ($changedPath in $normalizedChangedPaths) {
    $fallbackApp = if ($changedPath.StartsWith('src/', [System.StringComparison]::OrdinalIgnoreCase)) {
        'FreeX'
    }
    elseif ($changedPath.StartsWith('freew/', [System.StringComparison]::OrdinalIgnoreCase)) {
        'FreeW'
    }
    elseif ($changedPath.StartsWith('freep/', [System.StringComparison]::OrdinalIgnoreCase)) {
        'FreeP'
    }
    else {
        $null
    }

    $isOwned = $false
    foreach ($directory in $projectByDirectory.Keys) {
        if ($changedPath.StartsWith("$directory/", [System.StringComparison]::OrdinalIgnoreCase)) {
            $isOwned = $true
            break
        }
    }
    if ($null -ne $fallbackApp -and -not $isOwned) {
        foreach ($gate in @($windowsCommitGates | Where-Object app -EQ $fallbackApp)) {
            Add-Gate $gate "unowned $fallbackApp source path changed: $changedPath"
        }
    }
    elseif ($changedPath.StartsWith('shared/', [System.StringComparison]::OrdinalIgnoreCase) -and -not $isOwned) {
        foreach ($gate in $windowsCommitGates) {
            Add-Gate $gate "unowned shared source path changed: $changedPath"
        }
    }
}

$orderedGateIds = @($windowsCommitGates | Where-Object { $selectedGateIds.Contains([string]$_.id) } | ForEach-Object { [string]$_.id })
$result = [ordered]@{
    baseRef = $BaseRef
    headRef = $HeadRef
    changedPaths = $normalizedChangedPaths
    gateIds = $orderedGateIds
    reasons = @($reasons)
}

if ($OutputFormat -eq 'Json') {
    $result | ConvertTo-Json -Depth 5
}
else {
    if ($orderedGateIds.Count -eq 0) {
        Write-Host 'No local commit-test gate is affected; repository preflight and the Release build remain required.'
    }
    else {
        Write-Host "Affected local commit gates: $($orderedGateIds -join ', ')"
        foreach ($reason in $reasons) {
            Write-Host "  $($reason.gateId): $($reason.reason)"
        }
    }
}
