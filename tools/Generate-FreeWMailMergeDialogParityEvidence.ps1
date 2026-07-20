param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$jsonPath = Join-Path $repo 'docs\parity\freew-mail-merge-dialog-parity-20260720.json'
$markdownPath = Join-Path $repo 'docs\parity\freew-mail-merge-dialog-parity-20260720.md'

$sourceFiles = @(
    'freew/FreeW.App.Host/Ribbon/FreeWRibbonCommands.cs',
    'freew/FreeW.App.Avalonia/MailMergeDialogs.cs',
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
    [ordered]@{ name = 'Envelopes'; wpfAuthority = 'EnvelopeSetupDialog'; avaloniaSurface = 'AskEnvelopeAsync'; sharedPolicy = 'CreateEnvelopeDialogPlan/PlanEnvelope'; status = 'implemented'; shellWiringGap = 'MainWindow and ribbon command files are ownership-forbidden; dialog route is recorded but not connected in this slice.' },
    [ordered]@{ name = 'Labels'; wpfAuthority = 'LabelSetupDialog'; avaloniaSurface = 'AskLabelsAsync'; sharedPolicy = 'CreateLabelDialogPlan/PlanLabel'; status = 'implemented'; shellWiringGap = 'MainWindow and ribbon command files are ownership-forbidden; dialog route is recorded but not connected in this slice.' },
    [ordered]@{ name = 'Start Mail Merge/type selection'; wpfAuthority = 'SetMergeModeCommand choices'; avaloniaSurface = 'AskStartMailMergeAsync'; sharedPolicy = 'MailMergeStartDialogPlanner'; status = 'implemented'; shellWiringGap = 'FreeWAvaloniaRibbonCommands.cs is command-registry-owned and cannot be edited here.' },
    [ordered]@{ name = 'Select/Edit Recipients'; wpfAuthority = 'MergeDataDialog'; avaloniaSurface = 'AskRecipientCsvAsync'; sharedPolicy = 'MailMergeRecipientDialogPlanner'; status = 'implemented'; shellWiringGap = 'Existing callback wiring remains in forbidden MainWindow.cs.' },
    [ordered]@{ name = 'Address Block'; wpfAuthority = 'InsertAddressBlockCommand (direct insertion, no modal)'; avaloniaSurface = 'MailMergeInsertionPlanner plus existing engine action'; sharedPolicy = 'MailMergeInsertionPlanner'; status = 'implemented'; shellWiringGap = 'Existing shell command route is outside the ownership boundary.' },
    [ordered]@{ name = 'Greeting Line'; wpfAuthority = 'InsertGreetingLineCommand (direct insertion, no modal)'; avaloniaSurface = 'MailMergeInsertionPlanner plus existing engine action'; sharedPolicy = 'MailMergeInsertionPlanner'; status = 'implemented'; shellWiringGap = 'Existing shell command route is outside the ownership boundary.' },
    [ordered]@{ name = 'Insert Merge Field'; wpfAuthority = 'InsertMergeFieldCommand'; avaloniaSurface = 'AskMergeFieldNameAsync'; sharedPolicy = 'MailMergeInsertionPlanner'; status = 'implemented'; shellWiringGap = 'Existing callback wiring remains in forbidden MainWindow.cs.' },
    [ordered]@{ name = 'Rules'; wpfAuthority = 'MergeRuleIfDialog/MergeRuleCondDialog/MergeRulePromptDialog/MergeRuleAskSetDialog'; avaloniaSurface = 'AskMergeRuleIfAsync/AskMergeRuleConditionAsync/AskMergeRulePromptAsync/AskMergeRuleNameValueAsync'; sharedPolicy = 'MailMergeRuleDialogPlanner'; status = 'implemented'; shellWiringGap = 'Existing rule callbacks are supplied by forbidden MainWindow.cs.' },
    [ordered]@{ name = 'Match Fields'; wpfAuthority = 'MatchFieldsDialog'; avaloniaSurface = 'AskMatchFieldsAsync'; sharedPolicy = 'MailMergeMatchFieldsDialogPlanner'; status = 'implemented'; shellWiringGap = 'FreeWAvaloniaRibbonCommands.cs and MainWindow.cs are command/shell-owned and cannot be edited here.' },
    [ordered]@{ name = 'Filter and Sort Recipients'; wpfAuthority = 'FilterSortRecipientsDialog'; avaloniaSurface = 'AskFilterSortRecipientsAsync'; sharedPolicy = 'MailMergeFilterSortDialogPlanner'; status = 'implemented'; shellWiringGap = 'FreeWAvaloniaRibbonCommands.cs and MainWindow.cs are command/shell-owned and cannot be edited here.' },
    [ordered]@{ name = 'Update Labels'; wpfAuthority = 'LabelsCommand label-cell population'; avaloniaSurface = 'Existing engine ApplyDefaultLabels'; sharedPolicy = 'MailingsEnvelopeLabelPlanner'; status = 'implemented-policy-only'; shellWiringGap = 'Ribbon update-label command construction is forbidden; no new Avalonia callback was added.' },
    [ordered]@{ name = 'Preview Results'; wpfAuthority = 'PreviewNavigationDialog'; avaloniaSurface = 'AskPreviewNavigationAsync'; sharedPolicy = 'MailMergePreviewDialogPlanner'; status = 'implemented'; shellWiringGap = 'Preview action routing lives in forbidden MainWindow.cs/ribbon files.' },
    [ordered]@{ name = 'Find Recipient'; wpfAuthority = 'WPF preview recipient search gap; explicit parity addition'; avaloniaSurface = 'AskFindRecipientAsync'; sharedPolicy = 'MailMergeFindRecipientPlanner'; status = 'implemented'; shellWiringGap = 'Find action routing is shell-owned and intentionally not edited.' },
    [ordered]@{ name = 'Check for Errors'; wpfAuthority = 'Word Mailings check-for-errors three-mode contract'; avaloniaSurface = 'AskCheckForErrorsAsync'; sharedPolicy = 'MailMergeCheckForErrorsPlanner'; status = 'implemented'; shellWiringGap = 'No Avalonia command callback was added because command registry/MainWindow files are forbidden.' },
    [ordered]@{ name = 'Finish and Merge destination/options'; wpfAuthority = 'FinishMergeCommand plus MailMergeFinishPlanner'; avaloniaSurface = 'AskFinishMergeAsync'; sharedPolicy = 'MailMergeFinishPlanner.CreateDialogPlan/Plan'; status = 'implemented-policy-only'; shellWiringGap = 'Existing engine finishes to a new document; destination dialog wiring and printer/email routes are shell-owned.' },
    [ordered]@{ name = 'Send E-mail Messages'; wpfAuthority = 'EmailMergeDialog'; avaloniaSurface = 'AskEmailMergeDeliveryAsync'; sharedPolicy = 'MailMergeEmailDeliveryPlanner'; status = 'implemented-policy-only'; shellWiringGap = 'Existing route plans only and deliberately sends no mail.' }
)

$hashes = [ordered]@{}
foreach ($relative in $sourceFiles) {
    $path = Join-Path $repo ($relative -replace '/', '\')
    if (-not (Test-Path -LiteralPath $path)) { throw "Missing evidence input: $relative" }
    $hashes[$relative] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
}

$evidence = [ordered]@{
    schema = 'freex.freew.mail-merge-dialog-parity.v1'
    authority = 'FreeW.App.Host WPF dialog and command behavior'
    generatedInputs = $sourceFiles
    sourceSha256 = $hashes
    ownershipBoundary = @(
        'Do not edit MainWindow files.',
        'Do not edit ribbon construction, command registry, or profile files.',
        'Do not edit Backstage, page-layout/media/design, or shared shell files.',
        'Record shell-wiring gaps exactly instead of changing forbidden files.'
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

Generated from the WPF authority and shared/presentation/Avalonia source hashes. This report is deterministic; run `tools/Generate-FreeWMailMergeDialogParityEvidence.ps1 -Check` to verify freshness.

- Schema: `$($evidence.schema)`
- Surfaces inventoried: $($surfaces.Count)
- Implemented dialog/policy surfaces: $implemented
- Implemented policy-only surfaces awaiting forbidden shell wiring: $policyOnly
- Authority: `$($evidence.authority)`

| Surface | Status | Shared policy | Exact shell-wiring gap |
|---|---|---|---|
$gapLines

## Boundary

MainWindow, ribbon construction/command registry/profile, Backstage, page-layout/media/design, and shared shell files were not edited. The `shellWiringGap` column is the handoff list for those files.

## Freshness

The JSON records SHA-256 hashes for every authority, implementation, and focused-test input. `-Check` regenerates both artifacts in memory and fails if either committed artifact differs.
"@

if ($Check) {
    if (-not (Test-Path -LiteralPath $jsonPath) -or -not (Test-Path -LiteralPath $markdownPath)) { throw 'Generated evidence files are missing.' }
    if ([IO.File]::ReadAllText($jsonPath) -ne $jsonText -or [IO.File]::ReadAllText($markdownPath) -ne $markdownText) { throw 'Generated evidence is stale. Run the generator without -Check.' }
    Write-Output "Fresh: $jsonPath"
    Write-Output "Fresh: $markdownPath"
    exit 0
}

$utf8 = New-Object System.Text.UTF8Encoding($false)
[IO.File]::WriteAllText($jsonPath, $jsonText, $utf8)
[IO.File]::WriteAllText($markdownPath, $markdownText, $utf8)
Write-Output "Wrote $jsonPath"
Write-Output "Wrote $markdownPath"
