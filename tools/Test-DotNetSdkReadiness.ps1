param(
    [string]$ProjectRoot = ".",
    [string]$WorkflowPath = ".github\workflows\tester-release.yml"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

$resolvedProjectRoot = Resolve-ToolRepoPath -Path $ProjectRoot -RepoRoot $repoRoot
if (-not (Test-Path -LiteralPath $resolvedProjectRoot -PathType Container)) {
    throw "Project root was not found: $resolvedProjectRoot"
}

$resolvedWorkflowPath = Resolve-ToolRepoPath -Path $WorkflowPath -RepoRoot $repoRoot
if (-not (Test-Path -LiteralPath $resolvedWorkflowPath -PathType Leaf)) {
    throw "Tester Release workflow was not found: $resolvedWorkflowPath"
}

$workflow = Get-Content -LiteralPath $resolvedWorkflowPath -Raw
$dotnetVersionMatch = [regex]::Match($workflow, "(?m)^\s*dotnet-version:\s*['""]?(?<major>\d+)\.(?<minor>\d+)\.(?<patch>x|\d+)['""]?\s*$")
if (-not $dotnetVersionMatch.Success) {
    throw "Tester Release workflow is missing a dotnet-version SDK such as 10.0.111."
}

$requiredMajor = [int]$dotnetVersionMatch.Groups["major"].Value
$requiredMinor = [int]$dotnetVersionMatch.Groups["minor"].Value
$requiredPatchText = $dotnetVersionMatch.Groups["patch"].Value
$requiredPatch = if ($requiredPatchText -eq "x") { $null } else { [int]$requiredPatchText }
$requiredSdk = "$requiredMajor.$requiredMinor.$requiredPatchText"

$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
if ($null -eq $dotnetCommand) {
    throw ".NET SDK $requiredSdk is required by the Tester Release workflow, but dotnet was not found on PATH."
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
        Where-Object {
            $_.Major -eq $requiredMajor -and
            $_.Minor -eq $requiredMinor -and
            ($null -eq $requiredPatch -or $_.Build -eq $requiredPatch)
        } |
        Sort-Object -Descending
)

if ($matchingSdkVersions.Count -eq 0) {
    throw ".NET SDK $requiredSdk is required by the Tester Release workflow. Installed SDKs: $($installedVersions -join ', ')"
}

$projectFiles = @(
    Get-ToolProjectFiles -Directory (Get-Item -LiteralPath $resolvedProjectRoot) |
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
                $relativeProjectPath = Get-ToolRelativePath -RootPath $resolvedProjectRoot -Path $projectFile.FullName
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
