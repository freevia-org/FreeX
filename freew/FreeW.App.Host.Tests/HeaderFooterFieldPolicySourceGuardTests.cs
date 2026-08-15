using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class HeaderFooterFieldPolicySourceGuardTests
{
    [Fact]
    public void FreeWRibbonCommands_DelegatesHeaderFooterAndFieldPolicyToPresentationPlanners()
    {
        var source = ReadHostRibbonSource();

        source.Should().Contain("HeaderFooterDialogPlanner.PlanSlotActivation(");
        source.Should().Contain("HeaderFooterDialogPlanner.GetSlot(");
        source.Should().Contain("HeaderFooterDialogPlanner.SetSlot(");
        source.Should().Contain("HeaderFooterDialogPlanner.BuildPlainTextHeaderFooter(");
        source.Should().Contain("HeaderFooterDialogPlanner.AddPageNumberToSlot(");
        source.Should().Contain("HeaderFooterDialogPlanner.BuildSlotDialogState(");
        source.Should().Contain("HeaderFooterDialogPlanner.BuildSlotDialogResult(");
        source.Should().Contain("HeaderFooterDialogPlanner.TryParseDistance(");
        source.Should().Contain("HeaderFooterDialogPlanner.FormatDistance(");
        source.Should().Contain("FieldPickerDialogPlanner.Categories");
        source.Should().Contain("FieldPickerDialogPlanner.ChoicesForCategory(");
        source.Should().Contain("FieldPickerDialogPlanner.TryGetInstruction(");
        source.Should().Contain("QuickPartRibbonWorkflow.Register(");
    }

    [Fact]
    public void FreeWRibbonCommands_DoesNotOwnHeaderFooterAndFieldCatalogPolicy()
    {
        var source = ReadHostRibbonSource();

        source.Should().NotContain("new Choice(\"Date and Time\"");
        source.Should().NotContain("new Choice(\"Document Information\"");
        source.Should().NotContain("new Choice(\"Numbering\"");
        source.Should().NotContain("new Choice(\"References\"");
        source.Should().NotContain("slotName switch");
        source.Should().NotContain("FieldKind == RunFieldKind.PageNumber) ?? false");
        source.Should().NotContain("new HeaderFooter();");
        source.Should().NotContain("new Run(\"Page \")");
    }

    private static string ReadHostRibbonSource()
    {
        var path = Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew",
            "FreeW.App.Host",
            "Ribbon",
            "FreeWRibbonCommands.cs");
        return File.ReadAllText(path);
    }

}
