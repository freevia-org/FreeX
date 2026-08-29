[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$ProjectPath,

    [Parameter(Mandatory = $true)]
    [ValidateRange(0, 63)]
    [int]$PartitionIndex,

    [Parameter(Mandatory = $true)]
    [ValidateRange(2, 64)]
    [int]$PartitionCount
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($PartitionIndex -ge $PartitionCount) {
    throw "Partition index $PartitionIndex must be less than partition count $PartitionCount."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectFullPath = Join-Path $repoRoot $ProjectPath
if (-not (Test-Path -LiteralPath $projectFullPath -PathType Leaf)) {
    throw "Test project was not found: $ProjectPath"
}

$projectDirectory = Split-Path -Parent $projectFullPath
$namespacePattern = '(?m)^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*[;{]'
$classPattern = '(?m)^\s*(?:public|internal)?\s*(?:(?:sealed|static|partial|abstract)\s+)*class\s+([A-Za-z_][A-Za-z0-9_]*)'
$factAttributePattern = '\[Fact(?:Attribute)?(?:\(|\])'
$theoryAttributePattern = '\[Theory(?:Attribute)?(?:\(|\])'
$inlineDataAttributePattern = '\[InlineData(?:Attribute)?(?:\(|\])'
$candidates = [System.Collections.Generic.List[object]]::new()

foreach ($sourceFile in @(Get-ChildItem -LiteralPath $projectDirectory -Recurse -File -Filter '*.cs' |
    Where-Object { $_.FullName -notmatch '[\\/](?:bin|obj)[\\/]' })) {
    $source = Get-Content -LiteralPath $sourceFile.FullName -Raw
    $factCount = [regex]::Matches($source, $factAttributePattern).Count
    $theoryCount = [regex]::Matches($source, $theoryAttributePattern).Count
    if ($factCount + $theoryCount -eq 0) {
        continue
    }
    $inlineDataCount = [regex]::Matches($source, $inlineDataAttributePattern).Count

    $namespaceMatch = [regex]::Match($source, $namespacePattern)
    if (-not $namespaceMatch.Success) {
        throw "Could not identify a namespace in '$($sourceFile.FullName)'."
    }
    $classNames = @(
        [regex]::Matches($source, $classPattern) |
            ForEach-Object { "$($namespaceMatch.Groups[1].Value).$($_.Groups[1].Value)" } |
            Sort-Object -Unique
    )
    if ($classNames.Count -eq 0) {
        throw "Could not identify a test class in '$($sourceFile.FullName)'."
    }

    $relativePath = $sourceFile.FullName.Substring($projectDirectory.Length + 1).Replace('\', '/')
    $candidates.Add([pscustomobject]@{
        Path = $relativePath
        # InlineData rows are independently discovered test cases. MemberData and other dynamic
        # theories retain a minimum weight of one because their row count cannot be derived safely
        # without executing user code during matrix generation.
        Weight = $factCount + [Math]::Max($theoryCount, $inlineDataCount)
        ClassNames = $classNames
    })
}

if ($candidates.Count -eq 0) {
    throw "No [Fact] or [Theory] test classes were found under '$ProjectPath'."
}

$seenClasses = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($candidate in $candidates) {
    foreach ($className in $candidate.ClassNames) {
        if (-not $seenClasses.Add($className)) {
            throw "Test class '$className' is declared by more than one test-containing source file; partition ownership would be ambiguous."
        }
    }
}

# Largest-first bin packing keeps the partitions balanced by statically discoverable test-case
# count (facts plus inline theory rows). The path tie-breaker makes the result deterministic on
# Windows, Linux, and macOS.
$partitionWeights = [int[]]::new($PartitionCount)
$partitionFiles = [object[]]::new($PartitionCount)
for ($index = 0; $index -lt $PartitionCount; $index++) {
    $partitionFiles[$index] = [System.Collections.Generic.List[object]]::new()
}
foreach ($candidate in @($candidates | Sort-Object @{ Expression = 'Weight'; Descending = $true }, @{ Expression = 'Path'; Descending = $false })) {
    $target = 0
    for ($index = 1; $index -lt $PartitionCount; $index++) {
        if ($partitionWeights[$index] -lt $partitionWeights[$target]) {
            $target = $index
        }
    }

    $partitionFiles[$target].Add($candidate)
    $partitionWeights[$target] += $candidate.Weight
}

$selectedClasses = @(
    $partitionFiles[$PartitionIndex] |
        ForEach-Object { $_.ClassNames } |
        Sort-Object -Unique
)
if ($selectedClasses.Count -eq 0) {
    throw "Partition $($PartitionIndex + 1) of $PartitionCount selected no test classes for '$ProjectPath'."
}

[xml]$project = Get-Content -LiteralPath $projectFullPath -Raw
$baseFilter = @(
    $project.Project.PropertyGroup.VSTestTestCaseFilter |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
        Select-Object -Last 1
)
$partitionFilter = '(' + (($selectedClasses | ForEach-Object { "FullyQualifiedName~$_." }) -join '|') + ')'
if ($baseFilter.Count -gt 0) {
    "($([string]$baseFilter[0]))&$partitionFilter"
}
else {
    $partitionFilter
}
