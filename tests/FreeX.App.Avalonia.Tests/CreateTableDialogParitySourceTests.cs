using FluentAssertions;

namespace FreeX.App.Avalonia.Tests;

public sealed class CreateTableDialogParitySourceTests
{
    [Fact]
    public void WpfAndAvaloniaConsumeTheSharedCreateTableContract()
    {
        var planner = ReadSource("src", "FreeX.App.Services", "CreateTableDialogPlanner.cs");
        var wpf = ReadSource("src", "FreeX.App.Host", "CreateTableDialog.cs");
        var avalonia = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.InsertObjects.cs");

        planner.Should().Contain("CreateTableInputParser.TryParse(");
        wpf.Should().Contain("CreateTableDialogPlanner.DefaultFirstRowHasHeaders");
        wpf.Should().Contain("CreateTableDialogPlanner.ContentMargin");
        avalonia.Should().Contain("CreateTableDialogPlanner.DefaultFirstRowHasHeaders");
        avalonia.Should().Contain("CreateTableDialogPlanner.RangePickerGap");
        avalonia.Should().Contain("Children = { rangePicker, rangeBox }");
    }

    [Fact]
    public void AvaloniaCreateTablePreservesWpfKeyboardAndPointingSemantics()
    {
        var source = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.InsertObjects.cs");

        source.Should().Contain("IsDefault = true");
        source.Should().Contain("IsCancel = true");
        source.Should().Contain("ConfigureDialogCancelOnEscape(dialog, cancelButton);");
        source.Should().Contain("await AvaloniaUserMessageDialog.ShowWarningAsync(");
        source.Should().Contain("rangeBox.Focus();");
        source.Should().Contain("rangeBox.SelectAll();");
        source.Should().Contain("AttachDialogRangePicker(dialog, rangePicker, rangeBox, \"range.create-table.range\");");
    }

    [Fact]
    public void AvaloniaProductionCallerUsesStyledTableCommandLikeWpf()
    {
        var source = ReadSource("src", "FreeX.App.Avalonia", "MainWindow.InsertObjects.cs");

        source.Should().Contain("TableStyleGalleryPlanner.GetOption(0, _session.Workbook.Theme)");
        source.Should().Contain("TableCreationPlanner.BuildStyledCommand(");
        source.Should().Contain("defaultStyle.Banding");
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(TestWorkspaceFileLocator.FindFileFromBaseDirectory(parts));
}
