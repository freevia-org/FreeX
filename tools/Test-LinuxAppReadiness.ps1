param()

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$validationErrors = New-Object System.Collections.Generic.List[string]

function Add-ValidationError {
    param([Parameter(Mandatory = $true)][string]$Message)

    $validationErrors.Add($Message)
    if ($env:GITHUB_ACTIONS -eq "true") {
        $escaped = $Message.Replace("%", "%25").Replace("`r", "%0D").Replace("`n", "%0A")
        Write-Host "::error title=Linux app readiness::$escaped"
    }
    Write-Error $Message -ErrorAction Continue
}

function Assert-True {
    param(
        [Parameter(Mandatory = $true)][bool]$Condition,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if (-not $Condition) {
        Add-ValidationError $Message
    }
}

function Get-RepoFile {
    param([Parameter(Mandatory = $true)][string[]]$Segments)

    $parts = [string[]](@($repoRoot) + $Segments)
    return [System.IO.Path]::Combine($parts)
}

function Assert-FileContains {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string[]]$Needles
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        Add-ValidationError "Expected file '$Path' was not found."
        return
    }

    $content = Get-Content -LiteralPath $Path -Raw
    foreach ($needle in $Needles) {
        Assert-True ($content.Contains($needle)) "File '$Path' is missing expected content: $needle"
    }
}

$packagingDir = Get-RepoFile @("src", "FreeX.App.Avalonia", "Packaging", "linux")
$appId = "io.github.tony-xmelon.freex"

# Packaging assets.
$desktopFile = Join-Path $packagingDir "$appId.desktop"
Assert-FileContains -Path $desktopFile -Needles @(
    "[Desktop Entry]",
    "Type=Application",
    "Name=FreeX",
    "Exec=freex %F",
    "Icon=$appId",
    "Categories=Office;Spreadsheet;",
    "application/vnd.freex.workbook+json",
    "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
    "text/csv",
    "StartupWMClass=FreeX"
)

$mimeFile = Join-Path $packagingDir "$appId.xml"
Assert-FileContains -Path $mimeFile -Needles @(
    "http://www.freedesktop.org/standards/shared-mime-info",
    "application/vnd.freex.workbook+json",
    '<glob pattern="*.fxl"/>'
)

$iconFile = Join-Path $packagingDir "$appId.svg"
Assert-True (Test-Path -LiteralPath $iconFile) "Linux app icon '$iconFile' was not found."

$packageEntrypoint = Join-Path $packagingDir "package-linux-app.sh"
Assert-FileContains -Path $packageEntrypoint -Needles @(
    "package-linux.sh",
    "--operation tarball"
)

$packageScript = Get-RepoFile @("tools", "packaging", "linux", "package-linux.sh")
Assert-FileContains -Path $packageScript -Needles @(
    "--runtime",
    "--published",
    "--output",
    'lib/$library_dir',
    "install.sh",
    "uninstall.sh",
    "tar -C",
    'library_dir',
    'binary_name'
)

$appImageEntrypoint = Join-Path $packagingDir "build-appimage.sh"
Assert-FileContains -Path $appImageEntrypoint -Needles @(
    "package-linux.sh",
    "--operation appimage"
)

$appImageScript = Get-RepoFile @("tools", "packaging", "linux", "package-linux.sh")
Assert-FileContains -Path $appImageScript -Needles @(
    "AppDir",
    "AppRun",
    "appimagetool",
    "linux-x64) arch=`"x86_64`"",
    "linux-arm64) arch=`"aarch64`""
)

# Avalonia project keeps the platform-neutral launch-smoke alias.
$smokeSource = Get-RepoFile @("tools", "FreeX.Validation.Avalonia", "MacOsLaunchSmoke.cs")
Assert-FileContains -Path $smokeSource -Needles @(
    'public const string NeutralArgument = "--launch-smoke";',
    'public const string NeutralDiagnosticsDirectoryArgument = "--launch-smoke-diagnostics-dir";'
)

$packagedProductLaunchProbe = Get-RepoFile @("tools", "Run-PackagedProductLaunchProbe.sh")
Assert-FileContains -Path $packagedProductLaunchProbe -Needles @(
    '"$executable" "${app_arguments[@]}" >"$log_path" 2>&1 &',
    'grep -R -F -q "$readiness_marker" "$readiness_root"',
    'process_is_active "$probe_pid"',
    'kill "$probe_pid"',
    'packaged_product_launch_status=passed',
    'packaged_product_executable=$executable'
)

# Linux workflow markers.
$workflow = Get-RepoFile @(".github", "workflows", "linux-app.yml")
Assert-FileContains -Path $workflow -Needles @(
    "name: Linux App Preview",
    "runtime: linux-x64",
    "runtime: linux-arm64",
    "runner: ubuntu-latest",
    "runner: ubuntu-24.04-arm",
    "dotnet publish src/FreeX.App.Avalonia/FreeX.App.Avalonia.csproj",
    "dotnet publish tools/FreeX.Validation.Avalonia/FreeX.Validation.Avalonia.csproj",
    "--self-contained true",
    "-p:UseAppHost=true",
    "--packaging-smoke",
    '"$validation_published/FreeX.Validation.Avalonia" --packaging-smoke',
    "bash tools/Run-PackagedProductLaunchProbe.sh",
    '--executable "$published/FreeX"',
    'grep -Fqx "packaged_product_launch_status=passed" "$packaged_product_launch_report"',
    'grep -Fqx "packaged_product_executable=$published/FreeX" "$packaged_product_launch_report"',
    "xvfb-run -a",
    "--launch-smoke",
    "desktop-file-validate",
    "package-linux-app.sh",
    "sha256sum -c",
    "packaging_smoke_status=passed",
    "launch_smoke_status=passed",
    "Test-LinuxPublicPreviewReadiness.ps1",
    "linux-preview-readiness"
)

# The macOS-only signing/notarization machinery must not leak into the Linux lane.
if (Test-Path -LiteralPath $workflow) {
    $workflowContent = Get-Content -LiteralPath $workflow -Raw
    $productProbeIndex = $workflowContent.IndexOf('bash tools/Run-PackagedProductLaunchProbe.sh', [System.StringComparison]::Ordinal)
    $launchPassedIndex = $workflowContent.IndexOf('echo "launch_smoke_status=passed"', [System.StringComparison]::Ordinal)
    Assert-True ($productProbeIndex -ge 0 -and $productProbeIndex -lt $launchPassedIndex) "Linux workflow must exercise the published product apphost before recording launch_smoke_status=passed."
    foreach ($forbidden in @("codesign", "notarytool", "MACOS_CODESIGN", "lsregister", "spctl")) {
        Assert-True (-not $workflowContent.Contains($forbidden)) "Linux workflow must not contain macOS-only token: $forbidden"
    }
}

$releaseWorkflow = Get-RepoFile @(".github", "workflows", "linux-release.yml")
Assert-FileContains -Path $releaseWorkflow -Needles @(
    '"$validation_published/FreeX.Validation.Avalonia" --packaging-smoke',
    "bash tools/Run-PackagedProductLaunchProbe.sh",
    '--executable "$published/FreeX"',
    'packaged_product_launch_status=passed'
)

# Readiness validator present.
$readinessTool = Get-RepoFile @("tools", "Test-LinuxPublicPreviewReadiness.ps1")
Assert-True (Test-Path -LiteralPath $readinessTool) "tools/Test-LinuxPublicPreviewReadiness.ps1 was not found."

if ($validationErrors.Count -gt 0) {
    Write-Host ""
    Write-Host "Linux app readiness FAILED with $($validationErrors.Count) issue(s)."
    exit 1
}

Write-Host "Linux app readiness PASSED."
