param(
    [string]$ArtifactRoot = "artifacts",
    [string[]]$Runtimes = @("osx-arm64", "osx-x64"),
    [switch]$DistributionCandidate,
    [switch]$RequireSeparateDiagnosticsArtifact
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$validationErrors = New-Object System.Collections.Generic.List[string]

function Resolve-InputPath {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    $currentDirectoryCandidate = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $Path))
    if (Test-Path -LiteralPath $currentDirectoryCandidate) {
        return $currentDirectoryCandidate
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $Path))
}

function Add-ValidationError {
    param([Parameter(Mandatory = $true)][string]$Message)

    $validationErrors.Add($Message)
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

function Assert-FileExists {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        Add-ValidationError "$Label was not found: $Path"
        return $false
    }

    return $true
}

function Assert-ContainsText {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Needle,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if ($Text.IndexOf($Needle, [System.StringComparison]::Ordinal) -lt 0) {
        Add-ValidationError $Message
    }
}

function Get-ExpectedFileNames {
    param([Parameter(Mandatory = $true)][string]$Runtime)

    return [ordered]@{
        Zip = "freex-$Runtime-macos-app.zip"
        Checksum = "freex-$Runtime-macos-app.zip.sha256"
        Evidence = "freex-$Runtime-macos-evidence.txt"
        PackagingSmoke = "freex-$Runtime-macos-packaging-smoke.log"
        LaunchSmoke = "freex-$Runtime-macos-launch-smoke.txt"
        OpenWithSmoke = "freex-$Runtime-macos-open-with-launch-smoke.txt"
        DefaultOpenSmoke = "freex-$Runtime-macos-default-open-launch-smoke.txt"
        NotarizationLog = "freex-$Runtime-macos-notarization.log"
        TesterInstructions = "freex-$Runtime-macos-tester-instructions.md"
    }
}

function Get-KeyValueMap {
    param([Parameter(Mandatory = $true)][string]$Path)

    $map = @{}
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return $map
    }

    foreach ($line in Get-Content -LiteralPath $Path) {
        if ($line -match "^\s*([A-Za-z0-9_.-]+)\s*=\s*(.*?)\s*$") {
            $key = $Matches[1]
            $value = $Matches[2]
            if (-not $map.ContainsKey($key)) {
                $map[$key] = New-Object System.Collections.Generic.List[string]
            }

            $map[$key].Add($value)
        }
    }

    return $map
}

function Get-KeyValues {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Map,
        [Parameter(Mandatory = $true)][string]$Key
    )

    if (-not $Map.ContainsKey($Key)) {
        return @()
    }

    return @($Map[$Key] | ForEach-Object { [string]$_ })
}

function Get-LatestKeyValue {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Map,
        [Parameter(Mandatory = $true)][string]$Key
    )

    $values = @(Get-KeyValues -Map $Map -Key $Key)
    if ($values.Count -eq 0) {
        return $null
    }

    return $values[$values.Count - 1]
}

function Assert-KeyPresent {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Map,
        [Parameter(Mandatory = $true)][string]$Key,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $value = Get-LatestKeyValue -Map $Map -Key $Key
    Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($value)) -Message "$Label must include '$Key'."
}

function Assert-KeyEquals {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Map,
        [Parameter(Mandatory = $true)][string]$Key,
        [Parameter(Mandatory = $true)][string]$ExpectedValue,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $values = @(Get-KeyValues -Map $Map -Key $Key)
    if ($values.Count -eq 0) {
        Add-ValidationError "$Label must include '$Key=$ExpectedValue'."
        return
    }

    if ($values -notcontains $ExpectedValue) {
        Add-ValidationError "$Label must include '$Key=$ExpectedValue'. Actual value(s): $($values -join ', ')."
    }
}

function Assert-KeyMatches {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Map,
        [Parameter(Mandatory = $true)][string]$Key,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $values = @(Get-KeyValues -Map $Map -Key $Key)
    if ($values.Count -eq 0) {
        Add-ValidationError "$Label must include '$Key'."
        return
    }

    foreach ($value in $values) {
        if ($value -match $Pattern) {
            return
        }
    }

    Add-ValidationError "$Label '$Key' must match /$Pattern/. Actual value(s): $($values -join ', ')."
}

function Assert-KeyPositiveInteger {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Map,
        [Parameter(Mandatory = $true)][string]$Key,
        [Parameter(Mandatory = $true)][int]$Minimum,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $value = Get-LatestKeyValue -Map $Map -Key $Key
    if ([string]::IsNullOrWhiteSpace($value)) {
        Add-ValidationError "$Label must include '$Key'."
        return
    }

    $parsed = 0
    if (-not [int]::TryParse($value, [ref]$parsed) -or $parsed -lt $Minimum) {
        Add-ValidationError "$Label '$Key' must be at least $Minimum, but was '$value'."
    }
}

function Find-RuntimeBundleDirectories {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Runtime
    )

    $names = Get-ExpectedFileNames -Runtime $Runtime
    $directories = New-Object System.Collections.Generic.List[string]
    $candidateFiles = @()
    $candidateFiles += @(Get-ChildItem -LiteralPath $Root -Recurse -File -Filter $names.Zip -ErrorAction SilentlyContinue)
    $candidateFiles += @(Get-ChildItem -LiteralPath $Root -Recurse -File -Filter $names.Evidence -ErrorAction SilentlyContinue)

    foreach ($file in $candidateFiles) {
        if ($null -eq $file.Directory) {
            continue
        }

        if (-not $directories.Contains($file.Directory.FullName)) {
            $directories.Add($file.Directory.FullName)
        }
    }

    return @($directories | Sort-Object)
}

function Test-ArtifactFileSet {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$Runtime,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][bool]$RequireZip
    )

    $names = Get-ExpectedFileNames -Runtime $Runtime
    $requiredKeys = @("Evidence", "PackagingSmoke", "LaunchSmoke", "OpenWithSmoke", "DefaultOpenSmoke", "NotarizationLog", "TesterInstructions")
    if ($RequireZip) {
        $requiredKeys = @("Zip", "Checksum") + $requiredKeys
    }

    $allPresent = $true
    foreach ($key in $requiredKeys) {
        $path = Join-Path $Directory $names[$key]
        if (-not (Assert-FileExists -Path $path -Label "$Label $key")) {
            $allPresent = $false
        }
    }

    return $allPresent
}

function Get-FileTextOrEmpty {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        return ""
    }

    return Get-Content -LiteralPath $Path -Raw
}

function Test-ChecksumEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$BundleDirectory,
        [Parameter(Mandatory = $true)][string]$Runtime,
        [Parameter(Mandatory = $true)][hashtable]$Evidence
    )

    $names = Get-ExpectedFileNames -Runtime $Runtime
    $zipPath = Join-Path $BundleDirectory $names.Zip
    $checksumPath = Join-Path $BundleDirectory $names.Checksum

    if (-not (Test-Path -LiteralPath $zipPath -PathType Leaf) -or -not (Test-Path -LiteralPath $checksumPath -PathType Leaf)) {
        return
    }

    $actualHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $checksumText = Get-FileTextOrEmpty -Path $checksumPath
    Assert-ContainsText -Text $checksumText -Needle $names.Zip -Message "$Runtime checksum file must name $($names.Zip)."

    $checksumHash = $null
    if ($checksumText -match "([0-9a-fA-F]{64})") {
        $checksumHash = $Matches[1].ToLowerInvariant()
    }

    Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($checksumHash)) -Message "$Runtime checksum file must contain a SHA-256 hash."
    if (-not [string]::IsNullOrWhiteSpace($checksumHash)) {
        Assert-True -Condition ($checksumHash -eq $actualHash) -Message "$Runtime checksum file hash must match $($names.Zip)."
    }

    Assert-KeyEquals -Map $Evidence -Key "zip_sha256" -ExpectedValue $actualHash -Label "$Runtime evidence"
    Assert-KeyMatches -Map $Evidence -Key "zip_sha256" -Pattern "^[0-9a-fA-F]{64}$" -Label "$Runtime evidence"
}

function Test-ChannelEvidence {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Evidence,
        [Parameter(Mandatory = $true)][string]$Runtime
    )

    Assert-KeyEquals -Map $Evidence -Key "runtime" -ExpectedValue $Runtime -Label "$Runtime evidence"
    Assert-KeyPresent -Map $Evidence -Key "artifact_channel" -Label "$Runtime evidence"
    Assert-KeyPresent -Map $Evidence -Key "distribution_candidate" -Label "$Runtime evidence"
    Assert-KeyPresent -Map $Evidence -Key "distribution_contract" -Label "$Runtime evidence"
    Assert-KeyPresent -Map $Evidence -Key "distribution_readiness" -Label "$Runtime evidence"
    Assert-KeyEquals -Map $Evidence -Key "codesign_verified" -ExpectedValue "true" -Label "$Runtime evidence"
    Assert-KeyPresent -Map $Evidence -Key "codesign_mode" -Label "$Runtime evidence"
    Assert-KeyPresent -Map $Evidence -Key "notarization_status" -Label "$Runtime evidence"
    Assert-KeyPresent -Map $Evidence -Key "stapler_validated" -Label "$Runtime evidence"

    $artifactChannel = Get-LatestKeyValue -Map $Evidence -Key "artifact_channel"
    $distributionCandidateValue = Get-LatestKeyValue -Map $Evidence -Key "distribution_candidate"
    $isDistributionCandidateArtifact = $DistributionCandidate.IsPresent -or
        $artifactChannel -eq "distribution-candidate" -or
        $distributionCandidateValue -eq "true"

    if ($isDistributionCandidateArtifact) {
        Assert-KeyEquals -Map $Evidence -Key "artifact_channel" -ExpectedValue "distribution-candidate" -Label "$Runtime distribution-candidate evidence"
        Assert-KeyEquals -Map $Evidence -Key "distribution_candidate" -ExpectedValue "true" -Label "$Runtime distribution-candidate evidence"
        Assert-KeyEquals -Map $Evidence -Key "distribution_contract" -ExpectedValue "distribution_candidate_requires_developer_id_notarization_stapling" -Label "$Runtime distribution-candidate evidence"
        Assert-KeyEquals -Map $Evidence -Key "distribution_readiness" -ExpectedValue "distribution_candidate_ready" -Label "$Runtime distribution-candidate evidence"
        Assert-KeyEquals -Map $Evidence -Key "codesign_mode" -ExpectedValue "developer-id" -Label "$Runtime distribution-candidate evidence"
        Assert-KeyEquals -Map $Evidence -Key "notarization_status" -ExpectedValue "accepted" -Label "$Runtime distribution-candidate evidence"
        Assert-KeyEquals -Map $Evidence -Key "stapler_validated" -ExpectedValue "true" -Label "$Runtime distribution-candidate evidence"
    }
    else {
        Assert-KeyEquals -Map $Evidence -Key "artifact_channel" -ExpectedValue "internal-preview" -Label "$Runtime internal-preview evidence"
        Assert-KeyEquals -Map $Evidence -Key "distribution_candidate" -ExpectedValue "false" -Label "$Runtime internal-preview evidence"
        Assert-KeyEquals -Map $Evidence -Key "distribution_contract" -ExpectedValue "internal_preview_not_for_distribution_notarization_optional" -Label "$Runtime internal-preview evidence"
        Assert-KeyEquals -Map $Evidence -Key "distribution_readiness" -ExpectedValue "internal_preview_not_for_distribution" -Label "$Runtime internal-preview evidence"
    }

    return $isDistributionCandidateArtifact
}

function Test-PackagingSmoke {
    param(
        [Parameter(Mandatory = $true)][string]$PackagingSmokePath,
        [Parameter(Mandatory = $true)][hashtable]$Evidence,
        [Parameter(Mandatory = $true)][string]$Runtime
    )

    $smokeText = Get-FileTextOrEmpty -Path $PackagingSmokePath
    Assert-ContainsText -Text $smokeText -Needle "Packaging smoke opened" -Message "$Runtime packaging smoke must record startup open."
    Assert-ContainsText -Text $smokeText -Needle "macOS Preview Workbook" -Message "$Runtime packaging smoke must exercise the preview workbook."
    Assert-ContainsText -Text $smokeText -Needle "edited, saved, and reopened" -Message "$Runtime packaging smoke must record save/reopen."
    Assert-ContainsText -Text $smokeText -Needle "format_cells_style_roundtrip=true" -Message "$Runtime packaging smoke must record Format Cells style roundtrip."
    Assert-KeyEquals -Map $Evidence -Key "smoke_status" -ExpectedValue "passed" -Label "$Runtime evidence"
    Assert-KeyEquals -Map $Evidence -Key "format_cells_style_roundtrip" -ExpectedValue "true" -Label "$Runtime evidence"
    Assert-KeyPositiveInteger -Map $Evidence -Key "format_cells_style_roundtrip_count" -Minimum 2 -Label "$Runtime evidence"

    $roundTripMatches = [System.Text.RegularExpressions.Regex]::Matches($smokeText, "format_cells_style_roundtrip=true")
    Assert-True -Condition ($roundTripMatches.Count -ge 2) -Message "$Runtime packaging smoke must record at least two Format Cells style roundtrip confirmations."
}

function Test-LaunchSmoke {
    param(
        [Parameter(Mandatory = $true)][string]$LaunchSmokePath,
        [Parameter(Mandatory = $true)][string]$Runtime
    )

    $launch = Get-KeyValueMap -Path $LaunchSmokePath
    Assert-KeyEquals -Map $launch -Key "macos_launch_smoke" -ExpectedValue "passed" -Label "$Runtime LaunchServices smoke"
    Assert-KeyEquals -Map $launch -Key "window_shown" -ExpectedValue "true" -Label "$Runtime LaunchServices smoke"
    Assert-KeyMatches -Map $launch -Key "opened_source_path" -Pattern ([System.Text.RegularExpressions.Regex]::Escape("freex-$Runtime-launch.csv")) -Label "$Runtime LaunchServices smoke"
    Assert-KeyPositiveInteger -Map $launch -Key "viewport_rows" -Minimum 1 -Label "$Runtime LaunchServices smoke"
    Assert-KeyPositiveInteger -Map $launch -Key "viewport_columns" -Minimum 1 -Label "$Runtime LaunchServices smoke"
    Assert-KeyEquals -Map $launch -Key "native_open_recent_menu_item" -ExpectedValue "true" -Label "$Runtime LaunchServices smoke"
    Assert-KeyPositiveInteger -Map $launch -Key "native_open_recent_item_count" -Minimum 1 -Label "$Runtime LaunchServices smoke"
    Assert-KeyEquals -Map $launch -Key "live_command_key_smoke_required" -ExpectedValue "true" -Label "$Runtime command key smoke"
    Assert-KeyEquals -Map $launch -Key "live_command_key_smoke" -ExpectedValue "passed" -Label "$Runtime command key smoke"
    Assert-KeyEquals -Map $launch -Key "live_command_key_smoke_attempted" -ExpectedValue "true" -Label "$Runtime command key smoke"
    Assert-KeyEquals -Map $launch -Key "live_command_key_smoke_ready" -ExpectedValue "true" -Label "$Runtime command key smoke"
    Assert-KeyEquals -Map $launch -Key "live_cmd_bold_state_changed" -ExpectedValue "true" -Label "$Runtime command key smoke"
    Assert-KeyEquals -Map $launch -Key "live_cmd_italic_state_changed" -ExpectedValue "true" -Label "$Runtime command key smoke"
    Assert-KeyEquals -Map $launch -Key "live_cmd_underline_state_changed" -ExpectedValue "true" -Label "$Runtime command key smoke"
}

function Test-OpenWithSmoke {
    param(
        [Parameter(Mandatory = $true)][string]$OpenWithSmokePath,
        [Parameter(Mandatory = $true)][string]$Runtime
    )

    $openWith = Get-KeyValueMap -Path $OpenWithSmokePath
    Assert-KeyEquals -Map $openWith -Key "macos_launch_smoke" -ExpectedValue "passed" -Label "$Runtime Open-With smoke"
    Assert-KeyEquals -Map $openWith -Key "window_shown" -ExpectedValue "true" -Label "$Runtime Open-With smoke"
    Assert-KeyMatches -Map $openWith -Key "opened_source_path" -Pattern ([System.Text.RegularExpressions.Regex]::Escape("freex-$Runtime-open-with.csv")) -Label "$Runtime Open-With smoke"
    Assert-KeyPositiveInteger -Map $openWith -Key "viewport_rows" -Minimum 1 -Label "$Runtime Open-With smoke"
    Assert-KeyPositiveInteger -Map $openWith -Key "viewport_columns" -Minimum 1 -Label "$Runtime Open-With smoke"
    Assert-KeyEquals -Map $openWith -Key "native_open_recent_menu_item" -ExpectedValue "true" -Label "$Runtime Open-With smoke"
    Assert-KeyPositiveInteger -Map $openWith -Key "native_open_recent_item_count" -Minimum 1 -Label "$Runtime Open-With smoke"
}

function Test-DefaultOpenSmoke {
    param(
        [Parameter(Mandatory = $true)][string]$DefaultOpenSmokePath,
        [Parameter(Mandatory = $true)][string]$Runtime
    )

    $defaultOpen = Get-KeyValueMap -Path $DefaultOpenSmokePath
    Assert-KeyEquals -Map $defaultOpen -Key "macos_launch_smoke" -ExpectedValue "passed" -Label "$Runtime .fxl default-open smoke"
    Assert-KeyEquals -Map $defaultOpen -Key "window_shown" -ExpectedValue "true" -Label "$Runtime .fxl default-open smoke"
    Assert-KeyMatches -Map $defaultOpen -Key "opened_source_path" -Pattern ([System.Text.RegularExpressions.Regex]::Escape("freex-$Runtime-default-open.fxl")) -Label "$Runtime .fxl default-open smoke"
    Assert-KeyPositiveInteger -Map $defaultOpen -Key "viewport_rows" -Minimum 1 -Label "$Runtime .fxl default-open smoke"
    Assert-KeyPositiveInteger -Map $defaultOpen -Key "viewport_columns" -Minimum 1 -Label "$Runtime .fxl default-open smoke"
    Assert-KeyEquals -Map $defaultOpen -Key "native_open_recent_menu_item" -ExpectedValue "true" -Label "$Runtime .fxl default-open smoke"
    Assert-KeyPositiveInteger -Map $defaultOpen -Key "native_open_recent_item_count" -Minimum 1 -Label "$Runtime .fxl default-open smoke"
    Assert-KeyEquals -Map $defaultOpen -Key "launchservices_default_open_attempted" -ExpectedValue "true" -Label "$Runtime .fxl default-open boundary"
    Assert-KeyEquals -Map $defaultOpen -Key "launchservices_default_open_app_override" -ExpectedValue "false" -Label "$Runtime .fxl default-open boundary"
    Assert-KeyEquals -Map $defaultOpen -Key "launchservices_default_open_document_extension" -ExpectedValue "fxl" -Label "$Runtime .fxl default-open boundary"
    Assert-KeyEquals -Map $defaultOpen -Key "launchservices_default_open_boundary" -ExpectedValue "ci_open_document_without_app_override_not_finder_double_click" -Label "$Runtime .fxl default-open boundary"
}

function Test-NotarizationLog {
    param(
        [Parameter(Mandatory = $true)][string]$NotarizationLogPath,
        [Parameter(Mandatory = $true)][bool]$IsDistributionCandidateArtifact,
        [Parameter(Mandatory = $true)][string]$Runtime
    )

    $notaryText = Get-FileTextOrEmpty -Path $NotarizationLogPath
    Assert-ContainsText -Text $notaryText -Needle "artifact_channel=" -Message "$Runtime notarization log must record artifact_channel."
    Assert-ContainsText -Text $notaryText -Needle "distribution_contract=" -Message "$Runtime notarization log must record distribution_contract."
    if ($IsDistributionCandidateArtifact) {
        if ($notaryText.IndexOf('"status"', [System.StringComparison]::OrdinalIgnoreCase) -lt 0 -or
            $notaryText.IndexOf("Accepted", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            Add-ValidationError "$Runtime distribution-candidate notarization log must include an accepted notary submission."
        }

        if ($notaryText.IndexOf("staple", [System.StringComparison]::OrdinalIgnoreCase) -lt 0 -and
            $notaryText.IndexOf("stapler", [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
            Add-ValidationError "$Runtime distribution-candidate notarization log must include stapler evidence."
        }
    }
}

function Test-TesterInstructions {
    param(
        [Parameter(Mandatory = $true)][string]$InstructionsPath,
        [Parameter(Mandatory = $true)][string]$Runtime,
        [Parameter(Mandatory = $true)][bool]$IsDistributionCandidateArtifact
    )

    $names = Get-ExpectedFileNames -Runtime $Runtime
    $instructions = Get-FileTextOrEmpty -Path $InstructionsPath
    foreach ($needle in @(
            $Runtime,
            $names.Zip,
            $names.Checksum,
            $names.Evidence,
            $names.PackagingSmoke,
            $names.LaunchSmoke,
            $names.OpenWithSmoke,
            $names.DefaultOpenSmoke,
            $names.NotarizationLog,
            "shasum -a 256 -c",
            "artifact_channel=",
            "distribution_readiness=",
            "codesign_mode=",
            "notarization_status=",
            "stapler_validated=",
            "zip_sha256=")) {
        Assert-ContainsText -Text $instructions -Needle $needle -Message "$Runtime tester instructions must mention '$needle'."
    }

    if ($IsDistributionCandidateArtifact) {
        Assert-ContainsText -Text $instructions -Needle "distribution-candidate" -Message "$Runtime distribution-candidate tester instructions must name the channel."
        Assert-ContainsText -Text $instructions -Needle "Developer ID" -Message "$Runtime distribution-candidate tester instructions must mention Developer ID signing."
        Assert-ContainsText -Text $instructions -Needle "notarization" -Message "$Runtime distribution-candidate tester instructions must mention notarization."
        Assert-ContainsText -Text $instructions -Needle "stapling" -Message "$Runtime distribution-candidate tester instructions must mention stapling."
        Assert-ContainsText -Text $instructions -Needle "reject" -Message "$Runtime distribution-candidate tester instructions must tell testers to reject missing evidence."
    }
    else {
        Assert-ContainsText -Text $instructions -Needle "internal-preview" -Message "$Runtime internal-preview tester instructions must name the channel."
        Assert-ContainsText -Text $instructions -Needle "not a public release channel" -Message "$Runtime internal-preview tester instructions must warn that the artifact is not public."
    }
}

function Find-DiagnosticsArtifactDirectories {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Runtime
    )

    return @(Get-ChildItem -LiteralPath $Root -Recurse -Directory -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name.IndexOf($Runtime, [System.StringComparison]::OrdinalIgnoreCase) -ge 0 -and
            $_.Name.IndexOf("macos-diagnostics", [System.StringComparison]::OrdinalIgnoreCase) -ge 0
        } |
        ForEach-Object { $_.FullName } |
        Sort-Object)
}

function Test-DiagnosticsArtifact {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$BundleDirectory,
        [Parameter(Mandatory = $true)][string]$Runtime
    )

    $diagnosticsDirectories = @(Find-DiagnosticsArtifactDirectories -Root $Root -Runtime $Runtime)
    if ($diagnosticsDirectories.Count -gt 0) {
        $validatedAny = $false
        foreach ($directory in $diagnosticsDirectories) {
            if (Test-ArtifactFileSet -Directory $directory -Runtime $Runtime -Label "$Runtime diagnostics artifact" -RequireZip $true) {
                $validatedAny = $true
            }
        }

        Assert-True -Condition $validatedAny -Message "$Runtime diagnostics artifact directory was found but did not contain the required file set."
        return
    }

    if ($RequireSeparateDiagnosticsArtifact.IsPresent) {
        Add-ValidationError "$Runtime diagnostics artifact directory was not found. Expected a directory name containing '$Runtime' and 'macos-diagnostics'."
        return
    }

    Assert-True -Condition (Test-ArtifactFileSet -Directory $BundleDirectory -Runtime $Runtime -Label "$Runtime diagnostics file set" -RequireZip $true) -Message "$Runtime diagnostics artifact file set must be present."
}

function Test-RuntimeBundle {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$BundleDirectory,
        [Parameter(Mandatory = $true)][string]$Runtime
    )

    Write-Host "Validating macOS public-preview evidence for $Runtime in $BundleDirectory..."
    if (-not (Test-ArtifactFileSet -Directory $BundleDirectory -Runtime $Runtime -Label "$Runtime app artifact" -RequireZip $true)) {
        return
    }

    $names = Get-ExpectedFileNames -Runtime $Runtime
    $evidencePath = Join-Path $BundleDirectory $names.Evidence
    $packagingSmokePath = Join-Path $BundleDirectory $names.PackagingSmoke
    $launchSmokePath = Join-Path $BundleDirectory $names.LaunchSmoke
    $openWithSmokePath = Join-Path $BundleDirectory $names.OpenWithSmoke
    $defaultOpenSmokePath = Join-Path $BundleDirectory $names.DefaultOpenSmoke
    $notarizationLogPath = Join-Path $BundleDirectory $names.NotarizationLog
    $testerInstructionsPath = Join-Path $BundleDirectory $names.TesterInstructions

    $evidence = Get-KeyValueMap -Path $evidencePath
    $isDistributionCandidateArtifact = Test-ChannelEvidence -Evidence $evidence -Runtime $Runtime

    Test-ChecksumEvidence -BundleDirectory $BundleDirectory -Runtime $Runtime -Evidence $evidence
    Test-PackagingSmoke -PackagingSmokePath $packagingSmokePath -Evidence $evidence -Runtime $Runtime
    Test-LaunchSmoke -LaunchSmokePath $launchSmokePath -Runtime $Runtime
    Test-OpenWithSmoke -OpenWithSmokePath $openWithSmokePath -Runtime $Runtime
    Test-DefaultOpenSmoke -DefaultOpenSmokePath $defaultOpenSmokePath -Runtime $Runtime
    Test-NotarizationLog -NotarizationLogPath $notarizationLogPath -IsDistributionCandidateArtifact $isDistributionCandidateArtifact -Runtime $Runtime
    Test-TesterInstructions -InstructionsPath $testerInstructionsPath -Runtime $Runtime -IsDistributionCandidateArtifact $isDistributionCandidateArtifact
    Test-DiagnosticsArtifact -Root $Root -BundleDirectory $BundleDirectory -Runtime $Runtime
}

$resolvedArtifactRoot = Resolve-InputPath $ArtifactRoot
if (-not (Test-Path -LiteralPath $resolvedArtifactRoot -PathType Container)) {
    throw "macOS public-preview artifact root was not found: $resolvedArtifactRoot"
}

foreach ($runtime in $Runtimes) {
    Assert-True -Condition ($runtime -eq "osx-arm64" -or $runtime -eq "osx-x64") -Message "Unsupported macOS runtime '$runtime'. Expected osx-arm64 or osx-x64."
}

foreach ($runtime in $Runtimes) {
    $bundleDirectories = @(Find-RuntimeBundleDirectories -Root $resolvedArtifactRoot -Runtime $runtime)
    if ($bundleDirectories.Count -eq 0) {
        Add-ValidationError "$runtime artifact bundle was not found under $resolvedArtifactRoot."
        continue
    }

    Test-RuntimeBundle -Root $resolvedArtifactRoot -BundleDirectory $bundleDirectories[0] -Runtime $runtime
}

if ($validationErrors.Count -gt 0) {
    throw "macOS public-preview evidence preflight failed with $($validationErrors.Count) issue(s)."
}

Write-Host "macOS public-preview evidence preflight passed for runtime(s): $($Runtimes -join ', ')."
