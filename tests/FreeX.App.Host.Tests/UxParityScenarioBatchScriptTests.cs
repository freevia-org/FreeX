using FluentAssertions;
using System.Text.Json;

namespace FreeX.App.Host.Tests;

public sealed class UxParityScenarioBatchScriptTests
{
    [Fact]
    public void NativeOutputSuite_BatchesGuardedFileExportAndPrintForegroundScenarios()
    {
        var source = WorkspaceFileLocator.ReadAllText("tools", "Run-UxParityScenarioBatch.ps1");

        source.Should().Contain("\"native-output\"");
        source.Should().Contain("id = \"open-dialog\"");
        source.Should().Contain("id = \"save-as-dialog\"");
        source.Should().Contain("id = \"save-as-invalid-path\"");
        source.Should().Contain("freexScenario = \"freex-save-as-invalid-path\"");
        source.Should().Contain("id = \"export-pdf-save-dialog-cancel\"");
        source.Should().Contain("freexScenario = \"freex-export-pdf-save-dialog-cancel\"");
        source.Should().Contain("id = \"export-overwrite-prompt\"");
        source.Should().Contain("freexScenario = \"freex-export-overwrite-prompt\"");
        source.Should().Contain("id = \"export-xps-accept\"");
        source.Should().Contain("freexScenario = \"freex-export-xps-accept\"");
        source.Should().Contain("id = \"native-print-dialog\"");
        source.Should().Contain("freexScenario = \"freex-native-print-dialog\"");
        source.Should().Contain("\"native-output\" { return $pairs | Where-Object { $_[\"area\"] -in @(\"Native file dialogs\", \"Native output dialogs\") } }");
    }

    [Fact]
    public void NativeOutputSuite_DeclaresEvidenceArtifactsAndAvaloniaBaselineDebt()
    {
        var source = WorkspaceFileLocator.ReadAllText("tools", "Run-UxParityScenarioBatch.ps1");

        source.Should().Contain("evidenceScope = \"excel-freex-wpf-paired-native-dialog\"");
        source.Should().Contain("evidenceScope = \"freex-wpf-native-output\"");
        source.Should().Contain("avaloniaEvidenceStatus = \"pending-avalonia-native-dialog-baseline\"");
        source.Should().Contain("avaloniaEvidenceStatus = \"pending-avalonia-native-output-baseline\"");
        source.Should().Contain("requiredArtifacts = @(\"excel-manifest\", \"excel-screenshot\", \"freex-wpf-manifest\", \"freex-wpf-screenshot\")");
        source.Should().Contain("requiredArtifacts = @(\"freex-wpf-manifest\", \"freex-wpf-screenshot\", \"native-dialog-validation\")");
        source.Should().Contain("requiredArtifacts = @(\"freex-wpf-manifest\", \"freex-wpf-screenshot\", \"native-dialog-validation\", \"native-output-file\")");
    }

    [Fact]
    public void NativeOutputSuite_LeavesOutputOnlyScenariosFreeXOnlyUntilExcelBaselinesExist()
    {
        var source = WorkspaceFileLocator.ReadAllText("tools", "Run-UxParityScenarioBatch.ps1");

        foreach (var scenarioId in new[]
        {
            "save-as-invalid-path",
            "export-pdf-save-dialog-cancel",
            "export-overwrite-prompt",
            "export-xps-accept",
            "native-print-dialog"
        })
        {
            var idIndex = source.IndexOf($"id = \"{scenarioId}\"", StringComparison.Ordinal);
            idIndex.Should().BeGreaterThanOrEqualTo(0, scenarioId);

            var nextIdIndex = source.IndexOf("id = \"", idIndex + 1, StringComparison.Ordinal);
            var block = nextIdIndex >= 0
                ? source[idIndex..nextIdIndex]
                : source[idIndex..];

            block.Should().Contain("area = \"Native output dialogs\"", scenarioId);
            block.Should().Contain("comparisonMode = \"freex-only\"", scenarioId);
            block.Should().NotContain("excelScenario", scenarioId);
        }
    }

    [Fact]
    public void NativeOutputSuite_CanListScenarioEvidenceContractWithoutLaunchingForegroundCapture()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var result = PowerShellScriptRunner.RunToolScript(
            "Run-UxParityScenarioBatch.ps1",
            repoRoot,
            "-Suite native-output -ListScenarios -RunId native-output-list-test");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.CombinedOutput.Should().NotContain("FreeX host executable was not found");
        result.CombinedOutput.Should().NotContain("Running UX parity pair");

        using var document = JsonDocument.Parse(result.Output);
        var root = document.RootElement;
        root.GetProperty("mode").GetString().Should().Be("scenario-catalog");
        root.GetProperty("suite").GetString().Should().Be("native-output");
        root.GetProperty("scenarioCount").GetInt32().Should().Be(7);
        root.GetProperty("missingEvidenceCount").GetInt32().Should().Be(7);
        root.GetProperty("missingArtifactCount").GetInt32().Should().Be(4);

        var records = root.GetProperty("records").EnumerateArray().ToArray();
        records.Should().Contain(record =>
            record.GetProperty("id").GetString() == "open-dialog" &&
            record.GetProperty("comparisonMode").GetString() == "paired" &&
            record.GetProperty("excelScenario").GetString() == "excel-open-dialog" &&
            record.GetProperty("freexWpfScenario").GetString() == "freex-open-dialog" &&
            record.GetProperty("avaloniaEvidenceStatus").GetString() == "pending-avalonia-native-dialog-baseline" &&
            record.GetProperty("nextMissingArtifact").GetString() == "avalonia-foreground-capture");

        records.Should().Contain(record =>
            record.GetProperty("id").GetString() == "export-xps-accept" &&
            record.GetProperty("comparisonMode").GetString() == "freex-only" &&
            record.GetProperty("freexWpfScenario").GetString() == "freex-export-xps-accept" &&
            record.GetProperty("requiredArtifacts").EnumerateArray().Any(artifact => artifact.GetString() == "native-output-file") &&
            record.GetProperty("nextMissingArtifact").GetString() == "freex-wpf-screenshot" &&
            record.GetProperty("missingArtifacts").EnumerateArray().Any(artifact => artifact.GetString() == "native-output-file"));

        records.Should().OnlyContain(record =>
            record.GetProperty("missingEvidence").EnumerateArray().Any(missing => missing.GetString() == "avaloniaForegroundCapture"));
    }

    [Fact]
    public void NativeOutputSuite_ListScenariosReportsMissingRetainedArtifactsBeforeTrustingCatalogRows()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var result = PowerShellScriptRunner.RunToolScript(
            "Run-UxParityScenarioBatch.ps1",
            repoRoot,
            "-Suite native-output -ListScenarios -RunId native-output-artifacts-test");

        result.ExitCode.Should().Be(0, result.CombinedOutput);

        using var document = JsonDocument.Parse(result.Output);
        var records = document.RootElement.GetProperty("records").EnumerateArray().ToArray();

        var openDialog = records.Single(record => record.GetProperty("id").GetString() == "open-dialog");
        openDialog.GetProperty("artifactStatuses").EnumerateArray()
            .Single(status => status.GetProperty("subject").GetString() == "excel")
            .GetProperty("missingArtifacts")
            .EnumerateArray()
            .Should()
            .BeEmpty("the retained Excel Open screenshot is resolved beside the manifest even when the manifest has an older absolute path");

        var saveAsDialog = records.Single(record => record.GetProperty("id").GetString() == "save-as-dialog");
        saveAsDialog.GetProperty("nextMissingArtifact").GetString().Should().Be("excel-screenshot");
        saveAsDialog.GetProperty("missingEvidence").EnumerateArray()
            .Should()
            .Contain(missing => missing.GetString() == "excelForegroundCapture");

        var xpsAccept = records.Single(record => record.GetProperty("id").GetString() == "export-xps-accept");
        xpsAccept.GetProperty("missingArtifacts").EnumerateArray()
            .Select(artifact => artifact.GetString())
            .Should()
            .Contain(new[] { "freex-wpf-screenshot", "native-output-file", "avalonia-foreground-capture" });

        var nativePrint = records.Single(record => record.GetProperty("id").GetString() == "native-print-dialog");
        nativePrint.GetProperty("nextMissingArtifact").GetString().Should().Be("freex-wpf-screenshot");
    }

    [Fact]
    public void NativeOutputSuite_CanAssertScenarioCoverageWithoutLaunchingForegroundCapture()
    {
        var repoRoot = WorkspaceFileLocator.FindWorkspaceRoot();
        var result = PowerShellScriptRunner.RunToolScript(
            "Run-UxParityScenarioBatch.ps1",
            repoRoot,
            "-Suite native-output -AssertScenarioCoverage -RunId native-output-assert-test");

        result.ExitCode.Should().Be(0, result.CombinedOutput);
        result.CombinedOutput.Should().Contain("Scenario coverage assertion passed for suite 'native-output' (7 scenario pair(s)).");
        result.CombinedOutput.Should().NotContain("FreeX host executable was not found");
        result.CombinedOutput.Should().NotContain("Running UX parity pair");
    }
}
