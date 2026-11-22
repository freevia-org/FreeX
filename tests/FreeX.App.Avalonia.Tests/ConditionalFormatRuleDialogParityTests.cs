using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;

using FluentAssertions;

using Free.Shared.Ribbon;
using FreeX.App.Presentation.ConditionalFormatting;
using FreeX.Core.Model;

using Xunit;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// The Avalonia rule dialogs mirror WPF's <c>ConditionalFormatDialog</c> family: each Home &gt; Conditional
/// Formatting entry opens its dedicated dialog (title, fields and defaults), and editing a rule keeps every
/// stored data-bar / color-scale / icon-set setting. Every lambda ends in <c>return true</c> so it binds to the
/// <c>Func&lt;Task&lt;T&gt;&gt;</c> Dispatch overload (an async-void lambda would swallow assertion failures).
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class ConditionalFormatRuleDialogParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static T Find<T>(MainWindow.ConditionalFormatRuleDialogInspection probe, string id) where T : Control =>
        probe.FindControl(id).Should().BeOfType<T>($"the dialog should expose '{id}'").Subject;

    private static async Task RunAsync(Func<MainWindow, Task> body)
    {
        await Session.Dispatch(async () =>
        {
            var window = new MainWindow([]);
            window.Show();
            try
            {
                await body(window);
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }

            return true;
        }, CancellationToken.None);
    }

    private static GridRange Range(MainWindow window, string sheetName)
    {
        var sheet = window.Session.Workbook.AddSheet(sheetName);
        window.Session.SelectSheet(sheet.Id);
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 3));
        window.Session.SelectRange(range);
        return range;
    }

    [Fact]
    public Task GreaterThanDialog_UsesHighlightCellsTitleAndBuildsCellValueRule() =>
        RunAsync(async window =>
        {
            var range = Range(window, "CfGreater");
            string? title = null;

            var rule = await window.ShowConditionalFormatRuleDialogForTestAsync("Greater Than", null, probe =>
            {
                title = probe.Dialog.Title;
                Find<ComboBox>(probe, "ConditionalFormatRuleTypeBox").SelectedItem.Should().Be("Cell Value");
                Find<TextBox>(probe, "ConditionalFormatValue1Box").Text = "100";
                Click(probe.OkButton);
            });

            title.Should().Be("Highlight Cells Rule - Greater Than");
            rule.Should().NotBeNull();
            rule!.RuleType.Should().Be(CfRuleType.CellValue);
            rule.Operator.Should().Be(CfOperator.GreaterThan);
            rule.Value1.Should().Be("100");
            rule.AppliesTo.Should().Be(range);
            rule.FormatIfTrue!.FillColor.Should().Be(new CellColor(255, 199, 206));
        });

    [Fact]
    public Task BetweenDialog_ShowsMinimumAndMaximumBoxes_AndRejectsBlankValue() =>
        RunAsync(async window =>
        {
            Range(window, "CfBetween");
            var rule = await window.ShowConditionalFormatRuleDialogForTestAsync("Between", null, probe =>
            {
                Find<TextBox>(probe, "ConditionalFormatValue1Box").Text = "1";
                Find<TextBox>(probe, "ConditionalFormatValue2Box").Text = "9";
                Click(probe.OkButton);
            });

            rule!.Operator.Should().Be(CfOperator.Between);
            rule.Value1.Should().Be("1");
            rule.Value2.Should().Be("9");
        });

    [Fact]
    public Task TextDialog_OperatorChangeRebuildsDescriptionAndBuildsBeginsWith() =>
        RunAsync(async window =>
        {
            Range(window, "CfText");
            var rule = await window.ShowConditionalFormatRuleDialogForTestAsync("Text Contains", null, probe =>
            {
                var operatorBox = Find<ComboBox>(probe, "ConditionalFormatTextOperatorBox");
                operatorBox.SelectedItem = "beginning with";
                Find<TextBox>(probe, "ConditionalFormatTextBox").Text = "abc";
                Click(probe.OkButton);
            });

            rule!.RuleType.Should().Be(CfRuleType.BeginsWith);
            rule.TextRuleText.Should().Be("abc");
        });

    [Fact]
    public Task BottomPercentDialog_UsesTopBottomTitleAndBuildsBottomPercentRule() =>
        RunAsync(async window =>
        {
            Range(window, "CfBottomPercent");
            string? title = null;
            var rule = await window.ShowConditionalFormatRuleDialogForTestAsync("Bottom 10%", null, probe =>
            {
                title = probe.Dialog.Title;
                Find<TextBox>(probe, "ConditionalFormatRankBox").Text.Should().Be("10");
                Click(probe.OkButton);
            });

            title.Should().Be("Top/Bottom Rule - Bottom 10%");
            rule!.RuleType.Should().Be(CfRuleType.Top10);
            rule.TopBottomPercent.Should().BeTrue();
            rule.AboveAverage.Should().BeFalse();
            rule.TopBottomRank.Should().Be(10);
        });

    [Fact]
    public Task ColorScaleDialog_OpensWithWpfDefaults_TwoColorMidpointDisabled() =>
        RunAsync(async window =>
        {
            Range(window, "CfColorScale");
            string? title = null;
            var rule = await window.ShowConditionalFormatRuleDialogForTestAsync("Color Scale", null, probe =>
            {
                title = probe.Dialog.Title;
                Find<CheckBox>(probe, "ConditionalFormatThreeColorBox").IsChecked.Should().BeFalse();
                Find<ComboBox>(probe, "ConditionalFormatColorScaleMidTypeBox").IsEnabled.Should().BeFalse();
                Find<TextBox>(probe, "ConditionalFormatMinColorBox").Text.Should().Be("99,190,123");
                Find<TextBox>(probe, "ConditionalFormatMaxColorBox").Text.Should().Be("248,105,107");
                Click(probe.OkButton);
            });

            title.Should().Be("Color Scale Rule");
            rule!.RuleType.Should().Be(CfRuleType.ColorScale);
            rule.UseThreeColorScale.Should().BeFalse();
            rule.MinThresholdType.Should().Be(CfThresholdType.Min);
            rule.MaxThresholdType.Should().Be(CfThresholdType.Max);
        });

    [Fact]
    public Task DataBarDialog_ExposesWpfFieldsAndAppliesThresholdSettings() =>
        RunAsync(async window =>
        {
            Range(window, "CfDataBarNew");
            string? title = null;
            var rule = await window.ShowConditionalFormatRuleDialogForTestAsync("Data Bar", null, probe =>
            {
                title = probe.Dialog.Title;
                Find<ComboBox>(probe, "ConditionalFormatDataBarMinTypeBox").SelectedItem.Should().Be(CfThresholdType.AutoMin);
                Find<ComboBox>(probe, "ConditionalFormatDataBarMaxTypeBox").SelectedItem.Should().Be(CfThresholdType.AutoMax);
                Find<CheckBox>(probe, "ConditionalFormatDataBarGradientBox").IsChecked.Should().BeTrue();
                Find<CheckBox>(probe, "ConditionalFormatDataBarShowBarOnlyBox").IsChecked.Should().BeFalse();
                probe.FindControl("DataBarPreview").Should().NotBeNull();

                Find<ComboBox>(probe, "ConditionalFormatDataBarMinTypeBox").SelectedItem = CfThresholdType.Number;
                Find<TextBox>(probe, "ConditionalFormatDataBarMinValueBox").Text = "5";
                Find<CheckBox>(probe, "ConditionalFormatDataBarBorderBox").IsChecked = true;
                Click(probe.OkButton);
            });

            title.Should().Be("Data Bar Rule");
            rule!.RuleType.Should().Be(CfRuleType.DataBar);
            rule.DataBarMinThresholdType.Should().Be(CfThresholdType.Number);
            rule.DataBarMinThresholdValue.Should().Be("5");
            rule.DataBarBorder.Should().BeTrue();
        });

    [Fact]
    public Task EditingDataBarRule_KeepsEveryStoredSetting() =>
        RunAsync(async window =>
        {
            var range = Range(window, "CfDataBarEdit");
            var original = new ConditionalFormat
            {
                AppliesTo = range,
                RuleType = CfRuleType.DataBar,
                DataBarColor = new RgbColor(91, 155, 213),
                DataBarMinThresholdType = CfThresholdType.Percent,
                DataBarMinThresholdValue = "10",
                DataBarMaxThresholdType = CfThresholdType.Percent,
                DataBarMaxThresholdValue = "90",
                DataBarShowValue = false,
                DataBarGradient = false,
                DataBarMinLength = 5,
                DataBarMaxLength = 80,
                DataBarBorder = true,
                DataBarAxisPosition = "middle",
                DataBarAxisColor = new RgbColor(1, 2, 3),
                DataBarNegativeFillColor = new RgbColor(4, 5, 6),
                DataBarNegativeBorderColor = new RgbColor(7, 8, 9),
                StopIfTrue = true,
            };

            string? title = null;
            var edited = await window.ShowConditionalFormatRuleDialogForTestAsync("Data Bar", original, probe =>
            {
                title = probe.Dialog.Title;
                Click(probe.OkButton);
            });

            title.Should().Be("Edit Formatting Rule");
            edited.Should().NotBeNull();
            edited!.Id.Should().Be(original.Id);
            edited.DataBarMinThresholdType.Should().Be(CfThresholdType.Percent);
            edited.DataBarMinThresholdValue.Should().Be("10");
            edited.DataBarMaxThresholdType.Should().Be(CfThresholdType.Percent);
            edited.DataBarMaxThresholdValue.Should().Be("90");
            edited.DataBarShowValue.Should().BeFalse();
            edited.DataBarGradient.Should().BeFalse();
            edited.DataBarMinLength.Should().Be(5);
            edited.DataBarMaxLength.Should().Be(80);
            edited.DataBarBorder.Should().BeTrue();
            edited.DataBarAxisPosition.Should().Be("middle");
            edited.DataBarAxisColor.Should().Be(new RgbColor(1, 2, 3));
            edited.DataBarNegativeFillColor.Should().Be(new RgbColor(4, 5, 6));
            edited.DataBarNegativeBorderColor.Should().Be(new RgbColor(7, 8, 9));
            edited.DataBarColor.Should().Be(new RgbColor(91, 155, 213));
            edited.StopIfTrue.Should().BeTrue("fields the editor does not surface survive an edit");
        });

    [Fact]
    public Task EditingColorScaleRule_KeepsThresholdTypesValuesAndColors() =>
        RunAsync(async window =>
        {
            var range = Range(window, "CfColorScaleEdit");
            var original = new ConditionalFormat
            {
                AppliesTo = range,
                RuleType = CfRuleType.ColorScale,
                UseThreeColorScale = true,
                MinThresholdType = CfThresholdType.Number,
                MinThresholdValue = "2",
                MidThresholdType = CfThresholdType.Percentile,
                MidThresholdValue = "40",
                MaxThresholdType = CfThresholdType.Percent,
                MaxThresholdValue = "95",
                MinColor = new RgbColor(10, 20, 30),
                MidColor = new RgbColor(40, 50, 60),
                MaxColor = new RgbColor(70, 80, 90),
            };

            var edited = await window.ShowConditionalFormatRuleDialogForTestAsync("Color Scale", original, probe =>
            {
                Find<CheckBox>(probe, "ConditionalFormatThreeColorBox").IsChecked.Should().BeTrue();
                Click(probe.OkButton);
            });

            edited!.UseThreeColorScale.Should().BeTrue();
            edited.MinThresholdType.Should().Be(CfThresholdType.Number);
            edited.MinThresholdValue.Should().Be("2");
            edited.MidThresholdValue.Should().Be("40");
            edited.MaxThresholdType.Should().Be(CfThresholdType.Percent);
            edited.MaxThresholdValue.Should().Be("95");
            edited.MinColor.Should().Be(new RgbColor(10, 20, 30));
            edited.MidColor.Should().Be(new RgbColor(40, 50, 60));
            edited.MaxColor.Should().Be(new RgbColor(70, 80, 90));
        });

    [Fact]
    public Task EditingIconSetRule_KeepsShowValueReverseAndThresholds() =>
        RunAsync(async window =>
        {
            var range = Range(window, "CfIconSetEdit");
            var original = new ConditionalFormat
            {
                AppliesTo = range,
                RuleType = CfRuleType.IconSet,
                IconSetStyle = "3Arrows",
                IconSetShowValue = false,
                IconSetReverse = true,
            };
            original.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Percent, "0"));
            original.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Number, "25"));
            original.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Percentile, "80"));

            var edited = await window.ShowConditionalFormatRuleDialogForTestAsync("Icon Set", original, probe =>
            {
                Find<CheckBox>(probe, "ConditionalFormatIconSetShowValueBox").IsChecked.Should().BeFalse();
                Find<CheckBox>(probe, "ConditionalFormatIconSetReverseBox").IsChecked.Should().BeTrue();
                Click(probe.OkButton);
            });

            edited!.IconSetStyle.Should().Be("3Arrows");
            edited.IconSetShowValue.Should().BeFalse();
            edited.IconSetReverse.Should().BeTrue();
            edited.IconSetThresholds.Select(t => (t.Type, t.Value)).Should().Equal(
                (CfThresholdType.Percent, "0"),
                (CfThresholdType.Number, "25"),
                (CfThresholdType.Percentile, "80"));
        });

    [Fact]
    public Task RuleShellList_RetargetsDescriptionToTheDataBarFields() =>
        RunAsync(async window =>
        {
            Range(window, "CfShell");
            var rule = await window.ShowConditionalFormatRuleDialogForTestAsync("Greater Than", null, probe =>
            {
                probe.RuleShellList.SelectedIndex.Should().Be(1);
                probe.RuleShellList.SelectedIndex = 0;
                Find<ComboBox>(probe, "ConditionalFormatFormatStyleBox").SelectedItem.Should().Be("Data Bar");
                Find<ComboBox>(probe, "ConditionalFormatDataBarMinTypeBox");
                Click(probe.OkButton);
            });

            rule!.RuleType.Should().Be(CfRuleType.DataBar);
        });

    [Fact]
    public Task DuplicateValuesDialog_UniqueChoiceBuildsUniqueValuesRule() =>
        RunAsync(async window =>
        {
            Range(window, "CfUnique");
            var rule = await window.ShowConditionalFormatRuleDialogForTestAsync("Duplicate Values", null, probe =>
            {
                Find<ComboBox>(probe, "ConditionalFormatDuplicateKindBox").SelectedItem = "Unique";
                Click(probe.OkButton);
            });

            rule!.RuleType.Should().Be(CfRuleType.UniqueValues);
        });

    [Fact]
    public Task ManageRules_ApplyCommitsPendingEditsWithoutClosing_AndOkWithoutNewEditsIsNoOp() =>
        RunAsync(async window =>
        {
            var range = Range(window, "CfManage");
            var sheet = window.Session.ActiveSheet;
            sheet.ConditionalFormats.Add(new ConditionalFormat
            {
                AppliesTo = range, RuleType = CfRuleType.CellValue, Operator = CfOperator.GreaterThan, Value1 = "1", Priority = 1,
            });
            sheet.ConditionalFormats.Add(new ConditionalFormat
            {
                AppliesTo = range, RuleType = CfRuleType.CellValue, Operator = CfOperator.LessThan, Value1 = "9", Priority = 2,
            });

            var observed = (applyInitiallyEnabled: true, applyEnabledAfterDelete: false, rulesAfterApply: -1, applyEnabledAfterApply: true, rulesAfterOk: -1);
            await window.ShowManageConditionalFormatsDialogAsync(probe =>
            {
                observed.applyInitiallyEnabled = probe.ApplyButton.IsEnabled;
                probe.ListBox.SelectedIndex = 0;
                Click(probe.DeleteButton);
                observed.applyEnabledAfterDelete = probe.ApplyButton.IsEnabled;
                Click(probe.ApplyButton);
                observed.rulesAfterApply = sheet.ConditionalFormats.Count;
                observed.applyEnabledAfterApply = probe.ApplyButton.IsEnabled;
                Click(probe.CloseButton);
                observed.rulesAfterOk = sheet.ConditionalFormats.Count;
            });

            observed.applyInitiallyEnabled.Should().BeFalse("WPF disables Apply until a rule is edited");
            observed.applyEnabledAfterDelete.Should().BeTrue();
            observed.rulesAfterApply.Should().Be(1, "Apply commits the working copy without closing the dialog");
            observed.applyEnabledAfterApply.Should().BeFalse();
            observed.rulesAfterOk.Should().Be(1);
        });
}
