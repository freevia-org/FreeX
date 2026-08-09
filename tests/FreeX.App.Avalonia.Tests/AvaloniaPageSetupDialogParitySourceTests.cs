using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaPageSetupDialogParitySourceTests
{
    [Fact]
    public void PageSetup_UsesSharedPlannerForSeparateMarginFieldsAndValidationFocus()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.PageLayout.cs"));

        source.Should().Contain("PageSetupDialogPlanner.LeftMarginBoxAutomationId");
        source.Should().Contain("PageSetupDialogPlanner.RightMarginBoxAutomationId");
        source.Should().Contain("PageSetupDialogPlanner.TopMarginBoxAutomationId");
        source.Should().Contain("PageSetupDialogPlanner.BottomMarginBoxAutomationId");
        source.Should().Contain("UiText.Get(\"PageSetup_Left\")");
        source.Should().Contain("UiText.Get(\"PageSetup_Right\")");
        source.Should().Contain("UiText.Get(\"PageSetup_Top\")");
        source.Should().Contain("UiText.Get(\"PageSetup_Bottom\")");
        source.Should().Contain("UiText.Get(\"PageSetup_Header\")");
        source.Should().Contain("UiText.Get(\"PageSetup_Footer\")");
        source.Should().Contain("ColumnDefinitions = new ColumnDefinitions(\"120,*\")");
        source.Should().Contain("LeftMarginText = leftMarginBox.Text ?? \"\"");
        source.Should().Contain("RightMarginText = rightMarginBox.Text ?? \"\"");
        source.Should().Contain("TopMarginText = topMarginBox.Text ?? \"\"");
        source.Should().Contain("BottomMarginText = bottomMarginBox.Text ?? \"\"");
        source.Should().Contain("HasSeparateMarginFields = true");
        source.Should().Contain("PageSetupDialogFocusTarget.RightMargin => rightMarginBox");
        source.Should().NotContain("PageSetup_LeftMargin");
        source.Should().NotContain("PageSetup_RightMargin");
        source.Should().NotContain("PageSetup_TopMargin");
        source.Should().NotContain("PageSetup_BottomMargin");
        source.Should().NotContain("UiText.Get(\"PageSetup_HeaderMargin\")");
        source.Should().NotContain("UiText.Get(\"PageSetup_FooterMargin\")");
        source.Should().NotContain("UiText.Get(\"PageSetup_CenterOnPage\")");
        source.Should().NotContain("var marginsBox = new TextBox");
    }

    private static string RepoFile(params string[] parts) =>
        TestWorkspaceFileLocator.Find(RepoFile);
}
