using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AllowEditRangeParitySourceTests
{
    [Fact]
    public void ParityFixture_MatchesWpfSeedAndDefaultRange()
    {
        var source = File.ReadAllText(RepoFile("tools", "FreeX.ParityCapture.Avalonia", "Capture", "MainWindow.ParityCapture.cs"));

        source.Should().Contain("new CellAddress(sheetId, 1, 1)");
        source.Should().Contain("new CellAddress(sheetId, 5, 5)");
        source.Should().Contain("new AllowEditRangeCommand(sheetId, existingRange)");
        source.Should().Contain("new CellAddress(sheetId, 2, 2)");
        source.Should().Contain("new CellAddress(sheetId, 5, 4)");
        source.Should().Contain("_session.SelectRange(new GridRange(");
    }

    [Fact]
    public void DialogVisualFlow_UsesWpfGeometryAndSharedChrome()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.AllowEditRange.cs"));

        source.Should().Contain("Width = 430");
        source.Should().Contain("Height = 420");
        source.Should().Contain("initialRangeText ?? FormatRangeReference(_session.SelectedRange)");
        source.Should().Contain("AllowEditRangePasswordBox");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyWindow(dialog, AllowEditRangeDialogChromeStyle)");
        source.Should().Contain("AvaloniaCompactDialogChrome.ApplyGroupBox(existingRangesGroup, AllowEditRangeDialogChromeStyle)");
        source.Should().Contain("HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left");
        source.Should().Contain("Width = 390");
        source.Should().Contain("new StackPanel");
        source.Should().Contain("existingRangesGroup");
        source.Should().Contain("rangeGroup");
        source.Should().Contain("bottomRow");
        source.Should().Contain("CreateDialogRangePickerButton(");
        source.Should().Contain("rangePicker.IsVisible = false");
        source.Should().Contain("rangePicker.IsTabStop = false");
        source.Should().Contain("rangeBox.KeyDown +=");
        source.Should().Contain("args.Key != Key.F4");
        source.Should().Contain("AttachDialogRangePicker(dialog, rangePicker, rangeBox, \"range.allow-edit-range.range\");");
        source.Should().NotContain("BuildDialogRangePickerRow(");
        source.Should().NotContain("VerticalScrollBarVisibility =");
    }

    private static string RepoFile(params string[] parts) =>
        Path.Combine([TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx"), .. parts]);
}
