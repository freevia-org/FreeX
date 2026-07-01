param(
    [string]$ScriptDirectory = "tools"
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

function Test-IsExcludedPath {
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

$resolvedScriptDirectory = Resolve-RepoPath $ScriptDirectory
if (-not (Test-Path -LiteralPath $resolvedScriptDirectory -PathType Container)) {
    throw "Tool script directory was not found: $resolvedScriptDirectory"
}

$scripts = @(Get-ChildItem -LiteralPath $resolvedScriptDirectory -Filter "*.ps1" -File -Recurse |
    Where-Object { -not (Test-IsExcludedPath $_.FullName) } |
    Sort-Object FullName)
if ($scripts.Count -eq 0) {
    throw "No PowerShell tool scripts were found in $resolvedScriptDirectory"
}

$failedScripts = New-Object System.Collections.Generic.List[string]
$missingFailFastScripts = New-Object System.Collections.Generic.List[string]

foreach ($script in $scripts) {
    $tokens = $null
    $parseErrors = $null
    [System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref]$tokens, [ref]$parseErrors) | Out-Null

    if ($parseErrors.Count -gt 0) {
        $failedScripts.Add($script.FullName)
        foreach ($parseError in $parseErrors) {
            Write-Error "$($script.FullName): $($parseError.Message)" -ErrorAction Continue
        }
    }

    if ($script.Name.StartsWith("Test-", [System.StringComparison]::OrdinalIgnoreCase)) {
        $content = Get-Content -LiteralPath $script.FullName -Raw
        if (-not $content.Contains('$ErrorActionPreference = "Stop"')) {
            $missingFailFastScripts.Add($script.FullName)
            Write-Error "$($script.FullName): preflight scripts must set `$ErrorActionPreference = `"Stop`"." -ErrorAction Continue
        }
    }
}

if ($failedScripts.Count -gt 0) {
    throw "PowerShell syntax validation failed for $($failedScripts.Count) tool script(s)."
}

if ($missingFailFastScripts.Count -gt 0) {
    throw "PowerShell fail-fast validation failed for $($missingFailFastScripts.Count) preflight script(s)."
}

Write-Host "Validated $($scripts.Count) PowerShell tool script(s)."
