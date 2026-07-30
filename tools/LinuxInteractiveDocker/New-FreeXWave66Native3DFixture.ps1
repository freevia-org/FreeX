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

function New-NumberCells {
    param(
        [Parameter(Mandatory = $true)]
        [int]$SheetOffset,
        [Parameter(Mandatory = $true)]
        [int]$Row
    )

    $cells = [System.Collections.Generic.List[string]]::new()
    for ($column = 2; $column -le 4; $column++) {
        $columnName = [char](64 + $column)
        $value = $SheetOffset + (($Row - 1) * 3) + $column - 1
        $cells.Add("<c r=`"$columnName$Row`" t=`"n`"><v>$value</v></c>")
    }

    $cells -join ""
}

function New-WorksheetXml {
    param(
        [Parameter(Mandatory = $true)]
        [int]$SheetNumber,
        [Parameter(Mandatory = $true)]
        [int]$SheetOffset
    )

    $cells = if ($SheetNumber -eq 1) {
        "<c r=`"G10`"><f>SUM('O''Brien Data:Revenue Data'!B2:C3)</f><v>88</v></c>"
    } else {
        ""
    }

    $rows = if ($SheetNumber -eq 1) {
        '<row r="10">' + $cells + '</row>'
    } else {
        $rowXml = [System.Text.StringBuilder]::new()
        for ($row = 2; $row -le 4; $row++) {
            [void]$rowXml.Append("<row r=`"$row`">")
            [void]$rowXml.Append((New-NumberCells -SheetOffset $SheetOffset -Row $row))
            [void]$rowXml.Append('</row>')
        }
        $rowXml.ToString()
    }

    @"
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><dimension ref="B2:G10"/><sheetViews><sheetView workbookViewId="0"/></sheetViews><sheetData>$rows</sheetData></worksheet>
"@
}

$output = [System.IO.Path]::GetFullPath($OutputPath)
$parent = [System.IO.Path]::GetDirectoryName($output)
if (-not [string]::IsNullOrWhiteSpace($parent)) {
    [System.IO.Directory]::CreateDirectory($parent) | Out-Null
}
$temporary = "$output.$([guid]::NewGuid().ToString('N')).tmp"

$contentTypes = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet2.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet3.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/worksheets/sheet4.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>
'@
$rootRels = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
'@
$workbook = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="Summary" sheetId="1" r:id="rId1"/><sheet name="Revenue Data" sheetId="2" r:id="rId2"/><sheet name="O'Brien Data" sheetId="3" r:id="rId3"/><sheet name="Tail" sheetId="4" r:id="rId4"/></sheets><calcPr calcId="191029" calcMode="auto" fullCalcOnLoad="1" forceFullCalc="1"/></workbook>
'@
$workbookRels = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet2.xml"/><Relationship Id="rId3" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet3.xml"/><Relationship Id="rId4" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet4.xml"/><Relationship Id="rId5" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
'@
$styles = @'
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="0"/><fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs><cellXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>
'@

try {
    $file = [System.IO.File]::Open($temporary, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    $archive = [System.IO.Compression.ZipArchive]::new($file, [System.IO.Compression.ZipArchiveMode]::Create, $false)
    try {
        Add-XmlEntry $archive '[Content_Types].xml' $contentTypes
        Add-XmlEntry $archive '_rels/.rels' $rootRels
        Add-XmlEntry $archive 'xl/workbook.xml' $workbook
        Add-XmlEntry $archive 'xl/_rels/workbook.xml.rels' $workbookRels
        Add-XmlEntry $archive 'xl/styles.xml' $styles
        Add-XmlEntry $archive 'xl/worksheets/sheet1.xml' (New-WorksheetXml -SheetNumber 1 -SheetOffset 0)
        Add-XmlEntry $archive 'xl/worksheets/sheet2.xml' (New-WorksheetXml -SheetNumber 2 -SheetOffset 0)
        Add-XmlEntry $archive 'xl/worksheets/sheet3.xml' (New-WorksheetXml -SheetNumber 3 -SheetOffset 10)
        Add-XmlEntry $archive 'xl/worksheets/sheet4.xml' (New-WorksheetXml -SheetNumber 4 -SheetOffset 0)
    } finally {
        $archive.Dispose()
        $file.Dispose()
    }

    Move-Item -LiteralPath $temporary -Destination $output -Force
} finally {
    Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue
}
