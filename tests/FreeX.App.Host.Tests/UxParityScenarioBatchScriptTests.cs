using FluentAssertions;

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
}
