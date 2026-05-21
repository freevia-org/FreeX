using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Freexcel.App.Host;
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
        xaml.Should().Contain("Click=\"UseSelectionButton_Click\"");
        xaml.Should().Contain("Click=\"UseSelection2Button_Click\"");
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
                GetControl<Button>(dialog, "UseSelection2Button").Visibility.Should().Be(Visibility.Visible);

                SelectComboItemByTag(GetControl<ComboBox>(dialog, "OperatorCombo"), "Equal");

                GetControl<Label>(dialog, "Formula1Label").Content.Should().Be("_Value:");
                GetControl<Label>(dialog, "Formula2Label").Visibility.Should().Be(Visibility.Collapsed);
                GetControl<TextBox>(dialog, "Formula2Box").Visibility.Should().Be(Visibility.Collapsed);
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

    private static void SelectComboItemByTag(ComboBox comboBox, string tag)
    {
        comboBox.SelectedItem = comboBox.Items
            .OfType<ComboBoxItem>()
            .Single(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal));
    }
}
