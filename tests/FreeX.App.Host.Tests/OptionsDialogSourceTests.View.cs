using System.Windows.Controls;
using FreeX.App.Host;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class OptionsDialogSourceTests
{
    [Fact]
    public void OptionsDialog_ExposesPersistedViewOptions()
    {
        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("OptionsDialog.xaml");
        var source = DialogSourceTestSupport.ReadHostSources("OptionsDialog.xaml.cs");

        xaml.Should().Contain("<ListBoxItem Content=\"View\"/>");
        xaml.Should().Contain("x:Name=\"PanelView\"");
        xaml.Should().Contain("x:Name=\"OptShowFormulaBar\"");
        xaml.Should().Contain("x:Name=\"OptFormulaBarExpanded\"");
        source.Should().Contain("OptShowFormulaBar.IsChecked = _opts.ShowFormulaBar");
        source.Should().Contain("OptFormulaBarExpanded.IsChecked = _opts.FormulaBarExpanded");
        // R123: OkBtn_Click now computes each edited value into a local `editedX` before deciding
        // (against _opts) whether to apply it onto the freshly-reloaded `opts` -- see
        // FreeXOptionsDialogMultiWindowSaveTests -- so the assignment is no longer a plain
        // object-initializer line.
        source.Should().Contain("editedShowFormulaBar = OptShowFormulaBar.IsChecked == true");
        source.Should().Contain("editedFormulaBarExpanded = OptShowFormulaBar.IsChecked == true && OptFormulaBarExpanded.IsChecked == true");
    }

    [Fact]
    public void OptionsDialog_ShowFormulaBarToggleControlsExpandedState()
    {
        var xaml = DialogSourceTestSupport.ReadHostSources("OptionsDialog.xaml");
        var source = DialogSourceTestSupport.ReadHostSources("OptionsDialog.xaml.cs");

        xaml.Should().Contain("Checked=\"ShowFormulaBar_Changed\"");
        xaml.Should().Contain("Unchecked=\"ShowFormulaBar_Changed\"");
        source.Should().Contain("UpdateFormulaBarExpandedState();");
        source.Should().Contain("private void ShowFormulaBar_Changed(object sender, RoutedEventArgs e)");
        source.Should().Contain("private void UpdateFormulaBarExpandedState()");
        source.Should().Contain("OptFormulaBarExpanded.IsEnabled = OptShowFormulaBar.IsChecked == true;");
        source.Should().Contain("editedFormulaBarExpanded = OptShowFormulaBar.IsChecked == true && OptFormulaBarExpanded.IsChecked == true");
    }

    [Fact]
    public void OptionsDialog_RuntimeFormulaBarToggleControlsExpandedState()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new OptionsDialog(new FreeXOptions
            {
                ShowFormulaBar = false,
                FormulaBarExpanded = true
            });

            dialog.Show();
            try
            {
                var showFormulaBar = GetControl<CheckBox>(dialog, "OptShowFormulaBar");
                var expandedFormulaBar = GetControl<CheckBox>(dialog, "OptFormulaBarExpanded");

                expandedFormulaBar.IsChecked.Should().BeTrue();
                expandedFormulaBar.IsEnabled.Should().BeFalse();

                showFormulaBar.IsChecked = true;

                expandedFormulaBar.IsEnabled.Should().BeTrue();

                showFormulaBar.IsChecked = false;

                expandedFormulaBar.IsEnabled.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void OptionsDialog_AppliesWorksheetViewOptionsThroughUndoableCommand()
    {
        var backstageSource = DialogSourceTestSupport.ReadHostSources("MainWindow.Backstage.cs");
        var workbookUiSource = DialogSourceTestSupport.ReadHostSources("MainWindow.WorkbookUiState.cs");

        backstageSource.Should().Contain("ApplyOptionsWorksheetViewSettings()");
        backstageSource.Should().Contain("new SetWorksheetViewOptionsCommand(");
        workbookUiSource.Should().NotContain("currentSheet.ShowGridlines = _options.ShowGridlines");
        workbookUiSource.Should().NotContain("currentSheet.ShowHeadings = _options.ShowHeadings");
    }
}
