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
