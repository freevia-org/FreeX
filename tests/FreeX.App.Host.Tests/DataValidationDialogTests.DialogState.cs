using System.Windows;
using System.Windows.Controls;
using FreeX.App.Host;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class DataValidationDialogTests
{
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
    public void DataValidationDialog_ShowsValueRangePickerForNonListValidationTypes()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new DataValidationDialog();
            dialog.Show();
            try
            {
                SelectComboItemByTag(GetControl<ComboBox>(dialog, "TypeCombo"), "Any");
                GetControl<Button>(dialog, "SourcePickerButton").Visibility.Should().Be(Visibility.Collapsed);

                SelectComboItemByTag(GetControl<ComboBox>(dialog, "TypeCombo"), "WholeNumber");
                GetControl<Button>(dialog, "SourcePickerButton").Visibility.Should().Be(Visibility.Visible);
                GetControl<Button>(dialog, "UseSelectionButton").Visibility.Should().Be(Visibility.Collapsed);

                SelectComboItemByTag(GetControl<ComboBox>(dialog, "TypeCombo"), "Custom");
                GetControl<Button>(dialog, "SourcePickerButton").Visibility.Should().Be(Visibility.Visible);
                GetControl<Button>(dialog, "UseSelectionButton").Visibility.Should().Be(Visibility.Collapsed);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void InputMessageToggle_DisablesPromptEditors()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new DataValidationDialog();
            dialog.Show();
            try
            {
                var showInputMessage = GetControl<CheckBox>(dialog, "ShowInputMessageBox");
                var promptTitle = GetControl<TextBox>(dialog, "PromptTitleBox");
                var promptMessage = GetControl<TextBox>(dialog, "PromptMessageBox");

                promptTitle.IsEnabled.Should().BeTrue();
                promptMessage.IsEnabled.Should().BeTrue();

                showInputMessage.IsChecked = false;

                promptTitle.IsEnabled.Should().BeFalse();
                promptMessage.IsEnabled.Should().BeFalse();

                showInputMessage.IsChecked = true;

                promptTitle.IsEnabled.Should().BeTrue();
                promptMessage.IsEnabled.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ErrorAlertToggle_DisablesAlertEditors()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new DataValidationDialog();
            dialog.Show();
            try
            {
                var showErrorMessage = GetControl<CheckBox>(dialog, "ShowErrorMessageBox");
                var alertStyle = GetControl<ComboBox>(dialog, "AlertStyleCombo");
                var errorTitle = GetControl<TextBox>(dialog, "ErrorTitleBox");
                var errorMessage = GetControl<TextBox>(dialog, "ErrorMessageBox");

                alertStyle.IsEnabled.Should().BeTrue();
                errorTitle.IsEnabled.Should().BeTrue();
                errorMessage.IsEnabled.Should().BeTrue();

                showErrorMessage.IsChecked = false;

                alertStyle.IsEnabled.Should().BeFalse();
                errorTitle.IsEnabled.Should().BeFalse();
                errorMessage.IsEnabled.Should().BeFalse();

                showErrorMessage.IsChecked = true;

                alertStyle.IsEnabled.Should().BeTrue();
                errorTitle.IsEnabled.Should().BeTrue();
                errorMessage.IsEnabled.Should().BeTrue();
            }
            finally
            {
                dialog.Close();
            }
        });
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
    public void OkAfterSwitchingToAnyValue_DropsHiddenCriteriaFields()
    {
        StaTestRunner.Run(() =>
        {
            var existing = new DataValidation
            {
                Type = DvType.List,
                Formula1 = "Red,Blue",
                Formula2 = "Hidden",
                ShowDropdown = true
            };
            var dialog = new DataValidationDialog(existing);
            dialog.Show();
            try
            {
                SelectComboItemByTag(GetControl<ComboBox>(dialog, "TypeCombo"), "Any");

                InvokePrivateAllowingNonModalDialogResult(dialog, "OkButton_Click");

                dialog.Result.Should().NotBeNull();
                dialog.Result!.Type.Should().Be(DvType.Any);
                dialog.Result.Formula1.Should().BeEmpty();
                dialog.Result.Formula2.Should().BeEmpty();
                dialog.Result.ShowDropdown.Should().BeFalse();
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void OkAfterSwitchingToSingleValueOperator_DropsHiddenSecondFormula()
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
                SelectComboItemByTag(GetControl<ComboBox>(dialog, "OperatorCombo"), "Equal");

                InvokePrivateAllowingNonModalDialogResult(dialog, "OkButton_Click");

                dialog.Result.Should().NotBeNull();
                dialog.Result!.Type.Should().Be(DvType.WholeNumber);
                dialog.Result.Operator.Should().Be(DvOperator.Equal);
                dialog.Result.Formula1.Should().Be("1");
                dialog.Result.Formula2.Should().BeEmpty();
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
