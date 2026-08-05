using FluentAssertions;

namespace FreeX.App.Presentation.Tests.FormulaBar;

public sealed class FormulaBarChromePlannerTests
{
    [Fact]
    public void CommandButtons_ExposeSharedFormulaBarOrderAndMetadata()
    {
        FormulaBarChromePlanner.CommandButtons.Should().ContainInOrder(
            FormulaBarChromePlanner.CancelEditButton,
            FormulaBarChromePlanner.EnterEditButton,
            FormulaBarChromePlanner.InsertFunctionButton);

        FormulaBarChromePlanner.CancelEditButton.Should().Be(new FormulaBarChromeElementPlan(
            FormulaBarChromeElement.CancelEditButton,
            "FormulaBarCancelButton",
            "MainWindow_TooltipTitle_CancelFormulaBarEdit",
            "MainWindow_TooltipDescription_CancelFormulaBarEdit",
            "Cancel Formula Bar Edit",
            "FC",
            FormulaBarChromeGlyph.Cancel));

        FormulaBarChromePlanner.EnterEditButton.KeyTip.Should().Be("FE");
        FormulaBarChromePlanner.InsertFunctionButton.Should().Match<FormulaBarChromeElementPlan>(plan =>
            plan.AutomationId == "FormulaBarFxButton" &&
            plan.CommandName == "Insert Function" &&
            plan.KeyTip == "FX" &&
            plan.ContentResourceKey == "MainWindow_Content_Fx" &&
            plan.IsItalic);
    }

    [Fact]
    public void FormulaBarFields_ExposeSharedResourceKeys()
    {
        FormulaBarChromePlanner.NameBox.AutomationNameResourceKey.Should().Be("MainWindow_AutomationName_NameBox");
        FormulaBarChromePlanner.NameBox.HelpTextResourceKey.Should().Be("MainWindow_AutomationHelpText_GoToACellOrNamedRange");

        FormulaBarChromePlanner.FormulaBox.AutomationNameResourceKey.Should().Be("MainWindow_AutomationName_FormulaBar");
        FormulaBarChromePlanner.FormulaBox.HelpTextResourceKey.Should().Be("MainWindow_AutomationHelpText_EditTheActiveCellValueOrFormula");
    }

    [Fact]
    public void WpfFormulaBarChrome_UsesPlannerMetadataKeys()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "FreeX.App.Host", "MainWindow.xaml"));

        foreach (var plan in FormulaBarChromePlanner.CommandButtons)
        {
            xaml.Should().Contain(plan.AutomationId);
            xaml.Should().Contain(plan.AutomationNameResourceKey);
            xaml.Should().Contain(plan.HelpTextResourceKey);
            xaml.Should().Contain(plan.CommandName);
            xaml.Should().Contain(plan.KeyTip);
        }

        xaml.Should().Contain(FormulaBarChromePlanner.NameBox.AutomationNameResourceKey);
        xaml.Should().Contain(FormulaBarChromePlanner.NameBox.HelpTextResourceKey);
        xaml.Should().Contain(FormulaBarChromePlanner.FormulaBox.AutomationNameResourceKey);
        xaml.Should().Contain(FormulaBarChromePlanner.FormulaBox.HelpTextResourceKey);
        xaml.Should().Contain(FormulaBarChromePlanner.ExpandButton.AutomationId);
        xaml.Should().Contain(FormulaBarChromePlanner.ExpandButton.AutomationNameResourceKey);
        xaml.Should().Contain(FormulaBarChromePlanner.ExpandButton.HelpTextResourceKey);
    }

    [Theory]
    [InlineData(false, FormulaBarChromeElement.ExpandButton, "MainWindow_AutomationName_ExpandFormulaBar", "MainWindow_AutomationHelpText_ExpandTheFormulaBarToAMultiLineEditor", 30, 40, false, "MainWindow_TooltipTitle_ExpandFormulaBar", "MainWindow_TooltipDescription_ExpandTheFormulaBarToAMultiLineEditor")]
    [InlineData(true, FormulaBarChromeElement.CollapseButton, "MainWindow_AutomationName_CollapseFormulaBar", "MainWindow_AutomationHelpText_CollapseTheFormulaBarToASingleLineEditor", 84, 94, true, "MainWindow_TooltipTitle_CollapseFormulaBar", "MainWindow_TooltipDescription_CollapseTheFormulaBarToASingleLineEditor")]
    public void BuildExpansion_ChoosesStateSpecificEditorChromePlan(
        bool expanded,
        FormulaBarChromeElement expectedElement,
        string expectedNameKey,
        string expectedHelpKey,
        double expectedEditorHeight,
        double expectedHostHeight,
        bool expectedChevronPointsUp,
        string expectedTooltipTitleKey,
        string expectedTooltipDescriptionKey)
    {
        var plan = FormulaBarChromePlanner.BuildExpansion(expanded);

        plan.Expanded.Should().Be(expanded);
        plan.AcceptsReturn.Should().Be(expanded);
        plan.EditorHeight.Should().Be(expectedEditorHeight);
        plan.HostHeight.Should().Be(expectedHostHeight);
        plan.ChevronPointsUp.Should().Be(expectedChevronPointsUp);
        plan.TooltipTitleResourceKey.Should().Be(expectedTooltipTitleKey);
        plan.TooltipDescriptionResourceKey.Should().Be(expectedTooltipDescriptionKey);
        plan.Button.Element.Should().Be(expectedElement);
        plan.Button.AutomationId.Should().Be("FormulaBarExpandBtn");
        plan.Button.AutomationNameResourceKey.Should().Be(expectedNameKey);
        plan.Button.HelpTextResourceKey.Should().Be(expectedHelpKey);
        plan.Button.KeyTip.Should().Be("BX");
        FormulaBarChromePlanner.ExpansionButton(expanded).Should().Be(plan.Button);
    }

    private static string FindRepositoryFile(params string[] relativeParts) =>
        TestWorkspaceFileLocator.FindFileFromBaseDirectory(relativeParts);
}
