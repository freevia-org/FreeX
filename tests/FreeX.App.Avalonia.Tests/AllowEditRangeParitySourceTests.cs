using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class AllowEditRangeParitySourceTests
{
    [Fact]
    public void ParityFixture_MatchesWpfSeedAndDefaultRange()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.ParityCapture.cs"));

        source.Should().Contain("new CellAddress(sheetId, 1, 1)");
        source.Should().Contain("new CellAddress(sheetId, 5, 5)");
        source.Should().Contain("new AllowEditRangeCommand(sheetId, existingRange)");
        source.Should().Contain("new CellAddress(sheetId, 2, 2)");
        source.Should().Contain("new CellAddress(sheetId, 5, 4)");
        source.Should().Contain("_session.SelectRange(new GridRange(");
    }

    [Fact]
    public void DialogVisualFlow_KeepsPickerPasswordAndWpfSizing()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.AllowEditRange.cs"));

        source.Should().Contain("Width = 430");
        source.Should().Contain("Height = 420");
        source.Should().Contain("CreateDialogRangePickerButton(");
        source.Should().Contain("initialRangeText ?? FormatRangeReference(_session.SelectedRange)");
        source.Should().Contain("AllowEditRangePasswordBox");
        source.Should().Contain("new StackPanel");
        source.Should().Contain("VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden");
        source.Should().Contain("existingRangesGroup");
        source.Should().Contain("rangeGroup");
        source.Should().Contain("bottomRow");
    }

    private static string RepoFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FreeX.slnx")))
            directory = directory.Parent;

        if (directory is null)
            throw new DirectoryNotFoundException("Could not find repository root containing FreeX.slnx.");

        return Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
    }
}
