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

    $publishScript = Get-Content -LiteralPath (Join-Path $ToolRoot "Publish-UserTestBuild.ps1") -Raw
    if (-not $publishScript.Contains("ConvertTo-ToolXmlAttribute")) {
        throw "Publish-UserTestBuild.ps1 must use ConvertTo-ToolXmlAttribute."
    }

    if ($publishScript -match 'function\s+ConvertTo-XmlAttributeValue\b') {
        throw "Publish-UserTestBuild.ps1 redeclares obsolete helper ConvertTo-XmlAttributeValue."
    }

    $screenshotSupportPath = Join-Path $ToolRoot "ScreenshotCaptureSupport.ps1"
    $screenshotSupport = Get-Content -LiteralPath $screenshotSupportPath -Raw
    foreach ($requiredCaptureHelper in @("public class ScreenshotWin32", "function Capture-ScreenRectangle")) {
        if (-not $screenshotSupport.Contains($requiredCaptureHelper)) {
            throw "ScreenshotCaptureSupport.ps1 is missing required helper '$requiredCaptureHelper'."
        }
    }

    foreach ($scenarioName in @("screenshot_ribbon.ps1", "screenshot_excel.ps1")) {
        $scenario = Get-Content -LiteralPath (Join-Path $ToolRoot $scenarioName) -Raw
        if ($scenario -match 'public class Win32[ce]\b|public class WindowInfo[CE]\b|\$g\s*\.\s*CopyFromScreen|Add-Type\s+-TypeDefinition.*DllImport') {
            throw "$scenarioName contains duplicated Win32 or screen-capture interop."
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
    Assert-SharedToolHelperBehavior -RepoRoot $repoRoot
    Assert-GeneratedDocCheckNewlineSemantics -ToolRoot $resolvedDirectory
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
