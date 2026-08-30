param(
    [string]$OutputRoot = "tools/ux-parity-runs",

    [string]$FreeXExe,

    [ValidateSet("smoke", "full")]
    [string]$Suite = "smoke",

    [switch]$SkipFreeXBuild,

    [switch]$KeepAppsOpen
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "ToolScriptSupport.ps1")

function Get-LinkedDataTypePackageEntries {
    param([Parameter(Mandatory = $true)][string]$WorkbookPath)

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($WorkbookPath)
    try {
        return @($archive.Entries |
            Where-Object { $_.FullName.Replace('\', '/').StartsWith('xl/richData/', [System.StringComparison]::OrdinalIgnoreCase) } |
            ForEach-Object FullName)
    }
    finally {
        $archive.Dispose()
    }
}

function Release-ComObject {
    param([object]$Value)

    if ($null -ne $Value -and [System.Runtime.InteropServices.Marshal]::IsComObject($Value)) {
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($Value)
    }
}

function Set-Cell {
    param(
        [object]$Sheet,
        [int]$Row,
        [int]$Column,
        [object]$Value
    )

    $cellValue = $Value
    if ($Value -is [int] -or $Value -is [long]) {
        $cellValue = [double]$Value
    }

    try {
        $cell = $Sheet.Cells.Item($Row, $Column)
        if ($cellValue -is [double] -or $cellValue -is [decimal] -or $cellValue -is [single]) {
            $cell.Formula = [Convert]::ToString($cellValue, [System.Globalization.CultureInfo]::InvariantCulture)
        }
        else {
            $cell.Value2 = [string]$cellValue
        }
    }
    catch {
        $typeName = if ($null -eq $cellValue) { "<null>" } else { $cellValue.GetType().FullName }
        throw "Set-Cell failed at row $Row column $Column with value type $typeName and value '$cellValue': $($_.Exception.Message)"
    }
}

function Set-Formula {
    param(
        [object]$Sheet,
        [string]$Address,
        [string]$Formula
    )

    try {
        $Sheet.Range($Address).Formula2 = $Formula
    }
    catch {
        $Sheet.Range($Address).Formula = $Formula
    }
}

function Rename-Sheet {
    param(
        [object]$Sheet,
        [string]$Name
    )

    $safe = $Name
    if ($safe.Length -gt 31) {
        $safe = $safe.Substring(0, 31)
    }

    $Sheet.Name = $safe
}

function Get-InScopeFunctionNames {
    param([string]$RepoRoot)

    $functionsPath = Join-Path $RepoRoot "docs/parity/functions.md"
    if (-not (Test-Path $functionsPath)) {
        return @()
    }

    $names = New-Object System.Collections.Generic.List[string]
    foreach ($line in Get-Content $functionsPath) {
        if ($line -match '^\|\s*([A-Z][A-Z0-9\._]*)\s*\|\s*Implemented\s*\|') {
            $names.Add($Matches[1])
        }
    }

    return $names.ToArray()
}

function Add-Table {
    param(
        [object]$Sheet,
        [string]$Address,
        [string]$Name
    )

    $range = $Sheet.Range($Address)
    $table = $Sheet.ListObjects.Add(1, $range, $null, 1)
    $table.Name = $Name
    $table.TableStyle = "TableStyleMedium2"
    return $table
}

function Build-CorpusWorkbook {
    param(
        [string]$RepoRoot,
        [string]$WorkbookPath
    )

    $excel = $null
    $workbook = $null
    $createdFunctionCount = 0

    try {
        $excel = New-Object -ComObject Excel.Application
        $excel.Visible = $true
        $excel.DisplayAlerts = $false
        $excel.ScreenUpdating = $true

        $workbook = $excel.Workbooks.Add()
        while ($workbook.Worksheets.Count -lt 7) {
            [void]$workbook.Worksheets.Add()
        }

        $overview = $workbook.Worksheets.Item(1)
        Rename-Sheet $overview "UX Overview"
        Set-Cell $overview 1 1 "FreeX / Excel UX parity corpus"
        Set-Cell $overview 3 1 "Purpose"
        Set-Cell $overview 3 2 "Seed workbook for paired mouse, keyboard, dialog, grid, chrome, formula, formatting, chart, table, and pivot user testing."
        Set-Cell $overview 4 1 "Generated UTC"
        Set-Cell $overview 4 2 ([DateTimeOffset]::UtcNow.ToString("o"))
        Set-Cell $overview 5 1 "Suite"
        Set-Cell $overview 5 2 $Suite
        Set-Cell $overview 7 1 "Next evidence layers"
        Set-Cell $overview 8 1 "1. Launch Excel and FreeX against this same workbook."
        Set-Cell $overview 9 1 "2. Walk the UI surface by mouse, keyboard shortcut, keytip, access key, and UI Automation."
        Set-Cell $overview 10 1 "3. Capture screenshots, UIA tree/events, workbook state deltas, saved files, and disparity records."
        $overview.Columns.AutoFit() | Out-Null

        $grid = $workbook.Worksheets.Item(2)
        Rename-Sheet $grid "Grid Basics"
        $headers = @("Region", "Rep", "Month", "Units", "Revenue", "Margin", "Notes")
        for ($i = 0; $i -lt $headers.Count; $i++) {
            $headerValue = $headers[$i]
            Set-Cell $grid 1 ($i + 1) $headerValue
        }

        $rows = @(
            @("North", "Ada", "Jan", 12, 2400, 0.32, "plain value"),
            @("South", "Grace", "Jan", 19, 4180, 0.28, "formatted number"),
            @("East", "Katherine", "Feb", 7, 1750, 0.41, "filter candidate"),
            @("West", "Dorothy", "Feb", 15, 3300, 0.35, "chart candidate"),
            @("North", "Mary", "Mar", 22, 5720, 0.30, "pivot candidate"),
            @("South", "Annie", "Mar", 9, 1890, 0.24, "validation candidate"),
            @("East", "Barbara", "Apr", 18, 5040, 0.37, "comment candidate"),
            @("West", "Evelyn", "Apr", 11, 2860, 0.33, "sort candidate")
        )

        for ($r = 0; $r -lt $rows.Count; $r++) {
            for ($c = 0; $c -lt $rows[$r].Count; $c++) {
                $cellValue = $rows[$r][$c]
                Set-Cell $grid ($r + 2) ($c + 1) $cellValue
            }
        }

        $grid.Range("E2:E9").NumberFormat = "$#,##0"
        $grid.Range("F2:F9").NumberFormat = "0%"
        $grid.Range("A1:G1").Font.Bold = $true
        [void](Add-Table $grid "A1:G9" "UxParitySales")
        $grid.Columns.AutoFit() | Out-Null

        $formulas = $workbook.Worksheets.Item(3)
        Rename-Sheet $formulas "Formulas"
        Set-Cell $formulas 1 1 "Area"
        Set-Cell $formulas 1 2 "Formula"
        Set-Cell $formulas 1 3 "Expected"
        Set-Cell $formulas 1 4 "Notes"
        $formulaRows = @(
            @("Math", "=SUM(1,2,3)", 6, "scalar arithmetic"),
            @("Lookup", "=XLOOKUP(""Ada"",'Grid Basics'!B:B,'Grid Basics'!E:E)", 2400, "modern lookup"),
            @("Conditional aggregate", "=SUMIFS('Grid Basics'!E:E,'Grid Basics'!A:A,""North"")", 8120, "dynamic-array metadata is exercised in a dedicated parity fixture"),
            @("Text", "=TEXTJOIN(""-"",TRUE,""FreeX"",""Excel"",""Parity"")", "FreeX-Excel-Parity", "text join"),
            @("Date", "=DATE(2026,7,1)+7", 46211, "date serial/rendering"),
            @("Logical", "=IF(SUM('Grid Basics'!D2:D9)>100,""high"",""low"")", "high", "range reference"),
            @("Structured reference", "=SUM(UxParitySales[Revenue])", 27140, "table formula")
        )

        for ($r = 0; $r -lt $formulaRows.Count; $r++) {
            $formulaArea = $formulaRows[$r][0]
            $formulaText = $formulaRows[$r][1]
            $formulaExpected = $formulaRows[$r][2]
            $formulaNotes = $formulaRows[$r][3]
            Set-Cell $formulas ($r + 2) 1 $formulaArea
            Set-Formula $formulas ("B" + ($r + 2)) $formulaText
            Set-Cell $formulas ($r + 2) 3 $formulaExpected
            Set-Cell $formulas ($r + 2) 4 $formulaNotes
        }
        $formulas.Range("A1:D1").Font.Bold = $true
        $formulas.Columns.AutoFit() | Out-Null

        $inventory = $workbook.Worksheets.Item(4)
        Rename-Sheet $inventory "Function Inventory"
        Set-Cell $inventory 1 1 "Function"
        Set-Cell $inventory 1 2 "FreeX status"
        Set-Cell $inventory 1 3 "UX scenario status"
        Set-Cell $inventory 1 4 "Scenario source"
        $functions = Get-InScopeFunctionNames $RepoRoot
        $createdFunctionCount = $functions.Count
        for ($i = 0; $i -lt $functions.Count; $i++) {
            $functionName = $functions[$i]
            Set-Cell $inventory ($i + 2) 1 $functionName
            Set-Cell $inventory ($i + 2) 2 "Implemented"
            Set-Cell $inventory ($i + 2) 3 "Needs paired UX evidence"
            Set-Cell $inventory ($i + 2) 4 "docs/parity/functions.md"
        }
        if ($functions.Count -gt 0) {
            [void](Add-Table $inventory ("A1:D" + ($functions.Count + 1)) "UxParityFunctionInventory")
        }
        $inventory.Columns.AutoFit() | Out-Null

        $features = $workbook.Worksheets.Item(5)
        Rename-Sheet $features "Feature Matrix"
        $featureHeaders = @("Area", "Target", "Mouse", "Keyboard", "Dialog", "Persistence", "Excel evidence", "FreeX evidence", "Disparity")
        for ($i = 0; $i -lt $featureHeaders.Count; $i++) {
            $featureHeader = $featureHeaders[$i]
            Set-Cell $features 1 ($i + 1) $featureHeader
        }
        $featureRows = @(
            @("Chrome", "title bar, QAT, system menu", "Planned", "Planned", "N/A", "N/A", "", "", ""),
            @("Grid", "selection, edit, fill, resize, scroll", "Planned", "Planned", "N/A", "Workbook delta", "", "", ""),
            @("Ribbon", "all tabs and contextual tabs", "Planned", "Keytips planned", "Dropdowns planned", "N/A", "", "", ""),
            @("Backstage", "open/save/export/options/account", "Planned", "Access keys planned", "Native dialogs planned", "Output files", "", "", ""),
            @("Dialogs", "all modal/modeless app dialogs", "Planned", "Tab/access keys planned", "Planned", "Workbook delta", "", "", ""),
            @("Objects", "charts, shapes, pictures, sparklines", "Planned", "Planned", "Format panes/dialogs", "XLSX/FXL", "", "", ""),
            @("Analysis", "tables, pivots, filters, what-if", "Planned", "Planned", "Planned", "XLSX/FXL", "", "", "")
        )
        for ($r = 0; $r -lt $featureRows.Count; $r++) {
            for ($c = 0; $c -lt $featureRows[$r].Count; $c++) {
                $featureValue = $featureRows[$r][$c]
                Set-Cell $features ($r + 2) ($c + 1) $featureValue
            }
        }
        [void](Add-Table $features "A1:I8" "UxParityFeatureMatrix")
        $features.Columns.AutoFit() | Out-Null

        $charts = $workbook.Worksheets.Item(6)
        Rename-Sheet $charts "Charts"
        Set-Cell $charts 1 1 "Month"
        Set-Cell $charts 1 2 "North"
        Set-Cell $charts 1 3 "South"
        Set-Cell $charts 1 4 "East"
        Set-Cell $charts 1 5 "West"
        $chartRows = @(
            @("Jan", 2400, 4180, 0, 0),
            @("Feb", 0, 0, 1750, 3300),
            @("Mar", 5720, 1890, 0, 0),
            @("Apr", 0, 0, 5040, 2860)
        )
        for ($r = 0; $r -lt $chartRows.Count; $r++) {
            for ($c = 0; $c -lt $chartRows[$r].Count; $c++) {
                $chartValue = $chartRows[$r][$c]
                Set-Cell $charts ($r + 2) ($c + 1) $chartValue
            }
        }
        $chartObject = $charts.ChartObjects().Add(360, 24, 420, 260)
        $chart = $chartObject.Chart
        $chart.SetSourceData($charts.Range("A1:E5"))
        $chart.ChartType = 51
        $chart.HasTitle = $true
        $chart.ChartTitle.Text = "Revenue by region"
        $charts.Columns.AutoFit() | Out-Null

        $pivots = $workbook.Worksheets.Item(7)
        Rename-Sheet $pivots "Pivot Seed"
        Set-Cell $pivots 1 1 "PivotTable seed"
        Set-Cell $pivots 3 1 "Use this sheet as the paired Excel/FreeX target for PivotTable dialogs, field list, slicers, timelines, and contextual tabs."
        Set-Cell $pivots 5 1 "Source table"
        Set-Cell $pivots 5 2 "Grid Basics!UxParitySales"
        $pivots.Columns.AutoFit() | Out-Null

        $excel.CalculateFullRebuild()
        $workbook.SaveAs($WorkbookPath, 51)

        return [pscustomobject]@{
            Excel = $excel
            Workbook = $workbook
            FunctionCount = $createdFunctionCount
        }
    }
    catch {
        Release-ComObject $workbook
        if ($null -ne $excel) {
            try { $excel.Quit() } catch { }
            Release-ComObject $excel
        }
        throw
    }
}

function New-WorkbookComparisonCopies {
    param(
        [object]$Excel,
        [object]$Workbook,
        [string]$ExcelWorkbookPath,
        [string]$FreeXWorkbookPath
    )

    try {
        $Excel.CalculateFullRebuild()
        $Workbook.Save()
        $Workbook.Close($false)
    }
    finally {
        Release-ComObject $Workbook
    }

    # The baseline must remain a valid Excel-authored package and must not include a dynamic-array
    # rich-data graph. That graph is exercised by its own FreeX parity fixture.
    $linkedDataTypeEntries = Get-LinkedDataTypePackageEntries $ExcelWorkbookPath
    if ($linkedDataTypeEntries.Count -gt 0) {
        throw "The default UX corpus unexpectedly contains rich-data parts: $($linkedDataTypeEntries -join ', ')"
    }

    [System.IO.File]::Copy($ExcelWorkbookPath, $FreeXWorkbookPath, $true)
    $excelHash = (Get-FileHash -LiteralPath $ExcelWorkbookPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $freeXHash = (Get-FileHash -LiteralPath $FreeXWorkbookPath -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($excelHash -cne $freeXHash) {
        throw "Excel and FreeX workbook copies are not byte-identical after cloning."
    }

    return [pscustomobject]@{
        Workbook = $Excel.Workbooks.Open($ExcelWorkbookPath)
        ContentHashSha256 = $excelHash
        LinkedDataTypeEntries = $linkedDataTypeEntries
    }
}

function Start-FreeXDesktopHost {
    param(
        [string]$ExePath,
        [string]$WorkbookPath
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $ExePath
    $startInfo.Arguments = '"' + $WorkbookPath + '"'
    $startInfo.WorkingDirectory = Split-Path -Parent $ExePath
    $startInfo.UseShellExecute = $false

    # Do not inherit a developer-local runtime root that can hide the installed host runtime.
    foreach ($variableName in @("DOTNET_ROOT", "DOTNET_ROOT_X64", "DOTNET_ROOT_X86", "DOTNET_ROOT_ARM64")) {
        [void]$startInfo.EnvironmentVariables.Remove($variableName)
    }

    return [System.Diagnostics.Process]::Start($startInfo)
}

$repoRoot = Get-RepoRoot -ScriptRoot $PSScriptRoot
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$runRoot = Join-Path (Resolve-Path $repoRoot).Path $OutputRoot
$runDir = Join-Path $runRoot $timestamp
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

$manifestPath = Join-Path $runDir "ux-parity-run.json"
$excelWorkbookPath = Join-Path $runDir "excel-workbook.xlsx"
$freeXWorkbookPath = Join-Path $runDir "freex-workbook.xlsx"
$excelBundle = $null
$freeXProcess = $null

$manifest = [ordered]@{
    schemaVersion = 1
    suite = $Suite
    status = "started"
    createdAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    machine = $env:COMPUTERNAME
    user = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    repo = [ordered]@{
        root = $repoRoot
        branch = Get-GitValue $repoRoot @("rev-parse", "--abbrev-ref", "HEAD")
        commit = Get-GitValue $repoRoot @("rev-parse", "HEAD")
        status = Get-GitValue $repoRoot @("status", "--short", "--branch")
    }
    workbook = [ordered]@{
        excelPath = $excelWorkbookPath
        freexPath = $freeXWorkbookPath
        authoringApp = "Microsoft Excel COM"
        initialContentHashSha256 = $null
        copiesByteIdentical = $false
        linkedDataTypes = [ordered]@{
            policy = "must-not-appear-in-default-manual-corpus"
            detectedEntries = @()
        }
        functionInventoryCount = 0
    }
    excel = [ordered]@{
        launched = $false
        processId = $null
        version = $null
        hwnd = $null
    }
    freex = [ordered]@{
        launched = $false
        processId = $null
        exe = $null
        startupWorkbook = $freeXWorkbookPath
    }
    scenarioMatrix = @(
        [ordered]@{ id = "launch.open-corpus"; area = "App launch"; status = "started"; evidence = @() },
        [ordered]@{ id = "chrome.window-qat"; area = "Chrome"; status = "planned"; evidence = @() },
        [ordered]@{ id = "grid.mouse-keyboard"; area = "Worksheet grid"; status = "planned"; evidence = @() },
        [ordered]@{ id = "ribbon.all-tabs-keytips"; area = "Ribbon"; status = "planned"; evidence = @() },
        [ordered]@{ id = "dialogs.full-catalog"; area = "Dialogs"; status = "planned"; evidence = @() },
        [ordered]@{ id = "workbook.feature-corpus"; area = "Workbook features"; status = "started"; evidence = @($excelWorkbookPath, $freeXWorkbookPath) },
        [ordered]@{ id = "visual.paired-screenshots"; area = "Visual comparison"; status = "planned"; evidence = @() },
        [ordered]@{ id = "disparities.triage"; area = "Disparity log"; status = "planned"; evidence = @() }
    )
    notes = @(
        "Excel COM and FreeX launch are serialized. The foreground mouse/keyboard scenario tools should append evidence to this run folder.",
        "External connections, cloud services, proprietary automation, Data Model, and OLAP remain out of scope unless separately requested."
    )
}

try {
    $excelBundle = Build-CorpusWorkbook $repoRoot $excelWorkbookPath
    $comparisonCopies = New-WorkbookComparisonCopies $excelBundle.Excel $excelBundle.Workbook $excelWorkbookPath $freeXWorkbookPath
    $excelBundle.Workbook = $comparisonCopies.Workbook
    $manifest.workbook.functionInventoryCount = $excelBundle.FunctionCount
    $manifest.workbook.initialContentHashSha256 = $comparisonCopies.ContentHashSha256
    $manifest.workbook.copiesByteIdentical = $true
    $manifest.workbook.linkedDataTypes.detectedEntries = @($comparisonCopies.LinkedDataTypeEntries)
    $manifest.excel.launched = $true
    $manifest.excel.version = [string]$excelBundle.Excel.Version
    $manifest.excel.hwnd = [int]$excelBundle.Excel.Hwnd
    try {
        $manifest.excel.processId = [System.Diagnostics.Process]::GetProcesses() |
            Where-Object { $_.MainWindowHandle -eq [IntPtr]([int]$excelBundle.Excel.Hwnd) } |
            Select-Object -First 1 -ExpandProperty Id
    }
    catch {
        $manifest.excel.processId = $null
    }

    $freeXPath = Resolve-FreeXExe $repoRoot $FreeXExe -SkipBuild:$SkipFreeXBuild
    $manifest.freex.exe = $freeXPath
    $freeXProcess = Start-FreeXDesktopHost $freeXPath $freeXWorkbookPath
    Start-Sleep -Seconds 8
    $manifest.freex.launched = -not $freeXProcess.HasExited
    $manifest.freex.processId = $freeXProcess.Id

    $manifest.scenarioMatrix[0].status = if ($manifest.freex.launched) { "complete" } else { "blocked" }
    $manifest.scenarioMatrix[5].status = "complete"
    $manifest.status = if ($manifest.freex.launched) { "ready-for-walkthrough" } else { "blocked" }
}
catch {
    $manifest.status = "blocked"
    $manifest.blockReason = $_.Exception.Message
    throw
}
finally {
    $manifest.completedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    $manifest | ConvertTo-Json -Depth 10 | Set-Content -Path $manifestPath -Encoding UTF8

    if (-not $KeepAppsOpen) {
        if ($null -ne $excelBundle) {
            try { $excelBundle.Workbook.Close($false) } catch { }
            try { $excelBundle.Excel.Quit() } catch { }
            Release-ComObject $excelBundle.Workbook
            Release-ComObject $excelBundle.Excel
        }

        if ($null -ne $freeXProcess) {
            try {
                if (-not $freeXProcess.HasExited) {
                    $freeXProcess.CloseMainWindow() | Out-Null
                    if (-not $freeXProcess.WaitForExit(5000)) {
                        $freeXProcess.Kill($true)
                    }
                }
            }
            catch {
            }
        }
    }
}

Write-Host "UX parity suite run manifest: $manifestPath"
Write-Host "Excel workbook copy: $excelWorkbookPath"
Write-Host "FreeX workbook copy: $freeXWorkbookPath"
if ($KeepAppsOpen) {
    Write-Host "Excel and FreeX were left open for interactive walkthrough."
}
