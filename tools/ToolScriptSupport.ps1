function Test-ToolPathRooted {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $true
    }

    return $Path -match '^(?:[A-Za-z]:[\\/]|[\\/]{2})'
}

function Test-ToolIsWindows {
    return [System.IO.Path]::DirectorySeparatorChar -eq [char]92
}

function Test-ToolIsLinux {
    return [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::Linux)
}

function Test-ToolIsMacOS {
    return [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform(
        [System.Runtime.InteropServices.OSPlatform]::OSX)
}

function Get-ToolPathComparison {
    if (Test-ToolIsWindows) {
        return [System.StringComparison]::OrdinalIgnoreCase
    }

    return [System.StringComparison]::Ordinal
}

function Get-ToolPathComparer {
    if (Test-ToolIsWindows) {
        return [System.StringComparer]::OrdinalIgnoreCase
    }

    return [System.StringComparer]::Ordinal
}

function Test-ToolPathEquals {
    param(
        [Parameter(Mandatory = $true)][string]$Left,
        [Parameter(Mandatory = $true)][string]$Right,
        [switch]$ResolveExisting
    )

    $leftPath = if ($ResolveExisting) { Resolve-ToolExistingPath -Path $Left } else { Resolve-ToolFullPath -Path $Left }
    $rightPath = if ($ResolveExisting) { Resolve-ToolExistingPath -Path $Right } else { Resolve-ToolFullPath -Path $Right }
    return $leftPath.Equals($rightPath, (Get-ToolPathComparison))
}

function Test-ToolPathWithinRoot {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RootPath,
        [switch]$AllowRoot,
        [switch]$ResolveExisting
    )

    $fullRoot = if ($ResolveExisting) { Resolve-ToolExistingPath -Path $RootPath } else { Resolve-ToolFullPath -Path $RootPath }
    $fullPath = if ($ResolveExisting) { Resolve-ToolExistingPath -Path $Path } else { Resolve-ToolFullPath -Path $Path -BasePath $fullRoot }
    $trimmedRoot = $fullRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $comparison = Get-ToolPathComparison

    if ($AllowRoot -and $fullPath.Equals($trimmedRoot, $comparison)) {
        return $true
    }

    $rootPrefix = $trimmedRoot + [System.IO.Path]::DirectorySeparatorChar
    return $fullPath.StartsWith($rootPrefix, $comparison)
}

function Get-ToolPowerShellPath {
    $commandNames = if (Test-ToolIsWindows) {
        @("pwsh", "powershell.exe")
    }
    else {
        @("pwsh")
    }

    foreach ($commandName in $commandNames) {
        $command = Get-Command $commandName -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($null -ne $command -and -not [string]::IsNullOrWhiteSpace($command.Source)) {
            return $command.Source
        }
    }

    throw "A supported PowerShell host was not found. Install pwsh on Linux/macOS or pwsh/Windows PowerShell on Windows."
}

function Get-ToolNormalizedTextSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Text file not found: $Path"
    }

    $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
    $text = [System.IO.File]::ReadAllText($Path, $utf8)
    $normalized = $text.Replace("`r`n", "`n").Replace("`r", "`n")
    $bytes = $utf8.GetBytes($normalized)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([System.BitConverter]::ToString($sha256.ComputeHash($bytes))).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $sha256.Dispose()
    }
}

function ConvertTo-ToolPlatformPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $separator = [string][System.IO.Path]::DirectorySeparatorChar
    return $Path.Replace([string][char]92, $separator).Replace([string][char]47, $separator)
}

function Resolve-ToolFullPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [string]$BasePath = (Get-Location).Path
    )

    $normalizedPath = ConvertTo-ToolPlatformPath -Path $Path
    if (Test-ToolPathRooted -Path $Path) {
        return [System.IO.Path]::GetFullPath($normalizedPath)
    }

    $normalizedBasePath = ConvertTo-ToolPlatformPath -Path $BasePath
    if (-not (Test-ToolPathRooted -Path $BasePath)) {
        $normalizedBasePath = [System.IO.Path]::GetFullPath($normalizedBasePath)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $normalizedBasePath $normalizedPath))
}

function Resolve-ToolProviderPath([Parameter(Mandatory = $true)][string]$Path) {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Resolve-ToolExistingPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (Test-ToolIsWindows) {
        return (Resolve-Path -LiteralPath $fullPath).ProviderPath
    }

    # Resolve every existing segment so lexical Unix aliases such as macOS
    # /var and their nested targets cannot disagree with child-process paths.
    $currentPath = [System.IO.Path]::GetPathRoot($fullPath)
    $relativePath = $fullPath.Substring($currentPath.Length)
    foreach ($segment in ($relativePath -split '[\\/]' | Where-Object { $_.Length -gt 0 })) {
        $item = Get-Item -LiteralPath (Join-Path $currentPath $segment) -Force
        $linkTarget = $item.ResolveLinkTarget($true)
        $currentPath = if ($null -ne $linkTarget) {
            Resolve-ToolExistingPath -Path $linkTarget.FullName
        }
        else {
            $item.FullName
        }
    }

    return [System.IO.Path]::GetFullPath($currentPath)
}

function Find-ToolReleaseArtifact {
    param(
        [Parameter(Mandatory = $true)][string]$InputRoot,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $resolvedRoot = (Resolve-Path -LiteralPath $InputRoot).Path
    $rootArtifact = Join-Path $resolvedRoot $Name
    if (Test-Path -LiteralPath $rootArtifact -PathType Leaf) {
        return Get-Item -LiteralPath $rootArtifact
    }

    # Downloaded GitHub artifacts are wrapped in per-artifact directories, so
    # callers sometimes need a recursive fallback. A canonical file staged at
    # the distribution root always wins over build, installer, or SBOM working
    # copies below that root.
    $matches = @(Get-ChildItem -LiteralPath $resolvedRoot -Recurse -File -Filter $Name)
    if ($matches.Count -ne 1) {
        throw "Expected exactly one release artifact '$Name' below '$resolvedRoot'; found $($matches.Count)."
    }

    return $matches[0]
}

function Resolve-ToolRepoPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $fullRepoRoot = Resolve-ToolFullPath -Path $RepoRoot
    return Resolve-ToolFullPath -Path $Path -BasePath $fullRepoRoot
}

function New-ToolTemporaryDirectory {
    param([Parameter(Mandatory = $true)][string]$Prefix)

    if ([string]::IsNullOrWhiteSpace($Prefix) -or
        $Prefix.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -ge 0) {
        throw "Temporary-directory prefix must be a valid file-name prefix."
    }

    $path = Join-Path ([System.IO.Path]::GetTempPath()) ($Prefix + [System.IO.Path]::GetRandomFileName())
    New-Item -ItemType Directory -Path $path -ErrorAction Stop | Out-Null
    return [System.IO.Path]::GetFullPath($path)
}

function Remove-ToolTemporaryDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [ValidateRange(1, 100)][int]$MaximumAttempts = 20,
        [ValidateRange(0, 5000)][int]$RetryDelayMilliseconds = 50
    )

    $resolvedPath = Resolve-ToolFullPath -Path $Path
    $temporaryRoot = Resolve-ToolFullPath -Path ([System.IO.Path]::GetTempPath())
    $rootPrefix = $temporaryRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $comparison = Get-ToolPathComparison
    if (-not $resolvedPath.StartsWith($rootPrefix, $comparison)) {
        throw "Refusing to remove a temporary directory outside '$temporaryRoot': $resolvedPath"
    }

    for ($attempt = 1; $attempt -le $MaximumAttempts; $attempt++) {
        try {
            if (Test-Path -LiteralPath $resolvedPath) {
                Remove-Item -LiteralPath $resolvedPath -Recurse -Force -ErrorAction Stop
            }
            return
        }
        catch [System.IO.IOException], [System.UnauthorizedAccessException] {
            if ($attempt -eq $MaximumAttempts) {
                throw
            }
            Start-Sleep -Milliseconds $RetryDelayMilliseconds
        }
    }
}

function Add-ToolValidationError {
    param(
        [Parameter(Mandatory = $true)]$Errors,
        [Parameter(Mandatory = $true)][string]$Message,
        [string]$GitHubTitle,
        [switch]$SuppressWriteError
    )

    [void]$Errors.Add($Message)
    if ($env:GITHUB_ACTIONS -eq "true" -and -not [string]::IsNullOrWhiteSpace($GitHubTitle)) {
        $escaped = $Message.Replace("%", "%25").Replace("`r", "%0D").Replace("`n", "%0A")
        Write-Host "::error title=${GitHubTitle}::$escaped"
    }
    if (-not $SuppressWriteError) {
        Write-Error $Message -ErrorAction Continue
    }
}

function Invoke-ToolProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [string[]]$Arguments = @(),
        [string]$FailureMessage,
        [string]$WorkingDirectory,
        [switch]$OutputToHost,
        [string]$OutputPath
    )

    if ($OutputToHost -and -not [string]::IsNullOrWhiteSpace($OutputPath)) {
        throw "OutputToHost and OutputPath cannot be used together."
    }

    $previousLocation = $null
    try {
        if (-not [string]::IsNullOrWhiteSpace($WorkingDirectory)) {
            $previousLocation = Get-Location
            Set-Location -LiteralPath (Resolve-ToolFullPath -Path $WorkingDirectory)
        }

        if ($OutputToHost) {
            & $FilePath @Arguments | Out-Host
        }
        elseif (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
            & $FilePath @Arguments 2>&1 | Tee-Object -FilePath $OutputPath
        }
        else {
            & $FilePath @Arguments
        }
        $exitCode = $LASTEXITCODE
    }
    finally {
        if ($null -ne $previousLocation) {
            Set-Location $previousLocation
        }
    }

    if ($exitCode -ne 0) {
        if ([string]::IsNullOrWhiteSpace($FailureMessage)) {
            throw "$FilePath exited with code $exitCode."
        }

        throw "$FailureMessage with exit code $exitCode"
    }
}

function Invoke-DotNetStep([string]$Label, [string[]]$Arguments, [string]$WorkingDirectory, [string]$DotNetPath = "dotnet") {
    Write-Host ""
    Write-Host "== $Label ==" -ForegroundColor Cyan
    Invoke-ToolProcess -FilePath $DotNetPath -Arguments $Arguments -WorkingDirectory $WorkingDirectory -FailureMessage $Label
}
function Invoke-PowerShellStep([string]$Label, [string]$ScriptPath, [string[]]$Arguments, [string]$WorkingDirectory, [string]$PowerShellPath) {
    if ([string]::IsNullOrWhiteSpace($PowerShellPath)) {
        $PowerShellPath = (Get-Command powershell.exe -ErrorAction SilentlyContinue).Path
    }

    if ([string]::IsNullOrWhiteSpace($PowerShellPath)) {
        throw "$Label requires powershell.exe because MS Word COM automation is Windows-only."
    }

    Write-Host ""
    Write-Host "== $Label ==" -ForegroundColor Cyan
    Invoke-ToolProcess -FilePath $PowerShellPath `
        -Arguments (@("-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $ScriptPath) + $Arguments) `
        -WorkingDirectory $WorkingDirectory `
        -FailureMessage $Label
}
function Invoke-DotNetRun([string]$ProjectPath, [string[]]$ToolArgs = @(), [string]$Configuration = "Release", [string]$WorkingDirectory, [string]$DotNetPath = "dotnet") {
    Invoke-ToolProcess -FilePath $DotNetPath `
        -Arguments (@("run", "--project", $ProjectPath, "--configuration", $Configuration, "--") + $ToolArgs) `
        -WorkingDirectory $WorkingDirectory `
        -FailureMessage "dotnet run failed for $ProjectPath"
}

function Invoke-ToolGeneratedProject {
    param([Parameter(Mandatory = $true)][hashtable]$Options)
    $cleanupTempRoot = New-ToolTemporaryDirectory -Prefix ($Options.Prefix + "-")
    $tempRoot = Resolve-ToolExistingPath -Path $cleanupTempRoot
    try {
        $projectPath = Join-Path $tempRoot "$($Options.Name).csproj"
        $programPath = Join-Path $tempRoot "Program.cs"
        [IO.File]::WriteAllText($projectPath, @"
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <NuGetAudit>false</NuGetAudit>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$($Options.Reference)" />
  </ItemGroup>
</Project>
"@)
        [IO.File]::WriteAllText($programPath, $Options.Source)
        $outputPaths = @($Options.Outputs.GetEnumerator() | ForEach-Object { [pscustomobject]@{ TempPath = Join-Path $tempRoot (Split-Path -Leaf $_.Key); DestinationPath = $_.Key; Label = $_.Value } })
        $buildArguments = @(
            "build", $projectPath,
            "--configuration", "Release"
        )
        & $Options.DotNetPath $buildArguments
        if ($LASTEXITCODE -ne 0) {
            Write-Warning "Generated-project build failed; retrying once with isolated build servers and serialized compilation."
            & $Options.DotNetPath @(
            "build", $projectPath,
            "--configuration", "Release",
            "--no-incremental",
            "--disable-build-servers",
            "-p:UseSharedCompilation=false",
            "-p:NodeReuse=false",
            "/nr:false",
            "-m:1"
            )
        }
        if ($LASTEXITCODE -ne 0) {
            throw $Options.Failure
        }
        & $Options.DotNetPath (@(
            "run",
            "--no-build",
            "--project", $projectPath,
            "--configuration", "Release",
            "--"
        ) + @(& $Options.Arguments $outputPaths))
        if ($LASTEXITCODE -ne 0) {
            throw $Options.Failure
        }
        if ($Options.Check) {
            foreach ($generatedFile in $outputPaths) {
                Test-ToolGeneratedFileContentMatches -ExpectedPath $generatedFile.TempPath -ActualPath $generatedFile.DestinationPath -Label $generatedFile.Label -GeneratorScriptName $Options.Script -NormalizeNewlines
            }
            Write-Host $Options.CheckMessage
            return
        }
        foreach ($generatedFile in $outputPaths) {
            $destinationDirectory = Split-Path -Parent $generatedFile.DestinationPath
            New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null
            $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
            $content = [System.IO.File]::ReadAllText($generatedFile.TempPath, $utf8).Replace("`r`n", "`n").Replace("`r", "`n")
            [System.IO.File]::WriteAllText($generatedFile.DestinationPath, $content, $utf8)
        }
        Write-Host $Options.WriteMessage
    } finally {
        Remove-ToolTemporaryDirectory -Path $cleanupTempRoot
    }
}

function Invoke-DotNetBuild([string]$ProjectPath, [string]$Configuration = "Release", [string]$WorkingDirectory, [string]$DotNetPath = "dotnet") {
    Invoke-ToolProcess -FilePath $DotNetPath `
        -Arguments @("build", $ProjectPath, "--configuration", $Configuration) `
        -WorkingDirectory $WorkingDirectory `
        -FailureMessage "dotnet build failed for $ProjectPath"
}
function Invoke-DotNetRunNoBuild([string]$ProjectPath, [string[]]$ToolArgs = @(), [string]$Configuration = "Release", [string]$WorkingDirectory, [string]$DotNetPath = "dotnet") {
    Invoke-ToolProcess -FilePath $DotNetPath `
        -Arguments (@("run", "--no-restore", "--no-build", "--project", $ProjectPath, "--configuration", $Configuration, "--") + $ToolArgs) `
        -WorkingDirectory $WorkingDirectory `
        -FailureMessage "dotnet run --no-build failed for $ProjectPath"
}

function Resolve-InputPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    if (Test-ToolPathRooted -Path $Path) {
        return Resolve-ToolFullPath -Path $Path
    }

    $currentDirectoryCandidate = Resolve-ToolFullPath -Path $Path
    if (Test-Path -LiteralPath $currentDirectoryCandidate) {
        return $currentDirectoryCandidate
    }

    return Resolve-ToolRepoPath -Path $Path -RepoRoot $RepoRoot
}

function Get-ToolRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$RootPath,
        [Parameter(Mandatory = $true)][string]$Path
    )

    $fullRootPath = Resolve-ToolFullPath -Path $RootPath
    $fullPath = Resolve-ToolFullPath -Path $Path -BasePath $fullRootPath
    $rootWithSeparator = $fullRootPath.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

    $rootUri = [System.Uri]::new($rootWithSeparator)
    $pathUri = [System.Uri]::new($fullPath)
    return ConvertTo-ToolNormalizedRelativePath -Path ([System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($pathUri).ToString()))
}

function ConvertTo-ToolNormalizedRelativePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    return $Path.Replace([string][char]92, "/")
}

function Test-ToolExcludedPath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string[]]$ExcludedDirectoryNames = @("bin", "obj", ".worktrees", ".claude")
    )

    $relativePath = Get-ToolRelativePath -RootPath $RepoRoot -Path $Path
    $segments = (ConvertTo-ToolNormalizedRelativePath -Path $relativePath) -split '/'
    foreach ($directoryName in $ExcludedDirectoryNames) {
        if ($segments -contains $directoryName) {
            return $true
        }
    }

    return $false
}

function Get-ToolTrackedRepositoryFiles {
    param([Parameter(Mandatory = $true)][string]$RepoRoot)

    $gitOutput = & git -C $RepoRoot ls-files --deduplicate
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to enumerate tracked files with git ls-files."
    }

    foreach ($relativePath in $gitOutput) {
        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            continue
        }

        $relativePath
    }
}

function Test-ToolIgnoredDirectoryName {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [string[]]$IgnoredDirectoryNames = @("bin", "obj", ".git", ".worktrees", ".claude")
    )

    return $IgnoredDirectoryNames -contains $Name
}

function Get-ToolProjectFiles {
    param(
        [Parameter(Mandatory = $true)][System.IO.DirectoryInfo]$Directory,
        [string[]]$IgnoredDirectoryNames = @("bin", "obj", ".git", ".worktrees", ".claude"),
        [string[]]$IgnoredProjectNamePatterns = @("*_wpftmp.csproj")
    )

    foreach ($projectFile in $Directory.EnumerateFiles("*.csproj")) {
        $isIgnored = $false
        foreach ($pattern in $IgnoredProjectNamePatterns) {
            if ($projectFile.Name -like $pattern) {
                $isIgnored = $true
                break
            }
        }

        if (-not $isIgnored) {
            $projectFile
        }
    }

    foreach ($childDirectory in $Directory.EnumerateDirectories()) {
        if (Test-ToolIgnoredDirectoryName -Name $childDirectory.Name -IgnoredDirectoryNames $IgnoredDirectoryNames) {
            continue
        }

        Get-ToolProjectFiles `
            -Directory $childDirectory `
            -IgnoredDirectoryNames $IgnoredDirectoryNames `
            -IgnoredProjectNamePatterns $IgnoredProjectNamePatterns
    }
}

function Get-RepoRoot {
    param([Parameter(Mandatory = $true)][string]$ScriptRoot)

    return (Resolve-Path (Join-Path $ScriptRoot "..")).Path
}

function Get-GitValue {
    param(
        [string]$RepoRoot,
        [string[]]$Arguments
    )

    try {
        $value = & git -C $RepoRoot @Arguments 2>$null
        if ($LASTEXITCODE -eq 0) {
            return ($value -join "`n").Trim()
        }
    }
    catch {
    }

    return $null
}

function Resolve-FreeXExe {
    param(
        [string]$RepoRoot,
        [string]$RequestedPath,
        [switch]$SkipBuild
    )

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $resolved = Resolve-ToolRepoPath -Path $RequestedPath -RepoRoot $RepoRoot
        if (-not (Test-Path -LiteralPath $resolved)) {
            throw "FreeX executable was not found at $RequestedPath"
        }
        return (Resolve-Path -LiteralPath $resolved).Path
    }

    $candidate = Join-Path $RepoRoot "src/FreeX.App.Host/bin/Release/net10.0-windows10.0.19041.0/FreeX.App.Host.exe"
    if (-not (Test-Path $candidate) -and -not $SkipBuild) {
        $buildOutput = & dotnet build (Join-Path $RepoRoot "src/FreeX.App.Host/FreeX.App.Host.csproj") --configuration Release
        $buildOutput | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) {
            throw "FreeX host build failed with exit code $LASTEXITCODE"
        }
    }

    if (-not (Test-Path $candidate)) {
        throw "FreeX host executable was not found. Build Release or pass -FreeXExe. Expected: $candidate"
    }

    return (Resolve-Path $candidate).Path
}

function ConvertTo-ToolRepoRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    $fullRoot = Resolve-ToolFullPath -Path $RepoRoot
    $fullPath = Resolve-ToolFullPath -Path $Path -BasePath $fullRoot
    $trimmedRoot = $fullRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $comparison = Get-ToolPathComparison

    if ($fullPath.Equals($trimmedRoot, $comparison)) {
        return ""
    }

    if (-not (Test-ToolPathWithinRoot -Path $fullPath -RootPath $trimmedRoot)) {
        throw "Path '$fullPath' is outside repository root '$trimmedRoot'."
    }

    $rootPrefix = $trimmedRoot + [System.IO.Path]::DirectorySeparatorChar
    return ConvertTo-ToolNormalizedRelativePath -Path $fullPath.Substring($rootPrefix.Length)
}

function Read-ToolJson {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [string]$MissingMessage = "Required generated JSON input is missing"
    )

    $resolvedPath = Resolve-ToolRepoPath -Path $Path -RepoRoot $RepoRoot
    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "$MissingMessage`: $resolvedPath"
    }

    Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json
}

function ConvertTo-ToolMarkdownCell {
    param([AllowNull()][string]$Value)

    if ($null -eq $Value -or $Value.Length -eq 0) {
        return ""
    }

    $Value.Replace('|', '\|')
}

function ConvertTo-ToolXmlAttribute {
    param([Parameter(Mandatory = $true)][string]$Value)

    return [System.Security.SecurityElement]::Escape($Value)
}

function Get-ToolCommandInventoryMenuTraversalSource {
    return @'
    private static IEnumerable<(string CommandId, CommandLocation Location)> MenuLocations(
        RibbonControl control,
        RibbonTab tab,
        RibbonGroup group,
        string profile)
    {
        var menu = control switch
        {
            RibbonSplitButton splitButton => splitButton.Menu,
            RibbonDropdown dropdown => dropdown.Menu,
            _ => null,
        };

        if (menu is null)
            yield break;

        foreach (var item in MenuItems(menu.Items))
        {
            if (item.CommandId is null)
                continue;

            yield return (item.CommandId.Value.Value, new CommandLocation(
                Profile: profile,
                TabId: tab.Id,
                Tab: tab.Header,
                GroupId: group.Id,
                Group: group.Header,
                Label: item.Header,
                ControlType: "RibbonMenuItem",
                Layout: "Menu"));
        }
    }

    private static IEnumerable<RibbonMenuItem> MenuItems(IEnumerable<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            yield return item;
            foreach (var child in MenuItems(item.Children))
                yield return child;
        }
    }
'@.TrimEnd()
}

function Test-ToolGeneratedFileContentMatches {
    param(
        [Parameter(Mandatory = $true)][string]$ExpectedPath,
        [Parameter(Mandatory = $true)][string]$ActualPath,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$GeneratorScriptName,
        [switch]$NormalizeNewlines
    )

    $utf8 = [System.Text.UTF8Encoding]::new($false, $true)
    Test-ToolGeneratedContentMatches `
        -ExpectedContent ([System.IO.File]::ReadAllText($ExpectedPath, $utf8)) `
        -ActualPath $ActualPath `
        -Label $Label `
        -GeneratorScriptName $GeneratorScriptName `
        -NormalizeNewlines:$NormalizeNewlines
}

function Test-ToolGeneratedContentMatches {
    param(
        [Parameter(Mandatory = $true)][string]$ExpectedContent,
        [Parameter(Mandatory = $true)][string]$ActualPath,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$GeneratorScriptName,
        [switch]$NormalizeNewlines
    )

    if (-not (Test-Path -LiteralPath $ActualPath -PathType Leaf)) {
        throw "$Label is missing. Run $GeneratorScriptName to create it."
    }

    $actual = [System.IO.File]::ReadAllText($ActualPath, [System.Text.UTF8Encoding]::new($false, $true))
    $expected = $ExpectedContent
    if ($NormalizeNewlines) {
        $expected = $expected -replace "`r`n", "`n"
        $actual = $actual -replace "`r`n", "`n"
    }

    if ($expected -cne $actual) {
        throw "$Label is out of date. Run $GeneratorScriptName to refresh it."
    }
}

function Invoke-ToolCanonicalPwshHost {
    <#
    .SYNOPSIS
    Re-launches a deterministic generator under cross-platform PowerShell.

    .DESCRIPTION
    Windows PowerShell 5.1 and pwsh serialize the same object differently in
    ConvertTo-Json. Generators that commit or compare byte-for-byte artifacts
    call this helper before producing content so Windows, Linux, macOS, local
    tests, and hosted workflows all use the same PowerShell implementation.
    The helper returns normally under pwsh and exits the calling script with
    the child exit code after re-launching from Windows PowerShell.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [string[]]$ForwardedArguments = @()
    )

    if ($PSVersionTable.PSEdition -ne 'Desktop') {
        return
    }

    $pwshCommand = Get-Command pwsh -ErrorAction Stop
    & $pwshCommand.Source -NoProfile -ExecutionPolicy Bypass -File $ScriptPath @ForwardedArguments
    exit $LASTEXITCODE
}

function Invoke-FidelityCorpusDownload {
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$FilesDirectory,
        [Parameter(Mandatory = $true)][string]$CorpusLabel,
        [Parameter(Mandatory = $true)][string]$LocalDirectoryLabel,
        [string]$Source,
        [switch]$Force,
        [scriptblock]$DownloadAction
    )

    if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
        throw "Manifest not found: $ManifestPath"
    }

    if (-not (Test-Path -LiteralPath $FilesDirectory -PathType Container)) {
        New-Item -ItemType Directory -Path $FilesDirectory -Force | Out-Null
    }

    $rows = @(Import-Csv -LiteralPath $ManifestPath)
    if (-not [string]::IsNullOrWhiteSpace($Source)) {
        $rows = @($rows | Where-Object { $_.source -eq $Source })
    }

    if ($null -eq $DownloadAction) {
        $DownloadAction = {
            param([string]$Uri, [string]$TargetPath)
            Invoke-WebRequest -Uri $Uri -OutFile $TargetPath -UseBasicParsing -TimeoutSec 120
        }
    }

    $downloaded = 0
    $skipped = 0
    $failed = 0
    $localSkipped = 0

    foreach ($row in $rows) {
        if ([string]::IsNullOrWhiteSpace($row.license)) {
            throw "Manifest row '$($row.id)' is missing a license."
        }

        $target = Join-Path $FilesDirectory $row.file
        if ($row.url -like 'local://*' -or $row.source -eq 'local') {
            if (Test-Path -LiteralPath $target) {
                Write-Host "[local ] $($row.file) (present)"
            }
            else {
                Write-Warning "[local ] $($row.file) declared local but not found in $LocalDirectoryLabel"
            }

            $localSkipped++
            continue
        }

        if ((Test-Path -LiteralPath $target) -and -not $Force) {
            $skipped++
            Write-Host "[skip  ] $($row.file) (already downloaded)"
            continue
        }

        try {
            $targetDirectory = Split-Path -Parent $target
            if (-not (Test-Path -LiteralPath $targetDirectory -PathType Container)) {
                New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
            }

            & $DownloadAction $row.url $target $row
            $size = (Get-Item -LiteralPath $target).Length
            if ($size -le 0) {
                throw "downloaded 0 bytes"
            }

            $downloaded++
            Write-Host ("[ok    ] {0} ({1:N0} bytes, {2})" -f $row.file, $size, $row.license)
        }
        catch {
            $failed++
            if (Test-Path -LiteralPath $target -PathType Leaf) {
                Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
            }

            Write-Warning "[fail  ] $($row.file) <- $($row.url): $($_.Exception.Message)"
        }
    }

    Write-Host ""
    Write-Host ("{0}: {1} downloaded, {2} already present, {3} local, {4} failed (of {5} rows)." -f `
        $CorpusLabel, $downloaded, $skipped, $localSkipped, $failed, $rows.Count)
    Write-Host "Files: $FilesDirectory"

    [pscustomobject]@{
        Downloaded  = $downloaded
        Skipped     = $skipped
        LocalSkipped = $localSkipped
        Failed      = $failed
        RowCount    = $rows.Count
        ExitCode    = if ($failed -gt 0) { 1 } else { 0 }
    }
}
