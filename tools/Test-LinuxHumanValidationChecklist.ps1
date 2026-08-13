param(
    [string]$ChecklistPath = "docs/release/linux-human-validation-checklist.md",
    [string]$ExpectedRunId,
    [string]$ExpectedRunAttempt,
    [string]$ManifestPath = "artifacts/linux-human-validation-manifest.json"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$validationErrors = New-Object System.Collections.Generic.List[string]

. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

# Human gates a tester must record before a Linux build can be a public-preview candidate.
# Mirrors the human portion of docs/release/linux-public-preview-checklist.md.
$RequiredGates = @(
    "install_tarball",
    "appimage_launch",
    "desktop_association",
    "file_open",
    "file_dialogs",
    "clipboard",
    "drag_drop",
    "x11_session",
    "wayland_session",
    "keyboard_only",
    "screen_reader_orca",
    "external_links",
    "known_issues_reviewed"
)

function Add-ValidationError {
    param([Parameter(Mandatory = $true)][string]$Message)

    Add-ToolValidationError -Errors $validationErrors -Message $Message -GitHubTitle "Linux human validation"
}

$path = Resolve-InputPath -Path $ChecklistPath -RepoRoot $repoRoot
if (-not (Test-Path -LiteralPath $path)) {
    Add-ValidationError "Checklist '$path' was not found."
    Write-Host "Linux human validation FAILED."
    exit 1
}

# Parse the machine-readable record: `key: value` lines that follow the sentinel comment.
$record = @{}
$inRecord = $false
foreach ($line in Get-Content -LiteralPath $path) {
    if ($line -match "freex-linux-validation") {
        $inRecord = $true
        continue
    }
    if (-not $inRecord) { continue }
    if ($line -match '^\s*```') {
        if ($record.Count -gt 0) { break }
        continue
    }

    $separator = $line.IndexOf(":")
    if ($separator -lt 1) { continue }
    $key = $line.Substring(0, $separator).Trim()
    $value = $line.Substring($separator + 1).Trim().ToLowerInvariant()
    if ($key.Length -gt 0) { $record[$key] = $value }
}

if ($record.Count -eq 0) {
    Add-ValidationError "No '<!-- freex-linux-validation -->' record block with key: value lines was found."
}

foreach ($gate in $RequiredGates) {
    if (-not $record.ContainsKey($gate)) {
        Add-ValidationError "Required gate '$gate' is missing from the validation record."
        continue
    }

    $value = $record[$gate]
    if ($value -ne "pass" -and $value -ne "na") {
        Add-ValidationError "Gate '$gate' must be 'pass' or 'na' (was '$value')."
    }
}

if ($ExpectedRunId) {
    if (($record["run_id"]) -ne $ExpectedRunId.ToLowerInvariant()) {
        Add-ValidationError "Validation record run_id '$($record["run_id"])' does not match expected '$ExpectedRunId'."
    }
}
if ($ExpectedRunAttempt) {
    if (($record["run_attempt"]) -ne $ExpectedRunAttempt.ToLowerInvariant()) {
        Add-ValidationError "Validation record run_attempt '$($record["run_attempt"])' does not match expected '$ExpectedRunAttempt'."
    }
}

$manifest = [ordered]@{
    schema = "io.github.tony-xmelon.freex.linux-human-validation.v1"
    checklist = $ChecklistPath
    runtime = $record["runtime"]
    run_id = $record["run_id"]
    run_attempt = $record["run_attempt"]
    gates = ($RequiredGates | ForEach-Object { [ordered]@{ gate = $_; value = $record[$_] } })
    status = if ($validationErrors.Count -eq 0) { "validated" } else { "blocked" }
}

$manifestFull = Resolve-InputPath -Path $ManifestPath -RepoRoot $repoRoot
$manifestDir = Split-Path -Parent $manifestFull
if (-not (Test-Path -LiteralPath $manifestDir)) {
    New-Item -ItemType Directory -Path $manifestDir -Force | Out-Null
}
$manifest | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $manifestFull -Encoding ascii

if ($validationErrors.Count -gt 0) {
    Write-Host ""
    Write-Host "Linux human validation FAILED with $($validationErrors.Count) issue(s)."
    exit 1
}

Write-Host "Linux human validation PASSED ($($RequiredGates.Count) gates)."
exit 0
