param(
    [string]$ProjectRoot = "."
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

function Resolve-RepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $repoRoot $Path
}

function Get-RelativeRepoPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $rootPath = [System.IO.Path]::GetFullPath($resolvedProjectRoot)
    if (-not $rootPath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $rootPath += [System.IO.Path]::DirectorySeparatorChar
    }

    $rootUri = New-Object System.Uri($rootPath)
    $pathUri = New-Object System.Uri([System.IO.Path]::GetFullPath($Path))
    return [System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString())
}

function Test-IsIgnoredProjectPath {
    param([Parameter(Mandatory = $true)][System.IO.FileInfo]$ProjectFile)

    if ($ProjectFile.Name -like "*_wpftmp.csproj") {
        return $true
    }

    $relativePath = Get-RelativeRepoPath $ProjectFile.FullName
    $segments = $relativePath -split "/"
    return $segments -contains "bin" -or
        $segments -contains "obj" -or
        $segments -contains ".git" -or
        $segments -contains ".worktrees"
}

$resolvedProjectRoot = Resolve-RepoPath $ProjectRoot
if (-not (Test-Path -LiteralPath $resolvedProjectRoot -PathType Container)) {
    throw "Project root was not found: $resolvedProjectRoot"
}

$projectFiles = @(
    Get-ChildItem -LiteralPath $resolvedProjectRoot -Filter "*.csproj" -File -Recurse |
        Where-Object { -not (Test-IsIgnoredProjectPath $_) } |
        Sort-Object FullName
)

if ($projectFiles.Count -eq 0) {
    throw "No .csproj files were found in $resolvedProjectRoot"
}

$missingReferences = New-Object System.Collections.Generic.List[string]

foreach ($projectFile in $projectFiles) {
    [xml]$projectXml = Get-Content -LiteralPath $projectFile.FullName -Raw
    $projectReferences = @($projectXml.Project.ItemGroup.ProjectReference)

    foreach ($projectReference in $projectReferences) {
        $include = [string]$projectReference.Include
        if ([string]::IsNullOrWhiteSpace($include)) {
            continue
        }

        $referencedProjectPath = Join-Path $projectFile.DirectoryName $include
        $resolvedReferencePath = [System.IO.Path]::GetFullPath($referencedProjectPath)

        if (-not (Test-Path -LiteralPath $resolvedReferencePath -PathType Leaf)) {
            $relativeProjectPath = Get-RelativeRepoPath $projectFile.FullName
            $missingReferences.Add("${relativeProjectPath}: $include")
        }
    }
}

if ($missingReferences.Count -gt 0) {
    foreach ($missingReference in $missingReferences) {
        Write-Error "Missing ProjectReference target: $missingReference" -ErrorAction Continue
    }

    throw "Project reference validation failed for $($missingReferences.Count) reference(s)."
}

Write-Host "Validated ProjectReference targets for $($projectFiles.Count) .NET project file(s)."
