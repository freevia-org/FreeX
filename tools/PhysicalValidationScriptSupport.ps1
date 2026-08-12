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

function Read-PhysicalValidationFixtureValues {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][string]$Action,
        [Parameter(Mandatory = $true)][string]$ArtifactPath
    )

    $lines = @(Invoke-PhysicalValidationFixture -ProjectPath $ProjectPath -Action $Action -ArtifactPath $ArtifactPath)
    return ConvertFrom-PhysicalValidationKeyValueLines -Lines $lines
}
