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
        source.Should().Contain("var optionsHeader = new Button");
        source.Should().Contain("AutomationProperties.SetAutomationId(optionsHeader, \"FindReplaceOptionsExpander\")");
        source.Should().Contain("optionsContent.IsVisible = !optionsContent.IsVisible;");
        source.Should().Contain("? Fr(FindReplaceDialogText.OptionsExpanded)");
        source.Should().Contain(": Fr(FindReplaceDialogText.Options)");
        source.Should().Contain("FindReplaceDialogSchema.WithinChoices");
        source.Should().Contain("FindReplaceDialogSchema.SearchChoices");
        source.Should().Contain("FindReplaceDialogSchema.LookInChoices");
        source.Should().Contain("AutomationProperties.SetName(optionsHeader, optionsHeaderText.Text);");
        source.Should().Contain("private static ColumnDefinitions FindReplaceResultColumns()");
        source.Should().Contain("FindReplaceDialogPlanner.ResultBookColumnWidth");
        source.Should().Contain("RowDefinitions = new RowDefinitions(\"Auto,Auto,*,Auto,Auto\")");
        source.Should().Contain("resultsBorder.MinHeight = FindReplaceDialogPlanner.ResultsMinimumHeight");
        source.Should().Contain("Width = FindReplaceDialogPlanner.Width");
        source.Should().Contain("Height = FindReplaceDialogPlanner.Height");
        source.Should().NotContain("DockPanel.SetDock(resultsBorder");
    }
}
