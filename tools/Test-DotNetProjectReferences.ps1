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

$resolvedProjectRoot = Resolve-RepoPath $ProjectRoot
if (-not (Test-Path -LiteralPath $resolvedProjectRoot -PathType Container)) {
    throw "Project root was not found: $resolvedProjectRoot"
}

$projectFiles = @(
    Get-ChildItem -LiteralPath $resolvedProjectRoot -Filter "*.csproj" -File -Recurse |
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
            $relativeProjectPath = [System.IO.Path]::GetRelativePath($resolvedProjectRoot, $projectFile.FullName)
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
