param(
    [string]$JsonPath = "docs\unification\dedup-residual-metrics.json",
    [string]$MarkdownPath = "docs\unification\dedup-residual-metrics.md",
    [string]$RepositoryRoot,
    [string]$UpstreamRevision = "origin/main",
    [string]$AnalysisRevision,
    [ValidateRange(3, 100)][int]$BlockSize = 12,
    [ValidateRange(1, 100000)][int]$MinimumBlockCharacters = 200,
    [ValidateRange(2, 1000)][int]$MaximumFingerprintOccurrences = 64,
    [ValidateRange(1, 500)][int]$TopCandidateCount = 30,
    [switch]$Check
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path -Parent $PSScriptRoot
}
$repoRoot = Resolve-ToolFullPath -Path $RepositoryRoot
$resolvedJsonPath = Resolve-ToolRepoPath -Path $JsonPath -RepoRoot $repoRoot
$resolvedMarkdownPath = Resolve-ToolRepoPath -Path $MarkdownPath -RepoRoot $repoRoot
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$sha256 = [System.Security.Cryptography.SHA256]::Create()

if (-not ("DedupMetricSourceAnalyzer" -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

public sealed class DedupMetricLineRecord
{
    public int PhysicalLine { get; set; }
    public string Canonical { get; set; }
}

public sealed class DedupMetricSourceAnalysis
{
    public int PhysicalLines { get; set; }
    public int NonBlankLines { get; set; }
    public DedupMetricLineRecord[] Exact { get; set; }
    public DedupMetricLineRecord[] Normalized { get; set; }
    public string NormalizedSha256 { get; set; }
}

public sealed class DedupMetricWindowOccurrence
{
    public string RootId { get; set; }
    public string Path { get; set; }
    public int Start { get; set; }
}

public static class DedupMetricSourceAnalyzer
{
    private const RegexOptions Options = RegexOptions.Compiled | RegexOptions.CultureInvariant;
    private static readonly Regex VerbatimString = new Regex("@\"(?:\"\"|[^\"])*\"", Options);
    private static readonly Regex RegularString = new Regex("\"(?:\\\\.|[^\"\\\\])*\"", Options);
    private static readonly Regex Character = new Regex("'(?:\\\\.|[^'\\\\])'", Options);
    private static readonly Regex LineComment = new Regex("//.*$", Options);
    private static readonly Regex Number = new Regex("(?<![A-Za-z0-9_])(?:0[xX][0-9A-Fa-f_]+|0[bB][01_]+|(?:\\d[\\d_]*\\.?[\\d_]*|\\.\\d[\\d_]*)(?:[eE][+-]?\\d[\\d_]*)?)[uUlLfFdDmM]*", Options);
    private static readonly Regex AppName = new Regex("Free[XWP]", Options);
    private static readonly Regex FrameworkName = new Regex("(?:Avalonia|Wpf)", Options | RegexOptions.IgnoreCase);
    private static readonly Regex Whitespace = new Regex("\\s+", Options);
    private static readonly SHA256 WindowSha256 = SHA256.Create();

    public static DedupMetricSourceAnalysis Analyze(string path)
    {
        string[] lines = File.ReadAllLines(path);
        var exact = new List<DedupMetricLineRecord>();
        var normalized = new List<DedupMetricLineRecord>();
        bool insideBlockComment = false;
        int nonBlankLines = 0;

        for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            if (!String.IsNullOrWhiteSpace(lines[lineIndex]))
                nonBlankLines++;

            string candidate = lines[lineIndex].Trim();
            if (insideBlockComment)
            {
                int commentEnd = candidate.IndexOf("*/", StringComparison.Ordinal);
                if (commentEnd < 0)
                    continue;

                candidate = candidate.Substring(commentEnd + 2).Trim();
                insideBlockComment = false;
            }

            while (candidate.StartsWith("/*", StringComparison.Ordinal))
            {
                int commentEnd = candidate.IndexOf("*/", 2, StringComparison.Ordinal);
                if (commentEnd < 0)
                {
                    insideBlockComment = true;
                    candidate = String.Empty;
                    break;
                }

                candidate = candidate.Substring(commentEnd + 2).Trim();
            }

            if (String.IsNullOrWhiteSpace(candidate) || candidate.StartsWith("//", StringComparison.Ordinal))
                continue;

            exact.Add(new DedupMetricLineRecord { PhysicalLine = lineIndex + 1, Canonical = candidate });
            string normalizedLine = Normalize(candidate);
            if (!String.IsNullOrWhiteSpace(normalizedLine))
                normalized.Add(new DedupMetricLineRecord { PhysicalLine = lineIndex + 1, Canonical = normalizedLine });
        }

        var normalizedBuilder = new StringBuilder();
        for (int index = 0; index < normalized.Count; index++)
        {
            if (index > 0)
                normalizedBuilder.Append('\n');
            normalizedBuilder.Append(normalized[index].Canonical);
        }

        return new DedupMetricSourceAnalysis
        {
            PhysicalLines = lines.Length,
            NonBlankLines = nonBlankLines,
            Exact = exact.ToArray(),
            Normalized = normalized.ToArray(),
            NormalizedSha256 = HashText(normalizedBuilder.ToString())
        };
    }

    public static string GetWindowHash(DedupMetricLineRecord[] records, int start, int length, int minimumCharacters)
    {
        var builder = new StringBuilder();
        for (int index = start; index < start + length; index++)
        {
            if (index > start)
                builder.Append('\n');
            builder.Append(records[index].Canonical);
        }

        string text = builder.ToString();
        return text.Length >= minimumCharacters ? HashText(text) : null;
    }

    public static void AddWindows(
        Dictionary<string, List<DedupMetricWindowOccurrence>> fingerprints,
        DedupMetricLineRecord[] records,
        string rootId,
        string path,
        int blockSize,
        int minimumCharacters)
    {
        for (int start = 0; start <= records.Length - blockSize; start++)
        {
            string hash = GetWindowHash(records, start, blockSize, minimumCharacters);
            if (hash == null)
                continue;

            List<DedupMetricWindowOccurrence> occurrences;
            if (!fingerprints.TryGetValue(hash, out occurrences))
            {
                occurrences = new List<DedupMetricWindowOccurrence>();
                fingerprints.Add(hash, occurrences);
            }
            occurrences.Add(new DedupMetricWindowOccurrence { RootId = rootId, Path = path, Start = start });
        }
    }

    public static int GetCharacterCount(DedupMetricLineRecord[] records, int start, int length)
    {
        int count = 0;
        for (int index = start; index < start + length; index++)
            count += records[index].Canonical.Length;
        return count;
    }

    private static string Normalize(string line)
    {
        string value = VerbatimString.Replace(line.Trim(), "\"<string>\"");
        value = RegularString.Replace(value, "\"<string>\"");
        value = Character.Replace(value, "'<char>'");
        value = LineComment.Replace(value, String.Empty);
        value = Number.Replace(value, "<number>");
        value = AppName.Replace(value, "FreeApp");
        value = FrameworkName.Replace(value, "Renderer");
        return Whitespace.Replace(value, String.Empty);
    }

    private static string HashText(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        byte[] hash;
        lock (WindowSha256)
            hash = WindowSha256.ComputeHash(bytes);
        return BitConverter.ToString(hash).Replace("-", String.Empty).ToLowerInvariant();
    }
}
'@
}

function Get-GitOutput {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    $output = & git -C $repoRoot @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed in $repoRoot."
    }

    return @($output)
}

function Get-GitValue {
    param([Parameter(Mandatory = $true)][string[]]$Arguments)

    return [string](@(Get-GitOutput -Arguments $Arguments) -join "`n").Trim()
}

function Get-Sha256Text {
    param([AllowEmptyString()][Parameter(Mandatory = $true)][string]$Text)

    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $hash = $sha256.ComputeHash($bytes)
    return ([System.BitConverter]::ToString($hash)).Replace("-", "").ToLowerInvariant()
}

function Get-Sha256File {
    param([Parameter(Mandatory = $true)][string]$Path)

    $stream = [System.IO.File]::OpenRead($Path)
    try {
        $hash = $sha256.ComputeHash($stream)
        return ([System.BitConverter]::ToString($hash)).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $stream.Dispose()
    }
}

function Test-ExcludedSourceFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RelativePath
    )

    $segments = $RelativePath -split '/'
    foreach ($directoryName in @("bin", "obj", ".git", ".worktrees", ".claude", "generated")) {
        if ($segments -contains $directoryName) {
            return $true
        }
    }

    $name = [System.IO.Path]::GetFileName($Path)
    return $name -match '(?i)(?:\.g(?:\.i)?|\.generated|\.designer)\.cs$' -or
        $name -in @("AssemblyInfo.cs", "GlobalUsings.g.cs")
}

function Get-RendererFiles {
    param([Parameter(Mandatory = $true)]$RootSpecs)

    $files = New-Object 'System.Collections.Generic.List[object]'
    foreach ($rootSpec in $RootSpecs) {
        $rootPath = Resolve-ToolRepoPath -Path $rootSpec.path -RepoRoot $repoRoot
        if (-not (Test-Path -LiteralPath $rootPath -PathType Container)) {
            throw "Renderer root is missing: $($rootSpec.path)"
        }

        $sourceFiles = @(Get-ChildItem -LiteralPath $rootPath -Filter "*.cs" -File -Recurse | Sort-Object FullName)
        foreach ($sourceFile in $sourceFiles) {
            $relativePath = Get-ToolRelativePath -RootPath $repoRoot -Path $sourceFile.FullName
            if (Test-ExcludedSourceFile -Path $sourceFile.FullName -RelativePath $relativePath) {
                continue
            }

            $analysis = [DedupMetricSourceAnalyzer]::Analyze($sourceFile.FullName)

            $files.Add([pscustomobject][ordered]@{
                    rootId = $rootSpec.id
                    app = $rootSpec.app
                    platform = $rootSpec.platform
                    rootPath = $rootSpec.path
                    path = $relativePath
                    fullPath = $sourceFile.FullName
                    physicalLines = $analysis.PhysicalLines
                    nonBlankLines = $analysis.NonBlankLines
                    codeLines = $analysis.Exact.Count
                    exactRecords = $analysis.Exact
                    normalizedRecords = $analysis.Normalized
                    sha256 = Get-Sha256File -Path $sourceFile.FullName
                    normalizedSha256 = $analysis.NormalizedSha256
                })
        }
    }

    return @($files | Sort-Object path)
}

function Get-BlockIdentity {
    param([Parameter(Mandatory = $true)]$Block)

    return "$($Block.fileA):$($Block.startLineA)-$($Block.endLineA)|$($Block.fileB):$($Block.startLineB)-$($Block.endLineB)"
}

function Get-DuplicateAnalysis {
    param(
        [Parameter(Mandatory = $true)]$Files,
        [Parameter(Mandatory = $true)][ValidateSet("exact", "normalized")][string]$Mode
    )

    $fingerprints = New-Object 'System.Collections.Generic.Dictionary[string,System.Collections.Generic.List[DedupMetricWindowOccurrence]]' ([System.StringComparer]::Ordinal)
    $fileByPath = @{}
    $coverageByFile = @{}

    foreach ($file in $Files) {
        $fileByPath[$file.path] = $file
        $coverageByFile[$file.path] = New-Object 'System.Collections.Generic.HashSet[int]'
        [DedupMetricLineRecord[]]$records = @(if ($Mode -eq "exact") { $file.exactRecords } else { $file.normalizedRecords })
        if ($records.Count -lt $BlockSize) {
            continue
        }

        [DedupMetricSourceAnalyzer]::AddWindows(
            $fingerprints,
            $records,
            [string]$file.rootId,
            [string]$file.path,
            $BlockSize,
            $MinimumBlockCharacters)
    }

    $pairRuns = @{}
    $crossRootFingerprintCount = 0
    $highFrequencyFingerprintCount = 0
    foreach ($fingerprintHash in @($fingerprints.Keys | Sort-Object)) {
        $occurrences = @($fingerprints[$fingerprintHash] | Sort-Object path, start)
        $rootCount = @($occurrences.rootId | Sort-Object -Unique).Count
        if ($rootCount -lt 2) {
            continue
        }

        $crossRootFingerprintCount++
        foreach ($occurrence in $occurrences) {
            [DedupMetricLineRecord[]]$records = @(if ($Mode -eq "exact") {
                    $fileByPath[$occurrence.path].exactRecords
                }
                else {
                    $fileByPath[$occurrence.path].normalizedRecords
                })
            for ($lineOffset = 0; $lineOffset -lt $BlockSize; $lineOffset++) {
                [void]$coverageByFile[$occurrence.path].Add([int]$records[$occurrence.start + $lineOffset].physicalLine)
            }
        }

        if ($occurrences.Count -gt $MaximumFingerprintOccurrences) {
            $highFrequencyFingerprintCount++
            continue
        }

        for ($leftIndex = 0; $leftIndex -lt $occurrences.Count; $leftIndex++) {
            for ($rightIndex = $leftIndex + 1; $rightIndex -lt $occurrences.Count; $rightIndex++) {
                $left = $occurrences[$leftIndex]
                $right = $occurrences[$rightIndex]
                if ($left.rootId -eq $right.rootId) {
                    continue
                }

                $fileA = $left
                $fileB = $right
                if ([string]::CompareOrdinal($fileA.path, $fileB.path) -gt 0) {
                    $fileA = $right
                    $fileB = $left
                }

                $delta = [int]$fileB.start - [int]$fileA.start
                $pairKey = "$($fileA.path)`u{001f}$($fileB.path)`u{001f}$delta"
                if (-not $pairRuns.ContainsKey($pairKey)) {
                    $pairRuns[$pairKey] = [pscustomobject][ordered]@{
                        fileA = $fileA.path
                        fileB = $fileB.path
                        delta = $delta
                        starts = New-Object 'System.Collections.Generic.HashSet[int]'
                    }
                }
                [void]$pairRuns[$pairKey].starts.Add([int]$fileA.start)
            }
        }
    }

    $blocks = New-Object 'System.Collections.Generic.List[object]'
    foreach ($pairKey in @($pairRuns.Keys | Sort-Object)) {
        $pair = $pairRuns[$pairKey]
        $starts = @($pair.starts | Sort-Object)
        if ($starts.Count -eq 0) {
            continue
        }

        $runStart = [int]$starts[0]
        $previous = $runStart
        for ($index = 1; $index -le $starts.Count; $index++) {
            $isRunEnd = $index -eq $starts.Count -or [int]$starts[$index] -ne ($previous + 1)
            if (-not $isRunEnd) {
                $previous = [int]$starts[$index]
                continue
            }

            $endStart = $previous
            $startA = $runStart
            $endA = $endStart + $BlockSize - 1
            $startB = $startA + [int]$pair.delta
            $endB = $endA + [int]$pair.delta
            [DedupMetricLineRecord[]]$recordsA = @(if ($Mode -eq "exact") { $fileByPath[$pair.fileA].exactRecords } else { $fileByPath[$pair.fileA].normalizedRecords })
            [DedupMetricLineRecord[]]$recordsB = @(if ($Mode -eq "exact") { $fileByPath[$pair.fileB].exactRecords } else { $fileByPath[$pair.fileB].normalizedRecords })
            $characterCount = [DedupMetricSourceAnalyzer]::GetCharacterCount($recordsA, $startA, $endA - $startA + 1)

            $blocks.Add([pscustomobject][ordered]@{
                    mode = $Mode
                    rootA = $fileByPath[$pair.fileA].rootId
                    fileA = $pair.fileA
                    startLineA = [int]$recordsA[$startA].physicalLine
                    endLineA = [int]$recordsA[$endA].physicalLine
                    rootB = $fileByPath[$pair.fileB].rootId
                    fileB = $pair.fileB
                    startLineB = [int]$recordsB[$startB].physicalLine
                    endLineB = [int]$recordsB[$endB].physicalLine
                    sourceLineCount = $endA - $startA + 1
                    normalizedCharacterCount = $characterCount
                })

            if ($index -lt $starts.Count) {
                $runStart = [int]$starts[$index]
                $previous = $runStart
            }
        }
    }

    $orderedBlocks = @($blocks | Sort-Object @{ Expression = "sourceLineCount"; Descending = $true }, fileA, startLineA, fileB, startLineB)
    $coverageByRoot = New-Object 'System.Collections.Generic.List[object]'
    foreach ($rootId in @($Files.rootId | Sort-Object -Unique)) {
        $rootFiles = @($Files | Where-Object { $_.rootId -eq $rootId })
        $codeLines = [int](($rootFiles | Measure-Object -Property codeLines -Sum).Sum)
        $duplicateLines = 0
        foreach ($file in $rootFiles) {
            $duplicateLines += $coverageByFile[$file.path].Count
        }
        $coverageByRoot.Add([pscustomobject][ordered]@{
                rootId = $rootId
                codeLines = $codeLines
                duplicateLines = $duplicateLines
                coveragePercent = if ($codeLines -eq 0) { 0.0 } else { [math]::Round(100.0 * $duplicateLines / $codeLines, 6) }
            })
    }

    $totalCodeLines = [int](($Files | Measure-Object -Property codeLines -Sum).Sum)
    $totalDuplicateLines = 0
    foreach ($file in $Files) {
        $totalDuplicateLines += $coverageByFile[$file.path].Count
    }

    return [pscustomobject][ordered]@{
        mode = $Mode
        crossRootFingerprintCount = $crossRootFingerprintCount
        highFrequencyFingerprintCount = $highFrequencyFingerprintCount
        blockCount = $orderedBlocks.Count
        coverage = [pscustomobject][ordered]@{
            codeLines = $totalCodeLines
            duplicateLines = $totalDuplicateLines
            percent = if ($totalCodeLines -eq 0) { 0.0 } else { [math]::Round(100.0 * $totalDuplicateLines / $totalCodeLines, 6) }
            byRoot = $coverageByRoot.ToArray()
        }
        blocks = $orderedBlocks
    }
}

function Get-WholeFileDuplicateGroups {
    param(
        [Parameter(Mandatory = $true)]$Files,
        [Parameter(Mandatory = $true)][ValidateSet("sha256", "normalizedSha256")][string]$HashProperty
    )

    $groups = New-Object 'System.Collections.Generic.List[object]'
    foreach ($hashGroup in @($Files | Group-Object -Property $HashProperty | Sort-Object Name)) {
        $groupFiles = @($hashGroup.Group | Sort-Object path)
        if ($groupFiles.Count -lt 2 -or @($groupFiles.rootId | Sort-Object -Unique).Count -lt 2) {
            continue
        }

        $groups.Add([pscustomobject][ordered]@{
                sha256 = $hashGroup.Name
                files = @($groupFiles | ForEach-Object {
                        [pscustomobject][ordered]@{
                            rootId = $_.rootId
                            path = $_.path
                        }
                    })
            })
    }

    return $groups.ToArray()
}

function Get-LocalizationCatalog {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $fullPath = Resolve-ToolRepoPath -Path $Path -RepoRoot $repoRoot
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Localization catalog is missing: $Path"
    }

    [xml]$document = [System.IO.File]::ReadAllText($fullPath)
    $values = New-Object 'System.Collections.Generic.Dictionary[string,System.Collections.Generic.List[string]]' ([System.StringComparer]::Ordinal)
    $keyCount = 0
    foreach ($dataNode in @($document.root.data | Sort-Object name)) {
        $key = [string]$dataNode.name
        $value = ([string]$dataNode.value).Trim()
        if ([string]::IsNullOrWhiteSpace($key)) {
            continue
        }
        $keyCount++
        if ([string]::IsNullOrWhiteSpace($value)) {
            continue
        }
        if (-not $values.ContainsKey($value)) {
            $values[$value] = New-Object 'System.Collections.Generic.List[string]'
        }
        $values[$value].Add($key)
    }

    return [pscustomobject][ordered]@{
        name = $Name
        path = $Path.Replace('\', '/')
        fullPath = $fullPath
        keyCount = $keyCount
        uniqueValueCount = $values.get_Count()
        values = $values
        sha256 = Get-Sha256File -Path $fullPath
    }
}

function Get-CommonLocalizationValues {
    param(
        [Parameter(Mandatory = $true)]$Catalogs,
        [Parameter(Mandatory = $true)][string[]]$Names
    )

    $selected = @($Names | ForEach-Object { $Catalogs[$_] })
    $commonValues = @($selected[0].values.Keys)
    foreach ($catalog in @($selected | Select-Object -Skip 1)) {
        $commonValues = @($commonValues | Where-Object { $catalog.values.ContainsKey($_) })
    }

    return @($commonValues | Sort-Object)
}

function ConvertTo-LocalizationValueRows {
    param(
        [Parameter(Mandatory = $true)]$Catalogs,
        [Parameter(Mandatory = $true)][string[]]$Names,
        [AllowEmptyCollection()][Parameter(Mandatory = $true)][string[]]$Values
    )

    return @($Values | ForEach-Object {
            $value = $_
            $keys = [ordered]@{}
            foreach ($name in $Names) {
                $keys[$name] = @($Catalogs[$name].values[$value] | Sort-Object)
            }
            [pscustomobject][ordered]@{
                value = $value
                keys = $keys
            }
        })
}

function Get-GitLocDelta {
    param(
        [Parameter(Mandatory = $true)][string]$BaseCommit,
        [Parameter(Mandatory = $true)][string]$TargetCommit,
        [Parameter(Mandatory = $true)][string[]]$PathSpecs
    )

    $arguments = @("diff", "--numstat", "$BaseCommit..$TargetCommit", "--") + $PathSpecs
    $added = 0
    $deleted = 0
    $files = 0
    foreach ($line in @(Get-GitOutput -Arguments $arguments)) {
        $parts = $line -split "`t", 3
        if ($parts.Count -ne 3 -or $parts[0] -eq "-" -or $parts[1] -eq "-") {
            continue
        }
        $added += [int]$parts[0]
        $deleted += [int]$parts[1]
        $files++
    }

    return [pscustomobject][ordered]@{
        filesChanged = $files
        addedLines = $added
        deletedLines = $deleted
        netLines = $added - $deleted
    }
}

function Format-Percent {
    param([Parameter(Mandatory = $true)][double]$Value)

    return $Value.ToString("0.000000", [System.Globalization.CultureInfo]::InvariantCulture)
}

function ConvertTo-MetricMarkdownCell {
    param([AllowNull()][string]$Value)

    if ($null -eq $Value) {
        return ""
    }
    return (ConvertTo-ToolMarkdownCell -Value ($Value -replace "`r?`n", "<br>"))
}

try {
    $analysisRevisionLabel = $AnalysisRevision
    if ($Check -and [string]::IsNullOrWhiteSpace($AnalysisRevision) -and (Test-Path -LiteralPath $resolvedJsonPath -PathType Leaf)) {
        $existingReport = Get-Content -LiteralPath $resolvedJsonPath -Raw | ConvertFrom-Json
        $analysisRevisionLabel = [string]$existingReport.repository.analysisRevision
        $AnalysisRevision = [string]$existingReport.repository.analysisCommit
    }
    if ([string]::IsNullOrWhiteSpace($AnalysisRevision)) {
        $AnalysisRevision = "HEAD"
    }
    if ([string]::IsNullOrWhiteSpace($analysisRevisionLabel)) {
        $analysisRevisionLabel = $AnalysisRevision
    }

    $analysisCommit = Get-GitValue -Arguments @("rev-parse", "$AnalysisRevision^{commit}")
    $upstreamCommit = Get-GitValue -Arguments @("rev-parse", "$UpstreamRevision^{commit}")
    $mergeBaseCommit = Get-GitValue -Arguments @("merge-base", $analysisCommit, $upstreamCommit)

    $rendererRoots = @(
        [pscustomobject][ordered]@{ id = "FreeX.Wpf"; app = "FreeX"; platform = "WPF"; path = "src/FreeX.App.Host" },
        [pscustomobject][ordered]@{ id = "FreeX.Avalonia"; app = "FreeX"; platform = "Avalonia"; path = "src/FreeX.App.Avalonia" },
        [pscustomobject][ordered]@{ id = "FreeW.Wpf"; app = "FreeW"; platform = "WPF"; path = "freew/FreeW.App.Host" },
        [pscustomobject][ordered]@{ id = "FreeW.Avalonia"; app = "FreeW"; platform = "Avalonia"; path = "freew/FreeW.App.Avalonia" },
        [pscustomobject][ordered]@{ id = "FreeP.Wpf.App"; app = "FreeP"; platform = "WPF"; path = "freep/FreeP.App.Host" },
        [pscustomobject][ordered]@{ id = "FreeP.Wpf.Rendering"; app = "FreeP"; platform = "WPF"; path = "freep/FreeP.App.Rendering.Wpf" },
        [pscustomobject][ordered]@{ id = "FreeP.Avalonia.App"; app = "FreeP"; platform = "Avalonia"; path = "freep/FreeP.App.Avalonia" },
        [pscustomobject][ordered]@{ id = "FreeP.Avalonia.Rendering"; app = "FreeP"; platform = "Avalonia"; path = "freep/FreeP.App.Rendering.Avalonia" }
    )

    Write-Host "Reading renderer C# roots..."
    $rendererFiles = @(Get-RendererFiles -RootSpecs $rendererRoots)
    Write-Host "Measuring exact duplicate blocks..."
    $exactAnalysis = Get-DuplicateAnalysis -Files $rendererFiles -Mode exact
    Write-Host "Measuring normalized duplicate blocks..."
    $normalizedAnalysis = Get-DuplicateAnalysis -Files $rendererFiles -Mode normalized

    $exactBlockIds = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    foreach ($block in @($exactAnalysis.blocks)) {
        [void]$exactBlockIds.Add((Get-BlockIdentity -Block $block))
    }
    foreach ($block in @($normalizedAnalysis.blocks)) {
        $block | Add-Member -NotePropertyName exactEquivalent -NotePropertyValue ($exactBlockIds.Contains((Get-BlockIdentity -Block $block)))
    }

    $rendererByRoot = New-Object 'System.Collections.Generic.List[object]'
    foreach ($root in $rendererRoots) {
        $rootFiles = @($rendererFiles | Where-Object { $_.rootId -eq $root.id })
        $rendererByRoot.Add([pscustomobject][ordered]@{
                id = $root.id
                app = $root.app
                platform = $root.platform
                path = $root.path
                fileCount = $rootFiles.Count
                physicalLines = [int](($rootFiles | Measure-Object -Property physicalLines -Sum).Sum)
                nonBlankLines = [int](($rootFiles | Measure-Object -Property nonBlankLines -Sum).Sum)
                codeLines = [int](($rootFiles | Measure-Object -Property codeLines -Sum).Sum)
            })
    }

    $localizationSpecs = @(
        [pscustomobject][ordered]@{ name = "Shared"; path = "shared/Free.Shared.Localization/Resources/Strings.resx" },
        [pscustomobject][ordered]@{ name = "FreeX"; path = "src/FreeX.App.Localization/Resources/Strings.resx" },
        [pscustomobject][ordered]@{ name = "FreeW"; path = "freew/FreeW.App.Localization/Resources/Strings.resx" },
        [pscustomobject][ordered]@{ name = "FreeP"; path = "freep/FreeP.App.Localization/Resources/Strings.resx" }
    )
    $catalogs = @{}
    foreach ($spec in $localizationSpecs) {
        $catalogs[$spec.name] = Get-LocalizationCatalog -Name $spec.name -Path $spec.path
    }

    $pairwiseLocalization = New-Object 'System.Collections.Generic.List[object]'
    $catalogNames = @("Shared", "FreeX", "FreeW", "FreeP")
    for ($leftIndex = 0; $leftIndex -lt $catalogNames.Count; $leftIndex++) {
        for ($rightIndex = $leftIndex + 1; $rightIndex -lt $catalogNames.Count; $rightIndex++) {
            $names = @($catalogNames[$leftIndex], $catalogNames[$rightIndex])
            $common = @(Get-CommonLocalizationValues -Catalogs $catalogs -Names $names)
            $pairwiseLocalization.Add([pscustomobject][ordered]@{
                    catalogA = $names[0]
                    catalogB = $names[1]
                    commonValueCount = $common.Count
                    commonValues = @(ConvertTo-LocalizationValueRows -Catalogs $catalogs -Names $names -Values $common)
                })
        }
    }

    $productCatalogNames = @("FreeX", "FreeW", "FreeP")
    $allProductCommon = @(Get-CommonLocalizationValues -Catalogs $catalogs -Names $productCatalogNames)
    $allProductAbsentShared = @($allProductCommon | Where-Object { -not $catalogs.Shared.values.ContainsKey($_) })

    $sharedProjectRoot = Resolve-ToolRepoPath -Path "shared" -RepoRoot $repoRoot
    $sharedProjects = @(Get-ChildItem -LiteralPath $sharedProjectRoot -Filter "*.csproj" -File -Recurse |
            Where-Object { -not (Test-ToolExcludedPath -Path $_.FullName -RepoRoot $repoRoot) } |
            ForEach-Object { Get-ToolRelativePath -RootPath $repoRoot -Path $_.FullName } |
            Sort-Object)

    $rendererPathSpecs = @($rendererRoots | ForEach-Object { ":(glob)$($_.path)/**/*.cs" })
    $allCSharpDelta = Get-GitLocDelta -BaseCommit $mergeBaseCommit -TargetCommit $analysisCommit -PathSpecs @("`:(glob)**/*.cs")
    $rendererCSharpDelta = Get-GitLocDelta -BaseCommit $mergeBaseCommit -TargetCommit $analysisCommit -PathSpecs $rendererPathSpecs

    $inputHashLines = New-Object 'System.Collections.Generic.List[string]'
    foreach ($file in $rendererFiles) {
        $inputHashLines.Add("renderer|$($file.path)|$($file.sha256)")
    }
    foreach ($spec in $localizationSpecs) {
        $catalog = $catalogs[$spec.name]
        $inputHashLines.Add("localization|$($catalog.path)|$($catalog.sha256)")
    }
    foreach ($projectPath in $sharedProjects) {
        $projectFullPath = Resolve-ToolRepoPath -Path $projectPath -RepoRoot $repoRoot
        $inputHashLines.Add("shared-project|$projectPath|$(Get-Sha256File -Path $projectFullPath)")
    }
    $inputHashLines.Add("parameters|$BlockSize|$MinimumBlockCharacters|$MaximumFingerprintOccurrences|$TopCandidateCount")
    $inputTreeSha256 = Get-Sha256Text -Text (@($inputHashLines | Sort-Object) -join "`n")

    $fileRows = @($rendererFiles | ForEach-Object {
            [pscustomobject][ordered]@{
                rootId = $_.rootId
                path = $_.path
                physicalLines = $_.physicalLines
                nonBlankLines = $_.nonBlankLines
                codeLines = $_.codeLines
                sha256 = $_.sha256
                normalizedSha256 = $_.normalizedSha256
            }
        })

    $report = [pscustomobject][ordered]@{
        schema = "freex.dedup-residual-metrics.v1"
        generatedBy = "tools/Measure-DedupResiduals.ps1"
        parameters = [pscustomobject][ordered]@{
            blockSize = $BlockSize
            minimumBlockCharacters = $MinimumBlockCharacters
            maximumFingerprintOccurrences = $MaximumFingerprintOccurrences
            topCandidateCount = $TopCandidateCount
            exactNormalization = "trim lines; omit blank and comment-only lines"
            lexicalNormalization = "exact normalization plus string/char/number literal folding, FreeX/FreeW/FreeP folding, WPF/Avalonia folding, line-comment removal, and whitespace removal"
            coverageDenominator = "nonblank, non-comment-only renderer C# lines"
            duplicateBoundary = "matching windows must occur in distinct configured renderer roots"
            excludedDirectories = @("bin", "obj", ".git", ".worktrees", ".claude", "generated")
            excludedFilePatterns = @("*.g.cs", "*.g.i.cs", "*.generated.cs", "*.designer.cs", "AssemblyInfo.cs", "GlobalUsings.g.cs")
        }
        repository = [pscustomobject][ordered]@{
            analysisRevision = $analysisRevisionLabel
            analysisCommit = $analysisCommit
            upstreamRevision = $UpstreamRevision
            upstreamCommit = $upstreamCommit
            mergeBaseCommit = $mergeBaseCommit
            inputTreeSha256 = $inputTreeSha256
            campaignLocDelta = [pscustomobject][ordered]@{
                allCSharp = $allCSharpDelta
                rendererCSharp = $rendererCSharpDelta
            }
        }
        renderer = [pscustomobject][ordered]@{
                roots = $rendererByRoot.ToArray()
            totals = [pscustomobject][ordered]@{
                fileCount = $rendererFiles.Count
                physicalLines = [int](($rendererFiles | Measure-Object -Property physicalLines -Sum).Sum)
                nonBlankLines = [int](($rendererFiles | Measure-Object -Property nonBlankLines -Sum).Sum)
                codeLines = [int](($rendererFiles | Measure-Object -Property codeLines -Sum).Sum)
            }
            duplicateCoverage = [pscustomobject][ordered]@{
                exact = $exactAnalysis.coverage
                normalized = $normalizedAnalysis.coverage
            }
            duplicateFingerprints = [pscustomobject][ordered]@{
                exact = [pscustomobject][ordered]@{
                    crossRootCount = $exactAnalysis.crossRootFingerprintCount
                    highFrequencyCount = $exactAnalysis.highFrequencyFingerprintCount
                }
                normalized = [pscustomobject][ordered]@{
                    crossRootCount = $normalizedAnalysis.crossRootFingerprintCount
                    highFrequencyCount = $normalizedAnalysis.highFrequencyFingerprintCount
                }
            }
            wholeFileDuplicates = [pscustomobject][ordered]@{
                exact = @(Get-WholeFileDuplicateGroups -Files $rendererFiles -HashProperty sha256)
                normalized = @(Get-WholeFileDuplicateGroups -Files $rendererFiles -HashProperty normalizedSha256)
            }
            duplicateBlocks = [pscustomobject][ordered]@{
                exact = @($exactAnalysis.blocks)
                normalized = @($normalizedAnalysis.blocks)
            }
            files = $fileRows
        }
        localization = [pscustomobject][ordered]@{
            catalogs = @($catalogNames | ForEach-Object {
                    $catalog = $catalogs[$_]
                    [pscustomobject][ordered]@{
                        name = $catalog.name
                        path = $catalog.path
                        keyCount = $catalog.keyCount
                        uniqueValueCount = $catalog.uniqueValueCount
                        sha256 = $catalog.sha256
                    }
                })
            pairwiseValueOverlap = $pairwiseLocalization.ToArray()
            allProductCommonValues = @(ConvertTo-LocalizationValueRows -Catalogs $catalogs -Names $productCatalogNames -Values $allProductCommon)
            allProductCommonValuesAbsentFromShared = @(ConvertTo-LocalizationValueRows -Catalogs $catalogs -Names $productCatalogNames -Values $allProductAbsentShared)
        }
        sharedProjects = [pscustomobject][ordered]@{
            count = $sharedProjects.Count
            paths = $sharedProjects
        }
    }

    $jsonContent = ($report | ConvertTo-Json -Depth 100) -replace "`r`n", "`n"
    $jsonContent = $jsonContent.TrimEnd() + "`n"

    $markdown = New-Object 'System.Collections.Generic.List[string]'
    $markdown.Add("# Dedup residual metrics")
    $markdown.Add("")
    $markdown.Add("Deterministic renderer duplication evidence generated by ``tools/Measure-DedupResiduals.ps1``. Normalized matches are lexical candidates for human classification, not proof that two renderer blocks have the same behavior.")
    $markdown.Add("")
    $markdown.Add("## Measurement contract")
    $markdown.Add("")
    $markdown.Add("- Analysis commit: ``$analysisCommit``")
    $markdown.Add("- Upstream: ``$UpstreamRevision`` at ``$upstreamCommit``")
    $markdown.Add("- Merge base: ``$mergeBaseCommit``")
    $markdown.Add("- Input tree SHA-256: ``$inputTreeSha256``")
    $markdown.Add("- Window: $BlockSize significant lines, at least $MinimumBlockCharacters canonical characters")
    $markdown.Add("- High-frequency reporting cap: $MaximumFingerprintOccurrences occurrences per fingerprint; coverage still includes capped fingerprints")
    $markdown.Add("- Exclusions: ``bin``, ``obj``, Git/worktree metadata, generated directories, and generated C# filename patterns")
    $markdown.Add("")
    $markdown.Add("## Renderer summary")
    $markdown.Add("")
    $markdown.Add("| Renderer root | Files | Code LOC | Exact duplicate LOC | Exact coverage | Normalized duplicate LOC | Normalized coverage |")
    $markdown.Add("|---|---:|---:|---:|---:|---:|---:|")
    foreach ($root in $rendererByRoot) {
        $exactRoot = @($exactAnalysis.coverage.byRoot | Where-Object { $_.rootId -eq $root.id })[0]
        $normalizedRoot = @($normalizedAnalysis.coverage.byRoot | Where-Object { $_.rootId -eq $root.id })[0]
        $markdown.Add("| ``$($root.id)`` | $($root.fileCount) | $($root.codeLines) | $($exactRoot.duplicateLines) | $(Format-Percent $exactRoot.coveragePercent)% | $($normalizedRoot.duplicateLines) | $(Format-Percent $normalizedRoot.coveragePercent)% |")
    }
    $markdown.Add("| **Total** | **$($rendererFiles.Count)** | **$($report.renderer.totals.codeLines)** | **$($exactAnalysis.coverage.duplicateLines)** | **$(Format-Percent $exactAnalysis.coverage.percent)%** | **$($normalizedAnalysis.coverage.duplicateLines)** | **$(Format-Percent $normalizedAnalysis.coverage.percent)%** |")
    $markdown.Add("")
    $markdown.Add("## Campaign LOC delta")
    $markdown.Add("")
    $markdown.Add("Measured from merge base ``$mergeBaseCommit`` to analysis commit ``$analysisCommit``.")
    $markdown.Add("")
    $markdown.Add("| Scope | Files changed | Added | Deleted | Net |")
    $markdown.Add("|---|---:|---:|---:|---:|")
    $markdown.Add("| All C# | $($allCSharpDelta.filesChanged) | $($allCSharpDelta.addedLines) | $($allCSharpDelta.deletedLines) | $($allCSharpDelta.netLines) |")
    $markdown.Add("| Renderer C# | $($rendererCSharpDelta.filesChanged) | $($rendererCSharpDelta.addedLines) | $($rendererCSharpDelta.deletedLines) | $($rendererCSharpDelta.netLines) |")

    foreach ($candidateMode in @("exact", "normalized")) {
        $markdown.Add("")
        $title = if ($candidateMode -eq "exact") { "Top exact residual candidates" } else { "Top normalized residual candidates" }
        $markdown.Add("## $title")
        $markdown.Add("")
        $candidates = @(if ($candidateMode -eq "exact") {
                $exactAnalysis.blocks | Select-Object -First $TopCandidateCount
            }
            else {
                $normalizedOnly = @($normalizedAnalysis.blocks | Where-Object { -not $_.exactEquivalent })
                $normalizedOnly | Select-Object -First $TopCandidateCount
            })
        if ($candidates.Count -eq 0) {
            $markdown.Add("No cross-root $candidateMode candidates met the configured threshold.")
            continue
        }
        $markdown.Add("| Lines | Renderer A | Renderer B | Exact equivalent |")
        $markdown.Add("|---:|---|---|:---:|")
        foreach ($candidate in $candidates) {
            $exactEquivalent = if ($candidate.PSObject.Properties.Name -contains "exactEquivalent") {
                if ($candidate.exactEquivalent) { "yes" } else { "no" }
            }
            else {
                "yes"
            }
            $rangeA = "``$($candidate.fileA):$($candidate.startLineA)-$($candidate.endLineA)``"
            $rangeB = "``$($candidate.fileB):$($candidate.startLineB)-$($candidate.endLineB)``"
            $markdown.Add("| $($candidate.sourceLineCount) | $rangeA | $rangeB | $exactEquivalent |")
        }
    }

    $markdown.Add("")
    $markdown.Add("## Whole-file duplicates")
    $markdown.Add("")
    $exactWholeFileGroups = @($report.renderer.wholeFileDuplicates.exact)
    $normalizedWholeFileGroups = @($report.renderer.wholeFileDuplicates.normalized)
    $markdown.Add("- Exact cross-root groups: $($exactWholeFileGroups.Count)")
    $markdown.Add("- Normalized cross-root groups: $($normalizedWholeFileGroups.Count)")

    $markdown.Add("")
    $markdown.Add("## Localization value overlap")
    $markdown.Add("")
    $markdown.Add("Exact, trimmed base-catalog values are compared case-sensitively; localized satellite catalogs are intentionally excluded.")
    $markdown.Add("")
    $markdown.Add("| Catalog A | Catalog B | Common values |")
    $markdown.Add("|---|---|---:|")
    foreach ($pair in $pairwiseLocalization) {
        $markdown.Add("| $($pair.catalogA) | $($pair.catalogB) | $($pair.commonValueCount) |")
    }
    $markdown.Add("")
    $markdown.Add("Values common to FreeX, FreeW, and FreeP but absent from Shared: $($allProductAbsentShared.Count).")
    foreach ($row in @(ConvertTo-LocalizationValueRows -Catalogs $catalogs -Names $productCatalogNames -Values $allProductAbsentShared | Select-Object -First $TopCandidateCount)) {
        $markdown.Add("- ``$(ConvertTo-MetricMarkdownCell -Value $row.value)``")
    }

    $markdown.Add("")
    $markdown.Add("## Shared projects")
    $markdown.Add("")
    $markdown.Add("Shared project count: $($sharedProjects.Count).")
    foreach ($projectPath in $sharedProjects) {
        $markdown.Add("- ``$projectPath``")
    }

    $markdownContent = ($markdown -join "`n").TrimEnd() + "`n"

    if ($Check) {
        Test-ToolGeneratedContentMatches -ExpectedContent $jsonContent -ActualPath $resolvedJsonPath -Label "Dedup residual JSON metrics" -GeneratorScriptName "tools/Measure-DedupResiduals.ps1" -NormalizeNewlines
        Test-ToolGeneratedContentMatches -ExpectedContent $markdownContent -ActualPath $resolvedMarkdownPath -Label "Dedup residual Markdown metrics" -GeneratorScriptName "tools/Measure-DedupResiduals.ps1" -NormalizeNewlines
        Write-Host "Dedup residual metrics are deterministic and current."
    }
    else {
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedJsonPath)) | Out-Null
        [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($resolvedMarkdownPath)) | Out-Null
        [System.IO.File]::WriteAllText($resolvedJsonPath, $jsonContent, $utf8NoBom)
        [System.IO.File]::WriteAllText($resolvedMarkdownPath, $markdownContent, $utf8NoBom)
        Write-Host "Wrote $resolvedJsonPath"
        Write-Host "Wrote $resolvedMarkdownPath"
    }
}
finally {
    $sha256.Dispose()
}
