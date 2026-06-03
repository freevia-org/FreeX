using System.IO;
using System.Windows;
using System.Windows.Controls;
using FreeX.App.Host;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class DataValidationDialogTests
{
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
    public void SelectionSourceSetter_RefreshesUseSelectionVisibilityForExistingListRule()
    {
        StaTestRunner.Run(() =>
        {
            var existing = new DataValidation
            {
                Type = DvType.List,
                Formula1 = "Old,Values"
            };
            var dialog = new DataValidationDialog(existing)
            {
                SelectionSource = "=Sheet1!$B$2:$B$8"
            };
            dialog.Show();
            try
            {
                var useSelection = GetControl<Button>(dialog, "UseSelectionButton");
                useSelection.Visibility.Should().Be(Visibility.Visible);

                InvokePrivate(dialog, "UseSelectionButton_Click");

                GetControl<TextBox>(dialog, "Formula1Box").Text.Should().Be("=Sheet1!$B$2:$B$8");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void SourcePickerButton_RaisesRangeSelectionRequestWithoutPreseededSelection()
    {
        StaTestRunner.Run(() =>
        {
            var requests = new List<DataValidationRangeSelectionRequest>();
            var dialog = new DataValidationDialog(requests.Add);
            dialog.Show();
            try
            {
                SelectComboItemByTag(GetControl<ComboBox>(dialog, "TypeCombo"), "Custom");
                GetControl<TextBox>(dialog, "Formula1Box").Text = "=$A$1:$A$10";

                InvokePrivate(dialog, "SourcePickerButton_Click");

                requests.Should().Equal(new DataValidationRangeSelectionRequest(
                    DataValidationRangeSelectionTarget.Formula1,
                    "=$A$1:$A$10",
                    CollapseDialog: true));
                dialog.RangeSelectionRequest.Should().Be(requests[0]);
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
                dialog.RangeSelectionRequest.Should().Be(new DataValidationRangeSelectionRequest(
                    DataValidationRangeSelectionTarget.Formula2,
                    "=Sheet1!$C$2:$C$8",
                    CollapseDialog: true));
                formula2Box.IsKeyboardFocusWithin.Should().BeTrue();
                formula2Box.SelectionLength.Should().Be(formula2Box.Text.Length);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void ApplyRangeSelection_UpdatesRequestedFormulaBox()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new DataValidationDialog();
            dialog.Show();
            try
            {
                dialog.ApplyRangeSelection(DataValidationRangeSelectionTarget.Formula1, "=Sheet1!$B$2:$B$8");
                dialog.ApplyRangeSelection(DataValidationRangeSelectionTarget.Formula2, "=Sheet1!$C$2:$C$8");

                GetControl<TextBox>(dialog, "Formula1Box").Text.Should().Be("=Sheet1!$B$2:$B$8");
                GetControl<TextBox>(dialog, "Formula2Box").Text.Should().Be("=Sheet1!$C$2:$C$8");
                dialog.SelectionSource.Should().Be("=Sheet1!$C$2:$C$8");
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void RangePickerButtons_RefocusFormulaInputAfterRequest()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "DataValidationDialog.xaml.cs"));
        var handlerSource = source[
            source.IndexOf("private void RequestRangeSelection", StringComparison.Ordinal)..
            source.IndexOf("private static void SelectComboItemByTag", StringComparison.Ordinal)];

        handlerSource.Should().Contain("_requestRangeSelection?.Invoke(RangeSelectionRequest);");
        handlerSource.Should().Contain("FocusRangeSelectionInput(textBox);");
        source.Should().Contain("private static void FocusRangeSelectionInput(TextBox textBox)");
        source.Should().Contain("DialogFocus.FocusAndSelect(textBox);");
    }

    [Fact]
    public void DataValidationRangeSelectionRequest_TrimsCurrentTextAndCollapsesDialog()
    {
        DataValidationDialog.CreateRangeSelectionRequest(
                DataValidationRangeSelectionTarget.Formula2,
                "  =Sheet1!$C$2:$C$8  ")
            .Should()
            .Be(new DataValidationRangeSelectionRequest(
                DataValidationRangeSelectionTarget.Formula2,
                "=Sheet1!$C$2:$C$8",
                CollapseDialog: true));
    }
}
