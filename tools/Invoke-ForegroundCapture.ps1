param(
    [string]$Scenario,

    [string]$Output = "tools/foreground-captures",

    [string]$FreeXExe,

    [string]$AvaloniaExe,

    [switch]$EnvironmentPreflight
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function New-PreflightCheck {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][bool]$Passed,
        [Parameter(Mandatory = $true)][string]$BlockerCategory,
        [Parameter(Mandatory = $true)][string]$Message,
        [hashtable]$Details = @{}
    )

    [ordered]@{
        name = $Name
        passed = $Passed
        blockerCategory = if ($Passed) { "none" } else { $BlockerCategory }
        message = $Message
        details = $Details
    }
}

function Test-ExcelCom {
    $excel = $null
    try {
        $excel = New-Object -ComObject Excel.Application
        return New-PreflightCheck `
            -Name "excel-com" `
            -Passed $true `
            -BlockerCategory "excel-com-unavailable" `
            -Message "Microsoft Excel COM can be created and quit."
    }
    catch {
        return New-PreflightCheck `
            -Name "excel-com" `
            -Passed $false `
            -BlockerCategory "excel-com-unavailable" `
            -Message "Microsoft Excel COM could not be created. Install/register desktop Excel before running Excel foreground captures." `
            -Details @{ error = $_.Exception.Message }
    }
    finally {
        if ($null -ne $excel) {
            try {
                $excel.Quit()
            }
            catch {
            }

            [Runtime.InteropServices.Marshal]::ReleaseComObject($excel) | Out-Null
        }
    }
}

function Test-ForegroundCaptureEnvironment {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $freeXCandidate = if ([string]::IsNullOrWhiteSpace($FreeXExe)) {
        Join-Path $repoRoot "src\FreeX.App.Host\bin\Release\net10.0-windows10.0.19041.0\FreeX.App.Host.exe"
    }
    else {
        Resolve-ToolRepoPath -Path $FreeXExe -RepoRoot $repoRoot
    }
    $avaloniaCandidate = if ([string]::IsNullOrWhiteSpace($AvaloniaExe)) {
        Join-Path $repoRoot "src\FreeX.App.Avalonia\bin\Release\net10.0\FreeX.exe"
    }
    else {
        Resolve-ToolRepoPath -Path $AvaloniaExe -RepoRoot $repoRoot
    }

    $checks = @(
        New-PreflightCheck `
            -Name "windows-desktop" `
            -Passed ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) `
            -BlockerCategory "windows-desktop-required" `
            -Message "Foreground captures require a Windows desktop session." `
            -Details @{ platform = [Environment]::OSVersion.Platform.ToString() }
        New-PreflightCheck `
            -Name "interactive-session" `
            -Passed ([Environment]::UserInteractive) `
            -BlockerCategory "foreground-focus-unavailable" `
            -Message "Foreground captures must run from an unlocked interactive desktop session." `
            -Details @{ sessionId = [Diagnostics.Process]::GetCurrentProcess().SessionId }
        New-PreflightCheck `
            -Name "wpf-release-exe" `
            -Passed (Test-Path -LiteralPath $freeXCandidate -PathType Leaf) `
            -BlockerCategory "freex-release-exe-missing" `
            -Message "Release WPF host executable must exist for FreeX WPF opened-state captures." `
            -Details @{ path = $freeXCandidate }
        New-PreflightCheck `
            -Name "avalonia-release-exe" `
            -Passed (Test-Path -LiteralPath $avaloniaCandidate -PathType Leaf) `
            -BlockerCategory "avalonia-release-exe-missing" `
            -Message "Release Avalonia executable must exist for Avalonia opened-state captures." `
            -Details @{ path = $avaloniaCandidate }
        Test-ExcelCom
    )

    $blocked = @($checks | Where-Object { -not $_.passed })
    [ordered]@{
        schema = "freex.foreground-capture.environment-preflight.v1"
        generatedBy = "tools/Invoke-ForegroundCapture.ps1"
        status = if ($blocked.Count -eq 0) { "ready" } else { "blocked" }
        blockerCategories = @($blocked | Group-Object -Property { $_.blockerCategory } | Sort-Object -Property Name | ForEach-Object {
                [ordered]@{
                    category = [string]$_.Name
                    count = [int]$_.Count
                }
            })
        checks = @($checks)
    }
}

if ($EnvironmentPreflight) {
    $preflight = Test-ForegroundCaptureEnvironment
    $preflight | ConvertTo-Json -Depth 8
    exit $(if ($preflight.status -eq "ready") { 0 } else { 1 })
}

if ([string]::IsNullOrWhiteSpace($Scenario)) {
    Write-Error "Missing -Scenario. Use -EnvironmentPreflight to inspect desktop/Excel/Release executable readiness."
    exit 2
}

$argsList = @(
    "run",
    "--project",
    "tools/FreeX.ForegroundCapture/FreeX.ForegroundCapture.csproj",
    "--configuration",
    "Release",
    "--",
    "--scenario",
    $Scenario,
    "--output",
    $Output
)

if (-not [string]::IsNullOrWhiteSpace($FreeXExe)) {
    $argsList += @("--freex-exe", $FreeXExe)
}

if (-not [string]::IsNullOrWhiteSpace($AvaloniaExe)) {
    $argsList += @("--avalonia-exe", $AvaloniaExe)
}

dotnet @argsList
exit $LASTEXITCODE
