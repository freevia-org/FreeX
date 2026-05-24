using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using FluentAssertions;
using Freexcel.Core.Model;

namespace Freexcel.App.Host.Tests;

public sealed class ManageConditionalFormatsDialogTests
{
    [Fact]
    public void BuildResultRules_ForCurrentSelectionPreservesRulesOutsideSelection()
    {
        var sheetId = SheetId.New();
        var outsideBefore = CreateRule(sheetId, 1, 1, 1);
        var selected = CreateRule(sheetId, 2, 2, 2);
        var outsideAfter = CreateRule(sheetId, 4, 4, 3);
        var editedSelected = CreateRule(sheetId, 2, 2, 9, selected.Id, stopIfTrue: true);

        var result = ManageConditionalFormatsDialog.BuildResultRules(
            [outsideBefore, selected, outsideAfter],
            new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 2, 2)),
            filterToSelection: true,
            [editedSelected]);

        result.Should().HaveCount(3);
        result.Select(rule => rule.Id).Should().Equal(outsideBefore.Id, selected.Id, outsideAfter.Id);
        result.Select(rule => rule.Priority).Should().Equal(1, 2, 3);
        result[1].StopIfTrue.Should().BeTrue();
    }

    [Fact]
    public void BuildResultRules_ForCurrentSelectionCanDeleteSelectedRulesOnly()
    {
        var sheetId = SheetId.New();
        var outsideBefore = CreateRule(sheetId, 1, 1, 1);
        var selected = CreateRule(sheetId, 2, 2, 2);
        var outsideAfter = CreateRule(sheetId, 4, 4, 3);

        var result = ManageConditionalFormatsDialog.BuildResultRules(
            [outsideBefore, selected, outsideAfter],
            new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 2, 2)),
            filterToSelection: true,
            []);

        result.Select(rule => rule.Id).Should().Equal(outsideBefore.Id, outsideAfter.Id);
        result.Select(rule => rule.Priority).Should().Equal(1, 2);
    }

    [Fact]
    public void DescribeRule_IconSetIncludesStyleAndFlags()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1",
            IconSetShowValue = false,
            IconSetReverse = true
        };

        ManageConditionalFormatsDialog.DescribeRule(rule)
            .Should().Be("Icon Set: 3TrafficLights1 (reverse, icons only)");
    }

    [Theory]
    [InlineData(CfRuleType.ContainsText, "Text contains \"urgent\"")]
    [InlineData(CfRuleType.DateOccurring, "Date occurring: Last 7 Days")]
    [InlineData(CfRuleType.DuplicateValues, "Duplicate Values")]
    [InlineData(CfRuleType.UniqueValues, "Unique Values")]
    public void DescribeRule_LongTailHighlightRulesUseExcelLabels(CfRuleType ruleType, string expected)
    {
        var rule = new ConditionalFormat
        {
            RuleType = ruleType,
            TextRuleText = "urgent",
            DateOccurringPeriod = "last7Days"
        };

        ManageConditionalFormatsDialog.DescribeRule(rule).Should().Be(expected);
    }

    [Fact]
    public void PreviewBrush_IconSetUsesNeutralBrush()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.IconSet,
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) }
        };

        ManageConditionalFormatsDialog.PreviewBrush(rule).Should().BeSameAs(Brushes.LightGray);
    }

    [Fact]
    public void PreviewBrush_DataBarUsesRuleColor()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.DataBar,
            DataBarColor = new RgbColor(91, 155, 213)
        };

        var brush = ManageConditionalFormatsDialog.PreviewBrush(rule).Should().BeOfType<SolidColorBrush>().Subject;
        brush.Color.Should().Be(Color.FromRgb(91, 155, 213));
    }

    [Fact]
    public void PreviewBrush_ColorScaleUsesGradientPreview()
    {
        var rule = new ConditionalFormat
        {
            RuleType = CfRuleType.ColorScale,
            MinColor = new RgbColor(99, 190, 123),
            MaxColor = new RgbColor(248, 105, 107)
        };

        var brush = ManageConditionalFormatsDialog.PreviewBrush(rule).Should().BeOfType<LinearGradientBrush>().Subject;
        brush.GradientStops.Should().ContainSingle(stop => stop.Color == Color.FromRgb(99, 190, 123));
        brush.GradientStops.Should().ContainSingle(stop => stop.Color == Color.FromRgb(248, 105, 107));
    }

    [Fact]
    public void AppliesToColumn_UsesEditableRangeTextAndPickerButton()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ManageConditionalFormatsDialog.cs"));

        source.Should().Contain("typeof(TextBox)");
        source.Should().Contain("typeof(Button)");
        source.Should().Contain("new Binding(nameof(ConditionalFormat.AppliesTo))");
        source.Should().Contain("new AppliesToRangeConverter(_sheet.Id)");
        source.Should().Contain("ToolTipProperty, \"Collapse dialog and select Applies To range\"");
        source.Should().Contain("AutomationProperties.NameProperty, \"Select Applies To range\"");
        source.Should().Contain("AutomationProperties.HelpTextProperty");
        source.Should().Contain("RangePickerButton_Click");
        source.Should().Contain("AppliesToRangeSelectionRequest = CreateAppliesToRangeSelectionRequest");
        source.Should().Contain("_requestAppliesToRangeSelection?.Invoke(AppliesToRangeSelectionRequest)");
        source.Should().Contain("RelativeSourceMode.FindAncestor, typeof(ListViewItem), 1");
        source.Should().Contain("SetBinding(UIElement.IsEnabledProperty, new Binding(\"IsSelected\")");
    }

    [Fact]
    public void CreateAppliesToRangeSelectionRequest_UsesExcelCollapseIntent()
    {
        var ruleId = Guid.NewGuid();

        ManageConditionalFormatsDialog.CreateAppliesToRangeSelectionRequest(ruleId, " $A$1:$C$5 ")
            .Should()
            .Be(new ConditionalFormatAppliesToRangeSelectionRequest(ruleId, "$A$1:$C$5", CollapseDialog: true));
    }

    [Fact]
    public void TryParseAppliesToText_AcceptsExcelAbsoluteRangeText()
    {
        var sheetId = SheetId.New();
        var fallback = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1));

        ManageConditionalFormatsDialog.TryParseAppliesToText("$B$2:$D$5", sheetId, fallback)
            .Should().Be(new GridRange(new CellAddress(sheetId, 2, 2), new CellAddress(sheetId, 5, 4)));
    }

    [Fact]
    public void AppliesToRangeConverter_InvalidTextRejectsEditInsteadOfFallingBackToA1()
    {
        var sheetId = SheetId.New();
        var converter = new AppliesToRangeConverter(sheetId);

        converter.ConvertBack("not a range", typeof(GridRange), parameter: null!, System.Globalization.CultureInfo.InvariantCulture)
            .Should().BeSameAs(Binding.DoNothing);
    }

    [Fact]
    public void StopIfTrueText_ShowsEnabledRules()
    {
        var rule = new ConditionalFormat { StopIfTrue = true };

        ManageConditionalFormatsDialog.StopIfTrueText(rule).Should().Be("Yes");
    }

    [Fact]
    public void StopIfTrueColumn_UsesEditableTwoWayCheckbox()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ManageConditionalFormatsDialog.cs"));

        source.Should().Contain("typeof(CheckBox)");
        source.Should().Contain("nameof(ConditionalFormat.StopIfTrue)");
        source.Should().Contain("BindingMode.TwoWay");
        source.Should().Contain("UpdateSourceTrigger.PropertyChanged");
    }

    [Fact]
    public void FormatPreviewColumn_ShowsExcelStyleSampleText()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ManageConditionalFormatsDialog.cs"));

        source.Should().Contain("Header = \"Format\"");
        source.Should().Contain("typeof(Border)");
        source.Should().Contain("typeof(TextBlock)");
        source.Should().Contain("AaBbCcYyZz");
        source.Should().Contain("new PreviewForegroundBrushConverter()");
        source.Should().Contain("new PreviewFontWeightConverter()");
        source.Should().Contain("new PreviewFontStyleConverter()");
        source.Should().Contain("new PreviewTextDecorationsConverter()");
    }

    [Fact]
    public void PreviewForegroundBrush_UsesConditionalFormatFontColor()
    {
        var sheetId = SheetId.New();
        var rule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)),
            FormatIfTrue = new CellStyle { FontColor = new CellColor(12, 34, 56) }
        };

        var brush = ManageConditionalFormatsDialog.PreviewForegroundBrush(rule)
            .Should()
            .BeOfType<SolidColorBrush>()
            .Subject;
        brush.Color.Should().Be(Color.FromRgb(12, 34, 56));
    }

    [Fact]
    public void DialogCommands_ExposeKeyboardAccessKeys()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ManageConditionalFormatsDialog.cs"));

        foreach (var content in new[]
        {
            "_OK",
            "_Cancel",
            "_Apply",
            "_New Rule...",
            "_Edit Rule",
            "D_uplicate Rule",
            "_Delete Rule"
        })
        source.Should().Contain($"Content = \"{content}\"");

        source.Should().Contain("Content = \"Show formatting _rules for:\"");
        source.Should().Contain("Target = _scopeBox");
        source.Should().Contain("ToolTip = \"Move selected rule up\"");
        source.Should().Contain("ToolTip = \"Move selected rule down\"");
        source.Should().Contain("AutomationProperties.SetName(_moveUpBtn, \"Move Up\")");
        source.Should().Contain("AutomationProperties.SetName(_moveDownBtn, \"Move Down\")");
    }

    [Fact]
    public void ScopeSelector_UsesExcelWorksheetLabelAndDefaultsToSelectionWhenAvailable()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ManageConditionalFormatsDialog.cs"));

        source.Should().Contain("ScopeSheet     = \"This Worksheet\"");
        source.Should().Contain("ScopeSelection = \"Current Selection\"");
        source.Should().Contain("_scopeBox.SelectedItem = selection.HasValue ? ScopeSelection : ScopeSheet");
    }

    [Fact]
    public void DialogOpenedFromKeyboard_FocusesScopeSelector()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ManageConditionalFormatsDialog.cs"));

        source.Should().Contain("Loaded += (_, _) => FocusInitialKeyboardTarget();");
        source.Should().Contain("private void FocusInitialKeyboardTarget()");
        source.Should().Contain("_scopeBox.Focus();");
        source.Should().Contain("Keyboard.Focus(_scopeBox);");
    }

    [Fact]
    public void ScopeSelector_DefaultsToCurrentSelectionWhenSelectionIsProvided()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book").AddSheet("Sheet1");
            var selection = new GridRange(new CellAddress(sheet.Id, 2, 2), new CellAddress(sheet.Id, 4, 4));
            var dialog = new ManageConditionalFormatsDialog(sheet, selection);

            var scope = GetControl<ComboBox>(dialog, "_scopeBox");

            scope.SelectedItem.Should().Be("Current Selection");
            scope.Items.Cast<string>().Should().Equal("This Worksheet", "Current Selection");

            dialog.Close();
        });
    }

    [Fact]
    public void ToolbarButtons_EnableOnlyValidSelectedRuleActions()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book").AddSheet("Sheet1");
            sheet.ConditionalFormats.Add(CreateRule(sheet.Id, 1, 1, 1));
            sheet.ConditionalFormats.Add(CreateRule(sheet.Id, 2, 1, 2));
            var dialog = new ManageConditionalFormatsDialog(sheet, selection: null);

            var listView = GetControl<ListView>(dialog, "_listView");
            var editButton = GetControl<Button>(dialog, "_editBtn");
            var duplicateButton = GetControl<Button>(dialog, "_duplicateBtn");
            var deleteButton = GetControl<Button>(dialog, "_deleteBtn");
            var moveUpButton = GetControl<Button>(dialog, "_moveUpBtn");
            var moveDownButton = GetControl<Button>(dialog, "_moveDownBtn");

            editButton.IsEnabled.Should().BeFalse();
            duplicateButton.IsEnabled.Should().BeFalse();
            deleteButton.IsEnabled.Should().BeFalse();
            moveUpButton.IsEnabled.Should().BeFalse();
            moveDownButton.IsEnabled.Should().BeFalse();

            listView.SelectedIndex = 0;
            editButton.IsEnabled.Should().BeTrue();
            duplicateButton.IsEnabled.Should().BeTrue();
            deleteButton.IsEnabled.Should().BeTrue();
            moveUpButton.IsEnabled.Should().BeFalse();
            moveDownButton.IsEnabled.Should().BeTrue();

            listView.SelectedIndex = 1;
            editButton.IsEnabled.Should().BeTrue();
            duplicateButton.IsEnabled.Should().BeTrue();
            deleteButton.IsEnabled.Should().BeTrue();
            moveUpButton.IsEnabled.Should().BeTrue();
            moveDownButton.IsEnabled.Should().BeFalse();

            dialog.Close();
        });
    }

    [Fact]
    public void SelectionGuardCommands_FocusRulesListWhenNoRuleIsSelected()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ManageConditionalFormatsDialog.cs"));

        source.Should().Contain("FocusRulesList();");
        source.Should().Contain("private void FocusRulesList()");
        source.Should().Contain("_listView.Focus();");
        source.Should().Contain("Keyboard.Focus(_listView);");
    }

    [Fact]
    public void DuplicateRuleCommand_InsertsCopyBelowSelectedRuleWithNewIdentity()
    {
        StaTestRunner.Run(() =>
        {
            var sheet = new Workbook("Book").AddSheet("Sheet1");
            var first = CreateRule(sheet.Id, 1, 1, 1);
            var second = CreateRule(sheet.Id, 2, 1, 2);
            first.StopIfTrue = true;
            sheet.ConditionalFormats.Add(first);
            sheet.ConditionalFormats.Add(second);
            var dialog = new ManageConditionalFormatsDialog(sheet, selection: null);

            var listView = GetControl<ListView>(dialog, "_listView");
            var duplicateButton = GetControl<Button>(dialog, "_duplicateBtn");

            listView.SelectedIndex = 0;
            duplicateButton.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            listView.Items.Count.Should().Be(3);
            listView.SelectedIndex.Should().Be(1);

            var copied = listView.SelectedItem.Should().BeOfType<ConditionalFormat>().Subject;
            copied.Id.Should().NotBe(first.Id);
            copied.AppliesTo.Should().Be(first.AppliesTo);
            copied.StopIfTrue.Should().BeTrue();
            listView.Items.Cast<ConditionalFormat>().Select(rule => rule.Priority).Should().Equal(1, 2, 3);

            dialog.Close();
        });
    }

    [Fact]
    public void NewRuleCommand_OpensSingleExcelStyleRuleDialogEntryPoint()
    {
        var source = File.ReadAllText(WorkspaceFileLocator.Find("src", "Freexcel.App.Host", "ManageConditionalFormatsDialog.cs"));

        source.Should().Contain("Content = \"_New Rule...\"");
        source.Should().Contain("new NewConditionalFormatRuleDialog(\"Greater Than\", defaultRange)");
        source.Should().NotContain("new ConditionalFormatDialog(\"Greater Than\", defaultRange)");
        source.Should().NotContain("_newRuleTypeBox");
        source.Should().NotContain("toolBar.Children.Add(_newRuleTypeBox)");
    }

    [Fact]
    public void CloneWithPriority_PreservesAdvancedConditionalFormatFields()
    {
        var source = new ConditionalFormat
        {
            Id = Guid.NewGuid(),
            AppliesTo = new GridRange(new CellAddress(SheetId.New(), 2, 2), new CellAddress(SheetId.New(), 5, 4)),
            Priority = 7,
            RuleType = CfRuleType.IconSet,
            Operator = CfOperator.Between,
            Value1 = "1",
            Value2 = "10",
            FormatIfTrue = new CellStyle { Bold = true, FillColor = new CellColor(1, 2, 3) },
            MinColor = new RgbColor(10, 20, 30),
            MidColor = new RgbColor(40, 50, 60),
            MaxColor = new RgbColor(70, 80, 90),
            UseThreeColorScale = true,
            MinThresholdType = CfThresholdType.Number,
            MinThresholdValue = "5",
            MidThresholdType = CfThresholdType.Percent,
            MidThresholdValue = "50",
            MaxThresholdType = CfThresholdType.Formula,
            MaxThresholdValue = "A1",
            DataBarColor = new RgbColor(9, 8, 7),
            DataBarMinThresholdType = CfThresholdType.Percentile,
            DataBarMinThresholdValue = "10",
            DataBarMaxThresholdType = CfThresholdType.Number,
            DataBarMaxThresholdValue = "99",
            DataBarShowValue = false,
            DataBarMinLength = 5,
            DataBarMaxLength = 95,
            AboveAverage = false,
            FormulaText = "A1>0",
            IconSetStyle = "5Arrows",
            IconSetShowValue = false,
            IconSetReverse = true,
            TopBottomRank = 3,
            TopBottomPercent = true,
            TextRuleText = "urgent",
            DateOccurringPeriod = "last7Days",
            StopIfTrue = true
        };

        var clone = CloneWithPriority(source, 2);

        clone.Priority.Should().Be(2);
        clone.Id.Should().Be(source.Id);
        clone.Should().BeEquivalentTo(source, options => options
            .Excluding(rule => rule.Priority)
            .Excluding(rule => rule.FormatIfTrue));
        clone.FormatIfTrue.Should().NotBeSameAs(source.FormatIfTrue);
        clone.FormatIfTrue.Should().Be(source.FormatIfTrue);
    }

    private static ConditionalFormat CloneWithPriority(ConditionalFormat source, int priority)
    {
        var method = typeof(ManageConditionalFormatsDialog)
            .GetMethod("CloneWithPriority", BindingFlags.Static | BindingFlags.NonPublic);
        method.Should().NotBeNull();
        return method!.Invoke(null, [source, priority, null]).Should().BeOfType<ConditionalFormat>().Subject;
    }

    private static T GetControl<T>(ManageConditionalFormatsDialog dialog, string name)
        where T : class
    {
        var field = typeof(ManageConditionalFormatsDialog).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();
        return field!.GetValue(dialog).Should().BeOfType<T>().Subject;
    }

    private static ConditionalFormat CreateRule(
        SheetId sheetId,
        uint row,
        uint col,
        int priority,
        Guid? id = null,
        bool stopIfTrue = false) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            AppliesTo = new GridRange(new CellAddress(sheetId, row, col), new CellAddress(sheetId, row, col)),
            Priority = priority,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "1",
            StopIfTrue = stopIfTrue
        };
}
