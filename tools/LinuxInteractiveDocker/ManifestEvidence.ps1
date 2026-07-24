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
