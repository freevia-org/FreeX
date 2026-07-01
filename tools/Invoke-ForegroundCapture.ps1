param(
    [Parameter(Mandatory = $true)]
    [string]$Scenario,

    [string]$Output = "tools/foreground-captures",

    [string]$FreeXExe,

    [string]$AvaloniaExe
)

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
