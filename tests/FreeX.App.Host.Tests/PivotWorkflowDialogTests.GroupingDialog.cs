using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class PivotWorkflowDialogTests
{
    [Fact]
    public void PivotFieldGroupingDialog_CreateResult_TrimsFieldAndClampsNumberRangeInterval()
    {
        var result = PivotFieldGroupingDialog.CreateResult(
            "  Order Date  ",
            sourceFieldIndex: -3,
            PivotFieldGrouping.NumberRange,
            "  10  ",
            "  90  ",
            "  -5  ",
            ungroup: false);

        result.Should().Be(new PivotFieldGroupingDialogResult(
            "Order Date",
            0,
            PivotFieldGrouping.NumberRange,
            10,
            90,
            1,
            false));
    }

    [Fact]
    public void PivotFieldGroupingDialog_CreateResult_UngroupClearsGroupingSettings()
    {
        var result = PivotFieldGroupingDialog.CreateResult(
            " Region ",
            sourceFieldIndex: 2,
            PivotFieldGrouping.Month,
            "1",
            "12",
            "3",
            ungroup: true);

        result.Should().Be(new PivotFieldGroupingDialogResult(
            "Region",
            2,
            PivotFieldGrouping.None,
            null,
            null,
            null,
            true));
    }

    [Fact]
    public void PivotFieldGroupingDialog_FromPivotField_UsesCurrentFieldSettings()
    {
        var field = new PivotFieldModel(
            SourceFieldIndex: 1,
            Grouping: PivotFieldGrouping.Month,
            GroupStart: 44562,
            GroupEnd: 44927,
            GroupInterval: 2);

        PivotFieldGroupingDialog.FromPivotField(["Region", "Order Date"], field)
            .Should()
            .Be(new PivotFieldGroupingDialogResult(
                "Order Date",
                1,
                PivotFieldGrouping.Month,
                44562,
                44927,
                2,
                false));
    }

    [Fact]
    public void PivotFieldGroupingDialog_FromPivotField_DefaultsToFirstFieldWhenCurrentSettingsAreMissing()
    {
        PivotFieldGroupingDialog.FromPivotField(["Region", "Order Date"], currentField: null)
            .Should()
            .Be(new PivotFieldGroupingDialogResult(
                "Region",
                0,
                PivotFieldGrouping.None,
                null,
                null,
                null,
                false));
    }

    [Fact]
    public void PivotFieldGroupingDialog_ExposesExcelLikeGroupingSections()
    {
        var source = ReadPivotWorkflowSource();

        source.Should().Contain("UiText.Get(\"PivotFieldGrouping_SelectionGroup\")");
        source.Should().Contain("UiText.Get(\"PivotFieldGrouping_GroupByGroup\")");
        source.Should().Contain("UiText.Get(\"PivotFieldGrouping_RangeGroup\")");
        source.Should().NotContain("Select the PivotTable field and grouping interval");
    }

    [Fact]
    public void PivotFieldGroupingDialogOpenedFromKeyboard_FocusesFieldBox()
    {
        var source = ReadClassSource(
            "PivotFieldGroupingDialog.cs",
            "public sealed class PivotFieldGroupingDialog",
            "");

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_fieldBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_fieldBox);");
    }

    [Fact]
    public void PivotFieldGroupingDialogInvalidNumberRangeIntervals_ShowOwnedWarningAndRefocusByBox()
    {
        var source = ReadClassSource(
            "PivotFieldGroupingDialog.cs",
            "public sealed class PivotFieldGroupingDialog",
            "");

        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotFieldGrouping_EnterPositiveGroupingInterval\"), _intervalBox);");
        source.Should().Contain("DialogMessageHelper.ShowWarning(this, message, Title)");
        source.Should().Contain("private bool ShowInvalidInputWarning(string message, TextBox target)");
        source.Should().Contain("string.IsNullOrWhiteSpace(value)");
        source.Should().Contain("!double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out interval)");
        source.Should().Contain("interval <= 0");
        source.Should().Contain("target.Focus();");
        source.Should().Contain("target.SelectAll();");
        source.Should().Contain("Keyboard.Focus(target);");
    }

    [Fact]
    public void PivotFieldGroupingDialogInvalidBounds_ShowOwnedWarningAndRefocusBadInput()
    {
        var source = ReadClassSource(
            "PivotFieldGroupingDialog.cs",
            "public sealed class PivotFieldGroupingDialog",
            "");

        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotFieldGrouping_EnterValidStartingValue\"), _startBox);");
        source.Should().Contain("ShowInvalidInputWarning(UiText.Get(\"PivotFieldGrouping_EnterValidEndingValue\"), _endBox);");
        source.Should().Contain("TryParseOptionalFiniteDouble(_startBox.Text, out _)");
        source.Should().Contain("TryParseOptionalFiniteDouble(_endBox.Text, out _)");
        source.Should().Contain("double.IsFinite(parsed)");
    }
}
