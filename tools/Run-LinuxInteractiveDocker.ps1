<#
.SYNOPSIS
  Run a FreeX-family Avalonia app in an interactive Linux desktop exposed through noVNC.

.DESCRIPTION
  Publishes FreeX, FreeW, or FreeP for linux-x64, starts an isolated Ubuntu 24.04
  container with Xvfb, Openbox, x11vnc, and noVNC, and exposes the desktop only
  on localhost. The container remains available until the Stop action is used.

.EXAMPLE
  powershell -File tools/Run-LinuxInteractiveDocker.ps1 -App FreeX -OpenBrowser

.EXAMPLE
  powershell -File tools/Run-LinuxInteractiveDocker.ps1 -Action Screenshot -App FreeX

.EXAMPLE
  powershell -File tools/Run-LinuxInteractiveDocker.ps1 -Action Stop -App FreeX
#>
[CmdletBinding()]
param(
    [ValidateSet("Start", "Stop", "Status", "Screenshot", "Clean")]
    [string]$Action = "Start",

    [ValidateSet("FreeX", "FreeW", "FreeP")]
    [string]$App = "FreeX",

    [ValidateSet("Application", "Validation")]
    [string]$Host = "Application",

    [ValidateRange(1024, 65535)]
    [int]$Port = 6080,

    [ValidateRange(640, 7680)]
    [int]$Width = 1280,

    [ValidateRange(480, 4320)]
    [int]$Height = 820,

    [ValidateRange(72, 240)]
    [int]$Dpi = 96,

    [ValidateSet("2g", "4g", "6g", "8g")]
    [string]$MemoryLimit = "4g",

    [string]$OutputDir = "artifacts/linux-interactive",
    [string]$PublishDir = "",
    [string]$Image = "freex-linux-interactive:ubuntu24.04",
    [string]$DocumentPath = "",
    [string[]]$AppArgument = @(),
    [string[]]$AppEnvironment = @(),
    [switch]$CupsDryRun,
    [ValidateSet("success", "failure")]
    [string]$CupsDryRunMode = "success",
    [string]$SessionMetadataPath = "",
    [switch]$SkipPublish,
    [switch]$SkipImageBuild,
    [switch]$OpenBrowser,
    [switch]$Replace
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$normalizedRepoRoot = [IO.Path]::GetFullPath($repoRoot).TrimEnd(
    [IO.Path]::DirectorySeparatorChar,
    [IO.Path]::AltDirectorySeparatorChar).ToLowerInvariant()
$workspaceHasher = [Security.Cryptography.SHA256]::Create()
try {
    $workspaceHashBytes = $workspaceHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($normalizedRepoRoot))
    $workspaceKey = -join ($workspaceHashBytes[0..5] | ForEach-Object { $_.ToString("x2") })
} finally {
    $workspaceHasher.Dispose()
}
$dockerContext = Join-Path $PSScriptRoot "LinuxInteractiveDocker"
$resolvedOutputRoot = if ([IO.Path]::IsPathRooted($OutputDir)) {
    [IO.Path]::GetFullPath($OutputDir)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $OutputDir))
}

$appDefinitions = @{
    FreeX = @{
        Project = "src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj"
        Executable = "FreeX"
        WindowTitle = "FreeX"
    }
    FreeW = @{
        Project = "freew/FreeW.App.Avalonia/FreeW.App.Avalonia.csproj"
        Executable = "FreeW"
        WindowTitle = "FreeW"
    }
    FreeP = @{
        Project = "freep/FreeP.App.Avalonia/FreeP.App.Avalonia.csproj"
        Executable = "FreeP"
        WindowTitle = "FreeP"
    }
}

$definition = $appDefinitions[$App]
if ($Host -eq "Validation") {
    if ($App -ne "FreeP") {
        throw "The Validation host is currently available only for FreeP."
    }
    $definition = @{
        Project = "freep/TestSupport/Validation.Avalonia/FreeP.Validation.Avalonia.csproj"
        Executable = "FreeP.Validation.Avalonia"
        WindowTitle = "FreeP"
    }
}
$appKey = $App.ToLowerInvariant()
$publishKey = if ($Host -eq "Validation") { "$appKey-validation" } else { $appKey }
$containerName = "freex-linux-interactive-$appKey-$Port"
$appImage = "freex-linux-interactive-app-$publishKey-$workspaceKey`:current"
$appOutputRoot = Join-Path $resolvedOutputRoot $appKey
$publishDir = if ([string]::IsNullOrWhiteSpace($PublishDir)) {
    Join-Path $env:TEMP "FreeX-LinuxInteractive/$workspaceKey/$publishKey/publish/linux-x64"
} elseif ([IO.Path]::IsPathRooted($PublishDir)) {
    [IO.Path]::GetFullPath($PublishDir)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $PublishDir))
}
$documentsDir = Join-Path $appOutputRoot "documents"
$currentSessionPath = Join-Path $appOutputRoot "current-session.json"
$sessionMetadataOutputPath = if ([string]::IsNullOrWhiteSpace($SessionMetadataPath)) {
    $currentSessionPath
} elseif ([IO.Path]::IsPathRooted($SessionMetadataPath)) {
    [IO.Path]::GetFullPath($SessionMetadataPath)
} else {
    [IO.Path]::GetFullPath((Join-Path $repoRoot $SessionMetadataPath))
}
$labelName = "io.github.tony-xmelon.freex.linux-interactive"

function Invoke-Docker {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $output = @(& docker @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $previousPreference
    $output = @($output | ForEach-Object { $_.ToString() })

    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "docker $($Arguments -join ' ') failed with exit code $exitCode.`n$($output -join [Environment]::NewLine)"
    }

    [pscustomobject]@{
        ExitCode = $exitCode
        Output = $output
    }
}

function Get-OwnedContainerStatus {
    $inspect = Invoke-Docker -Arguments @("inspect", $containerName) -AllowFailure

    if ($inspect.ExitCode -ne 0) {
        return $null
    }

    $containers = @((($inspect.Output -join [Environment]::NewLine) | ConvertFrom-Json))
    if ($containers.Count -ne 1) {
        throw "Expected one Docker inspection result for '$containerName', found $($containers.Count)."
    }

    $container = $containers[0]
    $ownershipLabel = $container.Config.Labels.PSObject.Properties[$labelName]
    if ($null -eq $ownershipLabel -or $ownershipLabel.Value -ne "true") {
        throw "Container '$containerName' exists but is not owned by this harness."
    }

    return [string]$container.State.Status
}

function Get-CurrentSession {
    if (-not (Test-Path -LiteralPath $currentSessionPath -PathType Leaf)) {
        return $null
    }

    return Get-Content -LiteralPath $currentSessionPath -Raw | ConvertFrom-Json
}

function Write-SessionMetadata {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)]$Metadata
    )

    $parent = Split-Path -Parent $Path
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $temporaryPath = Join-Path $parent (".$([IO.Path]::GetFileName($Path)).$([guid]::NewGuid().ToString('N')).tmp")
    try {
        $json = $Metadata | ConvertTo-Json -Depth 8
        [IO.File]::WriteAllText($temporaryPath, $json, (New-Object Text.UTF8Encoding($false)))
        Move-Item -LiteralPath $temporaryPath -Destination $Path -Force
    } finally {
        if (Test-Path -LiteralPath $temporaryPath -PathType Leaf) {
            Remove-Item -LiteralPath $temporaryPath -Force -ErrorAction SilentlyContinue
        }
    }
}

function Get-OwnedAppImage {
    $inspect = Invoke-Docker -Arguments @("image", "inspect", $appImage) -AllowFailure
    if ($inspect.ExitCode -ne 0) {
        return $null
    }

    $images = @((($inspect.Output -join [Environment]::NewLine) | ConvertFrom-Json))
    if ($images.Count -ne 1) {
        throw "Expected one Docker image inspection result for '$appImage', found $($images.Count)."
    }

    $image = $images[0]
    $ownershipLabel = $image.Config.Labels.PSObject.Properties[$labelName]
    if ($null -eq $ownershipLabel -or $ownershipLabel.Value -ne "true") {
        throw "Docker image '$appImage' exists but is not owned by this harness."
    }

    return $image
}

if ($Action -eq "Stop" -or $Action -eq "Clean") {
    $status = Get-OwnedContainerStatus
    if ($null -ne $status) {
        $stop = Invoke-Docker -Arguments @("stop", "--timeout", "10", $containerName) -AllowFailure
        if ($stop.ExitCode -eq 0 -or $null -eq (Get-OwnedContainerStatus)) {
            Write-Host "Stopped interactive container '$containerName'."
        } else {
            throw "Could not stop owned interactive container '$containerName'.`n$($stop.Output -join [Environment]::NewLine)"
        }
    } else {
        Write-Host "Interactive container '$containerName' is not running."
    }

    if ($Action -eq "Clean") {
        $image = Get-OwnedAppImage
        if ($null -ne $image) {
            Invoke-Docker -Arguments @("image", "rm", $appImage) | Out-Null
            Write-Host "Removed cached app image '$appImage'."
        }
    }
    exit 0
}

if ($Action -eq "Status") {
    $status = Get-OwnedContainerStatus
    if ($null -eq $status) {
        Write-Host "Interactive container '$containerName' is not running."
        exit 1
    }

    Write-Host "Container: $containerName"
    Write-Host "Status   : $status"
    Write-Host "Desktop  : http://127.0.0.1:$Port/vnc.html?autoconnect=true&resize=scale"
    $session = Get-CurrentSession
    if ($null -ne $session) {
        Write-Host "Session  : $($session.sessionDirectory)"
    }
    exit 0
}

if ($Action -eq "Screenshot") {
    $status = Get-OwnedContainerStatus
    if ($status -ne "running") {
        throw "Interactive container '$containerName' is not running."
    }

    $stamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
    $relativeScreenshot = "screenshots/manual-$stamp.png"
    Invoke-Docker -Arguments @(
        "exec",
        $containerName,
        "bash",
        "-lc",
        "export DISPLAY=:99; mkdir -p /work/screenshots && scrot -o /work/$relativeScreenshot"
    ) | Out-Null

    $session = Get-CurrentSession
    if ($null -eq $session) {
        throw "Current session metadata was not found at '$currentSessionPath'."
    }

    $screenshotPath = Join-Path ([string]$session.sessionDirectory) ($relativeScreenshot -replace "/", [IO.Path]::DirectorySeparatorChar)
    Write-Host "Screenshot: $screenshotPath"
    exit 0
}

$existingStatus = Get-OwnedContainerStatus
if ($null -ne $existingStatus) {
    if (-not $Replace) {
        throw "Interactive container '$containerName' already exists with status '$existingStatus'. Use -Replace or the Stop action."
    }

    $stop = Invoke-Docker -Arguments @("stop", "--timeout", "10", $containerName) -AllowFailure
    if ($stop.ExitCode -ne 0 -and $null -ne (Get-OwnedContainerStatus)) {
        throw "Could not replace owned interactive container '$containerName'.`n$($stop.Output -join [Environment]::NewLine)"
    }
}

New-Item -ItemType Directory -Path $appOutputRoot, $publishDir, $documentsDir -Force | Out-Null

if (-not $SkipImageBuild) {
    Write-Host "Building interactive Linux desktop image '$Image'..."
    $baseBuild = Invoke-Docker -Arguments @("build", "--quiet", "--tag", $Image, $dockerContext)
    Write-Host "Base image: $([string]$baseBuild.Output[-1])"
}

$projectPath = Join-Path $repoRoot $definition.Project
if (-not $SkipPublish) {
    Write-Host "Publishing $App $Host host for linux-x64..."
    $publishArguments = @(
        "--configuration", "Release",
        "--framework", "net10.0",
        "--runtime", "linux-x64",
        "--self-contained", "true",
        "--disable-build-servers",
        "-p:UseSharedCompilation=false",
        "-p:NodeReuse=false",
        "/nr:false",
        "-m:1",
        "-p:UseAppHost=true",
        "-p:PublishReadyToRun=false",
        "-p:PublishSingleFile=false",
        "--output", $publishDir)
    if ($App -eq "FreeP") {
        # FreeP selects its Windows target on a Windows host unless the Linux target is explicit.
        $publishArguments += "-p:FreePWindowsBuild=false"
    }
    & dotnet publish $projectPath @publishArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Publishing $App for linux-x64 failed."
    }
}

$publishedExecutable = Join-Path $publishDir $definition.Executable
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Published Linux executable was not found: $publishedExecutable"
}

$existingAppImage = Get-OwnedAppImage
if (-not $SkipPublish) {
    $imageContext = Join-Path $env:TEMP "FreeX-LinuxInteractive/$appKey/image-context"
    New-Item -ItemType Directory -Path $imageContext -Force | Out-Null
    $archivePath = Join-Path $imageContext "app.tar.gz"
    $appDockerfilePath = Join-Path $imageContext "Dockerfile"

    if (Test-Path -LiteralPath $archivePath -PathType Leaf) {
        Remove-Item -LiteralPath $archivePath -Force
    }

    Write-Host "Packing the Linux publish into one Docker build layer..."
    & tar -czf $archivePath -C $publishDir .
    if ($LASTEXITCODE -ne 0) {
        throw "Creating the Linux app archive failed."
    }

    $appDockerfile = @"
FROM $Image
LABEL $labelName="true"
LABEL $labelName.app="$appKey"
COPY app.tar.gz /tmp/app.tar.gz
RUN mkdir -p /opt/published \
    && tar -xzf /tmp/app.tar.gz -C /opt/published \
    && rm /tmp/app.tar.gz \
    && chmod +x /opt/published/$($definition.Executable)
"@
    [IO.File]::WriteAllText($appDockerfilePath, $appDockerfile, (New-Object Text.UTF8Encoding($false)))

    try {
        Write-Host "Building app image '$appImage'..."
        $appBuild = Invoke-Docker -Arguments @("build", "--quiet", "--tag", $appImage, $imageContext)
        Write-Host "App image : $([string]$appBuild.Output[-1])"
    } finally {
        Remove-Item -LiteralPath $archivePath -Force -ErrorAction SilentlyContinue
    }
} elseif ($null -eq $existingAppImage) {
    throw "Cached app image '$appImage' was not found. Run without -SkipPublish once."
}

$containerDocument = ""
if (-not [string]::IsNullOrWhiteSpace($DocumentPath)) {
    $resolvedDocument = [IO.Path]::GetFullPath($DocumentPath)
    if (-not (Test-Path -LiteralPath $resolvedDocument -PathType Leaf)) {
        throw "Document was not found: $resolvedDocument"
    }

    $documentName = Split-Path -Leaf $resolvedDocument
    Copy-Item -LiteralPath $resolvedDocument -Destination (Join-Path $documentsDir $documentName) -Force
    $containerDocument = "/documents/$documentName"
} elseif ($App -eq "FreeX") {
    $demoPath = Join-Path $documentsDir "linux-interactive-demo.csv"
    @"
Region,Q1,Q2,Total
North,120,135,255
South,98,110,208
East,143,150,293
West,87,92,179
"@ | Set-Content -LiteralPath $demoPath -Encoding utf8
    $containerDocument = "/documents/$(Split-Path -Leaf $demoPath)"
}

$sessionStamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssfffZ")
$sessionDir = Join-Path $appOutputRoot "sessions/$sessionStamp"
if (Test-Path -LiteralPath $sessionDir) {
    $sessionDir = Join-Path $appOutputRoot "sessions/$sessionStamp-$([guid]::NewGuid().ToString('N'))"
}
New-Item -ItemType Directory -Path $sessionDir -Force | Out-Null

$sessionMetadata = [ordered]@{
    schemaVersion = 1
    sessionId = [guid]::NewGuid().ToString("N")
    app = $App
    containerName = $containerName
    port = $Port
    url = "http://127.0.0.1:$Port/vnc.html?autoconnect=true&resize=scale"
    sessionDirectory = $sessionDir
    startedUtc = (Get-Date).ToUniversalTime().ToString("O")
}
# Keep the legacy pointer for interactive Status/Screenshot callers, and also write the
# caller-owned path atomically so validation can bind to this exact Start invocation.
Write-SessionMetadata -Path $currentSessionPath -Metadata $sessionMetadata
if ($sessionMetadataOutputPath -ne $currentSessionPath) {
    Write-SessionMetadata -Path $sessionMetadataOutputPath -Metadata $sessionMetadata
}

$sessionMount = "type=bind,source=$sessionDir,target=/work"
$documentsMount = "type=bind,source=$documentsDir,target=/documents"
$portBinding = "127.0.0.1:$Port`:6080"
$appArgumentsBase64 = if ($AppArgument.Count -eq 0) {
    ""
} else {
    [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes(($AppArgument -join "`n")))
}
$appEnvironmentArguments = @()
foreach ($environmentEntry in $AppEnvironment) {
    if ([string]::IsNullOrWhiteSpace($environmentEntry) -or $environmentEntry -notmatch '=') {
        throw "AppEnvironment entries must be non-empty NAME=VALUE pairs."
    }
    $appEnvironmentArguments += @("--env", $environmentEntry)
}

Write-Host "Starting interactive Linux container '$containerName'..."
$dockerRunArguments = @(
    "run",
    "--detach",
    "--rm",
    "--init",
    "--name", $containerName,
    "--label", "$labelName=true",
    "--label", "$labelName.app=$appKey",
    # Exhaustive parity runs retain rendered evidence for every dialog and need more than 2 GB.
    "--memory", $MemoryLimit,
    "--shm-size", "256m",
    "--publish", $portBinding,
    "--env", "APP_EXECUTABLE=$($definition.Executable)",
    "--env", "APP_WINDOW_TITLE=$($definition.WindowTitle)",
    "--env", "APP_DOCUMENT=$containerDocument",
    "--env", "APP_ARGUMENTS_B64=$appArgumentsBase64",
    "--env", "SCREEN_WIDTH=$Width",
    "--env", "SCREEN_HEIGHT=$Height",
    "--env", "SCREEN_DPI=$Dpi",
    "--mount", $sessionMount,
    "--mount", $documentsMount
) + $appEnvironmentArguments + @($appImage)
if ($CupsDryRun) {
    $dockerRunArguments = @($dockerRunArguments[0..($dockerRunArguments.Count - 2)]) + @(
        "--env", "FREEX_CUPS_DRY_RUN=1",
        "--env", "FREEX_CUPS_DRY_RUN_MODE=$CupsDryRunMode",
        $dockerRunArguments[-1])
}
$runResult = Invoke-Docker -Arguments $dockerRunArguments
Write-Host "Container id: $([string]$runResult.Output[0])"

$readyPath = Join-Path $sessionDir "ready.json"
$failurePath = Join-Path $sessionDir "failure.json"
$url = [string]$sessionMetadata.url
$deadline = (Get-Date).AddSeconds(90)
$webReady = $false

while ((Get-Date) -lt $deadline) {
    if (Test-Path -LiteralPath $failurePath -PathType Leaf) {
        $failure = Get-Content -LiteralPath $failurePath -Raw
        throw "The Linux desktop started, but the app did not become ready.`n$failure"
    }

    if (-not $webReady) {
        try {
            $response = Invoke-WebRequest -Uri $url -TimeoutSec 2 -UseBasicParsing
            $webReady = ($response.StatusCode -eq 200)
        } catch {
            $webReady = $false
        }
    }

    if ($webReady -and (Test-Path -LiteralPath $readyPath -PathType Leaf)) {
        break
    }
    Start-Sleep -Milliseconds 500
}

if (-not $webReady -or -not (Test-Path -LiteralPath $readyPath -PathType Leaf)) {
    $logs = Invoke-Docker -Arguments @("logs", "--tail", "100", $containerName) -AllowFailure
    throw "Interactive Linux session did not become ready within 90 seconds.`n$($logs.Output -join [Environment]::NewLine)"
}

$ready = Get-Content -LiteralPath $readyPath -Raw | ConvertFrom-Json
Write-Host ""
Write-Host "$App is running interactively on Linux."
Write-Host "Desktop    : $url"
Write-Host "Resolution : $($ready.screen) at $($ready.dpi) DPI"
Write-Host "Window     : $($ready.windowTitle)"
Write-Host "Published  : $publishDir"
Write-Host "Artifacts  : $sessionDir"
Write-Host "Session metadata: $sessionMetadataOutputPath"
Write-Host "Stop       : powershell -File tools/Run-LinuxInteractiveDocker.ps1 -Action Stop -App $App -Port $Port"

if ($OpenBrowser) {
    Start-Process $url
}
