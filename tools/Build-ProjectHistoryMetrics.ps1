<#
.SYNOPSIS
    Regenerates docs/history/build-history-metrics.md and docs/history/thread-commit-timing.md
    from the repository's git history plus local Claude Code / Codex usage logs on this machine.

.DESCRIPTION
    This is a committable, repeatable replacement for the one-off local extractor that produced
    the original (2026-06-06) snapshot of these two docs. It is designed to be re-run:
      - on this machine, at any later date, to refresh the git-derived numbers and this
        machine's token numbers, and
      - on the user's OTHER machines, each producing its own per-machine intermediate JSON
        (project-history-tokens-<MachineId>.json) that is TRACKED IN GIT (see below) so a
        later regeneration on any machine aggregates token totals across all machines whose
        JSON file has been committed and pulled into -OutputDir.

    Git-derived metrics (commit/churn/footprint/thread-timing) are always recomputed FRESH from
    the repository at the time the script runs and are authoritative/complete - they do not
    depend on which machine produced them, only on the git history itself.

    Token metrics are inherently per-machine (they come from local Claude Code / Codex log
    files that only exist on the machine that produced them). Each run writes this machine's
    per-day token sums to -OutputDir as project-history-tokens-<MachineId>.json, then the doc
    generation step reads and sums ALL project-history-tokens-*.json files present in
    -OutputDir.

    MULTI-MACHINE WORKFLOW (git-tracked, no manual file transfer):
    project-history-tokens-<MachineId>.json files under the -OutputDir used for this repo
    (conventionally ".metrics-data" at the repo root) are tracked in git (see the ".gitignore"
    entry for the exact carve-out). Each of the user's
    machines has a distinct $env:COMPUTERNAME, so each writes a distinctly-named file with no
    merge conflicts between machines. The cadence on each machine is: run this script, `git add`
    + commit just that machine's project-history-tokens-<MachineId>.json, `git pull` to pick up
    any other machines' committed JSON files, then re-run this script (or just re-run the doc
    generation) so the aggregation step in -OutputDir picks up everyone's numbers. Until a given
    machine's JSON has been committed and pulled elsewhere, the token columns on other machines
    reflect only the machine(s) whose JSON they already have locally. These JSON files contain
    ONLY per-day numeric token/byte/session counts plus machine id and provider metadata - no
    transcript content, prompts, file paths, or session titles - see the field list in the
    "Token Extraction Notes" section of the generated doc.

.PARAMETER RepoRoot
    Path to the repository root. Defaults to the repo containing this script.

.PARAMETER StartDate
    First date (yyyy-MM-dd) of the Daily Build Churn / Daily Provider Token Usage window.

.PARAMETER EndDate
    Last date (yyyy-MM-dd, inclusive) of the window. Defaults to today.

.PARAMETER OutputDir
    Directory for the per-machine intermediate token JSON files (also where the aggregation
    step looks for project-history-tokens-*.json from all machines). Defaults to
    docs/history under the repo root.

.PARAMETER MachineId
    Identifier for this machine's intermediate JSON file name. Defaults to $env:COMPUTERNAME.

.PARAMETER SkipAnthropic
    Skip scanning Claude Code (~/.claude/projects) logs on this machine.

.PARAMETER SkipCodex
    Skip scanning Codex (~/.codex/sessions, ~/.codex/archived_sessions) logs on this machine.

.PARAMETER SkipThreadTiming
    Skip regenerating docs/history/thread-commit-timing.md (useful for a fast dev iteration on
    just the build-history-metrics.md doc; thread timing over the full history is the slowest
    part of a full run).

.EXAMPLE
    pwsh -File tools/Build-ProjectHistoryMetrics.ps1

.EXAMPLE
    pwsh -File tools/Build-ProjectHistoryMetrics.ps1 -StartDate 2026-05-12 -EndDate 2026-08-08
#>

[CmdletBinding()]
param(
    [string]$RepoRoot,
    [string]$StartDate = '2026-05-12',
    [string]$EndDate = (Get-Date -Format 'yyyy-MM-dd'),
    [string]$OutputDir,
    [string]$MachineId = $env:COMPUTERNAME,
    [switch]$SkipAnthropic,
    [switch]$SkipCodex,
    [switch]$SkipThreadTiming
)

$ErrorActionPreference = 'Stop'

function Write-Progress2 {
    param([string]$Message)
    Write-Host "[history-metrics] $Message"
}

# ---------------------------------------------------------------------------
# Setup
# ---------------------------------------------------------------------------

if (-not $RepoRoot) {
    $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
    $RepoRoot = (& git -C $scriptDir rev-parse --show-toplevel 2>$null)
    if (-not $RepoRoot) { $RepoRoot = Split-Path -Parent $scriptDir }
}
$RepoRoot = (Resolve-Path $RepoRoot).Path.TrimEnd('\', '/')

if (-not $OutputDir) { $OutputDir = Join-Path $RepoRoot 'docs\history' }
if (-not (Test-Path $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }

$docsDir = Join-Path $RepoRoot 'docs\history'
if (-not (Test-Path $docsDir)) { New-Item -ItemType Directory -Path $docsDir -Force | Out-Null }

$startDt = [datetime]::ParseExact($StartDate, 'yyyy-MM-dd', $null)
$endDt = [datetime]::ParseExact($EndDate, 'yyyy-MM-dd', $null)
if ($endDt -lt $startDt) { throw "EndDate ($EndDate) is before StartDate ($StartDate)." }

$generatedAt = Get-Date
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

function Save-Utf8NoBom {
    param([string]$Path, [string]$Content)
    [System.IO.File]::WriteAllText($Path, $Content, $utf8NoBom)
}

function Format-Offset {
    param([TimeSpan]$Span)
    if ($Span.TotalSeconds -lt 0) { $Span = [TimeSpan]::Zero }
    $d = [int]$Span.Days
    $h = [int]$Span.Hours
    $m = [int]$Span.Minutes
    if ($d -gt 0) { return ('{0}d {1:D2}h {2:D2}m' -f $d, $h, $m) }
    if ($h -gt 0) { return ('{0}h {1:D2}m' -f $h, $m) }
    return ('{0}m' -f $m)
}

function Format-N0 {
    param([double]$Value)
    return $Value.ToString('N0', [System.Globalization.CultureInfo]::InvariantCulture)
}

Write-Progress2 "Repo root: $RepoRoot"
Write-Progress2 "Window: $StartDate .. $EndDate"
Write-Progress2 "Output dir (per-machine token JSON): $OutputDir"
Write-Progress2 "Machine id: $MachineId"

function Format-ProcessArg {
    # ProcessStartInfo.ArgumentList is unreliable under Windows PowerShell 5.1 (the property
    # getter returns $null on some builds instead of an empty collection), so build a single
    # Arguments string with Windows command-line quoting instead.
    param([string]$Arg)
    if ($null -eq $Arg) { $Arg = '' }
    if ($Arg -match '[\s"]') {
        $escaped = $Arg -replace '"', '\"'
        return '"' + $escaped + '"'
    }
    if ($Arg -eq '') { return '""' }
    return $Arg
}

function Invoke-Git {
    param([string[]]$GitArgs)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = 'git'
    $psi.WorkingDirectory = $RepoRoot
    $psi.Arguments = (($GitArgs | ForEach-Object { Format-ProcessArg $_ }) -join ' ')
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.UseShellExecute = $false
    $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
    $proc = [System.Diagnostics.Process]::Start($psi)
    $stdout = $proc.StandardOutput.ReadToEnd()
    $stderr = $proc.StandardError.ReadToEnd()
    $proc.WaitForExit()
    if ($proc.ExitCode -ne 0) {
        throw "git $($GitArgs -join ' ') failed: $stderr"
    }
    return $stdout
}

$originUrl = (Invoke-Git @('remote', 'get-url', 'origin')).Trim()
if (-not $originUrl) { $originUrl = '(no origin remote)' }
$headSha = (Invoke-Git @('rev-parse', 'HEAD')).Trim()
$headShaShort = (Invoke-Git @('rev-parse', '--short=9', 'HEAD')).Trim()

# ---------------------------------------------------------------------------
# Current Repository Footprint
# ---------------------------------------------------------------------------

Write-Progress2 'Computing current repository footprint...'

function Test-IsTestPath {
    param([string]$Path)
    return [regex]::IsMatch($Path, '(?i)(^|/)tests?(/|$)') -or [regex]::IsMatch($Path, '(?i)Tests?\.cs$')
}

# ---------------------------------------------------------------------------
# App / platform-layer classification (EXACT partitions by repo path; see
# docs/history/build-history-metrics.md "Git Churn By App" / "By Platform
# Layer" sections for the rationale). Every tracked path falls into exactly
# one App bucket and exactly one Platform bucket, so summing either bucket
# set reproduces the same totals as the unbucketed Daily Build Churn table.
# ---------------------------------------------------------------------------

$AppBucketOrder = @('FreeX', 'FreeW', 'FreeP', 'Shared', 'Docs/Tooling/Other')
$PlatformBucketOrder = @('Windows (WPF)', 'Avalonia (Linux/macOS)', 'Platform-neutral (core/shared/IO/model)', 'Non-code')

function Get-AppBucket {
    param([string]$Path)
    if ($Path -like 'src/*' -or $Path -like 'tests/*') { return 'FreeX' }
    if ($Path -like 'freew/*') { return 'FreeW' }
    if ($Path -like 'freep/*') { return 'FreeP' }
    if ($Path -like 'shared/*') { return 'Shared' }
    return 'Docs/Tooling/Other'
}

function Get-PlatformBucket {
    param([string]$Path)
    $isCodeArea = $Path -like 'src/*' -or $Path -like 'tests/*' -or $Path -like 'freew/*' -or $Path -like 'freep/*' -or $Path -like 'shared/*'
    if (-not $isCodeArea) { return 'Non-code' }
    if ($Path -match '(?i)\.App\.Host' -or $Path -match '(?i)\.App\.UI' -or $Path -match '(?i)\.Wpf' -or $Path -match '(?i)Free\.Shared\.[^./]+\.Windows') {
        return 'Windows (WPF)'
    }
    if ($Path -match '(?i)\.App\.Avalonia' -or $Path -match '(?i)\.App\.Rendering\.Avalonia' -or $Path -match '(?i)Free\.Shared\.[^./]+\.Avalonia') {
        return 'Avalonia (Linux/macOS)'
    }
    return 'Platform-neutral (core/shared/IO/model)'
}

$trackedFiles = (Invoke-Git @('ls-files')) -split "`n" | Where-Object { $_ -ne '' }
$trackedCount = $trackedFiles.Count

function Get-LineCount {
    param([string[]]$RelativePaths)
    $total = 0
    foreach ($rel in $RelativePaths) {
        $full = Join-Path $RepoRoot $rel
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            try {
                $count = 0
                $reader = New-Object System.IO.StreamReader($full)
                try {
                    while ($null -ne $reader.ReadLine()) { $count++ }
                } finally {
                    $reader.Dispose()
                }
                $total += $count
            } catch {
                # unreadable/binary-ish file; skip silently
            }
        }
    }
    return $total
}

$csFiles = $trackedFiles | Where-Object { $_ -like '*.cs' }
$testCsFiles = $csFiles | Where-Object { Test-IsTestPath $_ }
$srcCsFiles = $csFiles | Where-Object { -not (Test-IsTestPath $_) }
$xamlFiles = $trackedFiles | Where-Object { $_ -like '*.xaml' }
$mdFiles = $trackedFiles | Where-Object { $_ -like '*.md' }

$srcCsLoc = Get-LineCount $srcCsFiles
$testCsLoc = Get-LineCount $testCsFiles
$xamlLoc = Get-LineCount $xamlFiles
$docsLoc = Get-LineCount $mdFiles

$worktreeCount = ((Invoke-Git @('worktree', 'list')) -split "`n" | Where-Object { $_.Trim() -ne '' }).Count
$localBranchCount = ((Invoke-Git @('branch', '--list')) -split "`n" | Where-Object { $_.Trim() -ne '' }).Count
$remoteBranchCount = ((Invoke-Git @('branch', '-r')) -split "`n" | Where-Object { $_.Trim() -ne '' -and $_ -notmatch '->' }).Count

Write-Progress2 "Footprint: tracked=$trackedCount srcCsLoc=$srcCsLoc testCsLoc=$testCsLoc xamlLoc=$xamlLoc docsLoc=$docsLoc worktrees=$worktreeCount localBranches=$localBranchCount remoteBranches=$remoteBranchCount"

# ---------------------------------------------------------------------------
# Daily Build Churn (git log --numstat over the window, reachable from HEAD)
# ---------------------------------------------------------------------------

Write-Progress2 'Scanning git log --numstat for the daily churn window (this can take a little while)...'

$sinceArg = $startDt.ToString('yyyy-MM-dd')
$untilArg = $endDt.AddDays(1).ToString('yyyy-MM-dd')

# Record separator (0x1F) used to split git's OUTPUT below.
#
# Note the format string asks git for the separator via its own '%x1f' placeholder rather
# than interpolating a literal 0x1F from PowerShell. Two portability traps otherwise:
#   1. "`u{1F}" is a PowerShell 7+ escape; Windows PowerShell 5.1 renders it literally as
#      "u{1F}", which git parses as a bogus revision ("fatal: ambiguous argument 'u{1F}'").
#   2. Even a real [char]0x1F gets mangled when PowerShell 5.1 quotes native-command
#      arguments, splitting the one --pretty argument apart ("ambiguous argument '?'").
# Keeping the argument pure ASCII and letting git emit the byte avoids both on either shell.
$RS = [string][char]0x1F
$churnRaw = Invoke-Git @(
    'log', 'HEAD',
    "--since=$sinceArg",
    "--until=$untilArg",
    '--no-renames', '--numstat',
    '--date=format-local:%Y-%m-%d',
    '--pretty=format:@@C@@%x1f%H%x1f%ad%x1f%an <%ae>'
)

# Per-day accumulators
$dayStats = @{}
function Get-DayBucket {
    param([string]$Date)
    if (-not $dayStats.ContainsKey($Date)) {
        $dayStats[$Date] = [ordered]@{
            Commits      = 0
            FilesTouched = New-Object 'System.Collections.Generic.HashSet[string]'
            LocAdd       = 0L
            LocDel       = 0L
            SrcAdd       = 0L; SrcDel = 0L
            TestAdd      = 0L; TestDel = 0L
            DocsAdd      = 0L; DocsDel = 0L
            Authors      = New-Object 'System.Collections.Generic.HashSet[string]'
        }
    }
    return $dayStats[$Date]
}

# Per-day, per-app and per-day, per-platform-layer accumulators (see the
# Get-AppBucket / Get-PlatformBucket classifiers above). Keyed by "date|bucket".
$appDayStats = @{}
$platformDayStats = @{}
function Get-BucketDayEntry {
    param([hashtable]$Map, [string]$Date, [string]$Bucket)
    $key = "$Date|$Bucket"
    if (-not $Map.ContainsKey($key)) {
        $Map[$key] = [ordered]@{
            Date = $Date; Bucket = $Bucket
            Commits = 0
            FilesTouched = New-Object 'System.Collections.Generic.HashSet[string]'
            LocAdd = 0L; LocDel = 0L
        }
    }
    return $Map[$key]
}

$currentDate = $null
$currentBucket = $null
$currentCommitTouchedApps = New-Object 'System.Collections.Generic.HashSet[string]'
$currentCommitTouchedPlatforms = New-Object 'System.Collections.Generic.HashSet[string]'
$lines = $churnRaw -split "`n"
foreach ($line in $lines) {
    if ($line.StartsWith('@@C@@')) {
        $parts = $line.Substring(5).Split($RS)
        # parts[0] empty (leading RS), [1]=sha [2]=date [3]=author
        $date = $parts[2]
        $author = $parts[3]
        $currentDate = $date
        $currentBucket = Get-DayBucket $date
        $currentBucket.Commits++
        [void]$currentBucket.Authors.Add($author)
        $currentCommitTouchedApps = New-Object 'System.Collections.Generic.HashSet[string]'
        $currentCommitTouchedPlatforms = New-Object 'System.Collections.Generic.HashSet[string]'
        continue
    }
    if ($line.Trim() -eq '' -or $null -eq $currentBucket) { continue }
    # numstat line: added<TAB>deleted<TAB>path  (added/deleted may be '-' for binary)
    $cols = $line -split "`t"
    if ($cols.Count -lt 3) { continue }
    $addStr = $cols[0]; $delStr = $cols[1]; $path = $cols[2]
    $add = 0L; $del = 0L
    [void][long]::TryParse($addStr, [ref]$add)
    [void][long]::TryParse($delStr, [ref]$del)
    [void]$currentBucket.FilesTouched.Add($path)
    $currentBucket.LocAdd += $add
    $currentBucket.LocDel += $del
    if ($path -like '*.cs') {
        if (Test-IsTestPath $path) {
            $currentBucket.TestAdd += $add; $currentBucket.TestDel += $del
        } else {
            $currentBucket.SrcAdd += $add; $currentBucket.SrcDel += $del
        }
    } elseif ($path -like '*.md') {
        $currentBucket.DocsAdd += $add; $currentBucket.DocsDel += $del
    }

    $app = Get-AppBucket $path
    $appEntry = Get-BucketDayEntry $appDayStats $currentDate $app
    [void]$appEntry.FilesTouched.Add($path)
    $appEntry.LocAdd += $add; $appEntry.LocDel += $del
    if ($currentCommitTouchedApps.Add($app)) { $appEntry.Commits++ }

    $platform = Get-PlatformBucket $path
    $platformEntry = Get-BucketDayEntry $platformDayStats $currentDate $platform
    [void]$platformEntry.FilesTouched.Add($path)
    $platformEntry.LocAdd += $add; $platformEntry.LocDel += $del
    if ($currentCommitTouchedPlatforms.Add($platform)) { $platformEntry.Commits++ }
}

Write-Progress2 "Daily churn parsed: $($dayStats.Count) active day(s) in window."
Write-Progress2 "App/platform buckets parsed: $($appDayStats.Count) app-day entries, $($platformDayStats.Count) platform-day entries."

# ---------------------------------------------------------------------------
# Anthropic (Claude Code) token extraction - this machine
# ---------------------------------------------------------------------------

# Per-day, per-provider token accumulator shared by both providers and later merged
# with other machines' JSON files.
function New-ProviderDayBucket {
    [ordered]@{
        Files    = New-Object 'System.Collections.Generic.HashSet[string]'
        Sessions = New-Object 'System.Collections.Generic.HashSet[string]'
        Events   = 0L
        Bytes    = 0L
        Input    = 0L
        CachedInput = 0L
        CacheWrite  = 0L
        CacheRead   = 0L
        Output   = 0L
        Reasoning = 0L
        # MEASURED per-app / per-platform token breakdown (as opposed to the "Estimated Token
        # Allocation" section below, which spreads combined daily totals by git-churn share
        # because it assumes session logs carry no file-attribution signal). They do, in fact,
        # carry one: Anthropic tool_use blocks record an Edit/Write/Read file_path, and Codex
        # patch_apply_end events record the changed file(s) - both co-occur with (or closely
        # precede) their usage/token_count events. Each usage event is attributed to whichever
        # app/platform its session was most recently editing at that point ("sticky" state - see
        # the scan loops below), so a session that edits multiple apps in sequence splits its
        # tokens across buckets by when each edit happened, not evenly/proportionally. Every
        # event is attributed to exactly one App and one Platform, so summing either dictionary
        # reproduces the provider's top-level totals above exactly.
        Apps      = [ordered]@{}
        Platforms = [ordered]@{}
    }
}

function New-TokenSubBucket {
    [ordered]@{
        Events      = 0L
        Input       = 0L
        CachedInput = 0L
        CacheWrite  = 0L
        CacheRead   = 0L
        Output      = 0L
        Reasoning   = 0L
    }
}

# Adds one usage event's token counts into $Dict[$Key] (creating the sub-bucket on first use).
# $Dict is an [ordered]@{} living on a New-ProviderDayBucket's .Apps or .Platforms property.
function Add-ClassifiedTokens {
    param(
        [System.Collections.Specialized.OrderedDictionary]$Dict,
        [string]$Key,
        [int64]$InTok, [int64]$Cached, [int64]$CacheW, [int64]$CacheR, [int64]$Out, [int64]$Reason
    )
    if (-not $Dict.Contains($Key)) { $Dict[$Key] = New-TokenSubBucket }
    $b = $Dict[$Key]
    $b.Events += 1
    $b.Input += $InTok
    $b.CachedInput += $Cached
    $b.CacheWrite += $CacheW
    $b.CacheRead += $CacheR
    $b.Output += $Out
    $b.Reasoning += $Reason
}

# Converts an absolute file path (which may point inside a git worktree, e.g.
# .worktrees/<name>/freew/Foo.cs - a large share of this repo's work happens in secondary
# worktrees) into a path relative to the MAIN worktree root, so the Get-AppBucket /
# Get-PlatformBucket classifiers above (which match src/, freew/, freep/, shared/ etc.
# anchored at the start of the path) work the same regardless of which worktree produced it.
function ConvertTo-RepoRelativePath {
    param([string]$Path)
    if (-not $Path) { return $Path }
    $p = $Path -replace '\\', '/'
    $root = ($RepoRoot -replace '\\', '/').TrimEnd('/')
    if ($p.Length -ge $root.Length -and $p.Substring(0, $root.Length).ToLowerInvariant() -eq $root.ToLowerInvariant()) {
        $p = $p.Substring($root.Length).TrimStart('/')
    }
    $p = $p -replace '(?i)^\.worktrees/[^/]+/', ''
    return $p
}

# Thin adapters onto the existing Get-AppBucket / Get-PlatformBucket classifiers (defined above,
# used by the git-churn buckets) so the measured token breakdown uses the exact same app/platform
# taxonomy as the churn-based one - one set of buckets, two independent ways of populating them.
function Get-AppFromPath {
    param([string]$RelPath)
    if (-not $RelPath) { return 'Docs/Tooling/Other' }
    return Get-AppBucket $RelPath
}

function Get-PlatformFromPath {
    param([string]$RelPath)
    if (-not $RelPath) { return 'Non-code' }
    return Get-PlatformBucket $RelPath
}

$anthropicDaily = @{}   # date -> bucket
$anthropicFileBytesAttributedDates = @{} # file -> set of dates already charged bytes

if (-not $SkipAnthropic) {
    Write-Progress2 'Scanning Claude Code (Anthropic) project logs for FreeX...'
    $claudeProjectsRoot = Join-Path $env:USERPROFILE '.claude\projects'
    $claudeEventCount = 0
    $claudeFileCount = 0
    if (Test-Path $claudeProjectsRoot) {
        $freexDirs = Get-ChildItem -LiteralPath $claudeProjectsRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '(?i)freex' }
        foreach ($dir in $freexDirs) {
            $jsonlFiles = Get-ChildItem -LiteralPath $dir.FullName -Filter '*.jsonl' -File -Recurse -ErrorAction SilentlyContinue
            foreach ($f in $jsonlFiles) {
                $claudeFileCount++
                $sessionId = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
                $seenRequestIds = New-Object 'System.Collections.Generic.HashSet[string]'
                # Sticky app/platform classification for this session: updated whenever an
                # Edit/Write/Read tool_use with a file_path is seen, carried forward to
                # subsequent usage events (e.g. plain-text replies) that have no file signal
                # of their own. Reset per session file so classification never leaks across
                # unrelated sessions.
                $lastApp = 'Docs/Tooling/Other'
                $lastPlatform = 'Non-code'
                $reader = $null
                try {
                    $reader = New-Object System.IO.StreamReader($f.FullName)
                    $lineNo = 0
                    while ($null -ne ($line = $reader.ReadLine())) {
                        $lineNo++
                        if ($line.IndexOf('"usage"') -lt 0) { continue }
                        try { $obj = $line | ConvertFrom-Json -ErrorAction Stop } catch { continue }
                        $usage = $obj.message.usage
                        if (-not $usage) { continue }
                        $ts = $obj.timestamp
                        if (-not $ts) { continue }
                        try {
                            $dt = [datetime]::Parse($ts, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)
                        } catch { continue }
                        $localDate = $dt.ToLocalTime().ToString('yyyy-MM-dd')

                        $reqId = $obj.requestId
                        $dedupKey = if ($reqId) { "$reqId" } else { "$($f.FullName)|$lineNo" }
                        if (-not $seenRequestIds.Add($dedupKey)) { continue }

                        if ($localDate -lt $StartDate -or $localDate -gt $EndDate) { continue }

                        # Update sticky classification from any Edit/Write/Read tool_use block
                        # on THIS same message (usage and the tool call it elicited are on the
                        # same assistant-turn message, so no separate scan pass is needed).
                        $content = $obj.message.content
                        if ($content -is [System.Array]) {
                            foreach ($blk in $content) {
                                if ($blk.type -ne 'tool_use') { continue }
                                if ($blk.name -notin @('Edit', 'Write', 'Read')) { continue }
                                $fp = $blk.input.file_path
                                if (-not $fp) { continue }
                                $rel = ConvertTo-RepoRelativePath $fp
                                $lastApp = Get-AppFromPath $rel
                                $lastPlatform = Get-PlatformFromPath $rel
                            }
                        }

                        $bucket = $anthropicDaily[$localDate]
                        if (-not $bucket) { $bucket = New-ProviderDayBucket; $anthropicDaily[$localDate] = $bucket }

                        [void]$bucket.Files.Add($f.FullName)
                        [void]$bucket.Sessions.Add($sessionId)
                        $bucket.Events++
                        $claudeEventCount++

                        $inTok = [int64]($usage.input_tokens); if (-not $usage.input_tokens) { $inTok = 0 }
                        $cw = [int64]($usage.cache_creation_input_tokens); if (-not $usage.cache_creation_input_tokens) { $cw = 0 }
                        $cr = [int64]($usage.cache_read_input_tokens); if (-not $usage.cache_read_input_tokens) { $cr = 0 }
                        $out = [int64]($usage.output_tokens); if (-not $usage.output_tokens) { $out = 0 }

                        $bucket.Input += $inTok
                        $bucket.CacheWrite += $cw
                        $bucket.CacheRead += $cr
                        $bucket.Output += $out

                        Add-ClassifiedTokens -Dict $bucket.Apps -Key $lastApp -InTok $inTok -Cached 0 -CacheW $cw -CacheR $cr -Out $out -Reason 0
                        Add-ClassifiedTokens -Dict $bucket.Platforms -Key $lastPlatform -InTok $inTok -Cached 0 -CacheW $cw -CacheR $cr -Out $out -Reason 0

                        # Attribute the file's byte size once per date it contributes to.
                        $fileDateKey = "$($f.FullName)|$localDate"
                        if (-not $anthropicFileBytesAttributedDates.ContainsKey($fileDateKey)) {
                            $anthropicFileBytesAttributedDates[$fileDateKey] = $true
                            $bucket.Bytes += $f.Length
                        }
                    }
                } catch {
                    Write-Progress2 "  (warn) failed reading $($f.FullName): $($_.Exception.Message)"
                } finally {
                    if ($reader) { $reader.Dispose() }
                }
            }
            if ($jsonlFiles.Count -gt 0) {
                Write-Progress2 "  $($dir.Name): $($jsonlFiles.Count) jsonl file(s) scanned"
            }
        }
    } else {
        Write-Progress2 "  (no Claude projects directory found at $claudeProjectsRoot)"
    }
    Write-Progress2 "Anthropic: $claudeFileCount file(s), $claudeEventCount usage event(s) attributed in window."
}

# ---------------------------------------------------------------------------
# OpenAI / Codex token extraction - this machine (best-effort, jsonl sessions only)
# ---------------------------------------------------------------------------

$openaiDaily = @{}
$openaiFileBytesAttributedDates = @{}
$codexNote = $null
$codexTotalJsonlObserved = 0

if (-not $SkipCodex) {
    Write-Progress2 'Scanning Codex (OpenAI) session logs for FreeX...'
    $codexRoot = Join-Path $env:USERPROFILE '.codex'
    $codexSessionDirs = @()
    foreach ($sub in @('sessions', 'archived_sessions')) {
        $p = Join-Path $codexRoot $sub
        if (Test-Path $p) { $codexSessionDirs += $p }
    }
    if ($codexSessionDirs.Count -eq 0) {
        $codexNote = "Codex token extraction not implemented / needs schema: no ~/.codex/sessions or ~/.codex/archived_sessions directory found on this machine."
        Write-Progress2 "  (no codex session directories found)"
    } else {
        $codexFileCount = 0
        $codexMatchedFileCount = 0
        $codexEventCount = 0
        $scanned = 0
        foreach ($dir in $codexSessionDirs) {
            $jsonlFiles = Get-ChildItem -LiteralPath $dir -Filter '*.jsonl' -File -Recurse -ErrorAction SilentlyContinue
            foreach ($f in $jsonlFiles) {
                $codexTotalJsonlObserved++
                $codexFileCount++
                $scanned++
                if ($scanned % 500 -eq 0) { Write-Progress2 "  ...scanned $scanned codex session files" }

                $reader = $null
                $isFreeX = $false
                $sessionId = [System.IO.Path]::GetFileNameWithoutExtension($f.Name)
                # Sticky app/platform classification for this session (see the Anthropic loop
                # above for the rationale). Updated from patch_apply_end.changes file paths;
                # carried forward to subsequent token_count events until the next patch.
                $lastApp = 'Docs/Tooling/Other'
                $lastPlatform = 'Non-code'
                try {
                    $reader = New-Object System.IO.StreamReader($f.FullName)
                    $lineNo = 0
                    $checkedMeta = $false
                    while ($null -ne ($line = $reader.ReadLine())) {
                        $lineNo++
                        if (-not $checkedMeta) {
                            if ($line.IndexOf('"cwd"') -ge 0) {
                                $checkedMeta = $true
                                if ($line -match '(?i)freex') { $isFreeX = $true }
                                if (-not $isFreeX) { break } # not a FreeX session; stop reading this file
                            }
                            if ($lineNo -gt 5 -and -not $checkedMeta) {
                                # no session_meta with cwd found in first few lines; give up on this file
                                break
                            }
                        }
                        if (-not $isFreeX) { continue }
                        $hasTokenCount = $line.IndexOf('"token_count"') -ge 0
                        $hasPatchApply = $line.IndexOf('"patch_apply_end"') -ge 0
                        if (-not $hasTokenCount -and -not $hasPatchApply) { continue }
                        try { $obj = $line | ConvertFrom-Json -ErrorAction Stop } catch { continue }
                        if ($obj.type -ne 'event_msg') { continue }
                        $payload = $obj.payload
                        if (-not $payload) { continue }

                        if ($hasPatchApply -and $payload.type -eq 'patch_apply_end') {
                            # $payload.changes is a PSCustomObject whose PROPERTY NAMES are the
                            # absolute file paths touched by this patch. Classify each and take
                            # the most common app/platform (ties broken by first-seen order) as
                            # the new sticky state - a patch usually touches one or a few closely
                            # related files, so this rarely needs to break a real tie.
                            $changes = $payload.changes
                            if ($changes) {
                                $appCounts = [ordered]@{}
                                $platCounts = [ordered]@{}
                                foreach ($prop in $changes.PSObject.Properties) {
                                    $rel = ConvertTo-RepoRelativePath $prop.Name
                                    $app = Get-AppFromPath $rel
                                    $plat = Get-PlatformFromPath $rel
                                    if (-not $appCounts.Contains($app)) { $appCounts[$app] = 0 }
                                    $appCounts[$app] = $appCounts[$app] + 1
                                    if (-not $platCounts.Contains($plat)) { $platCounts[$plat] = 0 }
                                    $platCounts[$plat] = $platCounts[$plat] + 1
                                }
                                $bestApp = $null; $bestAppCount = -1
                                foreach ($k in $appCounts.Keys) { if ($appCounts[$k] -gt $bestAppCount) { $bestAppCount = $appCounts[$k]; $bestApp = $k } }
                                $bestPlat = $null; $bestPlatCount = -1
                                foreach ($k in $platCounts.Keys) { if ($platCounts[$k] -gt $bestPlatCount) { $bestPlatCount = $platCounts[$k]; $bestPlat = $k } }
                                if ($bestApp) { $lastApp = $bestApp }
                                if ($bestPlat) { $lastPlatform = $bestPlat }
                            }
                            continue
                        }

                        if (-not $hasTokenCount -or $payload.type -ne 'token_count') { continue }
                        $usage = $payload.info.last_token_usage
                        if (-not $usage) { continue }
                        $ts = $obj.timestamp
                        if (-not $ts) { continue }
                        try {
                            $dt = [datetime]::Parse($ts, [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)
                        } catch { continue }
                        $localDate = $dt.ToLocalTime().ToString('yyyy-MM-dd')
                        if ($localDate -lt $StartDate -or $localDate -gt $EndDate) { continue }

                        $bucket = $openaiDaily[$localDate]
                        if (-not $bucket) { $bucket = New-ProviderDayBucket; $openaiDaily[$localDate] = $bucket }

                        [void]$bucket.Files.Add($f.FullName)
                        [void]$bucket.Sessions.Add($sessionId)
                        $bucket.Events++
                        $codexEventCount++

                        $inTok = [int64]($usage.input_tokens); if (-not $usage.input_tokens) { $inTok = 0 }
                        $cached = [int64]($usage.cached_input_tokens); if (-not $usage.cached_input_tokens) { $cached = 0 }
                        $out = [int64]($usage.output_tokens); if (-not $usage.output_tokens) { $out = 0 }
                        $reasoning = [int64]($usage.reasoning_output_tokens); if (-not $usage.reasoning_output_tokens) { $reasoning = 0 }

                        $bucket.Input += $inTok
                        $bucket.CachedInput += $cached
                        $bucket.Output += $out
                        $bucket.Reasoning += $reasoning

                        Add-ClassifiedTokens -Dict $bucket.Apps -Key $lastApp -InTok $inTok -Cached $cached -CacheW 0 -CacheR 0 -Out $out -Reason $reasoning
                        Add-ClassifiedTokens -Dict $bucket.Platforms -Key $lastPlatform -InTok $inTok -Cached $cached -CacheW 0 -CacheR 0 -Out $out -Reason $reasoning

                        $fileDateKey = "$($f.FullName)|$localDate"
                        if (-not $openaiFileBytesAttributedDates.ContainsKey($fileDateKey)) {
                            $openaiFileBytesAttributedDates[$fileDateKey] = $true
                            $bucket.Bytes += $f.Length
                        }
                    }
                    if ($isFreeX) { $codexMatchedFileCount++ }
                } catch {
                    Write-Progress2 "  (warn) failed reading $($f.FullName): $($_.Exception.Message)"
                } finally {
                    if ($reader) { $reader.Dispose() }
                }
            }
        }
        Write-Progress2 "Codex: $codexFileCount file(s) scanned, $codexMatchedFileCount FreeX-scoped, $codexEventCount usage event(s) attributed in window."
        Write-Progress2 "Codex sqlite logs (logs_2.sqlite etc.) were NOT parsed: no documented/stable schema for per-day token usage was available without heavy reverse-engineering, per the 'do not guess' constraint. Only the jsonl session/archived_session logs (which do expose a clear payload.info.last_token_usage field) were used."
        $codexNote = "Codex jsonl sessions were extracted via payload.info.last_token_usage on event_msg/token_count lines, filtered to sessions whose recorded cwd contains 'FreeX'. Codex's sqlite logs (logs_2.sqlite etc.) were NOT parsed (no stable documented per-day usage schema without heavy reverse-engineering) - if the jsonl sessions directories are ever pruned/rotated, coverage for older dates could be incomplete."
    }
}

# ---------------------------------------------------------------------------
# Write this machine's per-machine intermediate token JSON
# ---------------------------------------------------------------------------

function ConvertTo-JsonSubBucketMap {
    param([System.Collections.Specialized.OrderedDictionary]$SubDict)
    $out = [ordered]@{}
    if (-not $SubDict) { return $out }
    foreach ($k in ($SubDict.Keys | Sort-Object)) {
        $sb = $SubDict[$k]
        $out[$k] = [ordered]@{
            events      = $sb.Events
            input       = $sb.Input
            cachedInput = $sb.CachedInput
            cacheWrite  = $sb.CacheWrite
            cacheRead   = $sb.CacheRead
            output      = $sb.Output
            reasoning   = $sb.Reasoning
        }
    }
    return $out
}

function ConvertTo-JsonDayMap {
    param([hashtable]$DayMap)
    $out = [ordered]@{}
    foreach ($k in ($DayMap.Keys | Sort-Object)) {
        $b = $DayMap[$k]
        $out[$k] = [ordered]@{
            files       = $b.Files.Count
            sessions    = $b.Sessions.Count
            events      = $b.Events
            bytes       = $b.Bytes
            input       = $b.Input
            cachedInput = $b.CachedInput
            cacheWrite  = $b.CacheWrite
            cacheRead   = $b.CacheRead
            output      = $b.Output
            reasoning   = $b.Reasoning
            apps        = ConvertTo-JsonSubBucketMap $b.Apps
            platforms   = ConvertTo-JsonSubBucketMap $b.Platforms
        }
    }
    return $out
}

$machineJsonPath = Join-Path $OutputDir "project-history-tokens-$MachineId.json"

# If a provider's scan was skipped this run (-SkipAnthropic / -SkipCodex), do NOT overwrite
# that provider's section of this machine's intermediate JSON with empty data - that would
# destroy a previous (possibly expensive, hours-long) extraction. Instead, preserve whatever
# is already on disk for the skipped provider(s) and only replace the section(s) that were
# actually (re)scanned this run.
$existingMachineData = $null
if (Test-Path -LiteralPath $machineJsonPath) {
    try {
        $existingMachineData = Get-Content -LiteralPath $machineJsonPath -Raw | ConvertFrom-Json -ErrorAction Stop
    } catch {
        Write-Progress2 "  (warn) failed reading existing $machineJsonPath, will not attempt to preserve skipped-provider data from it: $($_.Exception.Message)"
        $existingMachineData = $null
    }
}

if ($SkipAnthropic -and $existingMachineData -and $existingMachineData.PSObject.Properties['anthropic']) {
    $anthropicOut = $existingMachineData.anthropic
    Write-Progress2 '  Anthropic scan skipped this run - preserving existing anthropic section of this machine''s JSON.'
} else {
    $anthropicOut = ConvertTo-JsonDayMap $anthropicDaily
}

if ($SkipCodex -and $existingMachineData -and $existingMachineData.PSObject.Properties['openai']) {
    $openaiOut = $existingMachineData.openai
    $codexNoteOut = if ($existingMachineData.PSObject.Properties['codexNote']) { $existingMachineData.codexNote } else { $codexNote }
    Write-Progress2 '  Codex scan skipped this run - preserving existing openai section of this machine''s JSON.'
} else {
    $openaiOut = ConvertTo-JsonDayMap $openaiDaily
    $codexNoteOut = $codexNote
}

$machinePayload = [ordered]@{
    machineId   = $MachineId
    generatedAt = $generatedAt.ToString('o')
    startDate   = $StartDate
    endDate     = $EndDate
    anthropic   = $anthropicOut
    openai      = $openaiOut
    codexNote   = $codexNoteOut
}
Save-Utf8NoBom -Path $machineJsonPath -Content ($machinePayload | ConvertTo-Json -Depth 8)
Write-Progress2 "Wrote this machine's token intermediate: $machineJsonPath"

# ---------------------------------------------------------------------------
# Aggregate ALL project-history-tokens-*.json present in -OutputDir
# ---------------------------------------------------------------------------

Write-Progress2 'Aggregating project-history-tokens-*.json across all machines present in output dir...'

# aggDaily[date][provider] = ordered hashtable of summed fields, plus machine set
$aggDaily = @{}
$machinesSeen = New-Object 'System.Collections.Generic.HashSet[string]'
$codexNotesSeen = New-Object 'System.Collections.Generic.List[string]'

$machineFiles = Get-ChildItem -LiteralPath $OutputDir -Filter 'project-history-tokens-*.json' -File -ErrorAction SilentlyContinue
foreach ($mf in $machineFiles) {
    try {
        $data = Get-Content -LiteralPath $mf.FullName -Raw | ConvertFrom-Json
    } catch {
        Write-Progress2 "  (warn) skipping unreadable $($mf.Name): $($_.Exception.Message)"
        continue
    }
    [void]$machinesSeen.Add([string]$data.machineId)
    if ($data.codexNote) { $codexNotesSeen.Add("$($data.machineId): $($data.codexNote)") }

    foreach ($provider in @('anthropic', 'openai')) {
        $providerData = $data.$provider
        if (-not $providerData) { continue }
        foreach ($dateProp in $providerData.PSObject.Properties) {
            $date = $dateProp.Name
            $row = $dateProp.Value
            if (-not $aggDaily.ContainsKey($date)) { $aggDaily[$date] = @{} }
            if (-not $aggDaily[$date].ContainsKey($provider)) {
                $aggDaily[$date][$provider] = [ordered]@{
                    Files = 0; Sessions = 0; Events = 0L; Bytes = 0L
                    Input = 0L; CachedInput = 0L; CacheWrite = 0L; CacheRead = 0L
                    Output = 0L; Reasoning = 0L
                    Apps = [ordered]@{}; Platforms = [ordered]@{}
                }
            }
            $acc = $aggDaily[$date][$provider]
            $acc.Files += [int]$row.files
            $acc.Sessions += [int]$row.sessions
            $acc.Events += [int64]$row.events
            $acc.Bytes += [int64]$row.bytes
            $acc.Input += [int64]$row.input
            $acc.CachedInput += [int64]$row.cachedInput
            $acc.CacheWrite += [int64]$row.cacheWrite
            $acc.CacheRead += [int64]$row.cacheRead
            $acc.Output += [int64]$row.output
            $acc.Reasoning += [int64]$row.reasoning

            # apps/platforms are optional: older per-machine JSON files (written before this
            # measured breakdown existed) simply won't have them, and iterating PSObject
            # .Properties on a $null value is a no-op, not an error.
            foreach ($breakdownName in @('apps', 'platforms')) {
                $breakdownData = $row.$breakdownName
                if (-not $breakdownData) { continue }
                $targetDict = if ($breakdownName -eq 'apps') { $acc.Apps } else { $acc.Platforms }
                foreach ($keyProp in $breakdownData.PSObject.Properties) {
                    $keyName = $keyProp.Name
                    $keyRow = $keyProp.Value
                    if (-not $targetDict.Contains($keyName)) { $targetDict[$keyName] = New-TokenSubBucket }
                    $sub = $targetDict[$keyName]
                    $sub.Events += [int64]$keyRow.events
                    $sub.Input += [int64]$keyRow.input
                    $sub.CachedInput += [int64]$keyRow.cachedInput
                    $sub.CacheWrite += [int64]$keyRow.cacheWrite
                    $sub.CacheRead += [int64]$keyRow.cacheRead
                    $sub.Output += [int64]$keyRow.output
                    $sub.Reasoning += [int64]$keyRow.reasoning
                }
            }
        }
    }
}
Write-Progress2 "Aggregated token data from $($machineFiles.Count) machine JSON file(s): $($machinesSeen -join ', ')"

# ---------------------------------------------------------------------------
# Build Daily Provider Token Usage rows + billable-equivalent totals
# ---------------------------------------------------------------------------

function Get-RawTokens {
    param([string]$Provider, $Acc)
    if ($Provider -eq 'anthropic') {
        return $Acc.Input + $Acc.CachedInput + $Acc.CacheWrite + $Acc.CacheRead + $Acc.Output + $Acc.Reasoning
    } else {
        # OpenAI/Codex: input_tokens already includes cached_input_tokens as a subset (not additive).
        return $Acc.Input + $Acc.Output + $Acc.Reasoning
    }
}

function Get-BillableEquivalent {
    param([string]$Provider, $Acc)
    if ($Provider -eq 'anthropic') {
        return $Acc.Input + ($Acc.CacheWrite * 1.25) + ($Acc.CacheRead * 0.1) + $Acc.Output + $Acc.Reasoning
    } else {
        $uncached = $Acc.Input - $Acc.CachedInput
        if ($uncached -lt 0) { $uncached = 0 }
        return $uncached + ($Acc.CachedInput * 0.5) + $Acc.Output + $Acc.Reasoning
    }
}

$allDates = @()
$d = $startDt
while ($d -le $endDt) { $allDates += $d.ToString('yyyy-MM-dd'); $d = $d.AddDays(1) }

$tokenRows = New-Object System.Collections.Generic.List[object]
$totalBytes = 0L; $totalRaw = 0.0; $totalBillable = 0.0
$totalFilesSet = New-Object 'System.Collections.Generic.HashSet[string]'
foreach ($date in $allDates) {
    if (-not $aggDaily.ContainsKey($date)) { continue }
    foreach ($provider in @('anthropic', 'openai')) {
        if (-not $aggDaily[$date].ContainsKey($provider)) { continue }
        $acc = $aggDaily[$date][$provider]
        $raw = Get-RawTokens $provider $acc
        $billable = Get-BillableEquivalent $provider $acc
        $tokenRows.Add([ordered]@{
            Date = $date; Provider = $provider
            Files = $acc.Files; Sessions = $acc.Sessions; Events = $acc.Events
            Bytes = $acc.Bytes
            Input = $acc.Input; CachedInput = $acc.CachedInput
            CacheWrite = $acc.CacheWrite; CacheRead = $acc.CacheRead
            Output = $acc.Output; Reasoning = $acc.Reasoning
            Raw = $raw; Billable = $billable
        })
        $totalBytes += $acc.Bytes
        $totalRaw += $raw
        $totalBillable += $billable
    }
}

# Per-day totals (both providers) for the churn table's OpenAI/Anthropic Tokens + Bytes columns
$dayTokenTotals = @{}
foreach ($row in $tokenRows) {
    if (-not $dayTokenTotals.ContainsKey($row.Date)) {
        $dayTokenTotals[$row.Date] = [ordered]@{ Bytes = 0L; OpenAI = 0.0; Anthropic = 0.0 }
    }
    $dayTokenTotals[$row.Date].Bytes += $row.Bytes
    if ($row.Provider -eq 'openai') { $dayTokenTotals[$row.Date].OpenAI += $row.Raw }
    if ($row.Provider -eq 'anthropic') { $dayTokenTotals[$row.Date].Anthropic += $row.Raw }
}

# ---------------------------------------------------------------------------
# Window-total Provider x App and Provider x Platform MEASURED token totals, summed across
# every day/provider in $aggDaily (i.e. across every machine's contributed JSON that has the
# apps/platforms breakdown). Kept as window totals rather than per-day rows - a per-day x
# per-provider x per-app x per-platform table would be enormous and not materially more useful
# for the "where did the tokens go" question this breakdown exists to answer.
# ---------------------------------------------------------------------------
$appTotals = [ordered]@{}      # provider -> app -> {Events;Raw;Billable}
$platformTotals = [ordered]@{} # provider -> platform -> {Events;Raw;Billable}
foreach ($date in $aggDaily.Keys) {
    foreach ($provider in @('anthropic', 'openai')) {
        if (-not $aggDaily[$date].ContainsKey($provider)) { continue }
        $acc = $aggDaily[$date][$provider]
        if (-not $appTotals.Contains($provider)) { $appTotals[$provider] = [ordered]@{} }
        if (-not $platformTotals.Contains($provider)) { $platformTotals[$provider] = [ordered]@{} }

        foreach ($app in $acc.Apps.Keys) {
            $sub = $acc.Apps[$app]
            if (-not $appTotals[$provider].Contains($app)) {
                $appTotals[$provider][$app] = [ordered]@{ Events = 0L; Raw = 0.0; Billable = 0.0 }
            }
            $appTotals[$provider][$app].Events += $sub.Events
            $appTotals[$provider][$app].Raw += (Get-RawTokens $provider $sub)
            $appTotals[$provider][$app].Billable += (Get-BillableEquivalent $provider $sub)
        }
        foreach ($plat in $acc.Platforms.Keys) {
            $sub = $acc.Platforms[$plat]
            if (-not $platformTotals[$provider].Contains($plat)) {
                $platformTotals[$provider][$plat] = [ordered]@{ Events = 0L; Raw = 0.0; Billable = 0.0 }
            }
            $platformTotals[$provider][$plat].Events += $sub.Events
            $platformTotals[$provider][$plat].Raw += (Get-RawTokens $provider $sub)
            $platformTotals[$provider][$plat].Billable += (Get-BillableEquivalent $provider $sub)
        }
    }
}

# ---------------------------------------------------------------------------
# Render docs/history/build-history-metrics.md
# ---------------------------------------------------------------------------

Write-Progress2 'Rendering build-history-metrics.md...'

$sb = New-Object System.Text.StringBuilder
[void]$sb.AppendLine('# Project Build History Metrics')
[void]$sb.AppendLine()
[void]$sb.AppendLine("Generated: $($generatedAt.ToString('yyyy-MM-dd HH:mm zzz'))")
[void]$sb.AppendLine("Repository: $originUrl")
[void]$sb.AppendLine("Baseline ref: HEAD at ``$headShaShort`` (``$headSha``)")
[void]$sb.AppendLine("History window: $StartDate through $EndDate")
[void]$sb.AppendLine()
[void]$sb.AppendLine('## Scope And Caveats')
[void]$sb.AppendLine()
[void]$sb.AppendLine('- This doc is produced by the committable, repeatable extractor `tools/Build-ProjectHistoryMetrics.ps1`, not a one-off local script. Re-run it to refresh.')
[void]$sb.AppendLine('- Daily build rows are Git numstat churn for all commits reachable from HEAD (not just first-parent) whose commit date falls in the window, bucketed by that commit date in this machine''s local timezone. A no-rename numstat pass is used, so renamed files are represented by their added and removed lines.')
[void]$sb.AppendLine('- Files Changed is the count of *distinct* file paths touched that day (deduplicated across the day''s commits); LoC/Source/Test/Docs +/- are the raw additive churn (not deduplicated), i.e. repeated edits to the same file all count.')
[void]$sb.AppendLine('- Source C# / Test C# is split by path: a `.cs` file is classified as a test file if any path segment is `test`/`tests` (case-insensitive) or the filename ends in `Test(s).cs`; everything else `.cs` is source. Docs +/- covers every tracked `.md` file, not only `docs/`.')
[void]$sb.AppendLine('- Current repository footprint LOC counts are exact for the current checkout (`git ls-files` + line counts). Historical cumulative LOC per day is not computed (would require checking out every daily snapshot).')
$machineNote = if ($machinesSeen.Count -gt 0) { ($machinesSeen | Sort-Object) -join ', ' } else { '(none yet)' }
[void]$sb.AppendLine("- **Token columns currently reflect only the machine(s) that have contributed a `project-history-tokens-<MachineId>.json` file into ``$OutputDir`` so far: $machineNote.** This run's own machine id is ``$MachineId``. Other machines' logs are pending: copy their `project-history-tokens-*.json` (produced by running this same script there) into that directory and re-run to aggregate. The git-derived metrics above and below are complete/authoritative regardless of which machines have reported tokens.")
[void]$sb.AppendLine('- Anthropic (Claude Code) token rows sum `message.usage` fields from every `*.jsonl` transcript (including subagent transcripts) under `~/.claude/projects/*FreeX*`, deduplicated by `requestId` where present. Only numeric usage + timestamp + model were read - no transcript content was inspected or stored.')
[void]$sb.AppendLine('- OpenAI (Codex) token rows sum `payload.info.last_token_usage` from `token_count` events in `~/.codex/sessions/**/*.jsonl` and `~/.codex/archived_sessions/*.jsonl`, filtered to sessions whose recorded `cwd` contains "FreeX". Codex''s sqlite logs (`logs_2.sqlite` etc.) were **not** parsed - no stable, documented per-day usage schema was available there without heavy reverse-engineering, so per the "do not guess" rule they are left out rather than estimated.')
if ($codexNotesSeen.Count -gt 0) {
    foreach ($n in $codexNotesSeen) { [void]$sb.AppendLine("- Codex extraction note ($n)") }
}
[void]$sb.AppendLine('- Raw Tokens for Anthropic = Input + Cached Input + Cache Write + Cache Read + Output + Reasoning (Anthropic cache tokens are billed as distinct additive token types). Raw Tokens for OpenAI = Input + Output + Reasoning (OpenAI''s `input_tokens` already includes `cached_input_tokens` as a discounted subset, so it is not added again; Cached Input is shown for visibility only).')
[void]$sb.AppendLine('- Billable Eq Tokens applies simple cache weighting to make the local logs easier to compare with provider dashboards: OpenAI cached input at 0.5x, Anthropic cache write at 1.25x, Anthropic cache read at 0.1x, and all other input/output/reasoning tokens at 1x. This is an approximation, not an invoice.')
[void]$sb.AppendLine()
[void]$sb.AppendLine('## Current Repository Footprint')
[void]$sb.AppendLine()
[void]$sb.AppendLine("- Registered worktrees: $(Format-N0 $worktreeCount)")
[void]$sb.AppendLine("- Local branches: $(Format-N0 $localBranchCount)")
[void]$sb.AppendLine("- Remote branches: $(Format-N0 $remoteBranchCount)")
[void]$sb.AppendLine("- Tracked files: $(Format-N0 $trackedCount)")
[void]$sb.AppendLine("- Current C# source LOC: $(Format-N0 $srcCsLoc)")
[void]$sb.AppendLine("- Current C# test LOC: $(Format-N0 $testCsLoc)")
[void]$sb.AppendLine("- Current XAML LOC: $(Format-N0 $xamlLoc)")
[void]$sb.AppendLine("- Current docs LOC: $(Format-N0 $docsLoc)")
[void]$sb.AppendLine("- Observed Codex JSONL sessions/logs (this machine, all projects, unfiltered): $(Format-N0 $codexTotalJsonlObserved)")
$claudeTotalFreeXFiles = 0
if (Test-Path (Join-Path $env:USERPROFILE '.claude\projects')) {
    $claudeTotalFreeXFiles = (Get-ChildItem -LiteralPath (Join-Path $env:USERPROFILE '.claude\projects') -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '(?i)freex' } |
        ForEach-Object { Get-ChildItem -LiteralPath $_.FullName -Filter '*.jsonl' -File -Recurse -ErrorAction SilentlyContinue } |
        Measure-Object).Count
}
[void]$sb.AppendLine("- Observed Claude FreeX JSONL sessions/logs (this machine): $(Format-N0 $claudeTotalFreeXFiles)")
[void]$sb.AppendLine("- Provider log bytes attributed (all machines reporting so far): $(Format-N0 $totalBytes)")
[void]$sb.AppendLine("- Observed raw provider tokens (all machines reporting so far): $(Format-N0 $totalRaw)")
[void]$sb.AppendLine("- Provider-style billable-equivalent tokens (all machines reporting so far): $(Format-N0 $totalBillable)")
[void]$sb.AppendLine()
[void]$sb.AppendLine('## Daily Build Churn')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| Date | Commits | Files Changed | LoC +/- | Source C# +/- | Test C# +/- | Docs +/- | Bytes +/- | OpenAI Tokens | Anthropic Tokens | Git Authors |')
[void]$sb.AppendLine('| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |')

$totCommits = 0L; $totFiles = 0L
$totLocAdd = 0L; $totLocDel = 0L
$totSrcAdd = 0L; $totSrcDel = 0L
$totTestAdd = 0L; $totTestDel = 0L
$totDocsAdd = 0L; $totDocsDel = 0L
$totBytes2 = 0L; $totOpenAi = 0.0; $totAnthropic = 0.0
$totAuthorsAllDates = New-Object 'System.Collections.Generic.HashSet[string]'

foreach ($date in $allDates) {
    $b = $dayStats[$date]
    if (-not $b) {
        # no commits that day; still show token/byte activity if present
        $tt = $dayTokenTotals[$date]
        if (-not $tt) { continue }
        [void]$sb.AppendLine("| $date | 0 | 0 | +0 / -0 | +0 / -0 | +0 / -0 | +0 / -0 | +$(Format-N0 $tt.Bytes) / -0 | $(Format-N0 $tt.OpenAI) | $(Format-N0 $tt.Anthropic) | 0 |")
        $totBytes2 += $tt.Bytes; $totOpenAi += $tt.OpenAI; $totAnthropic += $tt.Anthropic
        continue
    }
    $tt = $dayTokenTotals[$date]
    $bytes = if ($tt) { $tt.Bytes } else { 0L }
    $openai = if ($tt) { $tt.OpenAI } else { 0.0 }
    $anthropic = if ($tt) { $tt.Anthropic } else { 0.0 }

    [void]$sb.AppendLine("| $date | $($b.Commits) | $($b.FilesTouched.Count) | +$(Format-N0 $b.LocAdd) / -$(Format-N0 $b.LocDel) | +$(Format-N0 $b.SrcAdd) / -$(Format-N0 $b.SrcDel) | +$(Format-N0 $b.TestAdd) / -$(Format-N0 $b.TestDel) | +$(Format-N0 $b.DocsAdd) / -$(Format-N0 $b.DocsDel) | +$(Format-N0 $bytes) / -0 | $(Format-N0 $openai) | $(Format-N0 $anthropic) | $($b.Authors.Count) |")

    $totCommits += $b.Commits
    $totFiles += $b.FilesTouched.Count
    $totLocAdd += $b.LocAdd; $totLocDel += $b.LocDel
    $totSrcAdd += $b.SrcAdd; $totSrcDel += $b.SrcDel
    $totTestAdd += $b.TestAdd; $totTestDel += $b.TestDel
    $totDocsAdd += $b.DocsAdd; $totDocsDel += $b.DocsDel
    $totBytes2 += $bytes; $totOpenAi += $openai; $totAnthropic += $anthropic
    foreach ($a in $b.Authors) { [void]$totAuthorsAllDates.Add($a) }
}
[void]$sb.AppendLine("| TOTAL | $(Format-N0 $totCommits) | $(Format-N0 $totFiles) | +$(Format-N0 $totLocAdd) / -$(Format-N0 $totLocDel) | +$(Format-N0 $totSrcAdd) / -$(Format-N0 $totSrcDel) | +$(Format-N0 $totTestAdd) / -$(Format-N0 $totTestDel) | +$(Format-N0 $totDocsAdd) / -$(Format-N0 $totDocsDel) | +$(Format-N0 $totBytes2) / -0 | $(Format-N0 $totOpenAi) | $(Format-N0 $totAnthropic) | $($totAuthorsAllDates.Count) |")
[void]$sb.AppendLine()

# ---------------------------------------------------------------------------
# Git Churn By App / By Platform Layer (EXACT partitions of the same numstat
# data used for Daily Build Churn above; see Get-AppBucket / Get-PlatformBucket).
# ---------------------------------------------------------------------------

function Write-BucketChurnSection {
    param(
        [System.Text.StringBuilder]$Sb,
        [string]$Title,
        [string]$BucketColumnHeader,
        [string[]]$BucketOrder,
        [hashtable]$DayEntries,
        [string[]]$ClassifierLines
    )
    [void]$Sb.AppendLine("## $Title")
    [void]$Sb.AppendLine()
    foreach ($cl in $ClassifierLines) { [void]$Sb.AppendLine("- $cl") }
    [void]$Sb.AppendLine('- "Files Changed" and "LoC +/-" are an EXACT partition of the same `git log --numstat` data behind Daily Build Churn above: every changed path is assigned to exactly one bucket, so these two columns sum exactly to the Daily Build Churn TOTAL row (the generator asserts this at build time and warns if it ever drifts).')
    [void]$Sb.AppendLine('- "Commits" counts a commit once per bucket if it touched at least one path in that bucket (a commit touching multiple buckets is counted in each), so it is NOT expected to sum to the Daily Build Churn TOTAL commit count: git suppresses `--numstat` output for merge commits unless `-m`/`-c` is passed, so a merge commit with no line-level diff is tallied in the overall commit total but contributes to zero buckets here.')
    [void]$Sb.AppendLine('- "Files Changed" is the sum of per-day distinct-path counts (matches the Daily Build Churn convention, not a window-wide dedup).')
    [void]$Sb.AppendLine()

    # Summary (whole window)
    $summary = [ordered]@{}
    foreach ($b in $BucketOrder) { $summary[$b] = [ordered]@{ Commits = 0L; Files = 0L; LocAdd = 0L; LocDel = 0L } }
    foreach ($entry in $DayEntries.Values) {
        $s = $summary[$entry.Bucket]
        if (-not $s) { continue }
        $s.Commits += $entry.Commits
        $s.Files += $entry.FilesTouched.Count
        $s.LocAdd += $entry.LocAdd
        $s.LocDel += $entry.LocDel
    }
    [void]$Sb.AppendLine("### $Title - Summary")
    [void]$Sb.AppendLine()
    [void]$Sb.AppendLine("| $BucketColumnHeader | Commits | Files Changed | LoC +/- |")
    [void]$Sb.AppendLine('| --- | ---: | ---: | ---: |')
    $sumCommits = 0L; $sumFiles = 0L; $sumLocAdd = 0L; $sumLocDel = 0L
    foreach ($b in $BucketOrder) {
        $s = $summary[$b]
        [void]$Sb.AppendLine("| $b | $(Format-N0 $s.Commits) | $(Format-N0 $s.Files) | +$(Format-N0 $s.LocAdd) / -$(Format-N0 $s.LocDel) |")
        $sumCommits += $s.Commits; $sumFiles += $s.Files; $sumLocAdd += $s.LocAdd; $sumLocDel += $s.LocDel
    }
    [void]$Sb.AppendLine("| TOTAL | $(Format-N0 $sumCommits) | $(Format-N0 $sumFiles) | +$(Format-N0 $sumLocAdd) / -$(Format-N0 $sumLocDel) |")
    [void]$Sb.AppendLine()

    # Monthly rollup
    $monthly = [ordered]@{} # "yyyy-MM|bucket" -> accumulator
    foreach ($entry in $DayEntries.Values) {
        $month = $entry.Date.Substring(0, 7)
        $key = "$month|$($entry.Bucket)"
        if (-not $monthly.Contains($key)) { $monthly[$key] = [ordered]@{ Month = $month; Bucket = $entry.Bucket; Commits = 0L; Files = 0L; LocAdd = 0L; LocDel = 0L } }
        $m = $monthly[$key]
        $m.Commits += $entry.Commits
        $m.Files += $entry.FilesTouched.Count
        $m.LocAdd += $entry.LocAdd
        $m.LocDel += $entry.LocDel
    }
    [void]$Sb.AppendLine("### $Title - Monthly")
    [void]$Sb.AppendLine()
    [void]$Sb.AppendLine("| Month | $BucketColumnHeader | Commits | Files Changed | LoC +/- |")
    [void]$Sb.AppendLine('| --- | --- | ---: | ---: | ---: |')
    $monthKeys = $monthly.Keys | Sort-Object
    foreach ($key in $monthKeys) {
        $m = $monthly[$key]
        [void]$Sb.AppendLine("| $($m.Month) | $($m.Bucket) | $(Format-N0 $m.Commits) | $(Format-N0 $m.Files) | +$(Format-N0 $m.LocAdd) / -$(Format-N0 $m.LocDel) |")
    }
    [void]$Sb.AppendLine()

    return [ordered]@{ Commits = $sumCommits; Files = $sumFiles; LocAdd = $sumLocAdd; LocDel = $sumLocDel }
}

$appChurnTotals = Write-BucketChurnSection -Sb $sb -Title 'Git Churn By App' -BucketColumnHeader 'App' -BucketOrder $AppBucketOrder -DayEntries $appDayStats -ClassifierLines @(
    'Buckets are assigned by repo path prefix: `FreeX` = `src/**` + `tests/**`; `FreeW` = `freew/**`; `FreeP` = `freep/**`; `Shared` = `shared/**`; `Docs/Tooling/Other` = everything else (`docs/**`, `tools/**`, top-level files, screenshots/fixture/corpus dirs, etc.).'
    '`tests/**` is bucketed under `FreeX` even where it exercises `Shared`/`FreeW`/`FreeP` code, because the shared test projects that live under `tests/` predate the FreeW/FreeP split; see the "By Platform Layer" section below for a platform-aware (not app-aware) view of the same `tests/**` paths.'
)

$platformChurnTotals = Write-BucketChurnSection -Sb $sb -Title 'Git Churn By Platform Layer' -BucketColumnHeader 'Platform Layer' -BucketOrder $PlatformBucketOrder -DayEntries $platformDayStats -ClassifierLines @(
    'The codebase is organized by UI framework, not OS, so "platform" here means UI framework layer: `Windows (WPF)` = any path under `src/**`, `tests/**`, `freew/**`, `freep/**`, or `shared/**` matching `*.App.Host*`, `*.App.UI*`, `*.Wpf*`, or `*Free.Shared.*.Windows*` (e.g. `src/FreeX.App.Host`, `shared/Free.Shared.Ribbon.Wpf`, `shared/Free.Shared.AppServices.Windows`).'
    '`Avalonia (Linux/macOS)` = same code area matching `*.App.Avalonia*`, `*.App.Rendering.Avalonia*`, or `*Free.Shared.*.Avalonia*` (e.g. `freep/FreeP.App.Rendering.Avalonia`, `shared/Free.Shared.Shell.Avalonia`).'
    '`Platform-neutral (core/shared/IO/model)` = everything else under those same four top-level dirs (Core.*, App.Presentation, App.Services, Ribbon.Definitions, IO, Model, Commands, Drawing, Opc, Pdf/Pdf.Skia, etc.).'
    '`Non-code` = everything outside `src/**`, `tests/**`, `freew/**`, `freep/**`, `shared/**` (`docs/**`, `tools/**`, top-level files, etc.).'
    'Caveat: this is literal-glob matching per the above patterns, not a semantic "runs on Windows" judgment - e.g. `freep/FreeP.App.Ole.Windows` and `freep/FreeP.App.Recording.Windows` have "Windows" in their project name but do not match any of the `Windows (WPF)` globs above (no `.App.Host`, `.App.UI`, `.Wpf`, or `Free.Shared.*.Windows` substring), so they land in `Platform-neutral`.'
)

# NOTE: only Files Changed and LoC +/- are asserted for exact equality here - those are true
# path-level partitions of the same numstat data as the Daily Build Churn TOTAL row. "Commits"
# is NOT expected to sum to $totCommits: git suppresses --numstat output for merge commits
# (multiple parents) unless -m/-c is passed, so a merge commit with no line-level diff is
# tallied once in $totCommits (every commit header increments it) but contributes to zero
# buckets here (a bucket's Commits only increments when at least one numstat line for that
# bucket is seen). A commit touching several buckets is also counted once per bucket it
# touches, so bucket Commits sums are commit-bucket-touch counts, not a partition of commits.
$appPlatformArithmeticOk = ($appChurnTotals.Files -eq $totFiles) -and ($appChurnTotals.LocAdd -eq $totLocAdd) -and ($appChurnTotals.LocDel -eq $totLocDel) -and
    ($platformChurnTotals.Files -eq $totFiles) -and ($platformChurnTotals.LocAdd -eq $totLocAdd) -and ($platformChurnTotals.LocDel -eq $totLocDel)
if (-not $appPlatformArithmeticOk) {
    Write-Progress2 "WARNING: app/platform bucket Files/LoC totals do not exactly match Daily Build Churn TOTAL row! App: files=$($appChurnTotals.Files) locAdd=$($appChurnTotals.LocAdd) locDel=$($appChurnTotals.LocDel); Platform: files=$($platformChurnTotals.Files) locAdd=$($platformChurnTotals.LocAdd) locDel=$($platformChurnTotals.LocDel); Expected: files=$totFiles locAdd=$totLocAdd locDel=$totLocDel"
} else {
    Write-Progress2 "App/platform bucket arithmetic check OK: both partitions' Files Changed and LoC +/- sum exactly to the Daily Build Churn TOTAL row (files=$totFiles, +$totLocAdd/-$totLocDel). (Commits sums differ from TOTAL by design - see the caveat in the generated doc's Git Churn By App/Platform sections: App commits=$($appChurnTotals.Commits), Platform commits=$($platformChurnTotals.Commits), Daily Build Churn TOTAL commits=$totCommits, difference is merge commits with no numstat diff plus multi-bucket commits counted per bucket touched.)"
}

[void]$sb.AppendLine('## Daily Provider Token Usage')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| Date | Provider | Files | Sessions | Events | Bytes +/- | Input | Cached Input | Cache Write | Cache Read | Output | Reasoning | Raw Tokens | Billable Eq Tokens |')
[void]$sb.AppendLine('| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |')
$tAcc = [ordered]@{ Files=0; Sessions=0; Events=0L; Bytes=0L; Input=0L; CachedInput=0L; CacheWrite=0L; CacheRead=0L; Output=0L; Reasoning=0L; Raw=0.0; Billable=0.0 }
foreach ($row in ($tokenRows | Sort-Object Date, Provider)) {
    [void]$sb.AppendLine("| $($row.Date) | $($row.Provider) | $($row.Files) | $($row.Sessions) | $(Format-N0 $row.Events) | $(Format-N0 $row.Bytes) | $(Format-N0 $row.Input) | $(Format-N0 $row.CachedInput) | $(Format-N0 $row.CacheWrite) | $(Format-N0 $row.CacheRead) | $(Format-N0 $row.Output) | $(Format-N0 $row.Reasoning) | $(Format-N0 $row.Raw) | $(Format-N0 $row.Billable) |")
    $tAcc.Files += $row.Files; $tAcc.Sessions += $row.Sessions; $tAcc.Events += $row.Events; $tAcc.Bytes += $row.Bytes
    $tAcc.Input += $row.Input; $tAcc.CachedInput += $row.CachedInput; $tAcc.CacheWrite += $row.CacheWrite; $tAcc.CacheRead += $row.CacheRead
    $tAcc.Output += $row.Output; $tAcc.Reasoning += $row.Reasoning; $tAcc.Raw += $row.Raw; $tAcc.Billable += $row.Billable
}
[void]$sb.AppendLine("| TOTAL | all | $($tAcc.Files) | $($tAcc.Sessions) | $(Format-N0 $tAcc.Events) | $(Format-N0 $tAcc.Bytes) | $(Format-N0 $tAcc.Input) | $(Format-N0 $tAcc.CachedInput) | $(Format-N0 $tAcc.CacheWrite) | $(Format-N0 $tAcc.CacheRead) | $(Format-N0 $tAcc.Output) | $(Format-N0 $tAcc.Reasoning) | $(Format-N0 $tAcc.Raw) | $(Format-N0 $tAcc.Billable) |")
[void]$sb.AppendLine()

# ---------------------------------------------------------------------------
# Provider token TOTALS summary (EXACT - already computed above from
# per-provider daily rows; this is just a compact, unambiguous rollup by
# provider, split from the per-day table for at-a-glance reading).
# ---------------------------------------------------------------------------

[void]$sb.AppendLine('## Provider Token Totals')
[void]$sb.AppendLine()
[void]$sb.AppendLine('EXACT - summed directly from the per-day Anthropic and OpenAI usage rows above (each row is one date+provider; a date with both providers active contributes one row per provider).')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| Provider | Raw Tokens | Billable Eq Tokens |')
[void]$sb.AppendLine('| --- | ---: | ---: |')
$providerTotals = [ordered]@{ anthropic = [ordered]@{ Raw = 0.0; Billable = 0.0 }; openai = [ordered]@{ Raw = 0.0; Billable = 0.0 } }
foreach ($row in $tokenRows) {
    $providerTotals[$row.Provider].Raw += $row.Raw
    $providerTotals[$row.Provider].Billable += $row.Billable
}
[void]$sb.AppendLine("| Anthropic (Claude) | $(Format-N0 $providerTotals.anthropic.Raw) | $(Format-N0 $providerTotals.anthropic.Billable) |")
[void]$sb.AppendLine("| OpenAI (Codex) | $(Format-N0 $providerTotals.openai.Raw) | $(Format-N0 $providerTotals.openai.Billable) |")
[void]$sb.AppendLine("| TOTAL | $(Format-N0 $tAcc.Raw) | $(Format-N0 $tAcc.Billable) |")
[void]$sb.AppendLine()

# ---------------------------------------------------------------------------
# Estimated token allocation by App / Platform Layer - DERIVED, NOT MEASURED.
#
# The session logs behind the token table above carry no reliable per-app
# attribution: git branch names are ~99.8% useless for this (almost all
# sessions run on `main` or an auto-generated `claude/<random>` branch), and
# `cwd` is the monorepo root for nearly every session. So instead of
# claiming a measurement that does not exist, each day's combined raw token
# total (OpenAI + Anthropic) is allocated across buckets in proportion to
# that day's EXACT git churn share (LoC added + removed) for the same
# bucket set used in "Git Churn By App" / "By Platform Layer" above. Days
# with tokens but no churn (or churn but no tokens) are not silently
# dropped - they show up in an explicit Unallocated bucket / are simply
# omitted from the allocated total, respectively.
# ---------------------------------------------------------------------------

function Write-TokenAllocationEstimate {
    param(
        [System.Text.StringBuilder]$Sb,
        [string]$Title,
        [string]$BucketColumnHeader,
        [string[]]$BucketOrder,
        [hashtable]$DayEntries
    )
    # Per-date, per-bucket churn weight (LoC added + removed that day in that bucket).
    $dayBucketWeight = @{} # date -> bucket -> weight
    foreach ($entry in $DayEntries.Values) {
        if (-not $dayBucketWeight.ContainsKey($entry.Date)) { $dayBucketWeight[$entry.Date] = @{} }
        $dayBucketWeight[$entry.Date][$entry.Bucket] = [double]($entry.LocAdd + $entry.LocDel)
    }

    $allocated = [ordered]@{}
    foreach ($b in $BucketOrder) { $allocated[$b] = 0.0 }
    $unallocatedTokens = 0.0
    $unallocatedDays = 0
    $allocatedDays = 0

    foreach ($date in $allDates) {
        $tt = $dayTokenTotals[$date]
        if (-not $tt) { continue }
        $dayTokens = $tt.OpenAI + $tt.Anthropic
        if ($dayTokens -le 0) { continue }
        $weights = $dayBucketWeight[$date]
        $totalWeight = 0.0
        if ($weights) { foreach ($w in $weights.Values) { $totalWeight += $w } }
        if (-not $weights -or $totalWeight -le 0) {
            # Tokens logged this day but no churn (bucket set has no weight to allocate against).
            $unallocatedTokens += $dayTokens
            $unallocatedDays++
            continue
        }
        $allocatedDays++
        foreach ($b in $BucketOrder) {
            $w = 0.0
            if ($weights.ContainsKey($b)) { $w = $weights[$b] }
            $allocated[$b] += $dayTokens * ($w / $totalWeight)
        }
    }

    [void]$Sb.AppendLine("### Estimated Token Allocation By $Title (derived, not measured)")
    [void]$Sb.AppendLine()
    [void]$Sb.AppendLine('**ESTIMATE - do not read as measured per-bucket token usage.** Token logs carry no app/platform attribution; these figures allocate each day''s combined raw token total (Anthropic + OpenAI) across buckets in proportion to that day''s EXACT git churn share (LoC added + removed) from the churn section above. A day with tokens logged but zero churn in the window falls into `Unallocated` rather than being dropped or forced into a bucket.')
    [void]$Sb.AppendLine()
    [void]$Sb.AppendLine("| $BucketColumnHeader | Est. Allocated Raw Tokens | Share |")
    [void]$Sb.AppendLine('| --- | ---: | ---: |')
    $grandTotal = $unallocatedTokens
    foreach ($b in $BucketOrder) { $grandTotal += $allocated[$b] }
    foreach ($b in $BucketOrder) {
        $share = if ($grandTotal -gt 0) { $allocated[$b] / $grandTotal } else { 0.0 }
        [void]$Sb.AppendLine("| $b | $(Format-N0 $allocated[$b]) | $($share.ToString('P1', [System.Globalization.CultureInfo]::InvariantCulture)) |")
    }
    $unallocatedShare = if ($grandTotal -gt 0) { $unallocatedTokens / $grandTotal } else { 0.0 }
    [void]$Sb.AppendLine("| Unallocated (tokens logged, no churn that day) | $(Format-N0 $unallocatedTokens) | $($unallocatedShare.ToString('P1', [System.Globalization.CultureInfo]::InvariantCulture)) |")
    [void]$Sb.AppendLine("| TOTAL | $(Format-N0 $grandTotal) | 100.0% |")
    [void]$Sb.AppendLine()
    [void]$Sb.AppendLine("- Days allocated (had both tokens and churn weight): $(Format-N0 $allocatedDays). Days with tokens but no churn to allocate against (routed to Unallocated): $(Format-N0 $unallocatedDays).")
    [void]$Sb.AppendLine()
}

[void]$sb.AppendLine('## Measured Token Usage By App (Provider-Attributed)')
[void]$sb.AppendLine()
[void]$sb.AppendLine('Unlike the estimated allocation below, this is **measured, not derived from churn share**: branch name and session `cwd` indeed carry no app signal, but individual tool calls do. Each usage event is attributed to whichever app the session was most recently editing at that point - Anthropic via the `file_path` of the latest Edit/Write/Read tool call on the same message as the usage event, Codex via the file(s) in the latest `patch_apply_end` before the token_count event - using the same App/Platform buckets as the "Git Churn By App" / "By Platform Layer" sections above. This is a "most recently touched file" attribution, not a proportional split: a session that edits FreeX then Shared then FreeW has its tokens split across those three buckets according to when each edit happened. Because every event is attributed to exactly one app, the per-app totals below sum exactly to each provider''s total in the "Daily Provider Token Usage" table. Coverage caveat: an event with no prior file-edit signal in its session yet (pure discussion/planning at the very start of a session) falls into `Docs/Tooling/Other` by default, same as work that is genuinely outside `src/`, `tests/`, `freew/`, `freep/`, `shared/`.')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| Provider | App | Events | Raw Tokens | Billable Eq Tokens |')
[void]$sb.AppendLine('| --- | --- | ---: | ---: | ---: |')
foreach ($provider in @('anthropic', 'openai')) {
    if (-not $appTotals.Contains($provider)) { continue }
    $seenApps = New-Object 'System.Collections.Generic.HashSet[string]'
    $orderedApps = New-Object System.Collections.Generic.List[string]
    foreach ($a in $AppBucketOrder) { if ($appTotals[$provider].Contains($a)) { [void]$seenApps.Add($a); $orderedApps.Add($a) } }
    foreach ($a in $appTotals[$provider].Keys) { if ($seenApps.Add($a)) { $orderedApps.Add($a) } }
    foreach ($a in $orderedApps) {
        $v = $appTotals[$provider][$a]
        [void]$sb.AppendLine("| $provider | $a | $(Format-N0 $v.Events) | $(Format-N0 $v.Raw) | $(Format-N0 $v.Billable) |")
    }
}
[void]$sb.AppendLine()

[void]$sb.AppendLine('## Measured Token Usage By Platform (Provider-Attributed)')
[void]$sb.AppendLine()
[void]$sb.AppendLine('Same measured "most recently touched file" attribution as the App breakdown above, using the same Windows (WPF) / Avalonia (Linux/macOS) / Platform-neutral / Non-code buckets as the "Git Churn By Platform Layer" section.')
[void]$sb.AppendLine()
[void]$sb.AppendLine('| Provider | Platform | Events | Raw Tokens | Billable Eq Tokens |')
[void]$sb.AppendLine('| --- | --- | ---: | ---: | ---: |')
foreach ($provider in @('anthropic', 'openai')) {
    if (-not $platformTotals.Contains($provider)) { continue }
    $seenPlats = New-Object 'System.Collections.Generic.HashSet[string]'
    $orderedPlats = New-Object System.Collections.Generic.List[string]
    foreach ($p in $PlatformBucketOrder) { if ($platformTotals[$provider].Contains($p)) { [void]$seenPlats.Add($p); $orderedPlats.Add($p) } }
    foreach ($p in $platformTotals[$provider].Keys) { if ($seenPlats.Add($p)) { $orderedPlats.Add($p) } }
    foreach ($p in $orderedPlats) {
        $v = $platformTotals[$provider][$p]
        [void]$sb.AppendLine("| $provider | $p | $(Format-N0 $v.Events) | $(Format-N0 $v.Raw) | $(Format-N0 $v.Billable) |")
    }
}
[void]$sb.AppendLine()

[void]$sb.AppendLine('## Estimated Token Allocation By App / Platform (derived, not measured)')
[void]$sb.AppendLine()
[void]$sb.AppendLine('The sections below are **estimates derived from git churn share, not measurements** - kept alongside the measured section above because it has full coverage of every token-bearing day (the measured breakdown''s `Docs/Tooling/Other` catch-all can include a large early-session share where no file had been touched yet), while this estimate assumes none. Claude Code / Codex session logs do not record which app or platform layer a session worked on via branch name or `cwd`: the overwhelming majority run on the `main` git branch (or an auto-generated `claude/<random-name>` branch carrying no app info), and the working directory recorded in nearly every session is the monorepo root rather than an app subfolder. (Individual tool calls DO carry a usable file-path signal, which is what the measured section above is built from - branch/cwd just is not it.) The allocation below instead spreads each day''s observed raw tokens across buckets using that same day''s EXACT churn share from the "Git Churn By App" / "By Platform Layer" sections. Treat it as a rough proxy for where effort likely went, not as billed or measured per-app usage.')
[void]$sb.AppendLine()
Write-TokenAllocationEstimate -Sb $sb -Title 'App' -BucketColumnHeader 'App' -BucketOrder $AppBucketOrder -DayEntries $appDayStats
Write-TokenAllocationEstimate -Sb $sb -Title 'Platform Layer' -BucketColumnHeader 'Platform Layer' -BucketOrder $PlatformBucketOrder -DayEntries $platformDayStats

[void]$sb.AppendLine('## Token Extraction Notes')
[void]$sb.AppendLine()
[void]$sb.AppendLine('- Anthropic / Claude source: `~/.claude/projects/*FreeX*/**/*.jsonl` (directory names containing "FreeX", case-insensitive; includes worktree-scoped project dirs and nested subagent transcripts).')
[void]$sb.AppendLine('- OpenAI / Codex source: `~/.codex/sessions/**/*.jsonl` and `~/.codex/archived_sessions/*.jsonl`, filtered to sessions whose `session_meta` `cwd` contains "FreeX".')
[void]$sb.AppendLine('- Files/Sessions counts are distinct file/session-id counts contributing to that date+provider row. Events is the count of usage-bearing records attributed to that date.')
[void]$sb.AppendLine('- Bytes +/- attributes each contributing file''s full size to every date on which it had at least one attributed usage event (a file spanning multiple days is counted on each of those days).')
[void]$sb.AppendLine("- Machines aggregated into this run's totals: $machineNote.")
[void]$sb.AppendLine('- Per-machine `project-history-tokens-<MachineId>.json` files (tracked in git; see the multi-machine workflow note at the top of `tools/Build-ProjectHistoryMetrics.ps1`) contain ONLY: `machineId`, `generatedAt`, `startDate`, `endDate`, an `anthropic` object and an `openai` object each keyed by date with per-day `files`/`sessions`/`events`/`bytes`/`input`/`cachedInput`/`cacheWrite`/`cacheRead`/`output`/`reasoning` counts, and a static `codexNote` methodology string. No transcript content, prompts, file paths, or session titles are read or stored.')
[void]$sb.AppendLine()

[void]$sb.AppendLine('## Git Authors Observed')
[void]$sb.AppendLine()
foreach ($date in $allDates) {
    $b = $dayStats[$date]
    if (-not $b -or $b.Authors.Count -eq 0) { continue }
    $names = ($b.Authors | Sort-Object) -join '; '
    [void]$sb.AppendLine("- $date`: $names")
}
[void]$sb.AppendLine()

[void]$sb.AppendLine('## Reading The Trend')
[void]$sb.AppendLine()
[void]$sb.AppendLine("- The daily churn table covers $StartDate through $EndDate, computed fresh from git history reachable from HEAD (``$headShaShort``) at generation time.")
[void]$sb.AppendLine("- Across the window: $(Format-N0 $totCommits) commits, $(Format-N0 $totFiles) changed-file/day entries, +$(Format-N0 $totLocAdd) / -$(Format-N0 $totLocDel) LoC.")
# Source these from the aggregated accumulator ($tAcc) that also feeds the provider TOTAL row.
# They previously read $totBytes/$totRaw/$totBillable, which are never assigned anywhere, so this
# line always claimed "0 bytes / 0 raw tokens" and contradicted the table directly above it.
[void]$sb.AppendLine("- Token rows reflect $(Format-N0 $tAcc.Bytes) bytes of local provider logs, $(Format-N0 $tAcc.Raw) observed raw tokens, and $(Format-N0 $tAcc.Billable) provider-style billable-equivalent tokens, from machine(s): $machineNote.")

$footprintNote = "This machine ($MachineId) has contributed its token logs. Run this script on the user's other machines and copy their project-history-tokens-*.json into $OutputDir before re-running here (or there) to fold their usage into these totals."
[void]$sb.AppendLine("- $footprintNote")

Save-Utf8NoBom -Path (Join-Path $docsDir 'build-history-metrics.md') -Content $sb.ToString()
Write-Progress2 'Wrote docs/history/build-history-metrics.md'

# ---------------------------------------------------------------------------
# Thread Commit Timing (first-parent merge thread analysis, full history)
# ---------------------------------------------------------------------------

if ($SkipThreadTiming) {
    Write-Progress2 'Skipping thread-commit-timing.md regeneration (-SkipThreadTiming).'
} else {
    Write-Progress2 'Building full commit graph for thread timing analysis (single git log pass)...'

    # See the $RS note above: ask git for the 0x1F separator via '%x1f' (pure-ASCII argument),
    # and use the real char only to split the output.
    $US = [string][char]0x1F
    $graphRaw = Invoke-Git @('log', '--pretty=format:%H%x1f%P%x1f%aI%x1f%s', 'HEAD')
    $commits = @{}   # sha -> @{ Parents=[string[]]; Date=[datetime]; Subject=string }
    foreach ($line in ($graphRaw -split "`n")) {
        if ($line.Trim() -eq '') { continue }
        $parts = $line.Split($US)
        if ($parts.Count -lt 4) { continue }
        $sha = $parts[0]
        $parents = @()
        if ($parts[1].Trim() -ne '') { $parents = $parts[1].Trim() -split ' ' }
        $dt = [datetime]::Parse($parts[2], [System.Globalization.CultureInfo]::InvariantCulture, [System.Globalization.DateTimeStyles]::RoundtripKind)
        $subject = $parts[3]
        $commits[$sha] = @{ Parents = $parents; Date = $dt; Subject = $subject }
    }
    Write-Progress2 "Commit graph loaded: $($commits.Count) commit(s) reachable from HEAD."

    Write-Progress2 'Walking first-parent chain...'
    $fpRaw = Invoke-Git @('log', '--first-parent', '--reverse', '--pretty=format:%H', 'HEAD')
    $firstParentChain = ($fpRaw -split "`n") | Where-Object { $_.Trim() -ne '' }
    Write-Progress2 "First-parent chain length: $($firstParentChain.Count)"

    Write-Progress2 'Loading ref names for thread identity resolution...'
    $refRaw = Invoke-Git @('for-each-ref', '--format=%(objectname) %(refname:short)')
    $refsBySha = @{}
    foreach ($line in ($refRaw -split "`n")) {
        if ($line.Trim() -eq '') { continue }
        $sp = $line.IndexOf(' ')
        if ($sp -lt 0) { continue }
        $sha = $line.Substring(0, $sp)
        $name = $line.Substring($sp + 1).Trim()
        if (-not $refsBySha.ContainsKey($sha)) { $refsBySha[$sha] = New-Object 'System.Collections.Generic.List[string]' }
        $refsBySha[$sha].Add($name)
    }

    function Resolve-ThreadName {
        param([string]$Sha, [string]$Subject)
        if ($refsBySha.ContainsKey($Sha)) {
            $names = $refsBySha[$Sha] | Where-Object { $_ -ne 'HEAD' -and $_ -ne 'main' -and $_ -ne 'origin/main' }
            $local = $names | Where-Object { $_ -notmatch '^origin/' } | Select-Object -First 1
            if ($local) { return $local }
            $remote = $names | Select-Object -First 1
            if ($remote) { return ($remote -replace '^origin/', '') }
        }
        if ($Subject -match "^Merge branch '([^']+)'") { return $Matches[1] }
        if ($Subject -match '^Merge pull request #\d+ from \S+/(.+)$') { return $Matches[1] }
        if ($Subject -match '^[Mm]erge:\s*(.+)$') { return $Matches[1] }
        if ($Subject -match '^Merge (.+)$') { return $Matches[1] }
        return $Subject
    }

    $knownSet = New-Object 'System.Collections.Generic.HashSet[string]'
    $threads = New-Object System.Collections.Generic.List[object]
    $directCommitCount = 0
    $noNewCommitMergeCount = 0
    $processed = 0

    foreach ($sha in $firstParentChain) {
        $processed++
        if ($processed % 1000 -eq 0) { Write-Progress2 "  ...processed $processed / $($firstParentChain.Count) first-parent commits" }
        $c = $commits[$sha]
        if (-not $c) { continue }
        $parents = $c.Parents

        if ($parents.Count -le 1) {
            [void]$knownSet.Add($sha)
            if ($parents.Count -eq 1) { $directCommitCount++ }
            continue
        }

        # Merge commit: parents[0] is mainline (already known), parents[1..] are newly merged tips.
        $introduced = New-Object System.Collections.Generic.List[string]
        $localVisited = New-Object 'System.Collections.Generic.HashSet[string]'
        $stack = New-Object 'System.Collections.Generic.Stack[string]'
        for ($i = 1; $i -lt $parents.Count; $i++) { $stack.Push($parents[$i]) }
        while ($stack.Count -gt 0) {
            $node = $stack.Pop()
            if ([string]::IsNullOrEmpty($node)) { continue }
            if ($knownSet.Contains($node) -or $localVisited.Contains($node)) { continue }
            [void]$localVisited.Add($node)
            $introduced.Add($node)
            $nc = $commits[$node]
            if ($nc) {
                foreach ($p in $nc.Parents) { if ($p) { $stack.Push($p) } }
            }
        }
        foreach ($n in $introduced) { [void]$knownSet.Add($n) }
        [void]$knownSet.Add($sha)

        if ($introduced.Count -eq 0) {
            $noNewCommitMergeCount++
            $threads.Add([ordered]@{
                Thread = Resolve-ThreadName $parents[1] $c.Subject
                Merge = $sha; MergeDate = $c.Date; MergeSubject = $c.Subject
                IntroducedCount = 0
                FirstSha = $null; FirstDate = $null; FirstSubject = $null
                LastSha = $null; LastDate = $null; LastSubject = $null
            })
            continue
        }

        # Force array context with @(...): when $introduced has exactly one element, piping it
        # bare through Sort-Object unwraps the pipeline result back down to a scalar string
        # instead of a 1-element array. Indexing that scalar with [0] / [Count-1] then indexes
        # into the SHA string's *characters* (PowerShell allows string indexing) rather than
        # the list, producing a bogus single-character "sha" that isn't in $commits and yields
        # null dates downstream. @() keeps it an array regardless of element count.
        $sortedByDate = @($introduced | Sort-Object { $commits[$_].Date })
        $firstSha = $sortedByDate[0]
        $lastSha = $sortedByDate[$sortedByDate.Count - 1]

        $threads.Add([ordered]@{
            Thread = Resolve-ThreadName $parents[1] $c.Subject
            Merge = $sha; MergeDate = $c.Date; MergeSubject = $c.Subject
            IntroducedCount = $introduced.Count
            FirstSha = $firstSha; FirstDate = $commits[$firstSha].Date; FirstSubject = $commits[$firstSha].Subject
            LastSha = $lastSha; LastDate = $commits[$lastSha].Date; LastSubject = $commits[$lastSha].Subject
        })
    }

    Write-Progress2 "Thread analysis complete: $($threads.Count) merge thread(s), $directCommitCount direct first-parent commit(s), $noNewCommitMergeCount no-op merge(s)."

    $threads = $threads | Sort-Object MergeDate
    for ($i = 0; $i -lt $threads.Count; $i++) { $threads[$i].Rank = $i + 1 }

    $projectStartSha = $firstParentChain[0]
    $projectStart = $commits[$projectStartSha].Date
    $projectStartSubject = $commits[$projectStartSha].Subject

    # Note: $threads elements are [ordered]@{...} hashtables (OrderedDictionary), not
    # PSCustomObjects. Dotted member access (e.g. $_.IntroducedCount) works on them via
    # PowerShell's special-cased hashtable-key adaptation, but Measure-Object -Property uses
    # reflection/Get-Member over the object's real properties and does not see hashtable keys
    # that way, so it fails with "the property ... cannot be found". Sum manually instead.
    $totalIntroduced = 0
    foreach ($t in $threads) { $totalIntroduced += $t.IntroducedCount }
    $totalReachable = $commits.Count

    # As above, $threads elements are OrderedDictionary hashtables, not PSCustomObjects. A
    # Sort-Object script block that does arithmetic on two of their properties (e.g.
    # ($_.MergeDate - $_.LastDate)) can fail on Windows PowerShell 5.1 with
    # "Cannot find an overload for 'op_Subtraction' and the argument count: 2" because the
    # hashtable-key adaptation used for simple dotted access does not compose reliably inside
    # a Sort-Object comparer delegate. Find each rollup with a plain manual scan instead.
    $mostIntroduced = $null
    foreach ($t in $threads) {
        if (-not $mostIntroduced -or $t.IntroducedCount -gt $mostIntroduced.IntroducedCount) { $mostIntroduced = $t }
    }
    $withSpan = @($threads | Where-Object { $_.IntroducedCount -gt 0 })
    $longestSpan = $null
    $longestSpanTicks = [long]::MinValue
    $longestLag = $null
    $longestLagTicks = [long]::MinValue
    foreach ($t in $withSpan) {
        [datetime]$lastDt = $t.LastDate
        [datetime]$firstDt = $t.FirstDate
        [datetime]$mergeDt = $t.MergeDate
        $spanTicks = ($lastDt - $firstDt).Ticks
        if ($spanTicks -gt $longestSpanTicks) { $longestSpanTicks = $spanTicks; $longestSpan = $t }
        $lagTicks = ($mergeDt - $lastDt).Ticks
        if ($lagTicks -gt $longestLagTicks) { $longestLagTicks = $lagTicks; $longestLag = $t }
    }

    $tsb = New-Object System.Text.StringBuilder
    [void]$tsb.AppendLine('# Thread Commit Timing Report')
    [void]$tsb.AppendLine()
    [void]$tsb.AppendLine("Generated: $($generatedAt.ToString('yyyy-MM-dd HH:mm zzz'))")
    [void]$tsb.AppendLine("Baseline ref: HEAD at ``$headShaShort`` (``$headSha``)")
    [void]$tsb.AppendLine("Project start: $($projectStart.ToString('yyyy-MM-dd HH:mm zzz')) at ``$($projectStartSha.Substring(0,9))`` - $projectStartSubject")
    [void]$tsb.AppendLine('History scope: first-parent history from project start through the baseline ref (HEAD).')
    [void]$tsb.AppendLine()
    [void]$tsb.AppendLine('## Method')
    [void]$tsb.AppendLine()
    [void]$tsb.AppendLine('- Produced by the committable, repeatable extractor `tools/Build-ProjectHistoryMetrics.ps1` (not a one-off script). Re-run it to refresh.')
    [void]$tsb.AppendLine('- A thread is represented by each first-parent merge commit.')
    [void]$tsb.AppendLine('- Thread identity is best-effort: an exact ref (local branch preferred, else remote-tracking branch) still pointing at the merge''s second parent, otherwise a branch/PR name parsed from the merge subject, otherwise the merge subject without the `Merge`/`merge:` prefix.')
    [void]$tsb.AppendLine('- Introduced commits for a merge are computed by walking parent edges from every non-mainline parent, stopping at any commit already known to be an ancestor of the mainline at that point in history (equivalent to `first_parent..second_parent`, generalized to octopus merges). Each commit in the whole graph is visited at most once across the entire run, so this is a single linear-time pass rather than one Git invocation per merge.')
    [void]$tsb.AppendLine('- Discovery time is the timestamp of the first introduced commit (author date). Implementation span is first introduced commit to last introduced commit. Integration lag is last introduced commit to the first-parent merge commit.')
    [void]$tsb.AppendLine('- Direct first-parent commits are summarized separately because they do not have a merge-thread identity.')
    [void]$tsb.AppendLine()
    [void]$tsb.AppendLine('## Summary')
    [void]$tsb.AppendLine()
    [void]$tsb.AppendLine('| Metric | Count |')
    [void]$tsb.AppendLine('| --- | ---: |')
    [void]$tsb.AppendLine("| First-parent merge threads | $(Format-N0 $threads.Count) |")
    [void]$tsb.AppendLine("| Introduced commits across merge threads | $(Format-N0 $totalIntroduced) |")
    [void]$tsb.AppendLine("| Merge threads with no new second-parent commits | $(Format-N0 $noNewCommitMergeCount) |")
    [void]$tsb.AppendLine("| Direct first-parent commits without merge-thread identity | $(Format-N0 $directCommitCount) |")
    [void]$tsb.AppendLine("| Total commits reachable from baseline | $(Format-N0 $totalReachable) |")
    [void]$tsb.AppendLine()
    [void]$tsb.AppendLine('| Rollup | Thread | Value |')
    [void]$tsb.AppendLine('| --- | --- | ---: |')
    if ($mostIntroduced) { [void]$tsb.AppendLine("| Most introduced commits | ``$($mostIntroduced.Thread)`` | $($mostIntroduced.IntroducedCount) commits |") }
    if ($longestSpan) { [void]$tsb.AppendLine("| Longest implementation span | ``$($longestSpan.Thread)`` | $(Format-Offset ($longestSpan.LastDate - $longestSpan.FirstDate)) |") }
    if ($longestLag) { [void]$tsb.AppendLine("| Longest integration lag | ``$($longestLag.Thread)`` | $(Format-Offset ($longestLag.MergeDate - $longestLag.LastDate)) |") }
    [void]$tsb.AppendLine()
    [void]$tsb.AppendLine('## Thread Timing Table')
    [void]$tsb.AppendLine()
    [void]$tsb.AppendLine('| # | Thread | Merge | Introduced Commits | First Commit | Last Commit | Discovery Time | Discovery Offset | Implementation Span | Integration Time | Integration Offset | Integration Lag |')
    [void]$tsb.AppendLine('| ---: | --- | --- | ---: | --- | --- | --- | ---: | ---: | --- | ---: | ---: |')
    foreach ($t in $threads) {
        $mergeShort = $t.Merge.Substring(0, 9)
        if ($t.IntroducedCount -eq 0) {
            [void]$tsb.AppendLine("| $($t.Rank) | ``$($t.Thread)`` | ``$mergeShort`` | 0 | (none) | (none) | - | - | - | $($t.MergeDate.ToString('yyyy-MM-dd HH:mm zzz')) | $(Format-Offset ($t.MergeDate - $projectStart)) | - |")
            continue
        }
        $firstShort = $t.FirstSha.Substring(0, 9)
        $lastShort = $t.LastSha.Substring(0, 9)
        $firstSubj = $t.FirstSubject
        $lastSubj = $t.LastSubject
        $discoveryOffset = Format-Offset ($t.FirstDate - $projectStart)
        $implSpan = Format-Offset ($t.LastDate - $t.FirstDate)
        $integrationOffset = Format-Offset ($t.MergeDate - $projectStart)
        $integrationLag = Format-Offset ($t.MergeDate - $t.LastDate)
        [void]$tsb.AppendLine("| $($t.Rank) | ``$($t.Thread)`` | ``$mergeShort`` | $($t.IntroducedCount) | ``$firstShort`` $firstSubj | ``$lastShort`` $lastSubj | $($t.FirstDate.ToString('yyyy-MM-dd HH:mm zzz')) | $discoveryOffset | $implSpan | $($t.MergeDate.ToString('yyyy-MM-dd HH:mm zzz')) | $integrationOffset | $integrationLag |")
    }

    Save-Utf8NoBom -Path (Join-Path $docsDir 'thread-commit-timing.md') -Content $tsb.ToString()
    Write-Progress2 'Wrote docs/history/thread-commit-timing.md'
}

Write-Progress2 'Done.'
