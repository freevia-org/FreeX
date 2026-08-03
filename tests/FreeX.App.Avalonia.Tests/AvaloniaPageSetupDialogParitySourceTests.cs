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
        source.Should().NotContain("var marginsBox = new TextBox");
    }

    private static string RepoFile(params string[] parts)
    {
        var current = AppContext.BaseDirectory;
        while (current is not null)
        {
            var candidate = Path.Combine(new[] { current }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            current = Directory.GetParent(current)?.FullName;
        }

        throw new FileNotFoundException($"Could not locate repository file: {Path.Combine(parts)}");
    }
}
