using FreeX.App.Host;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.App.Host.Tests;

/// <summary>
/// R86-io-data-validation-roundtrip-5-1: editing an existing data validation rule via
/// DataValidationDialog must preserve native/passthrough data (NativeAttributes,
/// NativeChildXmls, NativeContainerAttributes, NativeContainerChildXmls, IsX14) that the dialog
/// has no editors for. Previously these were silently dropped (left null) the moment the rule
/// round-tripped through the dialog's OK path, even when the user only touched one unrelated
/// field. See DataValidationDialog.xaml.cs's ctor and DataValidationDialogPlanner.CreateRule.
/// </summary>
public sealed class R86_DataValidationDialogNativePassthroughTests
{
    private static DataValidation NewListRuleWithNativeData() => new()
    {
        Type = DvType.List,
        Formula1 = "Red,Blue",
        AlertStyle = DvAlertStyle.Stop,
        IsX14 = true,
        NativeAttributes = new Dictionary<string, string> { ["imeMode"] = "fullKatakana" },
        NativeChildXmls = ["<extLst><ext>native</ext></extLst>"],
        NativeContainerAttributes = new Dictionary<string, string> { ["xr:uid"] = "{ABC}" },
        NativeContainerChildXmls = ["<xr:someContainerChild/>"]
    };

    [Fact]
    public void EditingUnrelatedField_PreservesNativeAttributesAndX14()
    {
        StaTestRunner.Run(() =>
        {
            var existing = NewListRuleWithNativeData();
            var dialog = new DataValidationDialog(existing);
            dialog.Show();
            try
            {
                // Change only AlertStyle: Stop -> Warning. Nothing else is touched.
                var alertStyleCombo = DialogSourceTestSupport.GetPrivateField<System.Windows.Controls.ComboBox>(dialog, "AlertStyleCombo");
                foreach (var item in alertStyleCombo.Items)
                {
                    if (item is System.Windows.Controls.ComboBoxItem comboBoxItem
                        && string.Equals(comboBoxItem.Tag as string, "Warning", StringComparison.Ordinal))
                    {
                        alertStyleCombo.SelectedItem = comboBoxItem;
                        break;
                    }
                }

                DialogSourceTestSupport.InvokePrivateHandlerAllowingNonModalDialogResult(dialog, "OkButton_Click");

                dialog.Result.Should().NotBeNull();
                dialog.Result!.Id.Should().Be(existing.Id);
                dialog.Result.AlertStyle.Should().Be(DvAlertStyle.Warning);

                // The unrelated native/passthrough data must survive the round trip unchanged.
                dialog.Result.IsX14.Should().BeTrue();
                dialog.Result.NativeAttributes.Should().NotBeNull();
                dialog.Result.NativeAttributes!["imeMode"].Should().Be("fullKatakana");
                dialog.Result.NativeChildXmls.Should().BeEquivalentTo(existing.NativeChildXmls);
                dialog.Result.NativeContainerAttributes.Should().NotBeNull();
                dialog.Result.NativeContainerAttributes!["xr:uid"].Should().Be("{ABC}");
                dialog.Result.NativeContainerChildXmls.Should().BeEquivalentTo(existing.NativeContainerChildXmls);
            }
            finally
            {
                dialog.Close();
            }
        });
    }

    [Fact]
    public void EditingUnrelatedField_WithoutNativeData_LeavesNativeFieldsNull()
    {
        // No-regression sibling: an existing rule that never had native/passthrough data must
        // continue to round-trip with null native fields (no accidental non-null defaults).
        StaTestRunner.Run(() =>
        {
            var existing = new DataValidation
            {
                Type = DvType.List,
                Formula1 = "Red,Blue",
                AlertStyle = DvAlertStyle.Stop
            };
            var dialog = new DataValidationDialog(existing);
            dialog.Show();
            try
            {
                var alertStyleCombo = DialogSourceTestSupport.GetPrivateField<System.Windows.Controls.ComboBox>(dialog, "AlertStyleCombo");
                foreach (var item in alertStyleCombo.Items)
                {
                    if (item is System.Windows.Controls.ComboBoxItem comboBoxItem
                        && string.Equals(comboBoxItem.Tag as string, "Warning", StringComparison.Ordinal))
                    {
                        alertStyleCombo.SelectedItem = comboBoxItem;
                        break;
                    }
                }

                DialogSourceTestSupport.InvokePrivateHandlerAllowingNonModalDialogResult(dialog, "OkButton_Click");

                dialog.Result.Should().NotBeNull();
                dialog.Result!.AlertStyle.Should().Be(DvAlertStyle.Warning);
                dialog.Result.IsX14.Should().BeFalse();
                dialog.Result.NativeAttributes.Should().BeNull();
                dialog.Result.NativeChildXmls.Should().BeNull();
                dialog.Result.NativeContainerAttributes.Should().BeNull();
                dialog.Result.NativeContainerChildXmls.Should().BeNull();
            }
            finally
            {
                dialog.Close();
            }
        });
    }
}
