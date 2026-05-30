param(
    [string]$ProjectRoot = ".",
    [string]$WorkflowPath = ".github\workflows\tester-release.yml"
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

$resolvedWorkflowPath = Resolve-RepoPath $WorkflowPath
if (-not (Test-Path -LiteralPath $resolvedWorkflowPath -PathType Leaf)) {
    throw "Tester Release workflow was not found: $resolvedWorkflowPath"
}

$workflow = Get-Content -LiteralPath $resolvedWorkflowPath -Raw
$dotnetVersionMatch = [regex]::Match($workflow, "(?m)^\s*dotnet-version:\s*['""]?(?<major>\d+)\.(?<minor>\d+)\.x['""]?\s*$")
if (-not $dotnetVersionMatch.Success) {
    throw "Tester Release workflow is missing a dotnet-version SDK band such as 10.0.x."
}

$requiredMajor = [int]$dotnetVersionMatch.Groups["major"].Value
$requiredMinor = [int]$dotnetVersionMatch.Groups["minor"].Value
$requiredSdkBand = "$requiredMajor.$requiredMinor.x"

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    throw ".NET SDK $requiredSdkBand is required by the Tester Release workflow, but dotnet was not found on PATH."
}

$sdkLines = & dotnet --list-sdks 2>&1
if ($LASTEXITCODE -ne 0) {
    $newline = [Environment]::NewLine
    throw "dotnet --list-sdks failed: $($sdkLines -join $newline)"
}

$installedVersions = @(
    foreach ($sdkLine in $sdkLines) {
        $sdkMatch = [regex]::Match([string]$sdkLine, "^(?<version>\d+\.\d+\.\d+)")
        if ($sdkMatch.Success) {
            [version]$sdkMatch.Groups["version"].Value
        }
    }
)

if ($installedVersions.Count -eq 0) {
    throw "dotnet --list-sdks returned no installed SDK versions."
}

$matchingSdkVersions = @(
    $installedVersions |
        Where-Object { $_.Major -eq $requiredMajor -and $_.Minor -eq $requiredMinor } |
        Sort-Object -Descending
)

if ($matchingSdkVersions.Count -eq 0) {
    throw ".NET SDK $requiredSdkBand is required by the Tester Release workflow. Installed SDKs: $($installedVersions -join ', ')"
}

$projectFiles = @(
    Get-ChildItem -LiteralPath $resolvedProjectRoot -Filter "*.csproj" -File -Recurse |
        Where-Object { -not (Test-IsIgnoredProjectPath $_) } |
        Sort-Object FullName
)

if ($projectFiles.Count -eq 0) {
    throw "No .csproj files were found in $resolvedProjectRoot"
}

$newerTargetFrameworks = New-Object System.Collections.Generic.List[string]
foreach ($projectFile in $projectFiles) {
    [xml]$projectXml = Get-Content -LiteralPath $projectFile.FullName -Raw
    $targetFrameworkValues = New-Object System.Collections.Generic.List[string]

    foreach ($propertyGroup in @($projectXml.Project.PropertyGroup)) {
        foreach ($propertyName in @("TargetFramework", "TargetFrameworks")) {
            $value = [string]$propertyGroup.$propertyName
            if (-not [string]::IsNullOrWhiteSpace($value)) {
                $targetFrameworkValues.Add($value)
            }
        }
    }

    foreach ($targetFrameworkValue in $targetFrameworkValues) {
        foreach ($targetFramework in $targetFrameworkValue.Split(";")) {
            $normalizedTargetFramework = $targetFramework.Trim()
            if ([string]::IsNullOrWhiteSpace($normalizedTargetFramework)) {
                continue
            }

            $targetFrameworkMatch = [regex]::Match($normalizedTargetFramework, "^net(?<major>\d+)\.(?<minor>\d+)")
            if (-not $targetFrameworkMatch.Success) {
                continue
            }

            $targetMajor = [int]$targetFrameworkMatch.Groups["major"].Value
            $targetMinor = [int]$targetFrameworkMatch.Groups["minor"].Value
            if ($targetMajor -gt $requiredMajor -or ($targetMajor -eq $requiredMajor -and $targetMinor -gt $requiredMinor)) {
                $relativeProjectPath = Get-RelativeRepoPath $projectFile.FullName
                $newerTargetFrameworks.Add("${relativeProjectPath}: $normalizedTargetFramework")
            }
        }
    }
}

if ($newerTargetFrameworks.Count -gt 0) {
    foreach ($newerTargetFramework in $newerTargetFrameworks) {
        Write-Error "Project targets a framework newer than workflow SDK ${requiredSdkBand}: $newerTargetFramework" -ErrorAction Continue
    }

    throw ".NET SDK readiness validation failed for $($newerTargetFrameworks.Count) project target framework(s)."
}

Write-Host "Validated .NET SDK $requiredSdkBand readiness with SDK $($matchingSdkVersions[0]) across $($projectFiles.Count) project file(s)."
