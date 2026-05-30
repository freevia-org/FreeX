param(
    [string[]]$XmlRoots = @("Directory.Build.props", "FreeX.slnx", "src", "tests"),
    [string[]]$XmlExtensions = @(".xml", ".xaml", ".slnx", ".csproj", ".props", ".targets", ".resx", ".config", ".ruleset")
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

    $segments = $Path -split '[\\/]'
    return $segments -contains "bin" -or $segments -contains "obj"
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

$xmlFiles = New-Object System.Collections.Generic.List[System.IO.FileInfo]
foreach ($xmlRoot in $XmlRoots) {
    $resolvedXmlRoot = Resolve-RepoPath $xmlRoot
    if (-not (Test-Path -LiteralPath $resolvedXmlRoot)) {
        throw "XML root was not found: $resolvedXmlRoot"
    }

    $rootItem = Get-Item -LiteralPath $resolvedXmlRoot
    if ($rootItem -is [System.IO.FileInfo]) {
        if ($normalizedExtensions.Contains($rootItem.Extension)) {
            $xmlFiles.Add($rootItem)
        }

        continue
    }

    Get-ChildItem -LiteralPath $rootItem.FullName -File -Recurse |
        Where-Object {
            $normalizedExtensions.Contains($_.Extension) -and -not (Test-IsBuildOutputPath $_.FullName)
        } |
        Sort-Object FullName |
        ForEach-Object { $xmlFiles.Add($_) }
}

if ($xmlFiles.Count -eq 0) {
    throw "No XML-backed files were found under: $($XmlRoots -join ', ')"
}

$failedFiles = New-Object System.Collections.Generic.List[string]
foreach ($xmlFile in $xmlFiles) {
    $reader = $null
    try {
        $reader = [System.Xml.XmlReader]::Create($xmlFile.FullName)
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
