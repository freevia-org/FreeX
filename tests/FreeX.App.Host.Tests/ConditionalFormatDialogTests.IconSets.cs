using System;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Host.Tests;

public sealed partial class ConditionalFormatDialogTests
{
    [Fact]
    public void IconSetRule_ThresholdTypePickers_ExcludeDataBarOnlyAutomaticEndpoints()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Icon Set", RangeFor(SheetId.New())));

            // Each per-bucket icon threshold row has its own type picker; AutoMin/AutoMax ("Automatic")
            // is a data-bar-only endpoint and must never leak into icon-set thresholds.
            var rowsField = typeof(ConditionalFormatDialog).GetField("_iconSetThresholdRows", BindingFlags.Instance | BindingFlags.NonPublic);
            var rows = (System.Collections.IEnumerable)rowsField!.GetValue(dialog)!;

            var typeBoxes = new List<ComboBox>();
            foreach (var row in rows)
                typeBoxes.Add((ComboBox)row.GetType().GetField("Item1")!.GetValue(row)!);

            typeBoxes.Should().NotBeEmpty();
            foreach (var typeBox in typeBoxes)
            {
                typeBox.Items.Cast<CfThresholdType>().Should().NotContain(
                    [CfThresholdType.AutoMin, CfThresholdType.AutoMax]);
            }

            dialog.Close();
        });
    }

    [Fact]
    public void IconSetRule_CreatesIconSetWithoutFormatIfTrue()
    {
        StaTestRunner.Run(() =>
        {
            var range = RangeFor(SheetId.New());
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Icon Set", range));

            GetControl<ComboBox>(dialog, "_iconSetStyleBox").SelectedItem = "5Arrows";
            GetControl<CheckBox>(dialog, "_iconSetShowValueBox").IsChecked = false;
            GetControl<CheckBox>(dialog, "_iconSetReverseBox").IsChecked = true;

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.RuleType.Should().Be(CfRuleType.IconSet);
            dialog.ResultRule.IconSetStyle.Should().Be("5Arrows");
            dialog.ResultRule.IconSetShowValue.Should().BeFalse();
            dialog.ResultRule.IconSetReverse.Should().BeTrue();
            dialog.ResultRule.FormatIfTrue.Should().BeNull();

            dialog.Close();
        });
    }

    [Fact]
    public void IconSetRule_CreatesThresholdsForSelectedIconCount()
    {
        StaTestRunner.Run(() =>
        {
            var range = RangeFor(SheetId.New());
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Icon Set", range));

            GetControl<ComboBox>(dialog, "_iconSetStyleBox").SelectedItem = "5Quarters";

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.IconSetStyle.Should().Be("5Quarters");
            dialog.ResultRule.IconSetThresholds.Should().Equal(
                new CfThresholdModel(CfThresholdType.Percent, "0"),
                new CfThresholdModel(CfThresholdType.Percent, "20"),
                new CfThresholdModel(CfThresholdType.Percent, "40"),
                new CfThresholdModel(CfThresholdType.Percent, "60"),
                new CfThresholdModel(CfThresholdType.Percent, "80"));

            dialog.Close();
        });
    }

    [Fact]
    public void IconSetRule_OffersExcelIconSetGalleryStyles()
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog("Icon Set", RangeFor(SheetId.New())));

            var styles = GetControl<ComboBox>(dialog, "_iconSetStyleBox").Items.Cast<string>();

            styles.Should().Contain([
                "3ArrowsGray",
                "3Flags",
                "4RedToBlack",
                "4Rating",
                "5Boxes"
            ]);

            dialog.Close();
        });
    }

    [Fact]
    public void ExistingIconSetRule_PrePopulatesIconSetFields()
    {
        StaTestRunner.Run(() =>
        {
            var id = Guid.NewGuid();
            var existing = new ConditionalFormat
            {
                Id = id,
                AppliesTo = RangeFor(SheetId.New()),
                Priority = 4,
                RuleType = CfRuleType.IconSet,
                IconSetStyle = "4TrafficLights",
                IconSetShowValue = false,
                IconSetReverse = true,
                StopIfTrue = true
            };

            var dialog = ShowDialogForTest(new ConditionalFormatDialog(existing));

            GetControl<ComboBox>(dialog, "_iconSetStyleBox").SelectedItem.Should().Be("4TrafficLights");
            GetControl<CheckBox>(dialog, "_iconSetShowValueBox").IsChecked.Should().BeFalse();
            GetControl<CheckBox>(dialog, "_iconSetReverseBox").IsChecked.Should().BeTrue();

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.RuleType.Should().Be(CfRuleType.IconSet);
            dialog.ResultRule.Id.Should().Be(id);
            dialog.ResultRule.Priority.Should().Be(4);
            dialog.ResultRule.IconSetStyle.Should().Be("4TrafficLights");
            dialog.ResultRule.IconSetShowValue.Should().BeFalse();
            dialog.ResultRule.IconSetReverse.Should().BeTrue();
            dialog.ResultRule.StopIfTrue.Should().BeTrue();

            dialog.Close();
        });
    }

    [Fact]
    public void ExistingIconSetRule_PreservesUnlistedIconSetStyleAndHiddenFields()
    {
        StaTestRunner.Run(() =>
        {
            var existing = new ConditionalFormat
            {
                AppliesTo = RangeFor(SheetId.New()),
                RuleType = CfRuleType.IconSet,
                IconSetStyle = "3ArrowsGray",
                IconSetShowValue = false,
                IconSetReverse = true,
                TopBottomRank = 5,
                StopIfTrue = true
            };

            var dialog = ShowDialogForTest(new ConditionalFormatDialog(existing));

            GetControl<ComboBox>(dialog, "_iconSetStyleBox").SelectedItem.Should().Be("3ArrowsGray");
            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.IconSetStyle.Should().Be("3ArrowsGray");
            dialog.ResultRule.TopBottomRank.Should().Be(5);
            dialog.ResultRule.StopIfTrue.Should().BeTrue();

            dialog.Close();
        });
    }

    [Fact]
    public void IconSetRule_ThresholdRows_IncludeIconOverrideDropdown()
    {
        var source = ReadConditionalFormatDialogSource();
        source.Should().Contain("OverrideBox", "each threshold row should have an icon-override selector");
        source.Should().Contain("CfIconOverride", "icon overrides use the model type");
        source.Should().Contain("IconOverrides", "result should write back to IconOverrides");
    }
}
