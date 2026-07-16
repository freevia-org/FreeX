param(
    [string]$ScriptDirectory = "tools"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot

. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Assert-ToolSourceCentralization {
    param([Parameter(Mandatory = $true)][string]$ToolRoot)

    $supportPath = Join-Path $ToolRoot "ToolScriptSupport.ps1"
    $support = Get-Content -LiteralPath $supportPath -Raw
    foreach ($requiredHelper in @(
            "function Resolve-ToolRepoPath",
            "function Test-ToolPathRooted",
            "function ConvertTo-ToolPlatformPath",
            "function Resolve-ToolFullPath",
            "function Resolve-InputPath",
            "function Get-ToolRelativePath",
            "function ConvertTo-ToolNormalizedRelativePath",
            "function Test-ToolExcludedPath",
            "function Get-ToolTrackedRepositoryFiles",
            "function Test-ToolIgnoredDirectoryName",
            "function Get-ToolProjectFiles",
            "function ConvertTo-ToolRepoRelativePath",
            "function Read-ToolJson",
            "function ConvertTo-ToolMarkdownCell",
            "function Test-ToolGeneratedContentMatches",
            "function Invoke-FidelityCorpusDownload",
            "function Invoke-ToolProcess",
            "function Invoke-DotNetStep",
            "function Invoke-PowerShellStep",
            "function Invoke-DotNetRun",
            "function Invoke-DotNetBuild",
            "function Invoke-DotNetRunNoBuild",
            "function Get-RepoRoot",
            "function Get-GitValue",
            "function Resolve-FreeXExe")) {
        if (-not $support.Contains($requiredHelper)) {
            throw "ToolScriptSupport.ps1 is missing required helper '$requiredHelper'."
        }
    }

    $generatorNames = @(
        "Generate-DialogParityInventory.ps1",
        "Generate-DialogVisualEvidenceSummary.ps1",
        "Generate-ConditionalFormatOpenedStateEvidence.ps1",
        "Generate-CrossAppParityDashboard.ps1"
    )
    foreach ($generatorName in $generatorNames) {
        $generatorPath = Join-Path $ToolRoot $generatorName
        $generator = Get-Content -LiteralPath $generatorPath -Raw
        if (-not $generator.Contains('ToolScriptSupport.ps1')) {
            throw "$generatorName must dot-source ToolScriptSupport.ps1."
        }

        if ($generator -match 'function\s+(ConvertTo-RepoRelativePath|Read-(JsonFile|GeneratedJson)|Escape-MarkdownCell|Test-FileContentMatches)\b') {
            throw "$generatorName redeclares a helper owned by ToolScriptSupport.ps1."
        }
    }

    $centralizedScriptHelpers = [ordered]@{
        "Test-JsonFiles.ps1" = @("Resolve-RepoPath", "Test-IsExcludedPath", "Get-RepositoryRelativePath", "Get-TrackedRepositoryFiles")
        "Test-XmlFiles.ps1" = @("Resolve-RepoPath", "Test-IsBuildOutputPath", "Get-RepositoryRelativePath", "Get-TrackedRepositoryFiles")
        "Test-RepositoryPreflight.ps1" = @("Resolve-RepoPath")
        "Test-SolutionProjects.ps1" = @("Resolve-RepoPath", "Normalize-RelativePath", "Get-RelativePath", "Test-IsIgnoredDirectoryName", "Get-ProjectFiles")
        "Test-DotNetProjectReferences.ps1" = @("Resolve-RepoPath", "Get-RelativeRepoPath", "Test-IsIgnoredDirectoryName", "Get-ProjectFiles")
        "Test-DotNetSdkReadiness.ps1" = @("Resolve-RepoPath", "Get-RelativeRepoPath", "Test-IsIgnoredDirectoryName", "Get-ProjectFiles")
        "Test-ConflictMarkers.ps1" = @("Resolve-RepoPath", "Get-RelativeRepoPath", "Test-IsIgnoredPath")
        "Test-TesterReleaseReadiness.ps1" = @("Resolve-RepoPath")
        "Invoke-ForegroundCapture.ps1" = @("Resolve-RepoPath")
        "Test-LinuxPublicPreviewReadiness.ps1" = @("Resolve-InputPath")
        "Test-LinuxPublicPreviewPromotion.ps1" = @("Resolve-InputPath")
        "Test-LinuxHumanValidationChecklist.ps1" = @("Resolve-InputPath")
        "Test-MacOsPublicPreviewReadiness.ps1" = @("Resolve-InputPath")
        "Test-MacOsPublicPreviewPromotion.ps1" = @("Resolve-InputPath")
        "Test-MacOsHumanValidationChecklist.ps1" = @("Resolve-InputPath")
        "Run-UxParitySuite.ps1" = @("Get-RepoRoot", "Get-GitValue", "Resolve-FreeXExe")
        "Run-UxParityScenarioBatch.ps1" = @("Get-RepoRoot", "Get-GitValue", "Resolve-FreeXExe")
        "Publish-UserTestBuild.ps1" = @()
    }
    foreach ($entry in $centralizedScriptHelpers.GetEnumerator()) {
        $scriptPath = Join-Path $ToolRoot $entry.Key
        $script = Get-Content -LiteralPath $scriptPath -Raw
        if (-not $script.Contains("ToolScriptSupport.ps1")) {
            throw "$($entry.Key) must dot-source ToolScriptSupport.ps1 for shared helpers."
        }

        foreach ($helperName in $entry.Value) {
            if ($script -match "function\s+$([regex]::Escape($helperName))\b") {
                throw "$($entry.Key) redeclares shared helper '$helperName'."
            }
        }
    }

    $repoRoot = Split-Path -Parent $ToolRoot
    $orderedToolScripts = [ordered]@{
        "freew-fidelity-corpus\tools\Run-FreeWVisualEvidence.ps1" = @("Resolve-RepositoryPath", "Invoke-DotNetStep", "Invoke-PowerShellStep")
        "tools\Run-FreeWWordBaselineEvidence.ps1" = @("Resolve-FullPath", "Invoke-DotNetRun", "Invoke-DotNetBuild", "Invoke-DotNetRunNoBuild")
        "tools\FreeW.RenderCompare\Export-WordPdfsVisible.ps1" = @("Resolve-FullPath")
    }
    foreach ($entry in $orderedToolScripts.GetEnumerator()) {
        $scriptPath = Join-Path $repoRoot $entry.Key
        $script = Get-Content -LiteralPath $scriptPath -Raw
        if (-not $script.Contains("ToolScriptSupport.ps1")) {
            throw "$($entry.Key) must dot-source ToolScriptSupport.ps1."
        }

        foreach ($helperName in $entry.Value) {
            if ($script -match "function\s+$([regex]::Escape($helperName))\b") {
                throw "$($entry.Key) redeclares shared helper '$helperName'."
            }
        }
    }

    $publishScript = Get-Content -LiteralPath (Join-Path $ToolRoot "Publish-UserTestBuild.ps1") -Raw
    if (-not $publishScript.Contains("ConvertTo-ToolXmlAttribute")) {
        throw "Publish-UserTestBuild.ps1 must use ConvertTo-ToolXmlAttribute."
    }

    if ($publishScript -match 'function\s+ConvertTo-XmlAttributeValue\b') {
        throw "Publish-UserTestBuild.ps1 redeclares obsolete helper ConvertTo-XmlAttributeValue."
    }

    $screenshotSupportPath = Join-Path $ToolRoot "ScreenshotCaptureSupport.ps1"
    $screenshotSupport = Get-Content -LiteralPath $screenshotSupportPath -Raw
    foreach ($requiredCaptureHelper in @(
            "public class ScreenshotWin32",
            "function Get-WindowTitle",
            "function Get-ForegroundWindowInfo",
            "function Assert-ForegroundWindowOwnership",
            "function Capture-ScreenRectangle")) {
        if (-not $screenshotSupport.Contains($requiredCaptureHelper)) {
            throw "ScreenshotCaptureSupport.ps1 is missing required helper '$requiredCaptureHelper'."
        }
    }

    foreach ($scenarioName in @("screenshot_ribbon.ps1", "screenshot_excel.ps1")) {
        $scenario = Get-Content -LiteralPath (Join-Path $ToolRoot $scenarioName) -Raw
        if ($scenario -match 'public class Win32[ce]\b|public class WindowInfo[CE]\b|\$g\s*\.\s*CopyFromScreen|Add-Type\s+-TypeDefinition.*DllImport') {
            throw "$scenarioName contains duplicated Win32 or screen-capture interop."
        }

        if ($scenario -notmatch '\.\s*\(Join-Path \$PSScriptRoot "ScreenshotCaptureSupport\.ps1"\)') {
            throw "$scenarioName must dot-source ScreenshotCaptureSupport.ps1."
        }

        if ($scenario -match 'function\s+(Get-WindowTitle|Get-ForegroundWindowInfo|Assert-ForegroundWindowOwnership)\b') {
            throw "$scenarioName redeclares a helper owned by ScreenshotCaptureSupport.ps1."
        }
    }

    $scopedPropsPath = Join-Path $ToolRoot "ToolProjects.props"
    $scopedProps = Get-Content -LiteralPath $scopedPropsPath -Raw
    if (-not $scopedProps.Contains('<Nullable>enable</Nullable>') -or
        -not $scopedProps.Contains('<ImplicitUsings>enable</ImplicitUsings>')) {
        throw "tools/ToolProjects.props must define the exact common tool metadata."
    }

    $retainedProjectMetadata = @("FreeP.RenderCompare.Tests.csproj")
    foreach ($project in @(Get-ChildItem -LiteralPath $ToolRoot -Filter "*.csproj" -File -Recurse)) {
        $projectText = Get-Content -LiteralPath $project.FullName -Raw
        if ($retainedProjectMetadata -contains $project.Name) {
            if (-not $projectText.Contains('<Nullable>enable</Nullable>') -or
                -not $projectText.Contains('<ImplicitUsings>enable</ImplicitUsings>')) {
                throw "$($project.FullName) is an intentional scoped-props exception and must retain its evaluated metadata explicitly."
            }
            continue
        }

        if (-not $projectText.Contains('Import Project="..\ToolProjects.props"')) {
            throw "$($project.FullName) does not import tools/ToolProjects.props."
        }

        if ($projectText -match '<Nullable>\s*enable\s*</Nullable>|<ImplicitUsings>\s*enable\s*</ImplicitUsings>') {
            throw "$($project.FullName) redeclares metadata centralized in tools/ToolProjects.props."
        }
    }

    Write-Host "Validated shared tooling source guards."
}

function Assert-ScreenshotCaptureSupportBehavior {
    param([Parameter(Mandatory = $true)][string]$ToolRoot)

    if ([System.Management.Automation.PSTypeName]'ScreenshotWin32'.Type) {
        throw "ScreenshotCaptureSupport behavior guard requires an isolated ScreenshotWin32 test type."
    }

    Add-Type @"
using System;
using System.Text;

public class ScreenshotWin32 {
    public static IntPtr GetForegroundWindow() {
        return new IntPtr(42);
    }

    public static uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId) {
        processId = 1234;
        return 0;
    }

    public static int GetWindowText(IntPtr hWnd, StringBuilder text, int capacity) {
        text.Append("Stub Window");
        return 11;
    }
}
"@

    . (Join-Path $ToolRoot "ScreenshotCaptureSupport.ps1")
    if ((Get-WindowTitle ([IntPtr]42)) -ne "Stub Window") {
        throw "Get-WindowTitle did not preserve Win32 window text behavior."
    }

    $foreground = Get-ForegroundWindowInfo
    if ($foreground.Handle -ne "42" -or $foreground.ProcessId -ne 1234 -or $foreground.Title -ne "Stub Window") {
        throw "Get-ForegroundWindowInfo did not preserve foreground handle, PID, or title behavior."
    }

    $script:unexpectedFailureAction = $false
    Assert-ForegroundWindowOwnership 1234 "Stub Window" "matching foreground" {
        $script:unexpectedFailureAction = $true
    }
    if ($script:unexpectedFailureAction) {
        throw "Assert-ForegroundWindowOwnership invoked its failure action for matching foreground ownership."
    }

    $script:failureOperation = $null
    $script:failureReason = $null
    $script:errorMessage = $null
    try {
        Assert-ForegroundWindowOwnership 4321 "Other Window" "stub mismatch" {
            param($operation, $expectedPid, $expectedTitle, $reason)
            $script:failureOperation = $operation
            $script:failureReason = $reason
        }
    }
    catch {
        $script:errorMessage = $_.Exception.Message
    }

    if ($script:failureOperation -ne "stub mismatch" -or
        $script:failureReason -ne "Foreground window 'Stub Window' (PID 1234) did not match expected 'Other Window' (PID 4321)." -or
        $script:errorMessage -ne "Blocked: foreground window 'Stub Window' (PID 1234) does not match expected 'Other Window' (PID 4321) before stub mismatch.") {
        throw "Assert-ForegroundWindowOwnership did not preserve mismatch callback and blocked error behavior."
    }

    Write-Host "Validated ScreenshotCaptureSupport source and behavior guards."
}

function Assert-SharedToolHelperBehavior {
    param([Parameter(Mandatory = $true)][string]$RepoRoot)

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("freex-tool-helper-behavior-" + [guid]::NewGuid().ToString("N"))
    $syntheticRepoRoot = Join-Path (Join-Path $tempRoot ".worktrees") "linked-repo"
    New-Item -ItemType Directory -Force -Path $syntheticRepoRoot | Out-Null
    $originalLocation = Get-Location
    try {
        Set-Location ([System.IO.Path]::GetTempPath())

        $expectedRelativePath = "src\bin\sample.json"
        $relativeForwardSlashPath = "src/bin/sample.json"
        $relativeBackslashPath = "src\bin\sample.json"
        $absolutePath = Join-Path (Join-Path (Join-Path $syntheticRepoRoot "src") "bin") "sample.json"
        $absoluteForwardSlashPath = $absolutePath.Replace([string][char]92, "/")

        foreach ($path in @($relativeForwardSlashPath, $relativeBackslashPath, $absolutePath, $absoluteForwardSlashPath)) {
            $relativePath = ConvertTo-ToolRepoRelativePath -Path $path -RepoRoot $syntheticRepoRoot
            if ($relativePath -cne $expectedRelativePath) {
                throw "ConvertTo-ToolRepoRelativePath was not slash-agnostic or repo-root anchored for '$path': '$relativePath'."
            }

            if (-not (Test-ToolExcludedPath -Path $path -RepoRoot $syntheticRepoRoot)) {
                throw "Test-ToolExcludedPath did not exclude '$path'."
            }
        }

        $nonExcludedPath = Join-Path (Join-Path $syntheticRepoRoot "src") "sample.json"
        if (Test-ToolExcludedPath -Path $nonExcludedPath.Replace([string][char]92, "/") -RepoRoot $syntheticRepoRoot) {
            throw "Test-ToolExcludedPath treated a non-excluded absolute path as excluded."
        }

        $relativeFromRoot = Get-ToolRelativePath -RootPath $syntheticRepoRoot -Path $absoluteForwardSlashPath
        if ($relativeFromRoot -cne "src/bin/sample.json") {
            throw "Get-ToolRelativePath returned '$relativeFromRoot' for a linked-worktree path."
        }

        $resolvedTools = Resolve-ToolRepoPath -Path "tools\ToolScriptSupport.ps1" -RepoRoot $RepoRoot
        $expectedTools = Join-Path (Join-Path $RepoRoot "tools") "ToolScriptSupport.ps1"
        if (-not [System.IO.Path]::GetFullPath($resolvedTools).Equals([System.IO.Path]::GetFullPath($expectedTools), [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Resolve-ToolRepoPath was not stable from outside the repository working directory."
        }
    }
    finally {
        Set-Location $originalLocation
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    if (@(Get-ToolTrackedRepositoryFiles -RepoRoot $RepoRoot).Count -eq 0) {
        throw "Get-ToolTrackedRepositoryFiles returned no tracked files."
    }

    if (@(Get-ToolProjectFiles -Directory (Get-Item -LiteralPath $RepoRoot)).Count -eq 0) {
        throw "Get-ToolProjectFiles returned no project files."
    }

    $escapedXml = ConvertTo-ToolXmlAttribute -Value 'CN=A&B <C> "D"'
    if ($escapedXml -cne 'CN=A&amp;B &lt;C&gt; &quot;D&quot;') {
        throw "ConvertTo-ToolXmlAttribute returned an unexpected escaped value."
    }

    Write-Host "Validated shared repository helper behavior."
}

function Assert-ToolProcessBehavior {
    param([Parameter(Mandatory = $true)][string]$ToolRoot)

    . (Join-Path $ToolRoot "ToolScriptSupport.ps1")
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("freex-tool-process-behavior-" + [guid]::NewGuid().ToString("N"))
    $cwdRoot = Join-Path $tempRoot "cwd-root"
    $syntheticRepoRoot = Join-Path $tempRoot "repo-root"
    $workingRoot = Join-Path $tempRoot "working-root"
    New-Item -ItemType Directory -Force -Path $cwdRoot, $syntheticRepoRoot, $workingRoot | Out-Null
    $originalLocation = Get-Location
    try {
        Set-Location -LiteralPath $cwdRoot
        $cwdResolved = Resolve-ToolFullPath -Path "child\cwd.txt"
        $expectedCwdPath = Join-Path $cwdRoot "child\cwd.txt"
        if (-not [System.IO.Path]::GetFullPath($cwdResolved).Equals([System.IO.Path]::GetFullPath($expectedCwdPath), [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Resolve-ToolFullPath did not preserve cwd-relative resolution: '$cwdResolved'."
        }

        $repoPath = Resolve-ToolRepoPath -Path "src/tools\child.txt" -RepoRoot $syntheticRepoRoot.Replace([string][char]92, "/")
        $expectedRepoPath = Join-Path $syntheticRepoRoot "src\tools\child.txt"
        if (-not [System.IO.Path]::GetFullPath($repoPath).Equals([System.IO.Path]::GetFullPath($expectedRepoPath), [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Resolve-ToolRepoPath did not preserve repo-relative resolution or slash handling: '$repoPath'."
        }

        $probePath = Join-Path $tempRoot "tool-process-probe.ps1"
        $probeOutputPath = Join-Path $tempRoot "probe-output.json"
        @'
param(
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [Parameter(Mandatory = $true)][string]$First,
    [Parameter(Mandatory = $true)][string]$Second
)

Write-Output "synthetic stdout"
[Console]::Error.WriteLine("synthetic stderr")
[ordered]@{
    WorkingDirectory = (Get-Location).Path
    First = $First
    Second = $Second
} | ConvertTo-Json | Set-Content -LiteralPath $OutputPath
'@ | Set-Content -LiteralPath $probePath -Encoding UTF8

        $powerShell = (Get-Command powershell.exe -ErrorAction Stop).Path
        Invoke-ToolProcess `
            -FilePath $powerShell `
            -Arguments @(
                "-NoProfile",
                "-ExecutionPolicy", "Bypass",
                "-File", $probePath,
                "-OutputPath", $probeOutputPath,
                "first value",
                "second value with spaces"
            ) `
            -WorkingDirectory ($workingRoot.Replace([string][char]92, "/")) `
            -FailureMessage "synthetic process probe"

        $probe = Get-Content -LiteralPath $probeOutputPath -Raw | ConvertFrom-Json
        if (-not [System.IO.Path]::GetFullPath($probe.WorkingDirectory).Equals([System.IO.Path]::GetFullPath($workingRoot), [System.StringComparison]::OrdinalIgnoreCase) -or
            $probe.First -cne "first value" -or $probe.Second -cne "second value with spaces") {
            throw "Invoke-ToolProcess did not preserve working directory or argument-array forwarding: $($probe | ConvertTo-Json -Compress)."
        }

        if (-not (Get-Location).Path.Equals($cwdRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Invoke-ToolProcess did not restore the parent working directory."
        }

        $nonzeroMessage = $null
        try {
            Invoke-ToolProcess `
                -FilePath $powerShell `
                -Arguments @("-NoProfile", "-Command", "exit 17") `
                -FailureMessage "synthetic nonzero process"
        }
        catch {
            $nonzeroMessage = $_.Exception.Message
        }

        if ($nonzeroMessage -ne "synthetic nonzero process with exit code 17" -or $LASTEXITCODE -ne 17) {
            throw "Invoke-ToolProcess did not preserve nonzero exit propagation: message='$nonzeroMessage', exit=$LASTEXITCODE."
        }
    }
    finally {
        Set-Location $originalLocation
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Validated synthetic tool process forwarding and failure behavior."
}

function Assert-GeneratedDocCheckNewlineSemantics {
    param([Parameter(Mandatory = $true)][string]$ToolRoot)

    foreach ($generatorName in @(
            "Generate-ConditionalFormatOpenedStateEvidence.ps1",
            "Generate-DialogParityInventory.ps1",
            "Generate-DialogVisualEvidenceSummary.ps1")) {
        $generator = Get-Content -LiteralPath (Join-Path $ToolRoot $generatorName) -Raw
        $checkLines = @($generator -split "`r?`n" | Where-Object { $_ -match "Test-ToolGeneratedContentMatches" })
        if ($checkLines.Count -ne 2 -or @($checkLines | Where-Object { $_ -notmatch "-NormalizeNewlines\b" }).Count -ne 0) {
            throw "$generatorName must normalize newlines for both generated-document checks."
        }
    }

    . (Join-Path $ToolRoot "ToolScriptSupport.ps1")
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("freex-tool-script-tests-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
    try {
        $actualPath = Join-Path $tempRoot "generated.txt"
        [System.IO.File]::WriteAllText(
            $actualPath,
            "first`r`nsecond`r`n",
            [System.Text.UTF8Encoding]::new($false))

        Test-ToolGeneratedContentMatches `
            -ExpectedContent "first`nsecond`n" `
            -ActualPath $actualPath `
            -Label "Normalized generated-content behavior" `
            -GeneratorScriptName "test generator" `
            -NormalizeNewlines

        $strictComparisonRejected = $false
        try {
            Test-ToolGeneratedContentMatches `
                -ExpectedContent "first`nsecond`n" `
                -ActualPath $actualPath `
                -Label "Strict generated-content behavior" `
                -GeneratorScriptName "test generator"
        }
        catch {
            $strictComparisonRejected = $true
        }

        if (-not $strictComparisonRejected) {
            throw "Test-ToolGeneratedContentMatches must remain strict when newline normalization is not requested."
        }
    }
    finally {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Validated generated-document newline normalization source and behavior."
}

function Assert-FidelityCorpusDownloaderBehavior {
    param([Parameter(Mandatory = $true)][string]$ToolRoot)

    . (Join-Path $ToolRoot "ToolScriptSupport.ps1")
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("freex-fidelity-downloader-" + [guid]::NewGuid().ToString("N"))
    $filesDirectory = Join-Path $tempRoot "files"
    New-Item -ItemType Directory -Force -Path $filesDirectory | Out-Null

    try {
        $manifestPath = Join-Path $tempRoot "manifest.csv"
        @(
            "id,file,source,license,url",
            "local-present,local/present.docx,local,Private,local://local/present.docx",
            "local-missing,local/missing.docx,local,Private,local://local/missing.docx",
            "existing,existing.xlsx,synthetic,MIT,https://example.invalid/existing.xlsx",
            "download,nested/downloaded.xlsx,synthetic,MIT,https://example.invalid/downloaded.xlsx",
            "failure,nested/partial.xlsx,synthetic,MIT,https://example.invalid/partial.xlsx",
            "other-source,other.xlsx,other,MIT,https://example.invalid/other.xlsx"
        ) | Set-Content -LiteralPath $manifestPath

        $localPresent = Join-Path $filesDirectory "local/present.docx"
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $localPresent) | Out-Null
        [System.IO.File]::WriteAllText($localPresent, "private")
        [System.IO.File]::WriteAllText((Join-Path $filesDirectory "existing.xlsx"), "existing")

        $downloadAction = {
            param([string]$Uri, [string]$TargetPath, $Row)
            if ($Row.id -eq "failure") {
                [System.IO.File]::WriteAllText($TargetPath, "partial")
                throw "synthetic download failure"
            }

            [System.IO.File]::WriteAllText($TargetPath, "downloaded:$Uri")
        }

        $result = Invoke-FidelityCorpusDownload `
            -ManifestPath $manifestPath `
            -FilesDirectory $filesDirectory `
            -CorpusLabel "Synthetic fidelity corpus" `
            -LocalDirectoryLabel "synthetic/files/" `
            -DownloadAction $downloadAction

        if ($result.Downloaded -ne 2 -or $result.Skipped -ne 1 -or
            $result.LocalSkipped -ne 2 -or $result.Failed -ne 1 -or
            $result.RowCount -ne 6 -or $result.ExitCode -ne 1) {
            throw "Synthetic fidelity downloader counters were not preserved: $($result | ConvertTo-Json -Compress)"
        }

        $downloadedPath = Join-Path $filesDirectory "nested/downloaded.xlsx"
        if (-not (Test-Path -LiteralPath $downloadedPath -PathType Leaf) -or
            (Get-Content -LiteralPath $downloadedPath -Raw) -cne "downloaded:https://example.invalid/downloaded.xlsx") {
            throw "Synthetic fidelity downloader did not create the nested downloaded target."
        }

        if (Test-Path -LiteralPath (Join-Path $filesDirectory "nested/partial.xlsx")) {
            throw "Synthetic fidelity downloader did not remove a partial failed target."
        }

        $sourceManifestPath = Join-Path $tempRoot "source-manifest.csv"
        @(
            "id,file,source,license,url",
            "selected,selected.xlsx,selected,MIT,https://example.invalid/selected.xlsx",
            "excluded,excluded.xlsx,excluded,MIT,https://example.invalid/excluded.xlsx"
        ) | Set-Content -LiteralPath $sourceManifestPath
        $sourceResult = Invoke-FidelityCorpusDownload `
            -ManifestPath $sourceManifestPath `
            -FilesDirectory $filesDirectory `
            -CorpusLabel "Synthetic source-filter corpus" `
            -LocalDirectoryLabel "synthetic/files/" `
            -Source "selected" `
            -DownloadAction $downloadAction
        if ($sourceResult.RowCount -ne 1 -or $sourceResult.Downloaded -ne 1 -or
            (Test-Path -LiteralPath (Join-Path $filesDirectory "excluded.xlsx"))) {
            throw "Synthetic fidelity downloader did not preserve source filtering."
        }

        $invalidManifestPath = Join-Path $tempRoot "invalid-manifest.csv"
        @(
            "id,file,source,license,url",
            "missing-license,invalid.xlsx,synthetic,,https://example.invalid/invalid.xlsx"
        ) | Set-Content -LiteralPath $invalidManifestPath
        $missingLicenseRejected = $false
        try {
            Invoke-FidelityCorpusDownload `
                -ManifestPath $invalidManifestPath `
                -FilesDirectory $filesDirectory `
                -CorpusLabel "Synthetic invalid corpus" `
                -LocalDirectoryLabel "synthetic/files/" `
                -DownloadAction $downloadAction | Out-Null
        }
        catch {
            $missingLicenseRejected = $_.Exception.Message -like "Manifest row 'missing-license' is missing a license.*"
        }
        if (-not $missingLicenseRejected) {
            throw "Synthetic fidelity downloader did not reject a missing manifest license."
        }
    }
    finally {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Validated synthetic fidelity corpus downloader behavior."
}

$resolvedScriptDirectory = Resolve-ToolRepoPath -Path $ScriptDirectory -RepoRoot $repoRoot
if (-not (Test-Path -LiteralPath $resolvedScriptDirectory -PathType Container)) {
    throw "Tool script directory was not found: $resolvedScriptDirectory"
}

$scripts = @(Get-ChildItem -LiteralPath $resolvedScriptDirectory -Filter "*.ps1" -File -Recurse |
    Where-Object { -not (Test-ToolExcludedPath -Path $_.FullName -RepoRoot $repoRoot) } |
    Sort-Object FullName)
if ($scripts.Count -eq 0) {
    throw "No PowerShell tool scripts were found in $resolvedScriptDirectory"
}

$toolsRoot = [System.IO.Path]::GetFullPath((Resolve-ToolRepoPath -Path "tools" -RepoRoot $repoRoot)).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$resolvedDirectory = [System.IO.Path]::GetFullPath($resolvedScriptDirectory).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
if ($resolvedDirectory.Equals($toolsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    Assert-ToolSourceCentralization -ToolRoot $resolvedDirectory
    Assert-ScreenshotCaptureSupportBehavior -ToolRoot $resolvedDirectory
    Assert-SharedToolHelperBehavior -RepoRoot $repoRoot
    Assert-ToolProcessBehavior -ToolRoot $resolvedDirectory
    Assert-GeneratedDocCheckNewlineSemantics -ToolRoot $resolvedDirectory
    Assert-FidelityCorpusDownloaderBehavior -ToolRoot $resolvedDirectory
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
