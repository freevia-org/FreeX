using System.IO;
using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class ResidualDialogParitySourceTests
{
    [Fact]
    public void UnfocusedListSelection_UsesWpfAero2ListBoxGreyNotListViewGrey()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "DialogControlStyles.cs"));

        // Sampled from the WPF dialog captures (GoTo, SelectDataSource, ChangeChartType, CF New Rule):
        // fill #F6F6F6 inside a 1 px #DADADA frame while the list is unfocused.
        source.Should().Contain("InactiveListBoxRowFace = new ImmutableSolidColorBrush(Color.FromRgb(0xF6, 0xF6, 0xF6))");
        source.Should().Contain("InactiveListBoxRowFrame = new ImmutableSolidColorBrush(Color.FromRgb(0xDA, 0xDA, 0xDA))");
        source.Should().Contain("DefaultInactiveSelectedItemBackgroundBrush = InactiveListBoxRowFace;");
        source.Should().Contain("DefaultInactiveSelectedItemBorderBrush = InactiveListBoxRowFrame;");
        source.Should().Contain("SelectedListRowPresenterStyle(InactiveList, InactiveListBoxRowFace, InactiveListBoxRowFrame)");
        // The focused state and the solid-highlight rows (Format Cells Number) are unchanged.
        source.Should().Contain("SelectedListRowPresenterStyle(x => x, SelectedRowFace, SelectedRowFrame)");
        source.Should().Contain("WpfHighlightFace = new ImmutableSolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD7))");
    }

    [Fact]
    public void DataValidationSettingsCheckBoxes_MatchWpfRowMetricsLocally()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        // WPF: 15 px rows, 8 px bottom margin (23 px pitch, box at a 1 px offset). The 20 px compact
        // row keeps its height (a 15 px cap clips descenders) and is shifted by -3 / +6 instead.
        source.Should().Contain("new Thickness(labelColumnWidth, -3, 0, 6)");
        source.Should().Contain("showDropdownBox.Margin = controlColumnCheckBoxMargin;");
        source.Should().Contain("allowBlankBox.Margin = controlColumnCheckBoxMargin;");
        source.Should().Contain("sameSettingsBox.Margin = controlColumnCheckBoxMargin;");
        // The shared compact check box chrome is not touched.
        source.Should().Contain("cb.MinHeight = 20; cb.MaxHeight = 20;");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
