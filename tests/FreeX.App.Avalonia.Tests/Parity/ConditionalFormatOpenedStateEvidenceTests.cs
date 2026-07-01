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
            })
            .OrderBy(target => target.Id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(3, targets.Length);
        Assert.Contains(targets, target => target.Subject == "excel" && target.Scenario == "excel-conditional-formatting-gallery");
        Assert.Contains(targets, target => target.Subject == "wpf" && target.Scenario == "freex-conditional-formatting-gallery");
        Assert.Contains(targets, target => target.Subject == "avalonia" && target.Scenario == "avalonia-conditional-formatting-gallery");
        Assert.All(targets, target => Assert.False(string.IsNullOrWhiteSpace(target.Status)));
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
}
