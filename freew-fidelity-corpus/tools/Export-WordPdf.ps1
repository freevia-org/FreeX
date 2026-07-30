param(
    [Parameter(Mandatory = $true)][string]$InputPath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [switch]$ReuseRunningWord,
    [switch]$HiddenWord,
    [ValidateRange(1, 120)][int]$ReadyTimeoutSeconds = 30,
    [string]$TracePath
)

$ErrorActionPreference = 'Stop'

$word = $null
$document = $null
$ownsWord = $false
$originalDisplayAlerts = $null
$originalAutomationSecurity = $null

function Write-WordPdfTrace([string]$Message) {
    $line = "$(Get-Date -Format 'o') [WordPdf] $Message"
    Write-Host $line
    if ($TracePath) {
        Add-Content -LiteralPath $TracePath -Value $line
    }
}

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
                Write-WordPdfTrace "ready: $name $version; documents=$documents"
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
        Write-WordPdfTrace 'acquiring running Word COM instance'
        $word = [System.Runtime.InteropServices.Marshal]::GetActiveObject('Word.Application')
        $originalDisplayAlerts = $word.DisplayAlerts
        $originalAutomationSecurity = $word.AutomationSecurity
    }
    else {
        Write-WordPdfTrace "creating isolated Word COM instance; visible=$(-not $HiddenWord)"
        $word = New-Object -ComObject Word.Application
        $word.Visible = -not $HiddenWord
        $ownsWord = $true
    }

    Wait-WordReady $word $ReadyTimeoutSeconds
    $wordPid = 0
    try {
        $wordPid = [int](Get-Process -Name WINWORD -ErrorAction SilentlyContinue |
            Sort-Object StartTime -Descending |
            Select-Object -First 1 -ExpandProperty Id)
    }
    catch { }
    Write-WordPdfTrace "Word ready; pid=$wordPid; inputLength=$($InputPath.Length); outputLength=$($OutputPath.Length)"
    $word.DisplayAlerts = 0
    $word.AutomationSecurity = 3 # msoAutomationSecurityForceDisable
    Write-WordPdfTrace "opening: $InputPath"
    $document = $word.Documents.Open([IO.Path]::GetFullPath($InputPath), $false, $true)
    Wait-WordReady $word $ReadyTimeoutSeconds
    Write-WordPdfTrace "opened read-only; exporting: $OutputPath"
    $document.ExportAsFixedFormat([IO.Path]::GetFullPath($OutputPath), 17) # wdExportFormatPDF
    if (-not (Test-Path -LiteralPath $OutputPath)) {
        throw "Word returned from ExportAsFixedFormat without creating '$OutputPath'."
    }
    Write-WordPdfTrace "exported: $OutputPath"
}
finally {
    if ($null -ne $document) {
        try { $document.Close($false); Write-WordPdfTrace 'closed read-only document' } catch {}
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($document)
    }
    if ($null -ne $word) {
        if ($ownsWord) {
            try { $word.Quit(); Write-WordPdfTrace 'quit owned Word instance' } catch {}
        }
        else {
            try { $word.DisplayAlerts = $originalDisplayAlerts } catch {}
            try { $word.AutomationSecurity = $originalAutomationSecurity } catch {}
            Write-WordPdfTrace 'released running Word instance without quitting it'
        }
        [void][System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($word)
    }
}
