using FreeW.App.Presentation.Backstage;
using FreeW.App.Presentation.DocumentView;
using FreeW.Core.Model;

namespace FreeW.App.Presentation.Tests;

public sealed class BackstagePrintPanePlannerTests
{
    [Fact]
    public void Build_ReturnsPrintActionsAndPageSummary()
    {
        var page = new PageSettings
        {
            WidthPt = 612,
            HeightPt = 792,
            MarginTopPt = 72,
            MarginBottomPt = 90,
            MarginLeftPt = 54,
            MarginRightPt = 54,
            ColumnCount = 2,
            ColumnSpacingPt = 36,
            Landscape = true,
        };

        var plan = BackstagePrintPanePlanner.Build("Agenda", page);

        plan.Description.Should().Contain("Print this document");
        plan.Fields.Should().Contain(row => row.Label == "Document" && row.Value == "Agenda");
        plan.Fields.Should().Contain(row => row.Label == "Paper" && row.Value == "8.5\" x 11\"");
        plan.Fields.Should().Contain(row => row.Label == "Orientation" && row.Value == "Landscape");
        plan.Fields.Should().Contain(row => row.Label == "Margins" && row.Value.Contains("Bottom 1.25\"", StringComparison.Ordinal));
        plan.Fields.Should().Contain(row => row.Label == "Columns" && row.Value == "2 (spacing 0.5\")");
        plan.Fields.Should().Contain(row => row.Label == "Direct print" && row.Value.StartsWith("Deferred -", StringComparison.Ordinal));

        plan.Groups.Should().Contain(group =>
            group.Heading == "Print" &&
            group.Actions.Any(action => action.Kind == BackstagePrintActionKind.Print && action.Label == "Print") &&
            group.Actions.Any(action => action.Kind == BackstagePrintActionKind.PrintPreview && action.Label == "Print Preview"));
        plan.Groups.Should().Contain(group => group.Heading == "Settings");

        plan.Evidence.Should().Contain(row =>
            row.Kind == BackstagePrintEvidenceKind.PrintPreviewFidelity &&
            row.Status == BackstagePrintEvidenceStatus.FixtureReady &&
            row.FixtureScenarioIds.Contains("backstage-print-preview-fidelity") &&
            row.Requirements.Any(requirement =>
                requirement.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId &&
                requirement.ScenarioId == "backstage-print-preview-fidelity" &&
                requirement.MinimumExpectedOutputs == 2) &&
            row.Requirements.Any(requirement =>
                requirement.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId &&
                requirement.ScenarioId == "backstage-print-preview-fidelity" &&
                requirement.MinimumExpectedOutputs == 2));
        plan.Evidence.Should().Contain(row =>
            row.Kind == BackstagePrintEvidenceKind.PdfExportFidelity &&
            row.Status == BackstagePrintEvidenceStatus.FixtureReady &&
            row.FixtureScenarioIds.Contains("backstage-pdf-export-fidelity") &&
            row.Requirements.Any(requirement =>
                requirement.HostId == FreeWVisualEvidenceManifestNormalizer.WpfHostId &&
                requirement.ScenarioId == "backstage-pdf-export-fidelity" &&
                requirement.MinimumExpectedOutputs == 2) &&
            row.Requirements.Any(requirement =>
                requirement.HostId == FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId &&
                requirement.ScenarioId == "backstage-pdf-export-fidelity" &&
                requirement.MinimumExpectedOutputs == 2));
        plan.Evidence.Should().Contain(row =>
            row.Kind == BackstagePrintEvidenceKind.NativePrint &&
            row.Status == BackstagePrintEvidenceStatus.Deferred &&
            row.FixtureScenarioIds.Count == 0 &&
            row.Requirements.Count == 0);
    }

    [Fact]
    public void BuildEvidenceRequirements_MirrorsVisualSummaryContract()
    {
        var preview = BackstagePrintPanePlanner.BuildEvidenceRequirements(
            BackstagePrintEvidenceKind.PrintPreviewFidelity);
        var pdf = BackstagePrintPanePlanner.BuildEvidenceRequirements(
            BackstagePrintEvidenceKind.PdfExportFidelity);

        preview.Should().BeEquivalentTo(
            FreeWVisualEvidenceManifestNormalizer.DefaultExpectedScenarios
                .Where(expected => expected.ScenarioId == "backstage-print-preview-fidelity")
                .Select(expected => new BackstagePrintEvidenceRequirement(
                    expected.HostId,
                    expected.ScenarioId,
                    expected.MinimumExpectedOutputs)));
        pdf.Should().BeEquivalentTo(
            FreeWVisualEvidenceManifestNormalizer.DefaultExpectedScenarios
                .Where(expected => expected.ScenarioId == "backstage-pdf-export-fidelity")
                .Select(expected => new BackstagePrintEvidenceRequirement(
                    expected.HostId,
                    expected.ScenarioId,
                    expected.MinimumExpectedOutputs)));

        preview.Select(requirement => requirement.HostId).Should().BeEquivalentTo([
            FreeWVisualEvidenceManifestNormalizer.WpfHostId,
            FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId
        ]);
        pdf.Select(requirement => requirement.HostId).Should().BeEquivalentTo([
            FreeWVisualEvidenceManifestNormalizer.WpfHostId,
            FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId
        ]);
    }

    [Fact]
    public void Build_WithTrustedVisualEvidenceSummary_MarksBackstageRendererRowsHostBacked()
    {
        var summary = BuildSummary(
            trusted: true,
            BuildScenario(
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                "backstage-print-preview-fidelity",
                trustedOutputs: 2),
            BuildScenario(
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                "backstage-print-preview-fidelity",
                trustedOutputs: 2),
            BuildScenario(
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                "backstage-pdf-export-fidelity",
                trustedOutputs: 2),
            BuildScenario(
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                "backstage-pdf-export-fidelity",
                trustedOutputs: 2));

        var plan = BackstagePrintPanePlanner.Build(
            "Agenda",
            new PageSettings(),
            visualEvidenceSummary: summary);

        plan.Evidence.Should().Contain(row =>
            row.Kind == BackstagePrintEvidenceKind.PrintPreviewFidelity &&
            row.Status == BackstagePrintEvidenceStatus.HostBacked &&
            row.Description.Contains("Real WPF and Avalonia captures", StringComparison.Ordinal));
        plan.Evidence.Should().Contain(row =>
            row.Kind == BackstagePrintEvidenceKind.PdfExportFidelity &&
            row.Status == BackstagePrintEvidenceStatus.HostBacked &&
            row.Description.Contains("backstage-pdf-export-fidelity", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_WithPartialVisualEvidenceSummary_MarksMissingRealCapturesDeferred()
    {
        var summary = BuildSummary(
            trusted: false,
            BuildScenario(
                FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                "backstage-print-preview-fidelity",
                trustedOutputs: 2),
            BuildScenario(
                FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                "backstage-print-preview-fidelity",
                trustedOutputs: 1));

        var plan = BackstagePrintPanePlanner.Build(
            "Agenda",
            new PageSettings(),
            visualEvidenceSummary: summary);

        var preview = plan.Evidence.Single(row =>
            row.Kind == BackstagePrintEvidenceKind.PrintPreviewFidelity);
        preview.Status.Should().Be(BackstagePrintEvidenceStatus.Deferred);
        preview.Description.Should().Contain("avalonia-page-layout-shot/backstage-print-preview-fidelity");
        preview.Description.Should().Contain("expected at least 2 trusted output");

        var pdf = plan.Evidence.Single(row =>
            row.Kind == BackstagePrintEvidenceKind.PdfExportFidelity);
        pdf.Status.Should().Be(BackstagePrintEvidenceStatus.Deferred);
        pdf.Description.Should().Contain("wpf-fidelity-render/backstage-pdf-export-fidelity");
        pdf.Description.Should().Contain("missing normalized scenario row");
    }

    [Fact]
    public void BuildEvidenceReadiness_IncludesScenarioSpecificSummaryFailures()
    {
        var summary = BuildSummary(
            trusted: false,
            [
                BuildScenario(
                    FreeWVisualEvidenceManifestNormalizer.WpfHostId,
                    "backstage-pdf-export-fidelity",
                    trustedOutputs: 2),
                BuildScenario(
                    FreeWVisualEvidenceManifestNormalizer.AvaloniaHostId,
                    "backstage-pdf-export-fidelity",
                    trustedOutputs: 2),
            ],
            failures:
            [
                "backstage renderer pair 'backstage-pdf-export-fidelity' missing Avalonia page(s): p2",
                "review renderer pair 'f2-comments' missing Avalonia page(s): p1"
            ]);

        var readiness = BackstagePrintPanePlanner.BuildEvidenceReadiness(
            BackstagePrintEvidenceKind.PdfExportFidelity,
            summary);

        readiness.Status.Should().Be(BackstagePrintEvidenceStatus.Deferred);
        readiness.Failures.Should().Contain(failure =>
            failure.Contains("backstage-pdf-export-fidelity", StringComparison.Ordinal));
        readiness.Failures.Should().NotContain(failure =>
            failure.Contains("f2-comments", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_WithNativePrintCapability_MarksDirectPrintHostBacked()
    {
        var plan = BackstagePrintPanePlanner.Build(
            "Agenda",
            new PageSettings(),
            BackstageDirectPrintCapability.NativeDialogAvailable(
                "WPF opens System.Windows.Controls.PrintDialog and prints the page-settings-aware paginator."));

        plan.Fields.Should().Contain(row =>
            row.Label == "Direct print" &&
            row.Value == "Available - operating-system printer dialog");
        plan.Groups.SelectMany(group => group.Actions)
            .Single(action => action.Kind == BackstagePrintActionKind.Print)
            .Description.Should().Be("Choose a printer and send the document to print.");
        plan.Evidence.Should().Contain(row =>
            row.Kind == BackstagePrintEvidenceKind.NativePrint &&
            row.Status == BackstagePrintEvidenceStatus.HostBacked &&
            row.Description.Contains("PrintDialog", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_NormalizesBlankDisplayNameAndMirrorMargins()
    {
        var page = new PageSettings { MirrorMargins = true };

        var plan = BackstagePrintPanePlanner.Build(" ", page);

        plan.Fields.Should().Contain(row => row.Label == "Document" && row.Value == "Untitled");
        plan.Fields.Should().Contain(row => row.Label == "Margins" && row.Value.EndsWith(" (mirror margins)", StringComparison.Ordinal));
    }

    private static FreeWVisualEvidenceNormalizedSummary BuildSummary(
        bool trusted,
        params FreeWVisualEvidenceNormalizedScenario[] scenarios) =>
        BuildSummary(trusted, scenarios, failures: []);

    private static FreeWVisualEvidenceNormalizedSummary BuildSummary(
        bool trusted,
        IReadOnlyList<FreeWVisualEvidenceNormalizedScenario> scenarios,
        IReadOnlyList<string> failures) =>
        new(
            FreeWVisualEvidenceManifestNormalizer.SummarySchemaId,
            FreeWVisualEvidenceManifestNormalizer.SummarySchemaVersion,
            [],
            scenarios.Select(scenario => new FreeWVisualEvidenceExpectedScenario(
                scenario.HostId,
                scenario.ScenarioId,
                scenario.MinimumExpectedOutputs)).ToArray(),
            scenarios,
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            [],
            new FreeWVisualEvidenceAuthoritySummary(
                "local-visual-evidence-only",
                false,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                []),
            [],
            new FreeWVisualEvidenceTrust(trusted, failures));

    private static FreeWVisualEvidenceNormalizedScenario BuildScenario(
        string hostId,
        string scenarioId,
        int trustedOutputs,
        bool trusted = true,
        IReadOnlyList<string>? failures = null) =>
        new(
            hostId,
            scenarioId,
            MinimumExpectedOutputs: 2,
            ActualOutputs: trustedOutputs,
            TrustedOutputs: trustedOutputs,
            Expected: true,
            new FreeWVisualEvidenceTrust(trusted, failures ?? []));
}
