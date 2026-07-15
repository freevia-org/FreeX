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

function Assert-ToolSourceCentralization {
    param([Parameter(Mandatory = $true)][string]$ToolRoot)

    $supportPath = Join-Path $ToolRoot "ToolScriptSupport.ps1"
    $support = Get-Content -LiteralPath $supportPath -Raw
    foreach ($requiredHelper in @(
            "function ConvertTo-ToolRepoRelativePath",
            "function Read-ToolJson",
            "function ConvertTo-ToolMarkdownCell",
            "function Test-ToolGeneratedContentMatches")) {
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

$toolsRoot = [System.IO.Path]::GetFullPath((Resolve-RepoPath "tools")).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
$resolvedDirectory = [System.IO.Path]::GetFullPath($resolvedScriptDirectory).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
if ($resolvedDirectory.Equals($toolsRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    Assert-ToolSourceCentralization -ToolRoot $resolvedDirectory
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
