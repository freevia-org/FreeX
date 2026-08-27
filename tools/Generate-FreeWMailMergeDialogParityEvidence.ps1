param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
. (Join-Path $PSScriptRoot 'ToolScriptSupport.ps1')
Invoke-ToolCanonicalPwshHost -ScriptPath $PSCommandPath -ForwardedArguments @("-Check:$([bool]$Check)")
$jsonPath = Join-Path $repo 'docs/parity/freew-mail-merge-dialog-parity-20260720.json'
$markdownPath = Join-Path $repo 'docs/parity/freew-mail-merge-dialog-parity-20260720.md'

$sourceFiles = @(
    'freew/FreeW.App.Host/Ribbon/FreeWRibbonCommands.cs',
    'freew/FreeW.App.Avalonia/MailMergeDialogs.cs',
    'freew/FreeW.App.Avalonia/MainWindow.cs',
    'freew/FreeW.App.Avalonia/Ribbon/FreeWAvaloniaRibbonCommands.cs',
    'freew/FreeW.Ribbon.Definitions/FreeWRibbon.cs',
    'freew/FreeW.Ribbon.Definitions/FreeWCanonicalRibbonTabs.cs',
    'freew/FreeW.Ribbon.Definitions/FreeWCanonicalRibbonTabs.Ordinary.cs',
    'freew/FreeW.Ribbon.Definitions/FreeWCanonicalRibbonTabs.Contextual.cs',
    'freew/FreeW.App.Presentation/Ribbon/MailMergeDialogPlanners.cs',
    'freew/FreeW.App.Presentation/Ribbon/MailMergeFinishPlanner.cs',
    'freew/FreeW.App.Presentation/Ribbon/MailingsEnvelopeLabelPlanner.cs',
    'freew/FreeW.App.Presentation/Ribbon/MailMergeMatchFieldsDialogPlanner.cs',
    'freew/FreeW.App.Presentation/Ribbon/MailMergeRecipientFilterSortPlanner.cs',
    'freew/FreeW.App.Presentation/Ribbon/MailMergePreviewNavigationPlanner.cs',
    'freew/FreeW.App.Presentation.Tests/MailMergeDialogPlannerTests.cs',
    'freew/FreeW.App.Avalonia.Tests/MailMergeDialogSurfaceTests.cs'
)

$surfaces = @(
    [ordered]@{ name = 'Envelopes'; wpfAuthority = 'EnvelopeSetupDialog'; avaloniaSurface = 'AskEnvelopeAsync'; sharedPolicy = 'CreateEnvelopeDialogPlan/PlanEnvelope'; status = 'implemented'; shellWiringGap = '' },
    [ordered]@{ name = 'Labels'; wpfAuthority = 'LabelSetupDialog'; avaloniaSurface = 'AskLabelsAsync'; sharedPolicy = 'CreateLabelDialogPlan/PlanLabel'; status = 'implemented'; shellWiringGap = '' },
    [ordered]@{ name = 'Start Mail Merge/type selection'; wpfAuthority = 'SetMergeModeCommand choices'; avaloniaSurface = 'AskStartMailMergeAsync'; sharedPolicy = 'MailMergeStartDialogPlanner'; status = 'implemented'; shellWiringGap = '' },
    [ordered]@{ name = 'Select/Edit Recipients'; wpfAuthority = 'MergeDataDialog'; avaloniaSurface = 'AskRecipientCsvAsync'; sharedPolicy = 'MailMergeRecipientDialogPlanner'; status = 'implemented'; shellWiringGap = '' },
    [ordered]@{ name = 'Address Block'; wpfAuthority = 'InsertAddressBlockCommand (direct insertion, no modal)'; avaloniaSurface = 'MailMergeInsertionPlanner plus existing engine action'; sharedPolicy = 'MailMergeInsertionPlanner'; status = 'implemented'; shellWiringGap = '' },
    [ordered]@{ name = 'Greeting Line'; wpfAuthority = 'InsertGreetingLineCommand (direct insertion, no modal)'; avaloniaSurface = 'MailMergeInsertionPlanner plus existing engine action'; sharedPolicy = 'MailMergeInsertionPlanner'; status = 'implemented'; shellWiringGap = '' },
    [ordered]@{ name = 'Insert Merge Field'; wpfAuthority = 'InsertMergeFieldCommand'; avaloniaSurface = 'AskMergeFieldNameAsync'; sharedPolicy = 'MailMergeInsertionPlanner'; status = 'implemented'; shellWiringGap = '' },
    [ordered]@{ name = 'Rules'; wpfAuthority = 'MergeRuleIfDialog/MergeRuleCondDialog/MergeRulePromptDialog/MergeRuleAskSetDialog'; avaloniaSurface = 'AskMergeRuleIfAsync/AskMergeRuleConditionAsync/AskMergeRulePromptAsync/AskMergeRuleNameValueAsync'; sharedPolicy = 'MailMergeRuleDialogPlanner'; status = 'implemented'; shellWiringGap = '' },
    [ordered]@{ name = 'Match Fields'; wpfAuthority = 'MatchFieldsDialog'; avaloniaSurface = 'AskMatchFieldsAsync'; sharedPolicy = 'MailMergeMatchFieldsDialogPlanner'; status = 'implemented'; shellWiringGap = '' },
    [ordered]@{ name = 'Filter and Sort Recipients'; wpfAuthority = 'FilterSortRecipientsDialog'; avaloniaSurface = 'AskFilterSortRecipientsAsync'; sharedPolicy = 'MailMergeFilterSortDialogPlanner'; status = 'implemented'; shellWiringGap = '' },
    [ordered]@{ name = 'Update Labels'; wpfAuthority = 'LabelsCommand label-cell population'; avaloniaSurface = 'Existing engine ApplyDefaultLabels'; sharedPolicy = 'MailingsEnvelopeLabelPlanner'; status = 'implemented'; shellWiringGap = '' },
    [ordered]@{ name = 'Preview Results'; wpfAuthority = 'PreviewNavigationDialog'; avaloniaSurface = 'AskPreviewNavigationAsync'; sharedPolicy = 'MailMergePreviewDialogPlanner'; status = 'implemented'; shellWiringGap = '' },
    [ordered]@{ name = 'Find Recipient'; wpfAuthority = 'WPF preview recipient search gap; explicit parity addition'; avaloniaSurface = 'AskFindRecipientAsync'; sharedPolicy = 'MailMergeFindRecipientPlanner'; status = 'implemented'; shellWiringGap = '' },
    [ordered]@{ name = 'Check for Errors'; wpfAuthority = 'Word Mailings check-for-errors three-mode contract'; avaloniaSurface = 'AskCheckForErrorsAsync'; sharedPolicy = 'MailMergeCheckForErrorsPlanner'; status = 'implemented'; shellWiringGap = '' },
    [ordered]@{ name = 'Finish and Merge destination/options'; wpfAuthority = 'FinishMergeCommand plus MailMergeFinishPlanner'; avaloniaSurface = 'AskFinishMergeAsync'; sharedPolicy = 'MailMergeFinishPlanner.CreateDialogPlan/Plan'; status = 'implemented'; shellWiringGap = '' },
    [ordered]@{ name = 'Send E-mail Messages'; wpfAuthority = 'EmailMergeDialog'; avaloniaSurface = 'AskEmailMergeDeliveryAsync'; sharedPolicy = 'MailMergeEmailDeliveryPlanner'; status = 'implemented'; shellWiringGap = '' }
)

$hashes = [ordered]@{}
foreach ($relative in $sourceFiles) {
    $path = Join-Path $repo $relative
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing evidence input: $relative" }
    $hashes[$relative] = Get-ToolNormalizedTextSha256 -Path $path
}

$evidence = [ordered]@{
    schema = 'freex.freew.mail-merge-dialog-parity.v1'
    authority = 'FreeW.App.Host WPF dialog and command behavior'
    generatedInputs = $sourceFiles
    sourceSha256 = $hashes
    ownershipBoundary = @(
        'MainWindow and ribbon command/definition routes are included in the integration fingerprints.',
        'Backstage, page-layout/media/design, and shared shell routes remain outside this mail-merge inventory.'
    )
    surfaces = $surfaces
    freshnessCheck = 'Run tools/Generate-FreeWMailMergeDialogParityEvidence.ps1 -Check; nonzero means generated JSON/Markdown no longer matches current source hashes.'
}

$jsonText = $evidence | ConvertTo-Json -Depth 12
$implemented = @($surfaces | Where-Object { $_.status -like 'implemented*' }).Count
$policyOnly = @($surfaces | Where-Object { $_.status -eq 'implemented-policy-only' }).Count
$gapLines = ($surfaces | ForEach-Object { "| $($_.name) | $($_.status) | $($_.sharedPolicy) | $($_.shellWiringGap) |" }) -join [Environment]::NewLine
$markdownText = @"
# FreeW Mail Merge Dialog Parity

Generated from the WPF authority and shared/presentation/Avalonia source hashes. This report is deterministic; run ``tools/Generate-FreeWMailMergeDialogParityEvidence.ps1 -Check`` to verify freshness.

- Schema: ``$($evidence.schema)``
- Surfaces inventoried: $($surfaces.Count)
- Implemented dialog/policy surfaces: $implemented
- Implemented policy-only surfaces: $policyOnly
- Authority: ``$($evidence.authority)``

| Surface | Status | Shared policy | Exact shell-wiring gap |
|---|---|---|---|
$gapLines

## Boundary

MainWindow and ribbon command/definition routes are included in the generated source fingerprints; no mail-merge shell wiring gaps remain.

## Freshness

The JSON records SHA-256 hashes for every authority, implementation, and focused-test input. ``-Check`` regenerates both artifacts in memory and fails if either committed artifact differs.
"@

if ($Check) {
    if (-not (Test-Path -LiteralPath $jsonPath) -or -not (Test-Path -LiteralPath $markdownPath)) { throw 'Generated evidence files are missing.' }
    Test-ToolGeneratedContentMatches -ExpectedContent $jsonText -ActualPath $jsonPath -Label 'FreeW mail-merge dialog parity JSON' -GeneratorScriptName 'tools/Generate-FreeWMailMergeDialogParityEvidence.ps1' -NormalizeNewlines
    Test-ToolGeneratedContentMatches -ExpectedContent $markdownText -ActualPath $markdownPath -Label 'FreeW mail-merge dialog parity Markdown' -GeneratorScriptName 'tools/Generate-FreeWMailMergeDialogParityEvidence.ps1' -NormalizeNewlines
    Write-Output "Fresh: $jsonPath"
    Write-Output "Fresh: $markdownPath"
    exit 0
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText($jsonPath, $jsonText, $utf8)
[IO.File]::WriteAllText($markdownPath, $markdownText, $utf8)
Write-Output "Wrote $jsonPath"
Write-Output "Wrote $markdownPath"
