using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Freexcel.App.Host;
using Freexcel.Core.Model;
using FluentAssertions;

namespace Freexcel.App.Host.Tests;

public sealed class DataValidationDialogTests
{
    [Fact]
    public void DataValidationDialog_ContainsRangePickerButtonsForBothFormulaFields()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "DataValidationDialog.xaml"));

        xaml.Should().Contain("x:Name=\"UseSelectionButton\"");
        xaml.Should().Contain("x:Name=\"UseSelection2Button\"");
        xaml.Should().Contain("x:Name=\"SourcePickerButton\"");
        xaml.Should().Contain("x:Name=\"SourcePicker2Button\"");
        xaml.Should().Contain("Click=\"UseSelectionButton_Click\"");
        xaml.Should().Contain("Click=\"UseSelection2Button_Click\"");
        xaml.Should().Contain("Click=\"SourcePickerButton_Click\"");
        xaml.Should().Contain("Click=\"SourcePicker2Button_Click\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Select source range\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Select maximum range\"");
    }

    [Fact]
    public void DataValidationDialog_OperatorSelectionChangesRefreshFormulaLabelsAndVisibility()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new DataValidationDialog { SelectionSource = "=Sheet1!$B$2:$B$8" };
            dialog.Show();
            try
            {
                SelectComboItemByTag(GetControl<ComboBox>(dialog, "TypeCombo"), "WholeNumber");
                SelectComboItemByTag(GetControl<ComboBox>(dialog, "OperatorCombo"), "Between");

                GetControl<Label>(dialog, "Formula1Label").Content.Should().Be("_Minimum:");
                GetControl<Label>(dialog, "Formula2Label").Visibility.Should().Be(Visibility.Visible);
                GetControl<TextBox>(dialog, "Formula2Box").Visibility.Should().Be(Visibility.Visible);
                GetControl<Button>(dialog, "SourcePicker2Button").Visibility.Should().Be(Visibility.Visible);
                GetControl<Button>(dialog, "UseSelection2Button").Visibility.Should().Be(Visibility.Visible);

                SelectComboItemByTag(GetControl<ComboBox>(dialog, "OperatorCombo"), "Equal");

                GetControl<Label>(dialog, "Formula1Label").Content.Should().Be("_Value:");
                GetControl<Label>(dialog, "Formula2Label").Visibility.Should().Be(Visibility.Collapsed);
                GetControl<TextBox>(dialog, "Formula2Box").Visibility.Should().Be(Visibility.Collapsed);
                GetControl<Button>(dialog, "SourcePicker2Button").Visibility.Should().Be(Visibility.Collapsed);
                GetControl<Button>(dialog, "UseSelection2Button").Visibility.Should().Be(Visibility.Collapsed);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void DataValidationDialog_UsesExcelStyleSettingsInputAndErrorTabs()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "DataValidationDialog.xaml"));

        xaml.Should().Contain("<TabControl");
        xaml.Should().Contain("Header=\"_Settings\"");
        xaml.Should().Contain("Header=\"_Input Message\"");
        xaml.Should().Contain("Header=\"_Error Alert\"");
    }

    [Fact]
    public void DataValidationDialog_ExposesKeyboardAccessKeysForOptionsAndButtons()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "DataValidationDialog.xaml"));

        foreach (var content in new[]
        {
            "_Allow:",
            "_Data:",
            "_Minimum:",
            "Ma_ximum:",
            "Input _title:",
            "Input _message:",
            "_Alert style:",
            "Error _title:",
            "Error _message:",
            "_Use Selection",
            "Use _Selection",
            "_In-cell dropdown",
            "_Ignore blank",
            "Apply these changes to all other cells with the _same settings",
            "Show _input message when cell is selected",
            "Show error _alert after invalid data is entered",
            "C_lear All",
            "_OK",
            "_Cancel"
        })
            xaml.Should().Contain($"Content=\"{content}\"");
    }

    [Fact]
    public void DataValidationDialog_UsesExcelLikeSectionLabelsAndListSourcePicker()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "DataValidationDialog.xaml"));

        xaml.Should().Contain("Validation criteria");
        xaml.Should().Contain("When selecting cell, show this input message");
        xaml.Should().Contain("When user enters invalid data, show this error alert");
        xaml.Should().Contain("x:Name=\"SourcePickerButton\"");
        xaml.Should().Contain("AutomationProperties.Name=\"Select source range\"");
        xaml.Should().Contain("Click=\"SourcePickerButton_Click\"");
    }

    [Fact]
    public void DataValidationDialog_EditableCaptionsAreAccessKeyLabelsWithTargets()
    {
        var xaml = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "DataValidationDialog.xaml"));

        foreach (var expected in new[]
        {
            "<Label Grid.Row=\"1\" Grid.Column=\"0\" Content=\"_Allow:\" Target=\"{Binding ElementName=TypeCombo}\"",
            "<Label x:Name=\"OperatorLabel\" Grid.Row=\"2\" Grid.Column=\"0\" Content=\"_Data:\" Target=\"{Binding ElementName=OperatorCombo}\"",
            "<Label x:Name=\"Formula1Label\" Grid.Row=\"3\" Grid.Column=\"0\" Content=\"_Minimum:\" Target=\"{Binding ElementName=Formula1Box}\"",
            "<Label x:Name=\"Formula2Label\" Grid.Row=\"4\" Grid.Column=\"0\" Content=\"Ma_ximum:\" Target=\"{Binding ElementName=Formula2Box}\"",
            "<Label Grid.Row=\"1\" Grid.Column=\"0\" Content=\"Input _title:\" Target=\"{Binding ElementName=PromptTitleBox}\"",
            "<Label Grid.Row=\"2\" Grid.Column=\"0\" Content=\"Input _message:\" Target=\"{Binding ElementName=PromptMessageBox}\"",
            "<Label Grid.Row=\"1\" Grid.Column=\"0\" Content=\"_Alert style:\" Target=\"{Binding ElementName=AlertStyleCombo}\"",
            "<Label Grid.Row=\"2\" Grid.Column=\"0\" Content=\"Error _title:\" Target=\"{Binding ElementName=ErrorTitleBox}\"",
            "<Label Grid.Row=\"3\" Grid.Column=\"0\" Content=\"Error _message:\" Target=\"{Binding ElementName=ErrorMessageBox}\""
        })
            xaml.Should().Contain(expected);

        xaml.Should().NotContain("Text=\"Allow:\"");
        xaml.Should().NotContain("Text=\"Data:\"");
        xaml.Should().NotContain("Text=\"Minimum:\"");
        xaml.Should().NotContain("Text=\"Maximum:\"");
    }

    [Fact]
    public void DataValidationDialog_UpdatesDynamicCaptionContent()
    {
        var codeBehind = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "DataValidationDialog.xaml.cs"));

        codeBehind.Should().Contain("Formula1Label.Content = \"_Source:\"");
        codeBehind.Should().Contain("Formula1Label.Content = \"_Formula:\"");
        codeBehind.Should().Contain("Formula1Label.Content = (opTag == \"Between\" || opTag == \"NotBetween\") ? \"_Minimum:\" : \"_Value:\"");
        codeBehind.Should().NotContain("Formula1Label.Text =");
    }

    [Fact]
    public void MainWindow_AppliesDataValidationToMatchingSettingsWhenRequested()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "MainWindow.DataFilterCommands.cs"));

        source.Should().Contain("new DataValidationDialog(existingRule)");
        source.Should().Contain("dlg.ApplyToSameSettings");
        source.Should().Contain("HasSameDataValidationSettings");
        source.Should().Contain("CompositeWorkbookCommand(\"Data Validation\", commands)");
    }

    [Fact]
    public void DataValidationDialog_PrePopulatesExistingRuleAndPreservesIdentity()
    {
        StaTestRunner.Run(() =>
        {
            var id = Guid.NewGuid();
            var sheetId = SheetId.New();
            var existing = new DataValidation
            {
                Id = id,
                AppliesTo = new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 2, 2)),
                Type = DvType.List,
                Formula1 = "Red,Blue",
                AllowBlank = false,
                ShowDropdown = false,
                AlertStyle = DvAlertStyle.Warning,
                ShowInputMessage = false,
                ShowErrorMessage = true,
                ErrorTitle = "Bad choice",
                ErrorMessage = "Pick from the list.",
                PromptTitle = "Color",
                PromptMessage = "Choose a color."
            };

            var dialog = new DataValidationDialog(existing);
            dialog.Show();
            try
            {
                SelectedTag(GetControl<ComboBox>(dialog, "TypeCombo")).Should().Be("List");
                GetControl<TextBox>(dialog, "Formula1Box").Text.Should().Be("Red,Blue");
                GetControl<CheckBox>(dialog, "AllowBlankBox").IsChecked.Should().BeFalse();
                GetControl<CheckBox>(dialog, "ShowDropdownBox").IsChecked.Should().BeFalse();
                SelectedTag(GetControl<ComboBox>(dialog, "AlertStyleCombo")).Should().Be("Warning");
                GetControl<TextBox>(dialog, "ErrorTitleBox").Text.Should().Be("Bad choice");

                InvokePrivateAllowingNonModalDialogResult(dialog, "OkButton_Click");

                dialog.Result.Should().NotBeNull();
                dialog.Result!.Id.Should().Be(id);
                dialog.Result.Type.Should().Be(DvType.List);
                dialog.Result.Formula1.Should().Be("Red,Blue");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ClearAllButton_ResetsDialogWithoutClosing()
    {
        StaTestRunner.Run(() =>
        {
            var existing = new DataValidation
            {
                Type = DvType.WholeNumber,
                Operator = DvOperator.Between,
                Formula1 = "1",
                Formula2 = "10",
                AllowBlank = false
            };
            var dialog = new DataValidationDialog(existing);
            dialog.Show();
            try
            {
                InvokePrivate(dialog, "ClearAllButton_Click");

                dialog.IsVisible.Should().BeTrue();
                dialog.ClearRequested.Should().BeTrue();
                dialog.Result.Should().BeNull();
                SelectedTag(GetControl<ComboBox>(dialog, "TypeCombo")).Should().Be("Any");
                GetControl<TextBox>(dialog, "Formula1Box").Text.Should().BeEmpty();
                GetControl<CheckBox>(dialog, "AllowBlankBox").IsChecked.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void OkAfterClearAll_AppliesLaterEditsInsteadOfKeepingClearRequest()
    {
        StaTestRunner.Run(() =>
        {
            var existing = new DataValidation
            {
                Type = DvType.WholeNumber,
                Operator = DvOperator.Between,
                Formula1 = "1",
                Formula2 = "10"
            };
            var dialog = new DataValidationDialog(existing);
            dialog.Show();
            try
            {
                InvokePrivate(dialog, "ClearAllButton_Click");
                SelectComboItemByTag(GetControl<ComboBox>(dialog, "TypeCombo"), "List");
                GetControl<TextBox>(dialog, "Formula1Box").Text = "Red,Blue";

                InvokePrivateAllowingNonModalDialogResult(dialog, "OkButton_Click");

                dialog.ClearRequested.Should().BeFalse();
                dialog.Result.Should().NotBeNull();
                dialog.Result!.Type.Should().Be(DvType.List);
                dialog.Result.Formula1.Should().Be("Red,Blue");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void SourcePickerButton_PopulatesListSource()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new DataValidationDialog { SelectionSource = "=Sheet1!$B$2:$B$8" };
            dialog.Show();
            try
            {
                InvokePrivate(dialog, "SourcePickerButton_Click");

                GetControl<TextBox>(dialog, "Formula1Box").Text.Should().Be("=Sheet1!$B$2:$B$8");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void UseSelection2Button_PopulatesFormula2()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new DataValidationDialog { SelectionSource = "=Sheet1!$B$2:$B$8" };
            dialog.Show();
            try
            {
                InvokePrivate(dialog, "UseSelection2Button_Click");

                GetControl<TextBox>(dialog, "Formula2Box").Text.Should().Be("=Sheet1!$B$2:$B$8");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void SourcePicker2Button_PopulatesAndFocusesFormula2()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new DataValidationDialog { SelectionSource = "=Sheet1!$C$2:$C$8" };
            dialog.Show();
            try
            {
                SelectComboItemByTag(GetControl<ComboBox>(dialog, "TypeCombo"), "WholeNumber");
                SelectComboItemByTag(GetControl<ComboBox>(dialog, "OperatorCombo"), "Between");

                InvokePrivate(dialog, "SourcePicker2Button_Click");

                var formula2Box = GetControl<TextBox>(dialog, "Formula2Box");
                formula2Box.Text.Should().Be("=Sheet1!$C$2:$C$8");
                formula2Box.IsKeyboardFocusWithin.Should().BeTrue();
                formula2Box.SelectionLength.Should().Be(formula2Box.Text.Length);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    private static T GetControl<T>(DataValidationDialog dialog, string name)
        where T : class
    {
        var field = typeof(DataValidationDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

    private static void InvokePrivate(DataValidationDialog dialog, string methodName)
    {
        var method = typeof(DataValidationDialog).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        method!.Invoke(dialog, [dialog, new RoutedEventArgs()]);
    }

    private static void InvokePrivateAllowingNonModalDialogResult(DataValidationDialog dialog, string methodName)
    {
        var method = typeof(DataValidationDialog).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        try
        {
            method!.Invoke(dialog, [dialog, new RoutedEventArgs()]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException invalidOperation &&
                                                   invalidOperation.Message.Contains("DialogResult", StringComparison.Ordinal))
        {
        }
    }

    private static void SelectComboItemByTag(ComboBox comboBox, string tag)
    {
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .Single(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal));
    }

    private static string? SelectedTag(ComboBox comboBox) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag as string;
}
