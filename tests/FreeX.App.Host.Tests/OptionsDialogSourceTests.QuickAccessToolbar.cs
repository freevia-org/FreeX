using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using FreeX.App.Host;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class OptionsDialogSourceTests
{
    [Fact]

    public void OptionsDialog_ExposesPersistedQuickAccessToolbarCustomization()

    {

        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("OptionsDialog.xaml");

        var source = DialogSourceTestSupport.ReadHostSources("OptionsDialog.xaml.cs");



        xaml.Should().Contain("<ListBoxItem Content=\"Quick Access Toolbar\"/>");

        xaml.Should().Contain("x:Name=\"PanelQuickAccessToolbar\"");

        xaml.Should().Contain("Customize the Quick Access Toolbar");

        xaml.Should().Contain("Show Quick Access Toolbar _below the Ribbon");

        xaml.Should().Contain("x:Name=\"QuickAccessSearchBox\"");

        xaml.Should().Contain("AutomationProperties.AutomationId=\"QuickAccessToolbarCommandSearchBox\"");

        xaml.Should().Contain("TextChanged=\"QuickAccessSearchBox_TextChanged\"");

        xaml.Should().Contain("x:Name=\"QuickAccessAvailableCommandsList\"");

        xaml.Should().Contain("KeyDown=\"QuickAccessAvailableCommandsList_KeyDown\"");

        xaml.Should().Contain("x:Name=\"QuickAccessSelectedCommandsList\"");

        xaml.Should().Contain("KeyDown=\"QuickAccessSelectedCommandsList_KeyDown\"");

        xaml.Should().Contain("x:Name=\"QuickAccessAddButton\"");

        xaml.Should().Contain("x:Name=\"QuickAccessRemoveButton\"");

        xaml.Should().Contain("x:Name=\"QuickAccessMoveUpButton\"");

        xaml.Should().Contain("x:Name=\"QuickAccessMoveDownButton\"");

        xaml.Should().Contain("x:Name=\"QuickAccessResetButton\"");

        xaml.Should().Contain("x:Name=\"QuickAccessImportExportButton\"");

        xaml.Should().Contain("AutomationProperties.AutomationId=\"QuickAccessToolbarImportExportButton\"");

        xaml.Should().Contain("Click=\"QuickAccessResetButton_Click\"");

        xaml.Should().Contain("Click=\"QuickAccessImportExportButton_Click\"");

        xaml.Should().Contain("MouseDoubleClick=\"QuickAccessAvailableCommandsList_MouseDoubleClick\"");

        xaml.Should().Contain("MouseDoubleClick=\"QuickAccessSelectedCommandsList_MouseDoubleClick\"");



        source.Should().Contain("PanelQuickAccessToolbar.Visibility = selectedIndex == 8 ? Visibility.Visible : Visibility.Collapsed;");

        source.Should().Contain("QuickAccessToolbarCatalog.NormalizeCommandIds(_opts.QuickAccessToolbarCommands)");

        source.Should().Contain("QuickAccessToolbarBelowRibbon = QuickAccessBelowRibbonCheckBox.IsChecked == true");

        source.Should().Contain("QuickAccessToolbarCommands = QuickAccessToolbarCatalog.NormalizeCommandIds(_quickAccessCommandIds).ToList()");

        source.Should().Contain("QuickAccessToolbarCustomizationPlanner.FilterAvailable(");
        source.Should().Contain("QuickAccessToolbarCustomizationPlanner.Apply(");
        source.Should().Contain("QuickAccessToolbarCustomizationPlanner.Move(");
        source.Should().Contain("QuickAccessToolbarCustomizationPlanner.Reset()");
        source.Should().NotContain("QuickAccessCommandMatchesFilter");

        source.Should().Contain("QuickAccessSearchBox_TextChanged");

        source.Should().Contain("private void QuickAccessAvailableCommandsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)");

        source.Should().Contain("private void QuickAccessSelectedCommandsList_MouseDoubleClick(object sender, MouseButtonEventArgs e)");

        source.Should().Contain("QuickAccessAddButton_Click(sender, e);");

        source.Should().Contain("QuickAccessRemoveButton_Click(sender, e);");

        source.Should().Contain("private void QuickAccessAvailableCommandsList_KeyDown(object sender, KeyEventArgs e)");

        source.Should().Contain("private bool TryHandleQuickAccessAvailableCommandsListKey(Key key)");

        source.Should().Contain("private void QuickAccessSelectedCommandsList_KeyDown(object sender, KeyEventArgs e)");

        source.Should().Contain("private bool TryHandleQuickAccessSelectedCommandsListKey(Key key, ModifierKeys modifiers)");

        source.Should().Contain("key is not (Key.Enter or Key.Return)");

        source.Should().Contain("key is not (Key.Delete or Key.Back)");

        source.Should().Contain("(modifiers & ModifierKeys.Control) == ModifierKeys.Control");

        source.Should().Contain("e.Handled = true;");

        source.Should().Contain("QuickAccessToolbarCustomizationFile.TryLoad(pickerResult.FileName!)");

        source.Should().Contain("QuickAccessToolbarCustomizationFile.TrySave(");

        source.Should().Contain("WpfFileDialogService.ShowOpenDialog(");

        source.Should().Contain("WpfFileDialogService.ShowSaveDialog(");

        source.Should().Contain("QuickAccessToolbarCustomizationFile.DialogFilter");
        source.Should().Contain("QuickAccessToolbarCustomizationFile.ImportMenuHeader");
        source.Should().Contain("QuickAccessToolbarCustomizationFile.ExportMenuHeader");

        source.Should().Contain("QuickAccessToolbarCustomizationFile.DefaultExtension");

        source.Should().NotContain("new OpenFileDialog");

        source.Should().NotContain("new SaveFileDialog");

        source.Should().Contain("QuickAccessToolbarImportCustomizationMenuItem");

        source.Should().Contain("QuickAccessToolbarExportCustomizationMenuItem");

        source.Should().NotContain("DeferredCommandMessages.QuickAccessToolbarReset()");

    }



    [Fact]

    public void OptionsDialog_ExposesStableQuickAccessToolbarAutomationMetadata()

    {

        var xaml = XamlLocalizationTestHelper.ReadLocalizedXaml("OptionsDialog.xaml");



        foreach (var expected in new[]

        {

            "AutomationProperties.AutomationId=\"QuickAccessToolbarBelowRibbonCheckBox\"",

            "AutomationProperties.HelpText=\"Show the Quick Access Toolbar below the ribbon instead of above it.\"",

            "AutomationProperties.AutomationId=\"QuickAccessToolbarCommandSearchBox\"",

            "AutomationProperties.HelpText=\"Filter available Quick Access Toolbar commands.\"",

            "AutomationProperties.Name=\"Available commands\"",

            "AutomationProperties.AutomationId=\"QuickAccessToolbarAvailableCommandsList\"",

            "AutomationProperties.HelpText=\"Commands that can be added to the Quick Access Toolbar. Press Enter or double-click to add the selected command.\"",

            "AutomationProperties.AutomationId=\"QuickAccessToolbarAddCommandButton\"",

            "AutomationProperties.HelpText=\"Add the selected command to the Quick Access Toolbar.\"",

            "AutomationProperties.AutomationId=\"QuickAccessToolbarRemoveCommandButton\"",

            "AutomationProperties.HelpText=\"Remove the selected command from the Quick Access Toolbar.\"",

            "AutomationProperties.Name=\"Quick Access Toolbar commands\"",

            "AutomationProperties.AutomationId=\"QuickAccessToolbarSelectedCommandsList\"",

            "AutomationProperties.HelpText=\"Commands currently shown on the Quick Access Toolbar. Press Delete to remove or Ctrl+Up/Ctrl+Down to reorder.\"",

            "AutomationProperties.AutomationId=\"QuickAccessToolbarMoveUpButton\"",

            "AutomationProperties.HelpText=\"Move the selected Quick Access Toolbar command up.\"",

            "AutomationProperties.AutomationId=\"QuickAccessToolbarMoveDownButton\"",

            "AutomationProperties.HelpText=\"Move the selected Quick Access Toolbar command down.\"",

            "AutomationProperties.AutomationId=\"QuickAccessToolbarResetButton\"",

            "AutomationProperties.HelpText=\"Restore the default Quick Access Toolbar commands.\"",

            "AutomationProperties.AutomationId=\"QuickAccessToolbarImportExportButton\"",

            "AutomationProperties.HelpText=\"Import or export FreeX Quick Access Toolbar customization.\""

        })

        {

            xaml.Should().Contain(expected);

        }

    }



    [Fact]

    public void OptionsDialog_RuntimeQuickAccessToolbarSearchFiltersAvailableCommandsAndKeepsAddFlow()

    {

        StaTestRunner.Run(() =>

        {

            var dialog = new OptionsDialog(new AppOptions());

            dialog.Show();

            try

            {

                var tabList = GetControl<ListBox>(dialog, "TabList");

                var searchBox = GetControl<TextBox>(dialog, "QuickAccessSearchBox");

                var availableList = GetControl<ListBox>(dialog, "QuickAccessAvailableCommandsList");

                var selectedList = GetControl<ListBox>(dialog, "QuickAccessSelectedCommandsList");

                var addButton = GetControl<Button>(dialog, "QuickAccessAddButton");



                tabList.SelectedIndex = 8;

                availableList.Items.Count.Should().BeGreaterThan(1);



                searchBox.Text = "bold";



                GetListDisplayNames(availableList).Should().Equal("Bold");

                addButton.IsEnabled.Should().BeFalse();



                availableList.SelectedIndex = 0;

                addButton.IsEnabled.Should().BeTrue();



                DialogSourceTestSupport.ClickButton(addButton);



                GetListDisplayNames(availableList).Should().BeEmpty();

                GetListDisplayNames(selectedList).Should().Contain("Bold");

                addButton.IsEnabled.Should().BeFalse();

            }

            finally

            {

                dialog.Close();

            }

        });

    }



    [Fact]

    public void OptionsDialog_RuntimeQuickAccessToolbarKeyboardAddsRemovesAndReordersCommands()

    {

        StaTestRunner.Run(() =>

        {

            var dialog = new OptionsDialog(new AppOptions());

            dialog.Show();

            try

            {

                var tabList = GetControl<ListBox>(dialog, "TabList");

                var searchBox = GetControl<TextBox>(dialog, "QuickAccessSearchBox");

                var availableList = GetControl<ListBox>(dialog, "QuickAccessAvailableCommandsList");

                var selectedList = GetControl<ListBox>(dialog, "QuickAccessSelectedCommandsList");



                tabList.SelectedIndex = 8;

                searchBox.Text = "bold";

                availableList.SelectedIndex = 0;



                var addKey = CreateKeyDownEvent(dialog, Key.Return);

                availableList.RaiseEvent(addKey);



                addKey.Handled.Should().BeTrue();

                GetListDisplayNames(availableList).Should().BeEmpty();

                GetListDisplayNames(selectedList).Should().Contain("Bold");



                selectedList.SelectedItem = selectedList.Items

                    .Cast<object>()

                    .First(item => string.Equals(GetListDisplayName(item), "Bold", StringComparison.Ordinal));



                InvokeQuickAccessSelectedKeyHandler(dialog, Key.Up, ModifierKeys.Control).Should().BeTrue();

                GetListDisplayNames(selectedList).Should().Equal("Save", "Undo", "Bold", "Redo");



                InvokeQuickAccessSelectedKeyHandler(dialog, Key.Down, ModifierKeys.Control).Should().BeTrue();

                GetListDisplayNames(selectedList).Should().Equal("Save", "Undo", "Redo", "Bold");



                InvokeQuickAccessSelectedKeyHandler(dialog, Key.Delete, ModifierKeys.None).Should().BeTrue();

                GetListDisplayNames(availableList).Should().Equal("Bold");

                GetListDisplayNames(selectedList).Should().NotContain("Bold");

            }

            finally

            {

                dialog.Close();

            }

        });

    }



    [Fact]

    public void OptionsDialog_RuntimeQuickAccessToolbarDoubleClickAddsAndRemovesSelectedCommand()

    {

        StaTestRunner.Run(() =>

        {

            var dialog = new OptionsDialog(new AppOptions());

            dialog.Show();

            try

            {

                var tabList = GetControl<ListBox>(dialog, "TabList");

                var searchBox = GetControl<TextBox>(dialog, "QuickAccessSearchBox");

                var availableList = GetControl<ListBox>(dialog, "QuickAccessAvailableCommandsList");

                var selectedList = GetControl<ListBox>(dialog, "QuickAccessSelectedCommandsList");



                tabList.SelectedIndex = 8;

                searchBox.Text = "bold";

                availableList.SelectedIndex = 0;



                var addDoubleClick = CreateMouseDoubleClickEvent();

                availableList.RaiseEvent(addDoubleClick);



                addDoubleClick.Handled.Should().BeTrue();

                GetListDisplayNames(availableList).Should().BeEmpty();

                GetListDisplayNames(selectedList).Should().Contain("Bold");



                selectedList.SelectedItem = selectedList.Items

                    .Cast<object>()

                    .First(item => string.Equals(GetListDisplayName(item), "Bold", StringComparison.Ordinal));



                var removeDoubleClick = CreateMouseDoubleClickEvent();

                selectedList.RaiseEvent(removeDoubleClick);



                removeDoubleClick.Handled.Should().BeTrue();

                GetListDisplayNames(availableList).Should().Equal("Bold");

                GetListDisplayNames(selectedList).Should().NotContain("Bold");

            }

            finally

            {

                dialog.Close();

            }

        });

    }
}
