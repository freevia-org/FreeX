using System.IO;
using System.Windows.Controls;
using System.Xml.Linq;
using FreeX.App.Host;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class OptionsDialogSourceTests
{
    [Fact]
    public void OptionsDialog_AdvancedConsumesSharedFrameAndRowMetrics()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("OptionsDialog.xaml");
        var source = DialogSourceTestSupport.ReadHostSources("OptionsDialog.xaml.cs");

        xaml.Should().Contain("x:Name=\"OptionsCategoryColumn\"");
        xaml.Should().Contain("x:Name=\"OptionsContentScrollViewer\"");
        xaml.Should().Contain("x:Name=\"OptionsFooterBorder\"");
        xaml.Should().Contain("x:Name=\"AdvancedDirectionGrid\"");
        xaml.Should().Contain("x:Name=\"AdvancedObjectsGrid\"");
        source.Should().Contain("private void ApplySharedOptionsLayoutMetrics()");
        source.Should().Contain("OptionsDialogPlanner.CategoryColumnWidth");
        source.Should().Contain("OptionsDialogPlanner.FooterHeight");
        source.Should().Contain("OptionsDialogPlanner.AdvancedDirectionLabelWidth");
        source.Should().Contain("OptionsDialogPlanner.AdvancedObjectsControlWidth");
    }

    [Fact]
    public void OptionsDialog_AdvancedInteractiveControlsRemainEnabled()
    {
        var xaml = XamlLocalizationTestHelper.LoadLocalizedXaml("OptionsDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";

        var autoComplete = xaml.Descendants(presentation + "CheckBox")
            .Single(element => element.Attribute(xamlNamespace + "Name")?.Value == "OptAdvancedAutoComplete");
        autoComplete.Attribute("IsEnabled").Should().BeNull();

        var objectsDisplay = xaml.Descendants(presentation + "ComboBox")
            .Single(element => element.Attribute(xamlNamespace + "Name")?.Value == "OptObjectsDisplay");
        objectsDisplay.Attribute("IsEnabled").Should().BeNull();
    }

    // R83-app-flashfill-autocomplete-5-2: "Enable AutoComplete for cell values" used to be shown
    // permanently checked AND disabled -- an unconditional claim of an active feature with no way
    // to turn it off and no feature behind it at all. It is now a real, persisted, user-togglable
    // option (see CellValueAutoCompleteSuggester for the feature it now actually gates).
    [Fact]
    public void OptionsDialog_AutoCompleteForCellValuesIsAGenuineTogglableOption()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new OptionsDialog(new FreeXOptions());
            dialog.Show();
            try
            {
                GetControl<CheckBox>(dialog, "OptAdvancedAutoComplete").IsEnabled.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void OptionsDialog_FillHandleAndCellDragAndDropIsGenuineTogglableOption()
    {
        var xaml = XamlLocalizationTestHelper.LoadLocalizedXaml("OptionsDialog.xaml");
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
        var fillHandle = xaml.Descendants(presentation + "CheckBox")
            .Single(element => element.Attribute(xamlNamespace + "Name")?.Value == "OptAdvancedFillHandle");

        fillHandle.Attribute("IsEnabled").Should().BeNull();
        // R123: computed into `editedFillHandle` and applied onto the freshly-reloaded `opts`
        // only when changed from _opts -- see FreeXOptionsDialogMultiWindowSaveTests.
        DialogSourceTestSupport.ReadHostSources("OptionsDialog.xaml.cs")
            .Should().Contain("OptAdvancedFillHandle.IsChecked = _opts.EnableFillHandleAndCellDragAndDrop")
            .And.Contain("editedFillHandle = OptAdvancedFillHandle.IsChecked == true");
    }

    [Fact]
    public void OptionsDialog_RoundTripsAutoCompleteForCellValuesOption()
    {
        using var temp = new TestTemporaryDirectory();
        var path = Path.Combine(temp.Path, "options.json");
        using var optionsPath = TestEnvironmentVariableScope.Set(FreeXOptions.OptionsPathEnvironmentVariable, path);

        StaTestRunner.Run(() =>
        {
            var dialog = new OptionsDialog(new FreeXOptions { EnableAutoCompleteForCellValues = true });
            dialog.Show();
            try
            {
                var autoComplete = GetControl<CheckBox>(dialog, "OptAdvancedAutoComplete");
                autoComplete.IsChecked.Should().BeTrue();

                autoComplete.IsChecked = false;

                ClickOkAllowingNonModalDialogResult(dialog);

                dialog.Result.EnableAutoCompleteForCellValues.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });

        var reloaded = FreeXOptions.LoadFromPath(path);
        reloaded.EnableAutoCompleteForCellValues.Should().BeFalse();
    }

    [Fact]
    public void MainWindowEditing_WiresAutoCompleteOptionToCellValueSuggestions()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Editing.cs");

        source.Should().Contain("_options.EnableAutoCompleteForCellValues");
        source.Should().Contain("CellValueAutoCompleteSuggester.CollectContiguousColumnTextEntries");
        source.Should().Contain("CellValueAutoCompleteSuggester.Suggest(candidates, text)");
    }

    [Fact]
    public void OptionsDialog_ExposesExcelLikeAdvancedAndDisplayAffordances()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("OptionsDialog.xaml");
        var source = DialogSourceTestSupport.ReadHostSources("OptionsDialog.xaml.cs");

        xaml.Should().Contain("x:Name=\"PanelAdvanced\"");
        xaml.Should().Contain("Editing options");
        xaml.Should().Contain("Display options for this workbook");
        xaml.Should().Contain("x:Name=\"OptAfterEnterDirection\"");
        xaml.Should().Contain("x:Name=\"OptMoveAfterEnter\"");
        xaml.Should().Contain("x:Name=\"OptShowGridlines\"");
        xaml.Should().Contain("x:Name=\"OptShowHeadings\"");
        xaml.Should().Contain("x:Name=\"OptObjectsDisplay\"");
        xaml.Should().Contain("x:Name=\"PanelTrustCenter\"");
        xaml.Should().Contain("Trust Center _Settings...");
        foreach (var clickHandler in new[]
        {
            "AutoCorrectOptionsButton_Click",
            "RibbonImportExportButton_Click",
            "QuickAccessResetButton_Click",
            "QuickAccessAddButton_Click",
            "QuickAccessRemoveButton_Click",
            "QuickAccessMoveUpButton_Click",
            "QuickAccessMoveDownButton_Click",
            "QuickAccessImportExportButton_Click",
            "AddInsGoButton_Click",
            "TrustCenterSettingsButton_Click"
        })
            xaml.Should().Contain($"Click=\"{clickHandler}\"");

        source.Should().Contain("PanelAdvanced.Visibility");
        source.Should().Contain("OptAfterEnterDirection.ItemsSource");
        source.Should().Contain("OptMoveAfterEnter.IsChecked = _opts.MoveSelectionAfterEnter");
        source.Should().Contain("ShowGridlines = OptShowGridlines.IsChecked == true");
        source.Should().Contain("ShowHeadings = OptShowHeadings.IsChecked == true");
        source.Should().Contain("ObjectsDisplay = OptObjectsDisplay.SelectedIndex switch");
        source.Should().Contain("OptObjectsDisplay.ItemsSource");
        source.Should().Contain("ShowDeferredOptionsMessage");
        source.Should().Contain("DeferredCommandMessages.AutoCorrectOptions()");
        source.Should().Contain("DeferredCommandMessages.RibbonCustomizationImportExport()");
        source.Should().Contain("DeferredCommandMessages.OfficeAddIns()");
        source.Should().Contain("DeferredCommandMessages.TrustCenterSettings()");
    }

    [Fact]
    public void OptionsDialog_DisablesVisibleCheckboxesThatAreNotPersistedOptions()
    {
        var document = XamlLocalizationTestHelper.LoadLocalizedXaml("OptionsDialog.xaml");

        foreach (var name in DeferredOptionCheckboxNames)
            AssertNamedCheckBoxDisabled(document, name);
    }

    [Fact]
    public void OptionsDialog_RuntimeDeferredOptionCheckboxesAreReadOnly()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new OptionsDialog(new FreeXOptions());
            dialog.Show();
            try
            {
                foreach (var name in DeferredOptionCheckboxNames)
                    GetControl<CheckBox>(dialog, name).IsEnabled.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void OptionsDialog_MoveAfterEnterToggleControlsDirectionEnabledState()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("OptionsDialog.xaml");
        var source = DialogSourceTestSupport.ReadHostSources("OptionsDialog.xaml.cs");

        xaml.Should().Contain("Checked=\"MoveAfterEnter_Changed\"");
        xaml.Should().Contain("Unchecked=\"MoveAfterEnter_Changed\"");
        source.Should().Contain("UpdateAfterEnterDirectionState();");
        source.Should().Contain("private void MoveAfterEnter_Changed(object sender, RoutedEventArgs e)");
        source.Should().Contain("private void UpdateAfterEnterDirectionState()");
        source.Should().Contain("OptAfterEnterDirection.IsEnabled = OptMoveAfterEnter.IsChecked == true;");
    }

    [Fact]
    public void Viewport_MapsObjectPlaceholderOptionToGridDisplayMode()
    {
        var source = DialogSourceTestSupport.ReadHostSources("MainWindow.Viewport.cs");

        source.Should().Contain("SheetGrid.ObjectDisplayMode = _options.ObjectsDisplay switch");
        source.Should().Contain("FreeXObjectDisplay.Placeholders => FreeX.App.UI.GridObjectDisplayMode.Placeholders");
        source.Should().Contain("FreeXObjectDisplay.Nothing => FreeX.App.UI.GridObjectDisplayMode.Nothing");
        source.Should().Contain("var keepObjectData = _options.ObjectsDisplay != FreeXObjectDisplay.Nothing");
    }
}
