param(
    [string[]]$XmlRoots = @(),
    [string[]]$XmlExtensions = @(".xml", ".xaml", ".axaml", ".slnx", ".csproj", ".props", ".targets", ".resx", ".config", ".ruleset", ".plist")
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

function Test-IsBuildOutputPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $relativePath = Get-RepositoryRelativePath $Path
    $segments = $relativePath -split '[\\/]'
    return $segments -contains "bin" -or
        $segments -contains "obj" -or
        $segments -contains ".worktrees" -or
        $segments -contains ".claude"
}

function Get-RepositoryRelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not [System.IO.Path]::IsPathRooted($Path)) {
        return $Path
    }

    $root = [System.IO.Path]::GetFullPath($repoRoot).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if ($fullPath.StartsWith($root, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($root.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    }

    return $Path
}

function Get-TrackedRepositoryFiles {
    $gitOutput = & git -C $repoRoot ls-files --deduplicate
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

$normalizedExtensions = New-Object System.Collections.Generic.HashSet[string]([System.StringComparer]::OrdinalIgnoreCase)
foreach ($extension in $XmlExtensions) {
    if ([string]::IsNullOrWhiteSpace($extension)) {
        continue
    }

    $normalized = if ($extension.StartsWith(".")) { $extension } else { ".$extension" }
    $normalizedExtensions.Add($normalized) | Out-Null
}

if ($normalizedExtensions.Count -eq 0) {
    throw "At least one XML extension must be provided."
}

$xmlPaths = New-Object System.Collections.Generic.List[string]
if ($XmlRoots.Count -eq 0) {
    foreach ($trackedPath in (Get-TrackedRepositoryFiles)) {
        $resolvedTrackedPath = Join-Path $repoRoot $trackedPath
        if ((Test-IsBuildOutputPath $trackedPath) -or -not (Test-Path -LiteralPath $resolvedTrackedPath -PathType Leaf)) {
            continue
        }

        $extension = [System.IO.Path]::GetExtension($trackedPath)
        if ($normalizedExtensions.Contains($extension)) {
            $xmlPaths.Add($resolvedTrackedPath)
        }
    }
}
else {
    foreach ($xmlRoot in $XmlRoots) {
        $resolvedXmlRoot = Resolve-RepoPath $xmlRoot
        if (-not (Test-Path -LiteralPath $resolvedXmlRoot)) {
            throw "XML root was not found: $resolvedXmlRoot"
        }

        $rootItem = Get-Item -LiteralPath $resolvedXmlRoot
        if ($rootItem -is [System.IO.FileInfo]) {
            if ($normalizedExtensions.Contains($rootItem.Extension)) {
                $xmlPaths.Add($rootItem.FullName)
            }

            continue
        }

        Get-ChildItem -LiteralPath $rootItem.FullName -File -Recurse |
            Where-Object {
                $normalizedExtensions.Contains($_.Extension) -and -not (Test-IsBuildOutputPath $_.FullName)
            } |
            ForEach-Object { $xmlPaths.Add($_.FullName) }
    }
}

$xmlFiles = @($xmlPaths | Sort-Object -Unique | ForEach-Object { Get-Item -LiteralPath $_ })
if ($xmlFiles.Count -eq 0) {
    if ($XmlRoots.Count -eq 0) {
        throw "No tracked XML-backed files were found."
    }

    throw "No XML-backed files were found under: $($XmlRoots -join ', ')"
}

$failedFiles = New-Object System.Collections.Generic.List[string]
$readerSettings = [System.Xml.XmlReaderSettings]::new()
$readerSettings.DtdProcessing = [System.Xml.DtdProcessing]::Ignore
foreach ($xmlFile in $xmlFiles) {
    $reader = $null
    try {
        $reader = [System.Xml.XmlReader]::Create($xmlFile.FullName, $readerSettings)
        while ($reader.Read()) {
        }
    }
    catch {
        $failedFiles.Add($xmlFile.FullName)
        Write-Error "$($xmlFile.FullName): $($_.Exception.Message)" -ErrorAction Continue
    }
    finally {
        if ($null -ne $reader) {
            $reader.Dispose()
        }
    }
}

if ($failedFiles.Count -gt 0) {
    throw "XML validation failed for $($failedFiles.Count) file(s)."
}

Write-Host "Validated $($xmlFiles.Count) XML-backed file(s)."
