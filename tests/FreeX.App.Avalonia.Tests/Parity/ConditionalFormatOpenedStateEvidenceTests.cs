using System;
using System.IO;
using System.Linq;
using System.Text.Json;

using Xunit;

namespace FreeX.App.Avalonia.Tests.Parity;

public sealed class ConditionalFormatOpenedStateEvidenceTests
{
    [Fact]
    public void OpenedStateEvidence_TracksConditionalFormatClassifierRows()
    {
        using var document = LoadEvidenceDocument();
        var root = document.RootElement;
        var summary = root.GetProperty("summary");

        Assert.Equal("freex.parity.conditional-format-opened-state-evidence.v1", root.GetProperty("schema").GetString());
        Assert.Equal(34, summary.GetProperty("conditionalFormatPopupGalleryRows").GetInt32());
        Assert.Equal(38, summary.GetProperty("conditionalFormatPopupCatalogItems").GetInt32());

        var wpf = FunctionalParityMatrix.LoadWpfHandlerIds();
        var classifierRows = FunctionalParityClassifier
            .Classify(FunctionalParityMatrix.Compute(wpf))
            .Where(FunctionalParityClassifier.IsConditionalFormattingGalleryRow)
            .Select(row => row.MatrixRow.CommandId)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var evidenceRows = root.GetProperty("rows")
            .EnumerateArray()
            .Select(row => row.GetProperty("id").GetString()!)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(classifierRows, evidenceRows);
    }

    [Fact]
    public void OpenedStateEvidence_RequiresExcelWpfAndAvaloniaTargets()
    {
        using var document = LoadEvidenceDocument();
        var targets = document.RootElement.GetProperty("captureTargets")
            .EnumerateArray()
            .Select(target => new
            {
                Id = target.GetProperty("id").GetString(),
                Subject = target.GetProperty("subject").GetString(),
                Scenario = target.GetProperty("scenario").GetString(),
                Status = target.GetProperty("retentionStatus").GetString(),
                RunnerCommand = target.GetProperty("runnerCommand").GetString(),
                RequiredEnvironment = target.GetProperty("requiredEnvironment").GetString(),
            })
            .OrderBy(target => target.Id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(3, targets.Length);
        Assert.Contains(targets, target => target.Subject == "excel" && target.Scenario == "excel-conditional-formatting-gallery");
        Assert.Contains(targets, target => target.Subject == "wpf" && target.Scenario == "freex-conditional-formatting-gallery");
        Assert.Contains(targets, target => target.Subject == "avalonia" && target.Scenario == "avalonia-conditional-formatting-gallery");
        Assert.All(targets, target => Assert.False(string.IsNullOrWhiteSpace(target.Status)));
        Assert.All(targets, target => Assert.Contains(target.Scenario!, target.RunnerCommand!, StringComparison.Ordinal));
        Assert.All(targets, target => Assert.Contains("foreground", target.RequiredEnvironment!, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OpenedStateEvidence_DoesNotCountBlockedOrMissingManifests()
    {
        using var document = LoadEvidenceDocument();
        var root = document.RootElement;
        var targets = root.GetProperty("captureTargets").EnumerateArray().ToArray();
        var completeTargets = targets.Count(IsCompleteRetainedOpenedStateTarget);

        Assert.Equal(
            completeTargets,
            root.GetProperty("summary").GetProperty("completeOpenedStateCaptureTargets").GetInt32());
        Assert.Equal(
            targets.Length - completeTargets,
            root.GetProperty("summary").GetProperty("missingOrIncompleteOpenedStateCaptureTargets").GetInt32());

        foreach (var blocked in targets.Where(target => !IsCompleteRetainedOpenedStateTarget(target)))
        {
            Assert.NotEqual("retained-opened-state-capture", blocked.GetProperty("retentionStatus").GetString());
            Assert.False(blocked.GetProperty("screenshotExists").GetBoolean());
        }
    }

    private static JsonDocument LoadEvidenceDocument()
    {
        var path = Path.Combine(
            FunctionalParityMatrix.RepoRoot(),
            "docs",
            "parity",
            "conditional-format-opened-state-evidence.json");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private static bool IsCompleteRetainedOpenedStateTarget(JsonElement target) =>
        target.GetProperty("captureStatus").GetString() == "complete" &&
        target.GetProperty("screenshotExists").GetBoolean() &&
        target.GetProperty("retentionStatus").GetString() == "retained-opened-state-capture";
}
