using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ConditionalFormatDialogTests
{
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
