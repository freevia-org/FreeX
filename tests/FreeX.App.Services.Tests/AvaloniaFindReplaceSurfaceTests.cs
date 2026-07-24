using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class AvaloniaFindReplaceSurfaceTests
{
    [Fact]
    public void FormatPicker_PreservesExistingCriterionWhenSelectionIsCancelled()
    {
        var source = File.ReadAllText(RepositoryFileLocator.Find("src", "FreeX.App.Avalonia", "MainWindow.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        source.Should().Contain("async Task<StyleDiff?> PickFindReplaceFormatAsync(StyleDiff? existingFormat)");
        source.Should().Contain("if (selection is null)\n                return existingFormat;");
        source.Should().Contain("var selectedFormat = await PickFindReplaceFormatAsync(findFormat);");
        source.Should().Contain("if (selectedFormat is not null)");
        source.Should().Contain("var optionsControls = CreateFindOptionsControls(\"FindReplace\", defaultLookInIndex: 0);");

        source.Should().Contain("var findFormatButton = CreateFindReplaceFormatButton(\"FindReplaceFindFormatButton\"");
        source.Should().Contain("var findChooseFormatButton = CreateFindReplaceFormatButton(\"FindReplaceFindChooseFormatFromCellButton\"");
        source.Should().Contain("var replaceFindFormatButton = CreateFindReplaceFormatButton(\"FindReplaceReplaceFindFormatButton\"");
        source.Should().Contain("var replaceFindChooseFormatButton = CreateFindReplaceFormatButton(\"FindReplaceReplaceFindChooseFormatFromCellButton\"");
        source.Should().Contain("var replaceWithFormatButton = CreateFindReplaceFormatButton(\"FindReplaceReplaceWithFormatButton\"");
        source.Should().Contain("var replaceWithChooseFormatButton = CreateFindReplaceFormatButton(\"FindReplaceReplaceWithChooseFormatFromCellButton\"");
        source.Should().Contain("Header = Fr(\"FindReplace_Options\", \"Options >>\")");
        source.Should().Contain("IsExpanded = false");
        source.Should().Contain("ColumnDefinitions = new ColumnDefinitions(\"110,100,90,70,*,*\")");
        source.Should().Contain("RowDefinitions = new RowDefinitions(\"Auto,Auto,*,Auto,Auto\")");
        source.Should().Contain("resultsBorder.MinHeight = 120");
        source.Should().Contain("Width = FindReplaceDialogPlanner.Width");
        source.Should().Contain("Height = FindReplaceDialogPlanner.Height");
        source.Should().NotContain("DockPanel.SetDock(resultsBorder");
    }
}
