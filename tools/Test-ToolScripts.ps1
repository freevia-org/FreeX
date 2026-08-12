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
            "function Resolve-ToolProviderPath",
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
            "function Get-ToolCommandInventoryMenuTraversalSource",
            "function Test-ToolGeneratedContentMatches",
            "function Invoke-ToolGeneratedProject",
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
        "Test-SolutionProjects.ps1" = @("Resolve-RepoPath", "Normalize-RelativePath", "Get-RelativePath", "Test-IsIgnoredDirectoryName", "Test-IsIgnoredProjectPath", "Get-ProjectFiles")
        "Test-DotNetProjectReferences.ps1" = @("Resolve-RepoPath", "Get-RelativeRepoPath", "Test-IsIgnoredDirectoryName", "Test-IsIgnoredProjectPath", "Get-ProjectFiles")
        "Test-DotNetSdkReadiness.ps1" = @("Resolve-RepoPath", "Get-RelativeRepoPath", "Test-IsIgnoredDirectoryName", "Test-IsIgnoredProjectPath", "Get-ProjectFiles")
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

    $sharedProcessScripts = @(
        "Run-FreePMultiSelectionX11Validation.ps1",
        "Run-FreePPortablePrinterValidation.ps1",
        "Run-FreeWFieldShortcutValidation.ps1",
        "Run-FreeWTablePaginationValidation.ps1"
    )
    foreach ($scriptName in $sharedProcessScripts) {
        $script = Get-Content -LiteralPath (Join-Path $ToolRoot $scriptName) -Raw
        if (-not $script.Contains("ToolScriptSupport.ps1") -or -not $script.Contains("Invoke-ToolProcess")) {
            throw "$scriptName must use Invoke-ToolProcess from ToolScriptSupport.ps1."
        }
    }

    $compatibilityAdapterFound = $false
    foreach ($scriptFile in Get-ChildItem -LiteralPath $ToolRoot -Filter "*.ps1" -File -Recurse) {
        $tokens = $null
        $parseErrors = $null
        $ast = [System.Management.Automation.Language.Parser]::ParseFile($scriptFile.FullName, [ref]$tokens, [ref]$parseErrors)
        $externalDeclarations = $ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.FunctionDefinitionAst] -and
                    $node.Name -eq "Invoke-External"
            }, $true)
        if ($externalDeclarations.Count -eq 0) {
            continue
        }

        if ($scriptFile.Name -eq "Run-FreePMultiSelectionX11Validation.ps1" -and
            $externalDeclarations.Count -eq 1 -and
            $externalDeclarations[0].Extent.Text.Contains("Invoke-ToolProcess")) {
            $compatibilityAdapterFound = $true
            continue
        }

        throw "$($scriptFile.Name) redeclares process invocation owned by ToolScriptSupport.ps1."
    }
    if (-not $compatibilityAdapterFound) {
        throw "Run-FreePMultiSelectionX11Validation.ps1 must retain its source-contract adapter to Invoke-ToolProcess."
    }

    $repoRoot = Split-Path -Parent $ToolRoot
    $orderedToolScripts = [ordered]@{
        "freew-fidelity-corpus\tools\Run-FreeWVisualEvidence.ps1" = [ordered]@{
            ForbiddenDeclarations = @("Resolve-RepositoryPath")
            RequiredPathCalls = @('Resolve-ToolFullPath (Join-Path $scriptDir "..\..")', 'Resolve-ToolRepoPath -Path $OutDir -RepoRoot $repoRoot')
            RequiredProcessCalls = @("Invoke-DotNetStep", "Invoke-PowerShellStep")
        }
        "tools\Run-FreeWWordBaselineEvidence.ps1" = [ordered]@{
            ForbiddenDeclarations = @("Resolve-FullPath")
            RequiredPathCalls = @('Resolve-ToolProviderPath (Join-Path $PSScriptRoot "..")', 'Resolve-ToolProviderPath $RunRoot')
            RequiredProcessCalls = @("Invoke-DotNetRun", "Invoke-DotNetBuild", "Invoke-DotNetRunNoBuild")
        }
        "tools\FreeW.RenderCompare\Export-WordPdfsVisible.ps1" = [ordered]@{
            ForbiddenDeclarations = @("Resolve-FullPath")
            RequiredPathCalls = @('Resolve-ToolProviderPath $CorpusDir', 'Resolve-ToolProviderPath $OutDir')
            RequiredProcessCalls = @()
        }
    }
    foreach ($entry in $orderedToolScripts.GetEnumerator()) {
        $scriptPath = Join-Path $repoRoot $entry.Key
        $script = Get-Content -LiteralPath $scriptPath -Raw
        if (-not $script.Contains("ToolScriptSupport.ps1")) {
            throw "$($entry.Key) must dot-source ToolScriptSupport.ps1."
        }

        foreach ($helperName in $entry.Value.ForbiddenDeclarations) {
            if ($script -match "function\s+$([regex]::Escape($helperName))\b") {
                throw "$($entry.Key) redeclares shared helper '$helperName'."
            }
        }

        foreach ($pathCall in $entry.Value.RequiredPathCalls) {
            if (-not $script.Contains($pathCall)) {
                throw "$($entry.Key) must call shared path helper with '$pathCall'."
            }
        }

        foreach ($processCall in $entry.Value.RequiredProcessCalls) {
            if ($script -notmatch "(?m)^\s*$([regex]::Escape($processCall))(?:\s|`$)") {
                throw "$($entry.Key) must call shared process helper '$processCall'."
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
            "function Assert-ForegroundProcessOwnership",
            "function Clear-ScreenshotTourEvidenceArtifacts",
            "function Set-ScreenshotCaptureWindowWidth",
            "function Write-RibbonScreenshotEvidenceManifest",
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

        if ($scenario -match 'function\s+(Get-WindowTitle|Get-ForegroundWindowInfo|Assert-ForegroundWindowOwnership|Assert-ForegroundProcessOwnership|Set-CaptureWindowWidth|Write-ScreenshotEvidenceManifest|Clear-(?:AutoFilterFlyout|NumberFormatDropdown|HomeBordersDropdown|WorksheetContextMenu|OpenWorkbookDialog|SaveAsWorkbookDialog)EvidenceArtifacts)\b') {
            throw "$scenarioName redeclares a helper owned by ScreenshotCaptureSupport.ps1."
        }

        foreach ($requiredCall in @("Clear-ScreenshotTourEvidenceArtifacts", "Set-ScreenshotCaptureWindowWidth", "Write-RibbonScreenshotEvidenceManifest")) {
            if (-not $scenario.Contains($requiredCall)) {
                throw "$scenarioName must use shared screenshot-tour helper '$requiredCall'."
            }
        }
    }

    $visualEvidenceSupportPath = Join-Path $ToolRoot "VisualEvidenceScriptSupport.ps1"
    $visualEvidenceSupport = Get-Content -LiteralPath $visualEvidenceSupportPath -Raw
    foreach ($requiredHelper in @(
            "function Resolve-VisualEvidenceOutputDirectory",
            "function Invoke-VisualEvidenceProcess",
            "function Wait-VisualEvidenceFile",
            "function Read-VisualEvidenceJson",
            "function Add-VisualEvidenceResultReferences",
            "function Get-VisualEvidenceFileSha256",
            "function Get-VisualEvidenceNormalizedTextSha256",
            "function Get-VisualEvidenceArtifactInventory")) {
        if (-not $visualEvidenceSupport.Contains($requiredHelper)) {
            throw "VisualEvidenceScriptSupport.ps1 is missing required helper '$requiredHelper'."
        }
    }

    $visualEvidenceRunnerNames = @(
        "Run-FamilyLinuxInteractionValidation.ps1",
        "Run-FreePAccessibilityValidation.ps1",
        "Run-FreePClipboardShortcutValidation.ps1",
        "Run-FreePFileSlideshowShortcutValidation.ps1",
        "Run-FreePNativePickerX11Validation.ps1",
        "Run-FreePPhysicalLinuxValidation.ps1",
        "Run-FreePRichTextShortcutValidation.ps1",
        "Run-FreePRotatedShapeTextEditValidation.ps1",
        "Run-FreePSmartArtAuthoringValidation.ps1",
        "Run-FreePTransformedTableCellEditValidation.ps1",
        "Run-FreeWForegroundPrintValidation.ps1"
    )
    foreach ($runnerName in $visualEvidenceRunnerNames) {
        $runner = Get-Content -LiteralPath (Join-Path $ToolRoot $runnerName) -Raw
        if (-not $runner.Contains("VisualEvidenceScriptSupport.ps1") -or
            -not $runner.Contains("Invoke-VisualEvidenceProcess") -or
            $runner -match 'function\s+Invoke-External\b') {
            throw "$runnerName must use shared visual-evidence process orchestration."
        }
    }

    foreach ($runnerName in @(
            "Run-FreePClipboardShortcutValidation.ps1",
            "Run-FreePFileSlideshowShortcutValidation.ps1",
            "Run-FreePNativePickerX11Validation.ps1",
            "Run-FreePRichTextShortcutValidation.ps1")) {
        $runner = Get-Content -LiteralPath (Join-Path $ToolRoot $runnerName) -Raw
        if (-not $runner.Contains("Add-VisualEvidenceResultReferences") -or
            $runner -match 'function\s+Add-ResultEvidence\b') {
            throw "$runnerName must use shared evidence-reference merging."
        }
    }

    foreach ($generatorName in @(
            "Generate-FreePDialogPaneVisualEvidenceManifest.ps1",
            "Generate-FreePWholeWindowVisualEvidenceManifest.ps1")) {
        $generator = Get-Content -LiteralPath (Join-Path $ToolRoot $generatorName) -Raw
        foreach ($requiredCall in @("Get-VisualEvidenceFileSha256", "Get-VisualEvidenceArtifactInventory")) {
            if (-not $generator.Contains($requiredCall)) {
                throw "$generatorName must use shared visual-evidence helper '$requiredCall'."
            }
        }
        if ($generator -match 'function\s+(Get-EvidenceRelativePath|Get-RelativePath|Get-NormalizedTextSha256)\b' -or
            $generator.Contains("Get-FileHash")) {
            throw "$generatorName redeclares visual-evidence hashing or inventory logic."
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

    $externalToolSupportProjects = @(
        "..\freep\TestSupport\VisualEvidence\FreeP.VisualEvidence.csproj",
        "..\freep\TestSupport\VisualEvidence.Avalonia\FreeP.VisualEvidence.Avalonia.csproj",
        "..\freep\TestSupport\VisualEvidence.Wpf\FreeP.VisualEvidence.Wpf.csproj",
        "..\freew\tests\FreeW.VisualEvidence.TestSupport\FreeW.VisualEvidence.TestSupport.csproj",
        "..\freew\tools\FreeW.VisualEvidenceSummary\FreeW.VisualEvidenceSummary.csproj"
    )
    foreach ($relativeProjectPath in $externalToolSupportProjects) {
        $projectPath = Join-Path $ToolRoot $relativeProjectPath
        $projectText = Get-Content -LiteralPath $projectPath -Raw
        if (-not $projectText.Contains('Import Project="..\..\..\tools\ToolProjects.props"')) {
            throw "$projectPath does not import tools/ToolProjects.props."
        }

        if ($projectText -match '<Nullable>\s*enable\s*</Nullable>|<ImplicitUsings>\s*enable\s*</ImplicitUsings>') {
            throw "$projectPath redeclares metadata centralized in tools/ToolProjects.props."
        }
    }

    Write-Host "Validated shared tooling source guards."
}

function Assert-CommandInventoryMenuTraversalCentralization {
    param([Parameter(Mandatory = $true)][string]$ToolRoot)

    $support = Get-Content -LiteralPath (Join-Path $ToolRoot "ToolScriptSupport.ps1") -Raw
    $helper = Get-ToolCommandInventoryMenuTraversalSource
    foreach ($requiredSource in @(
            "private static IEnumerable<(string CommandId, CommandLocation Location)> MenuLocations",
            "foreach (var item in MenuItems(menu.Items))",
            "private static IEnumerable<RibbonMenuItem> MenuItems",
            "foreach (var child in MenuItems(item.Children))",
            "yield return child")) {
        if (-not $helper.Contains($requiredSource)) {
            throw "Shared command inventory menu helper is missing '$requiredSource'."
        }
    }

    foreach ($generatorName in @("Generate-FreePCommandParityInventory.ps1", "Generate-FreeWCommandInventory.ps1")) {
        $generator = Get-Content -LiteralPath (Join-Path $ToolRoot $generatorName) -Raw
        if (([regex]::Matches($generator, "Get-ToolCommandInventoryMenuTraversalSource")).Count -ne 1) {
            throw "$generatorName must consume Get-ToolCommandInventoryMenuTraversalSource exactly once."
        }

        if ($generator.Contains("private static IEnumerable<(string CommandId, CommandLocation Location)> MenuLocations") -or
            $generator.Contains("private static IEnumerable<RibbonMenuItem> MenuItems")) {
            throw "$generatorName still contains a private copy of the shared menu traversal helper."
        }
    }

    if ($helper.IndexOf("yield return item", [System.StringComparison]::Ordinal) -ge
        $helper.IndexOf("foreach (var child in MenuItems(item.Children))", [System.StringComparison]::Ordinal)) {
        throw "Shared command inventory menu helper must yield a parent before recursively yielding nested children."
    }

    Write-Host "Validated command inventory menu traversal centralization and nested-item behavior."
}

function Assert-CommandInventoryGeneratedProjectCentralization {
    param([Parameter(Mandatory = $true)][string]$ToolRoot)

    $support = Get-Content -LiteralPath (Join-Path $ToolRoot "ToolScriptSupport.ps1") -Raw
    foreach ($requiredSource in @(
            "function Invoke-ToolGeneratedProject",
            '<Project Sdk="Microsoft.NET.Sdk">',
            '<ProjectReference Include="$($Options.Reference)" />',
            "Test-ToolGeneratedFileContentMatches",
            'Copy-Item -LiteralPath $generatedFile.TempPath')) {
        if (-not $support.Contains($requiredSource)) {
            throw "Shared generated-project helper is missing '$requiredSource'."
        }
    }

    foreach ($generatorName in @("Generate-FreePCommandParityInventory.ps1", "Generate-FreeWCommandInventory.ps1")) {
        $generator = Get-Content -LiteralPath (Join-Path $ToolRoot $generatorName) -Raw
        if (([regex]::Matches($generator, "Invoke-ToolGeneratedProject")).Count -ne 1) {
            throw "$generatorName must consume Invoke-ToolGeneratedProject exactly once."
        }

        foreach ($forbiddenSource in @(
                "& dotnet run",
                'New-Item -ItemType Directory -Path $tempRoot',
                "Test-ToolGeneratedFileContentMatches",
                'Copy-Item -LiteralPath $temp')) {
            if ($generator.Contains($forbiddenSource)) {
                throw "$generatorName still owns shared generated-project orchestration '$forbiddenSource'."
            }
        }

        foreach ($requiredCall in @("Outputs = [ordered]@", "Arguments = {", 'Check = $Check')) {
            if (-not $generator.Contains($requiredCall)) {
                throw "$generatorName must pass '$requiredCall' to Invoke-ToolGeneratedProject."
            }
        }
    }

    Write-Host "Validated command inventory generated-project orchestration centralization."
}

function Assert-GeneratedProjectOrchestrationBehavior {
    param([Parameter(Mandatory = $true)][string]$ToolRoot)

    . (Join-Path $ToolRoot "ToolScriptSupport.ps1")
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("freex-generated-project-helper-" + [guid]::NewGuid().ToString("N"))
    $shimRoot = Join-Path $tempRoot "shim"
    $destinationRoot = Join-Path $tempRoot "destination"
    New-Item -ItemType Directory -Force -Path $shimRoot, $destinationRoot | Out-Null

    $shimScript = Join-Path $shimRoot "synthetic-dotnet.ps1"
    $shimPath = Join-Path $shimRoot "synthetic-dotnet.cmd"
    $capturePath = Join-Path $tempRoot "captured.json"
    $projectCapturePath = Join-Path $tempRoot "project-path.txt"
    $previousExitCode = $env:FREEX_TOOL_GENERATOR_EXIT_CODE
    $previousCapturePath = $env:FREEX_TOOL_GENERATOR_CAPTURE
    $previousProjectCapturePath = $env:FREEX_TOOL_GENERATOR_PROJECT

    try {
        @'
$arguments = @($args)
$isBuild = $arguments.Count -gt 1 -and $arguments[0] -ceq "build"
$projectPath = if ($isBuild) {
    $arguments[1]
}
else {
    $projectIndex = [Array]::IndexOf([object[]]$arguments, "--project")
    $arguments[$projectIndex + 1]
}
[IO.File]::WriteAllText($env:FREEX_TOOL_GENERATOR_PROJECT, $projectPath)
if ($isBuild) {
    exit 0
}
[ordered]@{ Arguments = $arguments } | ConvertTo-Json -Compress | Set-Content -LiteralPath $env:FREEX_TOOL_GENERATOR_CAPTURE
$separatorIndex = [Array]::IndexOf([object[]]$arguments, "--")
if ($env:FREEX_TOOL_GENERATOR_EXIT_CODE -ne "0") {
    exit [int]$env:FREEX_TOOL_GENERATOR_EXIT_CODE
}
$outputPaths = @($arguments[($separatorIndex + 1)..($arguments.Count - 1)])
[IO.File]::WriteAllText($outputPaths[0], "synthetic-json")
[IO.File]::WriteAllText($outputPaths[1], "synthetic-markdown")
exit 0
'@ | Set-Content -LiteralPath $shimScript -Encoding UTF8
        @"
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$shimScript" %*
exit /b %ERRORLEVEL%
"@ | Set-Content -LiteralPath $shimPath -Encoding ASCII

        $env:FREEX_TOOL_GENERATOR_EXIT_CODE = "0"
        $env:FREEX_TOOL_GENERATOR_CAPTURE = $capturePath
        $env:FREEX_TOOL_GENERATOR_PROJECT = $projectCapturePath
        $jsonDestination = Join-Path $destinationRoot "nested\inventory.json"
        $markdownDestination = Join-Path $destinationRoot "nested\inventory.md"
        $invokeArguments = @{
            Prefix = "freex-generated-project-probe"
            Name = "Synthetic.Generator"
            Reference = "C:\repo\Definitions.csproj"
            Source = "class Program { static void Main() {} }"
            Outputs = [ordered]@{ $jsonDestination = "Synthetic JSON"; $markdownDestination = "Synthetic Markdown" }
            Arguments = {
            param($outputPaths)
            @($outputPaths[0].TempPath, $outputPaths[1].TempPath)
            }
            Script = "synthetic-generator.ps1"
            Failure = "Synthetic generator failed."
            Check = $false
            CheckMessage = "Synthetic generated files are up to date."
            WriteMessage = "Wrote synthetic generated files."
            DotNetPath = $shimPath
        }

        Invoke-ToolGeneratedProject $invokeArguments
        if ((Get-Content -LiteralPath $jsonDestination -Raw) -cne "synthetic-json" -or
            (Get-Content -LiteralPath $markdownDestination -Raw) -cne "synthetic-markdown") {
            throw "Invoke-ToolGeneratedProject did not copy generated output bytes to nested destinations."
        }

        $capture = Get-Content -LiteralPath $capturePath -Raw | ConvertFrom-Json
        if ($capture.Arguments[0] -cne "run" -or $capture.Arguments[1] -cne "--no-build" -or
            $capture.Arguments[2] -cne "--project" -or $capture.Arguments[4] -cne "--configuration" -or
            $capture.Arguments[5] -cne "Release" -or $capture.Arguments[6] -cne "--" -or
            $capture.Arguments.Count -ne 9) {
            throw "Invoke-ToolGeneratedProject did not preserve dotnet run argument ordering."
        }

        $projectPath = Get-Content -LiteralPath $projectCapturePath -Raw
        if (Test-Path -LiteralPath $projectPath) {
            throw "Invoke-ToolGeneratedProject did not clean up its temporary project."
        }

        $invokeArguments.Check = $true
        Invoke-ToolGeneratedProject $invokeArguments

        [IO.File]::WriteAllText($jsonDestination, "mismatch")
        $mismatchMessage = $null
        try {
            Invoke-ToolGeneratedProject $invokeArguments
        }
        catch {
            $mismatchMessage = $_.Exception.Message
        }
        if ($mismatchMessage -ne "Synthetic JSON is out of date. Run synthetic-generator.ps1 to refresh it.") {
            throw "Invoke-ToolGeneratedProject did not preserve check mismatch behavior: '$mismatchMessage'."
        }

        $invokeArguments.Check = $false
        $env:FREEX_TOOL_GENERATOR_EXIT_CODE = "23"
        $failureMessage = $null
        try {
            Invoke-ToolGeneratedProject $invokeArguments
        }
        catch {
            $failureMessage = $_.Exception.Message
        }
        if ($failureMessage -ne "Synthetic generator failed.") {
            throw "Invoke-ToolGeneratedProject did not preserve generator failure behavior: '$failureMessage'."
        }

        $failedProjectPath = Get-Content -LiteralPath $projectCapturePath -Raw
        if (Test-Path -LiteralPath $failedProjectPath) {
            throw "Invoke-ToolGeneratedProject did not clean up after a failed generator."
        }
    }
    finally {
        if ($null -eq $previousExitCode) { Remove-Item Env:FREEX_TOOL_GENERATOR_EXIT_CODE -ErrorAction SilentlyContinue } else { $env:FREEX_TOOL_GENERATOR_EXIT_CODE = $previousExitCode }
        if ($null -eq $previousCapturePath) { Remove-Item Env:FREEX_TOOL_GENERATOR_CAPTURE -ErrorAction SilentlyContinue } else { $env:FREEX_TOOL_GENERATOR_CAPTURE = $previousCapturePath }
        if ($null -eq $previousProjectCapturePath) { Remove-Item Env:FREEX_TOOL_GENERATOR_PROJECT -ErrorAction SilentlyContinue } else { $env:FREEX_TOOL_GENERATOR_PROJECT = $previousProjectCapturePath }
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Validated generated-project copy, check, failure, argument, and cleanup behavior."
}

function Assert-CommandInventoryMenuTraversalBehavior {
    param([Parameter(Mandatory = $true)][string]$ToolRoot)

    . (Join-Path $ToolRoot "ToolScriptSupport.ps1")
    $helper = Get-ToolCommandInventoryMenuTraversalSource
    $probeSource = @"
using System;
using System.Collections.Generic;
using System.Linq;

public static class CommandInventoryTraversalProbe {
    public static void Main() { Run(); }

    public static void Run() {
        var tab = new RibbonTab("tab.home", "Home");
        var group = new RibbonGroup("group.editing", "Editing");

        Assert(MenuLocations(new RibbonSplitButton(null), tab, group, "Synthetic").ToArray().Length == 0,
            "null split-button menus should emit no locations");
        Assert(MenuLocations(new RibbonDropdown(null), tab, group, "Synthetic").ToArray().Length == 0,
            "null dropdown menus should emit no locations");

        var menu = new RibbonMenu(
            new RibbonMenuItem("Parent", "command.parent",
                new RibbonMenuItem("First child", "command.child.first"),
                new RibbonMenuItem("Separator", null,
                    new RibbonMenuItem("After separator", "command.after.separator")),
                new RibbonMenuItem("Second child", "command.child.second",
                    new RibbonMenuItem("Grandchild", "command.grandchild"))),
            new RibbonMenuItem("Sibling", "command.sibling"));
        var actual = MenuLocations(new RibbonDropdown(menu), tab, group, "Synthetic").ToArray();
        var expected = new[] {
            ("command.parent", "Parent"),
            ("command.child.first", "First child"),
            ("command.after.separator", "After separator"),
            ("command.child.second", "Second child"),
            ("command.grandchild", "Grandchild"),
            ("command.sibling", "Sibling"),
        };

        Assert(actual.Length == expected.Length, "nested menu traversal emitted an unexpected item count");
        for (var index = 0; index < expected.Length; index++) {
            Assert(actual[index].CommandId == expected[index].Item1,
                "nested menu traversal emitted an unexpected command order");
            Assert(actual[index].Location.Label == expected[index].Item2,
                "nested menu traversal emitted an unexpected label");
            AssertLocation(actual[index].Location, "Synthetic", "tab.home", "Home", "group.editing", "Editing");
        }
    }

    private static void AssertLocation(
        CommandLocation location,
        string profile,
        string tabId,
        string tab,
        string groupId,
        string group) {
        Assert(location.Profile == profile, "location profile was not preserved");
        Assert(location.TabId == tabId, "location tab path was not preserved");
        Assert(location.Tab == tab, "location tab header was not preserved");
        Assert(location.GroupId == groupId, "location group path was not preserved");
        Assert(location.Group == group, "location group header was not preserved");
        Assert(location.ControlType == "RibbonMenuItem", "location control type was not preserved");
        Assert(location.Layout == "Menu", "location layout was not preserved");
    }

    private static void Assert(bool condition, string message) {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    public class RibbonControl { }

    public sealed class RibbonSplitButton : RibbonControl {
        public RibbonSplitButton(RibbonMenu? menu) { Menu = menu; }
        public RibbonMenu? Menu { get; }
    }

    public sealed class RibbonDropdown : RibbonControl {
        public RibbonDropdown(RibbonMenu? menu) { Menu = menu; }
        public RibbonMenu? Menu { get; }
    }

    public sealed class RibbonMenu {
        public RibbonMenu(params RibbonMenuItem[] items) { Items = items; }
        public IReadOnlyList<RibbonMenuItem> Items { get; }
    }

    public sealed class RibbonMenuItem {
        public RibbonMenuItem(string header, string? commandId, params RibbonMenuItem[] children) {
            Header = header;
            CommandId = commandId is null ? null : new CommandId(commandId);
            Children = children ?? Array.Empty<RibbonMenuItem>();
        }

        public string Header { get; }
        public CommandId? CommandId { get; }
        public IReadOnlyList<RibbonMenuItem> Children { get; }
    }

    public readonly struct CommandId {
        public CommandId(string value) { Value = value; }
        public string Value { get; }
    }

    public sealed class RibbonTab {
        public RibbonTab(string id, string header) { Id = id; Header = header; }
        public string Id { get; }
        public string Header { get; }
    }

    public sealed class RibbonGroup {
        public RibbonGroup(string id, string header) { Id = id; Header = header; }
        public string Id { get; }
        public string Header { get; }
    }

    public sealed class CommandLocation {
        public CommandLocation(
            string Profile,
            string TabId,
            string Tab,
            string GroupId,
            string Group,
            string Label,
            string ControlType,
            string Layout) {
            this.Profile = Profile;
            this.TabId = TabId;
            this.Tab = Tab;
            this.GroupId = GroupId;
            this.Group = Group;
            this.Label = Label;
            this.ControlType = ControlType;
            this.Layout = Layout;
        }

        public string Profile { get; }
        public string TabId { get; }
        public string Tab { get; }
        public string GroupId { get; }
        public string Group { get; }
        public string Label { get; }
        public string ControlType { get; }
        public string Layout { get; }
    }

$helper
}
"@

    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("freex-command-inventory-traversal-" + [guid]::NewGuid().ToString("N"))
    New-Item -ItemType Directory -Force -Path $tempRoot | Out-Null
    $projectPath = Join-Path $tempRoot "CommandInventoryTraversalProbe.csproj"
    $programPath = Join-Path $tempRoot "Program.cs"
    [IO.File]::WriteAllText($projectPath, @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
"@)
    [IO.File]::WriteAllText($programPath, $probeSource)
    try {
        Invoke-DotNetRun -ProjectPath $projectPath -Configuration "Release" -WorkingDirectory $tempRoot
    }
    finally {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
    }

    Write-Host "Validated executable command inventory menu traversal behavior."
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

function Assert-ToolProviderPathBehavior {
    param([Parameter(Mandatory = $true)][string]$ToolRoot)

    . (Join-Path $ToolRoot "ToolScriptSupport.ps1")
    $probeRelativePath = "freex-provider-path-probe\child"
    $expectedTildePath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath("~\$probeRelativePath")
    $resolvedTildePath = Resolve-ToolProviderPath "~\$probeRelativePath"
    if (-not $resolvedTildePath.Equals($expectedTildePath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Resolve-ToolProviderPath did not preserve tilde expansion: '$resolvedTildePath' versus '$expectedTildePath'."
    }

    $expectedProviderPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath("FileSystem::~\$probeRelativePath")
    $resolvedProviderPath = Resolve-ToolProviderPath "FileSystem::~\$probeRelativePath"
    if (-not $resolvedProviderPath.Equals($expectedProviderPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Resolve-ToolProviderPath did not preserve provider-qualified path resolution: '$resolvedProviderPath' versus '$expectedProviderPath'."
    }

    Write-Host "Validated provider-path and tilde resolution behavior."
}

function Assert-ToolProcessBehavior {
    param([Parameter(Mandatory = $true)][string]$ToolRoot)

    . (Join-Path $ToolRoot "ToolScriptSupport.ps1")
    $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("freex-tool-process-behavior-" + [guid]::NewGuid().ToString("N"))
    $cwdRoot = Join-Path $tempRoot "cwd-root"
    $syntheticRepoRoot = Join-Path $tempRoot "repo-root"
    $workingRoot = Join-Path $tempRoot "working-root"
    $shimRoot = Join-Path $tempRoot "shims"
    New-Item -ItemType Directory -Force -Path $cwdRoot, $syntheticRepoRoot, $workingRoot, $shimRoot | Out-Null
    $originalLocation = Get-Location
    $previousPath = $env:Path
    $previousProcessOutput = $env:FREEX_TOOL_PROCESS_OUTPUT
    $previousProcessExitCode = $env:FREEX_TOOL_PROCESS_EXIT_CODE
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

        $teeProbeOutputPath = Join-Path $tempRoot "tee-probe-output.json"
        $teeStreamPath = Join-Path $tempRoot "tee-stream.txt"
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = "Continue"
            $teeOutput = @(Invoke-ToolProcess `
                -FilePath $powerShell `
                -Arguments @(
                    "-NoProfile",
                    "-ExecutionPolicy", "Bypass",
                    "-File", $probePath,
                    "-OutputPath", $teeProbeOutputPath,
                    "first tee value",
                    "second tee value"
                ) `
                -WorkingDirectory $workingRoot `
                -OutputPath $teeStreamPath)
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }
        $teeStream = Get-Content -LiteralPath $teeStreamPath -Raw
        if ($teeOutput.Count -lt 2 -or $teeStream -notmatch 'synthetic stdout' -or $teeStream -notmatch 'synthetic stderr') {
            throw "Invoke-ToolProcess did not preserve tee-to-file stdout/stderr behavior."
        }

        $hostOnlyOutput = @(Invoke-ToolProcess `
            -FilePath $powerShell `
            -Arguments @("-NoProfile", "-Command", "Write-Output 'synthetic host-only stdout'") `
            -WorkingDirectory $workingRoot `
            -OutputToHost)
        if ($hostOnlyOutput.Count -ne 0) {
            throw "Invoke-ToolProcess did not preserve host-only output behavior."
        }

        $capturePath = Join-Path $shimRoot "capture-process.ps1"
        $wrapperOutputPath = Join-Path $tempRoot "wrapper-output.json"
        @'
$outputPath = $env:FREEX_TOOL_PROCESS_OUTPUT
[ordered]@{
    WorkingDirectory = (Get-Location).Path
    Arguments = @($args)
} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $outputPath
exit ([int]$env:FREEX_TOOL_PROCESS_EXIT_CODE)
'@ | Set-Content -LiteralPath $capturePath -Encoding UTF8

        $syntheticShimPath = Join-Path $shimRoot "synthetic-tool.cmd"
        @"
@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "$capturePath" %*
exit /b %ERRORLEVEL%
"@ | Set-Content -LiteralPath $syntheticShimPath -Encoding ASCII
        $targetScriptPath = Join-Path $tempRoot "synthetic-target.ps1"
        New-Item -ItemType File -Path $targetScriptPath | Out-Null
        $env:Path = "$shimRoot;$previousPath"
        $env:FREEX_TOOL_PROCESS_OUTPUT = $wrapperOutputPath
        $env:FREEX_TOOL_PROCESS_EXIT_CODE = "0"

        $assertWrapperCapture = {
            param([string]$Label, [string[]]$ExpectedArguments)
            $capture = Get-Content -LiteralPath $wrapperOutputPath -Raw | ConvertFrom-Json
            $actualArguments = @($capture.Arguments | ForEach-Object { [string]$_ })
            $expectedSerialized = $ExpectedArguments -join "`0"
            $actualSerialized = $actualArguments -join "`0"
            if (-not [System.IO.Path]::GetFullPath($capture.WorkingDirectory).Equals([System.IO.Path]::GetFullPath($workingRoot), [System.StringComparison]::OrdinalIgnoreCase) -or
                $actualSerialized -cne $expectedSerialized) {
                throw "$Label did not preserve wrapper argument ordering or working directory: $($capture | ConvertTo-Json -Compress)."
            }
        }

        Invoke-DotNetRun "project.csproj" @("--sample", "value with spaces") "Debug" $workingRoot $syntheticShimPath
        & $assertWrapperCapture "Invoke-DotNetRun" @("run", "--project", "project.csproj", "--configuration", "Debug", "--", "--sample", "value with spaces")

        Invoke-DotNetBuild "project.csproj" "Debug" $workingRoot $syntheticShimPath
        & $assertWrapperCapture "Invoke-DotNetBuild" @("build", "project.csproj", "--configuration", "Debug")

        Invoke-DotNetRunNoBuild "project.csproj" @("--sample", "value with spaces") "Debug" $workingRoot $syntheticShimPath
        & $assertWrapperCapture "Invoke-DotNetRunNoBuild" @("run", "--no-restore", "--no-build", "--project", "project.csproj", "--configuration", "Debug", "--", "--sample", "value with spaces")

        Invoke-DotNetStep "Synthetic dotnet step" @("run", "--sample", "value with spaces") $workingRoot $syntheticShimPath
        & $assertWrapperCapture "Invoke-DotNetStep" @("run", "--sample", "value with spaces")

        Invoke-PowerShellStep "Synthetic PowerShell step" $targetScriptPath @("--sample", "value with spaces") $workingRoot $syntheticShimPath
        & $assertWrapperCapture "Invoke-PowerShellStep" @("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $targetScriptPath, "--sample", "value with spaces")

        $env:FREEX_TOOL_PROCESS_EXIT_CODE = "23"
        $dotNetFailure = $null
        try {
            Invoke-DotNetRun "project.csproj" @() "Debug" $workingRoot $syntheticShimPath
        }
        catch {
            $dotNetFailure = $_.Exception.Message
        }
        if ($dotNetFailure -ne "dotnet run failed for project.csproj with exit code 23") {
            throw "Invoke-DotNetRun did not preserve synthetic nonzero failure behavior: '$dotNetFailure'."
        }

        $env:FREEX_TOOL_PROCESS_EXIT_CODE = "27"
        $dotNetStepFailure = $null
        try {
            Invoke-DotNetStep "Synthetic dotnet step failure" @("test", "project.csproj") $workingRoot $syntheticShimPath
        }
        catch {
            $dotNetStepFailure = $_.Exception.Message
        }
        if ($dotNetStepFailure -ne "Synthetic dotnet step failure with exit code 27") {
            throw "Invoke-DotNetStep did not preserve synthetic nonzero failure behavior: '$dotNetStepFailure'."
        }

        $env:FREEX_TOOL_PROCESS_EXIT_CODE = "29"
        $powerShellFailure = $null
        try {
            Invoke-PowerShellStep "Synthetic PowerShell failure" $targetScriptPath @() $workingRoot $syntheticShimPath
        }
        catch {
            $powerShellFailure = $_.Exception.Message
        }
        if ($powerShellFailure -ne "Synthetic PowerShell failure with exit code 29") {
            throw "Invoke-PowerShellStep did not preserve synthetic nonzero failure behavior: '$powerShellFailure'."
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

        $defaultNonzeroMessage = $null
        try {
            Invoke-ToolProcess `
                -FilePath $powerShell `
                -Arguments @("-NoProfile", "-Command", "exit 19")
        }
        catch {
            $defaultNonzeroMessage = $_.Exception.Message
        }

        if ($defaultNonzeroMessage -ne "$powerShell exited with code 19." -or $LASTEXITCODE -ne 19) {
            throw "Invoke-ToolProcess did not preserve default nonzero exit propagation: message='$defaultNonzeroMessage', exit=$LASTEXITCODE."
        }
    }
    finally {
        if ($null -eq $previousPath) {
            Remove-Item Env:Path -ErrorAction SilentlyContinue
        }
        else {
            $env:Path = $previousPath
        }
        if ($null -eq $previousProcessOutput) {
            Remove-Item Env:FREEX_TOOL_PROCESS_OUTPUT -ErrorAction SilentlyContinue
        }
        else {
            $env:FREEX_TOOL_PROCESS_OUTPUT = $previousProcessOutput
        }
        if ($null -eq $previousProcessExitCode) {
            Remove-Item Env:FREEX_TOOL_PROCESS_EXIT_CODE -ErrorAction SilentlyContinue
        }
        else {
            $env:FREEX_TOOL_PROCESS_EXIT_CODE = $previousProcessExitCode
        }
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
    Assert-CommandInventoryMenuTraversalCentralization -ToolRoot $resolvedDirectory
    Assert-CommandInventoryGeneratedProjectCentralization -ToolRoot $resolvedDirectory
    Assert-CommandInventoryMenuTraversalBehavior -ToolRoot $resolvedDirectory
    Assert-GeneratedProjectOrchestrationBehavior -ToolRoot $resolvedDirectory
    Assert-ScreenshotCaptureSupportBehavior -ToolRoot $resolvedDirectory
    Assert-SharedToolHelperBehavior -RepoRoot $repoRoot
    Assert-ToolProviderPathBehavior -ToolRoot $resolvedDirectory
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
