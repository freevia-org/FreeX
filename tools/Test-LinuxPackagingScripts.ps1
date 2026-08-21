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

function Invoke-BashScript {
    param([string]$ScriptPath, [string[]]$Arguments)

    $oldErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $lines = @(& $script:bash $ScriptPath @Arguments 2>&1 | ForEach-Object { $_.ToString() })
        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Output = $lines -join [Environment]::NewLine
        }
    } finally {
        $ErrorActionPreference = $oldErrorActionPreference
    }
}

function Read-KeyValueOutput {
    param([string]$Output)

    $values = @{}
    foreach ($line in ($Output -split "`r?`n")) {
        if ($line -match '^(?<key>[a-z_]+)=(?<value>.*)$') {
            $values[$Matches.key] = $Matches.value
        }
    }
    return $values
}

function Assert-BashExecutable {
    param([string]$Path, [string]$Label)

    $result = Invoke-BashScript -ScriptPath "-c" -Arguments @('test -x "$1"', 'test', $Path)
    Assert-Packaging ($result.ExitCode -eq 0) "$Label is not executable."
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

function Write-Utf8NoBom {
    param([string]$Path, [string[]]$Lines)

    [IO.File]::WriteAllText($Path, (($Lines -join "`n") + "`n"), (New-Object Text.UTF8Encoding($false)))
}

function Convert-ToBashPath {
    param([string]$Path)

    $result = Invoke-BashScript -ScriptPath "-c" -Arguments @('cygpath -u -- "$1"', 'cygpath', $Path)
    Assert-Packaging ($result.ExitCode -eq 0) "Could not convert path for bash: $Path"
    return $result.Output.Trim()
}

function Test-TarballProduct {
    param(
        [hashtable]$Entry,
        [string]$Sandbox
    )

    $published = Join-Path $Sandbox "$($Entry.Product)-published"
    $output = Join-Path $Sandbox "$($Entry.Product)-output"
    $extract = Join-Path $Sandbox "$($Entry.Product)-extract"
    New-Item -ItemType Directory -Force -Path $published, $output, $extract | Out-Null
    $binary = Join-Path $published $Entry.BinaryName
    Set-Content -LiteralPath $binary -Value '#!/usr/bin/env bash' -NoNewline
    $chmod = Invoke-BashScript -ScriptPath "-c" -Arguments @('chmod +x -- "$1"', 'chmod', $binary)
    Assert-Packaging ($chmod.ExitCode -eq 0) "Could not mark $($Entry.Product) fixture executable."
    $publishedBash = Convert-ToBashPath $published
    $outputBash = Convert-ToBashPath $output

    $common = @('--runtime', 'linux-x64', '--published', $publishedBash, '--version', '9.8.7', '--output', $outputBash)
    $dryRun = Invoke-BashScript -ScriptPath $Entry.TarballEntrypoint -Arguments ($common + '--dry-run')
    Assert-Packaging ($dryRun.ExitCode -eq 0) "$($Entry.Product) tarball dry-run failed: $($dryRun.Output)"
    $dryValues = Read-KeyValueOutput $dryRun.Output
    Assert-Packaging ($dryValues.product_key -eq $Entry.Product) "$($Entry.Product) dry-run forwarded the wrong product ID."
    Assert-Packaging ($dryValues.app_id -eq $Entry.AppId) "$($Entry.Product) dry-run forwarded the wrong app ID."
    Assert-Packaging ($dryValues.runtime -eq 'linux-x64') "$($Entry.Product) dry-run lost the runtime argument."
    Assert-Packaging ($dryValues.version -eq '9.8.7') "$($Entry.Product) dry-run lost the version argument."
    Assert-Packaging ($dryValues.output_name -eq "$($Entry.StagePrefix)-9.8.7-linux-x64.tar.gz") "$($Entry.Product) dry-run calculated the wrong output name."
    Assert-Packaging ($dryValues.desktop_asset -eq "$($Entry.AppId).desktop") "$($Entry.Product) dry-run calculated the wrong desktop asset."
    Assert-Packaging ($dryValues.icon_asset -eq "$($Entry.AppId).svg") "$($Entry.Product) dry-run calculated the wrong icon asset."
    Assert-Packaging ($dryValues.metainfo_asset -eq "$($Entry.AppId).metainfo.xml") "$($Entry.Product) dry-run calculated the wrong metainfo asset."

    $package = Invoke-BashScript -ScriptPath $Entry.TarballEntrypoint -Arguments $common
    Assert-Packaging ($package.ExitCode -eq 0) "$($Entry.Product) tarball build failed: $($package.Output)"
    $archive = Join-Path $output "$($Entry.StagePrefix)-9.8.7-linux-x64.tar.gz"
    Assert-Packaging (Test-Path -LiteralPath $archive -PathType Leaf) "$($Entry.Product) tarball was not created."
    if (-not (Test-Path -LiteralPath $archive -PathType Leaf)) {
        return
    }

    $archiveBash = Convert-ToBashPath $archive
    $tarListResult = Invoke-BashScript -ScriptPath "-c" -Arguments @('tar -tzf "$1"', 'tar', $archiveBash)
    $tarList = @($tarListResult.Output -split "`r?`n")
    if ($tarListResult.ExitCode -ne 0) {
        Assert-Packaging $false "$($Entry.Product) tarball could not be listed: $($tarListResult.Output)"
        return
    }
    $rootName = "$($Entry.StagePrefix)-9.8.7-linux-x64"
    Assert-Packaging ($tarList -contains "$rootName/bin/$($Entry.LauncherName)") "$($Entry.Product) tarball omitted its launcher."
    Assert-Packaging ($tarList -contains "$rootName/share/applications/$($Entry.AppId).desktop") "$($Entry.Product) tarball omitted its desktop metadata."
    Assert-Packaging ($tarList -contains "$rootName/share/icons/hicolor/scalable/apps/$($Entry.AppId).svg") "$($Entry.Product) tarball omitted its icon metadata."
    Assert-Packaging ($tarList -contains "$rootName/share/metainfo/$($Entry.AppId).metainfo.xml") "$($Entry.Product) tarball omitted its AppStream metadata."
    if ($Entry.MimeAsset) {
        Assert-Packaging ($tarList -contains "$rootName/share/mime/packages/$($Entry.MimeAsset)") "$($Entry.Product) tarball omitted its configured MIME asset."
    } else {
        Assert-Packaging (-not ($tarList -match '/share/mime/')) "$($Entry.Product) tarball unexpectedly contains MIME metadata."
    }

    $extractBash = Convert-ToBashPath $extract
    $tarExtract = Invoke-BashScript -ScriptPath "-c" -Arguments @('tar -xzf "$1" -C "$2"', 'tar', $archiveBash, $extractBash)
    if ($tarExtract.ExitCode -ne 0) {
        Assert-Packaging $false "$($Entry.Product) tarball could not be extracted: $($tarExtract.Output)"
        return
    }
    $packageRoot = Join-Path $extract $rootName
    if ($Entry.IconSource) {
        $packagedIcon = Join-Path $packageRoot "share/icons/hicolor/scalable/apps/$($Entry.AppId).svg"
        $packagedIconHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $packagedIcon).Hash
        $sourceIconHash = (Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $repoRoot $Entry.IconSource)).Hash
        Assert-Packaging ($packagedIconHash -eq $sourceIconHash) "$($Entry.Product) package must contain the canonical icon bytes."
    }
    Assert-BashExecutable (Join-Path (Join-Path $packageRoot 'bin') $Entry.LauncherName) "$($Entry.Product) relocatable launcher"
    Assert-BashExecutable (Join-Path $packageRoot 'install.sh') "$($Entry.Product) install script"
    Assert-BashExecutable (Join-Path $packageRoot 'uninstall.sh') "$($Entry.Product) uninstall script"
    $launcher = Get-Content -Raw (Join-Path (Join-Path $packageRoot 'bin') $Entry.LauncherName)
    Assert-Packaging ($launcher.Contains("../lib/$($Entry.LibraryDir)/$($Entry.BinaryName)")) "$($Entry.Product) launcher targets the wrong apphost."
    $install = Get-Content -Raw (Join-Path $packageRoot 'install.sh')
    $uninstall = Get-Content -Raw (Join-Path $packageRoot 'uninstall.sh')
    if ($Entry.MimeAsset) {
        Assert-Packaging ($install.Contains("share/mime/packages/`$mime_asset")) "$($Entry.Product) install script does not use the configured MIME variable."
        Assert-Packaging ($uninstall.Contains("share/mime/packages/`$mime_asset")) "$($Entry.Product) uninstall script does not use the configured MIME variable."
        $expectedMimeLine = 'mime_asset="' + $Entry.MimeAsset + '"'
        Assert-Packaging ($install.Contains($expectedMimeLine)) "$($Entry.Product) install script lost the configured MIME filename."
        Assert-Packaging ($uninstall.Contains($expectedMimeLine)) "$($Entry.Product) uninstall script lost the configured MIME filename."
    } else {
        Assert-Packaging (-not $install.Contains('mime_asset=')) "$($Entry.Product) install script unexpectedly declares a MIME asset."
        Assert-Packaging (-not $uninstall.Contains('mime_asset=')) "$($Entry.Product) uninstall script unexpectedly declares a MIME asset."
    }
}

$sharedRelative = "tools/packaging/linux/package-linux.sh"
$sharedPath = Join-Path $repoRoot $sharedRelative
$entrypoints = @(
    @{ Relative = "src/FreeX.App.Avalonia/Packaging/linux/package-linux-app.sh"; TarballEntrypoint = (Join-Path $repoRoot "src/FreeX.App.Avalonia/Packaging/linux/package-linux-app.sh"); Product = "freex"; Operation = "tarball"; Config = "freex.conf"; AppId = "io.github.tony-xmelon.freex"; BinaryName = "FreeX"; LauncherName = "freex"; LibraryDir = "freex"; StagePrefix = "freex"; MimeAsset = "io.github.tony-xmelon.freex.xml"; IconSource = "shared/Free.Shared.Shell/Resources/FreeX.svg" },
    @{ Relative = "src/FreeX.App.Avalonia/Packaging/linux/build-appimage.sh"; Product = "freex"; Operation = "appimage"; Config = "freex.conf"; AppId = "io.github.tony-xmelon.freex"; StagePrefix = "freex"; MimeAsset = "io.github.tony-xmelon.freex.xml" },
    @{ Relative = "src/FreeX.App.Avalonia/Packaging/linux/build-deb.sh"; Product = "freex"; Operation = "deb"; Config = "freex.conf"; AppId = "io.github.tony-xmelon.freex"; StagePrefix = "freex"; MimeAsset = "io.github.tony-xmelon.freex.xml" },
    @{ Relative = "freew/FreeW.App.Avalonia/Packaging/linux/package-linux-app.sh"; TarballEntrypoint = (Join-Path $repoRoot "freew/FreeW.App.Avalonia/Packaging/linux/package-linux-app.sh"); Product = "freew"; Operation = "tarball"; Config = "freew.conf"; AppId = "io.github.tony-xmelon.freew"; BinaryName = "FreeW"; LauncherName = "freew"; LibraryDir = "freew"; StagePrefix = "freew"; MimeAsset = ""; IconSource = "shared/Free.Shared.Shell/Resources/FreeW.svg" },
    @{ Relative = "freew/FreeW.App.Avalonia/Packaging/linux/build-appimage.sh"; Product = "freew"; Operation = "appimage"; Config = "freew.conf"; AppId = "io.github.tony-xmelon.freew"; StagePrefix = "freew"; MimeAsset = "" },
    @{ Relative = "freew/FreeW.App.Avalonia/Packaging/linux/build-deb.sh"; Product = "freew"; Operation = "deb"; Config = "freew.conf"; AppId = "io.github.tony-xmelon.freew"; StagePrefix = "freew"; MimeAsset = "" }
)

Assert-Packaging (Test-Path -LiteralPath $sharedPath -PathType Leaf) "Shared Linux packaging implementation is missing: $sharedPath"
$shared = if (Test-Path -LiteralPath $sharedPath) { Get-Content -LiteralPath $sharedPath -Raw } else { "" }
Assert-Packaging $shared.Contains("set -euo pipefail") "Shared packaging implementation must be fail-fast."
Assert-Packaging $shared.Contains("declare -A config_values") "Shared packaging implementation must parse data-only config."
Assert-Packaging $shared.Contains('ARCH="$arch" "$appimagetool"') "Shared implementation must preserve direct AppImage tool invocation."
Assert-Packaging $shared.Contains("dpkg-deb --root-owner-group --build") "Shared implementation must preserve direct dpkg-deb invocation."
Assert-Packaging $shared.Contains("--dry-run") "Shared implementation must expose the offline test seam."
Assert-Packaging $shared.Contains("--icon-file") "Shared implementation must accept an explicit canonical icon source."
Assert-Packaging $shared.Contains("validate_component_argument") "Shared implementation must validate output-name components."
Assert-Packaging $shared.Contains('printf ''mime_asset="%s"') "Generated scripts must retain the configured MIME filename."
Assert-Packaging (-not $shared.Contains('share/mime/packages/$app_id.xml')) "Shared implementation must not hardcode app_id.xml for MIME installation."
Assert-Packaging (-not $shared.Contains('share/mime/packages/$app_id.xml')) "Shared implementation must not hardcode app_id.xml for MIME removal."
Assert-Packaging (-not $shared.Contains("origin/main")) "Offline packaging tests must not depend on origin/main."
Assert-Packaging (-not [regex]::IsMatch($shared, '(?im)\bgit\s+')) "Shared implementation must not depend on Git refs."
Assert-Packaging (-not [regex]::IsMatch($shared, '(?im)^\s*(eval|exec\s+.*bash\s+-c|bash\s+-c|sh\s+-c)')) "Shared packaging implementation must not use eval or string-built shell commands."

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
    Assert-Packaging $text.Contains("package-linux.sh") "Entrypoint '$($entrypoint.Relative)' must consume the shared implementation."
    Assert-Packaging $text.Contains("--operation $($entrypoint.Operation)") "Entrypoint '$($entrypoint.Relative)' has the wrong operation."
    $expectedConfigArgument = '--config "$repo_root/tools/packaging/linux/' + $entrypoint.Config + '"'
    Assert-Packaging $text.Contains($expectedConfigArgument) "Entrypoint '$($entrypoint.Relative)' has no explicit product config."
    Assert-Packaging $text.Contains('--asset-dir "$script_dir"') "Entrypoint '$($entrypoint.Relative)' must preserve its product asset directory."
    if ($entrypoint.Product -eq 'freex') {
        Assert-Packaging $text.Contains('--icon-file "$repo_root/shared/Free.Shared.Shell/Resources/FreeX.svg"') "FreeX entrypoint '$($entrypoint.Relative)' must use the canonical shared icon."
    }
    elseif ($entrypoint.Product -eq 'freew') {
        Assert-Packaging $text.Contains('--icon-file "$repo_root/shared/Free.Shared.Shell/Resources/FreeW.svg"') "FreeW entrypoint '$($entrypoint.Relative)' must use the canonical shared icon."
    }
    Assert-Packaging (-not [regex]::IsMatch($text, '(?im)^\s*(eval|bash\s+-c|sh\s+-c)')) "Entrypoint '$($entrypoint.Relative)' must not build shell command strings."
    foreach ($mechanic in @('rm -rf', 'dpkg-deb', 'appimagetool', 'while [[ $#', 'declare -A', 'cat >')) {
        Assert-Packaging (-not $text.Contains($mechanic)) "Entrypoint '$($entrypoint.Relative)' still contains shared mechanic '$mechanic'."
    }
}
$newScriptLineCount += (Get-Content -LiteralPath $sharedPath).Count
foreach ($configName in @('freex.conf', 'freew.conf')) {
    $newScriptLineCount += (Get-Content -LiteralPath (Join-Path $repoRoot "tools/packaging/linux/$configName")).Count
}

# Fixed count is the reviewed six-script pre-dedup fixture, independent of moving refs.
$baselineLineCount = 637
$reductionPercent = 100.0 * ($baselineLineCount - $newScriptLineCount) / $baselineLineCount
Assert-Packaging ($reductionPercent -ge 15) "Linux packaging source reduction fell below 15%: $newScriptLineCount new lines vs $baselineLineCount baseline lines."

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

$bashCandidates = @(
    (Join-Path ${env:ProgramFiles} "Git/bin/bash.exe")
    (Join-Path ${env:ProgramFiles} "Git/usr/bin/bash.exe")
    Get-Command bash -All -ErrorAction SilentlyContinue |
        Where-Object { $_.Source -notmatch '\\Windows\\System32\\|\\WindowsApps\\' } |
        ForEach-Object Source
)
$bashUsable = $false
foreach ($candidate in $bashCandidates | Where-Object { $_ -and (Test-Path -LiteralPath $_ -PathType Leaf) } | Select-Object -Unique) {
    try {
        & $candidate -c 'exit 0' 1>$null 2>$null
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
        $arguments = @('--runtime', 'linux-x64', '--published', $repoRoot, '--version', '9.8.7', '--output', $repoRoot)
        if ($entrypoint.Operation -eq 'appimage') {
            $arguments += @('--appimagetool', (Join-Path $repoRoot 'missing-appimagetool'))
        }
        $result = Invoke-BashScript -ScriptPath $path -Arguments ($arguments + '--dry-run')
        Assert-Packaging ($result.ExitCode -eq 0) "Offline dry-run failed for '$($entrypoint.Relative)': $($result.Output)"
        $values = Read-KeyValueOutput $result.Output
        Assert-Packaging ($values.operation -eq $entrypoint.Operation) "Dry-run reported the wrong operation for '$($entrypoint.Relative)'."
        Assert-Packaging ($values.product_key -eq $entrypoint.Product) "Dry-run reported the wrong product for '$($entrypoint.Relative)'."
        Assert-Packaging ($values.app_id -eq $entrypoint.AppId) "Dry-run reported the wrong app ID for '$($entrypoint.Relative)'."
        Assert-Packaging ($values.mime_asset -eq $entrypoint.MimeAsset) "Dry-run reported the wrong MIME asset for '$($entrypoint.Relative)'."
        if ($entrypoint.Operation -eq 'appimage') {
            $displayPrefix = if ($entrypoint.Product -eq 'freex') { 'FreeX' } else { 'FreeW' }
            $expectedOutputName = "$displayPrefix-9.8.7-x86_64.AppImage"
        } elseif ($entrypoint.Operation -eq 'deb') {
            $expectedOutputName = "$($entrypoint.Product)_9.8.7_amd64.deb"
        } else {
            $expectedOutputName = "$($entrypoint.StagePrefix)-9.8.7-linux-x64.tar.gz"
        }
        Assert-Packaging ($values.output_name -eq $expectedOutputName) "Dry-run calculated the wrong output name for '$($entrypoint.Relative)'."
    }

    $sandbox = Join-Path ([IO.Path]::GetTempPath()) ("freex-linux-packaging-" + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $sandbox | Out-Null
    try {
        $tarballEntries = $entrypoints | Where-Object { $_.Operation -eq 'tarball' }
        foreach ($entry in $tarballEntries) {
            Test-TarballProduct -Entry $entry -Sandbox $sandbox
        }

        # A non-default filename proves the generated install/uninstall scripts use config, not app_id.xml.
        $customAssetDir = Join-Path $sandbox 'custom-assets'
        Copy-Item -LiteralPath (Join-Path $repoRoot 'src/FreeX.App.Avalonia/Packaging/linux') -Destination $customAssetDir -Recurse
        Copy-Item -LiteralPath (Join-Path $repoRoot 'shared/Free.Shared.Shell/Resources/FreeX.svg') -Destination (Join-Path $customAssetDir 'io.github.tony-xmelon.freex.svg')
        $customMime = Join-Path $customAssetDir 'custom-workbook-mime.xml'
        Move-Item -LiteralPath (Join-Path $customAssetDir 'io.github.tony-xmelon.freex.xml') -Destination $customMime
        $customConfig = Join-Path $sandbox 'custom-freex.conf'
        $customConfigLines = @(Get-Content (Join-Path $repoRoot 'tools/packaging/linux/freex.conf')) -replace '^mime_asset=.*$', 'mime_asset=custom-workbook-mime.xml'
        Write-Utf8NoBom -Path $customConfig -Lines $customConfigLines
        $customPublished = Join-Path $sandbox 'custom-published'
        $customOutput = Join-Path $sandbox 'custom-output'
        New-Item -ItemType Directory -Force -Path $customPublished, $customOutput | Out-Null
        $customBinary = Join-Path $customPublished 'FreeX'
        Set-Content -LiteralPath $customBinary -Value '#!/usr/bin/env bash' -NoNewline
        Invoke-BashScript -ScriptPath '-c' -Arguments @('chmod +x -- "$1"', 'chmod', $customBinary) | Out-Null
        $customConfigBash = Convert-ToBashPath $customConfig
        $customAssetDirBash = Convert-ToBashPath $customAssetDir
        $customPublishedBash = Convert-ToBashPath $customPublished
        $customOutputBash = Convert-ToBashPath $customOutput
        $customResult = Invoke-BashScript -ScriptPath $sharedPath -Arguments @('--operation', 'tarball', '--config', $customConfigBash, '--asset-dir', $customAssetDirBash, '--runtime', 'linux-x64', '--published', $customPublishedBash, '--version', '9.8.7', '--output', $customOutputBash)
        Assert-Packaging ($customResult.ExitCode -eq 0) "Custom MIME filename tarball failed: $($customResult.Output)"
        $customExtract = Join-Path $sandbox 'custom-extract'
        New-Item -ItemType Directory -Path $customExtract | Out-Null
        $customArchive = Join-Path $customOutput 'freex-9.8.7-linux-x64.tar.gz'
        $customArchiveBash = Convert-ToBashPath $customArchive
        $customExtractBash = Convert-ToBashPath $customExtract
        $customTarExtract = Invoke-BashScript -ScriptPath '-c' -Arguments @('tar -xzf "$1" -C "$2"', 'tar', $customArchiveBash, $customExtractBash)
        Assert-Packaging ($customTarExtract.ExitCode -eq 0) 'Custom MIME filename tarball could not be extracted.'
        $customRoot = Join-Path $customExtract 'freex-9.8.7-linux-x64'
        $customInstall = Get-Content -Raw (Join-Path $customRoot 'install.sh')
        $customUninstall = Get-Content -Raw (Join-Path $customRoot 'uninstall.sh')
        Assert-Packaging ($customInstall.Contains('mime_asset="custom-workbook-mime.xml"')) 'Custom MIME install script lost the configured filename.'
        Assert-Packaging ($customUninstall.Contains('mime_asset="custom-workbook-mime.xml"')) 'Custom MIME uninstall script lost the configured filename.'

        $badRuntime = Invoke-BashScript -ScriptPath (Join-Path $repoRoot 'src/FreeX.App.Avalonia/Packaging/linux/package-linux-app.sh') -Arguments @('--runtime', '../escape', '--published', $customPublishedBash, '--version', '9.8.7', '--output', $customOutputBash)
        Assert-Packaging ($badRuntime.ExitCode -eq 2) 'Traversal runtime did not fail with usage exit code 2.'
        $badVersion = Invoke-BashScript -ScriptPath (Join-Path $repoRoot 'src/FreeX.App.Avalonia/Packaging/linux/package-linux-app.sh') -Arguments @('--runtime', 'linux-x64', '--published', $customPublishedBash, '--version', '1/escape', '--output', $customOutputBash)
        Assert-Packaging ($badVersion.ExitCode -eq 2) 'Separator-containing version did not fail with usage exit code 2.'
        $badAppImageRuntime = Invoke-BashScript -ScriptPath (Join-Path $repoRoot 'src/FreeX.App.Avalonia/Packaging/linux/build-appimage.sh') -Arguments @('--runtime', 'linux-mips', '--published', $customPublishedBash, '--version', '9.8.7', '--output', $customOutputBash, '--appimagetool', '/does/not/run')
        Assert-Packaging ($badAppImageRuntime.ExitCode -eq 1) 'Unsupported AppImage runtime did not fail with exit code 1.'
        $badConfig = Join-Path $sandbox 'bad-config.conf'
        $badConfigLines = @(Get-Content $customConfig) -replace '^library_dir=.*$', 'library_dir=../escape'
        Write-Utf8NoBom -Path $badConfig -Lines $badConfigLines
        $badConfigBash = Convert-ToBashPath $badConfig
        $badConfigResult = Invoke-BashScript -ScriptPath $sharedPath -Arguments @('--operation', 'tarball', '--config', $badConfigBash, '--asset-dir', $customAssetDirBash, '--runtime', 'linux-x64', '--published', $customPublishedBash, '--version', '9.8.7', '--output', $customOutputBash, '--dry-run')
        Assert-Packaging ($badConfigResult.ExitCode -eq 1) 'Traversal product config value did not fail with config exit code 1.'
    } finally {
        Remove-Item -LiteralPath $sandbox -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Host "Bash syntax, wrapper forwarding, dry-run, tarball metadata, and negative offline checks passed."
} else {
    Write-Host "Bash syntax and offline packaging checks skipped because a usable bash executable is unavailable."
}

if ($errors.Count -gt 0) {
    Write-Host "Linux packaging script preflight FAILED with $($errors.Count) issue(s)."
    exit 1
}

Write-Host "Linux packaging script preflight PASSED."
