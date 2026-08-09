function Resolve-VisualEvidenceOutputDirectory {
    param(
        [Parameter(Mandatory = $true)][string]$OutputDirectory,
        [Parameter(Mandatory = $true)][string]$RepoRoot
    )

    if ([IO.Path]::IsPathRooted($OutputDirectory)) {
        return [IO.Path]::GetFullPath($OutputDirectory)
    }

    return [IO.Path]::GetFullPath((Join-Path $RepoRoot $OutputDirectory))
}

function Invoke-VisualEvidenceProcess {
    param(
        [Parameter(Mandatory = $true)][string]$FilePath,
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [switch]$OutputToHost
    )

    Push-Location $WorkingDirectory
    try {
        if ($OutputToHost) {
            & $FilePath @Arguments | Out-Host
        }
        else {
            & $FilePath @Arguments
        }
        $exitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if ($exitCode -ne 0) {
        throw "$FilePath exited with code $exitCode."
    }
}

function Wait-VisualEvidenceFile {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [ValidateRange(0, [int]::MaxValue)][int]$TimeoutMilliseconds = 0,
        [ValidateRange(1, [int]::MaxValue)][int]$PollMilliseconds = 250,
        [switch]$RequireNonEmpty,
        [string]$MissingMessage = "Expected evidence file was not written: $Path"
    )

    $deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMilliseconds)
    do {
        $entry = Get-Item -LiteralPath $Path -Force -ErrorAction SilentlyContinue
        if ($null -ne $entry -and -not $entry.PSIsContainer -and (-not $RequireNonEmpty -or $entry.Length -gt 0)) {
            return $entry.FullName
        }

        if ([DateTime]::UtcNow -ge $deadline) {
            break
        }
        Start-Sleep -Milliseconds $PollMilliseconds
    } while ($true)

    throw $MissingMessage
}

function Read-VisualEvidenceJson {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [ValidateRange(0, [int]::MaxValue)][int]$TimeoutMilliseconds = 0,
        [ValidateRange(1, [int]::MaxValue)][int]$PollMilliseconds = 250,
        [switch]$RequireNonEmpty,
        [string]$MissingMessage = "Expected evidence file was not written: $Path"
    )

    $resolvedPath = Wait-VisualEvidenceFile `
        -Path $Path `
        -TimeoutMilliseconds $TimeoutMilliseconds `
        -PollMilliseconds $PollMilliseconds `
        -RequireNonEmpty:$RequireNonEmpty `
        -MissingMessage $MissingMessage
    return Get-Content -LiteralPath $resolvedPath -Raw | ConvertFrom-Json
}

function Add-VisualEvidenceResultReferences {
    param(
        [Parameter(Mandatory = $true)]$Result,
        [Parameter(Mandatory = $true)][string[]]$Names
    )

    $evidence = [System.Collections.Generic.List[string]]::new()
    foreach ($name in @($Result.evidence) + $Names) {
        if (-not [string]::IsNullOrWhiteSpace([string]$name) -and -not $evidence.Contains([string]$name)) {
            $evidence.Add([string]$name)
        }
    }
    $Result.evidence = $evidence.ToArray()
}

function Get-VisualEvidenceFileSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-VisualEvidenceNormalizedTextSha256 {
    param([Parameter(Mandatory = $true)][string]$Path)

    $content = [IO.File]::ReadAllText($Path).Replace("`r`n", "`n").Replace("`r", "`n")
    $bytes = [Text.UTF8Encoding]::new($false).GetBytes($content)
    $hasher = [Security.Cryptography.SHA256]::Create()
    try {
        return [BitConverter]::ToString($hasher.ComputeHash($bytes)).Replace("-", "").ToLowerInvariant()
    }
    finally {
        $hasher.Dispose()
    }
}

function Get-VisualEvidenceRelativePath {
    param(
        [Parameter(Mandatory = $true)][string]$EvidenceRoot,
        [Parameter(Mandatory = $true)][string]$Path
    )

    return $Path.Substring($EvidenceRoot.Length).TrimStart('\', '/').Replace('\', '/')
}

function Get-VisualEvidenceArtifactInventory {
    param(
        [Parameter(Mandatory = $true)][string]$EvidenceRoot,
        [string[]]$ExcludedPaths = @(),
        [switch]$RequireNonEmpty,
        [string]$EmptyArtifactMessage = "Visual evidence contains an empty artifact: {0}"
    )

    $excluded = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $ExcludedPaths) {
        if (-not [string]::IsNullOrWhiteSpace($path)) {
            [void]$excluded.Add([IO.Path]::GetFullPath($path))
        }
    }

    return @(Get-ChildItem -LiteralPath $EvidenceRoot -Recurse -File |
        Where-Object { -not $excluded.Contains([IO.Path]::GetFullPath($_.FullName)) } |
        Sort-Object { Get-VisualEvidenceRelativePath -EvidenceRoot $EvidenceRoot -Path $_.FullName } |
        ForEach-Object {
            if ($RequireNonEmpty -and $_.Length -le 0) {
                throw ($EmptyArtifactMessage -f $_.FullName)
            }
            [ordered]@{
                path = Get-VisualEvidenceRelativePath -EvidenceRoot $EvidenceRoot -Path $_.FullName
                bytes = $_.Length
                sha256 = Get-VisualEvidenceFileSha256 -Path $_.FullName
            }
        })
}
