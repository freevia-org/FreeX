using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

/// <summary>
/// The Excel-parity pieces of the PivotTable dialogs that the Avalonia shell already had: the existing
/// calculated field/item picker with Delete, and the Summarize-by (measure) picker on the Value Filter dialog.
/// </summary>
public sealed partial class PivotWorkflowDialogTests
{
    [Fact]
    public void PivotCalculatedFieldDialog_ForPivotTable_ListsExistingFieldsAndStartsNewAtEquals()
    {
        StaTestRunner.Run(() =>
        {
            var pivot = new PivotTableModel();
            pivot.CalculatedFields.Add(new PivotCalculatedFieldModel("Margin", "Revenue-Cost"));
            var dialog = new PivotCalculatedFieldDialog(fieldNames: ["Revenue", "Cost"], pivotTable: pivot);
            var existingBox = DialogSourceTestSupport.GetPrivateField<ComboBox>(dialog, "_existingBox");
            var nameBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "_nameBox");
            var formulaBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "_formulaBox");
            var fieldList = DialogSourceTestSupport.GetPrivateField<ListBox>(dialog, "_fieldList");

            existingBox.Items.Cast<object>().Select(item => item.ToString()).Should()
                .Equal("(New calculated field)", "Margin");
            existingBox.SelectedIndex.Should().Be(0);
            nameBox.Text.Should().BeEmpty();
            formulaBox.Text.Should().Be("= ");
            fieldList.Items.Cast<object>().Should().Equal("Revenue", "Cost");

            existingBox.SelectedIndex = 1;
            nameBox.Text.Should().Be("Margin");
            formulaBox.Text.Should().Be("Revenue-Cost");

            existingBox.SelectedIndex = 0;
            nameBox.Text.Should().BeEmpty();
            formulaBox.Text.Should().Be("= ");
        });
    }

    [Fact]
    public void PivotCalculatedFieldDialog_ForPivotTable_DeleteCommitsRemovalOfTheNamedField()
    {
        StaTestRunner.Run(() =>
        {
            var pivot = new PivotTableModel();
            pivot.CalculatedFields.Add(new PivotCalculatedFieldModel("Margin", "Revenue-Cost"));
            pivot.CalculatedFields.Add(new PivotCalculatedFieldModel("Tax", "Revenue*0.2"));
            var dialog = new PivotCalculatedFieldDialog(fieldNames: ["Revenue", "Cost"], pivotTable: pivot);
            var existingBox = DialogSourceTestSupport.GetPrivateField<ComboBox>(dialog, "_existingBox");
            existingBox.SelectedIndex = 1;

            InvokePrivate(dialog, "DeleteField");

            dialog.Submission.Should().NotBeNull();
            var commit = dialog.CreateCommitPlan();
            commit.Should().NotBeNull();
            commit!.Success.Should().BeTrue();
            commit.CalculatedFields.Select(field => field.Name).Should().Equal("Tax");
        });
    }

    [Fact]
    public void PivotCalculatedFieldDialog_DraftForm_HasNoExistingFieldPickerOrCommitPlan()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PivotCalculatedFieldDialog(name: "Margin", formula: "Revenue-Cost", fieldNames: ["Revenue"]);
            var existingBox = DialogSourceTestSupport.GetPrivateField<ComboBox>(dialog, "_existingBox");

            existingBox.Items.Count.Should().Be(0);
            dialog.CreateCommitPlan().Should().BeNull();
        });
    }

    [Fact]
    public void PivotCalculatedItemDialog_ForPivotTable_ListsExistingItemsOfTheSelectedFieldAndDeletes()
    {
        StaTestRunner.Run(() =>
        {
            var pivot = new PivotTableModel();
            pivot.RowFields.Add(new PivotFieldModel(0));
            pivot.CalculatedItems.Add(new PivotCalculatedItemModel(0, "Coast", "East+West"));
            pivot.CalculatedItems.Add(new PivotCalculatedItemModel(1, "Other", "A+B"));
            var dialog = new PivotCalculatedItemDialog(
                ["Region", "Product"],
                0,
                itemNamesBySourceFieldIndex: new Dictionary<int, IEnumerable<string>> { [0] = ["East", "West"] },
                pivotTable: pivot);
            var existingBox = DialogSourceTestSupport.GetPrivateField<ComboBox>(dialog, "_existingBox");
            var nameBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "_nameBox");
            var formulaBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "_formulaBox");

            dialog.OpenIssue.Should().BeNull();
            existingBox.Items.Cast<object>().Select(item => item.ToString()).Should()
                .Equal("(New calculated item)", "Coast");
            formulaBox.Text.Should().Be("= ");

            existingBox.SelectedIndex = 1;
            nameBox.Text.Should().Be("Coast");
            formulaBox.Text.Should().Be("East+West");

            InvokePrivate(dialog, "DeleteItem");

            var commit = dialog.CreateCommitPlan();
            commit.Should().NotBeNull();
            commit!.Success.Should().BeTrue();
            commit.CalculatedItems.Select(item => item.Name).Should().Equal("Other");
        });
    }

    [Fact]
    public void PivotCalculatedItemDialog_ForPivotTableWithoutRowOrColumnFields_ReportsOpenIssue()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PivotCalculatedItemDialog(["Region"], 0, pivotTable: new PivotTableModel());

            dialog.OpenIssue.Should().NotBeNull();
        });
    }

    [Fact]
    public void PivotValueFilterDialog_WithDataFieldNames_SubmitsTheChosenMeasure()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = new PivotValueFilterDialog(0, dataFieldNames: ["Sum of Units", "Sum of Revenue"]);
            var measureBox = DialogSourceTestSupport.GetPrivateField<ComboBox>(dialog, "ValueFilterDataFieldBox");
            var valueBox = DialogSourceTestSupport.GetPrivateField<TextBox>(dialog, "ValueFilterValueBox");

            measureBox.Items.Count.Should().Be(2);
            measureBox.SelectedIndex.Should().Be(0);

            measureBox.SelectedIndex = 1;
            valueBox.Text = "10";
            InvokePrivate(dialog, "OkButton_Click");

            dialog.ResultFilter.Should().NotBeNull();
            dialog.ResultFilter!.DataFieldIndex.Should().Be(1);
        });
    }

    [Fact]
    public void PivotValueFilterDialog_EditingAnExistingFilter_PreselectsItsMeasure()
    {
        StaTestRunner.Run(() =>
        {
            var existing = new PivotValueFilterModel(1, PivotValueFilterKind.Top, Count: 3, SourceFieldIndex: 0);
            var dialog = new PivotValueFilterDialog(0, existing, ["Sum of Units", "Sum of Revenue"]);
            var measureBox = DialogSourceTestSupport.GetPrivateField<ComboBox>(dialog, "ValueFilterDataFieldBox");

            measureBox.SelectedIndex.Should().Be(1);
        });
    }

    [Fact]
    public void PivotValueFilterDialog_WithoutDataFieldNames_HidesTheMeasurePickerAndKeepsTheExistingMeasure()
    {
        StaTestRunner.Run(() =>
        {
            var existing = new PivotValueFilterModel(2, PivotValueFilterKind.Top, Count: 3, SourceFieldIndex: 0);
            var dialog = new PivotValueFilterDialog(0, existing);
            var measureBox = DialogSourceTestSupport.GetPrivateField<ComboBox>(dialog, "ValueFilterDataFieldBox");

            measureBox.Visibility.Should().Be(System.Windows.Visibility.Collapsed);
            InvokePrivate(dialog, "OkButton_Click");
            dialog.ResultFilter!.DataFieldIndex.Should().Be(2);
        });
    }

    // The dialogs set DialogResult last, which throws when the window was never shown; the state under test
    // is assigned before that.
    private static void InvokePrivate(object dialog, string methodName) =>
        DialogSourceTestSupport.InvokePrivateHandlerAllowingNonModalDialogResult(dialog, methodName);
}
