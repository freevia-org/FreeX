param(
    [Parameter(Mandatory = $true)][string]$InputPath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [switch]$ReuseRunningWord,
    [switch]$HiddenWord,
    [ValidateRange(1, 120)][int]$ReadyTimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'

$word = $null
$document = $null
$ownsWord = $false
$originalDisplayAlerts = $null
$originalAutomationSecurity = $null

function Wait-WordReady([object]$Application, [int]$TimeoutSeconds) {
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $lastFailure = $null

    do {
        try {
            $name = [string]$Application.Name
            $version = [string]$Application.Version
            $documents = [int]$Application.Documents.Count
            $saving = [int]$Application.BackgroundSavingStatus
            $printing = [int]$Application.BackgroundPrintingStatus
            if (-not [string]::IsNullOrWhiteSpace($name) -and
                -not [string]::IsNullOrWhiteSpace($version) -and
                $saving -eq 0 -and $printing -eq 0) {
                Write-Host "[WordPdf] ready: $name $version; documents=$documents"
                return
            }

            $lastFailure = "Word reported background save=$saving, print=$printing."
        }
        catch {
            $lastFailure = $_.Exception.Message
        }

        Start-Sleep -Milliseconds 250
    }
    while ([DateTime]::UtcNow -lt $deadline)

    $detail = if ($lastFailure) { " Last observation: $lastFailure" } else { '' }
    throw "Word did not become ready within $TimeoutSeconds seconds.$detail"
}

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

    Wait-WordReady $word $ReadyTimeoutSeconds
    $word.DisplayAlerts = 0
    $word.AutomationSecurity = 3 # msoAutomationSecurityForceDisable
    Write-Host "[WordPdf] opening: $InputPath"
    $document = $word.Documents.Open([IO.Path]::GetFullPath($InputPath), $false, $true)
    Wait-WordReady $word $ReadyTimeoutSeconds
    Write-Host "[WordPdf] exporting: $OutputPath"
    $document.ExportAsFixedFormat([IO.Path]::GetFullPath($OutputPath), 17) # wdExportFormatPDF
    Write-Host "[WordPdf] exported: $OutputPath"
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
