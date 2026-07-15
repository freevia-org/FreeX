param(
    [string]$ProjectRoot = "."
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Test-IsIgnoredProjectPath {
    param([Parameter(Mandatory = $true)][System.IO.FileInfo]$ProjectFile)

    if ($ProjectFile.Name -like "*_wpftmp.csproj") {
        return $true
    }

    $relativePath = Get-ToolRelativePath -RootPath $resolvedProjectRoot -Path $ProjectFile.FullName
    $segments = $relativePath -split "/"
    return $segments -contains "bin" -or
        $segments -contains "obj" -or
        $segments -contains ".git" -or
        $segments -contains ".worktrees" -or
        $segments -contains ".claude"
}

$resolvedProjectRoot = Resolve-ToolRepoPath -Path $ProjectRoot -RepoRoot $repoRoot
if (-not (Test-Path -LiteralPath $resolvedProjectRoot -PathType Container)) {
    throw "Project root was not found: $resolvedProjectRoot"
}
$resolvedProjectRootPath = [System.IO.Path]::GetFullPath($resolvedProjectRoot)
if (-not $resolvedProjectRootPath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
    $resolvedProjectRootPath += [System.IO.Path]::DirectorySeparatorChar
}

$projectFiles = @(
    Get-ToolProjectFiles -Directory (Get-Item -LiteralPath $resolvedProjectRoot) |
        Sort-Object FullName
)

if ($projectFiles.Count -eq 0) {
    throw "No .csproj files were found in $resolvedProjectRoot"
}

$missingReferences = New-Object System.Collections.Generic.List[string]
$escapedReferences = New-Object System.Collections.Generic.List[string]
$duplicateReferences = New-Object System.Collections.Generic.List[string]

foreach ($projectFile in $projectFiles) {
    [xml]$projectXml = Get-Content -LiteralPath $projectFile.FullName -Raw
    $projectReferences = @($projectXml.Project.ItemGroup.ProjectReference)
    $referencesByResolvedPath = @{}
    $relativeProjectPath = Get-ToolRelativePath -RootPath $resolvedProjectRoot -Path $projectFile.FullName

    foreach ($projectReference in $projectReferences) {
        $include = [string]$projectReference.Include
        if ([string]::IsNullOrWhiteSpace($include)) {
            continue
        }

        $referencedProjectPath = Join-Path $projectFile.DirectoryName $include
        $resolvedReferencePath = [System.IO.Path]::GetFullPath($referencedProjectPath)
        $resolvedReferenceKey = $resolvedReferencePath.ToUpperInvariant()

        if ($referencesByResolvedPath.ContainsKey($resolvedReferenceKey)) {
            $duplicateReferences.Add("${relativeProjectPath}: $include")
        } else {
            $referencesByResolvedPath[$resolvedReferenceKey] = $include
        }

        if (-not $resolvedReferencePath.StartsWith($resolvedProjectRootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            $escapedReferences.Add("${relativeProjectPath}: $include")
            continue
        }

        if (-not (Test-Path -LiteralPath $resolvedReferencePath -PathType Leaf)) {
            $missingReferences.Add("${relativeProjectPath}: $include")
        }
    }
}

if ($duplicateReferences.Count -gt 0) {
    foreach ($duplicateReference in $duplicateReferences) {
        Write-Error "Duplicate ProjectReference target: $duplicateReference" -ErrorAction Continue
    }
}

if ($escapedReferences.Count -gt 0) {
    foreach ($escapedReference in $escapedReferences) {
        Write-Error "ProjectReference target escapes project root: $escapedReference" -ErrorAction Continue
    }
}

if ($missingReferences.Count -gt 0) {
    foreach ($missingReference in $missingReferences) {
        Write-Error "Missing ProjectReference target: $missingReference" -ErrorAction Continue
    }
}

if ($duplicateReferences.Count -gt 0 -or $escapedReferences.Count -gt 0 -or $missingReferences.Count -gt 0) {
    throw "Project reference validation failed for $($duplicateReferences.Count + $escapedReferences.Count + $missingReferences.Count) reference(s)."
}

Write-Host "Validated ProjectReference targets for $($projectFiles.Count) .NET project file(s)."
