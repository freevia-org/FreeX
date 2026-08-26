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

$shellScripts = @($trackedPaths | Where-Object { $_.EndsWith('.sh', [System.StringComparison]::OrdinalIgnoreCase) })
foreach ($relativePath in $shellScripts) {
    $path = Join-Path $repoRoot $relativePath
    & bash -n $path
    if ($LASTEXITCODE -ne 0) {
        Add-PortabilityError "Bash syntax validation failed: $relativePath"
    }
}

$portablePowerShellScripts = @(
    'tools/Generate-CommandInventoryDocs.ps1',
    'tools/Generate-ConditionalFormatOpenedStateEvidence.ps1',
    'tools/Generate-CrossAppParityDashboard.ps1',
    'tools/Generate-DialogParityInventory.ps1',
    'tools/Generate-DialogVisualEvidenceSummary.ps1',
    'tools/Generate-FreePCommandParityInventory.ps1',
    'tools/Generate-FreePDialogPaneParityInventory.ps1',
    'tools/Generate-FreePDialogPaneVisualEvidenceManifest.ps1',
    'tools/Generate-FreePWholeWindowVisualEvidenceManifest.ps1',
    'tools/Generate-FreeWCommandInventory.ps1',
    'tools/Generate-FreeWEditingReferenceParityEvidence.ps1',
    'tools/Generate-FreeWPageLayoutDialogParityEvidence.ps1',
    'tools/Generate-FreeWShellVisualEvidence.ps1',
    'tools/Invoke-TestGate.ps1',
    'tools/New-ReleaseArtifactManifest.ps1',
    'tools/New-ReleaseSbom.ps1',
    'tools/Publish-SisterAppTesterPackages.ps1',
    'tools/Test-CrossAppParityDashboard.ps1',
    'tools/Test-FreeWShellVisualEvidence.ps1',
    'tools/Test-FreeWDialogVisualEvidence.ps1',
    'tools/Test-FreeWWordChromeEvidence.ps1',
    'tools/Test-FreePPowerPointChromeEvidence.ps1',
    'tools/Test-GeneratedDocs.ps1',
    'tools/Test-LinuxPackagingScripts.ps1',
    'tools/Test-ReleaseInstallation.ps1',
    'tools/Test-ReleasePackageContents.ps1',
    'tools/packaging/New-AppInstallers.ps1'
)
foreach ($relativePath in $portablePowerShellScripts) {
    $path = Join-Path $repoRoot $relativePath
    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($path, [ref]$tokens, [ref]$parseErrors)
    foreach ($parseError in @($parseErrors)) {
        Add-PortabilityError "$relativePath has a PowerShell parse error: $($parseError.Message)"
    }

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

Write-Host "Cross-platform portability checks passed for $($trackedPaths.Count) tracked paths, $($shellScripts.Count) shell scripts, and $($portablePowerShellScripts.Count) release/preflight scripts."
