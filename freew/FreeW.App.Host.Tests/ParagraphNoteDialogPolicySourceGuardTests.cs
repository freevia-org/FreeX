using System.IO;

namespace FreeW.App.Host.Tests;

public sealed class ParagraphNoteDialogPolicySourceGuardTests
{
    [Theory]
    [InlineData("ParagraphIndentDialog.cs", "ParagraphIndentDialogPlanner.BuildInitialState(", "new ParagraphIndentDialogInput(", "ParagraphIndentDialogPlanner.TryBuildResult(")]
    [InlineData("CustomParagraphSpacingDialog.cs", "CustomParagraphSpacingDialogPlanner.BuildInitialState(", "new CustomParagraphSpacingDialogInput(", "CustomParagraphSpacingDialogPlanner.TryBuildResult(")]
    [InlineData("ParagraphBreaksDialog.cs", "ParagraphBreaksDialogPlanner.BuildInitialState(", "new ParagraphBreaksDialogInput(", "ParagraphBreaksDialogPlanner.TryBuildResult(")]
    public void Dialogs_DelegateStateValidationAndResultPolicyToPresentationPlanner(
        string fileName,
        string initialStateCall,
        string inputConstruction,
        string resultCall)
    {
        var source = ReadHostSource(fileName);

        source.Should().Contain("using FreeW.App.Presentation.Dialogs;");
        source.Should().Contain(initialStateCall);
        source.Should().Contain(inputConstruction);
        source.Should().Contain(resultCall);
    }

    [Theory]
    [InlineData("FootnoteEndnoteOptionsDialog.cs")]
    [InlineData("ParagraphIndentDialog.cs")]
    [InlineData("CustomParagraphSpacingDialog.cs")]
    [InlineData("ParagraphBreaksDialog.cs")]
    public void Dialogs_DoNotOwnNumericParsingOrValidationMessages(string fileName)
    {
        var source = ReadHostSource(fileName);

        source.Should().NotContain("int.TryParse(");
        source.Should().NotContain("double.TryParse(");
        source.Should().NotContain("NumberStyles.");
        source.Should().NotContain("Enter a positive integer for the start-at values.");
        source.Should().NotContain("Enter non-negative indent values in points.");
        source.Should().NotContain("Space before must be between 0 and 200 pt.");
        source.Should().NotContain("Space after must be between 0 and 200 pt.");
        source.Should().NotContain("Line spacing must be between 0.01 and 10.");
        source.Should().NotContain("Enter valid non-negative values in points; line spacing must be positive.");
    }

    [Fact]
    public void FootnoteEndnoteOptionsDialog_DoesNotOwnFormatOrRestartCatalogs()
    {
        var source = ReadHostSource("FootnoteEndnoteOptionsDialog.cs");

        source.Should().Contain("FootnoteEndnoteOptionsDialogPlanner.CreateSession(");
        source.Should().Contain("_session.FormatItems");
        source.Should().Contain("_session.RestartItems(section.Kind)");
        source.Should().Contain("_session.PlanAcceptance()");
        source.Should().Contain("var surface = FootnoteEndnoteOptionsDialogPlanner.Surface;");
        source.Should().Contain("surface.Sections.ToDictionary(section => section.Kind, CreateControls)");
        source.Should().Contain("foreach (var section in surface.Sections)");
        source.Should().Contain("_session.UpdateIndex(");
        source.Should().Contain("AutomationProperties.SetAutomationId(");
        source.Should().NotContain("Title = \"Footnote and Endnote\"");
        source.Should().NotContain("SectionHeader(\"Footnotes\")");
        source.Should().NotContain("SectionHeader(\"Endnotes\")");
        source.Should().NotContain("AddRow(grid, 0, \"Number format:\"");
        source.Should().NotContain("NoteNumberFormat.Decimal");
        source.Should().NotContain("NoteNumberFormat.LowerRoman");
        source.Should().NotContain("NoteNumberRestart.EachPage");
        source.Should().NotContain("NoteNumberRestart.EachSection");
        source.Should().NotContain("new FootnoteEndnoteOptionsDialogInput(");
        source.Should().NotContain("FootnoteEndnoteOptionsDialogPlanner.TryBuildResult(");
        source.Should().Contain("private FootnoteEndnoteOptionsDialogResult? _result;");
        source.Should().Contain("_result = acceptance.Result;");
        source.Should().NotContain("internal sealed record Result(");
    }

    [Fact]
    public void ParagraphDialogs_DoNotOwnSignedFirstLineOrBreakResultConstruction()
    {
        var indentSource = ReadHostSource("ParagraphIndentDialog.cs");
        var breaksSource = ReadHostSource("ParagraphBreaksDialog.cs");

        indentSource.Should().Contain("ParagraphIndentDialogPlanner.SpecialItems");
        indentSource.Should().Contain("ParagraphIndentDialogPlanner.IsSpecialAmountEnabled(");
        indentSource.Should().NotContain("private enum Special");
        indentSource.Should().NotContain("Math.Abs(");

        breaksSource.Should().Contain("ParagraphIndentDialogPlanner.SpecialItems");
        breaksSource.Should().Contain("ParagraphBreaksDialogPlanner.IsSpecialAmountEnabled(");
        breaksSource.Should().Contain("using ParagraphBreaksResult = FreeW.App.Presentation.Dialogs.ParagraphBreaksDialogResult;");
        breaksSource.Should().NotContain("private enum Special");
        breaksSource.Should().NotContain("Math.Abs(");
        breaksSource.Should().NotContain("new ParagraphBreaksResult(");
        breaksSource.Should().NotContain("new ParagraphBreaksDialogResult(");
    }

    [Fact]
    public void CustomParagraphSpacingDialog_DoesNotConstructSpacingSet()
    {
        var source = ReadHostSource("CustomParagraphSpacingDialog.cs");

        source.Should().NotContain("new DocumentParagraphSpacingSet(");
    }

    private static string ReadHostSource(string fileName)
    {
        var path = Path.Combine(TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"), "freew", "FreeW.App.Host", fileName);
        return File.ReadAllText(path);
    }

}
