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
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.Sample.Valid.png"), width: 4, height: 2, nonBlank: true);
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
        result.Output.Should().Contain("Paired expected-size evidence mismatches: 0");

        var markdown = File.ReadAllText(markdownPath);
        markdown.Should().Contain("| WPF captured manifest surfaces with committed PNGs | 1 |");
        markdown.Should().Contain("| Paired captured surface ids | 1 |");
        markdown.Should().Contain("| Paired dimension mismatches | 1 |");
        markdown.Should().Contain("| Paired expected-size evidence mismatches | 0 |");
        markdown.Should().Contain("| dialog.Sample.Valid | dialog.Sample.Valid.png | 3x2 | True | dialog.Sample.Valid.png | 4x2 | True |");
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
        summary.GetProperty("pairedExpectedSizeMismatches").GetInt32().Should().Be(0);

        var paired = json.RootElement.GetProperty("pairedSurfaces")[0];
        paired.GetProperty("wpf").GetProperty("width").GetInt32().Should().Be(3);
        paired.GetProperty("avalonia").GetProperty("height").GetInt32().Should().Be(2);
        paired.GetProperty("comparison").GetProperty("dimensionMatch").GetBoolean().Should().BeFalse();
        paired.GetProperty("comparison").GetProperty("expectedSizeMismatch").GetBoolean().Should().BeFalse();

        var checkResult = PowerShellScriptRunner.RunToolScript(
            "Generate-DialogVisualEvidenceSummary.ps1",
            WorkspaceFileLocator.FindWorkspaceRoot(),
            $"-MarkdownPath \"{markdownPath}\" -JsonPath \"{jsonPath}\" -InventoryPath \"{inventoryPath}\" -WpfManifestPath \"{wpfManifestPath}\" -AvaloniaManifestPath \"{avaloniaManifestPath}\" -Check");

        checkResult.ExitCode.Should().Be(0, checkResult.CombinedOutput);
        checkResult.Output.Should().Contain("Dialog visual evidence summary is up to date.");
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

        var markdown = File.ReadAllText(markdownPath);
        markdown.Should().Contain("| Paired expected-size evidence mismatches | 1 |");
        markdown.Should().Contain("## Expected-Size Evidence Mismatches");
        markdown.Should().Contain("| dialog.OpenWorkbook | 640x420 | WorkbookFileDialogSurfacePlanner.Width/Height | 1280x800 | False | 640x420 | True |");

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var summary = json.RootElement.GetProperty("summary");
        summary.GetProperty("pairedExpectedSizeMismatches").GetInt32().Should().Be(1);

        var comparison = json.RootElement.GetProperty("pairedSurfaces")[0].GetProperty("comparison");
        comparison.GetProperty("expectedSizeMismatch").GetBoolean().Should().BeTrue();
        comparison.GetProperty("expectedWidth").GetInt32().Should().Be(640);
        comparison.GetProperty("expectedHeight").GetInt32().Should().Be(420);
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

    private static void WritePng(string path, int width, int height, bool nonBlank)
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

        var source = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
