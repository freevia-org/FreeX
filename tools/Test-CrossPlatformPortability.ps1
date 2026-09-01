param()

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$errors = [System.Collections.Generic.List[string]]::new()
$phaseTimer = [System.Diagnostics.Stopwatch]::StartNew()
$lastPhaseElapsed = [TimeSpan]::Zero
. (Join-Path $PSScriptRoot 'ToolScriptSupport.ps1')

function Add-PortabilityError([string]$Message) {
    $errors.Add($Message)
}

function Write-PortabilityPhase([string]$Name) {
    $elapsed = $phaseTimer.Elapsed
    $duration = $elapsed - $lastPhaseElapsed
    Write-Host ("Portability phase '{0}': {1:N2}s" -f $Name, $duration.TotalSeconds)
    $script:lastPhaseElapsed = $elapsed
}

$trackedPaths = @(& git -C $repoRoot ls-files)
if ($LASTEXITCODE -ne 0) {
    throw "Could not enumerate tracked files for the portability audit."
}

$trackedPathSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$trackedPathByCaseFold = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
$trackedPathByFormC = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
$trackedPathByFormD = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
$trackedDirectorySet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
$trackedDirectoryByCaseFold = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
foreach ($trackedPath in $trackedPaths) {
    [void]$trackedPathSet.Add($trackedPath)

    $foldedPath = $trackedPath.ToLowerInvariant()
    if ($trackedPathByCaseFold.ContainsKey($foldedPath)) {
        Add-PortabilityError "Case-insensitive tracked-path collision: $($trackedPathByCaseFold[$foldedPath]); $trackedPath"
    }
    else {
        $trackedPathByCaseFold.Add($foldedPath, $trackedPath)
    }

    $formCPath = $trackedPath.Normalize([Text.NormalizationForm]::FormC).ToLowerInvariant()
    if ($trackedPathByFormC.ContainsKey($formCPath)) {
        Add-PortabilityError "Unicode/case-normalized tracked-path collision: $($trackedPathByFormC[$formCPath]); $trackedPath"
    }
    else {
        $trackedPathByFormC.Add($formCPath, $trackedPath)
    }

    $formDPath = $trackedPath.Normalize([Text.NormalizationForm]::FormD).ToLowerInvariant()
    if ($trackedPathByFormD.ContainsKey($formDPath)) {
        Add-PortabilityError "macOS Unicode/case-normalized tracked-path collision: $($trackedPathByFormD[$formDPath]); $trackedPath"
    }
    else {
        $trackedPathByFormD.Add($formDPath, $trackedPath)
    }

    $slashIndex = $trackedPath.IndexOf('/')
    while ($slashIndex -ge 0) {
        $directory = $trackedPath.Substring(0, $slashIndex)
        if (-not $trackedDirectorySet.Add($directory)) {
            $slashIndex = $trackedPath.IndexOf('/', $slashIndex + 1)
            continue
        }
        $foldedDirectory = $directory.ToLowerInvariant()
        if ($trackedDirectoryByCaseFold.ContainsKey($foldedDirectory) -and
            $trackedDirectoryByCaseFold[$foldedDirectory] -cne $directory) {
            Add-PortabilityError "Case-insensitive tracked-directory collision: '$directory' and '$($trackedDirectoryByCaseFold[$foldedDirectory])'."
        }
        else {
            $trackedDirectoryByCaseFold[$foldedDirectory] = $directory
        }
        $slashIndex = $trackedPath.IndexOf('/', $slashIndex + 1)
    }
}
Write-PortabilityPhase 'tracked path index'

function Test-StaticRepositoryPathCase {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string]$Candidate
    )

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        return
    }

    $normalized = $Candidate.Replace([string][char]92, '/').TrimStart('.')
    $normalized = $normalized.TrimStart('/').TrimEnd('.', ',', ':', ';', ')', ']', '}', '"', "'")
    if ($normalized -notmatch '^(?:\.github|tools|eng|src|shared|freew|freep|tests|docs|release)/' -or
        $normalized -match '[*?`${}%]' -or $normalized -match '\s') {
        return
    }

    $segments = $normalized -split '/'
    $caseMismatch = $null
    $deepestKnownDirectory = 0
    for ($segmentIndex = 1; $segmentIndex -lt $segments.Length; $segmentIndex++) {
        $directory = ($segments[0..($segmentIndex - 1)] -join '/')
        $foldedDirectory = $directory.ToLowerInvariant()
        if ($trackedDirectorySet.Contains($directory)) {
            $deepestKnownDirectory = $segmentIndex
        }
        elseif ($trackedDirectoryByCaseFold.ContainsKey($foldedDirectory)) {
            $deepestKnownDirectory = $segmentIndex
            if ($null -eq $caseMismatch) {
                $caseMismatch = "$SourcePath references directory '$directory' with incorrect case; tracked directory is '$($trackedDirectoryByCaseFold[$foldedDirectory])'."
            }
        }
    }

    $folded = $normalized.ToLowerInvariant()
    if (-not $trackedPathSet.Contains($normalized) -and $trackedPathByCaseFold.ContainsKey($folded)) {
        Add-PortabilityError "$SourcePath references '$normalized' with incorrect case; tracked path is '$($trackedPathByCaseFold[$folded])'."
    }
    elseif ($null -ne $caseMismatch -and $deepestKnownDirectory -ge 2) {
        Add-PortabilityError $caseMismatch
    }
}

$reservedLeafPattern = '^(?i:(?:con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$))'
foreach ($path in $trackedPaths) {
    $leaf = ($path -split '/')[-1]
    if ($leaf -match '[. ]$' -or $leaf -match $reservedLeafPattern -or
        $leaf.IndexOfAny([char[]]'<>:"|?*') -ge 0 -or
        @($leaf.ToCharArray() | Where-Object { [int]$_ -lt 32 }).Count -gt 0) {
        Add-PortabilityError "Windows-incompatible tracked path: $path"
    }
}

$attributes = Get-Content -LiteralPath (Join-Path $repoRoot '.gitattributes') -Raw
foreach ($requiredRule in @(
    '*.sh text eol=lf', '*.ps1 text eol=lf', '*.psm1 text eol=lf',
    '*.yml text eol=lf', '*.yaml text eol=lf', '*.py text eol=lf', '*.mjs text eol=lf',
    '*.csproj text eol=lf', '*.props text eol=lf', '*.targets text eol=lf', '*.slnx text eol=lf',
    '*.json text eol=lf', '*.xml text eol=lf', '*.xaml text eol=lf', '*.resx text eol=lf',
    '*.plist text eol=lf', '*.desktop text eol=lf', '*.conf text eol=lf',
    '*.md text eol=lf', '*.txt text eol=lf', '*.csv text eol=lf', '*.html text eol=lf',
    '*.toml text eol=lf')) {
    if (-not $attributes.Contains($requiredRule)) {
        Add-PortabilityError ".gitattributes is missing required portability rule: $requiredRule"
    }
}

$editorConfigPath = Join-Path $repoRoot '.editorconfig'
if (-not (Test-Path -LiteralPath $editorConfigPath -PathType Leaf)) {
    Add-PortabilityError '.editorconfig is missing.'
}
else {
    $editorConfig = Get-Content -LiteralPath $editorConfigPath -Raw
    foreach ($setting in @('root = true', 'charset = utf-8', 'end_of_line = lf', 'insert_final_newline = true')) {
        if (-not $editorConfig.Contains($setting)) {
            Add-PortabilityError ".editorconfig is missing required portability setting: $setting"
        }
    }
}

$portableTextPaths = @($trackedPaths | Where-Object {
    $_ -match '(?i)\.(?:ps1|psm1|sh|py|mjs|ya?ml)$'
})
foreach ($relativePath in $portableTextPaths) {
    $bytes = [System.IO.File]::ReadAllBytes((Join-Path $repoRoot $relativePath))
    if ($bytes -contains 13) {
        Add-PortabilityError "$relativePath contains CR/CRLF bytes; scripts and workflows must use LF endings."
    }
    if ($bytes.Length -gt 0 -and $bytes[$bytes.Length - 1] -ne 10) {
        Add-PortabilityError "$relativePath must end with a newline."
    }
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        Add-PortabilityError "$relativePath contains a UTF-8 BOM; executable shebangs and cross-platform tools require BOM-free UTF-8."
    }
}
Write-PortabilityPhase 'tracked path and text hygiene'

$shellScripts = @($trackedPaths | Where-Object { $_.EndsWith('.sh', [System.StringComparison]::OrdinalIgnoreCase) })
foreach ($relativePath in $shellScripts) {
    $path = Join-Path $repoRoot $relativePath
    & bash -n $path
    if ($LASTEXITCODE -ne 0) {
        Add-PortabilityError "Bash syntax validation failed: $relativePath"
    }
}

$pythonScripts = @($trackedPaths | Where-Object { $_.EndsWith('.py', [System.StringComparison]::OrdinalIgnoreCase) })
if ($pythonScripts.Count -gt 0) {
    $pythonCommand = Get-Command python3, python -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $pythonCommand) {
        Add-PortabilityError 'Python scripts are tracked, but no python3/python interpreter is available for syntax validation.'
    }
    else {
        foreach ($relativePath in $pythonScripts) {
            & $pythonCommand.Source -c 'import ast, pathlib, sys; ast.parse(pathlib.Path(sys.argv[1]).read_bytes().decode(), filename=sys.argv[1])' (Join-Path $repoRoot $relativePath)
            if ($LASTEXITCODE -ne 0) {
                Add-PortabilityError "Python syntax validation failed: $relativePath"
            }
        }
    }
}

$nodeScripts = @($trackedPaths | Where-Object { $_.EndsWith('.mjs', [System.StringComparison]::OrdinalIgnoreCase) })
if ($nodeScripts.Count -gt 0) {
    $nodeCommand = Get-Command node -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $nodeCommand) {
        Add-PortabilityError 'Node scripts are tracked, but node is unavailable for syntax validation.'
    }
    else {
        foreach ($relativePath in $nodeScripts) {
            & $nodeCommand.Source --check (Join-Path $repoRoot $relativePath)
            if ($LASTEXITCODE -ne 0) {
                Add-PortabilityError "Node syntax validation failed: $relativePath"
            }
        }
    }
}
Write-PortabilityPhase 'shell/python/node syntax'

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
Write-PortabilityPhase 'PowerShell parse'

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
        Test-StaticRepositoryPathCase -SourcePath $relativePath -Candidate ([string]$stringNode.Value)
    }
}

# Shell, Python, Node, and workflow files do not have a PowerShell AST. Scan their
# static repository-looking tokens and validate every known directory/file segment.
$plainScriptPaths = @($trackedPaths | Where-Object { $_ -match '(?i)\.(?:sh|py|mjs|ya?ml)$' })
foreach ($relativePath in $plainScriptPaths) {
    $source = (Get-Content -LiteralPath (Join-Path $repoRoot $relativePath) -Raw).Replace([string][char]92, '/')
    foreach ($match in [regex]::Matches(
            $source,
            '(?<![A-Za-z0-9_.-])(?<path>(?:\.github|tools|eng|src|shared|freew|freep|tests|docs|release)/[A-Za-z0-9_.${}/?*%+-]+)')) {
        Test-StaticRepositoryPathCase -SourcePath $relativePath -Candidate $match.Groups['path'].Value
    }
}
Write-PortabilityPhase 'static repository path case'

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
    if ($source -match '(?m)\$(?:IsWindows|IsLinux|IsMacOS)\b') {
        Add-PortabilityError "$relativePath uses a PowerShell 6+ automatic platform variable; use Test-ToolIsWindows/Test-ToolIsLinux/Test-ToolIsMacOS for Windows PowerShell 5.1 compatibility."
    }
    $ast = $powerShellAsts[$relativePath]

    # PowerShell accepts backslashes as path separators on Windows, which lets repository-relative
    # literals survive local validation and then become literal filename characters on Unix. Keep
    # portable scripts on one representation. Test-ToolScripts intentionally contains synthetic
    # Windows paths to prove the shared helpers normalize foreign input.
    if ($relativePath -ne 'tools/Test-ToolScripts.ps1') {
        $repositoryPathStrings = @($ast.FindAll({
            param($node)
            ($node -is [System.Management.Automation.Language.StringConstantExpressionAst] -or
                $node -is [System.Management.Automation.Language.ExpandableStringExpressionAst]) -and
                [string]$node.Value -match '(?i)(?:^|[\s''"(=:])(?:\.\\)?(?:\.github|tools|eng|src|shared|freew|freep|tests|docs|release)\\'
        }, $true))
        foreach ($stringNode in $repositoryPathStrings) {
            Add-PortabilityError "$relativePath contains a Windows-separated repository path at line $($stringNode.Extent.StartLineNumber); use '/'."
        }
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

    if ($source -match '(?m)\$env:PATH\s*=.*;|\$env:PATH\s*\+=\s*["''];') {
        Add-PortabilityError "$relativePath hardcodes the Windows PATH separator instead of [IO.Path]::PathSeparator."
    }
    if ($source -match '(?m)\bchmod\s+[^\r\n]*\s--(?:\s|["''])') {
        Add-PortabilityError "$relativePath uses GNU-only chmod '--' syntax, which is not accepted by BSD chmod."
    }
    if ($source -match '(?m)\.Trim(?:Start|End)\(\s*["'']\\["'']\s*\)') {
        Add-PortabilityError "$relativePath trims only a Windows directory separator; use both platform separators or a shared helper."
    }
    if ($relativePath -ne 'tools/Test-CrossPlatformPortability.ps1') {
        foreach ($caseFoldMatch in [regex]::Matches(
                $source,
                '(?im)\$(?:[A-Za-z0-9_]*(?:path|root|directory)[A-Za-z0-9_]*)\.(?:ToLowerInvariant|ToUpperInvariant)\(\)')) {
            $lineStart = $source.LastIndexOf("`n", [Math]::Max(0, $caseFoldMatch.Index - 1)) + 1
            $lineEnd = $source.IndexOf("`n", $caseFoldMatch.Index)
            if ($lineEnd -lt 0) { $lineEnd = $source.Length }
            $line = $source.Substring($lineStart, $lineEnd - $lineStart)
            if (-not $line.Contains('Test-ToolIsWindows')) {
                Add-PortabilityError "$relativePath case-folds a physical path; use Get-ToolPathComparison/Get-ToolPathComparer so Unix paths remain case-sensitive."
            }
        }
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
Write-PortabilityPhase 'PowerShell portability rules'

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

$managedSourceFiles = @($trackedPaths | Where-Object { $_ -match '(?i)\.(?:cs|csx|fs|fsx|vb)$' })
$managedPathFixtureExceptions = @(
    # These fixtures deliberately feed Windows-separated project files into cross-platform parsers.
    'tests/FreeX.App.Host.Tests/DotNetProjectReferencesPreflightTests.cs',
    'tests/FreeX.App.Host.Tests/MacOsAppReadinessPreflightTests.cs',
    # These source-contract tests target explicitly Windows-only evidence scripts.
    'freew/FreeW.App.Presentation.Tests/VisualEvidenceRunnerScriptTests.cs',
    'tests/FreeX.App.Host.Tests/ScreenshotHarnessScriptTests.cs'
)
$managedPathSpecs = @('*.cs', '*.csx', '*.fs', '*.fsx', '*.vb')
$managedSourceCandidateSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
foreach ($needle in @('\\', 'Environment.NewLine')) {
    $matches = @(& git -C $repoRoot grep -l -F $needle -- $managedPathSpecs)
    $gitGrepExitCode = $LASTEXITCODE
    if ($gitGrepExitCode -gt 1) {
        throw "git grep failed while selecting managed portability candidates for '$needle' (exit code $gitGrepExitCode)."
    }
    foreach ($match in $matches) {
        [void]$managedSourceCandidateSet.Add($match)
    }
}
$managedSourceCandidates = @($managedSourceCandidateSet | Sort-Object)
$managedRepositoryPathPattern = [regex]::new(
    '(?i)(?:docs|freep|freew|shared|tests|tools)(?:\\\\[A-Za-z0-9_.()$%*? -]+)+',
    [Text.RegularExpressions.RegexOptions]::Compiled)
$managedMsBuildPathLinePattern = [regex]::new(
    '(?im)^(?=[^\r\n]*[A-Za-z0-9_.$)%*]\\\\[A-Za-z0-9_.$(%*?])(?=[^\r\n]*(?:\.(?:csproj|props|targets)|MSBuildThisFileDirectory|%\(RecursiveDir\)|(?:Include|Remove|Update|Link|Project)=))[^\r\n]*',
    [Text.RegularExpressions.RegexOptions]::Compiled)
foreach ($relativePath in $managedSourceCandidates) {
    $source = [IO.File]::ReadAllText((Join-Path $repoRoot $relativePath))
    if ($source -match '\.Split\s*\(\s*Environment\.NewLine\b') {
        Add-PortabilityError "$relativePath splits text on Environment.NewLine; normalize external/file text with ReplaceLineEndings before splitting so LF checkouts work on Windows."
    }
    if ($relativePath -notin $managedPathFixtureExceptions) {
        foreach ($match in $managedRepositoryPathPattern.Matches($source)) {
            $candidate = $match.Value.Replace('\\', '/')
            $separatorCount = ([regex]::Matches($candidate, '/')).Count
            if ($separatorCount -ge 2 -or $candidate -match '(?i)\.(?:cs|csproj|fs|fsproj|vb|vbproj|ps1|psm1|psd1|sh|py|js|json|xml|xaml|axaml|svg|png|ico|icns|md|txt|props|targets|slnx)$') {
                $lineNumber = ([regex]::Matches($source.Substring(0, $match.Index), "`n")).Count + 1
                Add-PortabilityError "$relativePath asserts or embeds a Windows-separated repository path '$candidate' at line $lineNumber; use '/'."
            }
        }
        foreach ($match in $managedMsBuildPathLinePattern.Matches($source)) {
            $lineNumber = ([regex]::Matches($source.Substring(0, $match.Index), "`n")).Count + 1
            Add-PortabilityError "$relativePath asserts or embeds a Windows-separated MSBuild path at line $lineNumber; use '/'."
        }
    }
}
Write-PortabilityPhase 'managed source paths'

$msbuildFiles = @($trackedPaths | Where-Object { $_ -match '(?i)\.(?:csproj|props|targets|slnx)$' })
$msbuildPathAttributeNames = @('Include', 'Exclude', 'Update', 'Remove', 'Link', 'Project', 'Path')
foreach ($relativePath in $msbuildFiles) {
    $fullPath = Join-Path $repoRoot $relativePath
    [xml]$xml = Get-Content -LiteralPath $fullPath -Raw
    foreach ($attribute in @($xml.SelectNodes('//@*'))) {
        if ($attribute.Name -in $msbuildPathAttributeNames -and $attribute.Value.Contains('\')) {
            Add-PortabilityError "$relativePath has a Windows-separated MSBuild $($attribute.Name) path '$($attribute.Value)'; use '/'."
        }
    }
    foreach ($elementName in @('OutputPath', 'IntermediateOutputPath', 'BaseIntermediateOutputPath', 'BaseOutputPath', 'VSTestResultsDirectory')) {
        foreach ($element in @($xml.SelectNodes("//*[local-name()='$elementName']"))) {
            if ($element.InnerText.Contains('\')) {
                Add-PortabilityError "$relativePath has a Windows-separated MSBuild $elementName value '$($element.InnerText)'; use '/'."
            }
        }
    }

    $requiredReferences = @(
        @($xml.SelectNodes("//*[local-name()='ProjectReference']/@Include"))
        @($xml.SelectNodes("//*[local-name()='Import']/@Project"))
        @($xml.SelectNodes("/*[local-name()='Solution']//*[local-name()='Project']/@Path"))
    )
    foreach ($attribute in $requiredReferences) {
        $candidate = [string]$attribute.Value
        if ([string]::IsNullOrWhiteSpace($candidate) -or $candidate -match '[*$?]' -or $candidate.Contains('$(')) {
            continue
        }
        $resolvedReference = Resolve-ToolFullPath -Path $candidate -BasePath (Split-Path -Parent $fullPath)
        if (-not (Test-ToolPathWithinRoot -Path $resolvedReference -RootPath $repoRoot)) {
            Add-PortabilityError "$relativePath has a repository-escaping project/import reference '$candidate'."
            continue
        }
        $repoRelativeReference = ConvertTo-ToolRepoRelativePath -Path $resolvedReference -RepoRoot $repoRoot
        if (-not $trackedPathSet.Contains($repoRelativeReference)) {
            $foldedReference = $repoRelativeReference.ToLowerInvariant()
            if ($trackedPathByCaseFold.ContainsKey($foldedReference)) {
                Add-PortabilityError "$relativePath references '$candidate' with incorrect case; tracked path is '$($trackedPathByCaseFold[$foldedReference])'."
            }
            else {
                Add-PortabilityError "$relativePath references missing tracked project/import path '$candidate'."
            }
        }
    }
}
Write-PortabilityPhase 'MSBuild paths and references'

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

$linuxOnlyShellPrefixes = @(
    'freew/FreeW.App.Avalonia/Packaging/linux/',
    'src/FreeX.App.Avalonia/Packaging/linux/',
    'tools/FreeX.LinuxLiveTest/',
    'tools/LinuxInteractiveDocker/',
    'tools/packaging/linux/'
)
$portableShellScripts = @($shellScripts | Where-Object {
    $candidate = $_
    -not @($linuxOnlyShellPrefixes | Where-Object { $candidate.StartsWith($_, [StringComparison]::Ordinal) }).Count
})
foreach ($relativePath in $portableShellScripts) {
    $source = Get-Content -LiteralPath (Join-Path $repoRoot $relativePath) -Raw
    $gnuOnlyPatterns = [ordered]@{
        '(?m)\breadlink\s+-f\b' = 'GNU readlink -f'
        '(?m)\bsed\s+(?:[^\r\n]*\s)?-i(?:\s|$)' = 'GNU/BSD-incompatible sed -i syntax'
        '(?m)\bsed\s+(?:[^\r\n]*\s)?-r(?:\s|$)' = 'GNU sed -r'
        '(?m)\bgrep\s+(?:[^\r\n]*\s)?-P' = 'GNU grep -P'
        '(?m)\bstat\s+(?:[^\r\n]*\s)?-c(?:\s|$)' = 'GNU stat -c'
        '(?m)\bdate\s+(?:[^\r\n]*\s)?-d(?:\s|$)' = 'GNU date -d'
        '(?m)\bxargs\s+(?:[^\r\n]*\s)?-r(?:\s|$)' = 'GNU xargs -r'
        '(?m)\bsort\s+(?:[^\r\n]*\s)?-V(?:\s|$)' = 'GNU sort -V'
        '(?m)\bfind\s+[^\r\n]*-printf(?:\s|$)' = 'GNU find -printf'
        '(?m)\bbase64\s+[^\r\n]*-w(?:\s|$)' = 'GNU base64 -w'
        '(?m)\bsha256sum\b' = 'Linux sha256sum'
    }
    foreach ($pattern in $gnuOnlyPatterns.GetEnumerator()) {
        if ($source -match $pattern.Key) {
            Add-PortabilityError "$relativePath uses $($pattern.Value), but the script is shared by Linux and macOS."
        }
    }
}
Write-PortabilityPhase 'Git index and shell portability'

$appReleaseWorkflow = Get-Content -LiteralPath (Join-Path $repoRoot '.github/workflows/full-release.yml') -Raw
foreach ($windowsOnlyToken in @('powershell.exe', 'cmd.exe')) {
    if ($appReleaseWorkflow.Contains($windowsOnlyToken)) {
        Add-PortabilityError "Full Signed Release contains an unscoped Windows-only command token: $windowsOnlyToken"
    }
}

if ($errors.Count -gt 0) {
    throw "Cross-platform portability validation failed:`n - $($errors -join "`n - ")"
}

Write-PortabilityPhase 'release workflow guard'

Write-Host "Cross-platform portability checks passed for $($trackedPaths.Count) tracked paths, $($portableTextPaths.Count) LF/BOM-normalized scripts/workflows, $($msbuildFiles.Count) MSBuild/solution files, all $($powerShellScripts.Count) PowerShell scripts ($($portablePowerShellScripts.Count) portable, $($windowsOnlyPowerShellScripts.Count) explicitly Windows-only), all $($shellScripts.Count) executable shell scripts ($($portableShellScripts.Count) Linux/macOS shared), $($pythonScripts.Count) Python scripts, and $($nodeScripts.Count) Node scripts."
