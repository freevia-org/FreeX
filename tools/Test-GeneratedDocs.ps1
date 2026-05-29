param(
    [string]$CommandInventoryScriptPath = "tools\Generate-CommandInventoryDocs.ps1"
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

function Invoke-GeneratedDocsCheck {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $resolvedScriptPath = Resolve-RepoPath $ScriptPath
    if (-not (Test-Path -LiteralPath $resolvedScriptPath)) {
        throw "$Label generated-docs check script was not found: $resolvedScriptPath"
    }

    Write-Host "Checking $Label generated docs..."
    & $resolvedScriptPath -Check
}

Invoke-GeneratedDocsCheck -ScriptPath $CommandInventoryScriptPath -Label "command inventory"

Write-Host "Generated documentation checks passed."
