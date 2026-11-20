# Dot-source this file to get Invoke-WithRetry.
#
# A bounded retry for the handful of release steps that only talk to an external service (a
# dotnet tool install, a token request, a git ls-remote, a GitHub API call) and fail now and then for
# reasons that have nothing to do with the commit. It is deliberately small: a fixed number of
# attempts with a growing delay, and the LAST failure is rethrown unchanged in meaning, so a
# permanent failure still fails the job after at most ~20 seconds of delay rather than being hidden.
#
# Use it only around operations that are safe to repeat. Anything that mutates published state must
# re-establish its own plan on every attempt (see Send-ReleaseAssets.ps1) instead of replaying blindly.

function Invoke-WithRetry {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]
        [scriptblock]$Action,

        [Parameter(Mandatory = $true)]
        [string]$Description,

        [ValidateRange(1, 6)]
        [int]$Attempts = 3,

        # Delay before attempt 2, 3, ... The last value repeats if there are more attempts.
        [int[]]$DelaySeconds = @(5, 15),

        # Offline testing: replaces Start-Sleep.
        [scriptblock]$Sleep = { param([int]$Seconds) Start-Sleep -Seconds $Seconds }
    )

    $lastFailure = ''
    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            $global:LASTEXITCODE = 0
            $output = & $Action
            if ($LASTEXITCODE -ne 0) {
                throw "exit code $LASTEXITCODE"
            }

            # An action with no output must yield NOTHING, not a lone $null: callers wrap the result in
            # @() and test .Count (git ls-remote of an absent tag), and @($null) has one element.
            if ($null -eq $output) { return }
            return $output
        }
        catch {
            $lastFailure = $_.Exception.Message
        }

        if ($attempt -lt $Attempts) {
            $delay = $DelaySeconds[[Math]::Min($attempt - 1, $DelaySeconds.Count - 1)]
            Write-Warning "$Description failed on attempt $attempt of ${Attempts}: $lastFailure. Retrying in $delay second(s)."
            & $Sleep $delay
        }
    }

    throw "$Description failed after $Attempts attempt(s): $lastFailure"
}
