[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,

    # Makes the probe object identifiable in the unlikely event cleanup fails.
    [string]$ProbeLabel = "publish-permission-probe",

    # Offline testing. When set, no GitHub call is made; the named commands are simulated as
    # succeeding or failing so both branches can be exercised without a repository.
    [ValidateSet('', 'grant', 'deny-create', 'deny-delete')]
    [string]$SimulateOutcome = ''
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# Why this runs before anything is built.
#
# Releasing 0.8.187 spent roughly forty minutes running every release gate, packaging nine app
# bundles across three operating systems, building both Free Suite installers, and clearing twelve
# deployment approvals -- and then failed on the very last step, four times over, with
# "HTTP 403: Resource not accessible by integration" from POST /releases. The publish job declares
# `permissions: contents: write`, but a job can only reduce what the repository grants: the
# repository's default_workflow_permissions had been left at `read`, which silently capped it.
#
# Nothing earlier in the run needs write access, so nothing earlier noticed. That is the shape of
# failure worth engineering against here: a run that does all of its expensive work before touching
# the one permission it actually needs.
#
# The check is a real create-and-delete of a draft release rather than an inspection of repository
# settings. Settings are an indirect proxy -- an organisation policy, a token change, or a future
# GitHub default could cap the token by some route the settings API does not describe -- whereas
# POST /releases is the exact call the publish job makes. A draft release creates no tag, is not
# visible to anyone but repository writers, and is deleted immediately below.

function Invoke-ProbeApi {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [Parameter(Mandatory = $true)][string]$FailureMessage
    )

    $output = & gh @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw ($FailureMessage + [Environment]::NewLine + ($output -join [Environment]::NewLine))
    }

    return ($output -join [Environment]::NewLine)
}

$probeTag = "{0}-{1}" -f $ProbeLabel, [guid]::NewGuid().ToString("N").Substring(0, 12)
$capMessage = @"
The workflow token cannot create releases in $Repository, so this run would package everything and
then fail at publish -- which is exactly what happened to 0.8.187.

Almost always this is the repository-level cap rather than the workflow: a job's `permissions:` block
can only reduce what the repository grants, never raise it. Check
  Settings -> Actions -> General -> Workflow permissions
and select "Read and write permissions"; confirm with
  gh api repos/$Repository/actions/permissions/workflow
which must report default_workflow_permissions "write".
"@

if ($SimulateOutcome -eq 'deny-create') {
    throw $capMessage
}

if ($SimulateOutcome -eq '') {
    $createdJson = Invoke-ProbeApi -FailureMessage $capMessage -Arguments @(
        "api", "--method", "POST", "repos/$Repository/releases",
        "-f", "tag_name=$probeTag",
        "-f", "name=$probeTag",
        "-F", "draft=true",
        "-f", "body=Transient probe confirming this run can publish. Deleted immediately."
    )
    $releaseId = ($createdJson | ConvertFrom-Json).id
}
else {
    $releaseId = 1
}

# Cleanup is not best-effort: a draft left behind is repository litter, and a probe that can create
# but not delete says the token's write access is partial, which the publish job would also hit.
try {
    if ($SimulateOutcome -eq 'deny-delete') {
        throw "simulated delete failure"
    }

    if ($SimulateOutcome -eq '') {
        Invoke-ProbeApi -FailureMessage "created the probe release but could not delete it" -Arguments @(
            "api", "--method", "DELETE", "repos/$Repository/releases/$releaseId"
        ) | Out-Null
    }
}
catch {
    throw ("The workflow token created draft release $releaseId ($probeTag) in $Repository but could " +
        "not delete it, so its write access is only partial and publishing would be unreliable. " +
        "Delete that draft by hand. Underlying error: $_")
}

Write-Host "Confirmed this run can create and delete releases in $Repository; publish will not 403."
