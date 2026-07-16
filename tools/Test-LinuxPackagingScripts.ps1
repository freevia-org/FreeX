param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$errors = New-Object System.Collections.Generic.List[string]

function Assert-Packaging {
    param([bool]$Condition, [string]$Message)

    if (-not $Condition) {
        $errors.Add($Message)
        Write-Error $Message -ErrorAction Continue
    }
}

function Read-PackagingConfig {
    param([string]$Path)

    $values = @{}
    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line.Length -eq 0 -or $line.StartsWith('#')) {
            continue
        }

        Assert-Packaging ($line.Contains('=')) "Config '$Path' must contain only key=value lines."
        if (-not $line.Contains('=')) {
            continue
        }

        $parts = $line.Split('=', 2)
        Assert-Packaging (-not $values.ContainsKey($parts[0])) "Config '$Path' repeats key '$($parts[0])'."
        $values[$parts[0]] = $parts[1]
    }

    return $values
}

function Get-RepoRelativeLineCount {
    param([string]$RelativePath)

    $content = & git -C $repoRoot show "origin/main:$RelativePath" 2>$null
    Assert-Packaging ($LASTEXITCODE -eq 0) "Could not read origin/main baseline file '$RelativePath'."
    return @($content).Count
}

$sharedRelative = "tools/packaging/linux/package-linux.sh"
$sharedPath = Join-Path $repoRoot $sharedRelative
$entrypoints = @(
    @{ Relative = "src/FreeX.App.Avalonia/Packaging/linux/package-linux-app.sh"; Product = "freex"; Operation = "tarball"; Config = "freex.conf" },
    @{ Relative = "src/FreeX.App.Avalonia/Packaging/linux/build-appimage.sh"; Product = "freex"; Operation = "appimage"; Config = "freex.conf" },
    @{ Relative = "src/FreeX.App.Avalonia/Packaging/linux/build-deb.sh"; Product = "freex"; Operation = "deb"; Config = "freex.conf" },
    @{ Relative = "freew/FreeW.App.Avalonia/Packaging/linux/package-linux-app.sh"; Product = "freew"; Operation = "tarball"; Config = "freew.conf" },
    @{ Relative = "freew/FreeW.App.Avalonia/Packaging/linux/build-appimage.sh"; Product = "freew"; Operation = "appimage"; Config = "freew.conf" },
    @{ Relative = "freew/FreeW.App.Avalonia/Packaging/linux/build-deb.sh"; Product = "freew"; Operation = "deb"; Config = "freew.conf" }
)

Assert-Packaging (Test-Path -LiteralPath $sharedPath -PathType Leaf) "Shared Linux packaging implementation is missing: $sharedPath"
$shared = if (Test-Path -LiteralPath $sharedPath) { Get-Content -LiteralPath $sharedPath -Raw } else { "" }
Assert-Packaging $shared.Contains("set -euo pipefail") "Shared packaging implementation must be fail-fast."
Assert-Packaging $shared.Contains("declare -A config_values") "Shared packaging implementation must parse data-only config."
Assert-Packaging $shared.Contains('ARCH="$arch" "$appimagetool"') "Shared implementation must preserve direct AppImage tool invocation."
Assert-Packaging $shared.Contains("dpkg-deb --root-owner-group --build") "Shared implementation must preserve direct dpkg-deb invocation."
Assert-Packaging (-not [regex]::IsMatch($shared, '(?im)^\s*(eval|exec\s+.*bash\s+-c|bash\s+-c|sh\s+-c)')) "Shared implementation must not use eval or string-built shell commands."

$newScriptLineCount = 0
foreach ($entrypoint in $entrypoints) {
    $path = Join-Path $repoRoot $entrypoint.Relative
    Assert-Packaging (Test-Path -LiteralPath $path -PathType Leaf) "Packaging entrypoint is missing: $path"
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        continue
    }

    $text = Get-Content -LiteralPath $path -Raw
    $newScriptLineCount += (Get-Content -LiteralPath $path).Count
    Assert-Packaging $text.Contains("set -euo pipefail") "Entrypoint '$($entrypoint.Relative)' must retain fail-fast mode."
    Assert-Packaging $text.Contains('package-linux.sh') "Entrypoint '$($entrypoint.Relative)' must consume the shared implementation."
    Assert-Packaging $text.Contains("--operation $($entrypoint.Operation)") "Entrypoint '$($entrypoint.Relative)' has the wrong operation."
    $expectedConfigArgument = '--config "$repo_root/tools/packaging/linux/' + $entrypoint.Config + '"'
    Assert-Packaging $text.Contains($expectedConfigArgument) "Entrypoint '$($entrypoint.Relative)' has no explicit product config."
    Assert-Packaging $text.Contains('--asset-dir "$script_dir"') "Entrypoint '$($entrypoint.Relative)' must preserve its product asset directory."
    Assert-Packaging (-not [regex]::IsMatch($text, '(?im)^\s*(eval|bash\s+-c|sh\s+-c)')) "Entrypoint '$($entrypoint.Relative)' must not build shell command strings."
    foreach ($mechanic in @('rm -rf', 'dpkg-deb', 'appimagetool', 'while [[ $#', 'declare -A', 'cat >')) {
        Assert-Packaging (-not $text.Contains($mechanic)) "Entrypoint '$($entrypoint.Relative)' still contains shared mechanic '$mechanic'."
    }
}

$configExpectations = @{
    "freex.conf" = @{ product_key = "freex"; display_name = "FreeX"; binary_name = "FreeX"; launcher_name = "freex"; library_dir = "freex"; app_id = "io.github.tony-xmelon.freex"; appimage_prefix = "FreeX"; stage_prefix = "freex"; package_name = "freex"; cache_mime = "true"; mime_asset = "io.github.tony-xmelon.freex.xml" }
    "freew.conf" = @{ product_key = "freew"; display_name = "FreeW"; binary_name = "FreeW"; launcher_name = "freew"; library_dir = "freew"; app_id = "io.github.tony-xmelon.freew"; appimage_prefix = "FreeW"; stage_prefix = "freew"; package_name = "freew"; cache_mime = "false"; mime_asset = "" }
}
foreach ($configName in $configExpectations.Keys) {
    $configPath = Join-Path $repoRoot "tools/packaging/linux/$configName"
    Assert-Packaging (Test-Path -LiteralPath $configPath -PathType Leaf) "Packaging config is missing: $configPath"
    if (-not (Test-Path -LiteralPath $configPath -PathType Leaf)) {
        continue
    }
    $values = Read-PackagingConfig $configPath
    foreach ($key in $configExpectations[$configName].Keys) {
        Assert-Packaging ($values[$key] -eq $configExpectations[$configName][$key]) "Config '$configName' has unexpected $key."
    }
}

$baselineLineCount = 0
foreach ($entrypoint in $entrypoints) {
    $baselineLineCount += Get-RepoRelativeLineCount $entrypoint.Relative
}
$newScriptLineCount += (Get-Content -LiteralPath $sharedPath).Count
foreach ($configName in $configExpectations.Keys) {
    $newScriptLineCount += (Get-Content -LiteralPath (Join-Path $repoRoot "tools/packaging/linux/$configName")).Count
}
Assert-Packaging ($newScriptLineCount -lt ($baselineLineCount * 0.8)) "Linux packaging source reduction is too small: $newScriptLineCount new lines vs $baselineLineCount baseline lines."

$bashCandidates = @(
    @(Get-Command bash -All -ErrorAction SilentlyContinue | ForEach-Object Source)
    (Join-Path ${env:ProgramFiles} "Git/bin/bash.exe")
    (Join-Path ${env:ProgramFiles} "Git/usr/bin/bash.exe")
)
$bashUsable = $false
foreach ($candidate in $bashCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -Unique) {
    try {
        & $candidate --version 1>$null 2>$null
        if ($LASTEXITCODE -eq 0) {
            $bash = $candidate
            $bashUsable = $true
            break
        }
    } catch {
        continue
    }
}
if ($bashUsable) {
    $bashFiles = @($sharedPath) + @($entrypoints | ForEach-Object { Join-Path $repoRoot $_.Relative })
    foreach ($path in $bashFiles) {
        & $bash -n -- $path
        Assert-Packaging ($LASTEXITCODE -eq 0) "Bash syntax validation failed: $path"
    }
    foreach ($entrypoint in $entrypoints) {
        $path = Join-Path $repoRoot $entrypoint.Relative
        $previousErrorActionPreference = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        & $bash $path 1>$null 2>$null
        $entrypointExitCode = $LASTEXITCODE
        $ErrorActionPreference = $previousErrorActionPreference
        Assert-Packaging ($entrypointExitCode -eq 2) "Entrypoint '$($entrypoint.Relative)' did not return usage exit code 2 without build arguments."
    }
    Write-Host "Bash syntax and no-build entrypoint checks passed."
} else {
    Write-Host "Bash syntax and no-build entrypoint checks skipped because a usable bash executable is unavailable."
}

if ($errors.Count -gt 0) {
    Write-Host "Linux packaging script preflight FAILED with $($errors.Count) issue(s)."
    exit 1
}

Write-Host "Linux packaging script preflight PASSED."
