function Invoke-PhysicalValidationFixture {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$Action,
        [Parameter(Mandatory = $true)][string]$ArtifactPath
    )

    $lines = @(& dotnet run --project $ProjectPath --configuration Release --no-restore -- $Action $ArtifactPath 2>&1)
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        throw "Fixture '$Action' failed with exit code $exitCode.`n$($lines -join [Environment]::NewLine)"
    }
    return $lines
}

function ConvertFrom-PhysicalValidationKeyValueLines {
    param([Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Lines)

    $values = [ordered]@{}
    foreach ($line in $Lines) {
        if ([string]$line -match '^([^=]+)=(.*)$') {
            $values[$Matches[1]] = $Matches[2]
        }
    }
    return $values
}

function Start-PhysicalValidationHarness {
    # Starts the harness-owned Linux interactive container for one document through the generic
    # Run-LinuxInteractiveDocker.ps1 entry point. The caller records that it started so the
    # matching Stop-PhysicalValidationHarness runs from its finally block.
    param(
        [Parameter(Mandatory = $true)][string]$PowerShellPath,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$App,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$OutputDir,
        [Parameter(Mandatory = $true)][string]$DocumentPath
    )

    & $PowerShellPath -NoProfile -File (Join-Path $RepoRoot "tools/Run-LinuxInteractiveDocker.ps1") `
        -Action Start -App $App -Port $Port -OutputDir $OutputDir -DocumentPath $DocumentPath -Replace
}

function Stop-PhysicalValidationHarness {
    # Stops the harness-owned container. With -WarnOnFailure (the finally-block form) a failed stop
    # is reported as a warning so it cannot mask the real validation error in flight; without it a
    # failed stop is not inspected, matching the mid-run stop before a reopen.
    param(
        [Parameter(Mandatory = $true)][string]$PowerShellPath,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$App,
        [Parameter(Mandatory = $true)][int]$Port,
        [switch]$WarnOnFailure
    )

    $runner = Join-Path $RepoRoot "tools/Run-LinuxInteractiveDocker.ps1"
    if (-not $WarnOnFailure) {
        & $PowerShellPath -NoProfile -File $runner -Action Stop -App $App -Port $Port
        return
    }
    try {
        & $PowerShellPath -NoProfile -File $runner -Action Stop -App $App -Port $Port
        if ($LASTEXITCODE -ne 0) { throw "exit code $LASTEXITCODE" }
    } catch {
        Write-Warning "Could not stop harness-owned $App container on port ${Port}: $($_.Exception.Message)"
    }
}

function Read-PhysicalValidationFixtureValues {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$Action,
        [Parameter(Mandatory = $true)][string]$ArtifactPath
    )

    $lines = @(Invoke-PhysicalValidationFixture -ProjectPath $ProjectPath -Action $Action -ArtifactPath $ArtifactPath)
    return ConvertFrom-PhysicalValidationKeyValueLines -Lines $lines
}

# --- Runner-session lifecycle shared by the dedicated physical Linux validation runners. ---
# These helpers own the "Start the harness-owned container through Run-LinuxInteractiveDocker.ps1,
# run the probe, stop the container in finally" skeleton that the FreeP/FreeW physical validation
# scripts used to repeat inline. Callers dot-source ToolScriptSupport.ps1 (directly or through
# VisualEvidenceScriptSupport.ps1) first; the process runs through Invoke-ToolProcess.

function Get-PhysicalValidationRunnerStartArguments {
    # Builds the pwsh argument list for "Run-LinuxInteractiveDocker.ps1 -Action Start". Scenario-specific
    # runner parameters (-Host, -AppArgument, -AppEnvironment, -CupsDryRun ...) go in -ExtraArguments.
    param(
        [Parameter(Mandatory = $true)][string]$RunnerPath,
        [Parameter(Mandatory = $true)][string]$App,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][int]$Width,
        [Parameter(Mandatory = $true)][int]$Height,
        [Parameter(Mandatory = $true)][int]$Dpi,
        [Parameter(Mandatory = $true)][string]$MemoryLimit,
        [Parameter(Mandatory = $true)][string]$OutputDir,
        [string]$DocumentPath,
        [string[]]$ExtraArguments = @(),
        [string]$PublishDir,
        [switch]$SkipPublish,
        [switch]$SkipImageBuild,
        [switch]$Replace
    )

    $arguments = @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $RunnerPath,
        "-Action", "Start", "-App", $App, "-Port", "$Port",
        "-Width", "$Width", "-Height", "$Height", "-Dpi", "$Dpi",
        "-MemoryLimit", $MemoryLimit, "-OutputDir", $OutputDir
    )
    if ($PSBoundParameters.ContainsKey("DocumentPath")) { $arguments += @("-DocumentPath", $DocumentPath) }
    $arguments += @($ExtraArguments)
    if (-not [string]::IsNullOrWhiteSpace($PublishDir)) { $arguments += @("-PublishDir", $PublishDir) }
    if ($SkipPublish) { $arguments += "-SkipPublish" }
    if ($SkipImageBuild) { $arguments += "-SkipImageBuild" }
    if ($Replace) { $arguments += "-Replace" }
    return $arguments
}

function Start-PhysicalValidationRunnerSession {
    # Starts the harness-owned container. The caller records that it started (after this returns) so
    # the matching Stop-PhysicalValidationRunnerSession* runs from its finally block.
    param(
        [Parameter(Mandatory = $true)][string]$RunnerPath,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$App,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][int]$Width,
        [Parameter(Mandatory = $true)][int]$Height,
        [Parameter(Mandatory = $true)][int]$Dpi,
        [Parameter(Mandatory = $true)][string]$MemoryLimit,
        [Parameter(Mandatory = $true)][string]$OutputDir,
        [string]$DocumentPath,
        [string[]]$ExtraArguments = @(),
        [string]$PublishDir,
        [switch]$SkipPublish,
        [switch]$SkipImageBuild,
        [switch]$Replace,
        [switch]$OutputToHost
    )

    $startParameters = @{
        RunnerPath = $RunnerPath; App = $App; Port = $Port; Width = $Width; Height = $Height; Dpi = $Dpi
        MemoryLimit = $MemoryLimit; OutputDir = $OutputDir; ExtraArguments = $ExtraArguments
        PublishDir = $PublishDir; SkipPublish = $SkipPublish; SkipImageBuild = $SkipImageBuild; Replace = $Replace
    }
    if ($PSBoundParameters.ContainsKey("DocumentPath")) { $startParameters.DocumentPath = $DocumentPath }
    $arguments = Get-PhysicalValidationRunnerStartArguments @startParameters
    Invoke-ToolProcess -FilePath (Get-ToolPowerShellPath) -Arguments $arguments -WorkingDirectory $RepoRoot -OutputToHost:$OutputToHost
}

function Stop-PhysicalValidationRunnerSession {
    # Stops the harness-owned container started with Start-PhysicalValidationRunnerSession. A failed stop
    # throws (the mid-run form); use Stop-PhysicalValidationRunnerSessionSafely from finally blocks.
    param(
        [Parameter(Mandatory = $true)][string]$RunnerPath,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$App,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$OutputDir,
        [switch]$OutputToHost
    )

    Invoke-ToolProcess -FilePath (Get-ToolPowerShellPath) -Arguments @(
        "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", $RunnerPath,
        "-Action", "Stop", "-App", $App, "-Port", "$Port", "-OutputDir", $OutputDir
    ) -WorkingDirectory $RepoRoot -OutputToHost:$OutputToHost
}

function Stop-PhysicalValidationRunnerSessionSafely {
    # The finally-block form: stops only a container that was started and not retained by request, and
    # reports a failed stop as a warning "<WarningPrefix>: <error>" so it cannot mask the validation
    # error in flight. -OmitExceptionDetail emits "<WarningPrefix>." instead; -AnnounceRetained prints
    # the "Container retained by request" line when -KeepContainer suppressed the stop.
    param(
        [Parameter(Mandatory = $true)][string]$RunnerPath,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [Parameter(Mandatory = $true)][string]$App,
        [Parameter(Mandatory = $true)][int]$Port,
        [Parameter(Mandatory = $true)][string]$OutputDir,
        [Parameter(Mandatory = $true)][bool]$Started,
        [Parameter(Mandatory = $true)][string]$WarningPrefix,
        [switch]$KeepContainer,
        [switch]$AnnounceRetained,
        [switch]$OmitExceptionDetail,
        [switch]$OutputToHost
    )

    if ($Started -and -not $KeepContainer) {
        try {
            Stop-PhysicalValidationRunnerSession -RunnerPath $RunnerPath -RepoRoot $RepoRoot -App $App -Port $Port -OutputDir $OutputDir -OutputToHost:$OutputToHost
        } catch {
            if ($OmitExceptionDetail) { Write-Warning "${WarningPrefix}." }
            else { Write-Warning "${WarningPrefix}: $($_.Exception.Message)" }
        }
    } elseif ($Started -and $AnnounceRetained) {
        Write-Host "Container retained by request on port $Port."
    }
}

function Get-PhysicalValidationSessionContext {
    # Reads <OutputRoot>/<AppFolder>/current-session.json written by the generic runner plus the optional
    # ready.json beside the session; returns Session, SessionDirectory and Ready.
    param(
        [Parameter(Mandatory = $true)][string]$OutputRoot,
        [Parameter(Mandatory = $true)][string]$AppFolder,
        [Parameter(Mandatory = $true)][string]$DefaultWindowTitle
    )

    $sessionMetadataPath = Join-Path $OutputRoot "$AppFolder/current-session.json"
    if (-not (Test-Path -LiteralPath $sessionMetadataPath -PathType Leaf)) { throw "Generic runner did not write current session metadata: $sessionMetadataPath" }
    $session = Get-Content -LiteralPath $sessionMetadataPath -Raw | ConvertFrom-Json
    $sessionDirectory = [IO.Path]::GetFullPath([string]$session.sessionDirectory)
    if (-not (Test-Path -LiteralPath $sessionDirectory -PathType Container)) { throw "Session directory does not exist: $sessionDirectory" }
    $readyPath = Join-Path $sessionDirectory "ready.json"
    $ready = if (Test-Path -LiteralPath $readyPath -PathType Leaf) { Get-Content -LiteralPath $readyPath -Raw | ConvertFrom-Json } else { [pscustomobject]@{ windowId = ""; windowTitle = $DefaultWindowTitle } }
    return [pscustomobject]@{ Session = $session; SessionDirectory = $sessionDirectory; Ready = $ready }
}

function Invoke-PhysicalValidationDockerProbe {
    # Runs "docker <args>" from WorkingDirectory capturing stdout+stderr and the exit code. When -LogPath
    # is given the output is written there (UTF-8), or -EmptyOutputMessage when docker produced nothing.
    param(
        [Parameter(Mandatory = $true)][object[]]$DockerArguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [string]$LogPath,
        [string]$EmptyOutputMessage
    )

    Push-Location $WorkingDirectory
    try {
        $probeOutput = @(& docker @DockerArguments 2>&1)
        $probeExitCode = $LASTEXITCODE
    } finally {
        Pop-Location
    }
    if (-not [string]::IsNullOrEmpty($LogPath)) {
        if ($probeOutput.Count -gt 0) { $probeOutput | Set-Content -LiteralPath $LogPath -Encoding utf8 }
        else { $EmptyOutputMessage | Set-Content -LiteralPath $LogPath -Encoding utf8 }
    }
    return [pscustomobject]@{ Output = $probeOutput; ExitCode = $probeExitCode }
}
