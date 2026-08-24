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

        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
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
        result.Output.Should().Contain("Policy-accepted native/control differences: 0");
        result.Output.Should().Contain("Dimension mismatch bucket 'real logical-size mismatch': 1");

        var markdown = File.ReadAllText(markdownPath);
        markdown.Should().Contain("| WPF captured manifest surfaces with committed PNGs | 1 |");
        markdown.Should().Contain("| Paired captured surface ids | 1 |");
        markdown.Should().Contain("| Paired dimension mismatches (scale-aware logical units) | 1 |");
        markdown.Should().Contain("| Raw PNG pixel dimension mismatches | 1 |");
        markdown.Should().Contain("| Raw PNG mismatches normalized by capture DPI | 0 |");
        markdown.Should().Contain("| Paired expected-size evidence mismatches | 0 |");
        markdown.Should().Contain("| Stale promoted expected-size evidence | 0 |");
        markdown.Should().Contain("| Policy-accepted native/control differences | 0 |");
        markdown.Should().Contain("## Scale-Aware Dimension Mismatch Classification");
        markdown.Should().Contain("| real logical-size mismatch | 1 | False | dialog.Sample.Valid |");
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
        summary.GetProperty("policyAcceptedNativeDifferences").GetInt32().Should().Be(0);
        summary.GetProperty("dimensionMismatchBuckets").GetProperty("real logical-size mismatch").GetInt32().Should().Be(1);
        summary.GetProperty("visualReviewTriageThreshold").GetDouble().Should().Be(0.4);
        summary.GetProperty("visualReviewTriageThresholdRationale").GetString().Should().Contain("not a pass/fail");
        summary.GetProperty("visualReviewCandidateCount").GetInt32().Should().Be(1);
        summary.GetProperty("highestTriageScore").GetDouble().Should().BeGreaterThan(0.4);

        var reviewCandidate = json.RootElement.GetProperty("visualReviewCandidates")[0];
        reviewCandidate.GetProperty("id").GetString().Should().Be("dialog.Sample.Valid");
        reviewCandidate.GetProperty("reviewStatus").GetString().Should().Be("unresolved visual review candidate");
        reviewCandidate.GetProperty("logicalDimensionMatch").GetBoolean().Should().BeFalse();
        reviewCandidate.GetProperty("reviewReason").GetString().Should().Contain("paired WPF/Avalonia visual review");

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
        paired.GetProperty("comparison").GetProperty("policyAcceptance").ValueKind.Should().Be(JsonValueKind.Null);

        var checkResult = PowerShellScriptRunner.RunToolScriptWithPwsh(
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
                { "routeId": "dialog.AutoFilter" },
                { "routeId": "dialog.GoalSeekStatus" },
                { "routeId": "dialog.Options" },
                { "routeId": "dialog.ScenarioManager" }
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
                { "id": "dialog.AutoFilter", "kind": "dialog", "png": "dialog.AutoFilter.png", "captured": true, "note": "" },
                { "id": "dialog.GoalSeekStatus", "kind": "dialog", "png": "dialog.GoalSeekStatus.png", "captured": true, "note": "" },
                { "id": "dialog.Options", "kind": "dialog", "png": "dialog.Options.png", "captured": true, "note": "" },
                { "id": "dialog.ScenarioManager", "kind": "dialog", "png": "dialog.ScenarioManager.png", "captured": true, "note": "" }
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
                { "id": "dialog.AutoFilter", "kind": "dialog", "png": "dialog.AutoFilter.png", "captured": true, "note": "" },
                { "id": "dialog.GoalSeekStatus", "kind": "dialog", "png": "dialog.GoalSeekStatus.png", "captured": true, "note": "" },
                { "id": "dialog.Options", "kind": "dialog", "png": "dialog.Options.png", "captured": true, "note": "" },
                { "id": "dialog.ScenarioManager", "kind": "dialog", "png": "dialog.ScenarioManager.png", "captured": true, "note": "" }
              ]
            }
            """);

        WritePng(Path.Combine(wpfManifestDirectory, "dialog.AutoFilter.png"), width: 3, height: 2, nonBlank: true);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.AutoFilter.png"), width: 5, height: 4, nonBlank: true);
        WritePng(Path.Combine(wpfManifestDirectory, "dialog.GoalSeekStatus.png"), width: 3, height: 2, nonBlank: true);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.GoalSeekStatus.png"), width: 3, height: 4, nonBlank: true);
        WritePng(Path.Combine(wpfManifestDirectory, "dialog.Options.png"), width: 4, height: 4, nonBlank: true);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.Options.png"), width: 4, height: 5, nonBlank: true);
        WritePng(Path.Combine(wpfManifestDirectory, "dialog.ScenarioManager.png"), width: 3, height: 2, nonBlank: true);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.ScenarioManager.png"), width: 5, height: 2, nonBlank: true);

        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
            "Generate-DialogVisualEvidenceSummary.ps1",
            WorkspaceFileLocator.FindWorkspaceRoot(),
            $"-MarkdownPath \"{markdownPath}\" -JsonPath \"{jsonPath}\" -InventoryPath \"{inventoryPath}\" -WpfManifestPath \"{wpfManifestPath}\" -AvaloniaManifestPath \"{avaloniaManifestPath}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.Output.Should().Contain("Dimension mismatch bucket 'content/visual mismatch': 1");
        result.Output.Should().Contain("Dimension mismatch bucket 'evidence limitation': 1");
        result.Output.Should().Contain("Dimension mismatch bucket 'expected platform/native difference': 1");
        result.Output.Should().Contain("Dimension mismatch bucket 'real logical-size mismatch': 1");
        result.Output.Should().Contain("Policy-accepted native/control differences: 1");

        var markdown = File.ReadAllText(markdownPath);
        markdown.Should().Contain("| content/visual mismatch | 1 | False | dialog.AutoFilter |");
        markdown.Should().Contain("| evidence limitation | 1 | False | dialog.GoalSeekStatus |");
        markdown.Should().Contain("| expected platform/native difference | 1 | True | dialog.Options |");
        markdown.Should().Contain("| real logical-size mismatch | 1 | False | dialog.ScenarioManager |");
        markdown.Should().Contain("## Policy-Accepted Native/Control Differences");
        markdown.Should().Contain("| Options host frame | 1 | dialog.Options |");
        markdown.Should().Contain("| dialog.AutoFilter | content/visual mismatch |");
        markdown.Should().Contain("| dialog.ScenarioManager | real logical-size mismatch |");
        markdown.Should().Contain("| dialog.GoalSeekStatus | evidence limitation |");
        markdown.Should().Contain("| dialog.Options | expected platform/native difference | Options host frame |");

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var buckets = json.RootElement.GetProperty("summary").GetProperty("dimensionMismatchBuckets");
        buckets.GetProperty("content/visual mismatch").GetInt32().Should().Be(1);
        buckets.GetProperty("evidence limitation").GetInt32().Should().Be(1);
        buckets.GetProperty("expected platform/native difference").GetInt32().Should().Be(1);
        buckets.GetProperty("real logical-size mismatch").GetInt32().Should().Be(1);
        json.RootElement.GetProperty("summary").GetProperty("policyAcceptedNativeDifferences").GetInt32().Should().Be(1);

        json.RootElement.GetProperty("dimensionMismatchClassification").GetArrayLength().Should().Be(4);
        json.RootElement.GetProperty("dimensionMismatchDetails").GetArrayLength().Should().Be(4);
        var policyFamily = json.RootElement.GetProperty("policyAcceptedNativeDifferenceFamilies")[0];
        policyFamily.GetProperty("family").GetString().Should().Be("Options host frame");
        policyFamily.GetProperty("count").GetInt32().Should().Be(1);
        var optionsComparison = json.RootElement.GetProperty("pairedSurfaces")
            .EnumerateArray()
            .Single(row => row.GetProperty("id").GetString() == "dialog.Options")
            .GetProperty("comparison");
        optionsComparison.GetProperty("policyAcceptance").GetProperty("status").GetString().Should().Be("policy-accepted");
        optionsComparison.GetProperty("policyAcceptance").GetProperty("family").GetString().Should().Be("Options host frame");
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

        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
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

        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
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

        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
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

        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
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
    public void DialogVisualEvidenceSummary_FlagsSymbolPickerPromotedEvidenceAgainstSharedDialogSize()
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
                { "routeId": "dialog.SymbolPicker" }
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
                  "id": "dialog.SymbolPicker",
                  "kind": "dialog",
                  "png": "dialog.SymbolPicker.png",
                  "captured": true,
                  "note": "Promoted from insert-objects-links-tour committed WPF screenshot evidence (screenshots\\insert-objects-links-tour\\freex_insert_symbol_picker_opened.png) after direct FreeX.App.Host --parity-capture emitted a transparent dialog PNG"
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
                  "id": "dialog.SymbolPicker",
                  "kind": "dialog",
                  "png": "dialog.SymbolPicker.png",
                  "captured": true,
                  "note": ""
                }
              ]
            }
            """);

        WritePng(Path.Combine(wpfManifestDirectory, "dialog.SymbolPicker.png"), width: 620, height: 500, nonBlank: true);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.SymbolPicker.png"), width: 840, height: 620, nonBlank: true);

        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
            "Generate-DialogVisualEvidenceSummary.ps1",
            WorkspaceFileLocator.FindWorkspaceRoot(),
            $"-MarkdownPath \"{markdownPath}\" -JsonPath \"{jsonPath}\" -InventoryPath \"{inventoryPath}\" -WpfManifestPath \"{wpfManifestPath}\" -AvaloniaManifestPath \"{avaloniaManifestPath}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.Output.Should().Contain("Paired expected-size evidence mismatches: 1");
        result.Output.Should().Contain("Stale promoted expected-size evidence: 1");
        result.Output.Should().Contain("Dimension mismatch bucket 'evidence limitation': 1");

        var markdown = File.ReadAllText(markdownPath);
        markdown.Should().Contain("| dialog.SymbolPicker | 840x620 | SymbolPickerCatalogPlanner.DialogWidth/DialogHeight | 620x500 | 620x500 px @ 96 DPI | False | 840x620 | 840x620 px @ 96 DPI | True |");
        markdown.Should().Contain("| dialog.SymbolPicker | WPF | 620x500 logical (620x500 px @ 96 DPI) | 840x620 | screenshots\\insert-objects-links-tour\\freex_insert_symbol_picker_opened.png | blocked-transparent-direct-parity-capture |");

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var summary = json.RootElement.GetProperty("summary");
        summary.GetProperty("pairedExpectedSizeMismatches").GetInt32().Should().Be(1);
        summary.GetProperty("stalePromotedExpectedSizeEvidence").GetInt32().Should().Be(1);
        summary.GetProperty("dimensionMismatchBuckets").GetProperty("evidence limitation").GetInt32().Should().Be(1);

        var comparison = json.RootElement.GetProperty("pairedSurfaces")[0].GetProperty("comparison");
        comparison.GetProperty("dimensionMismatchBucket").GetString().Should().Be("evidence limitation");
        comparison.GetProperty("expectedSizeSource").GetString().Should().Be("SymbolPickerCatalogPlanner.DialogWidth/DialogHeight");
        comparison.GetProperty("wpfExpectedSizeMatch").GetBoolean().Should().BeFalse();
        comparison.GetProperty("avaloniaExpectedSizeMatch").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void DialogVisualEvidenceSummary_FlagsSortPromotedEvidenceAgainstCurrentDialogSize()
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
                { "routeId": "dialog.Sort" }
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
                  "id": "dialog.Sort",
                  "kind": "dialog",
                  "png": "dialog.Sort.png",
                  "captured": true,
                  "note": "Promoted from data-sort-filter-outline-tour committed WPF screenshot evidence (screenshots\\data-sort-filter-outline-tour\\freex_data_sort_filter_outline_sort_dialog.png) after direct FreeX.App.Host --parity-capture emitted a transparent dialog PNG"
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
                  "id": "dialog.Sort",
                  "kind": "dialog",
                  "png": "dialog.Sort.png",
                  "captured": true,
                  "note": ""
                }
              ]
            }
            """);

        WritePng(Path.Combine(wpfManifestDirectory, "dialog.Sort.png"), width: 640, height: 420, nonBlank: true);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.Sort.png"), width: 760, height: 500, nonBlank: true);

        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
            "Generate-DialogVisualEvidenceSummary.ps1",
            WorkspaceFileLocator.FindWorkspaceRoot(),
            $"-MarkdownPath \"{markdownPath}\" -JsonPath \"{jsonPath}\" -InventoryPath \"{inventoryPath}\" -WpfManifestPath \"{wpfManifestPath}\" -AvaloniaManifestPath \"{avaloniaManifestPath}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.Output.Should().Contain("Paired expected-size evidence mismatches: 1");
        result.Output.Should().Contain("Stale promoted expected-size evidence: 1");
        result.Output.Should().Contain("Dimension mismatch bucket 'evidence limitation': 1");

        var markdown = File.ReadAllText(markdownPath);
        markdown.Should().Contain("| dialog.Sort | 760x500 | SortDialog.DialogDefaultWidth/DialogDefaultHeight | 640x420 | 640x420 px @ 96 DPI | False | 760x500 | 760x500 px @ 96 DPI | True |");
        markdown.Should().Contain("| dialog.Sort | WPF | 640x420 logical (640x420 px @ 96 DPI) | 760x500 | screenshots\\data-sort-filter-outline-tour\\freex_data_sort_filter_outline_sort_dialog.png | blocked-transparent-direct-parity-capture |");

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var summary = json.RootElement.GetProperty("summary");
        summary.GetProperty("pairedExpectedSizeMismatches").GetInt32().Should().Be(1);
        summary.GetProperty("stalePromotedExpectedSizeEvidence").GetInt32().Should().Be(1);
        summary.GetProperty("dimensionMismatchBuckets").GetProperty("evidence limitation").GetInt32().Should().Be(1);

        var comparison = json.RootElement.GetProperty("pairedSurfaces")[0].GetProperty("comparison");
        comparison.GetProperty("dimensionMismatchBucket").GetString().Should().Be("evidence limitation");
        comparison.GetProperty("expectedSizeSource").GetString().Should().Be("SortDialog.DialogDefaultWidth/DialogDefaultHeight");
        comparison.GetProperty("wpfExpectedSizeMatch").GetBoolean().Should().BeFalse();
        comparison.GetProperty("avaloniaExpectedSizeMatch").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public void DialogVisualEvidenceSummary_FlagsConditionalFormatNewRuleAgainstSharedRuleEditorSize()
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
                { "routeId": "dialog.ConditionalFormatNewRule" }
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
                  "id": "dialog.ConditionalFormatNewRule",
                  "kind": "dialog",
                  "png": "dialog.ConditionalFormatNewRule.png",
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
                  "id": "dialog.ConditionalFormatNewRule",
                  "kind": "dialog",
                  "png": "dialog.ConditionalFormatNewRule.png",
                  "captured": true,
                  "note": ""
                }
              ]
            }
            """);

        WritePng(Path.Combine(wpfManifestDirectory, "dialog.ConditionalFormatNewRule.png"), width: 634, height: 334, nonBlank: true);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.ConditionalFormatNewRule.png"), width: 640, height: 380, nonBlank: true);

        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
            "Generate-DialogVisualEvidenceSummary.ps1",
            WorkspaceFileLocator.FindWorkspaceRoot(),
            $"-MarkdownPath \"{markdownPath}\" -JsonPath \"{jsonPath}\" -InventoryPath \"{inventoryPath}\" -WpfManifestPath \"{wpfManifestPath}\" -AvaloniaManifestPath \"{avaloniaManifestPath}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.Output.Should().Contain("Paired expected-size evidence mismatches: 1");
        result.Output.Should().Contain("Dimension mismatch bucket 'evidence limitation': 1");

        var markdown = File.ReadAllText(markdownPath);
        markdown.Should().Contain("| dialog.ConditionalFormatNewRule | 634x334 | ConditionalFormatDialogCatalog.RuleEditorCaptureWidth/Height | 634x334 | 634x334 px @ 96 DPI | True | 640x380 | 640x380 px @ 96 DPI | False |");

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var comparison = json.RootElement.GetProperty("pairedSurfaces")[0].GetProperty("comparison");
        comparison.GetProperty("dimensionMismatchBucket").GetString().Should().Be("evidence limitation");
        comparison.GetProperty("expectedSizeSource").GetString().Should().Be("ConditionalFormatDialogCatalog.RuleEditorCaptureWidth/Height");
        comparison.GetProperty("wpfExpectedSizeMatch").GetBoolean().Should().BeTrue();
        comparison.GetProperty("avaloniaExpectedSizeMatch").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void DialogVisualEvidenceSummary_FlagsConsolidateAgainstSharedDialogSize()
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
                { "routeId": "dialog.Consolidate" }
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
                  "id": "dialog.Consolidate",
                  "kind": "dialog",
                  "png": "dialog.Consolidate.png",
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
                  "id": "dialog.Consolidate",
                  "kind": "dialog",
                  "png": "dialog.Consolidate.png",
                  "captured": true,
                  "note": ""
                }
              ]
            }
            """);

        WritePng(Path.Combine(wpfManifestDirectory, "dialog.Consolidate.png"), width: 380, height: 420, nonBlank: true);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.Consolidate.png"), width: 420, height: 450, nonBlank: true);

        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
            "Generate-DialogVisualEvidenceSummary.ps1",
            WorkspaceFileLocator.FindWorkspaceRoot(),
            $"-MarkdownPath \"{markdownPath}\" -JsonPath \"{jsonPath}\" -InventoryPath \"{inventoryPath}\" -WpfManifestPath \"{wpfManifestPath}\" -AvaloniaManifestPath \"{avaloniaManifestPath}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.Output.Should().Contain("Paired expected-size evidence mismatches: 1");
        result.Output.Should().Contain("Dimension mismatch bucket 'evidence limitation': 1");

        var markdown = File.ReadAllText(markdownPath);
        markdown.Should().Contain("| dialog.Consolidate | 380x420 | ConsolidateDialogPlanner.CaptureWidth/Height | 380x420 | 380x420 px @ 96 DPI | True | 420x450 | 420x450 px @ 96 DPI | False |");

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var comparison = json.RootElement.GetProperty("pairedSurfaces")[0].GetProperty("comparison");
        comparison.GetProperty("dimensionMismatchBucket").GetString().Should().Be("evidence limitation");
        comparison.GetProperty("expectedSizeSource").GetString().Should().Be("ConsolidateDialogPlanner.CaptureWidth/Height");
        comparison.GetProperty("wpfExpectedSizeMatch").GetBoolean().Should().BeTrue();
        comparison.GetProperty("avaloniaExpectedSizeMatch").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void DialogVisualEvidenceSummary_FlagsPivotTableOptionsEvidenceAgainstSharedDialogSize()
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
                { "routeId": "dialog.PivotTableOptions" }
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
                  "id": "dialog.PivotTableOptions",
                  "kind": "dialog",
                  "png": "dialog.PivotTableOptions.png",
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
                  "id": "dialog.PivotTableOptions",
                  "kind": "dialog",
                  "png": "dialog.PivotTableOptions.png",
                  "captured": true,
                  "note": ""
                }
              ]
            }
            """);

        WritePng(Path.Combine(wpfManifestDirectory, "dialog.PivotTableOptions.png"), width: 520, height: 676, nonBlank: true);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.PivotTableOptions.png"), width: 520, height: 610, nonBlank: true);

        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
            "Generate-DialogVisualEvidenceSummary.ps1",
            WorkspaceFileLocator.FindWorkspaceRoot(),
            $"-MarkdownPath \"{markdownPath}\" -JsonPath \"{jsonPath}\" -InventoryPath \"{inventoryPath}\" -WpfManifestPath \"{wpfManifestPath}\" -AvaloniaManifestPath \"{avaloniaManifestPath}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.Output.Should().Contain("Paired expected-size evidence mismatches: 1");
        result.Output.Should().Contain("Dimension mismatch bucket 'evidence limitation': 1");

        var markdown = File.ReadAllText(markdownPath);
        markdown.Should().Contain("| dialog.PivotTableOptions | 520x676 | PivotOptionsPlanner.DialogWidth/LayoutAndFormatCaptureHeight | 520x676 | 520x676 px @ 96 DPI | True | 520x610 | 520x610 px @ 96 DPI | False |");

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var comparison = json.RootElement.GetProperty("pairedSurfaces")[0].GetProperty("comparison");
        comparison.GetProperty("dimensionMismatchBucket").GetString().Should().Be("evidence limitation");
        comparison.GetProperty("expectedSizeSource").GetString().Should().Be("PivotOptionsPlanner.DialogWidth/LayoutAndFormatCaptureHeight");
        comparison.GetProperty("wpfExpectedSizeMatch").GetBoolean().Should().BeTrue();
        comparison.GetProperty("avaloniaExpectedSizeMatch").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public void DialogVisualEvidenceSummary_TreatsResolvedPriorityRowsAsExpectedSizeMatches()
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
                "totalRoutes": 10,
                "wpfCaptures": 10,
                "avaloniaCaptures": 10,
                "avaloniaHarnessRoutes": 10,
                "sharedOrPresentationBacked": 10
              },
              "rows": [
                { "routeId": "dialog.FindReplace" },
                { "routeId": "dialog.FindReplace.Find" },
                { "routeId": "dialog.FindReplace.Replace" },
                { "routeId": "dialog.ConditionalFormatNewRule" },
                { "routeId": "dialog.Consolidate" },
                { "routeId": "dialog.ExportOptions" },
                { "routeId": "dialog.ProtectWorkbook" },
                { "routeId": "dialog.Sparkline" },
                { "routeId": "dialog.PivotTableOptions" },
                { "routeId": "dialog.PivotTableOptions.LayoutAndFormat" }
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
                  "id": "dialog.FindReplace",
                  "kind": "dialog",
                  "png": "dialog.FindReplace.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.FindReplace.Find",
                  "kind": "dialog",
                  "png": "dialog.FindReplace.Find.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.FindReplace.Replace",
                  "kind": "dialog",
                  "png": "dialog.FindReplace.Replace.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.ConditionalFormatNewRule",
                  "kind": "dialog",
                  "png": "dialog.ConditionalFormatNewRule.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.Consolidate",
                  "kind": "dialog",
                  "png": "dialog.Consolidate.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.ExportOptions",
                  "kind": "dialog",
                  "png": "dialog.ExportOptions.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.ProtectWorkbook",
                  "kind": "dialog",
                  "png": "dialog.ProtectWorkbook.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.Sparkline",
                  "kind": "dialog",
                  "png": "dialog.Sparkline.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.PivotTableOptions",
                  "kind": "dialog",
                  "png": "dialog.PivotTableOptions.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.PivotTableOptions.LayoutAndFormat",
                  "kind": "dialog",
                  "png": "dialog.PivotTableOptions.LayoutAndFormat.png",
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
                  "id": "dialog.FindReplace",
                  "kind": "dialog",
                  "png": "dialog.FindReplace.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.FindReplace.Find",
                  "kind": "dialog",
                  "png": "dialog.FindReplace.Find.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.FindReplace.Replace",
                  "kind": "dialog",
                  "png": "dialog.FindReplace.Replace.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.ConditionalFormatNewRule",
                  "kind": "dialog",
                  "png": "dialog.ConditionalFormatNewRule.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.Consolidate",
                  "kind": "dialog",
                  "png": "dialog.Consolidate.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.ExportOptions",
                  "kind": "dialog",
                  "png": "dialog.ExportOptions.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.ProtectWorkbook",
                  "kind": "dialog",
                  "png": "dialog.ProtectWorkbook.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.Sparkline",
                  "kind": "dialog",
                  "png": "dialog.Sparkline.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.PivotTableOptions",
                  "kind": "dialog",
                  "png": "dialog.PivotTableOptions.png",
                  "captured": true,
                  "note": ""
                },
                {
                  "id": "dialog.PivotTableOptions.LayoutAndFormat",
                  "kind": "dialog",
                  "png": "dialog.PivotTableOptions.LayoutAndFormat.png",
                  "captured": true,
                  "note": ""
                }
              ]
            }
            """);

        var expectedSizes = new Dictionary<string, (int Width, int Height, string Source)>
        {
            ["dialog.FindReplace"] = (720, 430, "FindReplaceDialogPlanner.Width/Height"),
            ["dialog.FindReplace.Find"] = (720, 430, "FindReplaceDialogPlanner.Width/Height"),
            ["dialog.FindReplace.Replace"] = (720, 430, "FindReplaceDialogPlanner.Width/Height"),
            ["dialog.ConditionalFormatNewRule"] = (634, 334, "ConditionalFormatDialogCatalog.RuleEditorCaptureWidth/Height"),
            ["dialog.Consolidate"] = (380, 420, "ConsolidateDialogPlanner.CaptureWidth/Height"),
            ["dialog.ExportOptions"] = (430, 552, "ExportOptionsDialogSurfacePlanner.CaptureWidth/CaptureHeight"),
            ["dialog.ProtectWorkbook"] = (380, 250, "ProtectionDialogPlanner.ProtectWorkbookCaptureWidth/CaptureHeight"),
            ["dialog.Sparkline"] = (380, 280, "SparklinePlanner.InsertDialogCaptureWidth/CaptureHeight"),
            ["dialog.PivotTableOptions"] = (520, 676, "PivotOptionsPlanner.DialogWidth/LayoutAndFormatCaptureHeight"),
            ["dialog.PivotTableOptions.LayoutAndFormat"] = (520, 676, "PivotOptionsPlanner.DialogWidth/LayoutAndFormatCaptureHeight"),
        };

        foreach (var (id, (width, height, _)) in expectedSizes)
        {
            WritePng(Path.Combine(wpfManifestDirectory, $"{id}.png"), width, height, nonBlank: true);
            WritePng(Path.Combine(avaloniaManifestDirectory, $"{id}.png"), width, height, nonBlank: true);
        }

        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
            "Generate-DialogVisualEvidenceSummary.ps1",
            WorkspaceFileLocator.FindWorkspaceRoot(),
            $"-MarkdownPath \"{markdownPath}\" -JsonPath \"{jsonPath}\" -InventoryPath \"{inventoryPath}\" -WpfManifestPath \"{wpfManifestPath}\" -AvaloniaManifestPath \"{avaloniaManifestPath}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.Output.Should().Contain("Paired dimension mismatches: 0");
        result.Output.Should().Contain("Paired expected-size evidence mismatches: 0");
        result.Output.Should().Contain("Stale promoted expected-size evidence: 0");

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var summary = json.RootElement.GetProperty("summary");
        summary.GetProperty("pairedDimensionMismatches").GetInt32().Should().Be(0);
        summary.GetProperty("pairedExpectedSizeMismatches").GetInt32().Should().Be(0);
        summary.GetProperty("stalePromotedExpectedSizeEvidence").GetInt32().Should().Be(0);

        var pairedSurfaces = json.RootElement.GetProperty("pairedSurfaces")
            .EnumerateArray()
            .ToDictionary(surface => surface.GetProperty("id").GetString()!);
        pairedSurfaces.Keys.Should().BeEquivalentTo(expectedSizes.Keys);

        foreach (var (id, (width, height, source)) in expectedSizes)
        {
            var comparison = pairedSurfaces[id].GetProperty("comparison");
            comparison.GetProperty("expectedWidth").GetDouble().Should().Be(width);
            comparison.GetProperty("expectedHeight").GetDouble().Should().Be(height);
            comparison.GetProperty("expectedSizeSource").GetString().Should().Be(source);
            comparison.GetProperty("logicalDimensionMatch").GetBoolean().Should().BeTrue();
            comparison.GetProperty("expectedSizeMismatch").GetBoolean().Should().BeFalse();
            comparison.GetProperty("wpfExpectedSizeMatch").GetBoolean().Should().BeTrue();
            comparison.GetProperty("avaloniaExpectedSizeMatch").GetBoolean().Should().BeTrue();
            comparison.GetProperty("dimensionMismatchBucket").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Fact]
    public void DialogVisualEvidenceSummary_FlagsWorkbookStatisticsEvidenceAgainstSharedDialogSize()
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
                { "routeId": "dialog.WorkbookStatistics" }
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
                  "id": "dialog.WorkbookStatistics",
                  "kind": "dialog",
                  "png": "dialog.WorkbookStatistics.png",
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
                  "id": "dialog.WorkbookStatistics",
                  "kind": "dialog",
                  "png": "dialog.WorkbookStatistics.png",
                  "captured": true,
                  "note": ""
                }
              ]
            }
            """);

        WritePng(Path.Combine(wpfManifestDirectory, "dialog.WorkbookStatistics.png"), width: 360, height: 260, nonBlank: true);
        WritePng(Path.Combine(avaloniaManifestDirectory, "dialog.WorkbookStatistics.png"), width: 380, height: 320, nonBlank: true);

        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
            "Generate-DialogVisualEvidenceSummary.ps1",
            WorkspaceFileLocator.FindWorkspaceRoot(),
            $"-MarkdownPath \"{markdownPath}\" -JsonPath \"{jsonPath}\" -InventoryPath \"{inventoryPath}\" -WpfManifestPath \"{wpfManifestPath}\" -AvaloniaManifestPath \"{avaloniaManifestPath}\"");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.Output.Should().Contain("Paired expected-size evidence mismatches: 1");
        result.Output.Should().Contain("Stale promoted expected-size evidence: 0");
        result.Output.Should().Contain("Dimension mismatch bucket 'evidence limitation': 1");

        var markdown = File.ReadAllText(markdownPath);
        markdown.Should().Contain("| dialog.WorkbookStatistics | 500x560 | WorkbookStatisticsDialogPlanner.Width/Height | 360x260 | 360x260 px @ 96 DPI | False | 380x320 | 380x320 px @ 96 DPI | False |");

        using var json = JsonDocument.Parse(File.ReadAllText(jsonPath));
        var summary = json.RootElement.GetProperty("summary");
        summary.GetProperty("pairedExpectedSizeMismatches").GetInt32().Should().Be(1);
        summary.GetProperty("stalePromotedExpectedSizeEvidence").GetInt32().Should().Be(0);
        summary.GetProperty("dimensionMismatchBuckets").GetProperty("evidence limitation").GetInt32().Should().Be(1);

        var comparison = json.RootElement.GetProperty("pairedSurfaces")[0].GetProperty("comparison");
        comparison.GetProperty("dimensionMismatchBucket").GetString().Should().Be("evidence limitation");
        comparison.GetProperty("expectedSizeSource").GetString().Should().Be("WorkbookStatisticsDialogPlanner.Width/Height");
        comparison.GetProperty("wpfExpectedSizeMatch").GetBoolean().Should().BeFalse();
        comparison.GetProperty("avaloniaExpectedSizeMatch").GetBoolean().Should().BeFalse();
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

        var result = PowerShellScriptRunner.RunToolScriptWithPwsh(
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
