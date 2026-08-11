using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class TableFormulaDialogPolicySourceGuardTests
{
    [Fact]
    public void TableFormulaDialog_DelegatesCatalogsAndResultPolicyToPresentationSession()
    {
        var source = ReadHostSource("TableFormulaDialog.cs");

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain("TableFormulaDialogSession");
        source.Should().Contain("_session.NumberFormats");
        source.Should().Contain("_session.Functions");
        source.Should().Contain("Title = TableFormulaDialogPlanner.Title;");
        source.Should().Contain("TableFormulaDialogPlanner.FormulaLabel");
        source.Should().Contain("TableFormulaDialogPlanner.NumberFormatLabel");
        source.Should().Contain("TableFormulaDialogPlanner.PasteFunctionLabel");
        source.Should().Contain("_session.PasteFunction(");
        source.Should().Contain("new TableFormulaDialogInput(");
        source.Should().Contain("_session.PlanAcceptance(");
        source.Should().NotContain("TableFormulaDialogPlanner.PasteFunction(");
        source.Should().NotContain("TableFormulaDialogPlanner.TryBuildResult(");
        source.Should().NotContain("Title = \"Formula\"");
        source.Should().NotContain("Text = \"Formula:\"");
        source.Should().NotContain("Text = \"Number format:\"");
        source.Should().NotContain("Text = \"Paste function:\"");
        source.Should().NotContain("private static readonly string[] Functions");
        source.Should().NotContain("private static readonly string[] NumberFormats");
        source.Should().NotContain("new TableFormulaField(");
    }

    [Fact]
    public void TableFormulaCommand_DelegatesDefaultFormulaPolicyToPresentationPlanner()
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        var host = ReadHostSource("Ribbon", "FreeWRibbonCommands.cs");
        var profile = File.ReadAllText(Path.Combine(
            root,
            "freew",
            "FreeW.App.Presentation",
            "Ribbon",
            "FreeWRibbonEditorExecutionProfile.cs"));

        profile.Should().Contain("TableFormulaDialogPlanner.BuildInitialState(");
        host.Should().NotContain("TableFormulaDialogPlanner.BuildInitialState(");
        host.Should().NotContain("private static string DefaultFormula(");
        host.Should().NotContain("private static bool HasNumberAbove(");
        host.Should().NotContain("private static bool HasNumberLeft(");
        host.Should().NotContain("TableFormulaEvaluator.TryParseCellNumber(");
    }

    private static string ReadHostSource(params string[] pathParts)
    {
        var root = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host");
        var fullPath = pathParts.Aggregate(root, Path.Combine);
        return File.ReadAllText(fullPath);
    }

}
