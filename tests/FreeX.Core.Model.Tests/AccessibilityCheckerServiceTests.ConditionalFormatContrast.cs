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
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatBooleanReference()
    {
        AssertFormulaBooleanContrastLocations("=$C1", "B1", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatBooleanLiterals()
    {
        AssertFormulaBooleanContrastLocations("=TRUE", "B1", "B2", "B3", "B4");
        AssertFormulaBooleanContrastLocations("=FALSE");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatNumericLiterals()
    {
        AssertFormulaBooleanContrastLocations("=1", "B1", "B2", "B3", "B4");
        AssertFormulaBooleanContrastLocations("=0");
        AssertFormulaBooleanContrastLocations("=-1", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatNumericReferencePredicate()
    {
        AssertFormulaNumericTruthyContrastLocations("=$A1", "B1", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDateReferencePredicate()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var firstLabel = new CellAddress(sheet.Id, 1, 2);
        var secondLabel = new CellAddress(sheet.Id, 2, 2);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(DateTime.Today));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new DateTimeValue(0));
        sheet.SetCell(firstLabel, new TextValue("Current date"));
        sheet.SetCell(secondLabel, new TextValue("Zero date"));
        AddFormulaContrastRule(sheet, firstLabel, secondLabel, "=$A1");

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal("B1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatLogicalBooleanReference()
    {
        AssertFormulaBooleanContrastLocations("AND($A1>=100,$C1)", "B2");
        AssertFormulaBooleanContrastLocations("OR($A1>=100,$C1)", "B1", "B2", "B3");
        AssertFormulaBooleanContrastLocations("NOT($C1)", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatLogicalNumericReference()
    {
        AssertFormulaNumericTruthyContrastLocations("=AND($A1,$C1)", "B1");
        AssertFormulaNumericTruthyContrastLocations("=OR($A1,$C1)", "B1", "B2", "B3");
        AssertFormulaNumericTruthyContrastLocations("=NOT($A1)", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatIsPredicates()
    {
        AssertFormulaPredicateContrastLocations("ISBLANK($A1)", "B1");
        AssertFormulaPredicateContrastLocations("ISNUMBER($A1)", "B2", "B3");
        AssertFormulaPredicateContrastLocations("ISTEXT($A1)", "B4");
        AssertFormulaPredicateContrastLocations("ISNONTEXT($A1)", "B1", "B2", "B3", "B5", "B6", "B7");
        AssertFormulaPredicateContrastLocations("ISLOGICAL($A1)", "B5");
        AssertFormulaPredicateContrastLocations("ISERROR($A1)", "B6", "B7");
        AssertFormulaPredicateContrastLocations("ISERR($A1)", "B6");
        AssertFormulaPredicateContrastLocations("ISNA($A1)", "B7");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatIsPredicateLiterals()
    {
        AssertFormulaPredicateContrastLocations("ISNUMBER(42)", FormulaPredicateAllLocations);
        AssertFormulaPredicateContrastLocations("ISTEXT(\"Revenue\")", FormulaPredicateAllLocations);
        AssertFormulaPredicateContrastLocations("ISLOGICAL(TRUE)", FormulaPredicateAllLocations);
        AssertFormulaPredicateContrastLocations("ISERROR(#VALUE!)", FormulaPredicateAllLocations);
        AssertFormulaPredicateContrastLocations("ISERR(#VALUE!)", FormulaPredicateAllLocations);
        AssertFormulaPredicateContrastLocations("ISNA(#N/A)", FormulaPredicateAllLocations);
        AssertFormulaPredicateContrastLocations("ISNONTEXT(\"Revenue\")");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatNestedIsPredicates()
    {
        AssertFormulaPredicateContrastLocations("AND(ISNUMBER($A1),$A1>=40)", "B2", "B3");
        AssertFormulaPredicateContrastLocations("OR(ISERROR($A1),$C1)", "B5", "B6", "B7");
        AssertFormulaPredicateContrastLocations("NOT(ISNA($A1))", "B1", "B2", "B3", "B4", "B5", "B6");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatIsEvenIsOddReferences()
    {
        AssertFormulaParityContrastLocations("ISEVEN($A1)", "B1", "B3", "B5");
        AssertFormulaParityContrastLocations("ISODD($A1)", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatIsEvenIsOddLiterals()
    {
        AssertFormulaParityContrastLocations("ISEVEN(2.9)", FormulaParityAllLocations);
        AssertFormulaParityContrastLocations("ISODD(-3.2)", FormulaParityAllLocations);
        AssertFormulaParityContrastLocations("ISEVEN(3)");
        AssertFormulaParityContrastLocations("ISODD(4)");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatNestedIsEvenIsOddPredicates()
    {
        AssertFormulaParityContrastLocations("AND(ISEVEN($A1),$C1)", "B1", "B5");
        AssertFormulaParityContrastLocations("OR(ISODD($A1),$C1)", "B1", "B2", "B4", "B5");
        AssertFormulaParityContrastLocations("NOT(ISODD($A1))", "B1", "B3", "B5");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatIsEvenIsOddForNonNumericOperands()
    {
        AssertFormulaParityContrastLocations("ISEVEN($D1)");
        AssertFormulaParityContrastLocations("ISODD($D1)");
        AssertFormulaParityContrastLocations("ISEVEN(\"2\")");
        AssertFormulaParityContrastLocations("ISODD(TRUE)");
        AssertFormulaParityContrastLocations("ISEVEN(#VALUE!)");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatIfBooleanBranches()
    {
        AssertFormulaIfContrastLocations("IF($A1>=100,TRUE,FALSE)", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatIfNumericBranches()
    {
        AssertFormulaIfContrastLocations("IF($A1>=100,1,0)", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatIfInvertedBranches()
    {
        AssertFormulaIfContrastLocations("IF($A1>=100,FALSE,TRUE)", "B1", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatIfInsideLogicalWrappers()
    {
        AssertFormulaIfContrastLocations("AND(IF($A1>=100,TRUE,FALSE),$C1=\"Open\")", "B4");
        AssertFormulaIfContrastLocations("OR(IF($A1>=100,FALSE,TRUE),$C1=\"Open\")", "B1", "B3", "B4");
        AssertFormulaIfContrastLocations("NOT(IF($A1>=100,TRUE,FALSE))", "B1", "B3");
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
    public void FindIssues_DoesNotMatchUnsupportedFormulaConditionalFormatInsideIfWrapper()
    {
        AssertFormulaIfContrastLocations("IF(SUM($A1)>0,TRUE,FALSE)");
        AssertFormulaIfContrastLocations("IF($A1>=100,SUM($A1),FALSE)");
    }

    [Fact]
    public void FindIssues_DoesNotMatchUnsupportedFormulaConditionalFormatInsideIsPredicate()
    {
        AssertFormulaPredicateContrastLocations("ISNUMBER(SUM($A1))");
        AssertFormulaParityContrastLocations("ISEVEN(SUM($A1))");
        AssertFormulaParityContrastLocations("ISODD(SUM($A1))");
        AssertFormulaPredicateContrastLocations("SUM($A1)>0");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatTextBlankOrErrorReferencePredicate()
    {
        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var firstLabel = new CellAddress(sheet.Id, 1, 2);
        var lastLabel = new CellAddress(sheet.Id, 3, 2);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("1"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), ErrorValue.Value);
        sheet.SetCell(firstLabel, new TextValue("Text"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Blank"));
        sheet.SetCell(lastLabel, new TextValue("Error"));
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, "=$A1");

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

    private static Workbook CreateFormulaBooleanContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 4, 2);

        SetFormulaBooleanContrastRow(sheet, 1, 75, flag: true, "Below active");
        SetFormulaBooleanContrastRow(sheet, 2, 100, flag: true, "Threshold active");
        SetFormulaBooleanContrastRow(sheet, 3, 125, flag: false, "Escalated inactive");
        SetFormulaBooleanContrastRow(sheet, 4, 75, flag: false, "Below inactive");

        return workbook;
    }

    private static void SetFormulaBooleanContrastRow(
        Sheet sheet,
        uint row,
        double amount,
        bool flag,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(amount));
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), new BoolValue(flag));
    }

    private static Workbook CreateFormulaNumericTruthyContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 4, 2);

        SetFormulaNumericTruthyContrastRow(sheet, 1, left: 1, right: 1, "Both nonzero");
        SetFormulaNumericTruthyContrastRow(sheet, 2, left: 1, right: 0, "Left nonzero");
        SetFormulaNumericTruthyContrastRow(sheet, 3, left: 0, right: 1, "Right nonzero");
        SetFormulaNumericTruthyContrastRow(sheet, 4, left: 0, right: 0, "Both zero");

        return workbook;
    }

    private static void SetFormulaNumericTruthyContrastRow(
        Sheet sheet,
        uint row,
        double left,
        double right,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(left));
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(right));
    }

    private static Workbook CreateFormulaPredicateContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 7, 2);

        SetFormulaPredicateContrastRow(sheet, 1, BlankValue.Instance, flag: false, "Blank source");
        SetFormulaPredicateContrastRow(sheet, 2, new NumberValue(42), flag: false, "Number source");
        SetFormulaPredicateContrastRow(sheet, 3, DateTimeValue.FromDateTime(DateTime.Today), flag: false, "Date source");
        SetFormulaPredicateContrastRow(sheet, 4, new TextValue("Revenue"), flag: false, "Text source");
        SetFormulaPredicateContrastRow(sheet, 5, new BoolValue(true), flag: true, "Logical source");
        SetFormulaPredicateContrastRow(sheet, 6, ErrorValue.Value, flag: false, "Error source");
        SetFormulaPredicateContrastRow(sheet, 7, ErrorValue.NA, flag: false, "NA source");

        return workbook;
    }

    private static void SetFormulaPredicateContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue source,
        bool flag,
        string label)
    {
        if (source is not BlankValue)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), source);

        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), new BoolValue(flag));
    }

    private static Workbook CreateFormulaParityContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 9, 2);

        SetFormulaParityContrastRow(sheet, 1, new NumberValue(2), flag: true, "Even source", new TextValue("Text source"));
        SetFormulaParityContrastRow(sheet, 2, new NumberValue(3), flag: true, "Odd source", new BoolValue(true));
        SetFormulaParityContrastRow(sheet, 3, new NumberValue(2.9), flag: false, "Truncated even source", BlankValue.Instance);
        SetFormulaParityContrastRow(sheet, 4, new NumberValue(-3.2), flag: false, "Negative odd source", ErrorValue.Value);
        SetFormulaParityContrastRow(sheet, 5, new DateTimeValue(45000), flag: true, "Even date serial source", new TextValue("45000"));
        SetFormulaParityContrastRow(sheet, 6, new TextValue("2"), flag: false, "Numeric text source", new TextValue("2"));
        SetFormulaParityContrastRow(sheet, 7, new BoolValue(true), flag: false, "Logical source", new BoolValue(false));
        SetFormulaParityContrastRow(sheet, 8, BlankValue.Instance, flag: false, "Blank source", BlankValue.Instance);
        SetFormulaParityContrastRow(sheet, 9, ErrorValue.Value, flag: false, "Error source", ErrorValue.NA);

        return workbook;
    }

    private static void SetFormulaParityContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue source,
        bool flag,
        string label,
        ScalarValue nonNumericSource)
    {
        if (source is not BlankValue)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), source);

        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), new BoolValue(flag));

        if (nonNumericSource is not BlankValue)
            sheet.SetCell(new CellAddress(sheet.Id, row, 4), nonNumericSource);
    }

    private static void AssertFormulaBooleanContrastLocations(string formulaText, params string[] expectedLocations)
    {
        var workbook = CreateFormulaBooleanContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaNumericTruthyContrastLocations(string formulaText, params string[] expectedLocations)
    {
        var workbook = CreateFormulaNumericTruthyContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaPredicateContrastLocations(string formulaText, params string[] expectedLocations)
    {
        var workbook = CreateFormulaPredicateContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaParityContrastLocations(string formulaText, params string[] expectedLocations)
    {
        var workbook = CreateFormulaParityContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaIfContrastLocations(string formulaText, params string[] expectedLocations)
    {
        var workbook = CreateFormulaLogicalContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static string[] FormulaParityAllLocations =>
        ["B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9"];

    private static string[] FormulaPredicateAllLocations =>
        ["B1", "B2", "B3", "B4", "B5", "B6", "B7"];

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
