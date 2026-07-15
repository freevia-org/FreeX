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
