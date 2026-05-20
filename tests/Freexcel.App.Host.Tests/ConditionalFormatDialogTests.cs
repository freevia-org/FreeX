using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ConditionalFormatDialogTests
{
    [Theory]
    [InlineData("Greater Than", typeof(HighlightCellsRuleDialog))]
    [InlineData("Top 10%", typeof(TopBottomRuleDialog))]
    [InlineData("Data Bar", typeof(DataBarRuleDialog))]
    [InlineData("Color Scale", typeof(ColorScaleRuleDialog))]
    [InlineData("Icon Set", typeof(IconSetRuleDialog))]
    [InlineData("Formula", typeof(NewConditionalFormatRuleDialog))]
    public void Factory_CreatesRuleFamilySpecificDialogs(string ruleType, Type expectedDialogType)
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ConditionalFormatDialogFactory.Create(ruleType, RangeFor(SheetId.New()));

            dialog.Should().BeOfType(expectedDialogType);
            dialog.Close();
        });
    }

    [Theory]
    [InlineData("Top 10%", true, true)]
    [InlineData("Bottom 10%", false, true)]
    [InlineData("Below Average", false, false)]
    public void TopBottomParityRule_CreatesExpectedConditionalFormat(string ruleType, bool aboveAverage, bool topBottomPercent)
    {
        StaTestRunner.Run(() =>
        {
            var dialog = ShowDialogForTest(new ConditionalFormatDialog(ruleType, RangeFor(SheetId.New())));

            ClickOkForTest(dialog);

            dialog.ResultRule.Should().NotBeNull();
            dialog.ResultRule!.RuleType.Should().Be(ruleType.Contains("Average") ? CfRuleType.AboveAverage : CfRuleType.Top10);
            dialog.ResultRule.AboveAverage.Should().Be(aboveAverage);
            dialog.ResultRule.TopBottomPercent.Should().Be(topBottomPercent);

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

    private static ConditionalFormatDialog ShowDialogForTest(ConditionalFormatDialog dialog)
    {
        dialog.Show();
        return dialog;
    }

    private static T GetControl<T>(ConditionalFormatDialog dialog, string name)
        where T : class
    {
        var field = typeof(ConditionalFormatDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

    private static void ClickOkForTest(ConditionalFormatDialog dialog)
    {
        var method = typeof(ConditionalFormatDialog).GetMethod("Ok_Click", BindingFlags.Instance | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        try
        {
            method!.Invoke(dialog, [dialog, new RoutedEventArgs()]);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException invalidOperation
            && invalidOperation.Message.Contains("DialogResult"))
        {
            // The handler creates ResultRule before setting DialogResult. Direct modeless invocation in
            // tests reaches WPF's modal-only postcondition after the behavior under test runs.
        }
    }

    private static GridRange RangeFor(SheetId sheetId) =>
        new(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3));
}
