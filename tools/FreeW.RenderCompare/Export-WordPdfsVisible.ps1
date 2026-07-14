param(
    [Parameter(Mandatory = $true)]
    [string]$CorpusDir,
    [Parameter(Mandatory = $true)]
    [string]$OutDir,
    [string]$WordApplicationProgId = "Word.Application",
    [string[]]$Docs,
    [int]$DialogTimeoutSeconds = 30,
    [int]$PdfTimeoutSeconds = 120
)

$ErrorActionPreference = 'Stop'

Add-Type @'
using System;
using System.Text;
using System.Runtime.InteropServices;

public static class WordPdfVisibleUi32 {
  public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
  [DllImport("user32.dll", SetLastError=true)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
  [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);
  [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);
}
'@

function Resolve-FullPath([string]$Path) {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Path)
}

function Get-PublishDialogHandle {
    $handles = New-Object System.Collections.Generic.List[IntPtr]
    [WordPdfVisibleUi32]::EnumWindows({
        param($hWnd, $lParam)
        if (-not [WordPdfVisibleUi32]::IsWindowVisible($hWnd)) {
            return $true
        }

        $title = [Text.StringBuilder]::new(512)
        [void][WordPdfVisibleUi32]::GetWindowText($hWnd, $title, $title.Capacity)
        if ($title.ToString() -eq 'Publish as PDF or XPS') {
            $handles.Add($hWnd)
        }

        return $true
    }, [IntPtr]::Zero) | Out-Null

    if ($handles.Count -eq 0) {
        return [IntPtr]::Zero
    }

    return $handles[$handles.Count - 1]
}

function Wait-PublishDialog {
    param([int]$TimeoutSeconds)

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $handle = Get-PublishDialogHandle
        if ($handle -ne [IntPtr]::Zero) {
            return $handle
        }

        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)

    return [IntPtr]::Zero
}

function Invoke-PublishDialog {
    param([IntPtr]$DialogHandle)

    $publish = [WordPdfVisibleUi32]::GetDlgItem($DialogHandle, 1)
    if ($publish -eq [IntPtr]::Zero) {
        throw "Publish button was not found in dialog $DialogHandle."
    }

    [void][WordPdfVisibleUi32]::SendMessage($publish, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)
}

function Wait-File {
    param(
        [string]$Path,
        [int]$TimeoutSeconds
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        if (Test-Path -LiteralPath $Path) {
            $item = Get-Item -LiteralPath $Path
            if ($item.Length -gt 0) {
                return $item
            }
        }

        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)

    return $null
}

function Get-WordApplication {
    param(
        [string]$ProgId,
        [ref]$Created
    )

    try {
        $Created.Value = $false
        return [Runtime.InteropServices.Marshal]::GetActiveObject($ProgId)
    }
    catch {
        $Created.Value = $true
        return New-Object -ComObject $ProgId
    }
}

$corpusDirFull = Resolve-FullPath $CorpusDir
$outDirFull = Resolve-FullPath $OutDir
$logPath = Join-Path $outDirFull 'word-export-visible-ui.csv'
New-Item -ItemType Directory -Force -Path $outDirFull | Out-Null

$files =
if ($Docs) {
    $Docs | ForEach-Object { Join-Path $corpusDirFull $_ } | Where-Object { Test-Path -LiteralPath $_ }
}
else {
    Get-ChildItem -LiteralPath $corpusDirFull -Filter *.docx |
        Where-Object { $_.Name -notlike '~$*' } |
        Sort-Object Name |
        Select-Object -ExpandProperty FullName
}

if (-not $files) {
    throw "No .docx files found under '$corpusDirFull'."
}

$createdWord = $false
$word = Get-WordApplication $WordApplicationProgId ([ref]$createdWord)
$word.Visible = $true
try { $word.DisplayAlerts = 0 } catch {}

$results = New-Object System.Collections.Generic.List[object]
try {
    foreach ($docx in $files) {
        $fixture = Get-Item -LiteralPath $docx
        $baseName = [IO.Path]::GetFileNameWithoutExtension($fixture.Name)
        $sourcePdf = Join-Path $corpusDirFull ($baseName + '.pdf')
        $targetPdf = Join-Path $outDirFull ($baseName + '.pdf')
        Remove-Item -LiteralPath $sourcePdf -ErrorAction SilentlyContinue
        Remove-Item -LiteralPath $targetPdf -ErrorAction SilentlyContinue

        foreach ($doc in @($word.Documents)) {
            if ([string]::Equals($doc.FullName, $fixture.FullName, [StringComparison]::OrdinalIgnoreCase)) {
                $doc.Close($false)
            }
        }

        $doc = $null
        $child = $null
        $status = 'ok'
        $errorMessage = ''
        try {
            $doc = $word.Documents.Open($fixture.FullName, $false, $true, $false)
            $doc.Activate()
            $code = @"
`$ErrorActionPreference='Stop'
`$word=[Runtime.InteropServices.Marshal]::GetActiveObject('$WordApplicationProgId')
`$word.CommandBars.ExecuteMso('FileSaveAsPdfOrXps')
"@
            $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($code))
            $child = Start-Process -FilePath powershell.exe -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-EncodedCommand',$encoded) -PassThru -WindowStyle Hidden
            $dialog = Wait-PublishDialog -TimeoutSeconds $DialogTimeoutSeconds
            if ($dialog -eq [IntPtr]::Zero) {
                throw 'Publish as PDF or XPS dialog did not appear.'
            }

            Invoke-PublishDialog -DialogHandle $dialog
            $published = Wait-File -Path $sourcePdf -TimeoutSeconds $PdfTimeoutSeconds
            if (-not $published) {
                throw "PDF was not created: $sourcePdf"
            }

            Move-Item -LiteralPath $sourcePdf -Destination $targetPdf -Force
            Wait-Process -Id $child.Id -Timeout 10 -ErrorAction SilentlyContinue
            if (-not $child.HasExited) {
                Stop-Process -Id $child.Id -Force -ErrorAction SilentlyContinue
            }
        }
        catch {
            $status = 'fail'
            $errorMessage = $_.Exception.Message
            if ($child -and -not $child.HasExited) {
                Stop-Process -Id $child.Id -Force -ErrorAction SilentlyContinue
            }
        }
        finally {
            if ($doc) {
                try { $doc.Close($false) } catch {}
            }
        }

        $row = [pscustomobject]@{
            file = $fixture.Name
            pdf = $targetPdf
            status = $status
            error = $errorMessage
            bytes = if (Test-Path -LiteralPath $targetPdf) { (Get-Item -LiteralPath $targetPdf).Length } else { 0 }
        }
        $results.Add($row)
        Write-Host ("{0} {1} bytes={2} {3}" -f $row.status, $row.file, $row.bytes, $row.error)
    }
}
finally {
    if ($createdWord -and $word) {
        try { $word.Quit() } catch {}
    }
}

$results | Export-Csv -NoTypeInformation -Path $logPath
$failures = @($results | Where-Object { $_.status -ne 'ok' })
if ($failures.Count -gt 0) {
    throw "$($failures.Count) visible Word PDF export(s) failed. See $logPath"
}

Write-Host "Visible Word PDF exports complete: $($results.Count) PDF(s)."
