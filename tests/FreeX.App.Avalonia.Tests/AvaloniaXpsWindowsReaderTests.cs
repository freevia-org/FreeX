extern alias ProductionAvalonia;

using System.IO;
using System.Text;
using Free.Shared.AppServices.Printing;
using FreeX.App.Services;
using FreeX.Core.Model;
using AvaloniaXpsDocumentExporter = ProductionAvalonia::FreeX.App.Avalonia.Pdf.AvaloniaXpsDocumentExporter;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaXpsWindowsReaderTests
{
    [Fact]
    public async Task Publish_PaginatesAndRendersInWindowsXpsReader()
    {
        if (!OperatingSystem.IsWindows())
            return;

        const string cellText = "Native XPS sample";
        using var directory = new TestTemporaryDirectory("FreeX-Xps-");
        var workbook = new Workbook("Workbook");
        var sheet = workbook.AddSheet("Data");
        sheet.PaperSize = WorksheetPaperSize.Letter;
        sheet.PageOrientation = WorksheetPageOrientation.Portrait;
        sheet.PrintGridlines = false;
        sheet.ColumnWidths[1] = 32;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new Cell
        {
            Value = new TextValue(cellText),
            StyleId = workbook.RegisterStyle(new CellStyle
            {
                FontName = "Arial",
                FillColor = new CellColor(230, 190, 160)
            })
        });
        var printPlan = WorkbookExportPrintPlanner.CreatePlanFromPageSetup(
            workbook,
            new WorkbookExportPrintIntent(WorkbookExportPrintScope.ActiveSheet, WorkbookExportPrintOutputKind.Xps),
            WorkbookExportPrintSurface.MacOs);
        var exportPlan = PortablePdfExportPlanner.CreateXpsPlan(printPlan, workbook);

        using (var output = File.Create(Path.Combine(directory.Path, "published.xps")))
            AvaloniaXpsDocumentExporter.Save(workbook, exportPlan, output);

        // Windows PowerShell supplies the native WPF reader in an STA process without adding
        // a Windows desktop dependency to the portable Avalonia test project.
        var checkXps = $$"""
            $ErrorActionPreference = 'Stop'
            Add-Type -AssemblyName WindowsBase, PresentationCore, PresentationFramework, ReachFramework,
                UIAutomationProvider, UIAutomationTypes

            function Get-PageVisuals($visual) {
                $visual
                $children = [System.Windows.Media.VisualTreeHelper]::GetChildrenCount($visual)
                for ($i = 0; $i -lt $children; $i++) {
                    Get-PageVisuals ([System.Windows.Media.VisualTreeHelper]::GetChild($visual, $i))
                }
            }

            $path = Join-Path (Get-Location).Path 'published.xps'
            $document = [System.Windows.Xps.Packaging.XpsDocument]::new($path, [System.IO.FileAccess]::Read)
            try {
                $sequence = $document.GetFixedDocumentSequence()
                $paginator = $sequence.DocumentPaginator
                $page = $paginator.GetPage(0)
                if ($paginator.PageCount -ne 1 -or $page.Size.Width -ne 816 -or $page.Size.Height -ne 1056) {
                    throw "Expected one US Letter page in WPF display units, got $($paginator.PageCount) at $($page.Size)."
                }

                $visuals = @(Get-PageVisuals $page.Visual)
                $paths = @($visuals | Where-Object { $_ -is [System.Windows.Shapes.Path] })
                $cellFill = [System.Windows.Media.Color]::FromRgb(230, 190, 160)
                $cellPaths = @($paths | Where-Object {
                    $_.Fill -is [System.Windows.Media.SolidColorBrush] -and $_.Fill.Color -eq $cellFill
                })
                if ($cellPaths.Count -ne 1) {
                    throw 'The XPS reader did not load the cell background as a native vector path.'
                }
                $imagePaths = @($paths | Where-Object {
                    $_.Fill -is [System.Windows.Media.ImageBrush] -or $_.Stroke -is [System.Windows.Media.ImageBrush]
                })
                if ($imagePaths.Count -ne 0) {
                    throw 'The XPS reader found an image brush on the native worksheet page.'
                }

                $glyphs = @($visuals | Where-Object {
                    $_ -is [System.Windows.Documents.Glyphs] -and $_.UnicodeString -eq '{{cellText}}'
                })
                if ($glyphs.Count -ne 1 -or $glyphs[0].ToGlyphRun().GlyphIndices.Count -ne '{{cellText}}'.Length) {
                    throw 'The XPS reader could not resolve the exported cell font and glyphs.'
                }
                if ($glyphs[0].Fill -isnot [System.Windows.Media.SolidColorBrush] -or $glyphs[0].Fill.Color.A -ne 255) {
                    throw 'The XPS reader found no visible native glyphs for the cell.'
                }

                # WPF implements these public automation interfaces explicitly.
                $peer = [System.Windows.Automation.Peers.DocumentAutomationPeer]::new($sequence)
                $provider = $peer.GetPattern([System.Windows.Automation.Peers.PatternInterface]::Text)
                $range = [System.Windows.Automation.Provider.ITextProvider].GetProperty('DocumentRange').GetValue($provider)
                $rangeType = [System.Windows.Automation.Provider.ITextRangeProvider]
                $match = $rangeType.GetMethod('FindText').Invoke($range, @('{{cellText}}', $false, $false))
                if ($null -eq $match -or $rangeType.GetMethod('GetText').Invoke($match, @(-1)) -cne '{{cellText}}') {
                    throw 'The WPF document text provider could not find and extract the exported cell text.'
                }

                $image = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
                    816, 1056, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
                $image.Render($page.Visual)
                $pixels = [byte[]]::new(816 * 1056 * 4)
                $image.CopyPixels($pixels, 816 * 4, 0)
                $hasCellFill = $false
                $hasInk = $false
                for ($i = 0; $i -lt $pixels.Length; $i += 4) {
                    if ($pixels[$i + 3] -ne 255) {
                        continue
                    }
                    if ($pixels[$i] -eq $cellFill.B -and $pixels[$i + 1] -eq $cellFill.G -and $pixels[$i + 2] -eq $cellFill.R) {
                        $hasCellFill = $true
                    }
                    if ($pixels[$i] -lt 128 -and $pixels[$i + 1] -lt 128 -and $pixels[$i + 2] -lt 128) {
                        $hasInk = $true
                    }
                    if ($hasCellFill -and $hasInk) {
                        return
                    }
                }
                throw 'The XPS reader rendered the page without both the cell background and visible cell text.'
            }
            finally {
                $document.Close()
            }
            """;
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(checkXps));
        var powershell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var invocation = new ProcessInvocation(
            powershell,
            ["-NoProfile", "-NonInteractive", "-STA", "-EncodedCommand", encodedCommand],
            WorkingDirectory: directory.Path,
            Timeout: TimeSpan.FromSeconds(45));

        var result = await new SystemProcessRunner().RunAsync(invocation);
        Assert.True(result.Succeeded,
            $"Windows XPS reader exited with {result.ExitCode} (timed out: {result.TimedOut}).\n{result.StandardOutput}\n{result.StandardError}");
    }
}
