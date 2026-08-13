using FreeW.App.Presentation.Backstage;

namespace FreeW.App.Presentation.Tests;

public sealed class BackstagePrintEvidenceTextFormatterTests
{
    [Fact]
    public void Format_ProjectsLabelsDescriptionScenariosAndRequirements()
    {
        var row = new BackstagePrintEvidenceRow(
            BackstagePrintEvidenceKind.PrintPreviewFidelity,
            BackstagePrintEvidenceStatus.HostBacked,
            "Preview is rendered by the active host.",
            ["print-preview"],
            [new BackstagePrintEvidenceRequirement("freew-wpf", "print-preview", 2)]);

        BackstagePrintEvidenceTextFormatter.Format(row).Should().Be(
            "Print preview fidelity - Host backed\n" +
            "Preview is rendered by the active host.\n" +
            "Scenarios: print-preview\n" +
            "Required rows: freew-wpf/print-preview >= 2");
    }

    [Fact]
    public void Format_UsesSharedEmptyEvidenceText()
    {
        var row = new BackstagePrintEvidenceRow(
            BackstagePrintEvidenceKind.NativePrint,
            BackstagePrintEvidenceStatus.Deferred,
            "Native print remains deferred.",
            [],
            []);

        BackstagePrintEvidenceTextFormatter.Format(row).Should().EndWith(
            "Scenarios: No fixture scenario\nRequired rows: No required visual row");
    }

    [Fact]
    public void Labels_FallBackToEnumTextForUnknownValues()
    {
        BackstagePrintEvidenceTextFormatter.KindLabel((BackstagePrintEvidenceKind)99).Should().Be("99");
        BackstagePrintEvidenceTextFormatter.StatusLabel((BackstagePrintEvidenceStatus)99).Should().Be("99");
    }
}
