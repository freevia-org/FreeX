param(
    [string]$ChecklistPath = "docs/release/macos-public-preview-checklist.md",
    [string]$ExpectedRuntime,
    [string]$ExpectedRunId,
    [string]$ExpectedRunAttempt
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$validationErrors = New-Object System.Collections.Generic.List[string]

. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Add-ValidationError {
    param([Parameter(Mandatory = $true)][string]$Message)

    Add-ToolValidationError -Errors $validationErrors -Message $Message -SuppressWriteError
}

function Normalize-Cell {
    param([AllowNull()][string]$Value)

    if ($null -eq $Value) {
        return ""
    }

    return $Value.Replace([string][char]0x60, "").Trim()
}

function Split-MarkdownRow {
    param([Parameter(Mandatory = $true)][string]$Line)

    $trimmed = $Line.Trim()
    if ($trimmed.StartsWith("|", [System.StringComparison]::Ordinal)) {
        $trimmed = $trimmed.Substring(1)
    }

    if ($trimmed.EndsWith("|", [System.StringComparison]::Ordinal)) {
        $trimmed = $trimmed.Substring(0, $trimmed.Length - 1)
    }

    return @($trimmed -split "\|" | ForEach-Object { Normalize-Cell $_ })
}

function Test-MarkdownSeparatorRow {
    param([Parameter(Mandatory = $true)][object[]]$Cells)

    if ($Cells.Count -eq 0) {
        return $false
    }

    foreach ($cell in $Cells) {
        $normalized = ([string]$cell).Replace(" ", "")
        if ($normalized -notmatch "^:?-{3,}:?$") {
            return $false
        }
    }

    return $true
}

function Get-MarkdownTables {
    param([Parameter(Mandatory = $true)][string]$Path)

    $lines = @(Get-Content -LiteralPath $Path)
    $tables = @{}
    $currentSection = ""
    $index = 0

    while ($index -lt $lines.Count) {
        $line = [string]$lines[$index]
        if ($line -match "^\s*##\s+(.+?)\s*$") {
            $currentSection = $Matches[1].Trim()
        }

        if ($line.TrimStart().StartsWith("|", [System.StringComparison]::Ordinal) -and $index + 1 -lt $lines.Count) {
            $headers = @(Split-MarkdownRow -Line $line)
            $separator = @(Split-MarkdownRow -Line ([string]$lines[$index + 1]))
            if (Test-MarkdownSeparatorRow -Cells $separator) {
                $rows = New-Object System.Collections.Generic.List[hashtable]
                $index += 2

                while ($index -lt $lines.Count -and ([string]$lines[$index]).TrimStart().StartsWith("|", [System.StringComparison]::Ordinal)) {
                    $cells = @(Split-MarkdownRow -Line ([string]$lines[$index]))
                    if (-not (Test-MarkdownSeparatorRow -Cells $cells)) {
                        $row = @{}
                        for ($headerIndex = 0; $headerIndex -lt $headers.Count; $headerIndex++) {
                            $header = [string]$headers[$headerIndex]
                            $value = ""
                            if ($headerIndex -lt $cells.Count) {
                                $value = [string]$cells[$headerIndex]
                            }

                            $row[$header] = $value
                        }

                        $rows.Add($row)
                    }

                    $index++
                }

                if (-not [string]::IsNullOrWhiteSpace($currentSection)) {
                    $tables[$currentSection] = [pscustomobject]@{
                        Section = $currentSection
                        Headers = $headers
                        Rows = $rows.ToArray()
                    }
                }

                continue
            }
        }

        $index++
    }

    return $tables
}

function Get-RequiredTable {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Tables,
        [Parameter(Mandatory = $true)][string]$Section
    )

    if (-not $Tables.ContainsKey($Section)) {
        Add-ValidationError "Checklist must include the '$Section' table."
        return $null
    }

    $table = $Tables[$Section]
    if ($table.Rows.Count -eq 0) {
        Add-ValidationError "Checklist section '$Section' must include at least one evidence row."
    }

    return $table
}

function Get-CellValue {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Row,
        [Parameter(Mandatory = $true)][string]$Column
    )

    if (-not $Row.ContainsKey($Column)) {
        return ""
    }

    return Normalize-Cell ([string]$Row[$Column])
}

function Test-TemplateValue {
    param([Parameter(Mandatory = $true)][string]$Value)

    $templateValues = @(
        "Apple Silicon / Intel",
        "osx-arm64 / osx-x64",
        "Pass / Fail",
        "Not implemented / Pass / Fail",
        "Pass / Fail / Internal-only",
        "Yes / No"
    )

    if ($templateValues -contains $Value) {
        return $true
    }

    return $Value -match "<[^>]+>"
}

function Assert-FilledValue {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][AllowNull()][string]$Value,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($Value)) {
        Add-ValidationError "$Label must be filled in."
        return
    }

    if (Test-TemplateValue -Value $Value) {
        Add-ValidationError "$Label still contains template placeholder text ('$Value')."
    }
}

function Assert-ValueMatches {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][AllowNull()][string]$Value,
        [Parameter(Mandatory = $true)][string]$Pattern,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ($Value -notmatch $Pattern) {
        Add-ValidationError "$Label has unexpected value '$Value'."
    }
}

function Assert-ValueEquals {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][AllowNull()][string]$Value,
        [Parameter(Mandatory = $true)][string]$ExpectedValue,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if (-not [string]::Equals($Value, $ExpectedValue, [System.StringComparison]::OrdinalIgnoreCase)) {
        Add-ValidationError "$Label must be '$ExpectedValue', but was '$Value'."
    }
}

function Assert-AllowedValue {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][AllowNull()][string]$Value,
        [Parameter(Mandatory = $true)][string[]]$AllowedValues,
        [Parameter(Mandatory = $true)][string]$Label
    )

    Assert-FilledValue -Value $Value -Label $Label
    foreach ($allowed in $AllowedValues) {
        if ([string]::Equals($Value, $allowed, [System.StringComparison]::OrdinalIgnoreCase)) {
            return
        }
    }

    Add-ValidationError "$Label must be one of: $($AllowedValues -join ', '). Actual value: '$Value'."
}

function Assert-AnyEvidenceValue {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Row,
        [Parameter(Mandatory = $true)][string[]]$Columns,
        [Parameter(Mandatory = $true)][string]$Label
    )

    foreach ($column in $Columns) {
        $value = Get-CellValue -Row $Row -Column $column
        if (-not [string]::IsNullOrWhiteSpace($value) -and -not (Test-TemplateValue -Value $value)) {
            return
        }
    }

    Add-ValidationError "$Label must include actual evidence, an attachment, or an explanatory note."
}

function Assert-TableContainsLabels {
    param(
        [Parameter(Mandatory = $true)][object]$Table,
        [Parameter(Mandatory = $true)][string]$LabelColumn,
        [Parameter(Mandatory = $true)][string[]]$ExpectedLabels
    )

    $labels = @($Table.Rows | ForEach-Object { Get-CellValue -Row $_ -Column $LabelColumn })
    foreach ($expected in $ExpectedLabels) {
        if ($labels -notcontains $expected) {
            Add-ValidationError "Checklist section '$($Table.Section)' must include '$expected'."
        }
    }
}

function Assert-TableContainsHeaders {
    param(
        [Parameter(Mandatory = $true)][object]$Table,
        [Parameter(Mandatory = $true)][string[]]$ExpectedHeaders
    )

    foreach ($expected in $ExpectedHeaders) {
        $headerFound = $false
        foreach ($header in @($Table.Headers)) {
            if ([string]::Equals([string]$header, $expected, [System.StringComparison]::OrdinalIgnoreCase)) {
                $headerFound = $true
                break
            }
        }

        if (-not $headerFound) {
            Add-ValidationError "Checklist section '$($Table.Section)' table must include a '$expected' column."
        }
    }
}

function Get-StatusAllowedValues {
    param(
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string[]]$DefaultAllowedValues,
        [Parameter(Mandatory = $true)][hashtable]$AllowedByLabelPattern
    )

    foreach ($pattern in $AllowedByLabelPattern.Keys) {
        if ($Label -like $pattern) {
            return @($AllowedByLabelPattern[$pattern])
        }
    }

    return $DefaultAllowedValues
}

function Test-StatusTable {
    param(
        [Parameter(Mandatory = $true)][object]$Table,
        [Parameter(Mandatory = $true)][string]$LabelColumn,
        [Parameter(Mandatory = $true)][string]$StatusColumn,
        [string[]]$DefaultAllowedValues = @("Pass"),
        [hashtable]$AllowedByLabelPattern = @{},
        [string[]]$EvidenceColumns = @()
    )

    foreach ($row in $Table.Rows) {
        $label = Get-CellValue -Row $row -Column $LabelColumn
        if ([string]::IsNullOrWhiteSpace($label)) {
            Add-ValidationError "Checklist section '$($Table.Section)' has a row with a blank '$LabelColumn' value."
            continue
        }

        $status = Get-CellValue -Row $row -Column $StatusColumn
        $allowed = @(Get-StatusAllowedValues -Label $label -DefaultAllowedValues $DefaultAllowedValues -AllowedByLabelPattern $AllowedByLabelPattern)
        Assert-AllowedValue -Value $status -AllowedValues $allowed -Label "$($Table.Section) '$label' status"

        if ($EvidenceColumns.Count -gt 0) {
            Assert-AnyEvidenceValue -Row $row -Columns $EvidenceColumns -Label "$($Table.Section) '$label'"
        }
    }
}

function Get-SummaryValue {
    param(
        [Parameter(Mandatory = $true)][object]$SummaryTable,
        [Parameter(Mandatory = $true)][string]$Field
    )

    foreach ($row in $SummaryTable.Rows) {
        $candidateField = Get-CellValue -Row $row -Column "Field"
        if ([string]::Equals($candidateField, $Field, [System.StringComparison]::OrdinalIgnoreCase)) {
            return Get-CellValue -Row $row -Column "Value"
        }
    }

    Add-ValidationError "Candidate Summary must include '$Field'."
    return ""
}

function Get-WorkflowRunIdentity {
    param([Parameter(Mandatory = $true)][AllowEmptyString()][AllowNull()][string]$Value)

    if ($Value -notmatch "^(?<RunId>[0-9]+)\s*/\s*(?<RunAttempt>[0-9]+)$") {
        Add-ValidationError "Candidate Summary 'Workflow run id / attempt' must be '<numeric run id> / <numeric run attempt>', but was '$Value'."
        return [pscustomobject]@{
            RunId = ""
            RunAttempt = ""
        }
    }

    return [pscustomobject]@{
        RunId = $Matches.RunId
        RunAttempt = $Matches.RunAttempt
    }
}

function Get-TableRowByLabel {
    param(
        [Parameter(Mandatory = $true)][object]$Table,
        [Parameter(Mandatory = $true)][string]$LabelColumn,
        [Parameter(Mandatory = $true)][string]$Label
    )

    foreach ($row in $Table.Rows) {
        $candidateLabel = Get-CellValue -Row $row -Column $LabelColumn
        if ([string]::Equals($candidateLabel, $Label, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $row
        }
    }

    return $null
}

function Get-RowEvidenceText {
    param(
        [Parameter(Mandatory = $true)][hashtable]$Row,
        [Parameter(Mandatory = $true)][string[]]$Columns
    )

    $values = @()
    foreach ($column in $Columns) {
        $values += Get-CellValue -Row $Row -Column $column
    }

    return ($values -join " ")
}

function Assert-TextIncludesValue {
    param(
        [Parameter(Mandatory = $true)][AllowEmptyString()][AllowNull()][string]$Text,
        [Parameter(Mandatory = $true)][AllowEmptyString()][AllowNull()][string]$ExpectedValue,
        [Parameter(Mandatory = $true)][string]$Label
    )

    if ([string]::IsNullOrWhiteSpace($ExpectedValue)) {
        return
    }

    $normalizedText = ""
    if ($null -ne $Text) {
        $normalizedText = $Text
    }

    if ($normalizedText.IndexOf($ExpectedValue, [System.StringComparison]::OrdinalIgnoreCase) -lt 0) {
        Add-ValidationError "$Label must include '$ExpectedValue'."
    }
}

function Test-CandidateSummary {
    param([Parameter(Mandatory = $true)][object]$SummaryTable)

    $requiredFields = @(
        "Validation date",
        "Tester",
        "Mac model",
        "Processor family",
        "macOS version and build",
        "Runtime under test",
        "Workflow run id / attempt",
        "Source branch or commit",
        "Artifact wrapper name",
        "Diagnostics artifact name",
        "Inner app ZIP",
        "ZIP SHA-256",
        "Evidence file",
        "Signing mode",
        "Notarization status",
        "Stapler status",
        "Final decision",
        "Decision owner"
    )

    foreach ($field in $requiredFields) {
        $value = Get-SummaryValue -SummaryTable $SummaryTable -Field $field
        Assert-FilledValue -Value $value -Label "Candidate Summary '$field'"
    }

    $runtime = Get-SummaryValue -SummaryTable $SummaryTable -Field "Runtime under test"
    Assert-AllowedValue -Value $runtime -AllowedValues @("osx-arm64", "osx-x64") -Label "Candidate Summary 'Runtime under test'"
    if (-not [string]::IsNullOrWhiteSpace($ExpectedRuntime)) {
        Assert-AllowedValue -Value $ExpectedRuntime -AllowedValues @("osx-arm64", "osx-x64") -Label "ExpectedRuntime"
        Assert-ValueEquals -Value $runtime -ExpectedValue $ExpectedRuntime -Label "Candidate Summary 'Runtime under test'"
    }

    $workflowRun = Get-SummaryValue -SummaryTable $SummaryTable -Field "Workflow run id / attempt"
    $workflowIdentity = Get-WorkflowRunIdentity -Value $workflowRun
    $workflowRunMatchesExpected = $true
    if (-not [string]::IsNullOrWhiteSpace($ExpectedRunId) -and $workflowIdentity.RunId -ne $ExpectedRunId) {
        $workflowRunMatchesExpected = $false
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedRunAttempt) -and $workflowIdentity.RunAttempt -ne $ExpectedRunAttempt) {
        $workflowRunMatchesExpected = $false
    }

    if (-not $workflowRunMatchesExpected) {
        Add-ValidationError "Candidate Summary 'Workflow run id / attempt' has unexpected value '$workflowRun'."
    }

    $artifactWrapper = Get-SummaryValue -SummaryTable $SummaryTable -Field "Artifact wrapper name"
    $diagnosticsWrapper = Get-SummaryValue -SummaryTable $SummaryTable -Field "Diagnostics artifact name"
    $artifactRunId = $workflowIdentity.RunId
    $artifactRunAttempt = $workflowIdentity.RunAttempt
    if (-not [string]::IsNullOrWhiteSpace($ExpectedRunId)) {
        $artifactRunId = $ExpectedRunId
    }

    if (-not [string]::IsNullOrWhiteSpace($ExpectedRunAttempt)) {
        $artifactRunAttempt = $ExpectedRunAttempt
    }

    if (-not [string]::IsNullOrWhiteSpace($artifactRunId) -and -not [string]::IsNullOrWhiteSpace($artifactRunAttempt)) {
        Assert-ValueEquals -Value $artifactWrapper -ExpectedValue "freex-$artifactRunId-$artifactRunAttempt-$runtime-macos-app" -Label "Candidate Summary 'Artifact wrapper name'"
        Assert-ValueEquals -Value $diagnosticsWrapper -ExpectedValue "freex-$artifactRunId-$artifactRunAttempt-$runtime-macos-diagnostics" -Label "Candidate Summary 'Diagnostics artifact name'"
    }
    else {
        Assert-ValueMatches -Value $artifactWrapper -Pattern "^freex-[0-9]+-[0-9]+-$runtime-macos-app$" -Label "Candidate Summary 'Artifact wrapper name'"
        Assert-ValueMatches -Value $diagnosticsWrapper -Pattern "^freex-[0-9]+-[0-9]+-$runtime-macos-diagnostics$" -Label "Candidate Summary 'Diagnostics artifact name'"
    }

    $innerAppZip = Get-SummaryValue -SummaryTable $SummaryTable -Field "Inner app ZIP"
    $evidenceFile = Get-SummaryValue -SummaryTable $SummaryTable -Field "Evidence file"
    $zipSha256 = Get-SummaryValue -SummaryTable $SummaryTable -Field "ZIP SHA-256"
    Assert-ValueEquals -Value $innerAppZip -ExpectedValue "freex-$runtime-macos-app.zip" -Label "Candidate Summary 'Inner app ZIP'"
    Assert-ValueEquals -Value $evidenceFile -ExpectedValue "freex-$runtime-macos-evidence.txt" -Label "Candidate Summary 'Evidence file'"
    Assert-ValueMatches -Value $zipSha256 -Pattern "^[0-9a-fA-F]{64}$" -Label "Candidate Summary 'ZIP SHA-256'"
    Assert-ValueMatches -Value (Get-SummaryValue -SummaryTable $SummaryTable -Field "Signing mode") -Pattern "^(developer-id|developer id)$" -Label "Candidate Summary 'Signing mode'"
    Assert-ValueEquals -Value (Get-SummaryValue -SummaryTable $SummaryTable -Field "Notarization status") -ExpectedValue "accepted" -Label "Candidate Summary 'Notarization status'"
    Assert-ValueMatches -Value (Get-SummaryValue -SummaryTable $SummaryTable -Field "Stapler status") -Pattern "^(true|validated|valid|stapled)$" -Label "Candidate Summary 'Stapler status'"
    Assert-ValueEquals -Value (Get-SummaryValue -SummaryTable $SummaryTable -Field "Final decision") -ExpectedValue "Pass" -Label "Candidate Summary 'Final decision'"

    return [pscustomobject]@{
        Runtime = $runtime
        RunId = $workflowIdentity.RunId
        RunAttempt = $workflowIdentity.RunAttempt
        ArtifactWrapper = $artifactWrapper
        DiagnosticsWrapper = $diagnosticsWrapper
        InnerAppZip = $innerAppZip
        ZipSha256 = $zipSha256
        EvidenceFile = $evidenceFile
    }
}

function Test-HostedEvidenceCopyForwardConsistency {
    param(
        [Parameter(Mandatory = $true)][object]$Table,
        [Parameter(Mandatory = $true)][object]$CandidateSummary
    )

    $checksumRow = Get-TableRowByLabel -Table $Table -LabelColumn "Required check" -Label "Checksum"
    if ($null -eq $checksumRow) {
        return
    }

    $checksumEvidence = Get-RowEvidenceText -Row $checksumRow -Columns @("Actual evidence", "Attachment")
    Assert-TextIncludesValue -Text $checksumEvidence -ExpectedValue $CandidateSummary.InnerAppZip -Label "Hosted Evidence Copy-Forward 'Checksum' evidence"
    Assert-TextIncludesValue -Text $checksumEvidence -ExpectedValue $CandidateSummary.ZipSha256 -Label "Hosted Evidence Copy-Forward 'Checksum' evidence"
}

function Test-AccessibilityKnownIssues {
    param([Parameter(Mandatory = $true)][object]$Table)

    Assert-TableContainsHeaders -Table $Table -ExpectedHeaders @(
        "Issue ID",
        "Affected workflow",
        "Severity",
        "User impact / evidence",
        "Workaround",
        "Owner",
        "Public-preview blocking",
        "Decision / rationale"
    )

    if ($Table.Rows.Count -eq 0) {
        return [pscustomobject]@{
            IssueCount = 0
            BlockingIssueCount = 0
            HasNoneRow = $false
        }
    }

    $noneRows = @()
    foreach ($row in $Table.Rows) {
        $issueId = Get-CellValue -Row $row -Column "Issue ID"
        if ([string]::Equals($issueId, "None", [System.StringComparison]::OrdinalIgnoreCase)) {
            $noneRows += $row
        }
    }

    if ($noneRows.Count -gt 0) {
        if ($noneRows.Count -ne 1 -or $Table.Rows.Count -ne 1) {
            Add-ValidationError "Accessibility Known Issues must contain either exactly one 'None' row or issue rows, not both."
        }

        $noneRow = $noneRows[0]
        Assert-ValueEquals -Value (Get-CellValue -Row $noneRow -Column "Affected workflow") -ExpectedValue "None" -Label "Accessibility Known Issues 'None' affected workflow"
        Assert-ValueEquals -Value (Get-CellValue -Row $noneRow -Column "Severity") -ExpectedValue "None" -Label "Accessibility Known Issues 'None' severity"
        Assert-FilledValue -Value (Get-CellValue -Row $noneRow -Column "User impact / evidence") -Label "Accessibility Known Issues 'None' user impact / evidence"
        Assert-ValueEquals -Value (Get-CellValue -Row $noneRow -Column "Workaround") -ExpectedValue "None" -Label "Accessibility Known Issues 'None' workaround"
        Assert-FilledValue -Value (Get-CellValue -Row $noneRow -Column "Owner") -Label "Accessibility Known Issues 'None' owner"
        Assert-AllowedValue -Value (Get-CellValue -Row $noneRow -Column "Public-preview blocking") -AllowedValues @("No") -Label "Accessibility Known Issues 'None' public-preview blocking"
        Assert-FilledValue -Value (Get-CellValue -Row $noneRow -Column "Decision / rationale") -Label "Accessibility Known Issues 'None' decision / rationale"

        return [pscustomobject]@{
            IssueCount = 0
            BlockingIssueCount = 0
            HasNoneRow = $true
        }
    }

    $issueCount = 0
    $blockingIssueCount = 0
    foreach ($row in $Table.Rows) {
        $issueId = Get-CellValue -Row $row -Column "Issue ID"
        $issueLabel = $issueId
        if ([string]::IsNullOrWhiteSpace($issueLabel)) {
            $issueLabel = "<blank issue id>"
        }

        Assert-FilledValue -Value $issueId -Label "Accessibility Known Issues issue id"
        Assert-FilledValue -Value (Get-CellValue -Row $row -Column "Affected workflow") -Label "Accessibility Known Issues '$issueLabel' affected workflow"
        Assert-AllowedValue -Value (Get-CellValue -Row $row -Column "Severity") -AllowedValues @("Critical", "High", "Medium", "Low") -Label "Accessibility Known Issues '$issueLabel' severity"
        Assert-FilledValue -Value (Get-CellValue -Row $row -Column "User impact / evidence") -Label "Accessibility Known Issues '$issueLabel' user impact / evidence"
        Assert-FilledValue -Value (Get-CellValue -Row $row -Column "Workaround") -Label "Accessibility Known Issues '$issueLabel' workaround"
        Assert-FilledValue -Value (Get-CellValue -Row $row -Column "Owner") -Label "Accessibility Known Issues '$issueLabel' owner"
        $blocking = Get-CellValue -Row $row -Column "Public-preview blocking"
        Assert-AllowedValue -Value $blocking -AllowedValues @("Yes", "No") -Label "Accessibility Known Issues '$issueLabel' public-preview blocking"
        Assert-FilledValue -Value (Get-CellValue -Row $row -Column "Decision / rationale") -Label "Accessibility Known Issues '$issueLabel' decision / rationale"

        $issueCount++
        if ([string]::Equals($blocking, "Yes", [System.StringComparison]::OrdinalIgnoreCase)) {
            $blockingIssueCount++
            Add-ValidationError "Accessibility Known Issues '$issueLabel' is marked public-preview blocking; public-preview candidates must be Internal-only until it is resolved or accepted as non-blocking."
        }
    }

    return [pscustomobject]@{
        IssueCount = $issueCount
        BlockingIssueCount = $blockingIssueCount
        HasNoneRow = $false
    }
}

function Test-VoiceOverKnownIssuesReviewConsistency {
    param([Parameter(Mandatory = $true)][object]$VoiceOverTable)

    $knownIssuesRow = Get-TableRowByLabel -Table $VoiceOverTable -LabelColumn "Surface" -Label "Known issues review"
    if ($null -eq $knownIssuesRow) {
        return
    }

    $knownIssuesEvidence = Get-RowEvidenceText -Row $knownIssuesRow -Columns @("Actual announcement or issue", "Evidence")
    Assert-TextIncludesValue -Text $knownIssuesEvidence -ExpectedValue "Accessibility Known Issues" -Label "VoiceOver Smoke 'Known issues review'"
}

function Test-NativeShareSheetReadiness {
    param([Parameter(Mandatory = $true)][object]$Table)

    Assert-TableContainsHeaders -Table $Table -ExpectedHeaders @(
        "Gate",
        "Expected result when native AppKit share sheet is implemented",
        "Actual result",
        "Status",
        "Evidence"
    )

    $expectedRows = @(
        "Native AppKit share sheet implementation status",
        "Saved workbook opens native share sheet",
        "Cancel leaves workbook and file unchanged",
        "Share target receives workbook file",
        "Existing share fallback still works",
        "Keyboard focus after open and cancel",
        "VoiceOver announcement and navigation"
    )
    Assert-TableContainsLabels -Table $Table -LabelColumn "Gate" -ExpectedLabels $expectedRows

    $implementationRow = Get-TableRowByLabel -Table $Table -LabelColumn "Gate" -Label "Native AppKit share sheet implementation status"
    if ($null -eq $implementationRow) {
        return
    }

    $implementationStatus = Get-CellValue -Row $implementationRow -Column "Status"
    Assert-AllowedValue -Value $implementationStatus -AllowedValues @("Pass", "Not implemented") -Label "Future Native Share Sheet Readiness 'Native AppKit share sheet implementation status' status"

    $nativeShareSheetImplemented = [string]::Equals($implementationStatus, "Pass", [System.StringComparison]::OrdinalIgnoreCase)
    $nativeShareSheetNotImplemented = [string]::Equals($implementationStatus, "Not implemented", [System.StringComparison]::OrdinalIgnoreCase)
    if (-not $nativeShareSheetImplemented -and -not $nativeShareSheetNotImplemented) {
        return
    }

    $requiredStatus = "Pass"
    if ($nativeShareSheetNotImplemented) {
        $requiredStatus = "Not implemented"
    }

    foreach ($row in $Table.Rows) {
        $gate = Get-CellValue -Row $row -Column "Gate"
        if ([string]::IsNullOrWhiteSpace($gate)) {
            Add-ValidationError "Checklist section '$($Table.Section)' has a row with a blank 'Gate' value."
            continue
        }

        Assert-AllowedValue -Value (Get-CellValue -Row $row -Column "Status") -AllowedValues @($requiredStatus) -Label "$($Table.Section) '$gate' status"
        Assert-AnyEvidenceValue -Row $row -Columns @("Actual result", "Evidence") -Label "$($Table.Section) '$gate'"
    }
}

function Test-LogAndArtifactCollection {
    param([Parameter(Mandatory = $true)][object]$Table)

    foreach ($row in $Table.Rows) {
        $artifact = Get-CellValue -Row $row -Column "Artifact"
        $required = Get-CellValue -Row $row -Column "Required for public preview"
        if ([string]::IsNullOrWhiteSpace($artifact)) {
            Add-ValidationError "Log And Artifact Collection has a row with a blank Artifact value."
            continue
        }

        if ($required.StartsWith("Yes", [System.StringComparison]::OrdinalIgnoreCase) -or
            $required.StartsWith("Required", [System.StringComparison]::OrdinalIgnoreCase)) {
            Assert-AnyEvidenceValue -Row $row -Columns @("Collected path or attachment") -Label "Log And Artifact Collection '$artifact'"
        }
    }
}

function Test-LogAndArtifactCollectionConsistency {
    param(
        [Parameter(Mandatory = $true)][object]$Table,
        [Parameter(Mandatory = $true)][object]$CandidateSummary
    )

    $appWrapperRow = Get-TableRowByLabel -Table $Table -LabelColumn "Artifact" -Label "GitHub Actions app artifact wrapper"
    if ($null -ne $appWrapperRow) {
        $appWrapperEvidence = Get-RowEvidenceText -Row $appWrapperRow -Columns @("Collected path or attachment", "Notes")
        Assert-TextIncludesValue -Text $appWrapperEvidence -ExpectedValue $CandidateSummary.ArtifactWrapper -Label "Log And Artifact Collection 'GitHub Actions app artifact wrapper'"
    }

    $innerZipRow = Get-TableRowByLabel -Table $Table -LabelColumn "Artifact" -Label "Inner app ZIP and .sha256 file"
    if ($null -ne $innerZipRow) {
        $innerZipEvidence = Get-RowEvidenceText -Row $innerZipRow -Columns @("Collected path or attachment", "Notes")
        Assert-TextIncludesValue -Text $innerZipEvidence -ExpectedValue $CandidateSummary.InnerAppZip -Label "Log And Artifact Collection 'Inner app ZIP and .sha256 file'"
        Assert-TextIncludesValue -Text $innerZipEvidence -ExpectedValue "$($CandidateSummary.InnerAppZip).sha256" -Label "Log And Artifact Collection 'Inner app ZIP and .sha256 file'"
    }

    $evidenceRow = Get-TableRowByLabel -Table $Table -LabelColumn "Artifact" -Label $CandidateSummary.EvidenceFile
    if ($null -ne $evidenceRow) {
        $evidenceFileEvidence = Get-RowEvidenceText -Row $evidenceRow -Columns @("Collected path or attachment", "Notes")
        Assert-TextIncludesValue -Text $evidenceFileEvidence -ExpectedValue $CandidateSummary.EvidenceFile -Label "Log And Artifact Collection '$($CandidateSummary.EvidenceFile)'"
    }

    $releaseAssetsRow = Get-TableRowByLabel -Table $Table -LabelColumn "Artifact" -Label "macOS release-assets wrapper"
    if ($null -ne $releaseAssetsRow) {
        $releaseAssetsEvidence = Get-RowEvidenceText -Row $releaseAssetsRow -Columns @("Collected path or attachment", "Notes")
        Assert-TextIncludesValue -Text $releaseAssetsEvidence -ExpectedValue "macos-release-assets" -Label "Log And Artifact Collection 'macOS release-assets wrapper'"
    }

    $manifestRow = Get-TableRowByLabel -Table $Table -LabelColumn "Artifact" -Label "FreeX-latest-macos-distribution-candidate-manifest.json"
    if ($null -ne $manifestRow) {
        $manifestEvidence = Get-RowEvidenceText -Row $manifestRow -Columns @("Collected path or attachment", "Notes")
        Assert-TextIncludesValue -Text $manifestEvidence -ExpectedValue "FreeX-latest-macos-distribution-candidate-manifest.json" -Label "Log And Artifact Collection 'FreeX-latest-macos-distribution-candidate-manifest.json'"
    }

    $diagnosticsRow = Get-TableRowByLabel -Table $Table -LabelColumn "Artifact" -Label "Diagnostics artifact"
    if ($null -ne $diagnosticsRow) {
        $diagnosticsEvidence = Get-RowEvidenceText -Row $diagnosticsRow -Columns @("Collected path or attachment", "Notes")
        Assert-TextIncludesValue -Text $diagnosticsEvidence -ExpectedValue $CandidateSummary.DiagnosticsWrapper -Label "Log And Artifact Collection 'Diagnostics artifact'"
    }
}

function Test-PublicPreviewDecision {
    param(
        [Parameter(Mandatory = $true)][object]$Table,
        [AllowNull()][object]$AccessibilityKnownIssues
    )

    $expectedDecisionRows = @(
        "Hosted public-preview preflight passed for both runtimes",
        "This runtime passed human Finder/Gatekeeper validation",
        "This runtime passed keyboard-only validation",
        "This runtime passed VoiceOver validation",
        "Known issues are listed with severity, workaround, owner, and blocking decision",
        "Release owner accepts this runtime for public preview"
    )
    Assert-TableContainsLabels -Table $Table -LabelColumn "Decision item" -ExpectedLabels $expectedDecisionRows

    foreach ($row in $Table.Rows) {
        $label = Get-CellValue -Row $row -Column "Decision item"
        $result = Get-CellValue -Row $row -Column "Result"
        if ([string]::Equals($label, "Release owner accepts this runtime for public preview", [System.StringComparison]::OrdinalIgnoreCase)) {
            Assert-AllowedValue -Value $result -AllowedValues @("Yes") -Label "Public-Preview Decision '$label'"
        }
        else {
            Assert-AllowedValue -Value $result -AllowedValues @("Pass") -Label "Public-Preview Decision '$label'"
        }
    }

    if ($null -ne $AccessibilityKnownIssues -and $AccessibilityKnownIssues.BlockingIssueCount -gt 0) {
        Add-ValidationError "Public-Preview Decision 'Known issues are listed with severity, workaround, owner, and blocking decision' cannot pass while Accessibility Known Issues has public-preview blocking issues."
    }
}

$resolvedChecklistPath = Resolve-InputPath -Path $ChecklistPath -RepoRoot $repoRoot
if (-not (Test-Path -LiteralPath $resolvedChecklistPath -PathType Leaf)) {
    throw "macOS human validation checklist was not found: $resolvedChecklistPath"
}

$tables = Get-MarkdownTables -Path $resolvedChecklistPath
$candidateSummary = Get-RequiredTable -Tables $tables -Section "Candidate Summary"
$candidateSummaryDetails = $null
$runtimeUnderTest = ""
if ($null -ne $candidateSummary) {
    $candidateSummaryDetails = Test-CandidateSummary -SummaryTable $candidateSummary
    $runtimeUnderTest = $candidateSummaryDetails.Runtime
}

$hostedEvidence = Get-RequiredTable -Tables $tables -Section "Hosted Evidence Copy-Forward"
if ($null -ne $hostedEvidence) {
    Assert-TableContainsLabels -Table $hostedEvidence -LabelColumn "Required check" -ExpectedLabels @(
        "Checksum",
        "Artifact channel",
        "Distribution readiness",
        "Signing",
        "Notarization",
        "Stapling",
        "Gatekeeper assessment",
        "Hosted launch smoke",
        "LaunchServices/Open-With smoke",
        "Command-key smoke",
        "Diagnostics artifact"
    )
    Test-StatusTable -Table $hostedEvidence -LabelColumn "Required check" -StatusColumn "Status" -EvidenceColumns @("Actual evidence", "Attachment")
    if ($null -ne $candidateSummaryDetails) {
        Test-HostedEvidenceCopyForwardConsistency -Table $hostedEvidence -CandidateSummary $candidateSummaryDetails
    }
}

$gatekeeper = Get-RequiredTable -Tables $tables -Section "Gatekeeper First Launch"
if ($null -ne $gatekeeper) {
    Assert-TableContainsLabels -Table $gatekeeper -LabelColumn "Step" -ExpectedLabels @(
        "Confirm quarantine is still present before first launch, if the artifact was browser-downloaded",
        "Double-click FreeX.app in Finder",
        "Record Gatekeeper prompt",
        "App reaches first usable window",
        "Quit and relaunch from Finder"
    )
    Test-StatusTable -Table $gatekeeper -LabelColumn "Step" -StatusColumn "Status" -AllowedByLabelPattern @{ "Confirm quarantine*" = @("Pass", "N/A") } -EvidenceColumns @("Actual result", "Evidence")
}

$finder = Get-RequiredTable -Tables $tables -Section "Finder And File Association"
if ($null -ne $finder) {
    Assert-TableContainsLabels -Table $finder -LabelColumn "Step" -ExpectedLabels @(
        "Verify .fxl appears as a FreeX-supported document type",
        "Set default .fxl handler, if permitted",
        "Double-click .fxl in Finder",
        "Confirm workbook identity",
        "Right-click .fxl > Open With > FreeX",
        "Drag supported .fxl/.xlsx workbook onto FreeX.app or Dock icon",
        "Repeat while FreeX is already running",
        "Drag supported workbook from Finder onto already-running FreeX window",
        "Optional spreadsheet file Open With"
    )
    Test-StatusTable -Table $finder -LabelColumn "Step" -StatusColumn "Status" -AllowedByLabelPattern @{
        "Set default .fxl handler*" = @("Pass", "Skipped")
        "Optional spreadsheet file Open With" = @("Pass", "N/A")
    } -EvidenceColumns @("Actual result", "Evidence")
}

$workbook = Get-RequiredTable -Tables $tables -Section "Workbook Smoke"
if ($null -ne $workbook) {
    Assert-TableContainsLabels -Table $workbook -LabelColumn "Step" -ExpectedLabels @(
        "Create a new workbook",
        "Enter values and formulas",
        "Save and Save As",
        "Open picker creates bookmark identity",
        "Bookmark scope wraps open, save, and recent-file I/O",
        "Bookmark payload stays out of diagnostics and release evidence",
        "Close dirty workbook",
        "Reopen saved workbook",
        "Recent files",
        "On-device file grant persistence",
        "File-access grant diagnostics review"
    )
    Test-StatusTable -Table $workbook -LabelColumn "Step" -StatusColumn "Status" -EvidenceColumns @("Actual result", "Evidence")
}

$nativeShareSheet = Get-RequiredTable -Tables $tables -Section "Future Native Share Sheet Readiness"
if ($null -ne $nativeShareSheet) {
    Test-NativeShareSheetReadiness -Table $nativeShareSheet
}

$commandKey = Get-RequiredTable -Tables $tables -Section "Command-Key Menu Behavior"
if ($null -ne $commandKey) {
    Assert-TableContainsLabels -Table $commandKey -LabelColumn "Command" -ExpectedLabels @(
        "Menu labels",
        "Cmd+N",
        "Cmd+O",
        "Cmd+S",
        "Cmd+Shift+S",
        "Cmd+W",
        "Cmd+Q",
        "Cmd+A",
        "Cmd+F and Find Next menu route",
        "Cmd+B, Cmd+I, Cmd+U",
        "Cmd+PageUp / Cmd+PageDown or hardware equivalent"
    )
    Test-StatusTable -Table $commandKey -LabelColumn "Command" -StatusColumn "Status" -AllowedByLabelPattern @{ "Cmd+PageUp*" = @("Pass", "N/A") } -EvidenceColumns @("Actual result", "Evidence")
}

$keyboardOnly = Get-RequiredTable -Tables $tables -Section "Keyboard-Only Accessibility"
if ($null -ne $keyboardOnly) {
    Assert-TableContainsLabels -Table $keyboardOnly -LabelColumn "Flow" -ExpectedLabels @(
        "First launch and initial focus",
        "Grid navigation and editing",
        "Formula box edits",
        "Native menus",
        "Toolbar or command surface",
        "Sheet tabs",
        "Dialogs",
        "Context menus",
        "Help and feedback routes",
        "Dirty close and Quit"
    )
    Test-StatusTable -Table $keyboardOnly -LabelColumn "Flow" -StatusColumn "Status" -EvidenceColumns @("Actual result", "Evidence")
}

$voiceOver = Get-RequiredTable -Tables $tables -Section "VoiceOver Smoke"
if ($null -ne $voiceOver) {
    Assert-TableContainsLabels -Table $voiceOver -LabelColumn "Surface" -ExpectedLabels @(
        "First launch",
        "Workbook grid focus",
        "Visible cells",
        "Formula box",
        "Status text",
        "Sheet tabs",
        "Drawing objects, if present",
        "Dialog titles and buttons",
        "Gatekeeper or accessibility prompts",
        "Known issues review"
    )
    Test-StatusTable -Table $voiceOver -LabelColumn "Surface" -StatusColumn "Status" -AllowedByLabelPattern @{
        "Drawing objects*" = @("Pass", "N/A")
        "Gatekeeper or accessibility prompts" = @("Pass", "N/A")
    } -EvidenceColumns @("Actual announcement or issue", "Evidence")
    Test-VoiceOverKnownIssuesReviewConsistency -VoiceOverTable $voiceOver
}

$accessibilityKnownIssues = Get-RequiredTable -Tables $tables -Section "Accessibility Known Issues"
$accessibilityKnownIssuesDetails = $null
if ($null -ne $accessibilityKnownIssues) {
    $accessibilityKnownIssuesDetails = Test-AccessibilityKnownIssues -Table $accessibilityKnownIssues
}

$logCollection = Get-RequiredTable -Tables $tables -Section "Log And Artifact Collection"
if ($null -ne $logCollection) {
    $evidenceArtifactLabel = "freex-<runtime>-macos-evidence.txt"
    if ($runtimeUnderTest -match "^osx-(arm64|x64)$") {
        $evidenceArtifactLabel = "freex-$runtimeUnderTest-macos-evidence.txt"
    }

    Assert-TableContainsLabels -Table $logCollection -LabelColumn "Artifact" -ExpectedLabels @(
        "Completed checklist/report",
        "GitHub Actions app artifact wrapper",
        "Inner app ZIP and .sha256 file",
        $evidenceArtifactLabel,
        "macOS release-assets wrapper",
        "FreeX-latest-macos-distribution-candidate-manifest.json",
        "Packaging smoke log",
        "Launch smoke file",
        "Notarization log",
        "Tester instructions",
        "Diagnostics artifact",
        "Screenshots or recordings",
        "Terminal transcript"
    )
    Test-LogAndArtifactCollection -Table $logCollection
    if ($null -ne $candidateSummaryDetails) {
        Test-LogAndArtifactCollectionConsistency -Table $logCollection -CandidateSummary $candidateSummaryDetails
    }
}

$publicDecision = Get-RequiredTable -Tables $tables -Section "Public-Preview Decision"
if ($null -ne $publicDecision) {
    Test-PublicPreviewDecision -Table $publicDecision -AccessibilityKnownIssues $accessibilityKnownIssuesDetails
}

if ($validationErrors.Count -gt 0) {
    $maximumReportedErrors = 40
    foreach ($message in @($validationErrors | Select-Object -First $maximumReportedErrors)) {
        Write-Host $message
    }

    if ($validationErrors.Count -gt $maximumReportedErrors) {
        Write-Host "...and $($validationErrors.Count - $maximumReportedErrors) more issue(s)."
    }

    throw "macOS human validation checklist failed with $($validationErrors.Count) issue(s)."
}

Write-Host "macOS human validation checklist passed: $resolvedChecklistPath"
