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

function ConvertTo-ToolXmlAttribute {
    param([Parameter(Mandatory = $true)][string]$Value)

    return [System.Security.SecurityElement]::Escape($Value)
}

function Test-ToolGeneratedFileContentMatches {
    param(
        [Parameter(Mandatory = $true)][string]$ExpectedPath,
        [Parameter(Mandatory = $true)][string]$ActualPath,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$GeneratorScriptName
    )

    if (-not (Test-Path -LiteralPath $ActualPath -PathType Leaf)) {
        throw "$Label is missing. Run $GeneratorScriptName to create it."
    }

    $expected = Get-Content -LiteralPath $ExpectedPath -Raw
    $actual = Get-Content -LiteralPath $ActualPath -Raw
    if ($expected -cne $actual) {
        throw "$Label is out of date. Run $GeneratorScriptName to refresh it."
    }
}
