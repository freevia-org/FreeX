param(
    [string]$SourceInventory = "docs\TOOLBAR_ICON_DESIGN_INVENTORY.md",
    [string]$OutputPath = "docs\ICONSET_PREVIEW.html",
    [switch]$SkipExcelLinkValidation
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

function ConvertTo-Slug {
    param([string]$Text)

    $builder = [System.Text.StringBuilder]::new()
    $pendingDash = $false
    foreach ($ch in $Text.Trim().ToLowerInvariant().ToCharArray()) {
        if (($ch -ge 'a' -and $ch -le 'z') -or ($ch -ge '0' -and $ch -le '9')) {
            if ($pendingDash -and $builder.Length -gt 0) {
                [void]$builder.Append('-')
            }
            [void]$builder.Append($ch)
            $pendingDash = $false
        } else {
            $pendingDash = $builder.Length -gt 0
        }
    }

    return $builder.ToString().Trim('-')
}

function ConvertTo-ImageMsoGuess {
    param([string]$Text)

    $tokens = @([regex]::Matches($Text, '[A-Za-z0-9]+') | ForEach-Object { $_.Value })
    if ($tokens.Count -eq 0) {
        return ""
    }

    return ($tokens | ForEach-Object {
        if ($_.Length -le 1) {
            $_.ToUpperInvariant()
        } else {
            $_.Substring(0, 1).ToUpperInvariant() + $_.Substring(1)
        }
    }) -join ''
}

function Split-MarkdownRow {
    param([string]$Line)

    return $Line.Trim().Trim('|').Split('|') | ForEach-Object { $_.Trim() }
}

function Remove-Markdown {
    param([string]$Text)

    $clean = [regex]::Replace($Text, '!\[[^\]]*\]\([^)]+\)', '')
    $clean = [regex]::Replace($clean, '\[([^\]]+)\]\([^)]+\)', '$1')
    $clean = $clean.Replace('**', '').Replace('<br>', ' ')
    return [System.Net.WebUtility]::HtmlDecode($clean).Trim()
}

function Get-PreviewSlug {
    param([string]$Line, [string]$Command)

    $match = [regex]::Match($Line, 'command-icons/40/([^)\s>]+)\.png')
    if ($match.Success) {
        return $match.Groups[1].Value
    }

    return ConvertTo-Slug $Command
}

function Get-ImageMsoName {
    param(
        [string]$Command,
        [string]$Notes
    )

    $reference = [regex]::Match($Notes, 'ImageMso reference:\s*([A-Za-z0-9_]+)')
    if ($reference.Success) {
        return $reference.Groups[1].Value
    }

    $map = @{
        "Freexcel" = "MicrosoftExcel"
        "Save" = "FileSave"
        "Save As" = "FileSaveAs"
        "Undo" = "Undo"
        "Redo" = "Redo"
        "Minimize" = "WindowMinimize"
        "Maximize/Restore" = "WindowMaximize"
        "Close" = "FileClose"
        "Back to workbook" = "FileBack"
        "New" = "FileNew"
        "Open" = "FileOpen"
        "Print" = "FilePrint"
        "Export" = "FileSaveAsPdfOrXps"
        "Options" = "ApplicationOptionsDialog"
        "Recent" = "FileRecent"
        "Info" = "Info"
        "Account" = "AccountMenu"
        "Share" = "Share"
        "Paste" = "Paste"
        "Cut" = "Cut"
        "Copy" = "Copy"
        "Paste Special" = "PasteSpecialDialog"
        "Bold" = "Bold"
        "Italic" = "Italic"
        "Underline" = "Underline"
        "Double Underline" = "DoubleUnderline"
        "Borders" = "BordersGallery"
        "Border presets" = "BordersGallery"
        "Accounting/Currency" = "AccountingFormat"
        "Currency" = "ApplyCurrencyFormat"
        "Percent" = "ApplyPercentageFormat"
        "Percent Style" = "ApplyPercentageFormat"
        "Comma Style" = "ApplyCommaFormat"
        "Increase Decimal" = "DecimalsIncrease"
        "Decrease Decimal" = "DecimalsDecrease"
        "Conditional Formatting" = "ConditionalFormattingMenu"
        "Format as Table" = "FormatAsTableGallery"
        "Cell Styles" = "CellStylesGallery"
        "Insert" = "CellsInsertDialog"
        "Delete" = "CellsDelete"
        "Format" = "FormatCellsDialog"
        "Fill" = "FillMenu"
        "Clear" = "ClearMenu"
        "Sort & Filter" = "SortAndFilterMenu"
        "Find & Select" = "FindDialog"
        "PivotTable" = "PivotTableInsert"
        "Table" = "TableInsert"
        "Pictures" = "PictureInsertFromFile"
        "Shapes" = "ShapesInsertGallery"
        "Icons" = "IconsInsert"
        "SmartArt" = "SmartArtInsert"
        "Column Chart" = "ChartInsertColumn"
        "Line Chart" = "ChartInsertLine"
        "Pie Chart" = "ChartInsertPie"
        "Bar Chart" = "ChartInsertBar"
        "Area Chart" = "ChartInsertArea"
        "Scatter Chart" = "ChartInsertXYScatter"
        "Sparklines" = "SparklineInsert"
        "Hyperlink" = "HyperlinkInsert"
        "Text Box" = "TextBoxInsert"
        "Header & Footer" = "HeaderFooterInsert"
        "Equation" = "EquationInsertGallery"
        "Symbol" = "SymbolInsert"
        "Themes" = "ThemesGallery"
        "Colors" = "ThemeColorsGallery"
        "Fonts" = "ThemeFontsGallery"
        "Effects" = "ThemeEffectsGallery"
        "Margins" = "PageMarginsGallery"
        "Page Orientation" = "PageOrientationGallery"
        "Size" = "PageSizeGallery"
        "Print Area" = "PrintAreaMenu"
        "Breaks" = "PageBreakInsertOrRemove"
        "Background" = "BackgroundImageGallery"
        "Print Titles" = "PrintTitles"
        "Insert Function" = "FunctionWizard"
        "Recently Used" = "FunctionsRecentlyUsed"
        "AutoSum" = "AutoSum"
        "Financial" = "FunctionsFinancial"
        "Logical" = "FunctionsLogical"
        "Text" = "FunctionsText"
        "Date & Time" = "FunctionsDateTime"
        "Lookup & Reference" = "FunctionsLookupReference"
        "Math & Trig" = "FunctionsMathTrig"
        "More Functions" = "FunctionsMore"
        "Name Manager" = "NameManager"
        "Define Name" = "NameDefine"
        "Use in Formula" = "UseInFormulaMenu"
        "Create from Selection" = "NameCreateFromSelection"
        "Trace Precedents" = "TracePrecedents"
        "Trace Dependents" = "TraceDependents"
        "Remove Arrows" = "RemoveArrows"
        "Show Formulas" = "ShowFormulas"
        "Error Checking" = "ErrorChecking"
        "Evaluate Formula" = "EvaluateFormula"
        "Watch Window" = "WatchWindow"
        "Calculation Options" = "CalculationOptionsMenu"
        "Calculate Now" = "CalculateNow"
        "Calculate Sheet" = "CalculateSheet"
        "Get Data" = "GetExternalDataFromText"
        "Refresh All" = "RefreshAll"
        "Queries & Connections" = "Connections"
        "Sort Ascending" = "SortAscendingExcel"
        "Sort Descending" = "SortDescendingExcel"
        "Filter" = "Filter"
        "Advanced Filter" = "AdvancedFilterDialog"
        "Text to Columns" = "TextToColumns"
        "Flash Fill" = "FlashFill"
        "Remove Duplicates" = "RemoveDuplicates"
        "Data Validation" = "DataValidation"
        "Consolidate" = "Consolidate"
        "Relationships" = "Relationships"
        "What-If Analysis" = "WhatIfAnalysisMenu"
        "Forecast Sheet" = "ForecastSheet"
        "Subtotal" = "Subtotal"
        "Group" = "Group"
        "Ungroup" = "Ungroup"
        "Show Detail" = "ShowDetail"
        "Hide Detail" = "HideDetail"
        "Spelling" = "Spelling"
        "Thesaurus" = "Thesaurus"
        "Workbook Statistics" = "Statistics"
        "Accessibility" = "AccessibilityChecker"
        "Translate" = "Translate"
        "New Comment" = "ReviewNewComment"
        "Delete Comment" = "ReviewDeleteComment"
        "Previous Comment" = "ReviewPreviousComment"
        "Next Comment" = "ReviewNextComment"
        "Show Comments" = "ReviewShowComments"
        "New Note" = "ReviewNewComment"
        "Protect Sheet" = "SheetProtect"
        "Protect Workbook" = "WorkbookProtect"
        "Normal" = "ViewNormalViewExcel"
        "Page Break Preview" = "ViewPageBreakPreviewView"
        "Page Layout" = "ViewPageLayoutView"
        "Custom Views" = "CustomViews"
        "Ruler" = "ViewRuler"
        "Gridlines" = "Gridlines"
        "Headings" = "ViewHeadings"
        "Formula Bar" = "FormulaBar"
        "Freeze Panes" = "FreezePanes"
        "Split" = "WindowSplit"
        "Zoom to 100%" = "Zoom100"
        "Zoom to Selection" = "ZoomToSelection"
        "New Window" = "NewWindow"
        "Arrange All" = "ArrangeAll"
        "View Side by Side" = "ViewSideBySide"
        "Synchronous Scrolling" = "SynchronousScrolling"
        "Reset Window Position" = "ResetWindowPosition"
        "Switch Windows" = "SwitchWindows"
        "Help" = "Help"
        "Send Feedback" = "Feedback"
        "About" = "Info"
    }

    if ($map.ContainsKey($Command)) {
        return $map[$Command]
    }

    return ConvertTo-ImageMsoGuess $Command
}

function New-FreexcelIconCell {
    param(
        [string]$Slug,
        [string]$Suffix,
        [string]$Label,
        [int]$CssSize
    )

    $fileName = if ($Suffix) { "$Slug-$Suffix.svg" } else { "$Slug.svg" }
    $relativePath = "../src/Freexcel.App.Host/Resources/CommandIconsSvg/$fileName"
    $literalPath = Join-Path (Split-Path $PSScriptRoot -Parent) "src\Freexcel.App.Host\Resources\CommandIconsSvg\$fileName"
    if (-not (Test-Path -LiteralPath $literalPath)) {
        return "<span class=""missing"">missing<br><code>$fileName</code></span>"
    }

    return "<div class=""icon-sample""><img src=""$relativePath"" width=""$CssSize"" height=""$CssSize"" alt=""$Label $CssSize px""><code>$fileName</code></div>"
}

function New-AppIconCell {
    param(
        [string]$Label,
        [int]$CssSize
    )

    return "<div class=""icon-sample""><img src=""../src/Freexcel.App.Host/Resources/Freexcel.ico"" width=""$CssSize"" height=""$CssSize"" alt=""$Label app icon $CssSize px""><code>Freexcel.ico</code></div>"
}

function New-NoIconCell {
    param([string]$Reason)
    return "<span class=""no-icon"">$(HtmlEncode $Reason)</span>"
}

$script:ExcelIconAvailability = @{}
function Test-ExcelIconName {
    param([string]$ImageMso)

    if ([string]::IsNullOrWhiteSpace($ImageMso)) {
        return $false
    }

    if ($SkipExcelLinkValidation) {
        return $true
    }

    if ($script:ExcelIconAvailability.ContainsKey($ImageMso)) {
        return $script:ExcelIconAvailability[$ImageMso]
    }

    $url = "https://www.spreadsheet1.com/imagemso/$ImageMso.png"
    try {
        $request = [System.Net.HttpWebRequest][System.Net.WebRequest]::Create($url)
        $request.Method = "HEAD"
        $request.UserAgent = "Mozilla/5.0"
        $request.Timeout = 5000
        $response = $request.GetResponse()
        try {
            $ok = [int]$response.StatusCode -ge 200 -and [int]$response.StatusCode -lt 300
        } finally {
            $response.Close()
        }
    } catch {
        $ok = $false
    }

    $script:ExcelIconAvailability[$ImageMso] = $ok
    return $ok
}

function HtmlEncode {
    param([string]$Text)
    return [System.Net.WebUtility]::HtmlEncode($Text)
}

$repoRoot = Split-Path $PSScriptRoot -Parent
$sourcePath = Join-Path $repoRoot $SourceInventory
$outputFullPath = Join-Path $repoRoot $OutputPath
$lines = Get-Content -LiteralPath $sourcePath

$rows = [System.Collections.Generic.List[object]]::new()
$tab = ""
$section = ""
$headers = @()
$activeTable = $false

foreach ($line in $lines) {
    if ($line -match '^##\s+(.+)$') {
        $tab = $Matches[1].Trim()
        $section = ""
        $activeTable = $false
        continue
    }

    if ($line -match '^###\s+(.+)$') {
        $section = $Matches[1].Trim()
        $activeTable = $false
        continue
    }

    if ($line -notmatch '^\|') {
        $activeTable = $false
        continue
    }

    if ($line -match '^\|[-:\s|]+\|?$') {
        $activeTable = $true
        continue
    }

    $cells = @(Split-MarkdownRow $line)
    if (-not $activeTable) {
        $headers = $cells
        continue
    }

    $headerText = $headers -join '|'
    if ($headerText -match 'Rule|16 px|Status') {
        continue
    }

    $commandIndex = 0
    if (($headers -contains "Wording") -and ($headers -contains "Surface")) {
        $commandIndex = [array]::IndexOf($headers, "Wording")
    } elseif ($headers -contains "Command wording") {
        $commandIndex = [array]::IndexOf($headers, "Command wording")
    } elseif ($headers -contains "Tab command") {
        $commandIndex = [array]::IndexOf($headers, "Tab command")
    }

    if ($cells.Count -le $commandIndex) {
        continue
    }

    $command = Remove-Markdown $cells[$commandIndex]
    if ([string]::IsNullOrWhiteSpace($command) -or $command -eq "N/A") {
        continue
    }

    $notes = if ($cells.Count -gt 0) { Remove-Markdown $cells[$cells.Count - 1] } else { "" }
    $slug = Get-PreviewSlug $line $command
    $imageMso = Get-ImageMsoName $command $notes

    $rows.Add([pscustomobject]@{
        Tab = $tab
        Section = if ($section) { $section } elseif ($headers -contains "Surface" -and $cells.Count -gt 0) { Remove-Markdown $cells[0] } else { $tab }
        Command = $command
        Slug = $slug
        Notes = $notes
        ImageMso = $imageMso
    })
}

$htmlRows = [System.Collections.Generic.List[string]]::new()
$lastTab = ""
$lastSection = ""
foreach ($row in $rows) {
    if ($row.Tab -ne $lastTab) {
        $lastTab = $row.Tab
        $lastSection = ""
        $htmlRows.Add("<tr class=""tab-row""><th colspan=""8"">$(HtmlEncode $row.Tab)</th></tr>")
    }

    if ($row.Section -ne $lastSection) {
        $lastSection = $row.Section
        $htmlRows.Add("<tr class=""section-row""><th colspan=""8"">$(HtmlEncode $row.Section)</th></tr>")
    }

    $hasNoIcon = $row.Notes -match 'No icon|Keep as text control|native slider'
    if ($row.Command -eq "Freexcel") {
        $small = New-AppIconCell $row.Command 22
        $base = New-AppIconCell $row.Command 32
        $large = New-AppIconCell $row.Command 40
    } elseif ($hasNoIcon) {
        $small = New-NoIconCell "text/native control"
        $base = New-NoIconCell "text/native control"
        $large = New-NoIconCell "text/native control"
    } else {
        $small = New-FreexcelIconCell $row.Slug "small" $row.Command 22
        $base = New-FreexcelIconCell $row.Slug "" $row.Command 32
        $large = New-FreexcelIconCell $row.Slug "large" $row.Command 32
    }
    $excelUrl = if ($row.ImageMso) { "https://www.spreadsheet1.com/imagemso/$($row.ImageMso).png" } else { "" }
    $excelCell = if ($excelUrl -and (Test-ExcelIconName $row.ImageMso)) {
        "<div class=""icon-sample excel""><img src=""$excelUrl"" width=""32"" height=""32"" alt=""Excel $($row.ImageMso)""><code>$($row.ImageMso)</code></div>"
    } elseif ($row.ImageMso) {
        "<span class=""missing"">ImageMso<br><code>$($row.ImageMso)</code><br>icon unavailable</span>"
    } else {
        "<span class=""missing"">mapping needed</span>"
    }

    $htmlRows.Add(@"
<tr>
  <td class="label">$(HtmlEncode $row.Command)</td>
  <td><code>$(HtmlEncode $row.Slug)</code></td>
  <td>$small</td>
  <td>$base</td>
  <td>$large</td>
  <td>$excelCell</td>
  <td class="notes">$(HtmlEncode $row.Notes)</td>
  <td class="comments"></td>
</tr>
"@)
}

$html = @"
<!doctype html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>Freexcel Iconset Preview</title>
  <style>
    :root { color-scheme: light; --green:#217346; --grid:#d7d7d7; --ink:#1f1f1f; --muted:#666; --soft:#f6f7f8; }
    body { margin: 0; font: 13px/1.35 "Segoe UI", Arial, sans-serif; color: var(--ink); background: white; }
    header { position: sticky; top: 0; z-index: 2; background: white; border-bottom: 2px solid var(--green); padding: 14px 18px 12px; }
    h1 { font-size: 22px; margin: 0 0 4px; font-weight: 600; }
    .meta { color: var(--muted); }
    main { padding: 14px 18px 28px; }
    table { width: 100%; border-collapse: collapse; table-layout: fixed; }
    th, td { border: 1px solid var(--grid); padding: 6px; vertical-align: middle; background: white; }
    thead th { position: sticky; top: 69px; z-index: 1; background: #eef5f1; color: #123b25; font-weight: 600; }
    .tab-row th { background: var(--green); color: white; text-align: left; font-size: 17px; padding: 9px 10px; }
    .section-row th { background: #e7f1eb; color: #123b25; text-align: left; font-size: 14px; padding: 7px 10px; }
    .label { font-weight: 600; }
    .icon-sample { display: flex; align-items: center; gap: 7px; min-height: 34px; }
    .icon-sample img { flex: 0 0 auto; object-fit: contain; image-rendering: auto; }
    .icon-sample code { display: block; font-size: 10px; color: #555; white-space: normal; overflow-wrap: anywhere; }
    .excel img { background: #fff; border: 1px solid #e4e4e4; padding: 3px; }
    .missing { display: inline-block; color: #9a3412; font-size: 11px; line-height: 1.2; }
    .no-icon { color: #666; font-size: 12px; font-style: italic; }
    .notes { color: #555; font-size: 12px; }
    .comments { background: #fffdf5; min-width: 160px; }
    col.label { width: 145px; }
    col.slug { width: 150px; }
    col.icon { width: 210px; }
    col.excel { width: 190px; }
    col.notes { width: 260px; }
    col.comments { width: 180px; }
    @media print {
      header, thead th { position: static; }
      body { font-size: 10px; }
      .tab-row th { break-before: page; }
    }
  </style>
</head>
<body>
  <header>
    <h1>Freexcel Iconset Preview</h1>
    <div class="meta">Generated from <code>$SourceInventory</code>. Freexcel columns show current SVG assets from <code>src/Freexcel.App.Host/Resources/CommandIconsSvg</code>. Excel comparison icons are linked by ImageMso name to Spreadsheet1's Microsoft Office ImageMso gallery for review reference.</div>
  </header>
  <main>
    <table>
      <colgroup>
        <col class="label"><col class="slug"><col class="icon"><col class="icon"><col class="icon"><col class="excel"><col class="notes"><col class="comments">
      </colgroup>
      <thead>
        <tr>
          <th>Label / Command</th>
          <th>Freexcel slug</th>
          <th>Freexcel small</th>
          <th>Freexcel base</th>
          <th>Freexcel large</th>
          <th>MS Excel comparison</th>
          <th>Design note</th>
          <th>Comments</th>
        </tr>
      </thead>
      <tbody>
$($htmlRows -join "`n")
      </tbody>
    </table>
  </main>
</body>
</html>
"@

[IO.Directory]::CreateDirectory((Split-Path -Parent $outputFullPath)) | Out-Null
$html = $html -replace "`r`n", "`n" -replace "`r", "`n"
[IO.File]::WriteAllText($outputFullPath, $html, [System.Text.UTF8Encoding]::new($false))
Write-Host "Wrote $outputFullPath with $($rows.Count) command rows."
