param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$errors = [System.Collections.Generic.List[string]]::new()

function Add-PortabilityError([string]$Message) {
    $errors.Add($Message)
}

$trackedPaths = @(& git -C $repoRoot ls-files)
if ($LASTEXITCODE -ne 0) {
    throw "Could not enumerate tracked files for the portability audit."
}

foreach ($group in @($trackedPaths | Group-Object { $_.ToLowerInvariant() } | Where-Object Count -gt 1)) {
    Add-PortabilityError "Case-insensitive tracked-path collision: $($group.Group -join '; ')"
}
foreach ($group in @($trackedPaths | Group-Object { $_.Normalize([Text.NormalizationForm]::FormC).ToLowerInvariant() } | Where-Object Count -gt 1)) {
    Add-PortabilityError "Unicode/case-normalized tracked-path collision: $($group.Group -join '; ')"
}

$trackedPathSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$trackedPathByCaseFold = @{}
foreach ($trackedPath in $trackedPaths) {
    [void]$trackedPathSet.Add($trackedPath)
    $trackedPathByCaseFold[$trackedPath.ToLowerInvariant()] = $trackedPath
}

$reservedLeafPattern = '^(?i:(?:con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$))'
foreach ($path in $trackedPaths) {
    $leaf = ($path -split '/')[-1]
    if ($leaf -match '[. ]$' -or $leaf -match $reservedLeafPattern) {
        Add-PortabilityError "Windows-incompatible tracked path: $path"
    }
}

$attributes = Get-Content -LiteralPath (Join-Path $repoRoot '.gitattributes') -Raw
foreach ($requiredRule in @('*.sh text eol=lf', '*.ps1 text eol=lf', '*.psm1 text eol=lf', '*.yml text eol=lf', '*.yaml text eol=lf')) {
    if (-not $attributes.Contains($requiredRule)) {
        Add-PortabilityError ".gitattributes is missing required portability rule: $requiredRule"
    }
}

$portableTextPaths = @($trackedPaths | Where-Object {
    $_ -match '(?i)\.(?:ps1|psm1|sh|ya?ml)$'
})
foreach ($relativePath in $portableTextPaths) {
    $bytes = [System.IO.File]::ReadAllBytes((Join-Path $repoRoot $relativePath))
    if ($bytes -contains 13) {
        Add-PortabilityError "$relativePath contains CR/CRLF bytes; scripts and workflows must use LF endings."
    }
}

$shellScripts = @($trackedPaths | Where-Object { $_.EndsWith('.sh', [System.StringComparison]::OrdinalIgnoreCase) })
foreach ($relativePath in $shellScripts) {
    $path = Join-Path $repoRoot $relativePath
    & bash -n $path
    if ($LASTEXITCODE -ne 0) {
        Add-PortabilityError "Bash syntax validation failed: $relativePath"
    }
}

$powerShellScripts = @($trackedPaths | Where-Object {
    $_.EndsWith('.ps1', [System.StringComparison]::OrdinalIgnoreCase) -or
        $_.EndsWith('.psm1', [System.StringComparison]::OrdinalIgnoreCase)
})
$powerShellAsts = @{}
foreach ($relativePath in $powerShellScripts) {
    $path = Join-Path $repoRoot $relativePath
    $tokens = $null
    $parseErrors = $null
    $powerShellAsts[$relativePath] = [System.Management.Automation.Language.Parser]::ParseFile(
        $path,
        [ref]$tokens,
        [ref]$parseErrors)
    foreach ($parseError in @($parseErrors)) {
        Add-PortabilityError "$relativePath has a PowerShell parse error: $($parseError.Message)"
    }
}

# A path can work on Windows and fail only after checkout on a case-sensitive file system.
# Check every static repository path mentioned by PowerShell, but report only proven case
# mismatches (not paths that may intentionally name generated output).
foreach ($relativePath in $powerShellScripts) {
    $strings = @($powerShellAsts[$relativePath].FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.StringConstantExpressionAst] -or
            $node -is [System.Management.Automation.Language.ExpandableStringExpressionAst]
    }, $true))
    foreach ($stringNode in $strings) {
        $candidate = ([string]$stringNode.Value).Replace([string][char]92, '/').TrimStart('./')
        if ($candidate -notmatch '^(?:\.github|tools|eng|src|shared|freew|freep|tests|docs|release)/' -or
            $candidate -match '[*?`${}%]' -or $candidate -match '\s') {
            continue
        }
        $folded = $candidate.ToLowerInvariant()
        if (-not $trackedPathSet.Contains($candidate) -and $trackedPathByCaseFold.ContainsKey($folded)) {
            Add-PortabilityError "$relativePath references '$candidate' with incorrect case; tracked path is '$($trackedPathByCaseFold[$folded])'."
        }
    }
}

$windowsOnlyPowerShellScripts = @(
    'tools/Capture-FreePPowerPointChrome.ps1',
    'tools/Capture-FreePResponsiveChrome.ps1',
    'tools/Capture-FreeWWordChrome.ps1',
    'tools/FreeW.RenderCompare/Export-WordPdfs.ps1',
    'tools/FreeW.RenderCompare/Export-WordPdfsVisible.ps1',
    'tools/Invoke-ForegroundCapture.ps1',
    'tools/Publish-UserTestBuild.ps1',
    'tools/Run-FidelityBatch.ps1',
    'tools/Run-FreeWWordBaselineEvidence.ps1',
    'tools/Run-UxParityScenarioBatch.ps1',
    'tools/Run-UxParitySuite.ps1',
    'tools/ScreenshotCaptureSupport.ps1',
    'tools/screenshot_excel.ps1',
    'tools/screenshot_ribbon.ps1',
    'tools/screenshot_ribbon_avalonia.ps1',
    'freew-fidelity-corpus/tools/Render-WordBaseline.ps1',
    'freew-fidelity-corpus/tools/Run-FreeWVisualEvidence.ps1',
    'freew-fidelity-corpus/tools/Run-VisualFidelity.ps1',
    'freew/build/publish-windows.ps1'
)
$portablePowerShellScripts = @($powerShellScripts | Where-Object { $_ -notin $windowsOnlyPowerShellScripts })
foreach ($relativePath in $portablePowerShellScripts) {
    $path = Join-Path $repoRoot $relativePath
    $source = Get-Content -LiteralPath $path -Raw
    if ($relativePath -ne 'tools/Test-CrossPlatformPortability.ps1' -and $source.Contains('pwsh.exe')) {
        Add-PortabilityError "$relativePath uses Windows-specific pwsh.exe instead of the portable pwsh command."
    }
    $ast = $powerShellAsts[$relativePath]

    $commands = @($ast.FindAll({
        param($node)
        $node -is [System.Management.Automation.Language.CommandAst]
    }, $true))
    foreach ($command in $commands) {
        if ($command.GetCommandName() -ieq 'powershell.exe' -or $command.GetCommandName() -ieq 'cmd.exe') {
            Add-PortabilityError "$relativePath invokes Windows-only '$($command.GetCommandName())' at line $($command.Extent.StartLineNumber)."
        }
        if ($command.GetCommandName() -eq 'Join-Path' -and $command.Extent.Text.Contains('\')) {
            Add-PortabilityError "$relativePath passes a Windows-separated child path to Join-Path at line $($command.Extent.StartLineNumber)."
        }
    }

    if ($source -match '(?m)\$env:PATH\s*=.*;|\$env:PATH\s*\+=\s*["''];') {
        Add-PortabilityError "$relativePath hardcodes the Windows PATH separator instead of [IO.Path]::PathSeparator."
    }
    if ($source -match '(?m)\bchmod\s+[^\r\n]*\s--(?:\s|["''])') {
        Add-PortabilityError "$relativePath uses GNU-only chmod '--' syntax, which is not accepted by BSD chmod."
    }
    if ($source -match '(?m)\.Trim(?:Start|End)\(\s*["'']\\["'']\s*\)') {
        Add-PortabilityError "$relativePath trims only a Windows directory separator; use both platform separators or a shared helper."
    }
    if (($source.Contains('ProgramFiles') -or $source.Contains('ProgramFiles(x86)')) -and
        $source -notmatch '(?m)(Test-ToolIsWindows|DirectorySeparatorChar\s+-eq\s+["'']\\)') {
        Add-PortabilityError "$relativePath reads Windows ProgramFiles without an explicit Windows-host guard."
    }
}

foreach ($relativePath in $windowsOnlyPowerShellScripts) {
    if ($relativePath -notin $powerShellScripts) {
        Add-PortabilityError "Windows-only script classification is stale or case-mismatched: $relativePath"
    }
}

foreach ($relativePath in $powerShellScripts) {
    if ($relativePath -in @('tools/ToolScriptSupport.ps1', 'tools/Test-CrossPlatformPortability.ps1')) {
        continue
    }

    $source = Get-Content -LiteralPath (Join-Path $repoRoot $relativePath) -Raw
    if ($source.Contains('.ResolveLinkTarget(')) {
        Add-PortabilityError "$relativePath bypasses the shared canonical-path resolver in ToolScriptSupport.ps1."
    }
}

$toolScripts = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'tools') -Recurse -File -Filter '*.ps1')
foreach ($script in $toolScripts) {
    if ($script.FullName -eq $PSCommandPath) {
        continue
    }
    $source = Get-Content -LiteralPath $script.FullName -Raw
    if ($source.Contains('[System.IO.Path]::GetRelativePath(') -or $source.Contains('[IO.Path]::GetRelativePath(')) {
        Add-PortabilityError "$($script.FullName.Substring($repoRoot.Length + 1)) uses Path.GetRelativePath, which is unavailable in Windows PowerShell 5.1."
    }
    if ($script.Name.StartsWith('Generate-', [System.StringComparison]::OrdinalIgnoreCase) -and
        ($source.Contains('sourceSha256') -or $source.Contains('SourceHashes')) -and
        $source.Contains('Get-FileHash')) {
        Add-PortabilityError "$($script.FullName.Substring($repoRoot.Length + 1)) hashes source bytes directly; use Get-ToolNormalizedTextSha256 so checkout line endings do not stale generated evidence."
    }
}

$shellIndexEntries = @(& git -C $repoRoot ls-files --stage -- '*.sh')
foreach ($entry in $shellIndexEntries) {
    if ($entry -notmatch '^(?<mode>\d{6})\s+[0-9a-f]+\s+\d+\s+(?<path>.+)$') {
        Add-PortabilityError "Could not parse shell-script Git index entry: $entry"
        continue
    }
    if ($Matches.mode -ne '100755') {
        Add-PortabilityError "$($Matches.path) must be tracked executable (Git mode 100755)."
    }
}

$allIndexEntries = @(& git -C $repoRoot ls-files --stage)
foreach ($entry in $allIndexEntries) {
    if ($entry -notmatch '^(?<mode>\d{6})\s+[0-9a-f]+\s+\d+\s+(?<path>.+)$' -or $Matches.mode -ne '120000') {
        continue
    }
    $linkPath = $Matches.path
    $target = (Get-Content -LiteralPath (Join-Path $repoRoot $linkPath) -Raw).Replace([string][char]92, '/')
    if ([IO.Path]::IsPathRooted($target)) {
        Add-PortabilityError "$linkPath is an absolute symlink; repository links must be relative."
        continue
    }
    $linkDirectory = Split-Path -Parent $linkPath
    $fullTargetPath = [IO.Path]::GetFullPath((Join-Path (Join-Path $repoRoot $linkDirectory) $target))
    $rootPrefix = [IO.Path]::GetFullPath($repoRoot).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $pathComparison = if ([IO.Path]::DirectorySeparatorChar -eq [char]92) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $targetPath = if ($fullTargetPath.StartsWith($rootPrefix, $pathComparison)) {
        $fullTargetPath.Substring($rootPrefix.Length).Replace([string][char]92, '/')
    }
    else {
        $null
    }
    if ([string]::IsNullOrWhiteSpace($targetPath) -or -not $trackedPathSet.Contains($targetPath)) {
        Add-PortabilityError "$linkPath has a missing or escaping symlink target '$target'."
    }
}

foreach ($relativePath in $shellScripts) {
    $source = Get-Content -LiteralPath (Join-Path $repoRoot $relativePath) -Raw
    $firstLine = ($source -split "`n", 2)[0].TrimEnd("`r")
    if ($firstLine -notmatch '^#!(?:/usr/bin/env (?:bash|sh)|/bin/(?:bash|sh))$') {
        Add-PortabilityError "$relativePath must use a portable bash/sh shebang."
    }
    if ($source -match '(?m)\bchmod\s+[^\r\n]*\s--(?:\s|["''])') {
        Add-PortabilityError "$relativePath uses GNU-only chmod '--' syntax, which is not accepted by BSD chmod."
    }
    if ($source -match '(?m)(?:^|[;&|]\s*)(?:powershell(?:\.exe)?|cmd(?:\.exe)?)\b') {
        Add-PortabilityError "$relativePath invokes a Windows-only shell from a Unix script."
    }
}

$appReleaseWorkflow = Get-Content -LiteralPath (Join-Path $repoRoot '.github/workflows/app-tester-release.yml') -Raw
foreach ($windowsOnlyToken in @('powershell.exe', 'cmd.exe')) {
    if ($appReleaseWorkflow.Contains($windowsOnlyToken)) {
        Add-PortabilityError "App Tester Release contains an unscoped Windows-only command token: $windowsOnlyToken"
    }
}

if ($errors.Count -gt 0) {
    throw "Cross-platform portability validation failed:`n - $($errors -join "`n - ")"
}

Write-Host "Cross-platform portability checks passed for $($trackedPaths.Count) tracked paths, $($portableTextPaths.Count) LF-normalized scripts/workflows, all $($powerShellScripts.Count) PowerShell scripts ($($portablePowerShellScripts.Count) portable, $($windowsOnlyPowerShellScripts.Count) explicitly Windows-only), and all $($shellScripts.Count) executable shell scripts."
