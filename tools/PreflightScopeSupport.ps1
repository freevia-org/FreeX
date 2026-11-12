# Diff-scoped selection of repository preflight checks for the LOCAL pre-merge gate. Dot-source.
#
# The local gate (tools/Test-BranchForIntegration.ps1) only trusts a scoped preflight when the
# branch contains the exact green origin/main commit: then every check whose inputs the branch did
# not touch is, by construction, as green as it was on that commit. A check runs when any changed
# path (added, deleted, modified, renamed -- both sides) matches its triggers in
# eng/preflight-scopes.json. FAIL-SAFE rules, all of which select EVERYTHING:
#   * the base commit is not an ancestor of HEAD, or the diff cannot be computed
#   * the scopes manifest is missing, malformed, or has no entry for a check that exists
#     (an unmapped CHECK always runs on its own; it also fails the contract test)
#   * a changed path matches a global trigger
#   * a changed path matches no check, is not declared ignorable (an unmapped PATH)
# CI (ci.yml), tester-release and a bare Test-RepositoryPreflight.ps1 never pass a base ref, so
# they keep running every check; -Full forces the same locally.

Set-StrictMode -Version Latest

function ConvertTo-PreflightGlobRegex {
    param([string]$Glob)

    $builder = [System.Text.StringBuilder]::new('^')
    $index = 0
    while ($index -lt $Glob.Length) {
        $character = $Glob[$index]
        if ($character -eq '*') {
            if ($index + 1 -lt $Glob.Length -and $Glob[$index + 1] -eq '*') {
                if ($index + 2 -lt $Glob.Length -and $Glob[$index + 2] -eq '/') {
                    [void]$builder.Append('(?:.*/)?')
                    $index += 3
                    continue
                }
                [void]$builder.Append('.*')
                $index += 2
                continue
            }
            [void]$builder.Append('[^/]*')
        }
        elseif ($character -eq '?') {
            [void]$builder.Append('[^/]')
        }
        else {
            [void]$builder.Append([regex]::Escape([string]$character))
        }
        $index++
    }
    [void]$builder.Append('$')
    return $builder.ToString()
}

# Ordered, last-match-wins pattern list; a leading '!' removes a previously matched path.
function Test-PreflightGlobMatch {
    param([string]$Path, [string[]]$Patterns)

    $matched = $false
    foreach ($pattern in $Patterns) {
        $negated = $pattern.StartsWith('!', [System.StringComparison]::Ordinal)
        $body = if ($negated) { $pattern.Substring(1) } else { $pattern }
        if ($Path -cmatch (ConvertTo-PreflightGlobRegex $body)) {
            $matched = -not $negated
        }
    }
    return $matched
}

function Expand-PreflightPatterns {
    param([string[]]$Patterns, [string[]]$CiInertDocsDirectories)

    $expanded = [System.Collections.Generic.List[string]]::new()
    foreach ($pattern in $Patterns) {
        if ($pattern -eq '{ciInertDocs}' -or $pattern -eq '!{ciInertDocs}') {
            $prefix = if ($pattern.StartsWith('!', [System.StringComparison]::Ordinal)) { '!' } else { '' }
            foreach ($directory in $CiInertDocsDirectories) {
                $expanded.Add("${prefix}docs/$directory/**")
            }
        }
        else {
            $expanded.Add($pattern)
        }
    }
    return @($expanded)
}

function Get-PreflightChangedPaths {
    param([string]$RepoRoot, [string]$MergeBase)

    $changes = [System.Collections.Generic.List[object]]::new()
    # Committed plus uncommitted tracked changes relative to the merge base. --no-renames reports a
    # rename as delete + add so BOTH sides are matched.
    $raw = & git -C $RepoRoot -c core.quotepath=off diff -z --name-status --no-renames $MergeBase
    if ($LASTEXITCODE -ne 0) { throw "git diff against $MergeBase failed." }
    $tokens = @(([string]($raw -join "`n")).Split([char]0) | Where-Object { $_ -ne '' })
    for ($i = 0; $i -lt $tokens.Count; $i++) {
        $status = $tokens[$i].Trim("`n")
        if ($status -notmatch '^[ACDMRTUXB][0-9]*$' -or $i + 1 -ge $tokens.Count) {
            throw "Unrecognized git diff output near '$status'."
        }
        $changes.Add([pscustomobject]@{ Status = $status.Substring(0, 1); Path = $tokens[$i + 1].Replace('\', '/') })
        $i++
    }
    # Untracked files are additions the diff above cannot see.
    $untracked = & git -C $RepoRoot -c core.quotepath=off ls-files -z --others --exclude-standard
    if ($LASTEXITCODE -ne 0) { throw 'git ls-files --others failed.' }
    foreach ($path in @(([string]($untracked -join "`n")).Split([char]0) | Where-Object { $_ -ne '' })) {
        $changes.Add([pscustomobject]@{ Status = 'A'; Path = $path.Replace('\', '/') })
    }
    return @($changes)
}

# Pure decision core (table-testable): given changes and the scopes manifest, which labels run?
function Resolve-PreflightScope {
    param(
        [object[]]$Changes,
        [string[]]$Labels,
        [object]$Scopes,
        [string[]]$CiInertDocsDirectories = @()
    )

    $runLabels = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $reasons = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    function Select-All([string]$Reason) {
        foreach ($label in $Labels) { [void]$runLabels.Add($label) }
        return [pscustomobject]@{ Full = $true; Reason = $Reason; RunLabels = $runLabels; Reasons = $reasons }
    }

    $globalTriggers = Expand-PreflightPatterns -Patterns @($Scopes.globalTriggers) -CiInertDocsDirectories $CiInertDocsDirectories
    $ignorable = Expand-PreflightPatterns -Patterns @($Scopes.ignorablePaths) -CiInertDocsDirectories $CiInertDocsDirectories
    $checkNames = @($Scopes.checks.PSObject.Properties.Name)

    # A check with no scope entry cannot be reasoned about: it always runs.
    foreach ($label in $Labels) {
        if ($checkNames -notcontains $label) {
            [void]$runLabels.Add($label)
            $reasons[$label] = 'no entry in eng/preflight-scopes.json (always runs)'
        }
    }

    $compiled = @{}
    foreach ($label in $Labels) {
        if ($checkNames -contains $label) {
            $entry = $Scopes.checks.$label
            $compiled[$label] = [pscustomobject]@{
                Triggers = Expand-PreflightPatterns -Patterns @($entry.triggers) -CiInertDocsDirectories $CiInertDocsDirectories
                Structural = Expand-PreflightPatterns -Patterns @($(if ($entry.PSObject.Properties.Name -contains 'structuralTriggers') { $entry.structuralTriggers })) -CiInertDocsDirectories $CiInertDocsDirectories
                Own = if ($entry.PSObject.Properties.Name -contains 'ownPaths') { @($entry.ownPaths) } else { @() }
            }
        }
    }

    foreach ($change in $Changes) {
        $path = [string]$change.Path
        if ([string]::IsNullOrWhiteSpace($path) -or $path -match '[\x00-\x1f]' -or $path.StartsWith('/', [System.StringComparison]::Ordinal) -or @($path.Split('/')) -contains '..') {
            return Select-All "odd path shape '$path'"
        }
        if (Test-PreflightGlobMatch -Path $path -Patterns $globalTriggers) {
            return Select-All "global trigger '$path' changed"
        }
        $structural = @('A', 'D', 'T', 'C', 'R') -contains [string]$change.Status
        $mapped = $false
        foreach ($label in $Labels) {
            if (-not $compiled.ContainsKey($label)) { continue }
            $scope = $compiled[$label]
            $hit = (Test-PreflightGlobMatch -Path $path -Patterns $scope.Triggers) -or
                (@($scope.Own) -contains $path) -or
                ($structural -and (Test-PreflightGlobMatch -Path $path -Patterns $scope.Structural))
            if ($hit) {
                $mapped = $true
                if ($runLabels.Add($label)) {
                    $reasons[$label] = "$($change.Status) $path"
                }
            }
        }
        if (-not $mapped -and -not (Test-PreflightGlobMatch -Path $path -Patterns $ignorable)) {
            return Select-All "unmapped path '$path' (matches no check and is not declared ignorable)"
        }
    }
    return [pscustomobject]@{ Full = $false; Reason = ''; RunLabels = $runLabels; Reasons = $reasons }
}

# Full orchestration: returns Full=$true with a reason whenever scoping cannot be trusted.
function Get-PreflightScopeSelection {
    param(
        [string]$RepoRoot,
        [string]$BaseRef,
        [string[]]$Labels,
        [hashtable]$OwnPathsByLabel = @{}
    )

    function New-FullSelection([string]$Reason) {
        $all = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
        foreach ($label in $Labels) { [void]$all.Add($label) }
        return [pscustomobject]@{ Full = $true; Reason = $Reason; RunLabels = $all; Reasons = @{} }
    }

    try {
        $scopesPath = Join-Path $RepoRoot 'eng/preflight-scopes.json'
        if (-not (Test-Path -LiteralPath $scopesPath -PathType Leaf)) {
            return New-FullSelection 'eng/preflight-scopes.json is missing'
        }
        $scopes = Get-Content -LiteralPath $scopesPath -Raw | ConvertFrom-Json
        if ($scopes.schemaVersion -ne 1) {
            return New-FullSelection "unsupported preflight scope schema version '$($scopes.schemaVersion)'"
        }

        $baseSha = (& git -C $RepoRoot rev-parse --verify "$BaseRef^{commit}" 2>$null)
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($baseSha)) {
            return New-FullSelection "base ref '$BaseRef' does not resolve"
        }
        & git -C $RepoRoot merge-base --is-ancestor $baseSha HEAD 2>$null
        if ($LASTEXITCODE -ne 0) {
            return New-FullSelection "HEAD does not contain base commit $($baseSha.Substring(0, 9)); a scoped run would not be anchored on a green commit"
        }

        $inert = @()
        $gatesPath = Join-Path $RepoRoot 'eng/test-gates.json'
        if (Test-Path -LiteralPath $gatesPath -PathType Leaf) {
            $gates = Get-Content -LiteralPath $gatesPath -Raw | ConvertFrom-Json
            if (@($gates.PSObject.Properties.Name) -contains 'ciImpact') {
                $inert = @($gates.ciImpact.inertDocsDirectories | ForEach-Object { [string]$_ })
            }
        }

        $changes = @(Get-PreflightChangedPaths -RepoRoot $RepoRoot -MergeBase $baseSha.Trim())
        # Each check also depends on its own script.
        foreach ($label in $OwnPathsByLabel.Keys) {
            $entry = $scopes.checks.PSObject.Properties[$label]
            if ($null -ne $entry) {
                $entry.Value | Add-Member -NotePropertyName ownPaths -NotePropertyValue @($OwnPathsByLabel[$label]) -Force
            }
        }
        $selection = Resolve-PreflightScope -Changes $changes -Labels $Labels -Scopes $scopes -CiInertDocsDirectories $inert
        $selection | Add-Member -NotePropertyName ChangedPathCount -NotePropertyValue $changes.Count -Force
        $selection | Add-Member -NotePropertyName BaseSha -NotePropertyValue $baseSha.Trim() -Force
        return $selection
    }
    catch {
        return New-FullSelection "scope selection failed closed ($($_.Exception.Message))"
    }
}
