using Free.Shared.AppServices.Printing;
using FreeP.App.Compositor.Printing;

namespace FreeP.App.Compositor.Tests;

public sealed class PrintSelectionPlannerTests
{
    [Fact]
    public void Build_PrefersRequestedPrinter_AndCarriesDialogSelections()
    {
        var discovery = new PrinterDiscoveryResult(
            PrinterDiscoveryStatus.Available,
            [new PrinterInfo("Office", true), new PrinterInfo("PDF")],
            "Office");

        var plan = PrintSelectionPlanner.Build(
            discovery,
            new PrintSelection("PDF", 3, PrintPageRange.Between(2, 4), PrintOrientation.Landscape, Collate: false));

        plan.Status.Should().Be(PrintCapabilityStatus.Ready);
        plan.SelectedPrinter.Should().Be("PDF");
        plan.Copies.Should().Be(3);
        plan.PageRange.Should().Be(PrintPageRange.Between(2, 4));
        plan.Orientation.Should().Be(PrintOrientation.Landscape);
        plan.CanSubmit.Should().BeTrue();
    }

    [Fact]
    public void Build_NoPrinters_DisablesSubmitWithTruthfulMessage()
    {
        var plan = PrintSelectionPlanner.Build(
            new PrinterDiscoveryResult(PrinterDiscoveryStatus.NoPrinters, [], null));

        plan.Status.Should().Be(PrintCapabilityStatus.NoPrinters);
        plan.SelectedPrinter.Should().BeNull();
        plan.CanSubmit.Should().BeFalse();
        plan.Message.Should().Contain("No printers");
    }

    [Fact]
    public void PrintSelection_RejectsInvalidCopiesAndRanges()
    {
        var copies = () => new PrintSelection(Copies: 0).Validate();
        var range = () => PrintPageRange.Between(4, 2).Validate();

        copies.Should().Throw<ArgumentOutOfRangeException>();
        range.Should().Throw<ArgumentException>();
    }
}
