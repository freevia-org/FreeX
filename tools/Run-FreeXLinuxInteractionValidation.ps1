<#
.SYNOPSIS
  Runs the exhaustive FreeX Avalonia interaction-validation matrix in the Linux Docker desktop.

.DESCRIPTION
  Publishes the current FreeX Avalonia build, first drives production keyboard and pointer input
  through X11, then asks the app to validate every catalogued interaction surface and model route.
  The two evidence streams are merged into one JSON/HTML report. Only the harness-owned container
  on the requested port is stopped.

  Use -PhysicalOnly to rerun the bounded physical X11 probes and write a physical-only report
  without starting the exhaustive managed interaction catalog.
#>
[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 6082,

    [ValidateRange(1, 60)]
    [int]$TimeoutMinutes = 20,

    [ValidateRange(1, 20)]
    [int]$DialogBatchSize = 1,

    [ValidateRange(1, 32)]
    [int]$RibbonBatchSize = 8,

    [ValidateRange(1, 100)]
    [int]$ContextBatchSize = 25,

    [ValidateRange(0, 100000)]
    [int]$ContextStart = 0,

    [string]$ResumeReportDirectory = "",

    [string]$ExistingX11Manifest = "",

    [ValidateSet("all", "backstage-print", "sheet-tabs", "name-box-dropdown", "name-box-dropdown-parity", "pivot-field-list", "pivot-table-details-double-click", "autofilter-recalculation", "formula-whole-range-point", "formula-multi-area-point", "formula-multi-area-edit", "formula-reference-grip", "formula-3d-grip", "formula-3d-native-xlsx", "grid-drag", "grid-autofit", "split-pane-pointer", "outline-group", "outline-nested-group", "outline-nested-save-reopen", "outline-nested-filter-save-reopen")]
    [string]$PhysicalProbeSelector = "all",

    [string]$PhysicalDocumentPath = "",

    [switch]$SkipX11,
    [switch]$PhysicalOnly,

    [switch]$SkipImageBuild,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$harness = Join-Path $PSScriptRoot "Run-LinuxInteractiveDocker.ps1"
$containerName = "freex-linux-interactive-freex-$Port"
$x11ProbeScript = Join-Path $PSScriptRoot "LinuxInteractiveDocker/run-freex-input-probes.sh"
$native3dFixtureGenerator = Join-Path $PSScriptRoot "LinuxInteractiveDocker/New-FreeXWave66Native3DFixture.ps1"
$gridAutofitFixtureGenerator = Join-Path $PSScriptRoot "LinuxInteractiveDocker/New-FreeXWave164GridAutofitFixture.ps1"
$nestedOutlineFilterFixtureGenerator = Join-Path $PSScriptRoot "LinuxInteractiveDocker/New-FreeXWave100NestedOutlineFilterFixture.ps1"
$native3dSchemaPath = Join-Path $PSScriptRoot "LinuxInteractiveDocker/freex-native-3d-formula-validation.schema.json"
$gridAutofitSchemaPath = Join-Path $PSScriptRoot "LinuxInteractiveDocker/freex-grid-autofit-validation.schema.json"
$nameBoxObjectsSchemaPath = Join-Path $PSScriptRoot "LinuxInteractiveDocker/freex-name-box-dropdown-objects-validation.schema.json"
$pivotDetailsFixturePath = Join-Path $repoRoot "tests/FreeX.App.Avalonia.Tests/Fixtures/FreeX_wave50_pivot_fields.xlsx"
$runnerSchemaVersion = 2
$authoritativeRibbonBindingRowCount = 631
$authoritativeCollapsedRibbonGroupRowCount = 74
$resumeRequested = -not [string]::IsNullOrWhiteSpace($ResumeReportDirectory)
$reportStamp = (Get-Date).ToUniversalTime().ToString("yyyyMMddTHHmmssZ")
$reportDirectory = if ([string]::IsNullOrWhiteSpace($ResumeReportDirectory)) {
    Join-Path $repoRoot "artifacts/linux-interactive/freex/interaction-validation/$reportStamp"
} else {
    if (-not (Test-Path -LiteralPath $ResumeReportDirectory -PathType Container)) {
        throw "Resume report directory does not exist: $ResumeReportDirectory"
    }
    (Resolve-Path -LiteralPath $ResumeReportDirectory).Path
}
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$provenancePath = Join-Path $reportDirectory "resume-provenance.json"
$workspaceHasher = [Security.Cryptography.SHA256]::Create()
try {
    $normalizedRepoRoot = [IO.Path]::GetFullPath($repoRoot).TrimEnd(
        [IO.Path]::DirectorySeparatorChar,
        [IO.Path]::AltDirectorySeparatorChar).ToLowerInvariant()
    $workspaceHashBytes = $workspaceHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($normalizedRepoRoot))
    $workspaceKey = -join ($workspaceHashBytes[0..5] | ForEach-Object { $_.ToString("x2") })
} finally {
    $workspaceHasher.Dispose()
}
$appImageReference = "freex-linux-interactive-app-freex-$workspaceKey" + ":current"
$publishDirectory = Join-Path $env:TEMP "FreeX-LinuxInteractive/$workspaceKey/freex/publish/linux-x64"
$sessionBindingDirectory = Join-Path $env:TEMP "FreeX-LinuxInteractive/$workspaceKey/freex/session-bindings/$reportStamp-$PID"
New-Item -ItemType Directory -Path $sessionBindingDirectory -Force | Out-Null

function Get-SourceCommit {
    $output = @(& git -C $repoRoot rev-parse --verify HEAD 2>$null)
    $gitExitCode = $LASTEXITCODE
    $commit = if ($output.Count -gt 0) { [string]$output[0] } else { "" }
    if ($gitExitCode -ne 0 -or [string]::IsNullOrWhiteSpace($commit)) {
        throw "Could not resolve the current source commit for interaction validation provenance."
    }
    $commit.Trim()
}

function Copy-LongPathSafeFile {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $sourcePath = [IO.Path]::GetFullPath($Source)
    $destinationPath = [IO.Path]::GetFullPath($Destination)
    if ([string]::Equals($sourcePath, $destinationPath, [StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    [IO.File]::Copy("\\?\$sourcePath", "\\?\$destinationPath", $true)
}

function Get-DirectoryFingerprint {
    param([Parameter(Mandatory = $true)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Published payload directory does not exist: $Path"
    }
    $files = @(Get-ChildItem -LiteralPath $Path -File -Recurse | Sort-Object FullName)
    if ($files.Count -eq 0) {
        throw "Published payload directory is empty: $Path"
    }
    $lines = foreach ($file in $files) {
        $relative = $file.FullName.Substring($Path.Length).TrimStart(
            [IO.Path]::DirectorySeparatorChar,
            [IO.Path]::AltDirectorySeparatorChar).Replace(
                [IO.Path]::DirectorySeparatorChar,
                [char]'/')
        $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
        "$relative|$hash"
    }
    $bytes = [Text.Encoding]::UTF8.GetBytes(($lines -join [Environment]::NewLine))
    $fingerprintHasher = [Security.Cryptography.SHA256]::Create()
    try {
        $digest = $fingerprintHasher.ComputeHash($bytes)
    } finally {
        $fingerprintHasher.Dispose()
    }
    [pscustomobject]@{
        fingerprint = ([BitConverter]::ToString($digest)).Replace("-", "").ToLowerInvariant()
        fileCount = $files.Count
    }
}

function Get-AppImageId {
    $imageId = @(& docker image inspect $appImageReference --format '{{.Id}}' 2>$null)
    if ($LASTEXITCODE -ne 0 -or $imageId.Count -ne 1 -or
        [string]::IsNullOrWhiteSpace([string]$imageId[0])) {
        throw "Could not inspect the owned FreeX app image '$appImageReference' for provenance."
    }
    ([string]$imageId[0]).Trim()
}

function Get-CurrentProvenance {
    $payload = Get-DirectoryFingerprint -Path $publishDirectory
    [pscustomobject]@{
        runnerSchemaVersion = $runnerSchemaVersion
        app = "FreeX"
        platform = "linux"
        shell = "avalonia"
        sourceCommit = Get-SourceCommit
        payloadFingerprint = $payload.fingerprint
        payloadFileCount = [int]$payload.fileCount
        appImageReference = $appImageReference
        appImageId = Get-AppImageId
    }
}

function Assert-ProvenanceMatchesCurrent {
    param([Parameter(Mandatory = $true)]$Expected)

    if ([int]$Expected.runnerSchemaVersion -ne $runnerSchemaVersion) {
        throw "Report provenance schema mismatch: expected $runnerSchemaVersion, observed $($Expected.runnerSchemaVersion)."
    }
    $actual = Get-CurrentProvenance
    foreach ($property in @(
        "app", "platform", "shell", "sourceCommit", "payloadFingerprint",
        "payloadFileCount", "appImageReference", "appImageId")) {
        if ([string]$Expected.$property -ne [string]$actual.$property) {
            throw "Report provenance mismatch for '$property': expected '$($Expected.$property)', observed '$($actual.$property)'."
        }
    }
}

function Ensure-ReportProvenance {
    if ($null -eq $script:reportProvenance) {
        $script:reportProvenance = Get-CurrentProvenance
        $script:reportProvenance | ConvertTo-Json -Depth 8 |
            Set-Content -LiteralPath $provenancePath -Encoding utf8
        return
    }
    Assert-ProvenanceMatchesCurrent -Expected $script:reportProvenance
}

function Assert-Native3DPostcondition {
    param([Parameter(Mandatory = $true)][string]$EvidenceDirectory)

    if (-not (Test-Path -LiteralPath $native3dSchemaPath -PathType Leaf)) {
        throw "Native 3-D validation schema is missing: $native3dSchemaPath"
    }
    $schema = Get-Content -LiteralPath $native3dSchemaPath -Raw | ConvertFrom-Json
    if ([int]$schema.properties.schemaVersion.const -ne 1 -or
        [string]$schema.properties.format.const -ne "xlsx" -or
        @($schema.required) -join "," -ne "schemaVersion,format,source,save,reopen,package") {
        throw "Native 3-D validation schema is not the expected version 1 XLSX contract."
    }

    $postconditionPath = Join-Path $EvidenceDirectory "formula-3d-native-xlsx-postcondition.json"
    if (-not (Test-Path -LiteralPath $postconditionPath -PathType Leaf)) {
        throw "Native 3-D probe did not emit its required postcondition JSON: $postconditionPath"
    }
    try {
        $postcondition = Get-Content -LiteralPath $postconditionPath -Raw | ConvertFrom-Json
    } catch {
        throw "Native 3-D postcondition is not valid JSON: $postconditionPath"
    }

    $expectedRoot = @("schemaVersion", "format", "source", "save", "reopen", "package")
    $actualRoot = @($postcondition.PSObject.Properties.Name)
    if ((@($actualRoot | Sort-Object) -join ",") -ne (@($expectedRoot | Sort-Object) -join ",")) {
        throw "Native 3-D postcondition root fields do not match the committed schema."
    }
    $expectedPoint = "=SUM('O''Brien Data:Revenue Data'!B2:C3)"
    $expectedResized = "=SUM('O''Brien Data:Revenue Data'!B2:D4)"
    $expectedPackageFormula = "SUM('O''Brien Data:Revenue Data'!B2:D4)"
    $expectedNestedFields = @{
        source = @("path", "pointFormula", "pointResult")
        save = @("clean", "resizedFormula", "resizedResult")
        reopen = @("physical", "formula", "result")
        package = @("zip", "workbook", "formula", "cachedResult")
    }
    foreach ($section in $expectedNestedFields.Keys) {
        $actualFields = @($postcondition.$section.PSObject.Properties.Name)
        $expectedFields = @($expectedNestedFields[$section])
        if ((@($actualFields | Sort-Object) -join ",") -ne (@($expectedFields | Sort-Object) -join ",")) {
            throw "Native 3-D postcondition '$section' fields do not match the committed schema."
        }
    }
    if ([int]$postcondition.schemaVersion -ne 1 -or
        [string]$postcondition.format -ne "xlsx" -or
        [string]::IsNullOrWhiteSpace([string]$postcondition.source.path) -or
        [string]$postcondition.source.pointFormula -ne $expectedPoint -or
        [string]$postcondition.source.pointResult -notmatch '^88(?:\.0+)?$' -or
        $postcondition.save.clean -ne $true -or
        [string]$postcondition.save.resizedFormula -ne $expectedResized -or
        [string]$postcondition.save.resizedResult -notmatch '^234(?:\.0+)?$' -or
        $postcondition.reopen.physical -ne $true -or
        [string]$postcondition.reopen.formula -ne $expectedResized -or
        [string]$postcondition.reopen.result -notmatch '^234(?:\.0+)?$' -or
        $postcondition.package.zip -ne $true -or
        $postcondition.package.workbook -ne $true -or
        [string]$postcondition.package.formula -ne $expectedPackageFormula -or
        [string]$postcondition.package.cachedResult -notmatch '^234(?:\.0+)?$') {
        throw "Native 3-D postcondition failed exact formula/result/save/reopen/package validation."
    }
}

function Assert-NameBoxDropdownObjectPostcondition {
    param([Parameter(Mandatory = $true)][string]$EvidenceDirectory)

    if (-not (Test-Path -LiteralPath $nameBoxObjectsSchemaPath -PathType Leaf)) {
        throw "Name Box object validation schema is missing: $nameBoxObjectsSchemaPath"
    }
    $postconditionPath = Join-Path $EvidenceDirectory "name-box-dropdown-object-postcondition.json"
    if (-not (Test-Path -LiteralPath $postconditionPath -PathType Leaf)) {
        throw "Name Box object probe did not emit its required postcondition JSON: $postconditionPath"
    }
    try {
        $postcondition = Get-Content -LiteralPath $postconditionPath -Raw | ConvertFrom-Json
    } catch {
        throw "Name Box object postcondition is not valid JSON: $postconditionPath"
    }
    $schema = Get-Content -LiteralPath $nameBoxObjectsSchemaPath -Raw | ConvertFrom-Json
    if ([int]$schema.properties.schemaVersion.const -ne 1 -or
        [string]$schema.properties.suite.const -ne "freex-name-box-dropdown-objects-physical") {
        throw "Name Box object validation schema is not the expected version 1 contract."
    }
    $expectedOrder = @(
        "PhysicalChart",
        "PhysicalName",
        "PhysicalPicture",
        "PhysicalShape",
        "PhysicalTable",
        "PhysicalTextBox"
    )
    $actualOrder = @($postcondition.expectedOrder | ForEach-Object { [string]$_ })
    $expectedContracts = [ordered]@{
        "name-box-dropdown-chart-physical" = @{
            expectedName = "PhysicalChart"
            expectedKind = "Chart"
            expectedId = "67000000-0000-0000-0000-000000000004"
            expectedActiveCell = "D5"
        }
        "name-box-dropdown-picture-physical" = @{
            expectedName = "PhysicalPicture"
            expectedKind = "Picture"
            expectedId = "67000000-0000-0000-0000-000000000002"
            expectedActiveCell = "D3"
        }
        "name-box-dropdown-shape-physical" = @{
            expectedName = "PhysicalShape"
            expectedKind = "Shape"
            expectedId = "67000000-0000-0000-0000-000000000001"
            expectedActiveCell = "D2"
        }
        "name-box-dropdown-textbox-physical" = @{
            expectedName = "PhysicalTextBox"
            expectedKind = "TextBox"
            expectedId = "67000000-0000-0000-0000-000000000003"
            expectedActiveCell = "D4"
        }
    }
    $expectedIds = @($expectedContracts.Keys)
    $results = @($postcondition.results)
    $actualIds = @($results | ForEach-Object { [string]$_.id })
    $missing = @($expectedIds | Where-Object { $_ -notin $actualIds })
    $duplicates = @($actualIds | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)
    $failed = @($results | Where-Object { [string]$_.status -ne "passed" })
    $violations = [System.Collections.Generic.List[string]]::new()
    if ((@($actualOrder) -join "|") -ne (@($expectedOrder) -join "|")) {
        $violations.Add("expectedOrder='$(@($actualOrder) -join ',')'")
    }
    foreach ($expectedId in $expectedIds) {
        $rows = @($results | Where-Object { [string]$_.id -eq $expectedId })
        if ($rows.Count -ne 1) {
            $violations.Add("$expectedId rowCount=$($rows.Count)")
            continue
        }

        $row = $rows[0]
        $contract = $expectedContracts[$expectedId]
        foreach ($field in @("expectedName", "expectedKind", "expectedId")) {
            if ([string]$row.$field -ne [string]$contract[$field]) {
                $violations.Add("$expectedId $field='$([string]$row.$field)'")
            }
        }
        foreach ($field in @("observedName", "observedObjectKind", "observedSelectedObjectKind", "observedId", "observedNameBox", "observedActiveCell")) {
            $expectedValue = switch ($field) {
                "observedName" { $contract.expectedName }
                "observedObjectKind" { $contract.expectedKind }
                "observedSelectedObjectKind" { $contract.expectedKind }
                "observedId" { $contract.expectedId }
                "observedNameBox" { $contract.expectedName }
                "observedActiveCell" { $contract.expectedActiveCell }
            }
            if ([string]$row.$field -ne [string]$expectedValue) {
                $violations.Add("$expectedId $field='$([string]$row.$field)'")
            }
        }
        if ([string]$row.baselineStage -ne "neutral-cell-selected" -or
            -not [string]::IsNullOrEmpty([string]$row.baselineSelectedObjectKind) -or
            -not [string]::IsNullOrEmpty([string]$row.baselineSelectedObjectId) -or
            [string]$row.baselineNameBox -ne "J20" -or
            [string]$row.baselineActiveCell -ne "J20" -or
            [string]$row.observedStage -ne "object-selected" -or
            [string]$row.observedItemKind -ne "Object" -or
            [string]$row.status -ne "passed") {
            $violations.Add("$expectedId observed stage/item-kind/active-cell/status is invalid")
        }
        if ([string]$row.baselineSequence -notmatch '^\d+$' -or
            [string]$row.observedSequence -notmatch '^\d+$' -or
            [int]$row.observedSequence -le [int]$row.baselineSequence) {
            $violations.Add("$expectedId sequence is not fresh")
        }
    }
    if ([int]$postcondition.schemaVersion -ne 1 -or
        [string]$postcondition.suite -ne "freex-name-box-dropdown-objects-physical" -or
        [string]$postcondition.platform -ne "linux" -or
        [string]$postcondition.shell -ne "avalonia" -or
        [string]$postcondition.app -ne "FreeX" -or
        $results.Count -ne $expectedIds.Count -or
        $missing.Count -ne 0 -or
        $duplicates.Count -ne 0 -or
        $failed.Count -ne 0 -or
        [int]$postcondition.summary.passed -ne $expectedIds.Count -or
        [int]$postcondition.summary.failed -ne 0 -or
        [int]$postcondition.summary.total -ne $expectedIds.Count) {
        $violations.Add("root/count/status contract is invalid")
    }
    if ($violations.Count -gt 0) {
        throw "Name Box object postcondition failed exact four-kind validation: $($violations -join '; ')."
    }
}

function Assert-GridAutofitPostcondition {
    param([Parameter(Mandatory = $true)][string]$EvidenceDirectory)

    if (-not (Test-Path -LiteralPath $gridAutofitSchemaPath -PathType Leaf)) {
        throw "Grid AutoFit validation schema is missing: $gridAutofitSchemaPath"
    }
    $schema = Get-Content -LiteralPath $gridAutofitSchemaPath -Raw | ConvertFrom-Json
    if ([int]$schema.properties.schemaVersion.const -ne 2 -or
        [string]$schema.properties.suite.const -ne "freex-grid-autofit-physical" -or
        @($schema.required) -join "," -ne "schemaVersion,suite,platform,shell,app,viewport,column,row,hiddenRowBoundary") {
        throw "Grid AutoFit validation schema is not the expected version 2 contract."
    }

    $postconditionPath = Join-Path $EvidenceDirectory "grid-autofit-postcondition.json"
    if (-not (Test-Path -LiteralPath $postconditionPath -PathType Leaf)) {
        throw "Grid AutoFit probe did not emit its required schema-v2 postcondition: $postconditionPath"
    }
    try {
        $postcondition = Get-Content -LiteralPath $postconditionPath -Raw | ConvertFrom-Json
    } catch {
        throw "Grid AutoFit postcondition is not valid JSON: $postconditionPath"
    }

    $expectedRoot = @("schemaVersion", "suite", "platform", "shell", "app", "viewport", "column", "row", "hiddenRowBoundary")
    $actualRoot = @($postcondition.PSObject.Properties.Name)
    if ((@($actualRoot | Sort-Object) -join ",") -ne (@($expectedRoot | Sort-Object) -join ",")) {
        throw "Grid AutoFit postcondition root fields do not match the committed schema-v2 contract."
    }
    foreach ($section in @("viewport", "column", "row", "hiddenRowBoundary")) {
        $actualFields = @($postcondition.$section.PSObject.Properties.Name)
        $expectedFields = switch ($section) {
            "viewport" { @("width", "height", "dpi") }
            "column" { @("seedCell", "beforeSize", "afterSize", "boundaryX", "boundaryY", "grew") }
            "row" { @("seedCell", "beforeSize", "afterSize", "boundaryX", "boundaryY", "grew") }
            "hiddenRowBoundary" { @("targetStart", "targetEnd", "hiddenRowsBefore", "hiddenRowsAfter", "beforeHeights", "afterHeights", "unhidden", "sized", "boundaryX", "boundaryY") }
        }
        if ((@($actualFields | Sort-Object) -join ",") -ne (@($expectedFields | Sort-Object) -join ",")) {
            throw "Grid AutoFit postcondition '$section' fields do not match the committed schema-v2 contract."
        }
    }

    $resizeProofs = @(
        @{ Name = "column"; Cell = "A1" },
        @{ Name = "row"; Cell = "B2" }
    )
    foreach ($proof in $resizeProofs) {
        $value = $postcondition.($proof.Name)
        if ([string]$value.seedCell -ne $proof.Cell -or
            [int]$value.beforeSize -lt 1 -or
            [int]$value.afterSize -le [int]$value.beforeSize -or
            [int]$value.boundaryX -lt 1 -or
            [int]$value.boundaryY -lt 1 -or
            $value.grew -ne $true) {
            throw "Grid AutoFit $($proof.Name) postcondition did not prove exact growth from the physical boundary."
        }
    }

    $hidden = $postcondition.hiddenRowBoundary
    $hiddenRowsAfter = @($hidden.hiddenRowsAfter | ForEach-Object { [int]$_ })
    $hiddenRowsAfterValid =
        $hiddenRowsAfter.Count -le 2 -and
        (@($hiddenRowsAfter | Where-Object { $_ -lt 4 -or $_ -gt 5 }).Count -eq 0) -and
        (@($hiddenRowsAfter | Group-Object | Where-Object Count -gt 1).Count -eq 0)
    if ([int]$postcondition.schemaVersion -ne 2 -or
        [string]$postcondition.suite -ne "freex-grid-autofit-physical" -or
        [string]$postcondition.platform -ne "linux" -or
        [string]$postcondition.shell -ne "avalonia" -or
        [string]$postcondition.app -ne "FreeX" -or
        [int]$postcondition.viewport.width -ne 1280 -or
        [int]$postcondition.viewport.height -ne 820 -or
        [int]$postcondition.viewport.dpi -ne 96 -or
        (@($hidden.hiddenRowsBefore | ForEach-Object { [int]$_ }) -join ",") -ne "4,5" -or
        -not $hiddenRowsAfterValid -or
        (@($hidden.beforeHeights | ForEach-Object { [int]$_ }) -join ",") -ne "0,0" -or
        @($hidden.afterHeights).Count -ne 2 -or
        @($hidden.afterHeights | Where-Object { [int]$_ -lt 0 }).Count -gt 0 -or
        [int]$hidden.targetStart -ne 4 -or
        [int]$hidden.targetEnd -ne 5 -or
        $hidden.unhidden -isnot [bool] -or
        $hidden.sized -isnot [bool] -or
        [int]$hidden.boundaryX -lt 1 -or
        [int]$hidden.boundaryY -lt 1) {
        throw "Grid AutoFit hidden-row diagnostic does not satisfy the schema-v2 contiguous rows 4:5 contract."
    }
}

function Assert-NameBoxDropdownParityNativeContract {
    param([Parameter(Mandatory = $true)][string]$EvidenceDirectory)

    $manifestPath = Join-Path $EvidenceDirectory "name-box-dropdown-parity-manifest.json"
    $geometryPath = Join-Path $EvidenceDirectory "name-box-dropdown-parity-native.json"
    $cropPath = Join-Path $EvidenceDirectory "popup.nameBoxDropdown.png"
    $sourcePath = Join-Path $EvidenceDirectory "name-box-dropdown-parity-open-root.png"
    $beforeInventoryPath = Join-Path $EvidenceDirectory "name-box-dropdown-parity-before-x11.txt"
    $openInventoryPath = Join-Path $EvidenceDirectory "name-box-dropdown-parity-open-x11.txt"
    foreach ($requiredPath in @(
            $manifestPath,
            $geometryPath,
            $cropPath,
            $sourcePath,
            $beforeInventoryPath,
            $openInventoryPath)) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Name Box parity native evidence is missing: $requiredPath"
        }
        if ((Get-Item -LiteralPath $requiredPath).Length -le 0) {
            throw "Name Box parity native evidence is empty: $requiredPath"
        }
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $geometry = Get-Content -LiteralPath $geometryPath -Raw | ConvertFrom-Json
    } catch {
        throw "Name Box parity native manifest or geometry evidence is not valid JSON."
    }

    $surfaces = @($manifest.surfaces)
    if ([string]$manifest.platform -ne "linux" -or
        [string]$manifest.shell -ne "avalonia" -or
        $surfaces.Count -ne 1) {
        throw "Name Box parity native manifest root contract is invalid."
    }
    $surface = $surfaces[0]
    if ([string]$surface.id -ne "popup.nameBoxDropdown" -or
        [string]$surface.kind -ne "overlay" -or
        $surface.captured -ne $true -or
        [int]$surface.width -ne 208 -or
        [int]$surface.height -ne 136 -or
        [string]$surface.evidenceProvenance -ne "native-x11-root-crop" -or
        [string]$surface.png -ne "popup.nameBoxDropdown.png" -or
        [string]$surface.sourcePng -ne "name-box-dropdown-parity-open-root.png" -or
        [string]$surface.geometryEvidence -ne "name-box-dropdown-parity-native.json" -or
        [int]$surface.sourceX -lt 0 -or
        [int]$surface.sourceY -lt 0 -or
        [int]$surface.sourceWidth -lt 208 -or
        [int]$surface.sourceHeight -lt 136) {
        throw "Name Box parity surface is not an authoritative 208x136 native X11 root crop."
    }
    if ([int]$geometry.schemaVersion -ne 1 -or
        [string]$geometry.platform -ne "linux" -or
        [string]$geometry.shell -ne "avalonia" -or
        [string]$geometry.surfaceId -ne "popup.nameBoxDropdown" -or
        [string]$geometry.evidenceProvenance -ne "native-x11-root-crop" -or
        $geometry.captured -ne $true -or
        [string]$geometry.sourcePng -ne [string]$surface.sourcePng -or
        [string]$geometry.windowInventoryBefore -ne "name-box-dropdown-parity-before-x11.txt" -or
        [string]$geometry.windowInventoryOpen -ne "name-box-dropdown-parity-open-x11.txt" -or
        [string]::IsNullOrWhiteSpace([string]$geometry.sourceWindow.id) -or
        [int]$geometry.sourceWindow.x -ne [int]$surface.sourceX -or
        [int]$geometry.sourceWindow.y -ne [int]$surface.sourceY -or
        [int]$geometry.sourceWindow.width -ne [int]$surface.sourceWidth -or
        [int]$geometry.sourceWindow.height -ne [int]$surface.sourceHeight -or
        [int]$geometry.crop.x -ne [int]$surface.sourceX -or
        [int]$geometry.crop.y -ne [int]$surface.sourceY -or
        [int]$geometry.crop.width -ne 208 -or
        [int]$geometry.crop.height -ne 136 -or
        $geometry.crop.resized -ne $false) {
        throw "Name Box parity native geometry does not match the surface manifest."
    }
    $popupWindowPrefix = "$([string]$geometry.sourceWindow.id)|"
    $beforeInventory = @(Get-Content -LiteralPath $beforeInventoryPath)
    $openInventory = @(Get-Content -LiteralPath $openInventoryPath)
    if (@($beforeInventory | Where-Object { $_.StartsWith($popupWindowPrefix, [StringComparison]::Ordinal) }).Count -ne 0 -or
        @($openInventory | Where-Object {
            $_.StartsWith($popupWindowPrefix, [StringComparison]::Ordinal) -and
            $_.IndexOf("X=$([int]$surface.sourceX) ", [StringComparison]::Ordinal) -ge 0 -and
            $_.IndexOf("Y=$([int]$surface.sourceY) ", [StringComparison]::Ordinal) -ge 0 -and
            $_.IndexOf("WIDTH=$([int]$surface.sourceWidth) ", [StringComparison]::Ordinal) -ge 0 -and
            $_.IndexOf("HEIGHT=$([int]$surface.sourceHeight) ", [StringComparison]::Ordinal) -ge 0
        }).Count -ne 1) {
        throw "Name Box parity popup is not a newly visible X11 window with the declared geometry."
    }

    $png = [IO.File]::ReadAllBytes($cropPath)
    if ($png.Length -lt 24 -or
        $png[0] -ne 0x89 -or $png[1] -ne 0x50 -or $png[2] -ne 0x4e -or $png[3] -ne 0x47) {
        throw "Name Box parity native crop is not a valid PNG."
    }
    $pngWidth = ($png[16] -shl 24) -bor ($png[17] -shl 16) -bor ($png[18] -shl 8) -bor $png[19]
    $pngHeight = ($png[20] -shl 24) -bor ($png[21] -shl 16) -bor ($png[22] -shl 8) -bor $png[23]
    if ($pngWidth -ne 208 -or $pngHeight -ne 136) {
        throw "Name Box parity native crop pixels must be 208x136, were ${pngWidth}x${pngHeight}."
    }
}

function Assert-NameBoxDropdownInteractionPostcondition {
    param([Parameter(Mandatory = $true)][string]$EvidenceDirectory)

    $postconditionPath = Join-Path $EvidenceDirectory "name-box-dropdown-interaction-postcondition.txt"
    if (-not (Test-Path -LiteralPath $postconditionPath -PathType Leaf)) {
        throw "Name Box interaction probe did not emit its required postcondition: $postconditionPath"
    }

    $lines = @(Get-Content -LiteralPath $postconditionPath)
    $expected = @(
        "keyboard-opened=true",
        "keyboard-gesture=Alt+Down,Home,Down,Down,Down,Down,Enter",
        "keyboard-clipboard=North`t120",
        "mouse-opened=true",
        "mouse-gesture=NameBoxChevron,PhysicalTableRow",
        "mouse-clipboard=North`t120"
    )
    $missing = @($expected | Where-Object { $_ -notin $lines })
    if ($missing.Count -ne 0) {
        throw "Name Box interaction postcondition failed native keyboard/mouse contract: $($missing -join '; ')."
    }
}

function Assert-FormulaWholeRangePointPostcondition {
    param([Parameter(Mandatory = $true)][string]$EvidenceDirectory)

    $postconditionPath = Join-Path $EvidenceDirectory "formula-whole-range-point-postcondition.txt"
    if (-not (Test-Path -LiteralPath $postconditionPath -PathType Leaf)) {
        throw "Whole-range formula point probe did not emit its required postcondition: $postconditionPath"
    }

    $lines = @(Get-Content -LiteralPath $postconditionPath)
    $expected = @(
        "schema-version=1",
        "selector=formula-whole-range-point",
        "column-header-expected=B:B",
        "column-header-formula-bar-clipboard==SUM(B:B)",
        "column-header-cell-formula==SUM(B:B)",
        "column-header-cell-package-formula==SUM(B:B)",
        "column-header-edit-active-before-commit=true",
        "column-header-passed=true",
        "row-header-expected=3:3",
        "row-header-formula-bar-clipboard==SUM(3:3)",
        "row-header-cell-formula==SUM(3:3)",
        "row-header-cell-package-formula==SUM(3:3)",
        "row-header-edit-active-before-commit=true",
        "row-header-passed=true",
        "select-all-expected=A1:XFD1048576",
        "select-all-formula-bar-clipboard==SUM(A1:XFD1048576)",
        "select-all-cell-package-formula-after-cancel=",
        "select-all-edit-active-before-cancel=true",
        "select-all-passed=true"
    )
    $missing = @($expected | Where-Object { $_ -notin $lines })
    if ($missing.Count -ne 0) {
        throw "Whole-range formula point postcondition failed exact semantic contract: $($missing -join '; ')."
    }
}

function Start-ValidationSession {
    param(
        [string[]]$AppArgument = @(),
        [string]$DocumentPath = "",
        [string]$MemoryLimit = "",
        [switch]$ReusePublishedPayload,
        [ValidateSet("Application", "Validation", "TestSupport")]
        [string]$HostMode = "Application"
    )

    $metadataPath = Join-Path $sessionBindingDirectory ("session-$([guid]::NewGuid().ToString('N')).json")
    $startArguments = @{
        Action = "Start"
        App = "FreeX"
        Port = $Port
        Replace = $true
        SessionMetadataPath = $metadataPath
        AppArgument = $AppArgument
        HostMode = $HostMode
    }
    if (-not [string]::IsNullOrWhiteSpace($DocumentPath)) {
        $startArguments.DocumentPath = $DocumentPath
    }
    if ($SkipImageBuild -or $ReusePublishedPayload) { $startArguments.SkipImageBuild = $true }
    if ($SkipPublish -or $ReusePublishedPayload) { $startArguments.SkipPublish = $true }
    if (-not [string]::IsNullOrWhiteSpace($MemoryLimit)) {
        $startArguments.MemoryLimit = $MemoryLimit
    }

    & $harness @startArguments
    if (-not $?) {
        throw "Linux interaction-validation harness failed to start."
    }
    if (-not (Test-Path -LiteralPath $metadataPath -PathType Leaf)) {
        throw "Linux interaction-validation harness did not write its bound session metadata: $metadataPath"
    }

    try {
        $session = Get-Content -LiteralPath $metadataPath -Raw | ConvertFrom-Json
    } catch {
        throw "Linux interaction-validation bound session metadata is not valid JSON: $metadataPath"
    }
    if ([string]::IsNullOrWhiteSpace([string]$session.sessionDirectory) -or
        -not (Test-Path -LiteralPath ([string]$session.sessionDirectory) -PathType Container)) {
        throw "Linux interaction-validation bound session metadata has no existing session directory: $metadataPath"
    }
    $session
}

if (-not $resumeRequested -and $SkipPublish) {
    throw "-SkipPublish requires -ResumeReportDirectory with an existing provenance record."
}
if ($PhysicalOnly -and $SkipX11) {
    throw "-PhysicalOnly cannot be combined with -SkipX11; physical-only mode runs the bounded X11 probes."
}
if ($resumeRequested) {
    if (-not (Test-Path -LiteralPath $provenancePath -PathType Leaf)) {
        throw "Resume report is missing provenance metadata: $provenancePath"
    }
    try {
        $script:reportProvenance = Get-Content -LiteralPath $provenancePath -Raw | ConvertFrom-Json
    } catch {
        throw "Resume report provenance is not valid JSON: $provenancePath"
    }
    Assert-ProvenanceMatchesCurrent -Expected $script:reportProvenance
}

function Read-CompletedJsonManifest {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][datetime]$Deadline,
        [string]$ExpectedSection = "",
        [bool]$ExpectedIncludeCoreResults = $false,
        [bool]$ExpectedRibbonOnly = $false,
        [int]$ExpectedDialogStart = 0,
        [int]$ExpectedDialogCount = 0,
        [int]$ExpectedRibbonCommandStart = 0,
        [int]$ExpectedRibbonCommandCount = 0,
        [int]$ExpectedContextMenuDispatchStart = 0,
        [int]$ExpectedContextMenuDispatchCount = 0,
        [bool]$RequireRunnerMetadata = $false
    )

    $lastLength = "missing"
    $lastError = "manifest was not observed"
    while ((Get-Date) -lt $Deadline) {
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            try {
                $before = Get-Item -LiteralPath $Path
                $raw = [IO.File]::ReadAllText($Path)
                $after = Get-Item -LiteralPath $Path
                $lastLength = [string]$after.Length
                if ($before.Length -ne $after.Length -or
                    $before.LastWriteTimeUtc -ne $after.LastWriteTimeUtc) {
                    throw "manifest changed while it was being read"
                }

                $candidate = $raw | ConvertFrom-Json
                Start-Sleep -Milliseconds 100
                $stableInfo = Get-Item -LiteralPath $Path
                if ($stableInfo.Length -ne $after.Length -or
                    $stableInfo.LastWriteTimeUtc -ne $after.LastWriteTimeUtc) {
                    throw "manifest changed before it became stable"
                }
                $stableRaw = [IO.File]::ReadAllText($Path)
                if ($stableRaw -ne $raw) {
                    throw "manifest content changed before it became stable"
                }

                if (-not [string]::IsNullOrWhiteSpace($ExpectedSection)) {
                    Validate-InteractionManifest `
                        $candidate `
                        $ExpectedSection `
                        $ExpectedIncludeCoreResults `
                        $ExpectedRibbonOnly `
                        $ExpectedDialogStart `
                        $ExpectedDialogCount `
                        $ExpectedRibbonCommandStart `
                        $ExpectedRibbonCommandCount `
                        $ExpectedContextMenuDispatchStart `
                        $ExpectedContextMenuDispatchCount `
                        $RequireRunnerMetadata | Out-Null
                }
                return $candidate
            } catch {
                $lastError = $_.Exception.Message
            }
        } else {
            $lastError = "manifest path does not exist"
        }
        Start-Sleep -Milliseconds 500
    }
    throw "Timed out waiting for a stable interaction manifest; path='$Path'; length=$lastLength; parseError=$lastError"
}

$script:catalogReference = $null

function Get-ManifestProperty {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $property = $Manifest.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "Interaction manifest is missing required property '$Name'."
    }
    $property.Value
}

function Get-ManifestStringArray {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $property = $Manifest.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "Interaction manifest is missing required property '$Name'."
    }
    $value = $property.Value
    if ($null -eq $value) {
        throw "Interaction manifest property '$Name' must be an array."
    }
    @($value | ForEach-Object { [string]$_ })
}

function Assert-ManifestCatalog {
    param([Parameter(Mandatory = $true)]$Manifest)

    if ([int](Get-ManifestProperty $Manifest "schemaVersion") -ne 2 -or
        [string](Get-ManifestProperty $Manifest "platform") -ne "linux" -or
        [string](Get-ManifestProperty $Manifest "shell") -ne "avalonia") {
        throw "Interaction manifest schema/platform/shell mismatch; expected schema 2, linux, avalonia."
    }

    $catalog = [ordered]@{
        DialogCatalogIds = Get-ManifestStringArray $Manifest "dialogCatalogIds"
        RibbonCommandCatalogIds = Get-ManifestStringArray $Manifest "ribbonCommandCatalogIds"
        ContextMenuDispatchCatalogIds = Get-ManifestStringArray $Manifest "contextMenuDispatchCatalogIds"
        ContextMenuFamilyCatalogIds = Get-ManifestStringArray $Manifest "contextMenuFamilyCatalogIds"
        ContextMenuVariantCatalogIds = Get-ManifestStringArray $Manifest "contextMenuVariantCatalogIds"
    }
    $counts = [ordered]@{
        DialogCatalogIds = [int](Get-ManifestProperty $Manifest "dialogCatalogCount")
        RibbonCommandCatalogIds = [int](Get-ManifestProperty $Manifest "ribbonCommandCatalogCount")
        ContextMenuDispatchCatalogIds = [int](Get-ManifestProperty $Manifest "contextMenuDispatchCatalogCount")
        ContextMenuFamilyCatalogIds = $catalog.ContextMenuFamilyCatalogIds.Count
        ContextMenuVariantCatalogIds = $catalog.ContextMenuVariantCatalogIds.Count
    }
    foreach ($name in $catalog.Keys) {
        $values = @($catalog[$name])
        if ($values.Count -ne $counts[$name]) {
            throw "Interaction manifest catalog '$name' count mismatch: declared $($counts[$name]), observed $($values.Count)."
        }
        $duplicates = @($values | Group-Object | Where-Object Count -gt 1)
        if ($duplicates.Count -gt 0) {
            throw "Interaction manifest catalog '$name' contains duplicate IDs: $($duplicates.Name -join ', ')."
        }
    }

    $snapshot = [pscustomobject]$catalog
    if ($null -eq $script:catalogReference) {
        $script:catalogReference = $snapshot
    } else {
        foreach ($name in $catalog.Keys) {
            $actual = @($snapshot.$name)
            $expected = @($script:catalogReference.$name)
            if ($actual.Count -ne $expected.Count -or
                (($actual -join "`n") -ne ($expected -join "`n"))) {
                throw "Interaction manifest catalog '$name' changed between batches."
            }
        }
    }
    $snapshot
}

function Get-ExpectedManifestSelectionIds {
    param(
        [Parameter(Mandatory = $true)][string]$Section,
        [Parameter(Mandatory = $true)][int]$DialogStart,
        [Parameter(Mandatory = $true)][int]$DialogCount,
        [Parameter(Mandatory = $true)][int]$RibbonCommandStart,
        [Parameter(Mandatory = $true)][int]$RibbonCommandCount,
        [Parameter(Mandatory = $true)][int]$ContextMenuDispatchStart,
        [Parameter(Mandatory = $true)][int]$ContextMenuDispatchCount
    )

    $catalog = $script:catalogReference
    switch ($Section) {
        "dialogs" { return @($catalog.DialogCatalogIds | Select-Object -Skip $DialogStart -First $DialogCount) }
        "ribbon-only" { return @($catalog.RibbonCommandCatalogIds | Select-Object -Skip $RibbonCommandStart -First $RibbonCommandCount) }
        "context-menus" { return @($catalog.ContextMenuDispatchCatalogIds | Select-Object -Skip $ContextMenuDispatchStart -First $ContextMenuDispatchCount) }
        "ribbon-bindings" { return @($catalog.RibbonCommandCatalogIds) }
        default { return @() }
    }
}

function ConvertTo-ManifestEscapedId {
    param([Parameter(Mandatory = $true)][string]$Value)

    # Windows PowerShell runs on .NET Framework, whose EscapeDataString leaves
    # these RFC 3986 reserved characters unescaped. The app runs on modern .NET.
    ([Uri]::EscapeDataString($Value)).
        Replace("!", "%21").
        Replace("*", "%2A").
        Replace("'", "%27").
        Replace("(", "%28").
        Replace(")", "%29")
}

function Assert-ManifestIdentity {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$Section,
        [Parameter(Mandatory = $true)][bool]$IncludeCoreResults,
        [Parameter(Mandatory = $true)][bool]$RibbonOnly,
        [Parameter(Mandatory = $true)][int]$DialogStart,
        [Parameter(Mandatory = $true)][int]$DialogCount,
        [Parameter(Mandatory = $true)][int]$RibbonCommandStart,
        [Parameter(Mandatory = $true)][int]$RibbonCommandCount,
        [Parameter(Mandatory = $true)][int]$ContextMenuDispatchStart,
        [Parameter(Mandatory = $true)][int]$ContextMenuDispatchCount
    )

    Assert-ManifestCatalog $Manifest | Out-Null
    $identity = [ordered]@{
        validationSection = $Section
        includeCoreResults = $IncludeCoreResults
        ribbonOnly = $RibbonOnly
        dialogStart = $DialogStart
        dialogCount = $DialogCount
        ribbonCommandStart = $RibbonCommandStart
        ribbonCommandCount = $RibbonCommandCount
        contextMenuDispatchStart = $ContextMenuDispatchStart
        contextMenuDispatchCount = $ContextMenuDispatchCount
    }
    foreach ($name in $identity.Keys) {
        $observed = Get-ManifestProperty $Manifest $name
        if ([string]$observed -ne [string]$identity[$name]) {
            throw "Interaction manifest batch identity mismatch for '$name': expected '$($identity[$name])', observed '$observed'."
        }
    }
    $expectedSelection = @(Get-ExpectedManifestSelectionIds -Section $Section -DialogStart $DialogStart -DialogCount $DialogCount -RibbonCommandStart $RibbonCommandStart -RibbonCommandCount $RibbonCommandCount -ContextMenuDispatchStart $ContextMenuDispatchStart -ContextMenuDispatchCount $ContextMenuDispatchCount)
    $observedSelection = @(Get-ManifestStringArray $Manifest "validationSelectionIds")
    if ($observedSelection.Count -ne $expectedSelection.Count -or
        (($observedSelection -join "`n") -ne ($expectedSelection -join "`n"))) {
        throw "Interaction manifest batch selection IDs mismatch for '$Section'."
    }
}

function Assert-ManifestResultShape {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$Section,
        [Parameter(Mandatory = $true)][int]$DialogStart,
        [Parameter(Mandatory = $true)][int]$DialogCount,
        [Parameter(Mandatory = $true)][int]$RibbonCommandStart,
        [Parameter(Mandatory = $true)][int]$RibbonCommandCount,
        [Parameter(Mandatory = $true)][int]$ContextMenuDispatchStart,
        [Parameter(Mandatory = $true)][int]$ContextMenuDispatchCount
    )

    $results = @((Get-ManifestProperty $Manifest "results"))
    $duplicateResultKeys = @($results | ForEach-Object { "$($_.category)|$($_.id)" } | Group-Object | Where-Object Count -gt 1)
    if ($duplicateResultKeys.Count -gt 0) {
        throw "Interaction manifest contains duplicate result IDs: $($duplicateResultKeys.Name -join ', ')."
    }
    $selected = @(Get-ExpectedManifestSelectionIds -Section $Section -DialogStart $DialogStart -DialogCount $DialogCount -RibbonCommandStart $RibbonCommandStart -RibbonCommandCount $RibbonCommandCount -ContextMenuDispatchStart $ContextMenuDispatchStart -ContextMenuDispatchCount $ContextMenuDispatchCount)
    switch ($Section) {
        "dialogs" {
            foreach ($category in @("dialog-inventory", "dialog-contract")) {
                $ids = @($results | Where-Object category -eq $category | ForEach-Object id | Sort-Object)
                $expected = @($selected | Sort-Object)
                if (($ids -join "`n") -ne ($expected -join "`n")) {
                    throw "Dialog batch '$Section' has unexpected $category IDs."
                }
            }
        }
        "ribbon-only" {
            $expected = @($selected | ForEach-Object { "ribbon-command-behavior/$(ConvertTo-ManifestEscapedId $_)" } | Sort-Object)
            $actual = @($results | Where-Object category -eq "ribbon-command-behavior" | ForEach-Object id | Sort-Object)
            if (($actual -join "`n") -ne ($expected -join "`n")) {
                throw "Ribbon batch has unexpected command behavior IDs."
            }
        }
        "context-menus" {
            $families = @($results | Where-Object category -eq "context-menu-family" | ForEach-Object id | Sort-Object)
            $variants = @($results | Where-Object category -eq "context-menu-variant" | ForEach-Object id | Sort-Object)
            if (($families -join "`n") -ne (@($script:catalogReference.ContextMenuFamilyCatalogIds | Sort-Object) -join "`n") -or
                ($variants -join "`n") -ne (@($script:catalogReference.ContextMenuVariantCatalogIds | Sort-Object) -join "`n")) {
                throw "Context-menu batch is missing authoritative family or variant aggregate rows."
            }
            $commandCount = @($results | Where-Object category -eq "context-menu-command").Count
            if (($selected.Count -eq 0 -and $commandCount -ne 0) -or ($selected.Count -gt 0 -and $commandCount -eq 0)) {
                throw "Context-menu batch command result count does not match its selection range."
            }
        }
        "ribbon-bindings" {
            if (@($results | Where-Object category -eq "ribbon-command").Count -ne $authoritativeRibbonBindingRowCount -or
                @($results | Where-Object category -eq "ribbon-collapsed-group").Count -ne $authoritativeCollapsedRibbonGroupRowCount) {
                throw "Ribbon binding section result counts are not authoritative."
            }
        }
        "shortcuts" {
            if (@($results | Where-Object category -eq "keyboard-shortcut").Count -ne 79 -or
                @($results | Where-Object category -eq "shortcut-scenario").Count -ne 276) {
                throw "Shortcut section result counts are not authoritative."
            }
        }
        "range-inventory" {
            if (@($results | Where-Object category -eq "range-selection-inventory").Count -ne 31) {
                throw "Range inventory section result count is not authoritative."
            }
        }
        "editing" {
            $expected = @(
                "cell-inline-edit",
                "cell-inline-formula-edit-point-mode",
                "formula-bar-edit-point-mode",
                "cell-inline-formula-point-range-drag")
            $actual = @($results | Where-Object category -eq "worksheet-editing" | ForEach-Object id | Sort-Object)
            if (($actual -join "`n") -ne (@($expected | Sort-Object) -join "`n")) {
                throw "Editing section result IDs are not authoritative."
            }
        }
        "quick-analysis-drawing" {
            $expectedQuickAnalysis = @(
                "quick-analysis.conditional-format",
                "quick-analysis.total")
            $expectedDrawing = @(
                "drawing.shape.move",
                "drawing.shape.resize",
                "drawing.shape.rotate",
                "drawing.shape.capture-loss-no-op")
            $actualQuickAnalysis = @($results | Where-Object category -eq "quick-analysis" | ForEach-Object id | Sort-Object)
            $actualDrawing = @($results | Where-Object category -eq "drawing-pointer" | ForEach-Object id | Sort-Object)
            if (($actualQuickAnalysis -join "`n") -ne (@($expectedQuickAnalysis | Sort-Object) -join "`n") -or
                ($actualDrawing -join "`n") -ne (@($expectedDrawing | Sort-Object) -join "`n")) {
                throw "Quick Analysis/drawing section result IDs are not authoritative."
            }
        }
    }
}

function Set-RunnerManifestMetadata {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$Section,
        [Parameter(Mandatory = $true)][int]$DialogStart,
        [Parameter(Mandatory = $true)][int]$DialogCount,
        [Parameter(Mandatory = $true)][int]$RibbonCommandStart,
        [Parameter(Mandatory = $true)][int]$RibbonCommandCount,
        [Parameter(Mandatory = $true)][int]$ContextMenuDispatchStart,
        [Parameter(Mandatory = $true)][int]$ContextMenuDispatchCount
    )

    $identity = "$Section|dialog=$DialogStart+$DialogCount|ribbon=$RibbonCommandStart+$RibbonCommandCount|context=$ContextMenuDispatchStart+$ContextMenuDispatchCount"
    foreach ($property in @{
        runnerSchemaVersion = $runnerSchemaVersion
        sourceCommit = $script:reportProvenance.sourceCommit
        payloadFingerprint = $script:reportProvenance.payloadFingerprint
        payloadFileCount = [int]$script:reportProvenance.payloadFileCount
        appImageReference = $script:reportProvenance.appImageReference
        appImageId = $script:reportProvenance.appImageId
        runnerBatchIdentity = $identity
    }.GetEnumerator()) {
        $Manifest | Add-Member -NotePropertyName $property.Key -NotePropertyValue $property.Value -Force
    }
    $Manifest
}

function Validate-InteractionManifest {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$Section,
        [Parameter(Mandatory = $true)][bool]$IncludeCoreResults,
        [Parameter(Mandatory = $true)][bool]$RibbonOnly,
        [Parameter(Mandatory = $true)][int]$DialogStart,
        [Parameter(Mandatory = $true)][int]$DialogCount,
        [Parameter(Mandatory = $true)][int]$RibbonCommandStart,
        [Parameter(Mandatory = $true)][int]$RibbonCommandCount,
        [Parameter(Mandatory = $true)][int]$ContextMenuDispatchStart,
        [Parameter(Mandatory = $true)][int]$ContextMenuDispatchCount,
        [Parameter(Mandatory = $true)][bool]$RequireRunnerMetadata
    )

    if ($Manifest.error) {
        throw "Interaction manifest contains an application failure: $($Manifest.error)"
    }
    Assert-ManifestIdentity $Manifest $Section $IncludeCoreResults $RibbonOnly $DialogStart $DialogCount $RibbonCommandStart $RibbonCommandCount $ContextMenuDispatchStart $ContextMenuDispatchCount
    Assert-ManifestResultShape $Manifest $Section $DialogStart $DialogCount $RibbonCommandStart $RibbonCommandCount $ContextMenuDispatchStart $ContextMenuDispatchCount
    $identity = "$Section|dialog=$DialogStart+$DialogCount|ribbon=$RibbonCommandStart+$RibbonCommandCount|context=$ContextMenuDispatchStart+$ContextMenuDispatchCount"
    if ($RequireRunnerMetadata) {
        foreach ($property in @("runnerSchemaVersion", "sourceCommit", "payloadFingerprint", "payloadFileCount", "appImageReference", "appImageId", "runnerBatchIdentity")) {
            if ($null -eq $Manifest.PSObject.Properties[$property]) {
                throw "Resumed interaction manifest is missing runner provenance '$property'."
            }
        }
        foreach ($property in @("sourceCommit", "payloadFingerprint", "payloadFileCount", "appImageReference", "appImageId")) {
            if ([string]$Manifest.$property -ne [string]$script:reportProvenance.$property) {
                throw "Resumed interaction manifest provenance mismatch for '$property'."
            }
        }
        if ([int]$Manifest.runnerSchemaVersion -ne $runnerSchemaVersion -or
            [string]$Manifest.runnerBatchIdentity -ne $identity) {
            throw "Resumed interaction manifest runner schema or batch identity mismatch."
        }
    }
    $Manifest
}

function Save-ValidatedManifest {
    param(
        [Parameter(Mandatory = $true)]$Manifest,
        [Parameter(Mandatory = $true)][string]$Destination,
        [Parameter(Mandatory = $true)][string]$Section,
        [Parameter(Mandatory = $true)][bool]$IncludeCoreResults,
        [Parameter(Mandatory = $true)][bool]$RibbonOnly,
        [Parameter(Mandatory = $true)][int]$DialogStart,
        [Parameter(Mandatory = $true)][int]$DialogCount,
        [Parameter(Mandatory = $true)][int]$RibbonCommandStart,
        [Parameter(Mandatory = $true)][int]$RibbonCommandCount,
        [Parameter(Mandatory = $true)][int]$ContextMenuDispatchStart,
        [Parameter(Mandatory = $true)][int]$ContextMenuDispatchCount,
        [bool]$RequireRunnerMetadata = $false
    )

    Validate-InteractionManifest $Manifest $Section $IncludeCoreResults $RibbonOnly $DialogStart $DialogCount $RibbonCommandStart $RibbonCommandCount $ContextMenuDispatchStart $ContextMenuDispatchCount $RequireRunnerMetadata | Out-Null
    Set-RunnerManifestMetadata $Manifest $Section $DialogStart $DialogCount $RibbonCommandStart $RibbonCommandCount $ContextMenuDispatchStart $ContextMenuDispatchCount | Out-Null
    $Manifest | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $Destination -Encoding utf8
    $Manifest
}

function Merge-ContextMenuAggregateResults {
    param([Parameter(Mandatory = $true)][object[]]$Results)

    $aggregateCategories = @("context-menu-family", "context-menu-variant")
    $aggregateRows = @($Results | Where-Object { $_.category -in $aggregateCategories })
    foreach ($category in $aggregateCategories) {
        $expectedIds = if ($category -eq "context-menu-family") {
            @($script:catalogReference.ContextMenuFamilyCatalogIds)
        } else {
            @($script:catalogReference.ContextMenuVariantCatalogIds)
        }
        $observedIds = @($aggregateRows | Where-Object category -eq $category | ForEach-Object id | Sort-Object -Unique)
        if (($observedIds -join "`n") -ne (@($expectedIds | Sort-Object) -join "`n")) {
            throw "Context-menu aggregate coverage is missing authoritative $category IDs."
        }
    }

    $merged = foreach ($group in @($aggregateRows | Group-Object category, id)) {
        $rows = @($group.Group)
        $selectedTotal = 0
        $expectedTotal = $null
        $passed = 0
        $failed = 0
        $skipped = 0
        $batchFailed = $false
        foreach ($row in $rows) {
            $match = [regex]::Match([string]$row.evidence, 'coverage=(\d+)/(\d+); batch-status=([^;]+); observed-passed=(\d+); observed-failed=(\d+); observed-skipped=(\d+)')
            if (-not $match.Success) {
                throw "Context-menu aggregate '$($group.Name)' has malformed coverage evidence."
            }
            $selected = [int]$match.Groups[1].Value
            $total = [int]$match.Groups[2].Value
            if ($null -eq $expectedTotal) { $expectedTotal = $total }
            if ($total -ne $expectedTotal -or $selected -gt $total) {
                throw "Context-menu aggregate '$($group.Name)' has inconsistent coverage totals."
            }
            $selectedTotal += $selected
            $passed += [int]$match.Groups[4].Value
            $observedFailed = [int]$match.Groups[5].Value
            $failed += $observedFailed
            $skipped += [int]$match.Groups[6].Value
            $batchStatus = $match.Groups[3].Value
            $batchFailed = $batchFailed -or
                $observedFailed -gt 0 -or
                ($selected -gt 0 -and $batchStatus -eq "failed")
        }
        $status = if ($batchFailed -or $failed -gt 0) {
            "failed"
        } elseif ($selectedTotal -lt $expectedTotal) {
            "skipped"
        } elseif ($passed -gt 0) {
            "passed"
        } else {
            "skipped"
        }
        [pscustomobject]@{
            id = [string]$rows[0].id
            category = [string]$rows[0].category
            status = $status
            evidenceLevel = if ($selectedTotal -eq $expectedTotal) { "executable-command-inventory" } else { "bounded-batch-aggregate-incomplete" }
            evidence = "coverage=$selectedTotal/$expectedTotal; batches=$($rows.Count); observed-passed=$passed; observed-failed=$failed; observed-skipped=$skipped"
            note = if ($selectedTotal -eq $expectedTotal) {
                "Every authoritative execution key in this aggregate was dispatched across the merged bounded batches."
            } else {
                "Only $selectedTotal of $expectedTotal execution keys were merged; this aggregate is not credited until all batches are present."
            }
        }
    }
    @($Results | Where-Object { $_.category -notin $aggregateCategories }) + @($merged)
}

try {
    if ($PhysicalProbeSelector -eq "formula-3d-native-xlsx") {
        if ([string]::IsNullOrWhiteSpace($PhysicalDocumentPath)) {
            $PhysicalDocumentPath = Join-Path $reportDirectory "fixtures/freex-wave66-native-3d.xlsx"
            & $native3dFixtureGenerator -OutputPath $PhysicalDocumentPath
        }
        if ([IO.Path]::GetExtension($PhysicalDocumentPath) -ine ".xlsx") {
            throw "formula-3d-native-xlsx requires an .xlsx PhysicalDocumentPath."
        }
    }
    if ($PhysicalProbeSelector -eq "grid-autofit") {
        if ([string]::IsNullOrWhiteSpace($PhysicalDocumentPath)) {
            $PhysicalDocumentPath = Join-Path $reportDirectory "fixtures/freex-wave164-grid-autofit.xlsx"
            & $gridAutofitFixtureGenerator -OutputPath $PhysicalDocumentPath
        }
        if (-not (Test-Path -LiteralPath $PhysicalDocumentPath -PathType Leaf) -or
            [IO.Path]::GetExtension($PhysicalDocumentPath) -ine ".xlsx") {
            throw "grid-autofit requires an existing .xlsx PhysicalDocumentPath."
        }
    }
    if ($PhysicalProbeSelector -eq "outline-nested-save-reopen" -and
        ([IO.Path]::GetExtension($PhysicalDocumentPath) -ine ".xlsx")) {
        throw "outline-nested-save-reopen requires an .xlsx PhysicalDocumentPath."
    }
    if ($PhysicalProbeSelector -eq "outline-nested-filter-save-reopen") {
        if ([string]::IsNullOrWhiteSpace($PhysicalDocumentPath)) {
            $PhysicalDocumentPath = Join-Path $reportDirectory "fixtures/freex-wave100-nested-outline-filter.xlsx"
            & $nestedOutlineFilterFixtureGenerator -OutputPath $PhysicalDocumentPath
        }
        if (-not (Test-Path -LiteralPath $PhysicalDocumentPath -PathType Leaf) -or
            [IO.Path]::GetExtension($PhysicalDocumentPath) -ine ".xlsx") {
            throw "outline-nested-filter-save-reopen requires an existing .xlsx PhysicalDocumentPath."
        }
    }
    if ($PhysicalProbeSelector -in @("pivot-field-list", "pivot-table-details-double-click")) {
        if ([string]::IsNullOrWhiteSpace($PhysicalDocumentPath)) {
            $PhysicalDocumentPath = $pivotDetailsFixturePath
        }
        if (-not (Test-Path -LiteralPath $PhysicalDocumentPath -PathType Leaf) -or
            [IO.Path]::GetExtension($PhysicalDocumentPath) -ine ".xlsx") {
            throw "$PhysicalProbeSelector requires an existing .xlsx PhysicalDocumentPath."
        }
    }
    if ($SkipX11) {
        if ([string]::IsNullOrWhiteSpace($ExistingX11Manifest) -or
            -not (Test-Path -LiteralPath $ExistingX11Manifest -PathType Leaf)) {
            throw "-SkipX11 requires -ExistingX11Manifest with a completed physical-probe manifest."
        }
        $x11Manifest = Get-Content -LiteralPath $ExistingX11Manifest -Raw | ConvertFrom-Json
        $x11ManifestPath = (Resolve-Path -LiteralPath $ExistingX11Manifest).Path
        $x11ProbeExit = if (@($x11Manifest.results | Where-Object status -eq "failed").Count -gt 0) { 1 } else { 0 }

        # Bounded validation batches always reuse the published payload. A resumed run that skips
        # physical X11 probes still needs to refresh that payload once unless explicitly told not to.
        if (-not $SkipPublish) {
            Start-ValidationSession -AppArgument @() | Out-Null
            Ensure-ReportProvenance
            & $harness -Action Stop -App FreeX -Port $Port
        }
    } else {
        # Phase one sends real X11 keyboard and pointer events through the production handlers.
        $physicalDocumentName = if ([string]::IsNullOrWhiteSpace($PhysicalDocumentPath)) {
            ""
        } else {
            Split-Path -Leaf ([IO.Path]::GetFullPath($PhysicalDocumentPath))
        }
        $x11AppArguments = @(
            "--freex-pivot-runtime-evidence",
            "/work/x11-validation/pivot-runtime-evidence.jsonl"
        )
        if ($PhysicalProbeSelector -eq "name-box-dropdown") {
            $x11AppArguments += @(
                "--freex-name-box-dropdown-physical",
                "--freex-name-box-dropdown-physical-evidence",
                "/work/x11-validation/name-box-dropdown-object-state.jsonl"
            )
        } elseif ($PhysicalProbeSelector -eq "name-box-dropdown-parity") {
            $x11AppArguments += "--freex-name-box-dropdown-parity-physical"
        }
        $x11Session = Start-ValidationSession -HostMode TestSupport -AppArgument $x11AppArguments -DocumentPath $PhysicalDocumentPath
        if ($PhysicalOnly) {
            Ensure-ReportProvenance
        }

        & docker cp $x11ProbeScript "${containerName}:/tmp/run-freex-input-probes.sh"
        if ($LASTEXITCODE -ne 0) { throw "Could not copy X11 input probes into '$containerName'." }
        $probeEnvironment = @(
            "DISPLAY=:99",
            "FREEX_X11_PROBE_SELECTOR=$PhysicalProbeSelector"
        )
        if (-not [string]::IsNullOrWhiteSpace($physicalDocumentName)) {
            $probeEnvironment += "FREEX_X11_DOCUMENT_PATH=/documents/$physicalDocumentName"
        }
        & docker exec @($probeEnvironment | ForEach-Object { "--env"; $_ }) $containerName bash /tmp/run-freex-input-probes.sh /work/x11-validation
        $x11ProbeExit = $LASTEXITCODE
        $x11ManifestPath = Join-Path ([string]$x11Session.sessionDirectory) "x11-validation/x11-input-results.json"
        if (-not (Test-Path -LiteralPath $x11ManifestPath -PathType Leaf)) {
            throw "X11 input probes did not write a result manifest (exit $x11ProbeExit): $x11ManifestPath"
        }
        $x11Manifest = Get-Content -LiteralPath $x11ManifestPath -Raw | ConvertFrom-Json

        & $harness -Action Stop -App FreeX -Port $Port
        if (-not $PhysicalOnly) {
            # The pivot probe uses the external test-support executable. Refresh the canonical
            # application payload before the managed interaction batches reuse that image.
            Start-ValidationSession -AppArgument @() | Out-Null
            Ensure-ReportProvenance
            & $harness -Action Stop -App FreeX -Port $Port
        }
    }

    $requiredPhysicalProbeIds = if ($PhysicalProbeSelector -eq "name-box-dropdown-parity") {
        @("name-box-dropdown-parity-native-crop")
    } elseif ($PhysicalProbeSelector -eq "name-box-dropdown") {
        @(
            "name-box-dropdown-keyboard-physical",
            "name-box-dropdown-mouse-physical",
            "name-box-dropdown-defined-name-physical",
            "name-box-dropdown-table-physical",
            "name-box-dropdown-chart-physical",
            "name-box-dropdown-picture-physical",
            "name-box-dropdown-shape-physical",
            "name-box-dropdown-textbox-physical"
        )
    } elseif ($PhysicalProbeSelector -eq "pivot-field-list") {
        @(
            "pivot-field-drag-cross-bucket-physical",
            "pivot-field-drag-same-bucket-reorder-physical"
        )
    } elseif ($PhysicalProbeSelector -eq "pivot-table-details-double-click") {
        @(
            "pivot-table-details-double-click-physical"
        )
    } elseif ($PhysicalProbeSelector -eq "autofilter-recalculation") {
        @(
            "autofilter-recalculation-apply-change-clear-physical"
        )
    } elseif ($PhysicalProbeSelector -eq "backstage-print") {
        @(
            "backstage-print-ctrl-shift-f12-cancel"
        )
    } elseif ($PhysicalProbeSelector -eq "formula-3d-grip") {
        @(
            "formula-bar-point-mode-3d-sheet-range-grip"
        )
    } elseif ($PhysicalProbeSelector -eq "formula-3d-native-xlsx") {
        @(
            "formula-bar-point-mode-3d-native-xlsx"
        )
    } elseif ($PhysicalProbeSelector -eq "formula-whole-range-point") {
        @(
            "formula-bar-point-mode-whole-column-header",
            "formula-bar-point-mode-whole-row-header",
            "formula-bar-point-mode-whole-select-all-corner"
        )
    } elseif ($PhysicalProbeSelector -eq "formula-multi-area-point") {
        @(
            "formula-bar-point-mode-multi-area-keyboard",
            "formula-bar-point-mode-multi-area-pointer"
        )
    } elseif ($PhysicalProbeSelector -eq "formula-multi-area-edit") {
        @(
            "formula-bar-point-mode-multi-area-edit"
        )
    } elseif ($PhysicalProbeSelector -eq "formula-reference-grip") {
        @(
            "formula-reference-grip-multi-area-physical"
        )
    } elseif ($PhysicalProbeSelector -eq "grid-drag") {
        @(
            "grid-autofill-handle-drag-physical",
            "grid-selection-border-move-physical",
            "grid-selection-border-copy-physical"
        )
    } elseif ($PhysicalProbeSelector -eq "grid-autofit") {
        @(
            "grid-header-double-click-autofit-column-physical",
            "grid-header-double-click-autofit-row-physical",
            "grid-header-double-click-autofit-hidden-row-boundary-physical"
        )
    } elseif ($PhysicalProbeSelector -eq "split-pane-pointer") {
        @(
            "split-pane-divider-drag-physical",
            "split-pane-active-pane-wheel-physical",
            "split-pane-bottom-left-wheel-physical",
            "split-pane-mini-scrollbar-physical"
        )
    } elseif ($PhysicalProbeSelector -eq "outline-group") {
        @("outline-group-physical", "outline-columns-group-physical")
    } elseif ($PhysicalProbeSelector -eq "outline-nested-group") {
        @("outline-nested-rows-group-physical", "outline-nested-columns-group-physical")
    } elseif ($PhysicalProbeSelector -eq "outline-nested-save-reopen") {
        @(
            "outline-nested-rows-group-physical",
            "outline-nested-columns-group-physical",
            "outline-nested-save-reopen-physical"
        )
    } elseif ($PhysicalProbeSelector -eq "outline-nested-filter-save-reopen") {
        @("outline-nested-filter-save-reopen-physical")
    } else {
        @(
        "inline-edit-f2-escape",
        "inline-edit-f2-enter-commit",
        "save-ctrl-s-persist",
        "save-shift-f12-persist",
        "inline-point-mode-click",
        "inline-point-mode-drag-range",
        "formula-bar-point-mode-click",
        "keytips-alt",
        "keytips-f10",
        "worksheet-context-shift-f10",
        "worksheet-context-right-click",
        "worksheet-context-copy-physical",
        "worksheet-context-clear-physical",
        "clipboard-copy-paste-roundtrip",
        "clipboard-cut-paste-roundtrip",
        "window-new-arrange-switch-physical",
        "outline-group-physical",
        "outline-columns-group-physical",
        "outline-nested-rows-group-physical",
        "outline-nested-columns-group-physical",
        "split-pane-divider-drag-physical",
        "split-pane-active-pane-wheel-physical",
        "split-pane-bottom-left-wheel-physical",
        "split-pane-mini-scrollbar-physical",
        "dialog-format-cells-keyboard",
        "native-save-as-f12-cancel",
        "native-open-ctrl-f12-cancel",
        "backstage-print-ctrl-shift-f12-cancel",
        "sheet-tab-overflow-create-physical",
        "sheet-tab-overflow-navigation-physical",
        "sheet-tab-overflow-activate-dialog-physical",
            "sheet-tab-drag-reorder-physical"
        )
    }
    $physicalProbeResults = @($x11Manifest.results)
    $x11EvidenceDirectory = Split-Path -Parent $x11ManifestPath
    $physicalProbeIds = @($physicalProbeResults | ForEach-Object { [string]$_.id })
    $missingPhysicalProbeIds = @($requiredPhysicalProbeIds | Where-Object { $_ -notin $physicalProbeIds })
    $duplicatePhysicalProbeIds = @($physicalProbeIds | Group-Object | Where-Object Count -gt 1 | ForEach-Object Name)
    $invalidPhysicalRows = @($physicalProbeResults | Where-Object {
        [string]$_.category -ne "x11-input" -or
        [string]$_.evidenceLevel -ne "physical-x11-input" -or
        [string]$_.status -notin @("passed", "failed") -or
        [string]::IsNullOrWhiteSpace([string]$_.evidence)
    })
    $artifactRequiredPhysicalProbeIds = if ($PhysicalProbeSelector -eq "name-box-dropdown-parity") {
        @("name-box-dropdown-parity-native-crop")
    } elseif ($PhysicalProbeSelector -eq "backstage-print") {
        @("backstage-print-ctrl-shift-f12-cancel")
    } elseif ($PhysicalProbeSelector -eq "name-box-dropdown") {
        @(
            "name-box-dropdown-keyboard-physical",
            "name-box-dropdown-mouse-physical",
            "name-box-dropdown-defined-name-physical",
            "name-box-dropdown-table-physical",
            "name-box-dropdown-chart-physical",
            "name-box-dropdown-picture-physical",
            "name-box-dropdown-shape-physical",
            "name-box-dropdown-textbox-physical"
        )
    } elseif ($PhysicalProbeSelector -eq "pivot-field-list") {
        @(
            "pivot-field-drag-cross-bucket-physical",
            "pivot-field-drag-same-bucket-reorder-physical"
        )
    } elseif ($PhysicalProbeSelector -eq "pivot-table-details-double-click") {
        @(
            "pivot-table-details-double-click-physical"
        )
    } elseif ($PhysicalProbeSelector -eq "autofilter-recalculation") {
        @(
            "autofilter-recalculation-apply-change-clear-physical"
        )
    } elseif ($PhysicalProbeSelector -eq "formula-3d-grip") {
        @(
            "formula-bar-point-mode-3d-sheet-range-grip"
        )
    } elseif ($PhysicalProbeSelector -eq "formula-3d-native-xlsx") {
        @(
            "formula-bar-point-mode-3d-native-xlsx"
        )
    } elseif ($PhysicalProbeSelector -eq "formula-whole-range-point") {
        @(
            "formula-bar-point-mode-whole-column-header",
            "formula-bar-point-mode-whole-row-header",
            "formula-bar-point-mode-whole-select-all-corner"
        )
    } elseif ($PhysicalProbeSelector -eq "formula-multi-area-point") {
        @(
            "formula-bar-point-mode-multi-area-keyboard",
            "formula-bar-point-mode-multi-area-pointer"
        )
    } elseif ($PhysicalProbeSelector -eq "formula-multi-area-edit") {
        @(
            "formula-bar-point-mode-multi-area-edit"
        )
    } elseif ($PhysicalProbeSelector -eq "formula-reference-grip") {
        @(
            "formula-reference-grip-multi-area-physical"
        )
    } elseif ($PhysicalProbeSelector -eq "grid-drag") {
        @(
            "grid-autofill-handle-drag-physical",
            "grid-selection-border-move-physical",
            "grid-selection-border-copy-physical"
        )
    } elseif ($PhysicalProbeSelector -eq "grid-autofit") {
        @(
            "grid-header-double-click-autofit-column-physical",
            "grid-header-double-click-autofit-row-physical",
            "grid-header-double-click-autofit-hidden-row-boundary-physical"
        )
    } elseif ($PhysicalProbeSelector -eq "split-pane-pointer") {
        @(
            "split-pane-divider-drag-physical",
            "split-pane-active-pane-wheel-physical",
            "split-pane-bottom-left-wheel-physical",
            "split-pane-mini-scrollbar-physical"
        )
    } elseif ($PhysicalProbeSelector -eq "outline-group") {
        @("outline-group-physical", "outline-columns-group-physical")
    } elseif ($PhysicalProbeSelector -eq "outline-nested-group") {
        @("outline-nested-rows-group-physical", "outline-nested-columns-group-physical")
    } elseif ($PhysicalProbeSelector -eq "outline-nested-save-reopen") {
        @(
            "outline-nested-rows-group-physical",
            "outline-nested-columns-group-physical",
            "outline-nested-save-reopen-physical"
        )
    } elseif ($PhysicalProbeSelector -eq "outline-nested-filter-save-reopen") {
        @("outline-nested-filter-save-reopen-physical")
    } else {
        @(
        "worksheet-context-copy-physical",
        "worksheet-context-clear-physical",
        "clipboard-copy-paste-roundtrip",
        "clipboard-cut-paste-roundtrip",
        "window-new-arrange-switch-physical",
        "outline-group-physical",
        "outline-columns-group-physical",
        "outline-nested-rows-group-physical",
        "outline-nested-columns-group-physical",
        "split-pane-divider-drag-physical",
        "split-pane-active-pane-wheel-physical",
        "split-pane-bottom-left-wheel-physical",
        "split-pane-mini-scrollbar-physical",
        "native-save-as-f12-cancel",
        "native-open-ctrl-f12-cancel",
        "backstage-print-ctrl-shift-f12-cancel",
        "sheet-tab-overflow-create-physical",
        "sheet-tab-overflow-navigation-physical",
        "sheet-tab-overflow-activate-dialog-physical",
            "sheet-tab-drag-reorder-physical"
        )
    }
    $missingPhysicalArtifactIds = @()
    $invalidPhysicalArtifactRows = @()
    foreach ($physicalRow in $physicalProbeResults) {
        $rowArtifactProperty = $physicalRow.PSObject.Properties["artifacts"]
        if ($null -eq $rowArtifactProperty) {
            continue
        }
        $rowArtifacts = @($rowArtifactProperty.Value | ForEach-Object { [string]$_ })
        foreach ($artifact in $rowArtifacts) {
            $artifactPath = Join-Path $x11EvidenceDirectory $artifact
            $validArtifactName = -not [string]::IsNullOrWhiteSpace($artifact) -and
                -not [System.IO.Path]::IsPathRooted($artifact) -and
                $artifact -notmatch "[\\/]" -and
                $artifact -notmatch "(^|[\\/])\.\.([\\/]|$)"
            if (-not $validArtifactName -or -not (Test-Path -LiteralPath $artifactPath -PathType Leaf)) {
                $invalidPhysicalArtifactRows += "$( [string]$physicalRow.id ):$artifact"
                continue
            }
            $artifactInfo = Get-Item -LiteralPath $artifactPath
            if ($artifactInfo.Length -le 0) {
                $invalidPhysicalArtifactRows += "$( [string]$physicalRow.id ):$artifact(empty)"
            }
        }
    }
    foreach ($requiredArtifactId in $artifactRequiredPhysicalProbeIds) {
        $artifactRow = @($physicalProbeResults | Where-Object { [string]$_.id -eq $requiredArtifactId } | Select-Object -First 1)
        $artifactProperty = if ($artifactRow.Count -eq 1) { $artifactRow[0].PSObject.Properties["artifacts"] } else { $null }
        $artifacts = if ($null -eq $artifactProperty) { @() } else { @($artifactProperty.Value | ForEach-Object { [string]$_ }) }
        if ($artifacts.Count -eq 0) {
            $missingPhysicalArtifactIds += $requiredArtifactId
        }
    }
    $reportedPhysicalPassed = @($physicalProbeResults | Where-Object status -eq "passed").Count
    $reportedPhysicalFailed = @($physicalProbeResults | Where-Object status -eq "failed").Count
    $physicalSchemaValid =
        [int]$x11Manifest.schemaVersion -eq 2 -and
        [string]$x11Manifest.platform -eq "linux" -and
        [string]$x11Manifest.shell -eq "avalonia" -and
        $physicalProbeResults.Count -eq [int]$x11Manifest.summary.total -and
        $reportedPhysicalPassed -eq [int]$x11Manifest.summary.passed -and
        $reportedPhysicalFailed -eq [int]$x11Manifest.summary.failed -and
        [int]$x11Manifest.calibration.window.width -gt 0 -and
        [int]$x11Manifest.calibration.window.height -gt 0 -and
        [int]$x11Manifest.calibration.grid.cellWidth -gt 0 -and
        [int]$x11Manifest.calibration.grid.cellHeight -gt 0 -and
        $missingPhysicalProbeIds.Count -eq 0 -and
        $duplicatePhysicalProbeIds.Count -eq 0 -and
        $invalidPhysicalRows.Count -eq 0 -and
        $missingPhysicalArtifactIds.Count -eq 0 -and
        $invalidPhysicalArtifactRows.Count -eq 0
    if (-not $physicalSchemaValid) {
        throw "Physical X11 manifest does not satisfy schema v2 (missing='$($missingPhysicalProbeIds -join ',')'; duplicate='$($duplicatePhysicalProbeIds -join ',')'; invalidRows=$($invalidPhysicalRows.Count); missingArtifacts='$($missingPhysicalArtifactIds -join ',')'; invalidArtifacts='$($invalidPhysicalArtifactRows -join ','))."
    }

    if ([string]$x11Manifest.calibration.status -ne "passed") {
        $reason = [string]$x11Manifest.calibration.reason
        throw "Physical X11 evidence is not authoritative because geometry calibration did not pass: $reason"
    }
    if ($PhysicalProbeSelector -eq "formula-3d-native-xlsx") {
        Assert-Native3DPostcondition -EvidenceDirectory $x11EvidenceDirectory
    }
    if ($PhysicalProbeSelector -eq "formula-whole-range-point") {
        Assert-FormulaWholeRangePointPostcondition -EvidenceDirectory $x11EvidenceDirectory
    }
    if ($PhysicalProbeSelector -eq "grid-autofit") {
        Assert-GridAutofitPostcondition -EvidenceDirectory $x11EvidenceDirectory
    }
    if ($PhysicalProbeSelector -eq "name-box-dropdown") {
        Assert-NameBoxDropdownObjectPostcondition -EvidenceDirectory $x11EvidenceDirectory
        Assert-NameBoxDropdownInteractionPostcondition -EvidenceDirectory $x11EvidenceDirectory
    }
    if ($PhysicalProbeSelector -eq "name-box-dropdown-parity") {
        Assert-NameBoxDropdownParityNativeContract -EvidenceDirectory $x11EvidenceDirectory
    }
    $x11ReportDirectory = Join-Path $reportDirectory "x11-validation"
    New-Item -ItemType Directory -Path $x11ReportDirectory -Force | Out-Null
    foreach ($evidenceFile in Get-ChildItem -LiteralPath $x11EvidenceDirectory -File) {
        $evidenceDestination = Join-Path $x11ReportDirectory $evidenceFile.Name
        Copy-LongPathSafeFile -Source $evidenceFile.FullName -Destination $evidenceDestination
    }
    if ($PhysicalProbeSelector -eq "name-box-dropdown-parity") {
        $nativePairSource = Join-Path $x11EvidenceDirectory "name-box-dropdown-parity-native"
        $nativePairDestination = Join-Path $x11ReportDirectory "name-box-dropdown-parity-native"
        if (-not (Test-Path -LiteralPath $nativePairSource -PathType Container)) {
            throw "Name Box parity native comparison directory is missing: $nativePairSource"
        }
        New-Item -ItemType Directory -Path $nativePairDestination -Force | Out-Null
        foreach ($nativePairFile in Get-ChildItem -LiteralPath $nativePairSource -File) {
            $nativePairFileDestination = Join-Path $nativePairDestination $nativePairFile.Name
            Copy-LongPathSafeFile -Source $nativePairFile.FullName -Destination $nativePairFileDestination
        }
    }

    if ($PhysicalOnly) {
        $manifest = [pscustomobject][ordered]@{
            schemaVersion = 2
            app = "FreeX"
            platform = "linux"
            shell = "avalonia"
            validationMode = "physical-only"
            coverage = [pscustomobject][ordered]@{
                exhaustive = $false
                scope = "bounded physical X11 probes"
            }
            results = @()
            summary = [pscustomobject]@{}
        }
        $combinedResults = @()
    } else {
        # Phase two uses a fresh X11 process for each bounded dialog slice. Avalonia retains native modal/input
        # resources across repeated closes, so one 120-dialog process is not a reliable validation boundary.
        $manifest = $null
        $combinedResults = @()
        $authoritativeDialogCount = $null
        $authoritativeRibbonCount = $null
        $authoritativeContextCount = $null
        $coreSections = @("ribbon-bindings", "shortcuts", "range-inventory", "editing", "quick-analysis-drawing")
        foreach ($coreSection in $coreSections) {
        $existingCorePath = Join-Path $reportDirectory "core-$coreSection.json"
        if (Test-Path -LiteralPath $existingCorePath -PathType Leaf) {
            $batchManifest = Read-CompletedJsonManifest -Path $existingCorePath -Deadline (Get-Date).AddMinutes(1) `
                -ExpectedSection $coreSection -ExpectedIncludeCoreResults $true -ExpectedRibbonOnly $false `
                -ExpectedContextMenuDispatchCount ([int]::MaxValue) -RequireRunnerMetadata $true
            if ($null -eq $batchManifest) {
                throw "Existing core interaction section '$coreSection' is incomplete: $existingCorePath"
            }
            if ($null -eq $authoritativeDialogCount) {
                $authoritativeDialogCount = [int]$batchManifest.dialogCatalogCount
                $authoritativeRibbonCount = [int]$batchManifest.ribbonCommandCatalogCount
                $authoritativeContextCount = [int]$batchManifest.contextMenuDispatchCatalogCount
            }
            Save-ValidatedManifest $batchManifest $existingCorePath $coreSection $true $false 0 0 0 0 0 ([int]::MaxValue) $true | Out-Null
            if ($null -eq $manifest) { $manifest = $batchManifest }
            $combinedResults += @($batchManifest.results)
            Write-Host "Reusing core interaction section '$coreSection'."
            continue
        }

        $appArguments = @(
            "--interaction-validation", "/work/validation",
            "--interaction-validation-dialog-start", "0",
            "--interaction-validation-dialog-count", "0",
            "--interaction-validation-ribbon-start", "0",
            "--interaction-validation-ribbon-count", "0",
            "--interaction-validation-core-section", $coreSection
        )
        Write-Host "Running core interaction section '$coreSection'..."
        $session = Start-ValidationSession -ReusePublishedPayload -AppArgument $appArguments
        $batchManifestPath = Join-Path ([string]$session.sessionDirectory) "validation/interaction-validation.json"
        $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
        $batchManifest = Read-CompletedJsonManifest -Path $batchManifestPath -Deadline $deadline `
            -ExpectedSection $coreSection -ExpectedIncludeCoreResults $true -ExpectedRibbonOnly $false `
            -ExpectedContextMenuDispatchCount ([int]::MaxValue)
        if ($null -eq $batchManifest) {
            $appLogPath = Join-Path ([string]$session.sessionDirectory) "logs/app.log"
            $appLog = if (Test-Path -LiteralPath $appLogPath) { Get-Content -LiteralPath $appLogPath -Raw } else { "" }
            throw "Core interaction section '$coreSection' did not write a complete manifest within $TimeoutMinutes minute(s).`n$appLog"
        }
        if ($batchManifest.error) {
            throw "Core interaction section '$coreSection' failed: $($batchManifest.error)"
        }

        if ($null -eq $authoritativeDialogCount) {
            $authoritativeDialogCount = [int]$batchManifest.dialogCatalogCount
            $authoritativeRibbonCount = [int]$batchManifest.ribbonCommandCatalogCount
            $authoritativeContextCount = [int]$batchManifest.contextMenuDispatchCatalogCount
            if ($authoritativeDialogCount -le 0 -or $authoritativeRibbonCount -le 0 -or $authoritativeContextCount -le 0) {
                throw "Interaction validation reported invalid catalog counts."
            }
            Write-Host "Authoritative dialog routes: $authoritativeDialogCount"
            Write-Host "Authoritative ribbon commands: $authoritativeRibbonCount"
            Write-Host "Authoritative context-menu dispatches: $authoritativeContextCount"
        }
        if ($null -eq $manifest) { $manifest = $batchManifest }
        $combinedResults += @($batchManifest.results)
        Save-ValidatedManifest $batchManifest (Join-Path $reportDirectory "core-$coreSection.json") $coreSection $true $false 0 0 0 0 0 ([int]::MaxValue) | Out-Null
        & $harness -Action Stop -App FreeX -Port $Port
    }

    for ($contextStart = $ContextStart; $contextStart -lt $authoritativeContextCount; $contextStart += $ContextBatchSize) {
        $contextCount = [Math]::Min($ContextBatchSize, $authoritativeContextCount - $contextStart)
        $existingContextPath = Join-Path $reportDirectory ("context-batch-{0:D3}.json" -f $contextStart)
        if (Test-Path -LiteralPath $existingContextPath -PathType Leaf) {
            $batchManifest = Read-CompletedJsonManifest -Path $existingContextPath -Deadline (Get-Date).AddMinutes(1) `
                -ExpectedSection "context-menus" -ExpectedIncludeCoreResults $true -ExpectedRibbonOnly $false `
                -ExpectedContextMenuDispatchStart $contextStart -ExpectedContextMenuDispatchCount $contextCount `
                -RequireRunnerMetadata $true
            if ($null -eq $batchManifest) {
                throw "Existing context batch is incomplete: $existingContextPath"
            }
            if ([int]$batchManifest.contextMenuDispatchCatalogCount -ne $authoritativeContextCount) {
                throw "Existing context-menu dispatch catalog count changed during validation."
            }
            Save-ValidatedManifest $batchManifest $existingContextPath "context-menus" $true $false 0 0 0 0 $contextStart $contextCount $true | Out-Null
            $combinedResults += @($batchManifest.results)
            Write-Host "Reusing context-menu dispatch batch $contextStart..$($contextStart + $contextCount - 1)."
            continue
        }

        $appArguments = @(
            "--interaction-validation", "/work/validation",
            "--interaction-validation-dialog-count", "0",
            "--interaction-validation-dialog-start", "0",
            "--interaction-validation-ribbon-start", "0",
            "--interaction-validation-ribbon-count", "0",
            "--interaction-validation-core-section", "context-menus",
            "--interaction-validation-context-start", [string]$contextStart,
            "--interaction-validation-context-count", [string]$contextCount
        )
        Write-Host "Running context-menu dispatch batch $contextStart..$($contextStart + $contextCount - 1)..."
        $session = Start-ValidationSession -MemoryLimit "6g" -ReusePublishedPayload -AppArgument $appArguments
        $batchManifestPath = Join-Path ([string]$session.sessionDirectory) "validation/interaction-validation.json"
        $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
        $batchManifest = Read-CompletedJsonManifest -Path $batchManifestPath -Deadline $deadline `
            -ExpectedSection "context-menus" -ExpectedIncludeCoreResults $true -ExpectedRibbonOnly $false `
            -ExpectedContextMenuDispatchStart $contextStart -ExpectedContextMenuDispatchCount $contextCount
        if ($null -eq $batchManifest) {
            $appLogPath = Join-Path ([string]$session.sessionDirectory) "logs/app.log"
            $appLog = if (Test-Path -LiteralPath $appLogPath) { Get-Content -LiteralPath $appLogPath -Raw } else { "" }
            throw "Context-menu interaction batch $contextStart did not write a complete manifest.`n$appLog"
        }
        if ([int]$batchManifest.contextMenuDispatchCatalogCount -ne $authoritativeContextCount) {
            throw "Context-menu dispatch catalog count changed during validation."
        }
        $combinedResults += @($batchManifest.results)
        Save-ValidatedManifest $batchManifest (Join-Path $reportDirectory ("context-batch-{0:D3}.json" -f $contextStart)) "context-menus" $true $false 0 0 0 0 $contextStart $contextCount | Out-Null
        & $harness -Action Stop -App FreeX -Port $Port
    }

    for ($dialogStart = 0; $null -eq $authoritativeDialogCount -or $dialogStart -lt $authoritativeDialogCount; $dialogStart += $DialogBatchSize) {
        $dialogCount = if ($null -eq $authoritativeDialogCount) {
            $DialogBatchSize
        } else {
            [Math]::Min($DialogBatchSize, $authoritativeDialogCount - $dialogStart)
        }
        $existingDialogPath = Join-Path $reportDirectory ("batch-{0:D3}.json" -f $dialogStart)
        if (Test-Path -LiteralPath $existingDialogPath -PathType Leaf) {
            $batchManifest = Read-CompletedJsonManifest -Path $existingDialogPath -Deadline (Get-Date).AddMinutes(1) `
                -ExpectedSection "dialogs" -ExpectedIncludeCoreResults $false -ExpectedRibbonOnly $false `
                -ExpectedDialogStart $dialogStart -ExpectedDialogCount $dialogCount `
                -RequireRunnerMetadata $true
            if ($null -eq $batchManifest) {
                throw "Existing dialog batch is incomplete: $existingDialogPath"
            }
            if ([int]$batchManifest.dialogCatalogCount -ne $authoritativeDialogCount) {
                throw "Existing dialog catalog count changed during validation."
            }
            if ([int]$batchManifest.ribbonCommandCatalogCount -ne $authoritativeRibbonCount) {
                throw "Existing dialog batch ribbon catalog count changed during validation."
            }
            Save-ValidatedManifest $batchManifest $existingDialogPath "dialogs" $false $false $dialogStart $dialogCount 0 0 0 0 $true | Out-Null
            if ($null -eq $manifest) { $manifest = $batchManifest }
            $combinedResults += @($batchManifest.results)
            Write-Host "Reusing dialog interaction batch $dialogStart..$($dialogStart + $dialogCount - 1)."
            continue
        }

        $appArguments = @(
            "--interaction-validation", "/work/validation",
            "--interaction-validation-dialog-start", [string]$dialogStart,
            "--interaction-validation-dialog-count", [string]$dialogCount,
            "--interaction-validation-ribbon-start", "0",
            "--interaction-validation-ribbon-count", "0",
            "--interaction-validation-context-start", "0",
            "--interaction-validation-context-count", "0",
            "--interaction-validation-dialog-only"
        )

        Write-Host "Running dialog interaction batch $dialogStart..$($dialogStart + $dialogCount - 1)..."
        $session = Start-ValidationSession -ReusePublishedPayload -AppArgument $appArguments
        $batchManifestPath = Join-Path ([string]$session.sessionDirectory) "validation/interaction-validation.json"
        $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
        $batchManifest = Read-CompletedJsonManifest -Path $batchManifestPath -Deadline $deadline `
            -ExpectedSection "dialogs" -ExpectedIncludeCoreResults $false -ExpectedRibbonOnly $false `
            -ExpectedDialogStart $dialogStart -ExpectedDialogCount $dialogCount
        if ($null -eq $batchManifest) {
            $appLogPath = Join-Path ([string]$session.sessionDirectory) "logs/app.log"
            $appLog = if (Test-Path -LiteralPath $appLogPath) { Get-Content -LiteralPath $appLogPath -Raw } else { "" }
            throw "Interaction-validation batch $dialogStart did not write a complete manifest within $TimeoutMinutes minute(s): $batchManifestPath`n$appLog"
        }

        if ($batchManifest.error) {
            throw "Interaction validation batch $dialogStart failed before producing results: $($batchManifest.error)"
        }
        if ($null -eq $authoritativeDialogCount) {
            $authoritativeDialogCount = [int]$batchManifest.dialogCatalogCount
            if ($authoritativeDialogCount -le 0) {
                throw "Interaction validation reported an invalid dialog catalog count: $authoritativeDialogCount"
            }
            Write-Host "Authoritative dialog routes: $authoritativeDialogCount"
        } elseif ([int]$batchManifest.dialogCatalogCount -ne $authoritativeDialogCount) {
            throw "Dialog catalog count changed during validation: expected $authoritativeDialogCount, observed $($batchManifest.dialogCatalogCount)."
        }
        if ($null -eq $authoritativeRibbonCount) {
            $authoritativeRibbonCount = [int]$batchManifest.ribbonCommandCatalogCount
            if ($authoritativeRibbonCount -le 0) {
                throw "Interaction validation reported an invalid ribbon command catalog count: $authoritativeRibbonCount"
            }
            Write-Host "Authoritative ribbon commands: $authoritativeRibbonCount"
        } elseif ([int]$batchManifest.ribbonCommandCatalogCount -ne $authoritativeRibbonCount) {
            throw "Ribbon command catalog count changed during validation: expected $authoritativeRibbonCount, observed $($batchManifest.ribbonCommandCatalogCount)."
        }
        if ($null -eq $manifest) { $manifest = $batchManifest }
        $combinedResults += @($batchManifest.results)
        Save-ValidatedManifest $batchManifest (Join-Path $reportDirectory ("batch-{0:D3}.json" -f $dialogStart)) "dialogs" $false $false $dialogStart $dialogCount 0 0 0 0 | Out-Null
        & $harness -Action Stop -App FreeX -Port $Port
    }

    # Ribbon commands are isolated into bounded app processes. Production ribbon dispatch can rebuild
    # substantial visual state, and Avalonia retains some subscriptions until process shutdown.
    for ($ribbonStart = 0; $ribbonStart -lt $authoritativeRibbonCount; $ribbonStart += $RibbonBatchSize) {
        $ribbonCount = [Math]::Min($RibbonBatchSize, $authoritativeRibbonCount - $ribbonStart)
        $existingRibbonPath = Join-Path $reportDirectory ("ribbon-batch-{0:D3}.json" -f $ribbonStart)
        if (Test-Path -LiteralPath $existingRibbonPath -PathType Leaf) {
            $batchManifest = Read-CompletedJsonManifest -Path $existingRibbonPath -Deadline (Get-Date).AddMinutes(1) `
                -ExpectedSection "ribbon-only" -ExpectedIncludeCoreResults $true -ExpectedRibbonOnly $true `
                -ExpectedRibbonCommandStart $ribbonStart -ExpectedRibbonCommandCount $ribbonCount `
                -ExpectedContextMenuDispatchCount ([int]::MaxValue) -RequireRunnerMetadata $true
            if ($null -eq $batchManifest) {
                throw "Existing ribbon batch is incomplete: $existingRibbonPath"
            }
            if ([int]$batchManifest.ribbonCommandCatalogCount -ne $authoritativeRibbonCount) {
                throw "Existing ribbon command catalog count changed during validation."
            }
            Save-ValidatedManifest $batchManifest $existingRibbonPath "ribbon-only" $true $true 0 0 $ribbonStart $ribbonCount 0 ([int]::MaxValue) $true | Out-Null
            $combinedResults += @($batchManifest.results)
            Write-Host "Reusing ribbon interaction batch $ribbonStart..$($ribbonStart + $ribbonCount - 1)."
            continue
        }

        $appArguments = @(
            "--interaction-validation", "/work/validation",
            "--interaction-validation-dialog-start", "0",
            "--interaction-validation-dialog-count", "0",
            "--interaction-validation-ribbon-start", [string]$ribbonStart,
            "--interaction-validation-ribbon-count", [string]$ribbonCount,
            "--interaction-validation-ribbon-only"
        )
        Write-Host "Running ribbon interaction batch $ribbonStart..$($ribbonStart + $ribbonCount - 1)..."
        $session = Start-ValidationSession -ReusePublishedPayload -AppArgument $appArguments
        $batchManifestPath = Join-Path ([string]$session.sessionDirectory) "validation/interaction-validation.json"
        $deadline = (Get-Date).AddMinutes($TimeoutMinutes)
        $batchManifest = Read-CompletedJsonManifest -Path $batchManifestPath -Deadline $deadline `
            -ExpectedSection "ribbon-only" -ExpectedIncludeCoreResults $true -ExpectedRibbonOnly $true `
            -ExpectedRibbonCommandStart $ribbonStart -ExpectedRibbonCommandCount $ribbonCount `
            -ExpectedContextMenuDispatchCount ([int]::MaxValue)
        if ($null -eq $batchManifest) {
            throw "Ribbon interaction-validation batch $ribbonStart did not write a complete manifest within $TimeoutMinutes minute(s): $batchManifestPath"
        }

        if ($batchManifest.error) {
            throw "Ribbon interaction validation batch $ribbonStart failed before producing results: $($batchManifest.error)"
        }
        if ([int]$batchManifest.ribbonCommandCatalogCount -ne $authoritativeRibbonCount) {
            throw "Ribbon command catalog count changed during validation: expected $authoritativeRibbonCount, observed $($batchManifest.ribbonCommandCatalogCount)."
        }
        $combinedResults += @($batchManifest.results)
        Save-ValidatedManifest $batchManifest (Join-Path $reportDirectory ("ribbon-batch-{0:D3}.json" -f $ribbonStart)) "ribbon-only" $true $true 0 0 $ribbonStart $ribbonCount 0 ([int]::MaxValue) | Out-Null
        & $harness -Action Stop -App FreeX -Port $Port
    }

    $combinedResults = @(Merge-ContextMenuAggregateResults -Results $combinedResults)

    $rangeInventory = @($combinedResults | Where-Object category -eq "range-selection-inventory")
    $rangeInteractionRows = @($combinedResults | Where-Object category -eq "range-selection")
    $deduplicatedRangeRows = foreach ($group in @($rangeInteractionRows | Group-Object id)) {
        $candidates = @($group.Group | Where-Object status -eq "failed" | Select-Object -First 1) +
            @($group.Group | Where-Object status -ne "failed" | Select-Object -First 1)
        $candidates | Select-Object -First 1
    }
    $observedRangeIds = @($deduplicatedRangeRows | Select-Object -ExpandProperty id -Unique)
    $missingRangeRows = foreach ($inventoryRow in $rangeInventory) {
        if ($observedRangeIds -contains [string]$inventoryRow.id) { continue }
        [pscustomobject]@{
            id = [string]$inventoryRow.id
            category = "range-selection"
            status = "failed"
            evidenceLevel = "registered-not-exercised"
            evidence = [string]$inventoryRow.evidence
            note = "No production picker apply/cancel evidence was observed across the complete dialog run."
        }
    }
    $combinedResults = @($combinedResults | Where-Object category -ne "range-selection") +
        @($deduplicatedRangeRows) + @($missingRangeRows)

        $dialogContractIds = @($combinedResults | Where-Object category -eq "dialog-contract" | Select-Object -ExpandProperty id -Unique)
        if ($dialogContractIds.Count -ne $authoritativeDialogCount) {
            $combinedResults += [pscustomobject]@{
                id = "validation.dialog-catalog-completeness"
                category = "validation-completeness"
                status = "failed"
                evidenceLevel = "catalog-count-mismatch"
                evidence = "expected=$authoritativeDialogCount; observed=$($dialogContractIds.Count)"
                note = "Every authoritative production dialog route must emit exactly one keyboard/focus contract row."
            }
        }
    }

    $physicalResults = @($x11Manifest.results)
    $physicalSummary = [ordered]@{}
    foreach ($group in @($physicalResults | Group-Object -Property status)) {
        $physicalSummary[[string]$group.Name] = [int]$group.Count
    }
    if (-not $physicalSummary.Contains("passed")) { $physicalSummary.passed = 0 }
    if (-not $physicalSummary.Contains("failed")) { $physicalSummary.failed = 0 }
    $physicalSummary.total = $physicalResults.Count
    $manifest | Add-Member -NotePropertyName physicalX11 -NotePropertyValue ([pscustomobject][ordered]@{
        summary = [pscustomobject]$physicalSummary
        calibration = $x11Manifest.calibration
        manifest = "x11-validation/x11-input-results.json"
        evidenceDirectory = "x11-validation"
    }) -Force
    $manifest.results = @($combinedResults) + $physicalResults
    $summary = [ordered]@{}
    foreach ($group in @($manifest.results | Group-Object -Property status)) {
        $summary[[string]$group.Name] = [int]$group.Count
    }
    $summary.total = @($manifest.results).Count
    $manifest.summary = [pscustomobject]$summary
    $manifestPath = Join-Path $reportDirectory "interaction-validation.json"
    [IO.File]::WriteAllText(
        $manifestPath,
        ($manifest | ConvertTo-Json -Depth 12),
        (New-Object Text.UTF8Encoding($false)))

    $reportPath = Join-Path $reportDirectory "interaction-validation.html"
    $rows = foreach ($result in $manifest.results) {
        $statusClass = [System.Net.WebUtility]::HtmlEncode([string]$result.status)
        $id = [System.Net.WebUtility]::HtmlEncode([string]$result.id)
        $category = [System.Net.WebUtility]::HtmlEncode([string]$result.category)
        $level = [System.Net.WebUtility]::HtmlEncode([string]$result.evidenceLevel)
        $evidence = [System.Net.WebUtility]::HtmlEncode([string]$result.evidence)
        $note = [System.Net.WebUtility]::HtmlEncode([string]$result.note)
        "<tr class='$statusClass'><td>$statusClass</td><td>$category</td><td>$id</td><td>$level</td><td>$evidence</td><td>$note</td></tr>"
    }
    $summaryText = ($manifest.summary.PSObject.Properties | ForEach-Object {
        "<strong>$([System.Net.WebUtility]::HtmlEncode($_.Name))</strong>: $($_.Value)"
    }) -join " &nbsp; "
    $physicalSummaryText = ($manifest.physicalX11.summary.PSObject.Properties | ForEach-Object {
        "<strong>$([System.Net.WebUtility]::HtmlEncode($_.Name))</strong>: $($_.Value)"
    }) -join " &nbsp; "
    $grid = $manifest.physicalX11.calibration.grid
    $calibrationText = "A1=($($grid.a1.x),$($grid.a1.y)); cell=$($grid.cellWidth)x$($grid.cellHeight); selection=$($manifest.physicalX11.calibration.selectionColor)"
    $categoryOptions = @($manifest.results | Select-Object -ExpandProperty category -Unique | Sort-Object) | ForEach-Object {
        $encoded = [System.Net.WebUtility]::HtmlEncode([string]$_)
        "<option value='$encoded'>$encoded</option>"
    }
    $html = @"
<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>FreeX Linux interaction validation</title>
<style>
body{font:13px Segoe UI,Arial,sans-serif;margin:24px;color:#202124}h1{font-size:22px}table{border-collapse:collapse;width:100%}
th,td{border:1px solid #d0d5da;padding:6px 8px;text-align:left;vertical-align:top}th{background:#eef1f4;position:sticky;top:0}
tr.failed{background:#fde7e9}tr.skipped{background:#fff4ce}tr.passed td:first-child{color:#107c10;font-weight:600}
</style></head><body><h1>FreeX Linux interaction validation</h1><p>$summaryText</p>
<h2>Physical X11 input</h2><p>$physicalSummaryText</p>
<p>Geometry calibration: <strong>$([System.Net.WebUtility]::HtmlEncode([string]$manifest.physicalX11.calibration.status))</strong>;
$([System.Net.WebUtility]::HtmlEncode($calibrationText)). <a href="x11-validation/x11-input-results.json">Manifest and evidence</a></p>
<p><input id="query" type="search" placeholder="Filter interactions" style="width:320px;padding:6px">
<select id="status" style="padding:6px"><option value="">All statuses</option><option>passed</option><option>failed</option><option>skipped</option></select>
<select id="category" style="padding:6px"><option value="">All categories</option>$($categoryOptions -join '')</select>
<span id="visibleCount"></span></p>
<table><thead><tr><th>Status</th><th>Category</th><th>Interaction</th><th>Evidence level</th><th>Evidence</th><th>Note</th></tr></thead>
<tbody>$($rows -join [Environment]::NewLine)</tbody></table>
<script>
const rows=[...document.querySelectorAll('tbody tr')], q=document.querySelector('#query'), s=document.querySelector('#status'), c=document.querySelector('#category'), n=document.querySelector('#visibleCount');
function filter(){let shown=0; for(const row of rows){const ok=(!q.value||row.textContent.toLowerCase().includes(q.value.toLowerCase()))&&(!s.value||row.cells[0].textContent===s.value)&&(!c.value||row.cells[1].textContent===c.value);row.hidden=!ok;if(ok)shown++;}n.textContent=shown+' of '+rows.length+' rows';}
q.addEventListener('input',filter);s.addEventListener('change',filter);c.addEventListener('change',filter);filter();
</script></body></html>
"@
    [IO.File]::WriteAllText($reportPath, $html, (New-Object Text.UTF8Encoding($false)))

    Write-Host "Manifest: $manifestPath"
    Write-Host "Report  : $reportPath"
    Write-Host "Summary : $summaryText"

    if ($x11ProbeExit -ne 0 -or [int]$manifest.summary.failed -gt 0) {
        exit 1
    }
} finally {
    & $harness -Action Stop -App FreeX -Port $Port
    if (Test-Path -LiteralPath $sessionBindingDirectory -PathType Container) {
        Remove-Item -LiteralPath $sessionBindingDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
