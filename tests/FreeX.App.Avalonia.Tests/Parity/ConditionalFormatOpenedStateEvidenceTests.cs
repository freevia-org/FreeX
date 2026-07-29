using System;
using System.IO;
using System.Linq;
using System.Text.Json;

using Xunit;

namespace FreeX.App.Avalonia.Tests.Parity;

public sealed class ConditionalFormatOpenedStateEvidenceTests
{
    [Fact]
    public void OpenedStateEvidence_TracksConditionalFormatRuntimeCatalog()
    {
        using var document = LoadEvidenceDocument();
        var root = document.RootElement;
        var summary = root.GetProperty("summary");

        Assert.Equal("freex.parity.conditional-format-opened-state-evidence.v1", root.GetProperty("schema").GetString());
        Assert.Equal(38, summary.GetProperty("conditionalFormatPopupGalleryRows").GetInt32());
        Assert.Equal(38, summary.GetProperty("conditionalFormatPopupCatalogItems").GetInt32());

        var catalogRows = FunctionalParityClassifier.ConditionalFormattingGalleryRows
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        var evidenceRows = root.GetProperty("rows")
            .EnumerateArray()
            .Select(row => row.GetProperty("id").GetString()!)
            .OrderBy(id => id, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(catalogRows, evidenceRows);
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
                BlockerCategory = target.GetProperty("blockerCategory").GetString(),
                ManifestValidationStatus = target.GetProperty("manifestValidationStatus").GetString(),
                NextCaptureAction = target.GetProperty("nextCaptureAction").GetString(),
                ManifestMatchesTarget = target.GetProperty("manifestMatchesTarget").GetBoolean(),
                EnvironmentSnapshotStatus = target.GetProperty("environmentSnapshotStatus").GetString(),
                EnvironmentSummary = target.GetProperty("environmentSummary").GetString(),
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
        Assert.All(targets, target => Assert.False(string.IsNullOrWhiteSpace(target.BlockerCategory)));
        Assert.All(targets, target => Assert.False(string.IsNullOrWhiteSpace(target.ManifestValidationStatus)));
        Assert.All(targets, target => Assert.False(string.IsNullOrWhiteSpace(target.NextCaptureAction)));
        Assert.All(targets, target => Assert.True(target.ManifestMatchesTarget));
        Assert.All(targets, target => Assert.Equal("captured", target.EnvironmentSnapshotStatus));
        Assert.All(targets, target => Assert.Contains("interactive=", target.EnvironmentSummary!, StringComparison.Ordinal));
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

    [Fact]
    public void OpenedStateEvidence_ValidatesCommittedCaptureManifests()
    {
        using var document = LoadEvidenceDocument();
        var targets = document.RootElement.GetProperty("captureTargets")
            .EnumerateArray()
            .ToArray();

        Assert.All(targets, target =>
        {
            var errors = target.GetProperty("manifestValidationErrors")
                .EnumerateArray()
                .Select(error => error.GetString())
                .ToArray();

            Assert.True(target.TryGetProperty("manifestValidationStatus", out var status));
            if (target.GetProperty("captureStatus").GetString() == "missing-manifest")
            {
                Assert.Equal("missing", status.GetString());
                Assert.Contains("manifest-file-missing", errors);
                return;
            }

            Assert.Equal("valid", status.GetString());
            Assert.Empty(errors);
            Assert.Equal("captured", target.GetProperty("environmentSnapshotStatus").GetString());
            Assert.False(string.IsNullOrWhiteSpace(target.GetProperty("environmentSummary").GetString()));
        });

        foreach (var retained in targets.Where(IsCompleteRetainedOpenedStateTarget))
        {
            Assert.True(retained.GetProperty("manifestMatchesTarget").GetBoolean());
            Assert.Equal("valid", retained.GetProperty("manifestValidationStatus").GetString());
            Assert.False(string.IsNullOrWhiteSpace(retained.GetProperty("screenshotPath").GetString()));
        }
    }

    [Fact]
    public void OpenedStateEvidence_ClassifiesCurrentCaptureBlockers()
    {
        using var document = LoadEvidenceDocument();
        var targets = document.RootElement.GetProperty("captureTargets")
            .EnumerateArray()
            .ToDictionary(
                target => target.GetProperty("id").GetString()!,
                target => target,
                StringComparer.Ordinal);

        AssertTargetBlocker(
            targets["excel.conditional-formatting-gallery.opened"],
            "excel-com-unavailable",
            "Microsoft Excel COM");
        AssertTargetBlocker(
            targets["wpf.conditional-formatting-gallery.opened"],
            "foreground-focus-unavailable",
            "foreground");
        AssertTargetBlocker(
            targets["avalonia.conditional-formatting-gallery.opened"],
            "foreground-focus-unavailable",
            "foreground");

        var categories = document.RootElement.GetProperty("blockerCategories")
            .EnumerateArray()
            .ToDictionary(
                item => item.GetProperty("category").GetString()!,
                item => item.GetProperty("count").GetInt32(),
                StringComparer.Ordinal);

        Assert.Equal(1, categories["excel-com-unavailable"]);
        Assert.Equal(2, categories["foreground-focus-unavailable"]);
    }

    [Fact]
    public void OpenedStateEvidence_EmitsForegroundOperatorChecklist()
    {
        using var document = LoadEvidenceDocument();
        var checklist = document.RootElement.GetProperty("operatorChecklist")
            .EnumerateArray()
            .Select(item => new
            {
                Phase = item.GetProperty("phase").GetString(),
                Command = item.GetProperty("command").GetString(),
                Purpose = item.GetProperty("purpose").GetString(),
            })
            .ToArray();

        Assert.Contains(checklist, item => item.Phase == "build" && item.Command!.Contains("dotnet build", StringComparison.Ordinal));
        Assert.Contains(checklist, item => item.Phase == "preflight" && item.Command!.Contains("Invoke-ForegroundCapture.ps1 -EnvironmentPreflight", StringComparison.Ordinal));
        Assert.Contains(checklist, item => item.Phase == "capture:excel" && item.Command!.Contains("excel-conditional-formatting-gallery", StringComparison.Ordinal));
        Assert.Contains(checklist, item => item.Phase == "capture:wpf" && item.Command!.Contains("freex-conditional-formatting-gallery", StringComparison.Ordinal));
        Assert.Contains(checklist, item => item.Phase == "capture:avalonia" && item.Command!.Contains("avalonia-conditional-formatting-gallery", StringComparison.Ordinal));
        Assert.Contains(checklist, item => item.Phase == "refresh" && item.Command!.Contains("Generate-ConditionalFormatOpenedStateEvidence.ps1 -Check", StringComparison.Ordinal));
        Assert.All(checklist, item => Assert.False(string.IsNullOrWhiteSpace(item.Purpose)));
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

    private static void AssertTargetBlocker(JsonElement target, string expectedCategory, string expectedActionText)
    {
        if (IsCompleteRetainedOpenedStateTarget(target))
        {
            return;
        }

        Assert.Equal(expectedCategory, target.GetProperty("blockerCategory").GetString());
        Assert.Contains(
            expectedActionText,
            target.GetProperty("nextCaptureAction").GetString(),
            StringComparison.OrdinalIgnoreCase);
    }
}
