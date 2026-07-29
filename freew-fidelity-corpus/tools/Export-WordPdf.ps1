param(
    [Parameter(Mandatory = $true)][string]$InputPath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [switch]$ReuseRunningWord,
    [switch]$HiddenWord
)

$ErrorActionPreference = 'Stop'

$word = $null
$document = $null
$ownsWord = $false
$originalDisplayAlerts = $null
$originalAutomationSecurity = $null

try {
    if ($ReuseRunningWord) {
        $word = [System.Runtime.InteropServices.Marshal]::GetActiveObject('Word.Application')
        $originalDisplayAlerts = $word.DisplayAlerts
        $originalAutomationSecurity = $word.AutomationSecurity
    }
    else {
        $word = New-Object -ComObject Word.Application
        $word.Visible = -not $HiddenWord
        $ownsWord = $true
    }

    $word.DisplayAlerts = 0
    $word.AutomationSecurity = 3 # msoAutomationSecurityForceDisable
    $document = $word.Documents.Open([IO.Path]::GetFullPath($InputPath), $false, $true)
    $document.ExportAsFixedFormat([IO.Path]::GetFullPath($OutputPath), 17) # wdExportFormatPDF
}
finally {
    if ($null -ne $document) {
        try { $document.Close($false) } catch {}
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($document)
    }
    if ($null -ne $word) {
        if ($ownsWord) {
            try { $word.Quit() } catch {}
        }
        else {
            try { $word.DisplayAlerts = $originalDisplayAlerts } catch {}
            try { $word.AutomationSecurity = $originalAutomationSecurity } catch {}
        }
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($word)
    }
}
