function Get-ManifestEvidenceFileMap {
    param(
        [Parameter(Mandatory = $true)][string]$EvidenceDirectory
    )

    $map = @{}
    foreach ($file in @(Get-ChildItem -LiteralPath $EvidenceDirectory -File -ErrorAction Stop)) {
        $map[$file.Name] = $file
    }
    return $map
}

function Read-ManifestContract {
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$SchemaPath,
        [string]$InvalidSchemaMessage = "Manifest contract reference is not a JSON Schema document.",
        [string]$SchemaTitlePattern
    )

    if (-not (Test-Path -LiteralPath $SchemaPath -PathType Leaf)) {
        throw "Manifest schema is missing: $SchemaPath"
    }

    $schema = Get-Content -LiteralPath $SchemaPath -Raw | ConvertFrom-Json
    if ($schema.'$schema' -notmatch "json-schema.org" -or
        (-not [string]::IsNullOrWhiteSpace($SchemaTitlePattern) -and $schema.title -notmatch $SchemaTitlePattern)) {
        throw $InvalidSchemaMessage
    }

    return Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
}

function Assert-ManifestIdentity {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Expected,
        [string]$FailureMessage = "Manifest identity does not satisfy its contract."
    )

    foreach ($entry in $Expected.GetEnumerator()) {
        $value = $Manifest
        foreach ($segment in ([string]$entry.Key -split '\.')) {
            $property = $value.PSObject.Properties[$segment]
            if ($null -eq $property) {
                throw "$FailureMessage Missing property '$($entry.Key)'."
            }
            $value = $property.Value
        }
        if ($value -ne $entry.Value) {
            throw "$FailureMessage Property '$($entry.Key)' was '$value'; expected '$($entry.Value)'."
        }
    }
}

function Assert-ManifestResultIds {
    param(
        [Parameter(Mandatory = $true)][object[]]$Results,
        [Parameter(Mandatory = $true)][string[]]$ExpectedIds,
        [switch]$AllowAnyOrder,
        [string]$FailureMessage = "Manifest result IDs do not satisfy the exact contract."
    )

    $actualIds = @($Results | ForEach-Object { [string]$_.id })
    $uniqueIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($id in $actualIds) {
        if (-not $uniqueIds.Add($id)) {
            throw "$FailureMessage Duplicate ID '$id'."
        }
    }
    if ($actualIds.Count -ne $ExpectedIds.Count) {
        throw "$FailureMessage Expected $($ExpectedIds.Count) rows; found $($actualIds.Count)."
    }

    if ($AllowAnyOrder) {
        foreach ($id in $ExpectedIds) {
            if (-not $uniqueIds.Contains($id)) {
                throw "$FailureMessage Missing ID '$id'."
            }
        }
        return
    }

    for ($index = 0; $index -lt $ExpectedIds.Count; $index++) {
        if (-not [string]::Equals($actualIds[$index], $ExpectedIds[$index], [System.StringComparison]::Ordinal)) {
            throw "$FailureMessage Row $index was '$($actualIds[$index])'; expected '$($ExpectedIds[$index])'."
        }
    }
}

function Assert-ManifestResultEvidence {
    param(
        [Parameter(Mandatory = $true)][object[]]$Results,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$FileMap,
        [string]$Category,
        [string]$EvidenceLevel,
        [string[]]$ValidStatuses = @("passed", "failed"),
        [ValidateRange(0, [int]::MaxValue)][int]$MinimumEvidenceCount = 1,
        [switch]$RequireNote,
        [string]$FailureMessage = "Manifest result does not satisfy its evidence contract."
    )

    foreach ($result in $Results) {
        $id = [string]$result.id
        if ((-not [string]::IsNullOrWhiteSpace($Category) -and $result.category -ne $Category) -or
            (-not [string]::IsNullOrWhiteSpace($EvidenceLevel) -and $result.evidenceLevel -ne $EvidenceLevel) -or
            $ValidStatuses -notcontains [string]$result.status -or
            @($result.evidence).Count -lt $MinimumEvidenceCount -or
            ($RequireNote -and [string]::IsNullOrWhiteSpace([string]$result.note))) {
            throw "$FailureMessage Result '$id'."
        }
        foreach ($name in @($result.evidence)) {
            Assert-ManifestEvidenceReference -FileMap $FileMap -Name ([string]$name) -Owner "Result '$id'"
        }
    }
}

function Assert-ManifestScreenshotEvidence {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$Screenshots,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$FileMap,
        [ValidateRange(-1, [int]::MaxValue)][int]$ExpectedCount = -1,
        [ValidateRange(0, [int]::MaxValue)][int]$MinimumCount = 0,
        [switch]$RequireKind
    )

    if (($ExpectedCount -ge 0 -and $Screenshots.Count -ne $ExpectedCount) -or
        $Screenshots.Count -lt $MinimumCount) {
        throw "Manifest screenshot count does not satisfy its contract."
    }
    foreach ($screenshot in $Screenshots) {
        $name = [string]$screenshot.name
        if ($RequireKind -and $screenshot.kind -ne "screenshot") {
            throw "Manifest screenshot '$name' has an invalid kind."
        }
        Assert-ManifestEvidenceReference -FileMap $FileMap -Name $name -Owner "Manifest" -ReferenceKind "screenshot"
    }
}

function Assert-ManifestContractPending {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [string]$Validator,
        [string]$ContractReference
    )

    if ($Manifest.contractValidation.status -ne "pending" -or
        (-not [string]::IsNullOrWhiteSpace($Validator) -and $Manifest.contractValidation.validator -ne $Validator) -or
        (-not [string]::IsNullOrWhiteSpace($ContractReference) -and $Manifest.contractValidation.contractReference -ne $ContractReference)) {
        throw "Probe must leave contractValidation pending with the expected validator and schema reference."
    }
}

function Assert-ManifestResultSummary {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][object[]]$Results,
        [Parameter(Mandatory = $true)][int]$ExpectedTotal,
        [switch]$RequireCompleteStatuses,
        [string]$FailureMessage = "Manifest summary does not match its result rows."
    )

    $passed = @($Results | Where-Object { $_.status -eq "passed" }).Count
    $failed = @($Results | Where-Object { $_.status -eq "failed" }).Count
    if ($Manifest.summary.total -ne $ExpectedTotal -or
        $Manifest.summary.passed -ne $passed -or
        $Manifest.summary.failed -ne $failed -or
        ($RequireCompleteStatuses -and ($passed + $failed) -ne $ExpectedTotal)) {
        throw $FailureMessage
    }
}

function Assert-ManifestEvidenceReference {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$FileMap,
        [AllowEmptyString()][string]$Name,
        [Parameter(Mandatory = $true)][string]$Owner,
        [ValidateSet("evidence", "screenshot")][string]$ReferenceKind = "evidence"
    )

    if ([string]::IsNullOrWhiteSpace($Name) -or
        [IO.Path]::IsPathRooted($Name) -or
        [IO.Path]::GetFileName($Name) -ne $Name -or
        $Name.Contains("/") -or
        $Name.Contains("\") -or
        -not $FileMap.ContainsKey($Name) -or
        $FileMap[$Name].Length -le 0) {
        throw "$Owner references missing, empty, or non-basename $ReferenceKind '$Name'."
    }
}

function Complete-ManifestContract {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$Validator,
        [Parameter(Mandatory = $true)][string]$ContractReference,
        [int]$JsonDepth = 12
    )

    $Manifest | Add-Member -NotePropertyName contractValidation -NotePropertyValue ([pscustomobject]@{
            status = "passed"
            validator = $Validator
            contractReference = $ContractReference
        }) -Force
    $Manifest | ConvertTo-Json -Depth $JsonDepth | Set-Content -LiteralPath $ManifestPath -Encoding utf8
    return $Manifest
}

function Get-ManifestEvidenceReferences {
    param(
        [Parameter(Mandatory = $true)]$Manifest
    )

    @(
        @($Manifest.results | ForEach-Object { $_.evidence }) |
            ForEach-Object { [string]$_ }
        @($Manifest.screenshots | ForEach-Object { $_.name }) |
            ForEach-Object { [string]$_ }
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
}

function Wait-ForManifestEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$ManifestPath,
        [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
        [int]$TimeoutSeconds = 15,
        [int]$PollMilliseconds = 250
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastMissing = @()
    $lastReadError = $null
    $previousCompleteSizeSignature = $null
    $lastSizeState = @()
    do {
        try {
            $manifest = Get-Content -LiteralPath $ManifestPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
            $lastReadError = $null
        } catch {
            $manifest = $null
            $lastReadError = $_.Exception.ToString()
            $lastMissing = @([IO.Path]::GetFileName($ManifestPath))
            $lastSizeState = @("manifest-unreadable")
            $previousCompleteSizeSignature = $null
        }

        if ($null -eq $manifest) {
            if ([DateTime]::UtcNow -ge $deadline) {
                break
            }
            Start-Sleep -Milliseconds $PollMilliseconds
            continue
        }

        $fileMap = Get-ManifestEvidenceFileMap -EvidenceDirectory $EvidenceDirectory
        $references = @(Get-ManifestEvidenceReferences -Manifest $manifest)
        $lastSizeState = @($references | ForEach-Object {
                if (-not $fileMap.ContainsKey($_)) {
                    "$_=MISSING"
                    return
                }
                $length = $fileMap[$_].Length
                if ($length -le 0) {
                    "$_=EMPTY"
                    return
                }
                "$_=$length"
            }) | Sort-Object
        $lastMissing = @($lastSizeState | ForEach-Object {
                if ($_ -match "^(.*)=(MISSING|EMPTY)$") {
                    $Matches[1]
                }
            })
        $completeSizeSignature = [string]::Join("|", $lastSizeState)
        if ($lastMissing.Count -eq 0) {
            if ($completeSizeSignature -eq $previousCompleteSizeSignature) {
                return
            }
            $previousCompleteSizeSignature = $completeSizeSignature
        } else {
            $previousCompleteSizeSignature = $null
        }

        if ([DateTime]::UtcNow -ge $deadline) {
            break
        }
        Start-Sleep -Milliseconds $PollMilliseconds
    } while ($true)

    $diagnosticName = "evidence-settle-timeout.txt"
    $diagnosticPath = Join-Path $EvidenceDirectory $diagnosticName
    @(
        "Manifest evidence did not become visible and non-empty before the bounded settle timeout.",
        "manifest=$ManifestPath",
        "evidence-directory=$EvidenceDirectory",
        "timeout-seconds=$TimeoutSeconds",
        "poll-milliseconds=$PollMilliseconds",
        "missing-or-empty-count=$($lastMissing.Count)",
        "missing-or-empty-paths:",
        @($lastMissing),
        "last-observed-size-state:",
        @($lastSizeState),
        "last-manifest-read-error:",
        $(if ($null -eq $lastReadError) { "<none>" } else { $lastReadError })
    ) | Set-Content -LiteralPath $diagnosticPath -Encoding utf8
    throw "Manifest evidence did not settle within $TimeoutSeconds seconds. Durable diagnostics: $diagnosticPath. Missing or empty references: $([string]::Join(', ', $lastMissing))"
}
