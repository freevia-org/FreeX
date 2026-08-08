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
        (project-history-tokens-<MachineId>.json) that can be copied alongside this one so a
        later regeneration on any machine aggregates token totals across all machines present
        in -OutputDir.

    Git-derived metrics (commit/churn/footprint/thread-timing) are always recomputed FRESH from
    the repository at the time the script runs and are authoritative/complete - they do not
    depend on which machine produced them, only on the git history itself.

    Token metrics are inherently per-machine (they come from local Claude Code / Codex log
    files that only exist on the machine that produced them). Each run writes this machine's
    per-day token sums to -OutputDir as project-history-tokens-<MachineId>.json, then the doc
    generation step reads and sums ALL project-history-tokens-*.json files present in
    -OutputDir. Until other machines' JSON files are copied in, the token columns reflect only
    the machine(s) that have contributed a JSON file so far.

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

$RS = "`u{1F}"  # record separator between commit metadata fields
$churnRaw = Invoke-Git @(
    'log', 'HEAD',
    "--since=$sinceArg",
    "--until=$untilArg",
    '--no-renames', '--numstat',
    '--date=format-local:%Y-%m-%d',
    "--pretty=format:@@C@@$RS%H$RS%ad$RS%an <%ae>"
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

$currentDate = $null
$currentBucket = $null
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
}

Write-Progress2 "Daily churn parsed: $($dayStats.Count) active day(s) in window."

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
    }
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
                        if ($line.IndexOf('"token_count"') -lt 0) { continue }
                        try { $obj = $line | ConvertFrom-Json -ErrorAction Stop } catch { continue }
                        if ($obj.type -ne 'event_msg') { continue }
                        $payload = $obj.payload
                        if (-not $payload -or $payload.type -ne 'token_count') { continue }
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
        }
    }
    return $out
}

$machineJsonPath = Join-Path $OutputDir "project-history-tokens-$MachineId.json"
$machinePayload = [ordered]@{
    machineId   = $MachineId
    generatedAt = $generatedAt.ToString('o')
    startDate   = $StartDate
    endDate     = $EndDate
    anthropic   = ConvertTo-JsonDayMap $anthropicDaily
    openai      = ConvertTo-JsonDayMap $openaiDaily
    codexNote   = $codexNote
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

[void]$sb.AppendLine('## Token Extraction Notes')
[void]$sb.AppendLine()
[void]$sb.AppendLine('- Anthropic / Claude source: `~/.claude/projects/*FreeX*/**/*.jsonl` (directory names containing "FreeX", case-insensitive; includes worktree-scoped project dirs and nested subagent transcripts).')
[void]$sb.AppendLine('- OpenAI / Codex source: `~/.codex/sessions/**/*.jsonl` and `~/.codex/archived_sessions/*.jsonl`, filtered to sessions whose `session_meta` `cwd` contains "FreeX".')
[void]$sb.AppendLine('- Files/Sessions counts are distinct file/session-id counts contributing to that date+provider row. Events is the count of usage-bearing records attributed to that date.')
[void]$sb.AppendLine('- Bytes +/- attributes each contributing file''s full size to every date on which it had at least one attributed usage event (a file spanning multiple days is counted on each of those days).')
[void]$sb.AppendLine("- Machines aggregated into this run's totals: $machineNote.")
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
[void]$sb.AppendLine("- Token rows reflect $(Format-N0 $totBytes) bytes of local provider logs, $(Format-N0 $totRaw) observed raw tokens, and $(Format-N0 $totBillable) provider-style billable-equivalent tokens, from machine(s): $machineNote.")

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

    $US = "`u{1F}"
    $graphRaw = Invoke-Git @('log', '--pretty=format:%H' + $US + '%P' + $US + '%aI' + $US + '%s', 'HEAD')
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

        $introDates = $introduced | ForEach-Object { $commits[$_].Date }
        $sortedByDate = $introduced | Sort-Object { $commits[$_].Date }
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

    $totalIntroduced = ($threads | Measure-Object -Property IntroducedCount -Sum).Sum
    $totalReachable = $commits.Count

    $mostIntroduced = $threads | Sort-Object -Property IntroducedCount -Descending | Select-Object -First 1
    $withSpan = $threads | Where-Object { $_.IntroducedCount -gt 0 }
    $longestSpan = $withSpan | Sort-Object { ($_.LastDate - $_.FirstDate).Ticks } -Descending | Select-Object -First 1
    $longestLag = $withSpan | Sort-Object { ($_.MergeDate - $_.LastDate).Ticks } -Descending | Select-Object -First 1

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
