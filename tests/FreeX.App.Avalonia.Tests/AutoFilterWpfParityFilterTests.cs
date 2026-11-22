using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AutoFilterWpfParityFilterTests
{
    [Fact]
    public void AutoFilterFlyout_ValidatesTypedCriteriaOnOkLikeWpf()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.AutoFilter.cs");

        source.Should().Contain("AutoFilterDialogCriteriaPlanner.ValidateTypedCriteria(");
        source.Should().Contain("criteriaValidation.Validate() is { } issue");
        source.Should().Contain("AutoFilterMenuItemKind.FilterFamily");
        source.Should().Contain("UiText.Format(\"AutoFilter_TitleWithHeader\", menuPlan.HeaderText)");
        source.Should().Contain("AvaloniaUserMessageDialog.ShowWarningAsync(this, message, title)");
        source.Should().Contain("valueBox.Text = \"10\"");
    }

    [Fact]
    public void PivotHeaderDropdownFromCell_UsesTheSameFieldMenuAsWpf()
    {
        var source = TestWorkspaceFileLocator.ReadAllTextFromWorkspaceRoot(
            "src", "FreeX.App.Avalonia", "MainWindow.Pivot.cs");
        var start = source.IndexOf("private void ShowPivotHeaderDropdownFromTarget", StringComparison.Ordinal);
        var end = source.IndexOf("private void InvokePivotHeaderAction", StringComparison.Ordinal);
        var body = source[start..end];

        body.Should().Contain("AvaloniaPivotChartFieldContextMenu.BuildItems(");
        body.Should().NotContain("PivotHeaderDropdownMenuBuilder");
    }
}
