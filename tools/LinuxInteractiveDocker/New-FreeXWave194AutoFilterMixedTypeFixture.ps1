[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression

function Add-XmlEntry {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$Content
    )
    $entry = $Archive.CreateEntry($Name, [System.IO.Compression.CompressionLevel]::Optimal)
    $stream = $entry.Open()
    $writer = [System.IO.StreamWriter]::new($stream, [System.Text.UTF8Encoding]::new($false))
    try { $writer.Write($Content) } finally { $writer.Dispose(); $stream.Dispose() }
}

function New-InlineStringCell {
    param([Parameter(Mandatory = $true)][string]$Address, [Parameter(Mandatory = $true)][string]$Value)
    $escaped = [System.Security.SecurityElement]::Escape($Value)
    '<c r="' + $Address + '" t="inlineStr"><is><t>' + $escaped + '</t></is></c>'
}

$output = [System.IO.Path]::GetFullPath($OutputPath)
$parent = [System.IO.Path]::GetDirectoryName($output)
if (-not [string]::IsNullOrWhiteSpace($parent)) { [System.IO.Directory]::CreateDirectory($parent) | Out-Null }
$temporary = Join-Path $parent ".freex-wave194-$PID-$([guid]::NewGuid().ToString('N').Substring(0, 8)).tmp"

$contentTypes = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/></Types>'
$rootRels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>'
$workbook = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="AutoFilter Mixed" sheetId="1" r:id="rId1"/></sheets><calcPr calcId="194029" calcMode="auto" fullCalcOnLoad="1" forceFullCalc="1"/></workbook>'
$workbookRels = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>'
$styles = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?><styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"><numFmts count="1"><numFmt numFmtId="164" formatCode="yyyy-mm-dd"/></numFmts><fonts count="1"><font><sz val="11"/><name val="Calibri"/></font></fonts><fills count="2"><fill><patternFill patternType="none"/></fill><fill><patternFill patternType="gray125"/></fill></fills><borders count="1"><border><left/><right/><top/><bottom/><diagonal/></border></borders><cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/></cellStyleXfs><cellXfs count="2"><xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/><xf numFmtId="164" fontId="0" fillId="0" borderId="0" xfId="0" applyNumberFormat="1"/></cellXfs><cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles></styleSheet>'

$rows = [System.Text.StringBuilder]::new()
[void]$rows.Append('<row r="1">')
[void]$rows.Append((New-InlineStringCell -Address "A1" -Value "Mixed"))
[void]$rows.Append((New-InlineStringCell -Address "B1" -Value "Kind"))
[void]$rows.Append('<c r="C1"><f>SUBTOTAL(103,A2:A7)</f><v>5</v></c></row>')
[void]$rows.Append('<row r="2"><c r="A2"><v>42</v></c>')
[void]$rows.Append((New-InlineStringCell -Address "B2" -Value "Number"))
[void]$rows.Append('</row><row r="3">')
[void]$rows.Append((New-InlineStringCell -Address "A3" -Value "42"))
[void]$rows.Append((New-InlineStringCell -Address "B3" -Value "NumericText"))
[void]$rows.Append('</row><row r="4">')
[void]$rows.Append((New-InlineStringCell -Address "A4" -Value "Alpha"))
[void]$rows.Append((New-InlineStringCell -Address "B4" -Value "Text"))
[void]$rows.Append('</row><row r="5">')
[void]$rows.Append((New-InlineStringCell -Address "B5" -Value "Blank"))
[void]$rows.Append('</row><row r="6"><c r="A6" s="1"><v>45292</v></c>')
[void]$rows.Append((New-InlineStringCell -Address "B6" -Value "Date"))
[void]$rows.Append('</row><row r="7"><c r="A7"><v>7</v></c>')
[void]$rows.Append((New-InlineStringCell -Address "B7" -Value "Seven"))
[void]$rows.Append('</row>')
$worksheet = "<?xml version=`"1.0`" encoding=`"UTF-8`" standalone=`"yes`"?><worksheet xmlns=`"http://schemas.openxmlformats.org/spreadsheetml/2006/main`"><dimension ref=`"A1:C7`"/><sheetViews><sheetView workbookViewId=`"0`" showGridLines=`"1`"/></sheetViews><sheetData>$($rows.ToString())</sheetData><autoFilter ref=`"A1:B7`"/></worksheet>"

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
    } finally { $archive.Dispose(); $file.Dispose() }
    Move-Item -LiteralPath $temporary -Destination $output -Force
} finally { Remove-Item -LiteralPath $temporary -Force -ErrorAction SilentlyContinue }
