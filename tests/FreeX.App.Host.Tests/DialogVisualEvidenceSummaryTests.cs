using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class DialogVisualEvidenceSummaryTests
{
    [Fact]
    public void DialogVisualEvidenceSummary_ReportsDeterministicPngTriageAndCheckMode()
    {
        using var temp = new TestTemporaryDirectory();

        var inventoryPath = Path.Combine(temp.Path, "dialog-parity-inventory.json");
        var wpfManifestDirectory = Path.Combine(temp.Path, "wpf-capture");
        var avaloniaManifestDirectory = Path.Combine(temp.Path, "avalonia-capture");
        Directory.CreateDirectory(wpfManifestDirectory);
        Directory.CreateDirectory(avaloniaManifestDirectory);

        var wpfManifestPath = Path.Combine(wpfManifestDirectory, "manifest.json");
        var avaloniaManifestPath = Path.Combine(avaloniaManifestDirectory, "manifest.json");
        var markdownPath = Path.Combine(temp.Path, "summary.md");
        var jsonPath = Path.Combine(temp.Path, "summary.json");

        File.WriteAllText(
            inventoryPath,
            """
            {
              "summary": {
                "totalRoutes": 1,
                "wpfCaptures": 1,
                "avaloniaCaptures": 1,
                "avaloniaHarnessRoutes": 1,
                "sharedOrPresentationBacked": 1
              },
              "rows": [
                { "routeId": "dialog.Sample" }
              ]
            }
            """);

        File.WriteAllText(
            wpfManifestPath,
            """
            {
              "platform": "windows",
              "shell": "wpf",
              "surfaces": [
                {
                  "id": "dialog.Sample.Valid",
                  "kind": "dialog",
                  "png": "dialog.Sample.Valid.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.Sample.Missing",
                  "kind": "dialog",
                  "png": "dialog.Sample.Missing.png",
                  "captured": true,
                  "note": ""
                }
              ]
            }
            """);

        File.WriteAllText(
            avaloniaManifestPath,
            """
            {
              "platform": "windows",
              "shell": "avalonia",
              "surfaces": [
                {
                  "id": "dialog.Sample.Valid",
                  "kind": "dialog",
                  "png": "dialog.Sample.Valid.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.Sample.Missing",
                  "kind": "dialog",
                  "png": "dialog.Sample.Missing.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.Sample.Extra",
                  "kind": "dialog",
                  "png": "dialog.Sample.Extra.png",
                  "captured": true,
                  "note": ""
                }
              ]
            }
            """);

        WritePng(Path.Combine(wpfManifestDirectory, "dialog.Sample.Valid.png"), width: 3, height: 2, nonBlank: true);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.Sample.Valid.png"), width: 5, height: 2, nonBlank: true);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.Sample.Extra.png"), width: 2, height: 3, nonBlank: true);

        var result = PowerShellScriptRunner.RunToolScript(
            "Generate-DialogVisualEvidenceSummary.ps1",
            WorkspaceFileLocator.FindWorkspaceRoot(),
            $"-MarkdownPath \"{markdownPath}\" -JsonPath \"{jsonPath}\" -InventoryPath \"{inventoryPath}\" -WpfManifestPath \"{wpfManifestPath}\" -AvaloniaManifestPath \"{avaloniaManifestPath}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.Output.Should().Contain("WPF captured manifest surfaces with committed PNGs: 1");
        result.Output.Should().Contain("Paired captured surface ids: 1");
        result.Output.Should().Contain("Avalonia-manifest-only screenshot surface ids needing WPF manifest pair: 1");
        result.Output.Should().Contain("Nonblank PNG check failures: 0");
        result.Output.Should().Contain("Paired dimension mismatches: 1");
        result.Output.Should().Contain("Raw PNG pixel dimension mismatches: 1");
        result.Output.Should().Contain("Raw PNG mismatches normalized by capture DPI: 0");
        result.Output.Should().Contain("Paired expected-size evidence mismatches: 0");
        result.Output.Should().Contain("Stale promoted expected-size evidence: 0");
        result.Output.Should().Contain("Dimension mismatch bucket 'real logical-size mismatch': 1");

        var markdown = File.ReadAllText(markdownPath);
        markdown.Should().Contain("| WPF captured manifest surfaces with committed PNGs | 1 |");
        markdown.Should().Contain("| Paired captured surface ids | 1 |");
        markdown.Should().Contain("| Paired dimension mismatches (scale-aware logical units) | 1 |");
        markdown.Should().Contain("| Raw PNG pixel dimension mismatches | 1 |");
        markdown.Should().Contain("| Raw PNG mismatches normalized by capture DPI | 0 |");
        markdown.Should().Contain("| Paired expected-size evidence mismatches | 0 |");
        markdown.Should().Contain("| Stale promoted expected-size evidence | 0 |");
        markdown.Should().Contain("## Scale-Aware Dimension Mismatch Classification");
        markdown.Should().Contain("| real logical-size mismatch | 1 | dialog.Sample.Valid |");
        markdown.Should().Contain("| dialog.Sample.Valid | dialog.Sample.Valid.png | 3x2 | 3x2 px @ 96 DPI | True | dialog.Sample.Valid.png | 5x2 | 5x2 px @ 96 DPI | True | False |");
        markdown.Should().Contain("| dialog.Sample | 1 | dialog.Sample.Extra |");
        markdown.Should().Contain("dialog.Sample.Valid");
        markdown.Should().NotContain("dialog.Sample.Missing |");

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var summary = json.RootElement.GetProperty("summary");
        summary.GetProperty("wpfCapturedManifestSurfaces").GetInt32().Should().Be(1);
        summary.GetProperty("avaloniaCapturedManifestSurfaces").GetInt32().Should().Be(2);
        summary.GetProperty("pairedCapturedSurfaceIds").GetInt32().Should().Be(1);
        summary.GetProperty("additionalAvaloniaCapturedSurfaceIds").GetInt32().Should().Be(1);
        summary.GetProperty("nonBlankPngFailures").GetInt32().Should().Be(0);
        summary.GetProperty("pairedDimensionMismatches").GetInt32().Should().Be(1);
        summary.GetProperty("pairedRawPixelDimensionMismatches").GetInt32().Should().Be(1);
        summary.GetProperty("pairedCaptureScaleNormalizedDimensionMatches").GetInt32().Should().Be(0);
        summary.GetProperty("pairedExpectedSizeMismatches").GetInt32().Should().Be(0);
        summary.GetProperty("stalePromotedExpectedSizeEvidence").GetInt32().Should().Be(0);
        summary.GetProperty("dimensionMismatchBuckets").GetProperty("real logical-size mismatch").GetInt32().Should().Be(1);

        var paired = json.RootElement.GetProperty("pairedSurfaces")[0];
        paired.GetProperty("wpf").GetProperty("width").GetInt32().Should().Be(3);
        paired.GetProperty("avalonia").GetProperty("height").GetInt32().Should().Be(2);
        paired.GetProperty("comparison").GetProperty("dimensionMatch").GetBoolean().Should().BeFalse();
        paired.GetProperty("comparison").GetProperty("logicalDimensionMatch").GetBoolean().Should().BeFalse();
        paired.GetProperty("comparison").GetProperty("rawPixelDimensionMatch").GetBoolean().Should().BeFalse();
        paired.GetProperty("comparison").GetProperty("captureScaleNormalizedDimensionMatch").GetBoolean().Should().BeFalse();
        paired.GetProperty("comparison").GetProperty("expectedSizeMismatch").GetBoolean().Should().BeFalse();
        paired.GetProperty("comparison").GetProperty("dimensionMismatchBucket").GetString().Should().Be("real logical-size mismatch");
        paired.GetProperty("comparison").GetProperty("dimensionMismatchNextAction").GetString().Should().Contain("layout target");

        var checkResult = PowerShellScriptRunner.RunToolScript(
            "Generate-DialogVisualEvidenceSummary.ps1",
            WorkspaceFileLocator.FindWorkspaceRoot(),
            $"-MarkdownPath \"{markdownPath}\" -JsonPath \"{jsonPath}\" -InventoryPath \"{inventoryPath}\" -WpfManifestPath \"{wpfManifestPath}\" -AvaloniaManifestPath \"{avaloniaManifestPath}\" -Check");

        checkResult.ExitCode.Should().Be(0, checkResult.CombinedOutput);
        checkResult.Output.Should().Contain("Dialog visual evidence summary is up to date.");
    }

    [Fact]
    public void DialogVisualEvidenceSummary_ClassifiesKnownDimensionMismatchBuckets()
    {
        using var temp = new TestTemporaryDirectory();

        var inventoryPath = Path.Combine(temp.Path, "dialog-parity-inventory.json");
        var wpfManifestDirectory = Path.Combine(temp.Path, "wpf-capture");
        var avaloniaManifestDirectory = Path.Combine(temp.Path, "avalonia-capture");
        Directory.CreateDirectory(wpfManifestDirectory);
        Directory.CreateDirectory(avaloniaManifestDirectory);

        var wpfManifestPath = Path.Combine(wpfManifestDirectory, "manifest.json");
        var avaloniaManifestPath = Path.Combine(avaloniaManifestDirectory, "manifest.json");
        var markdownPath = Path.Combine(temp.Path, "summary.md");
        var jsonPath = Path.Combine(temp.Path, "summary.json");

        File.WriteAllText(
            inventoryPath,
            """
            {
              "summary": {
                "totalRoutes": 4,
                "wpfCaptures": 4,
                "avaloniaCaptures": 4,
                "avaloniaHarnessRoutes": 4,
                "sharedOrPresentationBacked": 4
              },
              "rows": [
                { "routeId": "dialog.ScenarioManager" },
                { "routeId": "dialog.GoalSeekStatus" },
                { "routeId": "dialog.FindReplace" },
                { "routeId": "dialog.Generic" }
              ]
            }
            """);

        File.WriteAllText(
            wpfManifestPath,
            """
            {
              "platform": "windows",
              "shell": "wpf",
              "surfaces": [
                { "id": "dialog.ScenarioManager", "kind": "dialog", "png": "dialog.ScenarioManager.png", "captured": true, "note": "" },
                { "id": "dialog.GoalSeekStatus", "kind": "dialog", "png": "dialog.GoalSeekStatus.png", "captured": true, "note": "" },
                { "id": "dialog.FindReplace", "kind": "dialog", "png": "dialog.FindReplace.png", "captured": true, "note": "" },
                { "id": "dialog.Generic", "kind": "dialog", "png": "dialog.Generic.png", "captured": true, "note": "" }
              ]
            }
            """);

        File.WriteAllText(
            avaloniaManifestPath,
            """
            {
              "platform": "windows",
              "shell": "avalonia",
              "surfaces": [
                { "id": "dialog.ScenarioManager", "kind": "dialog", "png": "dialog.ScenarioManager.png", "captured": true, "note": "" },
                { "id": "dialog.GoalSeekStatus", "kind": "dialog", "png": "dialog.GoalSeekStatus.png", "captured": true, "note": "" },
                { "id": "dialog.FindReplace", "kind": "dialog", "png": "dialog.FindReplace.png", "captured": true, "note": "" },
                { "id": "dialog.Generic", "kind": "dialog", "png": "dialog.Generic.png", "captured": true, "note": "" }
              ]
            }
            """);

        WritePng(Path.Combine(wpfManifestDirectory, "dialog.ScenarioManager.png"), width: 3, height: 2, nonBlank: true);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.ScenarioManager.png"), width: 5, height: 4, nonBlank: true);
        WritePng(Path.Combine(wpfManifestDirectory, "dialog.GoalSeekStatus.png"), width: 3, height: 2, nonBlank: true);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.GoalSeekStatus.png"), width: 3, height: 4, nonBlank: true);
        WritePng(Path.Combine(wpfManifestDirectory, "dialog.FindReplace.png"), width: 4, height: 4, nonBlank: true);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.FindReplace.png"), width: 4, height: 5, nonBlank: true);
        WritePng(Path.Combine(wpfManifestDirectory, "dialog.Generic.png"), width: 3, height: 2, nonBlank: true);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.Generic.png"), width: 5, height: 2, nonBlank: true);

        var result = PowerShellScriptRunner.RunToolScript(
            "Generate-DialogVisualEvidenceSummary.ps1",
            WorkspaceFileLocator.FindWorkspaceRoot(),
            $"-MarkdownPath \"{markdownPath}\" -JsonPath \"{jsonPath}\" -InventoryPath \"{inventoryPath}\" -WpfManifestPath \"{wpfManifestPath}\" -AvaloniaManifestPath \"{avaloniaManifestPath}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.Output.Should().Contain("Dimension mismatch bucket 'content/visual mismatch': 1");
        result.Output.Should().Contain("Dimension mismatch bucket 'evidence limitation': 1");
        result.Output.Should().Contain("Dimension mismatch bucket 'expected platform/native difference': 1");
        result.Output.Should().Contain("Dimension mismatch bucket 'real logical-size mismatch': 1");

        var markdown = File.ReadAllText(markdownPath);
        markdown.Should().Contain("| content/visual mismatch | 1 | dialog.ScenarioManager |");
        markdown.Should().Contain("| evidence limitation | 1 | dialog.GoalSeekStatus |");
        markdown.Should().Contain("| expected platform/native difference | 1 | dialog.FindReplace |");
        markdown.Should().Contain("| real logical-size mismatch | 1 | dialog.Generic |");
        markdown.Should().Contain("| dialog.ScenarioManager | content/visual mismatch |");
        markdown.Should().Contain("| dialog.GoalSeekStatus | evidence limitation |");

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var buckets = json.RootElement.GetProperty("summary").GetProperty("dimensionMismatchBuckets");
        buckets.GetProperty("content/visual mismatch").GetInt32().Should().Be(1);
        buckets.GetProperty("evidence limitation").GetInt32().Should().Be(1);
        buckets.GetProperty("expected platform/native difference").GetInt32().Should().Be(1);
        buckets.GetProperty("real logical-size mismatch").GetInt32().Should().Be(1);

        json.RootElement.GetProperty("dimensionMismatchClassification").GetArrayLength().Should().Be(4);
        json.RootElement.GetProperty("dimensionMismatchDetails").GetArrayLength().Should().Be(4);
    }

    [Fact]
    public void DialogVisualEvidenceSummary_NormalizesCaptureDpiBeforeCountingDimensionMismatches()
    {
        using var temp = new TestTemporaryDirectory();

        var inventoryPath = Path.Combine(temp.Path, "dialog-parity-inventory.json");
        var wpfManifestDirectory = Path.Combine(temp.Path, "wpf-capture");
        var avaloniaManifestDirectory = Path.Combine(temp.Path, "avalonia-capture");
        Directory.CreateDirectory(wpfManifestDirectory);
        Directory.CreateDirectory(avaloniaManifestDirectory);

        var wpfManifestPath = Path.Combine(wpfManifestDirectory, "manifest.json");
        var avaloniaManifestPath = Path.Combine(avaloniaManifestDirectory, "manifest.json");
        var markdownPath = Path.Combine(temp.Path, "summary.md");
        var jsonPath = Path.Combine(temp.Path, "summary.json");

        File.WriteAllText(
            inventoryPath,
            """
            {
              "summary": {
                "totalRoutes": 1,
                "wpfCaptures": 1,
                "avaloniaCaptures": 1,
                "avaloniaHarnessRoutes": 1,
                "sharedOrPresentationBacked": 1
              },
              "rows": [
                { "routeId": "dialog.Scaled" }
              ]
            }
            """);

        File.WriteAllText(
            wpfManifestPath,
            """
            {
              "platform": "windows",
              "shell": "wpf",
              "surfaces": [
                {
                  "id": "dialog.Scaled",
                  "kind": "dialog",
                  "png": "dialog.Scaled.png",
                  "captured": true,
                  "note": ""
                }
              ]
            }
            """);

        File.WriteAllText(
            avaloniaManifestPath,
            """
            {
              "platform": "windows",
              "shell": "avalonia",
              "surfaces": [
                {
                  "id": "dialog.Scaled",
                  "kind": "dialog",
                  "png": "dialog.Scaled.png",
                  "captured": true,
                  "note": ""
                }
              ]
            }
            """);

        WritePng(Path.Combine(wpfManifestDirectory, "dialog.Scaled.png"), width: 6, height: 4, nonBlank: true, dpiX: 192, dpiY: 192);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.Scaled.png"), width: 3, height: 2, nonBlank: true);

        var result = PowerShellScriptRunner.RunToolScript(
            "Generate-DialogVisualEvidenceSummary.ps1",
            WorkspaceFileLocator.FindWorkspaceRoot(),
            $"-MarkdownPath \"{markdownPath}\" -JsonPath \"{jsonPath}\" -InventoryPath \"{inventoryPath}\" -WpfManifestPath \"{wpfManifestPath}\" -AvaloniaManifestPath \"{avaloniaManifestPath}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.Output.Should().Contain("Paired dimension mismatches: 0");
        result.Output.Should().Contain("Raw PNG pixel dimension mismatches: 1");
        result.Output.Should().Contain("Raw PNG mismatches normalized by capture DPI: 1");

        var markdown = File.ReadAllText(markdownPath);
        markdown.Should().Contain("| Paired dimension mismatches (scale-aware logical units) | 0 |");
        markdown.Should().Contain("| Raw PNG pixel dimension mismatches | 1 |");
        markdown.Should().Contain("| Raw PNG mismatches normalized by capture DPI | 1 |");
        markdown.Should().Contain("| dialog.Scaled | dialog.Scaled.png | 3x2 | 6x4 px @ ");
        markdown.Should().Contain("DPI | True | dialog.Scaled.png | 3x2 | 3x2 px @ 96 DPI | True | True |");

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var summary = json.RootElement.GetProperty("summary");
        summary.GetProperty("pairedDimensionMismatches").GetInt32().Should().Be(0);
        summary.GetProperty("pairedRawPixelDimensionMismatches").GetInt32().Should().Be(1);
        summary.GetProperty("pairedCaptureScaleNormalizedDimensionMatches").GetInt32().Should().Be(1);

        var pairedSurface = json.RootElement.GetProperty("pairedSurfaces")[0];
        pairedSurface.GetProperty("wpf").GetProperty("width").GetInt32().Should().Be(6);
        pairedSurface.GetProperty("wpf").GetProperty("logicalWidth").GetDouble().Should().BeApproximately(3, 0.001);
        pairedSurface.GetProperty("avalonia").GetProperty("logicalHeight").GetDouble().Should().BeApproximately(2, 0.001);

        var comparison = pairedSurface.GetProperty("comparison");
        comparison.GetProperty("dimensionMatch").GetBoolean().Should().BeTrue();
        comparison.GetProperty("logicalDimensionMatch").GetBoolean().Should().BeTrue();
        comparison.GetProperty("rawPixelDimensionMatch").GetBoolean().Should().BeFalse();
        comparison.GetProperty("captureScaleNormalizedDimensionMatch").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void DialogVisualEvidenceSummary_FlagsWorkbookFileDialogExpectedSizeEvidenceMismatch()
    {
        using var temp = new TestTemporaryDirectory();

        var inventoryPath = Path.Combine(temp.Path, "dialog-parity-inventory.json");
        var wpfManifestDirectory = Path.Combine(temp.Path, "wpf-capture");
        var avaloniaManifestDirectory = Path.Combine(temp.Path, "avalonia-capture");
        Directory.CreateDirectory(wpfManifestDirectory);
        Directory.CreateDirectory(avaloniaManifestDirectory);

        var wpfManifestPath = Path.Combine(wpfManifestDirectory, "manifest.json");
        var avaloniaManifestPath = Path.Combine(avaloniaManifestDirectory, "manifest.json");
        var markdownPath = Path.Combine(temp.Path, "summary.md");
        var jsonPath = Path.Combine(temp.Path, "summary.json");

        File.WriteAllText(
            inventoryPath,
            """
            {
              "summary": {
                "totalRoutes": 1,
                "wpfCaptures": 1,
                "avaloniaCaptures": 1,
                "avaloniaHarnessRoutes": 1,
                "sharedOrPresentationBacked": 1
              },
              "rows": [
                { "routeId": "dialog.OpenWorkbook" }
              ]
            }
            """);

        File.WriteAllText(
            wpfManifestPath,
            """
            {
              "platform": "windows",
              "shell": "wpf",
              "surfaces": [
                {
                  "id": "dialog.OpenWorkbook",
                  "kind": "dialog",
                  "png": "dialog.OpenWorkbook.png",
                  "captured": true,
                  "evidenceSource": "promoted-foreground-tour",
                  "sourcePng": "screenshots\\open-workbook-dialog-tour\\freex_open_workbook_dialog_opened.png",
                  "recaptureStatus": "blocked-transparent-direct-parity-capture",
                  "expectedWidth": 640,
                  "expectedHeight": 420,
                  "note": ""
                }
              ]
            }
            """);

        File.WriteAllText(
            avaloniaManifestPath,
            """
            {
              "platform": "windows",
              "shell": "avalonia",
              "surfaces": [
                {
                  "id": "dialog.OpenWorkbook",
                  "kind": "dialog",
                  "png": "dialog.OpenWorkbook.png",
                  "captured": true,
                  "note": ""
                }
              ]
            }
            """);

        WritePng(Path.Combine(wpfManifestDirectory, "dialog.OpenWorkbook.png"), width: 1280, height: 800, nonBlank: true);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.OpenWorkbook.png"), width: 640, height: 420, nonBlank: true);

        var result = PowerShellScriptRunner.RunToolScript(
            "Generate-DialogVisualEvidenceSummary.ps1",
            WorkspaceFileLocator.FindWorkspaceRoot(),
            $"-MarkdownPath \"{markdownPath}\" -JsonPath \"{jsonPath}\" -InventoryPath \"{inventoryPath}\" -WpfManifestPath \"{wpfManifestPath}\" -AvaloniaManifestPath \"{avaloniaManifestPath}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.Output.Should().Contain("Paired expected-size evidence mismatches: 1");
        result.Output.Should().Contain("Stale promoted expected-size evidence: 1");

        var markdown = File.ReadAllText(markdownPath);
        markdown.Should().Contain("| Paired expected-size evidence mismatches | 1 |");
        markdown.Should().Contain("| Stale promoted expected-size evidence | 1 |");
        markdown.Should().Contain("## Expected-Size Evidence Mismatches");
        markdown.Should().Contain("| dialog.OpenWorkbook | 640x420 | WorkbookFileDialogSurfacePlanner.Width/Height | 1280x800 | 1280x800 px @ 96 DPI | False | 640x420 | 640x420 px @ 96 DPI | True |");
        markdown.Should().Contain("## Stale Promoted Expected-Size Evidence");
        markdown.Should().Contain("| dialog.OpenWorkbook | WPF | 1280x800 logical (1280x800 px @ 96 DPI) | 640x420 | screenshots\\open-workbook-dialog-tour\\freex_open_workbook_dialog_opened.png | blocked-transparent-direct-parity-capture |");

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var summary = json.RootElement.GetProperty("summary");
        summary.GetProperty("pairedExpectedSizeMismatches").GetInt32().Should().Be(1);
        summary.GetProperty("stalePromotedExpectedSizeEvidence").GetInt32().Should().Be(1);

        var pairedSurface = json.RootElement.GetProperty("pairedSurfaces")[0];
        var wpf = pairedSurface.GetProperty("wpf");
        wpf.GetProperty("evidenceSource").GetString().Should().Be("promoted-foreground-tour");
        wpf.GetProperty("sourcePng").GetString().Should().Be(@"screenshots\open-workbook-dialog-tour\freex_open_workbook_dialog_opened.png");
        wpf.GetProperty("recaptureStatus").GetString().Should().Be("blocked-transparent-direct-parity-capture");
        wpf.GetProperty("expectedWidth").GetInt32().Should().Be(640);
        wpf.GetProperty("expectedHeight").GetInt32().Should().Be(420);

        var comparison = pairedSurface.GetProperty("comparison");
        comparison.GetProperty("expectedSizeMismatch").GetBoolean().Should().BeTrue();
        comparison.GetProperty("expectedWidth").GetInt32().Should().Be(640);
        comparison.GetProperty("expectedHeight").GetInt32().Should().Be(420);
        comparison.GetProperty("wpfExpectedSizeMatch").GetBoolean().Should().BeFalse();
        comparison.GetProperty("avaloniaExpectedSizeMatch").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void DialogVisualEvidenceSummary_FlagsPlannerSizeEvidenceMismatchOnlyOnStaleSide()
    {
        using var temp = new TestTemporaryDirectory();

        var inventoryPath = Path.Combine(temp.Path, "dialog-parity-inventory.json");
        var wpfManifestDirectory = Path.Combine(temp.Path, "wpf-capture");
        var avaloniaManifestDirectory = Path.Combine(temp.Path, "avalonia-capture");
        Directory.CreateDirectory(wpfManifestDirectory);
        Directory.CreateDirectory(avaloniaManifestDirectory);

        var wpfManifestPath = Path.Combine(wpfManifestDirectory, "manifest.json");
        var avaloniaManifestPath = Path.Combine(avaloniaManifestDirectory, "manifest.json");
        var markdownPath = Path.Combine(temp.Path, "summary.md");
        var jsonPath = Path.Combine(temp.Path, "summary.json");

        File.WriteAllText(
            inventoryPath,
            """
            {
              "summary": {
                "totalRoutes": 1,
                "wpfCaptures": 1,
                "avaloniaCaptures": 1,
                "avaloniaHarnessRoutes": 1,
                "sharedOrPresentationBacked": 1
              },
              "rows": [
                { "routeId": "dialog.InsertHyperlink" }
              ]
            }
            """);

        File.WriteAllText(
            wpfManifestPath,
            """
            {
              "platform": "windows",
              "shell": "wpf",
              "surfaces": [
                {
                  "id": "dialog.InsertHyperlink",
                  "kind": "dialog",
                  "png": "dialog.InsertHyperlink.png",
                  "captured": true,
                  "evidenceSource": "promoted-foreground-tour",
                  "note": ""
                }
              ]
            }
            """);

        File.WriteAllText(
            avaloniaManifestPath,
            """
            {
              "platform": "windows",
              "shell": "avalonia",
              "surfaces": [
                {
                  "id": "dialog.InsertHyperlink",
                  "kind": "dialog",
                  "png": "dialog.InsertHyperlink.png",
                  "captured": true,
                  "note": ""
                }
              ]
            }
            """);

        WritePng(Path.Combine(wpfManifestDirectory, "dialog.InsertHyperlink.png"), width: 560, height: 300, nonBlank: true);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.InsertHyperlink.png"), width: 560, height: 360, nonBlank: true);

        var result = PowerShellScriptRunner.RunToolScript(
            "Generate-DialogVisualEvidenceSummary.ps1",
            WorkspaceFileLocator.FindWorkspaceRoot(),
            $"-MarkdownPath \"{markdownPath}\" -JsonPath \"{jsonPath}\" -InventoryPath \"{inventoryPath}\" -WpfManifestPath \"{wpfManifestPath}\" -AvaloniaManifestPath \"{avaloniaManifestPath}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.Output.Should().Contain("Paired expected-size evidence mismatches: 1");
        result.Output.Should().Contain("Stale promoted expected-size evidence: 0");

        var markdown = File.ReadAllText(markdownPath);
        markdown.Should().Contain("| dialog.InsertHyperlink | 560x300 | HyperlinkDialogPlanner.Width/Height | 560x300 | 560x300 px @ 96 DPI | True | 560x360 | 560x360 px @ 96 DPI | False |");
        markdown.Should().NotContain("## Stale Promoted Expected-Size Evidence");

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var summary = json.RootElement.GetProperty("summary");
        summary.GetProperty("pairedExpectedSizeMismatches").GetInt32().Should().Be(1);
        summary.GetProperty("stalePromotedExpectedSizeEvidence").GetInt32().Should().Be(0);

        var comparison = json.RootElement.GetProperty("pairedSurfaces")[0].GetProperty("comparison");
        comparison.GetProperty("dimensionMismatchBucket").GetString().Should().Be("evidence limitation");
        comparison.GetProperty("expectedSizeSource").GetString().Should().Be("HyperlinkDialogPlanner.Width/Height");
        comparison.GetProperty("wpfExpectedSizeMatch").GetBoolean().Should().BeTrue();
        comparison.GetProperty("avaloniaExpectedSizeMatch").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void DialogVisualEvidenceSummary_FlagsShapeGradientPromotedEvidenceAgainstSharedPlannerSize()
    {
        using var temp = new TestTemporaryDirectory();

        var inventoryPath = Path.Combine(temp.Path, "dialog-parity-inventory.json");
        var wpfManifestDirectory = Path.Combine(temp.Path, "wpf-capture");
        var avaloniaManifestDirectory = Path.Combine(temp.Path, "avalonia-capture");
        Directory.CreateDirectory(wpfManifestDirectory);
        Directory.CreateDirectory(avaloniaManifestDirectory);

        var wpfManifestPath = Path.Combine(wpfManifestDirectory, "manifest.json");
        var avaloniaManifestPath = Path.Combine(avaloniaManifestDirectory, "manifest.json");
        var markdownPath = Path.Combine(temp.Path, "summary.md");
        var jsonPath = Path.Combine(temp.Path, "summary.json");

        File.WriteAllText(
            inventoryPath,
            """
            {
              "summary": {
                "totalRoutes": 1,
                "wpfCaptures": 1,
                "avaloniaCaptures": 1,
                "avaloniaHarnessRoutes": 1,
                "sharedOrPresentationBacked": 1
              },
              "rows": [
                { "routeId": "dialog.ShapeGradient" }
              ]
            }
            """);

        File.WriteAllText(
            wpfManifestPath,
            """
            {
              "platform": "windows",
              "shell": "wpf",
              "surfaces": [
                {
                  "id": "dialog.ShapeGradient",
                  "kind": "dialog",
                  "png": "dialog.ShapeGradient.png",
                  "captured": true,
                  "note": "Promoted from draw-object-formatting-tour committed WPF screenshot evidence (screenshots\\draw-object-formatting-tour\\freex_draw_object_formatting_shape_gradient_dialog.png) after direct FreeX.App.Host --parity-capture emitted a transparent dialog PNG"
                }
              ]
            }
            """);

        File.WriteAllText(
            avaloniaManifestPath,
            """
            {
              "platform": "windows",
              "shell": "avalonia",
              "surfaces": [
                {
                  "id": "dialog.ShapeGradient",
                  "kind": "dialog",
                  "png": "dialog.ShapeGradient.png",
                  "captured": true,
                  "note": ""
                }
              ]
            }
            """);

        WritePng(Path.Combine(wpfManifestDirectory, "dialog.ShapeGradient.png"), width: 420, height: 280, nonBlank: true);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.ShapeGradient.png"), width: 500, height: 300, nonBlank: true);

        var result = PowerShellScriptRunner.RunToolScript(
            "Generate-DialogVisualEvidenceSummary.ps1",
            WorkspaceFileLocator.FindWorkspaceRoot(),
            $"-MarkdownPath \"{markdownPath}\" -JsonPath \"{jsonPath}\" -InventoryPath \"{inventoryPath}\" -WpfManifestPath \"{wpfManifestPath}\" -AvaloniaManifestPath \"{avaloniaManifestPath}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.Output.Should().Contain("Paired expected-size evidence mismatches: 1");
        result.Output.Should().Contain("Stale promoted expected-size evidence: 1");
        result.Output.Should().Contain("Dimension mismatch bucket 'evidence limitation': 1");

        var markdown = File.ReadAllText(markdownPath);
        markdown.Should().Contain("| dialog.ShapeGradient | 500x300 | ShapeGradientPlanner.DialogWidth/DialogHeight | 420x280 | 420x280 px @ 96 DPI | False | 500x300 | 500x300 px @ 96 DPI | True |");
        markdown.Should().Contain("| dialog.ShapeGradient | WPF | 420x280 logical (420x280 px @ 96 DPI) | 500x300 | screenshots\\draw-object-formatting-tour\\freex_draw_object_formatting_shape_gradient_dialog.png | blocked-transparent-direct-parity-capture |");

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var summary = json.RootElement.GetProperty("summary");
        summary.GetProperty("pairedExpectedSizeMismatches").GetInt32().Should().Be(1);
        summary.GetProperty("stalePromotedExpectedSizeEvidence").GetInt32().Should().Be(1);
        summary.GetProperty("dimensionMismatchBuckets").GetProperty("evidence limitation").GetInt32().Should().Be(1);

        var comparison = json.RootElement.GetProperty("pairedSurfaces")[0].GetProperty("comparison");
        comparison.GetProperty("dimensionMismatchBucket").GetString().Should().Be("evidence limitation");
        comparison.GetProperty("expectedSizeSource").GetString().Should().Be("ShapeGradientPlanner.DialogWidth/DialogHeight");
        comparison.GetProperty("wpfExpectedSizeMatch").GetBoolean().Should().BeFalse();
        comparison.GetProperty("avaloniaExpectedSizeMatch").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void DialogVisualEvidenceSummary_FlagsBlankPngEvidence()
    {
        using var temp = new TestTemporaryDirectory();

        var inventoryPath = Path.Combine(temp.Path, "dialog-parity-inventory.json");
        var wpfManifestDirectory = Path.Combine(temp.Path, "wpf-capture");
        var avaloniaManifestDirectory = Path.Combine(temp.Path, "avalonia-capture");
        Directory.CreateDirectory(wpfManifestDirectory);
        Directory.CreateDirectory(avaloniaManifestDirectory);

        var wpfManifestPath = Path.Combine(wpfManifestDirectory, "manifest.json");
        var avaloniaManifestPath = Path.Combine(avaloniaManifestDirectory, "manifest.json");
        var markdownPath = Path.Combine(temp.Path, "summary.md");
        var jsonPath = Path.Combine(temp.Path, "summary.json");

        File.WriteAllText(
            inventoryPath,
            """
            {
              "summary": {
                "totalRoutes": 1,
                "wpfCaptures": 1,
                "avaloniaCaptures": 1,
                "avaloniaHarnessRoutes": 1,
                "sharedOrPresentationBacked": 1
              },
              "rows": [
                { "routeId": "dialog.Blank" }
              ]
            }
            """);

        File.WriteAllText(
            wpfManifestPath,
            """
            {
              "platform": "windows",
              "shell": "wpf",
              "surfaces": [
                {
                  "id": "dialog.Blank",
                  "kind": "dialog",
                  "png": "dialog.Blank.png",
                  "captured": true,
                  "note": ""
                }
              ]
            }
            """);

        File.WriteAllText(
            avaloniaManifestPath,
            """
            {
              "platform": "windows",
              "shell": "avalonia",
              "surfaces": [
                {
                  "id": "dialog.Blank",
                  "kind": "dialog",
                  "png": "dialog.Blank.png",
                  "captured": true,
                  "note": ""
                }
              ]
            }
            """);

        WritePng(Path.Combine(wpfManifestDirectory, "dialog.Blank.png"), width: 3, height: 3, nonBlank: false);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.Blank.png"), width: 3, height: 3, nonBlank: true);

        var result = PowerShellScriptRunner.RunToolScript(
            "Generate-DialogVisualEvidenceSummary.ps1",
            WorkspaceFileLocator.FindWorkspaceRoot(),
            $"-MarkdownPath \"{markdownPath}\" -JsonPath \"{jsonPath}\" -InventoryPath \"{inventoryPath}\" -WpfManifestPath \"{wpfManifestPath}\" -AvaloniaManifestPath \"{avaloniaManifestPath}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.Output.Should().Contain("Nonblank PNG check failures: 1");

        var markdown = File.ReadAllText(markdownPath);
        markdown.Should().Contain("| Nonblank PNG check failures | 1 |");
        markdown.Should().Contain("Nonblank check failures: dialog.Blank.");

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        json.RootElement.GetProperty("summary").GetProperty("nonBlankPngFailures").GetInt32().Should().Be(1);
    }

    private static void WritePng(string path, int width, int height, bool nonBlank, double dpiX = 96, double dpiY = 96)
    {
        var stride = width * 4;
        var pixels = new byte[stride * height];
        for (var index = 0; index < pixels.Length; index += 4)
        {
            pixels[index] = 0xFF;
            pixels[index + 1] = 0xFF;
            pixels[index + 2] = 0xFF;
            pixels[index + 3] = 0xFF;
        }

        if (nonBlank && pixels.Length >= 4)
        {
            pixels[0] = 0x00;
            pixels[1] = 0x66;
            pixels[2] = 0xCC;
            pixels[3] = 0xFF;
        }

        var source = BitmapSource.Create(width, height, dpiX, dpiY, PixelFormats.Bgra32, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
