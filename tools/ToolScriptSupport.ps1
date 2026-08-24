function Test-ToolPathRooted {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return $true
    }

    return $Path -match '^(?:[A-Za-z]:[\\/]|[\\/]{2})'
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
    $comparison = if ([System.IO.Path]::DirectorySeparatorChar -eq [char]92) {
        [System.StringComparison]::OrdinalIgnoreCase
    }
    else {
        [System.StringComparison]::Ordinal
    }
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
    $tempRoot = New-ToolTemporaryDirectory -Prefix ($Options.Prefix + "-")
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
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="$($Options.Reference)" />
  </ItemGroup>
</Project>
"@)
        [IO.File]::WriteAllText($programPath, $Options.Source)
        $outputPaths = @($Options.Outputs.GetEnumerator() | ForEach-Object { [pscustomobject]@{ TempPath = Join-Path $tempRoot (Split-Path -Leaf $_.Key); DestinationPath = $_.Key; Label = $_.Value } })
        & $Options.DotNetPath @(
            "build", $projectPath,
            "--configuration", "Release",
            "--disable-build-servers",
            "-p:UseSharedCompilation=false",
            "-p:NodeReuse=false",
            "/nr:false",
            "-m:1"
        )
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
                Test-ToolGeneratedFileContentMatches -ExpectedPath $generatedFile.TempPath -ActualPath $generatedFile.DestinationPath -Label $generatedFile.Label -GeneratorScriptName $Options.Script
            }
            Write-Host $Options.CheckMessage
            return
        }
        foreach ($generatedFile in $outputPaths) {
            $destinationDirectory = Split-Path -Parent $generatedFile.DestinationPath
            New-Item -ItemType Directory -Path $destinationDirectory -Force | Out-Null; Copy-Item -LiteralPath $generatedFile.TempPath -Destination $generatedFile.DestinationPath -Force
        }
        Write-Host $Options.WriteMessage
    } finally {
        Remove-ToolTemporaryDirectory -Path $tempRoot
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
    $comparison = if ([System.IO.Path]::DirectorySeparatorChar -eq [char]92) {
        [System.StringComparison]::OrdinalIgnoreCase
    }
    else {
        [System.StringComparison]::Ordinal
    }

    if ($fullPath.Equals($trimmedRoot, $comparison)) {
        return ""
    }

    $rootPrefix = $trimmedRoot + [System.IO.Path]::DirectorySeparatorChar
    if ($fullPath.StartsWith($rootPrefix, $comparison)) {
        return $fullPath.Substring($rootPrefix.Length).Replace([string][char]47, [string][char]92)
    }

    return $fullPath.Replace([string][char]47, [string][char]92)
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
