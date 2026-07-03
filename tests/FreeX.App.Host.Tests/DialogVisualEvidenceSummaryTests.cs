using System.IO;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed class DialogVisualEvidenceSummaryTests
{
    [Fact]
    public void DialogVisualEvidenceSummary_SkipsWpfCapturedRowsWhenPngDoesNotResolve()
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
                }
              ]
            }
            """);

        File.WriteAllText(Path.Combine(wpfManifestDirectory, "dialog.Sample.Valid.png"), "wpf-valid");
        File.WriteAllText(Path.Combine(avaloniaManifestDirectory, "dialog.Sample.Valid.png"), "avalonia-valid");
        File.WriteAllText(Path.Combine(avaloniaManifestDirectory, "dialog.Sample.Missing.png"), "avalonia-missing-counterpart");

        var result = PowerShellScriptRunner.RunToolScript(
            "Generate-DialogVisualEvidenceSummary.ps1",
            WorkspaceFileLocator.FindWorkspaceRoot(),
            $"-MarkdownPath \"{markdownPath}\" -InventoryPath \"{inventoryPath}\" -WpfManifestPath \"{wpfManifestPath}\" -AvaloniaManifestPath \"{avaloniaManifestPath}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.Output.Should().Contain("WPF captured manifest surfaces: 1");
        result.Output.Should().Contain("Paired captured surface ids: 1");

        var markdown = File.ReadAllText(markdownPath);
        markdown.Should().Contain("| WPF captured manifest surfaces | 1 |");
        markdown.Should().Contain("| Paired captured surface ids | 1 |");
        markdown.Should().Contain("dialog.Sample.Valid");
        markdown.Should().NotContain("dialog.Sample.Missing |");
    }
}
