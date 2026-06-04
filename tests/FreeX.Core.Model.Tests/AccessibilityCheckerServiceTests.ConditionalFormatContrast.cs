using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed partial class AccessibilityCheckerServiceTests
{
    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromMatchingConditionalFormat()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(address, new TextValue("At risk"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(address, address),
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "risk",
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(i => i.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("B2");
        issue.Message.Should().Be("Cell text should have at least 4.5:1 contrast against its fill.");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromDuplicateValuesConditionalFormat()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var first = new CellAddress(sheet.Id, 1, 1);
        var second = new CellAddress(sheet.Id, 2, 1);
        var third = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(first, new TextValue("West"));
        sheet.SetCell(second, new TextValue("West"));
        sheet.SetCell(third, new TextValue("North"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(first, third),
            RuleType = CfRuleType.DuplicateValues,
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText)
            .ToList();

        issues.Select(issue => issue.Location).Should().Equal("A1", "A2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromUniqueValuesConditionalFormat()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var first = new CellAddress(sheet.Id, 1, 1);
        var second = new CellAddress(sheet.Id, 2, 1);
        var third = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(first, new TextValue("West"));
        sheet.SetCell(second, new TextValue("West"));
        sheet.SetCell(third, new TextValue("North"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(first, third),
            RuleType = CfRuleType.UniqueValues,
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("A3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromAboveAverageConditionalFormat()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var first = new CellAddress(sheet.Id, 1, 1);
        var second = new CellAddress(sheet.Id, 2, 1);
        var third = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(first, new TextValue("10"));
        sheet.SetCell(second, new TextValue("20"));
        sheet.SetCell(third, new TextValue("30"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(first, third),
            RuleType = CfRuleType.AboveAverage,
            AboveAverage = true,
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("A3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromBelowAverageConditionalFormat()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var first = new CellAddress(sheet.Id, 1, 1);
        var second = new CellAddress(sheet.Id, 2, 1);
        var third = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(first, new TextValue("10"));
        sheet.SetCell(second, new TextValue("20"));
        sheet.SetCell(third, new TextValue("30"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(first, third),
            RuleType = CfRuleType.AboveAverage,
            AboveAverage = false,
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("A1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromTopRankedConditionalFormat()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var first = new CellAddress(sheet.Id, 1, 1);
        var second = new CellAddress(sheet.Id, 2, 1);
        var third = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(first, new TextValue("10"));
        sheet.SetCell(second, new TextValue("20"));
        sheet.SetCell(third, new TextValue("30"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(first, third),
            RuleType = CfRuleType.Top10,
            TopBottomRank = 2,
            AboveAverage = true,
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText)
            .ToList();

        issues.Select(issue => issue.Location).Should().Equal("A2", "A3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromBottomPercentConditionalFormat()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var first = new CellAddress(sheet.Id, 1, 1);
        var second = new CellAddress(sheet.Id, 2, 1);
        var third = new CellAddress(sheet.Id, 3, 1);
        sheet.SetCell(first, new TextValue("10"));
        sheet.SetCell(second, new TextValue("20"));
        sheet.SetCell(third, new TextValue("30"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(first, third),
            RuleType = CfRuleType.Top10,
            TopBottomRank = 50,
            TopBottomPercent = true,
            AboveAverage = false,
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText)
            .ToList();

        issues.Select(issue => issue.Location).Should().Equal("A1", "A2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromDateOccurringConditionalFormat()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var recent = new CellAddress(sheet.Id, 1, 1);
        var older = new CellAddress(sheet.Id, 2, 1);
        var today = DateTime.Today;
        sheet.SetCell(recent, DateTimeValue.FromDateTime(today.AddDays(-3)));
        sheet.SetCell(older, DateTimeValue.FromDateTime(today.AddDays(-8)));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(recent, older),
            RuleType = CfRuleType.DateOccurring,
            DateOccurringPeriod = "last7Days",
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        var issue = AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText).Subject;

        issue.Location.Should().Be("A1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatComparison()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var firstLabel = new CellAddress(sheet.Id, 1, 2);
        var secondLabel = new CellAddress(sheet.Id, 2, 2);
        var thirdLabel = new CellAddress(sheet.Id, 3, 2);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(75));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(100));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new NumberValue(125));
        sheet.SetCell(firstLabel, new TextValue("On track"));
        sheet.SetCell(secondLabel, new TextValue("At threshold"));
        sheet.SetCell(thirdLabel, new TextValue("Escalated"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(firstLabel, thirdLabel),
            RuleType = CfRuleType.Formula,
            FormulaText = "$A1>=100",
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        var issues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText)
            .ToList();

        issues.Select(issue => issue.Location).Should().Equal("B2", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAnd()
    {
        var workbook = CreateFormulaLogicalContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, "AND($A1>=100,$C1=\"Open\")");

        var issues = FindLowContrastCellTextIssues(workbook);

        issues.Select(issue => issue.Location).Should().Equal("B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatOr()
    {
        var workbook = CreateFormulaLogicalContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, "OR($A1>=100,$C1=\"Open\")");

        var issues = FindLowContrastCellTextIssues(workbook);

        issues.Select(issue => issue.Location).Should().Equal("B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatNot()
    {
        var workbook = CreateFormulaLogicalContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, "NOT($A1>=100)");

        var issues = FindLowContrastCellTextIssues(workbook);

        issues.Select(issue => issue.Location).Should().Equal("B1", "B3");
    }

    [Fact]
    public void FindIssues_DoesNotMatchUnsupportedFormulaConditionalFormatInsideLogicalWrapper()
    {
        var workbook = CreateFormulaLogicalContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, "AND($A1>=100,SUM($A1)>0)");

        FindLowContrastCellTextIssues(workbook).Should().BeEmpty();
    }

    [Fact]
    public void FindIssues_IgnoresConditionalFormatContrastWhenRuleDoesNotMatchCell()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(address, new TextValue("On track"));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(address, address),
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "risk",
            FormatIfTrue = new CellStyle
            {
                FontColor = new CellColor(120, 120, 120),
                FillColor = new CellColor(130, 130, 130)
            }
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(i => i.Kind == AccessibilityIssueKind.LowContrastCellText);
    }

    [Fact]
    public void FindIssues_LowContrastCellText_SharedConditionalRulesPreserveStopIfTrue()
    {
        var workbook = new Workbook("Accessibility");
        var stopSheet = workbook.AddSheet("Stop");
        var stopAddress = new CellAddress(stopSheet.Id, 1, 1);
        stopSheet.SetCell(stopAddress, new TextValue("Escalated"));
        AddNoBlankContrastRule(stopSheet, stopAddress, priority: 1, stopIfTrue: true, CellColor.Black, CellColor.White);
        AddNoBlankContrastRule(
            stopSheet,
            stopAddress,
            priority: 2,
            stopIfTrue: false,
            new CellColor(120, 120, 120),
            new CellColor(130, 130, 130));

        var stackSheet = workbook.AddSheet("Stack");
        var stackAddress = new CellAddress(stackSheet.Id, 1, 1);
        stackSheet.SetCell(stackAddress, new TextValue("Escalated"));
        AddNoBlankContrastRule(stackSheet, stackAddress, priority: 1, stopIfTrue: false, CellColor.Black, CellColor.White);
        AddNoBlankContrastRule(
            stackSheet,
            stackAddress,
            priority: 2,
            stopIfTrue: false,
            new CellColor(120, 120, 120),
            new CellColor(130, 130, 130));

        var lowContrastIssues = AccessibilityCheckerService.FindIssues(workbook)
            .Where(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText)
            .ToList();

        lowContrastIssues.Should().ContainSingle()
            .Which.SheetName.Should().Be("Stack");
    }

    private static Workbook CreateFormulaLogicalContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 4, 2);

        SetFormulaLogicalContrastRow(sheet, 1, 75, "Closed", "Below closed");
        SetFormulaLogicalContrastRow(sheet, 2, 100, "Closed", "Threshold closed");
        SetFormulaLogicalContrastRow(sheet, 3, 75, "Open", "Below open");
        SetFormulaLogicalContrastRow(sheet, 4, 125, "Open", "Escalated open");

        return workbook;
    }

    private static void SetFormulaLogicalContrastRow(
        Sheet sheet,
        uint row,
        double amount,
        string status,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(amount));
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), new TextValue(status));
    }

    private static void AddFormulaContrastRule(
        Sheet sheet,
        CellAddress firstLabel,
        CellAddress lastLabel,
        string formulaText)
    {
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(firstLabel, lastLabel),
            RuleType = CfRuleType.Formula,
            FormulaText = formulaText,
            FormatIfTrue = CreateLowContrastCellStyle()
        });
    }

    private static CellStyle CreateLowContrastCellStyle() => new()
    {
        FontColor = new CellColor(120, 120, 120),
        FillColor = new CellColor(130, 130, 130)
    };

    private static List<AccessibilityIssue> FindLowContrastCellTextIssues(Workbook workbook) =>
        AccessibilityCheckerService.FindIssues(workbook)
            .Where(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText)
            .ToList();
}
