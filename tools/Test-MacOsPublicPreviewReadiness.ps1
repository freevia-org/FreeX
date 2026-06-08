param(
    [string]$ArtifactRoot = "artifacts",
    [string[]]$Runtimes = @("osx-arm64", "osx-x64"),
    [string]$ExpectedRunId,
    [string]$ExpectedRunAttempt,
    [switch]$DistributionCandidate,
    [switch]$RequireSeparateDiagnosticsArtifact,
    [switch]$RequireReleasePublicationArtifact
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

function Assert-DoesNotContainText {
    param(
        [Parameter(Mandatory = $true)][string]$Text,
        [Parameter(Mandatory = $true)][string]$Needle,
        [Parameter(Mandatory = $true)][string]$Message
    )

    if ($Text.IndexOf($Needle, [System.StringComparison]::Ordinal) -ge 0) {
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

function Assert-KeyHasNoConflictingDuplicateValues {
    param(
        [Parameter(Mandatory = $true)][string[]]$Values,
        [Parameter(Mandatory = $true)][string]$Key,
        [Parameter(Mandatory = $true)][string]$Label,
        [string]$ExpectedDescription
    )

    $uniqueValues = New-Object System.Collections.Generic.List[string]
    foreach ($value in $Values) {
        if (-not $uniqueValues.Contains($value)) {
            $uniqueValues.Add($value)
        }
    }

    if ($uniqueValues.Count -le 1) {
        return $true
    }

    $message = "$Label has conflicting duplicate '$Key' values."
    if (-not [string]::IsNullOrWhiteSpace($ExpectedDescription)) {
        $message = "$message $ExpectedDescription"
    }

    Add-ValidationError "$message Actual value(s): $($Values -join ', '). Remove stale or contradictory entries before using this evidence."
    return $false
}

function Assert-KeyPresent {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Map,
        [Parameter(Mandatory = $true)][string]$Key,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $values = @(Get-KeyValues -Map $Map -Key $Key)
    if ($values.Count -eq 0) {
        Add-ValidationError "$Label must include '$Key'."
        return
    }

    Assert-KeyHasNoConflictingDuplicateValues -Values $values -Key $Key -Label $Label | Out-Null
    foreach ($value in $values) {
        if ([string]::IsNullOrWhiteSpace($value)) {
            Add-ValidationError "$Label must include a non-empty '$Key' value. Actual value(s): $($values -join ', ')."
            return
        }
    }
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

    Assert-KeyHasNoConflictingDuplicateValues -Values $values -Key $Key -Label $Label -ExpectedDescription "Expected '$Key=$ExpectedValue'." | Out-Null
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

    Assert-KeyHasNoConflictingDuplicateValues -Values $values -Key $Key -Label $Label -ExpectedDescription "Every value must match /$Pattern/." | Out-Null
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

    $values = @(Get-KeyValues -Map $Map -Key $Key)
    if ($values.Count -eq 0) {
        Add-ValidationError "$Label must include '$Key'."
        return
    }

    Assert-KeyHasNoConflictingDuplicateValues -Values $values -Key $Key -Label $Label -ExpectedDescription "Every value must be at least $Minimum." | Out-Null
    foreach ($value in $values) {
        if ([string]::IsNullOrWhiteSpace($value)) {
            Add-ValidationError "$Label must include a non-empty '$Key' value. Actual value(s): $($values -join ', ')."
            return
        }

        $parsed = 0
        if (-not [int]::TryParse($value, [ref]$parsed) -or $parsed -lt $Minimum) {
            Add-ValidationError "$Label '$Key' must be at least $Minimum, but was '$value'."
        }
    }
}

function Get-ExpectedArtifactWrapperName {
    param(
        [Parameter(Mandatory = $true)][string]$Runtime,
        [Parameter(Mandatory = $true)][string]$Kind
    )

    return "freex-<run-id>-<run-attempt>-$Runtime-macos-$Kind"
}

function Get-ArtifactDownloadIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Directory
    )

    $rootInfo = Get-Item -LiteralPath $Root
    $current = Get-Item -LiteralPath $Directory
    while ($null -ne $current) {
        if ($current.Name -match "^freex-(?<RunId>[0-9]+)-(?<RunAttempt>[0-9]+)-(?<Runtime>osx-(arm64|x64))-macos-(?<Kind>app|diagnostics)$") {
            return [pscustomobject]@{
                RunId = $Matches["RunId"]
                RunAttempt = $Matches["RunAttempt"]
                Runtime = $Matches["Runtime"]
                Kind = $Matches["Kind"]
                WrapperDirectory = $current.FullName
            }
        }

        if ([System.StringComparer]::OrdinalIgnoreCase.Equals($current.FullName, $rootInfo.FullName)) {
            break
        }

        $current = $current.Parent
    }

    return $null
}

function Test-ArtifactDownloadIdentity {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][string]$Runtime,
        [Parameter(Mandatory = $true)][string]$Kind,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $identity = Get-ArtifactDownloadIdentity -Root $Root -Directory $Directory
    if ($null -eq $identity) {
        if (-not [string]::IsNullOrWhiteSpace($ExpectedRunId) -or -not [string]::IsNullOrWhiteSpace($ExpectedRunAttempt)) {
            $expectedWrapper = Get-ExpectedArtifactWrapperName -Runtime $Runtime -Kind $Kind
            Add-ValidationError "$Label does not preserve a GitHub Actions artifact wrapper directory named '$expectedWrapper'. Re-download the artifact or keep the unzipped files under that wrapper directory before using -ExpectedRunId or -ExpectedRunAttempt."
        }

        return $null
    }

    Assert-True -Condition ($identity.Runtime -eq $Runtime) -Message "$Label wrapper directory '$($identity.WrapperDirectory)' is for runtime '$($identity.Runtime)', expected '$Runtime'."
    Assert-True -Condition ($identity.Kind -eq $Kind) -Message "$Label wrapper directory '$($identity.WrapperDirectory)' is a macOS '$($identity.Kind)' artifact, expected '$Kind'."

    if (-not [string]::IsNullOrWhiteSpace($ExpectedRunId)) {
        Assert-True -Condition ($identity.RunId -eq $ExpectedRunId) -Message "$Label is from GitHub Actions run '$($identity.RunId)', expected run '$ExpectedRunId'."
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedRunAttempt)) {
        Assert-True -Condition ($identity.RunAttempt -eq $ExpectedRunAttempt) -Message "$Label is from GitHub Actions run attempt '$($identity.RunAttempt)', expected attempt '$ExpectedRunAttempt'."
    }

    return $identity
}

function Test-ArtifactIdentityConsistency {
    param(
        [Parameter(Mandatory = $true)][object[]]$Identities,
        [Parameter(Mandatory = $true)][string]$Root
    )

    $knownIdentities = @($Identities | Where-Object { $null -ne $_ })
    if ($knownIdentities.Count -lt 2) {
        return
    }

    $first = $knownIdentities[0]
    foreach ($identity in $knownIdentities) {
        if ($identity.RunId -ne $first.RunId -or $identity.RunAttempt -ne $first.RunAttempt) {
            Add-ValidationError "Downloaded macOS app artifacts are from mixed GitHub Actions runs: $($first.Runtime) uses run $($first.RunId) attempt $($first.RunAttempt) from '$($first.WrapperDirectory)', but $($identity.Runtime) uses run $($identity.RunId) attempt $($identity.RunAttempt) from '$($identity.WrapperDirectory)'. Remove stale artifact folders under $Root or pass -ArtifactRoot to a single downloaded run."
        }
    }
}

function Test-ArtifactIdentityMatches {
    param(
        [Parameter(Mandatory = $true)][object]$Identity,
        [Parameter(Mandatory = $true)][object]$ExpectedIdentity,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$ExpectedLabel
    )

    if ($Identity.RunId -ne $ExpectedIdentity.RunId -or $Identity.RunAttempt -ne $ExpectedIdentity.RunAttempt) {
        Add-ValidationError "$Label is from GitHub Actions run '$($Identity.RunId)' attempt '$($Identity.RunAttempt)', but $ExpectedLabel is from run '$($ExpectedIdentity.RunId)' attempt '$($ExpectedIdentity.RunAttempt)'. Remove stale artifact folders under the artifact root or re-download the matching artifact set."
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

        if ($file.Directory.FullName.IndexOf("macos-diagnostics", [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
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
    $missingNames = New-Object System.Collections.Generic.List[string]
    foreach ($key in $requiredKeys) {
        $path = Join-Path $Directory $names[$key]
        if (-not (Assert-FileExists -Path $path -Label "$Label $key")) {
            $allPresent = $false
            $missingNames.Add($names[$key])
        }
    }

    if (-not $allPresent) {
        Add-ValidationError "$Label is incomplete. Missing file(s): $($missingNames -join ', '). Unzip the GitHub Actions artifact wrapper first and point -ArtifactRoot at the folder containing the downloaded macOS app evidence bundles."
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

function Get-FirstSha256HashFromText {
    param([Parameter(Mandatory = $true)][string]$Text)

    if ($Text -match "([0-9a-fA-F]{64})") {
        return $Matches[1].ToLowerInvariant()
    }

    return $null
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

    $checksumHash = Get-FirstSha256HashFromText -Text $checksumText

    Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($checksumHash)) -Message "$Runtime checksum file must contain a SHA-256 hash."
    if (-not [string]::IsNullOrWhiteSpace($checksumHash)) {
        Assert-True -Condition ($checksumHash -eq $actualHash) -Message "$Runtime checksum file hash must match $($names.Zip)."
    }

    Assert-KeyEquals -Map $Evidence -Key "zip_name" -ExpectedValue $names.Zip -Label "$Runtime evidence"
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
    Assert-KeyEquals -Map $Evidence -Key "gatekeeper_assessment_attempted" -ExpectedValue "true" -Label "$Runtime evidence"
    Assert-KeyPresent -Map $Evidence -Key "gatekeeper_assessment_required" -Label "$Runtime evidence"
    Assert-KeyEquals -Map $Evidence -Key "gatekeeper_assessment_subject" -ExpectedValue "unzipped_app_bundle" -Label "$Runtime evidence"
    Assert-KeyEquals -Map $Evidence -Key "gatekeeper_assessment_type" -ExpectedValue "execute" -Label "$Runtime evidence"
    Assert-KeyMatches -Map $Evidence -Key "gatekeeper_assessment_exit_code" -Pattern "^-?[0-9]+$" -Label "$Runtime evidence"
    Assert-KeyMatches -Map $Evidence -Key "gatekeeper_assessment_status" -Pattern "^(accepted|rejected)$" -Label "$Runtime evidence"
    Assert-KeyPresent -Map $Evidence -Key "gatekeeper_assessment_source" -Label "$Runtime evidence"

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
        Assert-KeyEquals -Map $Evidence -Key "gatekeeper_assessment_required" -ExpectedValue "true" -Label "$Runtime distribution-candidate evidence"
        Assert-KeyEquals -Map $Evidence -Key "gatekeeper_assessment_exit_code" -ExpectedValue "0" -Label "$Runtime distribution-candidate evidence"
        Assert-KeyEquals -Map $Evidence -Key "gatekeeper_assessment_status" -ExpectedValue "accepted" -Label "$Runtime distribution-candidate evidence"
        Assert-KeyEquals -Map $Evidence -Key "gatekeeper_assessment_source" -ExpectedValue "Notarized Developer ID" -Label "$Runtime distribution-candidate evidence"
    }
    else {
        Assert-KeyEquals -Map $Evidence -Key "artifact_channel" -ExpectedValue "internal-preview" -Label "$Runtime internal-preview evidence"
        Assert-KeyEquals -Map $Evidence -Key "distribution_candidate" -ExpectedValue "false" -Label "$Runtime internal-preview evidence"
        Assert-KeyEquals -Map $Evidence -Key "distribution_contract" -ExpectedValue "internal_preview_not_for_distribution_notarization_optional" -Label "$Runtime internal-preview evidence"
        Assert-KeyEquals -Map $Evidence -Key "distribution_readiness" -ExpectedValue "internal_preview_not_for_distribution" -Label "$Runtime internal-preview evidence"
        Assert-KeyEquals -Map $Evidence -Key "gatekeeper_assessment_required" -ExpectedValue "false" -Label "$Runtime internal-preview evidence"
    }

    return $isDistributionCandidateArtifact
}

function Get-JsonPropertyValue {
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$PropertyName
    )

    $property = $Object.PSObject.Properties[$PropertyName]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function Assert-JsonPropertyPresent {
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$PropertyName,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $value = Get-JsonPropertyValue -Object $Object -PropertyName $PropertyName
    if ($null -eq $value -or ($value -is [string] -and [string]::IsNullOrWhiteSpace($value))) {
        Add-ValidationError "$Label must include JSON property '$PropertyName'."
    }
}

function Assert-JsonPropertyEquals {
    param(
        [Parameter(Mandatory = $true)][object]$Object,
        [Parameter(Mandatory = $true)][string]$PropertyName,
        [Parameter(Mandatory = $true)][string]$ExpectedValue,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $value = Get-JsonPropertyValue -Object $Object -PropertyName $PropertyName
    if ($null -eq $value) {
        Add-ValidationError "$Label must include JSON property '$PropertyName=$ExpectedValue'."
        return
    }

    $actualValue = [string]$value
    if ($actualValue -ne $ExpectedValue) {
        Add-ValidationError "$Label JSON property '$PropertyName' must be '$ExpectedValue', but was '$actualValue'."
    }
}

function Test-ReleasePublicationArtifact {
    param(
        [Parameter(Mandatory = $true)][string]$Root,
        [Parameter(Mandatory = $true)][string[]]$ExpectedRuntimes
    )

    $manifestName = "FreeX-latest-macos-distribution-candidate-manifest.json"
    $instructionsName = "FreeX-latest-macos-distribution-candidate-instructions.md"
    $manifestFiles = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Filter $manifestName -ErrorAction SilentlyContinue)
    $instructionsFiles = @(Get-ChildItem -LiteralPath $Root -Recurse -File -Filter $instructionsName -ErrorAction SilentlyContinue)
    $hasPublicationArtifact = $manifestFiles.Count -gt 0 -or $instructionsFiles.Count -gt 0

    if (-not $RequireReleasePublicationArtifact.IsPresent -and -not $hasPublicationArtifact) {
        return
    }

    if ($manifestFiles.Count -eq 0) {
        Add-ValidationError "macOS release publication artifact manifest was not found. Expected '$manifestName'."
    }
    elseif ($manifestFiles.Count -gt 1) {
        Add-ValidationError "Expected exactly one macOS release publication artifact manifest named '$manifestName', but found $($manifestFiles.Count)."
    }

    if ($instructionsFiles.Count -eq 0) {
        Add-ValidationError "macOS release publication instructions were not found. Expected '$instructionsName'."
    }
    elseif ($instructionsFiles.Count -gt 1) {
        Add-ValidationError "Expected exactly one macOS release publication instructions file named '$instructionsName', but found $($instructionsFiles.Count)."
    }

    if ($manifestFiles.Count -ne 1 -or $instructionsFiles.Count -ne 1) {
        return
    }

    $manifestPath = $manifestFiles[0].FullName
    $releaseDirectory = $manifestFiles[0].Directory.FullName
    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        Add-ValidationError "macOS release publication artifact manifest must be valid JSON: $($_.Exception.Message)"
        return
    }

    Assert-JsonPropertyEquals -Object $manifest -PropertyName "schema" -ExpectedValue "io.github.tony-xmelon.freex.macos-distribution-candidate.v1" -Label "macOS release publication manifest"
    foreach ($propertyName in @("release_id", "tag", "repository", "workflow", "run_id", "run_attempt", "commit", "generated_at_utc", "source_artifact_pattern")) {
        Assert-JsonPropertyPresent -Object $manifest -PropertyName $propertyName -Label "macOS release publication manifest"
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedRunId)) {
        Assert-JsonPropertyEquals -Object $manifest -PropertyName "run_id" -ExpectedValue $ExpectedRunId -Label "macOS release publication manifest"
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedRunAttempt)) {
        Assert-JsonPropertyEquals -Object $manifest -PropertyName "run_attempt" -ExpectedValue $ExpectedRunAttempt -Label "macOS release publication manifest"
    }

    $sourceArtifactPattern = [string](Get-JsonPropertyValue -Object $manifest -PropertyName "source_artifact_pattern")
    Assert-True -Condition ($sourceArtifactPattern.IndexOf("*-macos-app", [System.StringComparison]::Ordinal) -ge 0) -Message "macOS release publication manifest source_artifact_pattern must target downloaded macOS app artifacts."
    if (-not [string]::IsNullOrWhiteSpace($ExpectedRunId) -and -not [string]::IsNullOrWhiteSpace($ExpectedRunAttempt)) {
        $expectedSourceArtifactPattern = "freex-$ExpectedRunId-$ExpectedRunAttempt-*-macos-app"
        Assert-JsonPropertyEquals -Object $manifest -PropertyName "source_artifact_pattern" -ExpectedValue $expectedSourceArtifactPattern -Label "macOS release publication manifest"
    }
    elseif (-not [string]::IsNullOrWhiteSpace($ExpectedRunId)) {
        $expectedRunPattern = "^freex-$([System.Text.RegularExpressions.Regex]::Escape($ExpectedRunId))-[0-9]+-\*-macos-app$"
        Assert-True -Condition ($sourceArtifactPattern -match $expectedRunPattern) -Message "macOS release publication manifest source_artifact_pattern must include expected run '$ExpectedRunId'."
    }
    elseif (-not [string]::IsNullOrWhiteSpace($ExpectedRunAttempt)) {
        $expectedAttemptPattern = "^freex-[0-9]+-$([System.Text.RegularExpressions.Regex]::Escape($ExpectedRunAttempt))-\*-macos-app$"
        Assert-True -Condition ($sourceArtifactPattern -match $expectedAttemptPattern) -Message "macOS release publication manifest source_artifact_pattern must include expected run attempt '$ExpectedRunAttempt'."
    }

    $rawMarkers = Get-JsonPropertyValue -Object $manifest -PropertyName "distribution_candidate_required_markers"
    if ($null -eq $rawMarkers) {
        Add-ValidationError "macOS release publication manifest must include JSON property 'distribution_candidate_required_markers'."
    }

    $markers = @($rawMarkers | ForEach-Object { [string]$_ })
    foreach ($marker in @(
            "artifact_channel=distribution-candidate",
            "distribution_candidate=true",
            "distribution_readiness=distribution_candidate_ready",
            "codesign_mode=developer-id",
            "notarization_status=accepted",
            "stapler_validated=true",
            "gatekeeper_assessment_attempted=true",
            "gatekeeper_assessment_required=true",
            "gatekeeper_assessment_exit_code=0",
            "gatekeeper_assessment_status=accepted",
            "gatekeeper_assessment_source=Notarized Developer ID")) {
        Assert-True -Condition ($markers -contains $marker) -Message "macOS release publication manifest must include distribution candidate marker '$marker'."
    }

    $rawAssets = Get-JsonPropertyValue -Object $manifest -PropertyName "assets"
    if ($null -eq $rawAssets) {
        Add-ValidationError "macOS release publication manifest must include JSON property 'assets'."
        return
    }

    $assets = @($rawAssets)
    foreach ($runtime in $ExpectedRuntimes) {
        $assetMatches = @($assets | Where-Object { $null -ne $_ -and [string](Get-JsonPropertyValue -Object $_ -PropertyName "runtime") -eq $runtime })
        if ($assetMatches.Count -ne 1) {
            Add-ValidationError "macOS release publication manifest must contain exactly one asset entry for '$runtime'."
            continue
        }

        $asset = $assetMatches[0]
        $names = Get-ExpectedFileNames -Runtime $runtime
        $assetLabel = if ($runtime -eq "osx-arm64") { "macos-arm64" } else { "macos-x64" }
        $stableZip = if ($runtime -eq "osx-arm64") { "FreeX-latest-macos-arm64.zip" } else { "FreeX-latest-macos-x64.zip" }
        $expectedAssetProperties = [ordered]@{
            asset_label = $assetLabel
            original_zip = $names.Zip
            stable_zip = $stableZip
            stable_zip_checksum = "$stableZip.sha256"
            evidence = "FreeX-latest-$assetLabel-evidence.txt"
            packaging_smoke_log = "FreeX-latest-$assetLabel-packaging-smoke.log"
            launch_smoke_report = "FreeX-latest-$assetLabel-launch-smoke.txt"
            open_with_launch_smoke_report = "FreeX-latest-$assetLabel-open-with-launch-smoke.txt"
            default_open_launch_smoke_report = "FreeX-latest-$assetLabel-default-open-launch-smoke.txt"
            notarization_log = "FreeX-latest-$assetLabel-notarization.log"
            tester_instructions = "FreeX-latest-$assetLabel-tester-instructions.md"
        }

        foreach ($entry in $expectedAssetProperties.GetEnumerator()) {
            Assert-JsonPropertyEquals -Object $asset -PropertyName $entry.Key -ExpectedValue $entry.Value -Label "$runtime release publication manifest asset"
        }

        $sha256 = [string](Get-JsonPropertyValue -Object $asset -PropertyName "sha256")
        $manifestHashLooksValid = $sha256 -match "^[0-9a-fA-F]{64}$"
        $manifestHash = $sha256.ToLowerInvariant()
        Assert-True -Condition $manifestHashLooksValid -Message "$runtime release publication manifest asset sha256 must be a SHA-256 hash."

        foreach ($propertyName in @("evidence", "packaging_smoke_log", "launch_smoke_report", "open_with_launch_smoke_report", "default_open_launch_smoke_report", "notarization_log", "tester_instructions")) {
            $fileName = [string](Get-JsonPropertyValue -Object $asset -PropertyName $propertyName)
            if ([string]::IsNullOrWhiteSpace($fileName)) {
                Add-ValidationError "$runtime release publication manifest asset must include '$propertyName'."
                continue
            }

            Assert-FileExists -Path (Join-Path $releaseDirectory $fileName) -Label "$runtime release publication asset $propertyName" | Out-Null
        }

        $stableZipFileName = [string](Get-JsonPropertyValue -Object $asset -PropertyName "stable_zip")
        $stableChecksumFileName = [string](Get-JsonPropertyValue -Object $asset -PropertyName "stable_zip_checksum")
        if ([string]::IsNullOrWhiteSpace($stableZipFileName)) {
            Add-ValidationError "$runtime release publication manifest asset must include 'stable_zip'."
        }

        if ([string]::IsNullOrWhiteSpace($stableChecksumFileName)) {
            Add-ValidationError "$runtime release publication manifest asset must include 'stable_zip_checksum'."
        }

        $stableZipPath = Join-Path $releaseDirectory $stableZipFileName
        $stableChecksumPath = Join-Path $releaseDirectory $stableChecksumFileName
        $stableZipExists = Assert-FileExists -Path $stableZipPath -Label "$runtime release publication asset stable_zip"
        $stableChecksumExists = Assert-FileExists -Path $stableChecksumPath -Label "$runtime release publication asset stable_zip_checksum"

        $stableZipHash = $null
        if ($stableZipExists) {
            $stableZipHash = (Get-FileHash -LiteralPath $stableZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
            if ($manifestHashLooksValid) {
                Assert-True -Condition ($stableZipHash -eq $manifestHash) -Message "$runtime release publication manifest asset sha256 must match stable ZIP $stableZipFileName."
            }
        }

        if ($stableChecksumExists) {
            $checksumText = Get-FileTextOrEmpty -Path $stableChecksumPath
            Assert-ContainsText -Text $checksumText -Needle $stableZipFileName -Message "$runtime release publication checksum must name $stableZipFileName."
            $checksumHash = Get-FirstSha256HashFromText -Text $checksumText
            Assert-True -Condition (-not [string]::IsNullOrWhiteSpace($checksumHash)) -Message "$runtime release publication checksum must contain a SHA-256 hash."
            if (-not [string]::IsNullOrWhiteSpace($checksumHash) -and $manifestHashLooksValid) {
                Assert-True -Condition ($checksumHash -eq $manifestHash) -Message "$runtime release publication checksum hash must match manifest sha256."
            }

            if (-not [string]::IsNullOrWhiteSpace($checksumHash) -and -not [string]::IsNullOrWhiteSpace($stableZipHash)) {
                Assert-True -Condition ($checksumHash -eq $stableZipHash) -Message "$runtime release publication checksum hash must match stable ZIP $stableZipFileName."
            }
        }

        $releaseEvidenceName = [string](Get-JsonPropertyValue -Object $asset -PropertyName "evidence")
        $releaseEvidencePath = Join-Path $releaseDirectory $releaseEvidenceName
        if (Test-Path -LiteralPath $releaseEvidencePath -PathType Leaf) {
            $releaseEvidence = Get-KeyValueMap -Path $releaseEvidencePath
            Assert-KeyEquals -Map $releaseEvidence -Key "runtime" -ExpectedValue $runtime -Label "$runtime release publication evidence asset"
            if ($manifestHashLooksValid) {
                Assert-KeyEquals -Map $releaseEvidence -Key "zip_sha256" -ExpectedValue $manifestHash -Label "$runtime release publication evidence asset"
            }

            if (-not [string]::IsNullOrWhiteSpace($ExpectedRunId)) {
                Assert-KeyEquals -Map $releaseEvidence -Key "github_run_id" -ExpectedValue $ExpectedRunId -Label "$runtime release publication evidence asset"
            }

            if (-not [string]::IsNullOrWhiteSpace($ExpectedRunAttempt)) {
                Assert-KeyEquals -Map $releaseEvidence -Key "github_run_attempt" -ExpectedValue $ExpectedRunAttempt -Label "$runtime release publication evidence asset"
            }
        }
    }

    $releaseInstructions = Get-FileTextOrEmpty -Path $instructionsFiles[0].FullName
    foreach ($needle in @(
            "FreeX-latest-macos-arm64.zip",
            "FreeX-latest-macos-x64.zip",
            $manifestName,
            "default-open launch smoke",
            "distribution-candidate",
            "Developer ID",
            "notarization",
            "stapler",
            "Gatekeeper",
            "gatekeeper_assessment_status=accepted",
            "Reject")) {
        Assert-ContainsText -Text $releaseInstructions -Needle $needle -Message "macOS release publication instructions must mention '$needle'."
    }
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
    Assert-KeyEquals -Map $launch -Key "live_cmd_select_all_state_changed" -ExpectedValue "true" -Label "$Runtime command key smoke"
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
            "gatekeeper_assessment_status=",
            "gatekeeper_assessment_source=",
            "zip_sha256=")) {
        Assert-ContainsText -Text $instructions -Needle $needle -Message "$Runtime tester instructions must mention '$needle'."
    }

    if ($IsDistributionCandidateArtifact) {
        Assert-ContainsText -Text $instructions -Needle "distribution-candidate" -Message "$Runtime distribution-candidate tester instructions must name the channel."
        Assert-ContainsText -Text $instructions -Needle "Developer ID" -Message "$Runtime distribution-candidate tester instructions must mention Developer ID signing."
        Assert-ContainsText -Text $instructions -Needle "notarization" -Message "$Runtime distribution-candidate tester instructions must mention notarization."
        Assert-ContainsText -Text $instructions -Needle "stapling" -Message "$Runtime distribution-candidate tester instructions must mention stapling."
        Assert-ContainsText -Text $instructions -Needle "reject" -Message "$Runtime distribution-candidate tester instructions must tell testers to reject missing evidence."
        foreach ($internalOnlyNeedle in @(
                "For artifact_channel=internal-preview",
                "ad-hoc signed or non-notarized previews may require",
                "Control-click or right-click > Open",
                "trusted internal testing")) {
            Assert-DoesNotContainText -Text $instructions -Needle $internalOnlyNeedle -Message "$Runtime distribution-candidate tester instructions must not include internal-preview-only guidance ('$internalOnlyNeedle')."
        }
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
        [Parameter(Mandatory = $true)][string]$Runtime,
        [object]$AppIdentity
    )

    $diagnosticsDirectories = @(Find-DiagnosticsArtifactDirectories -Root $Root -Runtime $Runtime)
    if ($diagnosticsDirectories.Count -gt 0) {
        $validatedAny = $false
        foreach ($directory in $diagnosticsDirectories) {
            if (Test-ArtifactFileSet -Directory $directory -Runtime $Runtime -Label "$Runtime diagnostics artifact" -RequireZip $true) {
                $validatedAny = $true
                $diagnosticsIdentity = Test-ArtifactDownloadIdentity -Root $Root -Directory $directory -Runtime $Runtime -Kind "diagnostics" -Label "$Runtime diagnostics artifact"
                if ($null -ne $AppIdentity -and $null -ne $diagnosticsIdentity) {
                    Test-ArtifactIdentityMatches -Identity $diagnosticsIdentity -ExpectedIdentity $AppIdentity -Label "$Runtime diagnostics artifact" -ExpectedLabel "$Runtime app artifact"
                }
                elseif ($null -ne $AppIdentity -and $null -eq $diagnosticsIdentity) {
                    $expectedWrapper = Get-ExpectedArtifactWrapperName -Runtime $Runtime -Kind "diagnostics"
                    Add-ValidationError "$Runtime diagnostics artifact does not preserve a GitHub Actions artifact wrapper directory named '$expectedWrapper', so it cannot be matched to the $Runtime app artifact from run '$($AppIdentity.RunId)' attempt '$($AppIdentity.RunAttempt)'."
                }
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
        [Parameter(Mandatory = $true)][string]$Runtime,
        [object]$ArtifactIdentity
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
    Test-DiagnosticsArtifact -Root $Root -BundleDirectory $BundleDirectory -Runtime $Runtime -AppIdentity $ArtifactIdentity
}

$resolvedArtifactRoot = Resolve-InputPath $ArtifactRoot
if (-not (Test-Path -LiteralPath $resolvedArtifactRoot -PathType Container)) {
    throw "macOS public-preview artifact root was not found: $resolvedArtifactRoot"
}

foreach ($runtime in $Runtimes) {
    Assert-True -Condition ($runtime -eq "osx-arm64" -or $runtime -eq "osx-x64") -Message "Unsupported macOS runtime '$runtime'. Expected osx-arm64 or osx-x64."
}

$artifactIdentities = New-Object System.Collections.Generic.List[object]
foreach ($runtime in $Runtimes) {
    $bundleDirectories = @(Find-RuntimeBundleDirectories -Root $resolvedArtifactRoot -Runtime $runtime)
    if ($bundleDirectories.Count -eq 0) {
        $expectedWrapper = Get-ExpectedArtifactWrapperName -Runtime $runtime -Kind "app"
        $names = Get-ExpectedFileNames -Runtime $runtime
        Add-ValidationError "$runtime app artifact bundle was not found under $resolvedArtifactRoot. Expected a downloaded GitHub Actions artifact wrapper named '$expectedWrapper' containing $($names.Zip), $($names.Checksum), and $($names.Evidence)."
        continue
    }

    if ($bundleDirectories.Count -gt 1) {
        Add-ValidationError "$runtime has multiple downloaded macOS app artifact bundles under $resolvedArtifactRoot. Remove stale artifact folders or pass -ArtifactRoot to a single downloaded run. Candidate directories: $($bundleDirectories -join '; ')."
        continue
    }

    $bundleDirectory = $bundleDirectories[0]
    $identity = Test-ArtifactDownloadIdentity -Root $resolvedArtifactRoot -Directory $bundleDirectory -Runtime $runtime -Kind "app" -Label "$runtime app artifact"
    if ($null -ne $identity) {
        $artifactIdentities.Add($identity)
    }

    Test-RuntimeBundle -Root $resolvedArtifactRoot -BundleDirectory $bundleDirectory -Runtime $runtime -ArtifactIdentity $identity
}

Test-ArtifactIdentityConsistency -Identities $artifactIdentities.ToArray() -Root $resolvedArtifactRoot
Test-ReleasePublicationArtifact -Root $resolvedArtifactRoot -ExpectedRuntimes $Runtimes

if ($validationErrors.Count -gt 0) {
    throw "macOS public-preview evidence preflight failed with $($validationErrors.Count) issue(s)."
}

Write-Host "macOS public-preview evidence preflight passed for runtime(s): $($Runtimes -join ', ')."
