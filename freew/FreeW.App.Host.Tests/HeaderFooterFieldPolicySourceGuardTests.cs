using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class HeaderFooterFieldPolicySourceGuardTests
{
    [Fact]
    public void FreeWRibbonCommands_DelegatesHeaderFooterAndFieldPolicyToPresentationPlanners()
    {
        var source = ReadHostRibbonSource();
        var workflow = ReadPresentationRibbonSource("HeaderFooterRibbonWorkflow.cs");
        var fieldPicker = ReadHostSource("FieldPickerDialog.cs");

        source.Should().Contain("HeaderFooterDialogPlanner.PlanSlotActivation(");
        source.Should().Contain("HeaderFooterDialogPlanner.GetSlot(");
        source.Should().Contain("HeaderFooterDialogPlanner.SetSlot(");
        source.Should().Contain("HeaderFooterDialogPlanner.BuildPlainTextHeaderFooter(");
        source.Should().Contain("HeaderFooterDialogPlanner.AddPageNumberToSlot(");
        source.Should().Contain("HeaderFooterDialogPlanner.BuildSlotDialogState(");
        source.Should().Contain("HeaderFooterDialogPlanner.BuildSlotDialogResult(");
        source.Should().Contain("HeaderFooterRibbonWorkflow.CreatePageSettingCommands(");
        workflow.Should().Contain("HeaderFooterDialogPlanner.TryParseDistance(");
        workflow.Should().Contain("HeaderFooterDialogPlanner.FormatDistance(");
        fieldPicker.Should().Contain("FieldPickerDialogPlanner.Categories");
        fieldPicker.Should().Contain(".ChoicesForCategory(");
        fieldPicker.Should().Contain("FieldPickerDialogPlanner.TryGetInstruction(");
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
        source.Should().NotContain("class DifferentFirstPageToggleCommand");
        source.Should().NotContain("class DifferentOddEvenPagesCommand");
        source.Should().NotContain("class HeaderFromTopCommand");
        source.Should().NotContain("class FooterFromBottomCommand");
    }

    private static string ReadHostRibbonSource()
        => ReadHostSource("Ribbon", "FreeWRibbonCommands.cs");

    private static string ReadHostSource(params string[] relativePath)
    {
        var path = Path.Combine(
            [
                TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
                "freew",
                "FreeW.App.Host",
                .. relativePath,
            ]);
        return File.ReadAllText(path);
    }

    private static string ReadPresentationRibbonSource(string fileName)
    {
        var path = Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew",
            "FreeW.App.Presentation",
            "Ribbon",
            fileName);
        return File.ReadAllText(path);
    }

}
