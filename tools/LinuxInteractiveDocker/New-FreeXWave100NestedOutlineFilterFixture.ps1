[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression

function Add-XmlEntry {
    param(
        [Parameter(Mandatory = $true)]
        [System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)]
        [string]$Name,
        [Parameter(Mandatory = $true)]
        [string]$Content
    )

    $entry = $Archive.CreateEntry($Name, [System.IO.Compression.CompressionLevel]::Optimal)
    $entryStream = $entry.Open()
    $writer = [System.IO.StreamWriter]::new($entryStream, [System.Text.UTF8Encoding]::new($false))
    try {
        $writer.Write($Content)
    } finally {
        $writer.Dispose()
        $entryStream.Dispose()
    }
}

function New-InlineStringCell {
    param(
        [Parameter(Mandatory = $true)][string]$Address,
        [Parameter(Mandatory = $true)][string]$Value
    )

    $escaped = [System.Security.SecurityElement]::Escape($Value)
    '<c r="' + $Address + '" t="inlineStr"><is><t>' + $escaped + '</t></is></c>'
}

$output = [System.IO.Path]::GetFullPath($OutputPath)
$parent = [System.IO.Path]::GetDirectoryName($output)
if (-not [string]::IsNullOrWhiteSpace($parent)) {
    [System.IO.Directory]::CreateDirectory($parent) | Out-Null
}
$temporary = "$output.$([guid]::NewGuid().ToString('N')).tmp"

$contentTypes = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>
'@
$rootRels = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
'@
$workbook = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Nested Filter" sheetId="1" r:id="rId1"/></sheets><calcPr calcId="191029" calcMode="auto" fullCalcOnLoad="1" forceFullCalc="1"/></workbook>
'@
$workbookRels = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
'@
$styles = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="0"/><fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
'@

$rows = @(
    @{ Number = 1; Level = 0; Values = @("Region", "Value") },
    @{ Number = 2; Level = 1; Values = @("Keep", "Outer2") },
    @{ Number = 3; Level = 2; Values = @("Drop", "InnerDrop3") },
    @{ Number = 4; Level = 2; Values = @("Keep", "InnerKeep4") },
    @{ Number = 5; Level = 1; Values = @("Keep", "InnerAnchor5") },
    @{ Number = 6; Level = 1; Values = @("Drop", "OuterDrop6") },
    @{ Number = 7; Level = 0; Values = @("Keep", "OuterSummary7") }
)
$rowXml = [System.Text.StringBuilder]::new()
foreach ($row in $rows) {
    $levelAttribute = if ($row.Level -gt 0) { ' outlineLevel="' + $row.Level + '"' } else { '' }
    [void]$rowXml.Append('<row r="' + $row.Number + '"' + $levelAttribute + '>')
    [void]$rowXml.Append((New-InlineStringCell -Address "A$($row.Number)" -Value $row.Values[0]))
    [void]$rowXml.Append((New-InlineStringCell -Address "B$($row.Number)" -Value $row.Values[1]))
    [void]$rowXml.Append('</row>')
}

$worksheet = @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><sheetPr><outlinePr summaryBelow="1" summaryRight="1"/></sheetPr><dimension ref="A1:B7"/><sheetViews><sheetView workbookViewId="0" showGridLines="1"/></sheetViews><sheetFormatPr defaultRowHeight="15" outlineLevelRow="2"/><sheetData>$($rowXml.ToString())</sheetData><autoFilter ref="A1:B7"><filterColumn colId="0"><filters><filter val="Keep"/></filters></filterColumn></autoFilter></worksheet>
"@

try {
    $file = [System.IO.File]::Open($temporary, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    $archive = [System.IO.Compression.ZipArchive]::new($file, [System.IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        Add-XmlEntry $archive '[Content_Types].xml' $contentTypes
        Add-XmlEntry $archive '_rels/.rels' $rootRels
        Add-XmlEntry $archive 'xl/workbook.xml' $workbook
        Add-XmlEntry $archive 'xl/_rels/workbook.xml.rels' $workbookRels
        Add-XmlEntry $archive 'xl/styles.xml' $styles
        Add-XmlEntry $archive 'xl/worksheets/sheet1.xml' $worksheet
    } finally {
        $archive.Dispose()
        $file.Dispose()
    }

    Move-Item -LiteralPath $temporary -Destination $output -Force
} finally {
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
}
