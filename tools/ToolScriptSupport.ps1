function Resolve-ToolRepoPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    return Join-Path $RepoRoot $Path
}

function Resolve-InputPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    $currentDirectoryCandidate = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
    if (Test-Path -LiteralPath $currentDirectoryCandidate) {
        return $currentDirectoryCandidate
    }

    return [System.IO.Path]::GetFullPath((Join-Path $RepoRoot $Path))
}

function Get-ToolRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$RootPath,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $fullRootPath = [System.IO.Path]::GetFullPath($RootPath)
    if (-not $fullRootPath.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $fullRootPath += [System.IO.Path]::DirectorySeparatorChar
    }

    $rootUri = New-Object System.Uri($fullRootPath)
    $pathUri = New-Object System.Uri([System.IO.Path]::GetFullPath($Path))
    return [System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString())
}

function ConvertTo-ToolNormalizedRelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return $Path.Replace("\", "/")
}

function Test-ToolExcludedPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string[]]$ExcludedDirectoryNames = @("bin", "obj", ".worktrees", ".claude")
    )

    $relativePath = ConvertTo-ToolRepoRelativePath -Path $Path -RepoRoot $RepoRoot
    $segments = $relativePath -split '[\\/]'
    foreach ($directoryName in $ExcludedDirectoryNames) {
        if ($segments -contains $directoryName) {
            return $true
        }
    }

    return $false
}

function Get-ToolTrackedRepositoryFiles {
    param([Parameter(Mandatory = $true)][string]$RepoRoot)

    $gitOutput = & git -C $RepoRoot ls-files --deduplicate
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enumerate tracked files with git ls-files."
    }

    foreach ($relativePath in $gitOutput) {
        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            continue
        }

        $relativePath
    }
}

function Test-ToolIgnoredDirectoryName {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string[]]$IgnoredDirectoryNames = @("bin", "obj", ".git", ".worktrees", ".claude")
    )

    return $IgnoredDirectoryNames -contains $Name
}

function Get-ToolProjectFiles {
    param(
        [Parameter(Mandatory = $true)][System.IO.DirectoryInfo]$Directory,
        [string[]]$IgnoredDirectoryNames = @("bin", "obj", ".git", ".worktrees", ".claude"),
        [string[]]$IgnoredProjectNamePatterns = @("*_wpftmp.csproj")
    )

    foreach ($projectFile in $Directory.EnumerateFiles("*.csproj")) {
        $isIgnored = $false
        foreach ($pattern in $IgnoredProjectNamePatterns) {
            if ($projectFile.Name -like $pattern) {
                $isIgnored = $true
                break
            }
        }

        if (-not $isIgnored) {
            $projectFile
        }
    }

    foreach ($childDirectory in $Directory.EnumerateDirectories()) {
        if (Test-ToolIgnoredDirectoryName -Name $childDirectory.Name -IgnoredDirectoryNames $IgnoredDirectoryNames) {
            continue
        }

        Get-ToolProjectFiles `
            -Directory $childDirectory `
            -IgnoredDirectoryNames $IgnoredDirectoryNames `
            -IgnoredProjectNamePatterns $IgnoredProjectNamePatterns
    }
}

function Get-RepoRoot {
    param([Parameter(Mandatory = $true)][string]$ScriptRoot)

    return (Resolve-Path (Join-Path $ScriptRoot "..")).Path
}

function Get-GitValue {
    param(
        [string]$RepoRoot,
        [string[]]$Arguments
    )

    try {
        $value = & git -C $RepoRoot @Arguments 2>$null
        if ($LASTEXITCODE -eq 0) {
            return ($value -join "`n").Trim()
        }
    }
    catch {
    }

    return $null
}

function Resolve-FreeXExe {
    param(
        [string]$RepoRoot,
        [string]$RequestedPath,
        [switch]$SkipBuild
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $resolved = (Resolve-Path $RequestedPath).Path
        if (-not (Test-Path $resolved)) {
            throw "FreeX executable was not found at $RequestedPath"
        }
        return $resolved
    }

    $candidate = Join-Path $RepoRoot "src/FreeX.App.Host/bin/Release/net10.0-windows10.0.19041.0/FreeX.App.Host.exe"
    if (-not (Test-Path $candidate) -and -not $SkipBuild) {
        $buildOutput = & dotnet build (Join-Path $RepoRoot "src/FreeX.App.Host/FreeX.App.Host.csproj") --configuration Release
        $buildOutput | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) {
            throw "FreeX host build failed with exit code $LASTEXITCODE"
        }
    }

    if (-not (Test-Path $candidate)) {
        throw "FreeX host executable was not found. Build Release or pass -FreeXExe. Expected: $candidate"
    }

    return (Resolve-Path $candidate).Path
}

function ConvertTo-ToolRepoRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $fullRoot = [System.IO.Path]::GetFullPath($RepoRoot).TrimEnd('\')
    if ($fullPath.StartsWith($fullRoot + '\', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($fullRoot.Length + 1).Replace('/', '\')
    }

    return $fullPath.Replace('/', '\')
}

function Read-ToolJson {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string]$MissingMessage = "Required generated JSON input is missing"
    )

    $resolvedPath = Resolve-ToolRepoPath -Path $Path -RepoRoot $RepoRoot
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "$MissingMessage`: $resolvedPath"
    }

    Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json
}

function ConvertTo-ToolMarkdownCell {
    param([AllowNull()][string]$Value)

    if ($null -eq $Value -or $Value.Length -eq 0) {
        return ""
    }

    $Value.Replace('|', '\|')
}

function ConvertTo-ToolXmlAttribute {
    param([Parameter(Mandatory = $true)][string]$Value)

    return [System.Security.SecurityElement]::Escape($Value)
}

function Test-ToolGeneratedFileContentMatches {
    param(
        [Parameter(Mandatory = $true)][string]$ExpectedPath,
        [Parameter(Mandatory = $true)][string]$ActualPath,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$GeneratorScriptName,
        [switch]$NormalizeNewlines
    )

    Test-ToolGeneratedContentMatches `
        -ExpectedContent (Get-Content -LiteralPath $ExpectedPath -Raw) `
        -ActualPath $ActualPath `
        -Label $Label `
        -GeneratorScriptName $GeneratorScriptName `
        -NormalizeNewlines:$NormalizeNewlines
}

function Test-ToolGeneratedContentMatches {
    param(
        [Parameter(Mandatory = $true)][string]$ExpectedContent,
        [Parameter(Mandatory = $true)][string]$ActualPath,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$GeneratorScriptName,
        [switch]$NormalizeNewlines
    )

    if (-not (Test-Path -LiteralPath $ActualPath -PathType Leaf)) {
        throw "$Label is missing. Run $GeneratorScriptName to create it."
    }

    $actual = Get-Content -LiteralPath $ActualPath -Raw
    $expected = $ExpectedContent
    if ($NormalizeNewlines) {
        $expected = $expected -replace "`r`n", "`n"
        $actual = $actual -replace "`r`n", "`n"
    }

    if ($expected -cne $actual) {
        throw "$Label is out of date. Run $GeneratorScriptName to refresh it."
    }
}
