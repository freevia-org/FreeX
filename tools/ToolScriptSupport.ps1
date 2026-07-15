function Test-ToolPathRooted {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $true
    }

    return $Path -match '^(?:[A-Za-z]:[\\/]|[\\/]{2})'
}

function ConvertTo-ToolPlatformPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $separator = [string][System.IO.Path]::DirectorySeparatorChar
    return $Path.Replace([string][char]92, $separator).Replace([string][char]47, $separator)
}

function Resolve-ToolFullPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$BasePath = (Get-Location).Path
    )

    $normalizedPath = ConvertTo-ToolPlatformPath -Path $Path
    if (Test-ToolPathRooted -Path $Path) {
        return [System.IO.Path]::GetFullPath($normalizedPath)
    }

    $normalizedBasePath = ConvertTo-ToolPlatformPath -Path $BasePath
    if (-not (Test-ToolPathRooted -Path $BasePath)) {
        $normalizedBasePath = [System.IO.Path]::GetFullPath($normalizedBasePath)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $normalizedBasePath $normalizedPath))
}

function Resolve-ToolRepoPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $fullRepoRoot = Resolve-ToolFullPath -Path $RepoRoot
    return Resolve-ToolFullPath -Path $Path -BasePath $fullRepoRoot
}

function Resolve-InputPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    if (Test-ToolPathRooted -Path $Path) {
        return Resolve-ToolFullPath -Path $Path
    }

    $currentDirectoryCandidate = Resolve-ToolFullPath -Path $Path
    if (Test-Path -LiteralPath $currentDirectoryCandidate) {
        return $currentDirectoryCandidate
    }

    return Resolve-ToolRepoPath -Path $Path -RepoRoot $RepoRoot
}

function Get-ToolRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$RootPath,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $fullRootPath = Resolve-ToolFullPath -Path $RootPath
    $fullPath = Resolve-ToolFullPath -Path $Path -BasePath $fullRootPath
    $rootWithSeparator = $fullRootPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

    $rootUri = [System.Uri]::new($rootWithSeparator)
    $pathUri = [System.Uri]::new($fullPath)
    return ConvertTo-ToolNormalizedRelativePath -Path ([System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()))
}

function ConvertTo-ToolNormalizedRelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return $Path.Replace([string][char]92, "/")
}

function Test-ToolExcludedPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string[]]$ExcludedDirectoryNames = @("bin", "obj", ".worktrees", ".claude")
    )

    $relativePath = Get-ToolRelativePath -RootPath $RepoRoot -Path $Path
    $segments = (ConvertTo-ToolNormalizedRelativePath -Path $relativePath) -split '/'
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
        $resolved = Resolve-ToolRepoPath -Path $RequestedPath -RepoRoot $RepoRoot
        if (-not (Test-Path -LiteralPath $resolved)) {
            throw "FreeX executable was not found at $RequestedPath"
        }
        return (Resolve-Path -LiteralPath $resolved).Path
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

    $fullRoot = Resolve-ToolFullPath -Path $RepoRoot
    $fullPath = Resolve-ToolFullPath -Path $Path -BasePath $fullRoot
    $trimmedRoot = $fullRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $comparison = if ([System.IO.Path]::DirectorySeparatorChar -eq [char]92) {
        [System.StringComparison]::OrdinalIgnoreCase
    }
    else {
        [System.StringComparison]::Ordinal
    }

    if ($fullPath.Equals($trimmedRoot, $comparison)) {
        return ""
    }

    $rootPrefix = $trimmedRoot + [System.IO.Path]::DirectorySeparatorChar
    if ($fullPath.StartsWith($rootPrefix, $comparison)) {
        return $fullPath.Substring($rootPrefix.Length).Replace([string][char]47, [string][char]92)
    }

    return $fullPath.Replace([string][char]47, [string][char]92)
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
