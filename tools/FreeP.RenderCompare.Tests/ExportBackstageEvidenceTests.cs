using FreeP.App.Compositor;

namespace FreeP.RenderCompare.Tests;

public sealed class ExportBackstageEvidenceTests
{
    [Fact]
    public void CreatePlan_UsesExportBackstageEvidenceRoutesWithoutPowerPointBaseline()
    {
        var root = Path.Combine(Path.GetTempPath(), "freep-export-backstage-" + Guid.NewGuid().ToString("N"));
        var deck = Path.Combine(root, "deck.pptx");

        var plan = ExportBackstageEvidence.CreatePlan(deck, root);

        plan.DeckPath.Should().Be(Path.GetFullPath(deck));
        plan.OutputDirectory.Should().Be(Path.GetFullPath(root));
        plan.SummaryCsvPath.Should().Be(Path.Combine(Path.GetFullPath(root), "export-backstage-evidence.csv"));
        plan.RequiresPowerPointBaseline.Should().BeFalse();
    }

    [Fact]
    public void WriteSummaryCsv_WritesNoComWpfAvaloniaClassificationRows()
    {
        var root = Path.Combine(Path.GetTempPath(), "freep-export-backstage-csv-" + Guid.NewGuid().ToString("N"));
        var csvPath = Path.Combine(root, "summary.csv");
        var rows = new[]
        {
            new PresentationExportBackstageEvidenceRow(
                EvidenceId: "freep.export.backstage.print-handouts-3",
                Area: "Backstage Print 3-up handout package handoff",
                SharedPlanner: PresentationExportBackstageEvidencePlanner.SharedPlannerEvidence,
                Status: "shared-package-ready-host-deferred",
                WpfEvidence: "WPF:HandoutPdf:2:HostPrinterUnavailableDeferredByHost",
                AvaloniaEvidence: "Avalonia:HandoutPdf:2:HostPrinterUnavailableDeferredByHost",
                PowerPointBaseline: PresentationExportBackstageEvidencePlanner.PowerPointBaselineDeferred,
                RequiresPowerPointComBaseline: true,
                Detail: "route=HandoutPdf; pages=2; layout=Handouts, with comma")
        };

        try
        {
            ExportBackstageEvidence.WriteSummaryCsv(csvPath, rows);

            File.ReadAllLines(csvPath).Should().Equal(
                "evidenceId,area,sharedPlanner,status,wpfEvidence,avaloniaEvidence,powerPointBaseline,requiresPowerPointComBaseline,detail",
                "freep.export.backstage.print-handouts-3,Backstage Print 3-up handout package handoff,shared-export-backstage-planner,shared-package-ready-host-deferred,WPF:HandoutPdf:2:HostPrinterUnavailableDeferredByHost,Avalonia:HandoutPdf:2:HostPrinterUnavailableDeferredByHost,n/a/deferred-powerpoint-com-baseline,true,\"route=HandoutPdf; pages=2; layout=Handouts, with comma\"");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
