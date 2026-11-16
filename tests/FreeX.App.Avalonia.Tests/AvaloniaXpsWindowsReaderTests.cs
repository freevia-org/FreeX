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
            StyleId = workbook.RegisterStyle(new CellStyle { FontName = "Arial" })
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
            Add-Type -AssemblyName WindowsBase, PresentationCore, PresentationFramework, ReachFramework

            function Get-PageGlyphs($visual) {
                if ($visual -is [System.Windows.Documents.Glyphs]) {
                    $visual
                }
                $children = [System.Windows.Media.VisualTreeHelper]::GetChildrenCount($visual)
                for ($i = 0; $i -lt $children; $i++) {
                    Get-PageGlyphs ([System.Windows.Media.VisualTreeHelper]::GetChild($visual, $i))
                }
            }

            $path = Join-Path (Get-Location).Path 'published.xps'
            $document = [System.Windows.Xps.Packaging.XpsDocument]::new($path, [System.IO.FileAccess]::Read)
            try {
                $paginator = $document.GetFixedDocumentSequence().DocumentPaginator
                $page = $paginator.GetPage(0)
                if ($paginator.PageCount -ne 1 -or $page.Size.Width -ne 816 -or $page.Size.Height -ne 1056) {
                    throw "Expected one US Letter page in WPF display units, got $($paginator.PageCount) at $($page.Size)."
                }

                $glyphs = @(Get-PageGlyphs $page.Visual | Where-Object UnicodeString -eq '{{cellText}}')
                if ($glyphs.Count -eq 0 -or $glyphs[0].ToGlyphRun().GlyphIndices.Count -ne '{{cellText}}'.Length) {
                    throw 'The XPS reader could not resolve the exported cell font and glyphs.'
                }

                $image = [System.Windows.Media.Imaging.RenderTargetBitmap]::new(
                    816, 1056, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
                $image.Render($page.Visual)
                $pixels = [byte[]]::new(816 * 1056 * 4)
                $image.CopyPixels($pixels, 816 * 4, 0)
                for ($i = 0; $i -lt $pixels.Length; $i += 4) {
                    if ($pixels[$i + 3] -eq 255 -and $pixels[$i] -lt 128 -and
                        $pixels[$i + 1] -lt 128 -and $pixels[$i + 2] -lt 128) {
                        return
                    }
                }
                throw 'The XPS reader rendered a page with no visible cell text.'
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
