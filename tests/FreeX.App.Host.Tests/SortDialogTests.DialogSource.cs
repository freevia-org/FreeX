using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class SortDialogTests
{
    [Fact]
    public void DialogCommands_ExposeKeyboardAccessKeys()
    {
        var source = DialogSourceTestSupport.ReadHostSources("SortDialog.cs");

        foreach (var content in new[]
        {
            "Content = UiText.Get(\"Sort_AddLevel\")",
            "Content = UiText.Get(\"Sort_DeleteLevel\")",
            "Content = UiText.Get(\"Sort_CopyLevel\")",
            "Content = UiText.Get(\"Sort_MoveUp\")",
            "Content = UiText.Get(\"Sort_MoveDown\")",
            "Content = UiText.Get(\"Sort_Options\")",
            "Content = UiText.Ok",
            "Content = UiText.Cancel"
        })
            source.Should().Contain(content);
    }

    [Fact]
    public void DialogLayout_ExposesExcelCustomSortFields()
    {
        var source = ReadSortDialogSource();

        source.Should().Contain("UiText.Get(\"Sort_MyDataHasHeaders\")");
        source.Should().Contain("IsChecked = hasHeaders");
        source.Should().Contain("ResultHasHeaders");
        source.Should().Contain("UiText.Get(\"Sort_SortLevels\")");
        source.Should().Contain("Header = UiText.Get(\"Sort_SortBy\")");
        source.Should().Contain("Header = UiText.Get(\"Sort_SortOn\")");
        source.Should().Contain("Header = UiText.Get(\"Sort_Order\")");
        source.Should().Contain("Header = UiText.Get(\"Sort_Color\")");
        source.Should().Contain("UiText.Get(\"Sort_SortOnCellValues\")");
        source.Should().Contain("UiText.Get(\"Sort_SortOnCellColor\")");
        source.Should().Contain("UiText.Get(\"Common_FontColor\")");
        source.Should().Contain("UiText.Get(\"Sort_OrderOnTop\")");
        source.Should().Contain("UiText.Get(\"Sort_OrderOnBottom\")");
        source.Should().Contain("CreateOrderColumn");
        source.Should().Contain("BuildColorChoices");
        source.Should().Contain("UpdateColumnChoices");
        source.Should().Contain("SortOptionsDialog");
    }

    [Fact]
    public void DialogOpenedFromKeyboard_FocusesFirstSortLevel()
    {
        var source = ReadSortDialogSource();

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_levelsGrid.SelectedIndex = 0;");
        source.Should().Contain("_levelsGrid.Focus();");
        source.Should().Contain("Keyboard.Focus(_levelsGrid);");
    }

    [Fact]
    public void SortLevelsGrid_DeletesSelectedLevelWithDeleteKey()
    {
        var source = ReadSortDialogSource();

        source.Should().Contain("_levelsGrid.KeyDown += LevelsGrid_KeyDown;");
        source.Should().Contain("private void LevelsGrid_KeyDown(object sender, KeyEventArgs e)");
        source.Should().Contain("e.Key == Key.Delete");
        source.Should().Contain("_deleteLevelButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));");
    }
}
