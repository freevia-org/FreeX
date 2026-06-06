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
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatBooleanFunctions()
    {
        AssertFormulaBooleanContrastLocations("=TRUE()", "B1", "B2", "B3", "B4");
        AssertFormulaBooleanContrastLocations("=FALSE()");
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
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatBooleanFunctionsInsideLogicalWrappers()
    {
        AssertFormulaBooleanContrastLocations("AND(TRUE(),$A1>=100)", "B2", "B3");
        AssertFormulaBooleanContrastLocations("OR(FALSE(),$C1)", "B1", "B2");
        AssertFormulaBooleanContrastLocations("NOT(FALSE())", "B1", "B2", "B3", "B4");
        AssertFormulaBooleanContrastLocations("XOR(TRUE(),$C1)", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatLogicalNumericReference()
    {
        AssertFormulaNumericTruthyContrastLocations("=AND($A1,$C1)", "B1");
        AssertFormulaNumericTruthyContrastLocations("=OR($A1,$C1)", "B1", "B2", "B3");
        AssertFormulaNumericTruthyContrastLocations("=NOT($A1)", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatXor()
    {
        AssertFormulaBooleanContrastLocations("XOR($A1>=100,$C1,$A1<80)", "B3", "B4");
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
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatNaFunctionPredicatesAndWrappers()
    {
        AssertFormulaPredicateContrastLocations("ISNA(NA())", FormulaPredicateAllLocations);
        AssertFormulaPredicateContrastLocations("ISERROR(NA())", FormulaPredicateAllLocations);
        AssertFormulaPredicateContrastLocations("ISERR(NA())");
        AssertFormulaPredicateContrastLocations("NOT(ISNA(NA()))");
        AssertFormulaPredicateContrastLocations("IF(ISNA(NA()),TRUE,FALSE)", FormulaPredicateAllLocations);
        AssertFormulaPredicateContrastLocations("XOR(ISNA(NA()),$C1)", "B1", "B2", "B3", "B4", "B6", "B7");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatNaFunctionAsConditionOrWrongArity()
    {
        AssertFormulaPredicateContrastLocations("NA()");
        AssertFormulaPredicateContrastLocations("NA(1)");
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
        AssertFormulaIfContrastLocations("IF($A1>=100,TRUE(),FALSE())", "B2", "B4");
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
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatXorNestedWrappers()
    {
        AssertFormulaPredicateContrastLocations("XOR(ISNUMBER($A1),$C1)", "B2", "B3", "B5");
        AssertFormulaXorContrastLocations("XOR(IF($A1>=100,TRUE,FALSE),$C1=\"Open\")", "B2", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAggregateComparison()
    {
        AssertFormulaAggregateContrastLocations("SUM($A1)>100", "B4");
        AssertFormulaAggregateContrastLocations("SUM($A1:$A3)>250", "B2");
        AssertFormulaAggregateContrastLocations("SUM(25,$A1)>=100", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUM(\"5\",$A1)>80", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAggregateLogicalWrappers()
    {
        AssertFormulaAggregateContrastLocations("AND(SUM($A1)>100,$C1=\"Open\")", "B4");
        AssertFormulaAggregateContrastLocations("OR(SUM($A1)>100,$C1=\"Open\")", "B3", "B4");
        AssertFormulaAggregateContrastLocations("NOT(SUM($A1)>100)", "B1", "B2", "B3");
        AssertFormulaAggregateContrastLocations("XOR(SUM($A1)>100,$C1=\"Open\")", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAggregateIfWrappers()
    {
        AssertFormulaAggregateContrastLocations("IF(SUM($A1)>100,TRUE,FALSE)", "B4");
        AssertFormulaAggregateContrastLocations("IF($A1>=100,SUM($A1),FALSE)", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAggregatePredicates()
    {
        AssertFormulaAggregateContrastLocations("ISNUMBER(SUM($A1))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISEVEN(SUM($A1))", "B2");
        AssertFormulaAggregateContrastLocations("ISODD(SUM($A1))", "B1", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCommonAggregates()
    {
        AssertFormulaAggregateContrastLocations("AVERAGE($A1:$A3)>80", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("MIN($A1:$A3)>=75", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("MAX($A1:$A3)>=125", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("COUNT($A1:$A3)>=2", "B1", "B2", "B3");
        AssertFormulaAggregateContrastLocations("COUNT($D1:$D3)>=1", "B1", "B2");
        AssertFormulaAggregateContrastLocations("SUM($D1:$D3)>0", "B1", "B2");
        AssertFormulaAggregateContrastLocations("COUNTA($D1:$D3)>=2", "B1", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatMedianAggregate()
    {
        AssertFormulaAggregateContrastLocations("MEDIAN($A1:$A3)=75", "B1");
        AssertFormulaAggregateContrastLocations("MEDIAN($A1:$A2)=87.5", "B1", "B2");
        AssertFormulaAggregateContrastLocations("MEDIAN($D1:$D3)=12", "B1", "B2");
        AssertFormulaAggregateContrastLocations("MEDIAN(25,$A1,125)=75", "B1", "B3");
        AssertFormulaAggregateContrastLocations("MEDIAN(\"25\",$A1,125)=75", "B1", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatMedianWrappersPredicatesAndNestedAggregates()
    {
        AssertFormulaAggregateContrastLocations("AND(MEDIAN($A1:$A2)>=87.5,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaAggregateContrastLocations("IF(MEDIAN($A1:$A3)>100,TRUE,FALSE)", "B4");
        AssertFormulaAggregateContrastLocations("ISNUMBER(MEDIAN($A1:$A3))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("MEDIAN($A1:$A2)+12.5=100", "B1", "B2");
        AssertFormulaAggregateContrastLocations("SUM(MEDIAN($A1:$A2),12.5)=100", "B1", "B2");
        AssertFormulaAggregateContrastLocations("MEDIAN(SUM($A1:$A2),$A1^2)>5000", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatSumSqAggregate()
    {
        AssertFormulaAggregateContrastLocations("SUMSQ($A1)>=10000", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUMSQ($A1:$A3)>30000", "B2");
        AssertFormulaAggregateContrastLocations("SUMSQ(2,$A1)>10000", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUMSQ(\"2\",$A1)>10000", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUMSQ($D1:$D3)=144", "B1", "B2");
        AssertFormulaAggregateContrastLocations("SUMSQ($D3:$D5)=0", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatSumSqWrappersPredicatesAndNestedAggregates()
    {
        AssertFormulaAggregateContrastLocations("AND(SUMSQ($A1:$A2)>15000,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaAggregateContrastLocations("IF(SUMSQ($A1)>10000,TRUE,FALSE)", "B4");
        AssertFormulaAggregateContrastLocations("ISNUMBER(SUMSQ($A1:$A3))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUM(SUMSQ($A1),1)>10000", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUMSQ(SUM($A1:$A2),$A1^2)>100000000", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDevSqAggregate()
    {
        AssertFormulaAggregateContrastLocations("DEVSQ($A1:$A3)>400", "B1", "B2", "B3");
        AssertFormulaAggregateContrastLocations("DEVSQ($A1:$A2)=312.5", "B1", "B2");
        AssertFormulaAggregateContrastLocations("DEVSQ($D1:$D3)=0", "B1", "B2");
        AssertFormulaAggregateContrastLocations("DEVSQ(25,$A1,125)=5000", "B1", "B3");
        AssertFormulaAggregateContrastLocations("DEVSQ(\"25\",$A1,125)=5000", "B1", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDevSqWrappersPredicatesAndNestedAggregates()
    {
        AssertFormulaAggregateContrastLocations("AND(DEVSQ($A1:$A2)>300,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaAggregateContrastLocations("IF(DEVSQ($A1:$A3)>1000,TRUE,FALSE)", "B2", "B3");
        AssertFormulaAggregateContrastLocations("ISNUMBER(DEVSQ($A1:$A3))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUM(DEVSQ($A1:$A2),12.5)=325", "B1", "B2");
        AssertFormulaAggregateContrastLocations("DEVSQ(SUM($A1:$A2),$A1^2)>45000000", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAveDevAggregate()
    {
        AssertFormulaAggregateContrastLocations("AVEDEV($A1:$A3)>20", "B3");
        AssertFormulaAggregateContrastLocations("AVEDEV($A1:$A2)=12.5", "B1", "B2");
        AssertFormulaAggregateContrastLocations("AVEDEV($D1:$D3)=0", "B1", "B2");
        AssertFormulaAggregateContrastLocations("AVEDEV(25,$A1,125)>35", "B2", "B4");
        AssertFormulaAggregateContrastLocations("AVEDEV(\"25\",$A1,125)>35", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAveDevWrappersPredicatesAndNestedAggregates()
    {
        AssertFormulaAggregateContrastLocations("AND(AVEDEV($A1:$A2)>=12.5,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaAggregateContrastLocations("IF(AVEDEV($A1:$A3)>20,TRUE,FALSE)", "B3");
        AssertFormulaAggregateContrastLocations("ISNUMBER(AVEDEV($A1:$A3))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUM(AVEDEV($A1:$A2),12.5)=25", "B1", "B2");
        AssertFormulaAggregateContrastLocations("AVEDEV(SUM($A1:$A2),$A1^2)>4000", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatPositiveMeanAggregates()
    {
        AssertFormulaAggregateContrastLocations("GEOMEAN($A1:$A3)>80", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("GEOMEAN($D1:$D3)=12", "B1", "B2");
        AssertFormulaAggregateContrastLocations("GEOMEAN(25,$A1,125)>65", "B2", "B4");
        AssertFormulaAggregateContrastLocations("GEOMEAN(\"25\",$A1,125)>65", "B2", "B4");
        AssertFormulaAggregateContrastLocations("HARMEAN($A1:$A2)>90", "B3", "B4");
        AssertFormulaAggregateContrastLocations("HARMEAN($D1:$D3)=12", "B1", "B2");
        AssertFormulaAggregateContrastLocations("HARMEAN(25,$A1,125)>50", "B2", "B4");
        AssertFormulaAggregateContrastLocations("HARMEAN(\"25\",$A1,125)>50", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatPositiveMeanWrappersPredicatesAndNestedAggregates()
    {
        AssertFormulaAggregateContrastLocations("AND(GEOMEAN($A1:$A2)>85,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaAggregateContrastLocations("IF(HARMEAN($A1:$A2)>90,TRUE,FALSE)", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISNUMBER(GEOMEAN($A1:$A3))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUM(HARMEAN($A1:$A2),5)>95", "B3", "B4");
        AssertFormulaAggregateContrastLocations("GEOMEAN(SUM($A1:$A2),$A1^2)>1000", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("HARMEAN(SUM($A1:$A2),$A1^2)>300", "B1", "B2", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatStdDevAggregates()
    {
        AssertFormulaAggregateContrastLocations("STDEV($A1:$A3)>20", "B2", "B3");
        AssertFormulaAggregateContrastLocations("STDEV.S($A1:$A2)>20", "B3");
        AssertFormulaAggregateContrastLocations("STDEVP($A1:$A3)>20", "B2", "B3");
        AssertFormulaAggregateContrastLocations("STDEV.P($A1:$A2)>20", "B3");
        AssertFormulaAggregateContrastLocations("STDEV.P($A1)=0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("STDEVP($D1:$D3)=0", "B1", "B2");
        AssertFormulaAggregateContrastLocations("STDEV(25,$A1,125)=50", "B1", "B3");
        AssertFormulaAggregateContrastLocations("STDEV.S(\"25\",$A1,125)=50", "B1", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatStdDevWrappersPredicatesAndNestedAggregates()
    {
        AssertFormulaAggregateContrastLocations("AND(STDEV($A1:$A3)>20,$C1=\"Open\")", "B3");
        AssertFormulaAggregateContrastLocations("IF(STDEV.S($A1:$A3)>20,TRUE,FALSE)", "B2", "B3");
        AssertFormulaAggregateContrastLocations("ISNUMBER(STDEV.P($A1:$A3))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUM(STDEVP($A1:$A2),12.5)>30", "B3");
        AssertFormulaAggregateContrastLocations("STDEV.P(SUM($A1:$A2),$A1^2)>3000", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatVarianceAggregates()
    {
        AssertFormulaAggregateContrastLocations("VAR($A1:$A3)>500", "B2", "B3");
        AssertFormulaAggregateContrastLocations("VAR.S($A1:$A2)>500", "B3");
        AssertFormulaAggregateContrastLocations("VARP($A1:$A3)>500", "B3");
        AssertFormulaAggregateContrastLocations("VAR.P($A1)=0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("VARP($D1:$D3)=0", "B1", "B2");
        AssertFormulaAggregateContrastLocations("VAR(25,$A1,125)=2500", "B1", "B3");
        AssertFormulaAggregateContrastLocations("VAR.S(\"25\",$A1,125)=2500", "B1", "B3");
        AssertFormulaAggregateContrastLocations("VAR.P(25,$A1,125)>1600", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatVarianceWrappersPredicatesAndNestedAggregates()
    {
        AssertFormulaAggregateContrastLocations("AND(VAR($A1:$A3)>500,$C1=\"Open\")", "B3");
        AssertFormulaAggregateContrastLocations("IF(VAR.S($A1:$A3)>500,TRUE,FALSE)", "B2", "B3");
        AssertFormulaAggregateContrastLocations("ISNUMBER(VAR.P($A1:$A3))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUM(VARP($A1:$A2),12.5)>300", "B3");
        AssertFormulaAggregateContrastLocations("VAR.P(SUM($A1:$A2),$A1^2)>20000000", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatProductAggregate()
    {
        AssertFormulaAggregateContrastLocations("PRODUCT($A1)>100", "B4");
        AssertFormulaAggregateContrastLocations("PRODUCT($A1:$A3)>900000", "B2");
        AssertFormulaAggregateContrastLocations("PRODUCT(2,$A1)>150", "B2", "B4");
        AssertFormulaAggregateContrastLocations("PRODUCT(\"2\",$A1)>150", "B2", "B4");
        AssertFormulaAggregateContrastLocations("PRODUCT($D1:$D3)=12", "B1", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatProductWrappersPredicatesAndNestedAggregates()
    {
        AssertFormulaAggregateContrastLocations("AND(PRODUCT($A1:$A2)>7000,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaAggregateContrastLocations("IF(PRODUCT($A1)>100,TRUE,FALSE)", "B4");
        AssertFormulaAggregateContrastLocations("ISNUMBER(PRODUCT($A1:$A3))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUM(PRODUCT($A1),1)>100", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCountBlankAggregate()
    {
        AssertFormulaAggregateContrastLocations("COUNTBLANK($D1)=1", "B3");
        AssertFormulaAggregateContrastLocations("COUNTBLANK($D1:$D3)=1", "B1", "B2");
        AssertFormulaAggregateContrastLocations("COUNTBLANK($D1:$D3)>=2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCountBlankWrappersPredicatesAndNestedAggregates()
    {
        AssertFormulaAggregateContrastLocations("AND(COUNTBLANK($D1:$D3)>=2,$C1=\"Open\")", "B3", "B4");
        AssertFormulaAggregateContrastLocations("IF(COUNTBLANK($D1:$D3)>=2,TRUE,FALSE)", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISNUMBER(COUNTBLANK($D1:$D3))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("COUNTBLANK($D1:$D3)+1=2", "B1", "B2");
        AssertFormulaAggregateContrastLocations("SUM(COUNTBLANK($D1:$D3),1)>=3", "B3", "B4");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatAggregateForOversizedRangeOrInvalidDirectText()
    {
        AssertFormulaAggregateContrastLocations("SUM($A1:$A20000)>0");
        AssertFormulaAggregateContrastLocations("SUM(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("MEDIAN()>0");
        AssertFormulaAggregateContrastLocations("MEDIAN($A1:$A20000)>0");
        AssertFormulaAggregateContrastLocations("MEDIAN(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("MEDIAN($D3:$D5)>0");
        AssertFormulaAggregateContrastLocations("MEDIAN($A1/0)>0");
        AssertFormulaAggregateContrastLocations("MEDIAN(1E308,1E308)>0");
        AssertFormulaAggregateContrastLocations("MEDIAN(A0)>0");
        AssertFormulaAggregateContrastLocations("DEVSQ()>0");
        AssertFormulaAggregateContrastLocations("DEVSQ($A1:$A20000)>0");
        AssertFormulaAggregateContrastLocations("DEVSQ(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("DEVSQ($D3:$D5)>0");
        AssertFormulaAggregateContrastLocations("DEVSQ($A1/0)>0");
        AssertFormulaAggregateContrastLocations("DEVSQ(1E308,0)>0");
        AssertFormulaAggregateContrastLocations("DEVSQ(A0)>0");
        AssertFormulaAggregateContrastLocations("AVEDEV()>0");
        AssertFormulaAggregateContrastLocations("AVEDEV($A1:$A20000)>0");
        AssertFormulaAggregateContrastLocations("AVEDEV(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("AVEDEV($D3:$D5)>0");
        AssertFormulaAggregateContrastLocations("AVEDEV($A1/0)>0");
        AssertFormulaAggregateContrastLocations("AVEDEV(1E308,-1E308)>0");
        AssertFormulaAggregateContrastLocations("AVEDEV(A0)>0");
        AssertFormulaAggregateContrastLocations("GEOMEAN()>0");
        AssertFormulaAggregateContrastLocations("GEOMEAN($A1:$A20000)>0");
        AssertFormulaAggregateContrastLocations("GEOMEAN(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("GEOMEAN($D3:$D5)>0");
        AssertFormulaAggregateContrastLocations("GEOMEAN(0,$A1)>0");
        AssertFormulaAggregateContrastLocations("GEOMEAN(-1,$A1)>0");
        AssertFormulaAggregateContrastLocations("GEOMEAN($A1/0)>0");
        AssertFormulaAggregateContrastLocations("GEOMEAN(1E308,1E308)>0");
        AssertFormulaAggregateContrastLocations("GEOMEAN(A0)>0");
        AssertFormulaAggregateContrastLocations("HARMEAN()>0");
        AssertFormulaAggregateContrastLocations("HARMEAN($A1:$A20000)>0");
        AssertFormulaAggregateContrastLocations("HARMEAN(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("HARMEAN($D3:$D5)>0");
        AssertFormulaAggregateContrastLocations("HARMEAN(0,$A1)>0");
        AssertFormulaAggregateContrastLocations("HARMEAN(-1,$A1)>0");
        AssertFormulaAggregateContrastLocations("HARMEAN($A1/0)>0");
        AssertFormulaAggregateContrastLocations("HARMEAN(1E308*1E308)>0");
        AssertFormulaAggregateContrastLocations("HARMEAN(A0)>0");
        AssertFormulaAggregateContrastLocations("STDEV()>0");
        AssertFormulaAggregateContrastLocations("STDEV($A1)>0");
        AssertFormulaAggregateContrastLocations("STDEV($A1:$A20000)>0");
        AssertFormulaAggregateContrastLocations("STDEV(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("STDEV($D1:$D3)>0");
        AssertFormulaAggregateContrastLocations("STDEV($A1/0)>0");
        AssertFormulaAggregateContrastLocations("STDEV(1E308,0)>0");
        AssertFormulaAggregateContrastLocations("STDEV(A0)>0");
        AssertFormulaAggregateContrastLocations("STDEVP()>0");
        AssertFormulaAggregateContrastLocations("STDEVP($A1:$A20000)>0");
        AssertFormulaAggregateContrastLocations("STDEVP(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("STDEV.P($D3:$D5)>0");
        AssertFormulaAggregateContrastLocations("STDEV.P($A1/0)>0");
        AssertFormulaAggregateContrastLocations("STDEV.P(1E308,0)>0");
        AssertFormulaAggregateContrastLocations("STDEV.P(A0)>0");
        AssertFormulaAggregateContrastLocations("VAR()>0");
        AssertFormulaAggregateContrastLocations("VAR($A1)>0");
        AssertFormulaAggregateContrastLocations("VAR($A1:$A20000)>0");
        AssertFormulaAggregateContrastLocations("VAR(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("VAR($D1:$D3)>0");
        AssertFormulaAggregateContrastLocations("VAR($A1/0)>0");
        AssertFormulaAggregateContrastLocations("VAR(1E308,0)>0");
        AssertFormulaAggregateContrastLocations("VAR(A0)>0");
        AssertFormulaAggregateContrastLocations("VARP()>0");
        AssertFormulaAggregateContrastLocations("VARP($A1:$A20000)>0");
        AssertFormulaAggregateContrastLocations("VARP(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("VAR.P($D3:$D5)>0");
        AssertFormulaAggregateContrastLocations("VAR.P($A1/0)>0");
        AssertFormulaAggregateContrastLocations("VAR.P(1E308,0)>0");
        AssertFormulaAggregateContrastLocations("VAR.P(A0)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAggregateScalarOperandArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM($A1+25,$A1%,-$A1)>=26", "B2", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGE($A1,$A1/2)>70", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAggregateNestedAndPowerArguments()
    {
        AssertFormulaAggregateContrastLocations("MIN(SUM($A1:$A2),$A1^2)>175", "B3");
        AssertFormulaAggregateContrastLocations("MAX(SUM($A1:$A2),$A1^2)>10000", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAggregateCountEvaluatedArguments()
    {
        AssertFormulaAggregateContrastLocations("COUNT($A1+1,$D1)>1", "B2");
        AssertFormulaAggregateContrastLocations("COUNTA($D1,$A1+1)>1", "B1", "B2", "B4");
        AssertFormulaAggregateContrastLocations("COUNTBLANK($D1,$A1+1)=1", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAggregateArgumentReferenceShifting()
    {
        AssertFormulaAggregateContrastLocations("SUM($A1+25,$A2)>175", "B1", "B2", "B3");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatAggregateUnsupportedOperandArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM($D1&\"x\")>0");
        AssertFormulaAggregateContrastLocations("COUNTA($D1&\"x\")>0");
        AssertFormulaAggregateContrastLocations("SUM($A1/0)>0");
        AssertFormulaAggregateContrastLocations("SUM(1E308*1E308)>0");
        AssertFormulaAggregateContrastLocations("SUM(KURT($A1)+1)>0");
        AssertFormulaAggregateContrastLocations("SUM(\"n/a\"+$A1)>0");
        AssertFormulaAggregateContrastLocations("SUM(SUM($A1:$A20000))>0");
        AssertFormulaAggregateContrastLocations("SUMSQ($A1:$A20000)>0");
        AssertFormulaAggregateContrastLocations("SUMSQ(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("SUMSQ(1E308)>0");
        AssertFormulaAggregateContrastLocations("SUMSQ(KURT($A1))>0");
        AssertFormulaAggregateContrastLocations("SUMSQ($A1/0)>0");
        AssertFormulaAggregateContrastLocations("DEVSQ(KURT($A1))>0");
        AssertFormulaAggregateContrastLocations("PRODUCT($A1:$A20000)>0");
        AssertFormulaAggregateContrastLocations("PRODUCT(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("PRODUCT(1E308,1E308)>0");
        AssertFormulaAggregateContrastLocations("PRODUCT(KURT($A1))>0");
        AssertFormulaAggregateContrastLocations("AVEDEV(KURT($A1))>0");
        AssertFormulaAggregateContrastLocations("GEOMEAN(KURT($A1))>0");
        AssertFormulaAggregateContrastLocations("HARMEAN(KURT($A1))>0");
        AssertFormulaAggregateContrastLocations("VAR(KURT($A1))>0");
        AssertFormulaAggregateContrastLocations("VAR.P(KURT($A1))>0");
        AssertFormulaAggregateContrastLocations("COUNTBLANK()>0");
        AssertFormulaAggregateContrastLocations("COUNTBLANK($D1:$D20000)>0");
        AssertFormulaAggregateContrastLocations("COUNTBLANK($D1&\"x\")>0");
        AssertFormulaAggregateContrastLocations("COUNTBLANK($A1/0)>0");
        AssertFormulaAggregateContrastLocations("COUNTBLANK(KURT($A1))>0");
        AssertFormulaAggregateContrastLocations("COUNTBLANK(A0)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatScalarFunctionOperands()
    {
        AssertFormulaArithmeticContrastLocations("ABS($A1-100)>=25", "B1", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("INT($A1/10)>=10", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ROUND($A1/3,0)>=33", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ROUNDUP($A1/100,1)>=1", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ROUNDUP(-$A1/100,1)<=-1", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ROUNDUP($A1,-1)>=100", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN($A1/100,1)>=1", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN(-1.29,1)=-1.2", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN($A1,-1)>=100", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("TRUNC($A1/100,1)>=1", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("TRUNC(-1.29,1)=-1.2", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("TRUNC($A1,-1)>=100", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("TRUNC(1.99)=1", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("TRUNC($A1/100)>=1", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("FACT($A1/25)>20", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("FACT(5.9)=120", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("MOD($A1,2)=0", "B2");
        AssertFormulaArithmeticContrastLocations("SQRT($A1)>=10", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("SQRTPI($A1)>18", "B4");
        AssertFormulaArithmeticContrastLocations("SIGN($A1-100)<0", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("SIGN($A1-100)=0", "B2");
        AssertFormulaArithmeticContrastLocations("SIGN($A1-100)>0", "B4");
        AssertFormulaArithmeticContrastLocations("POWER($A1,2)>10000", "B4");
        AssertFormulaArithmeticContrastLocations("POWER($A1/25,2)>=16", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("EXP($A1/100)>3", "B4");
        AssertFormulaArithmeticContrastLocations("EXP(($A1-75)/25)>=1", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("LN($A1)>4.7", "B4");
        AssertFormulaArithmeticContrastLocations("LOG10($A1)>2", "B4");
        AssertFormulaArithmeticContrastLocations("LOG10($A1)>=2", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("LOG($A1)>2", "B4");
        AssertFormulaArithmeticContrastLocations("LOG($A1,5)>2.9", "B4");
        AssertFormulaArithmeticContrastLocations("LOG($A1,0.5)<-6", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("DEGREES($A1/100)>60", "B4");
        AssertFormulaArithmeticContrastLocations("DEGREES($A1/100)>=45", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ROUND(DEGREES(PI()),0)=180", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("RADIANS($A1)>2", "B4");
        AssertFormulaArithmeticContrastLocations("ROUND(RADIANS(180),2)=3.14", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("SIN(RADIANS($A1))>0.95", "B1", "B2", "B3");
        AssertFormulaArithmeticContrastLocations("ROUND(SIN(PI()/2),2)=1", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("SINH($A1/100)>1", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("COSH($A1/100)>1.5", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("TANH($A1/100)>0.75", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ASIN(SIN(RADIANS($A1)))>1", "B1", "B2", "B3");
        AssertFormulaArithmeticContrastLocations("ASIN($A1/100)>1", "B2");
        AssertFormulaArithmeticContrastLocations("ACOS(COS(RADIANS($A1)))>1.5", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ACOS($A1/100)>0.7", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("ATAN($A1/100)>0.7", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ATAN(TAN(RADIANS($A1)))>1", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("ATAN2($A1,100)>0.8", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("ATAN2(0,$A1)>1.5", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ROUND(ATAN2(1,1),2)=0.79", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COS(RADIANS($A1))<0", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ROUND(COS(PI()),2)=-1", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("TAN(RADIANS($A1))>3", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("ROUND(TAN(PI()/4),2)=1", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("PI()>3", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("PI()*$A1>300", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ROUND(PI(),0)=3", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatScalarFunctionWrappers()
    {
        AssertFormulaArithmeticContrastLocations("IF(ABS($A1-100)>=25,TRUE,FALSE)", "B1", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("AND(ROUND($A1/3,0)>=33,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(ROUND($A1/3,0))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(ROUNDUP($A1/100,1)>=1,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(ROUNDUP($A1/100,1)>=1,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(ROUNDUP($A1/100,1))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(ROUNDDOWN($A1/100,1)>=1,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(ROUNDDOWN($A1/100,1)>=1,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(ROUNDDOWN($A1/100,1))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(TRUNC($A1/100,1)>=1,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(TRUNC($A1/100,1)>=1,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(TRUNC($A1/100))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(FACT($A1/25)>20,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(FACT($A1/25)>20,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(FACT($A1/25))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(SQRT($A1)>=10,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(SQRT($A1)>=10,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(SQRT($A1))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(SQRTPI($A1)>18,TRUE,FALSE)", "B4");
        AssertFormulaArithmeticContrastLocations("AND(SQRTPI($A1)>=17,$C1=\"Closed\")", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(SQRTPI($A1))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(SIGN($A1-100)>0,TRUE,FALSE)", "B4");
        AssertFormulaArithmeticContrastLocations("AND(SIGN($A1-100)<0,$C1=\"Closed\")", "B1");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(SIGN($A1-100))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(POWER($A1,2)>10000,TRUE,FALSE)", "B4");
        AssertFormulaArithmeticContrastLocations("AND(POWER($A1,2)>=10000,$C1=\"Closed\")", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(POWER($A1,2))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(EXP($A1/100)>3,TRUE,FALSE)", "B4");
        AssertFormulaArithmeticContrastLocations("AND(EXP($A1/100)>2,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(EXP($A1/100))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(LN($A1)>4.7,TRUE,FALSE)", "B4");
        AssertFormulaArithmeticContrastLocations("AND(LN($A1)>4.5,$C1=\"Closed\")", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(LN($A1))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(LOG10($A1)>2,TRUE,FALSE)", "B4");
        AssertFormulaArithmeticContrastLocations("AND(LOG10($A1)>=2,$C1=\"Closed\")", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(LOG10($A1))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(LOG($A1)>2,TRUE,FALSE)", "B4");
        AssertFormulaArithmeticContrastLocations("AND(LOG($A1,5)>2.8,$C1=\"Closed\")", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(LOG($A1,10))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(DEGREES($A1/100)>60,TRUE,FALSE)", "B4");
        AssertFormulaArithmeticContrastLocations("AND(DEGREES($A1/100)>=45,$C1=\"Closed\")", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(DEGREES($A1/100))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(RADIANS($A1)>2,TRUE,FALSE)", "B4");
        AssertFormulaArithmeticContrastLocations("AND(RADIANS($A1)>=1.5,$C1=\"Closed\")", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(RADIANS($A1))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(SIN(RADIANS($A1))<0.9,TRUE,FALSE)", "B4");
        AssertFormulaArithmeticContrastLocations("AND(SIN(RADIANS($A1))>0.95,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(SIN(RADIANS($A1)))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(SINH($A1/100)>1,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(SINH($A1/100)>1,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(SINH($A1/100))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(COSH($A1/100)>1.5,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(COSH($A1/100)>1.5,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(COSH($A1/100))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(TANH($A1/100)>0.75,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(TANH($A1/100)>0.75,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(TANH($A1/100))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(ASIN(SIN(RADIANS($A1)))>1,TRUE,FALSE)", "B1", "B2", "B3");
        AssertFormulaArithmeticContrastLocations("AND(ASIN(SIN(RADIANS($A1)))>1,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(ASIN(SIN(RADIANS($A1))))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(ACOS($A1/100)>0.7,TRUE,FALSE)", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("AND(ACOS(COS(RADIANS($A1)))>1.5,$C1=\"Closed\")", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(ACOS($A1/100))", "B1", "B2", "B3");
        AssertFormulaArithmeticContrastLocations("IF(ATAN($A1/100)>0.7,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(ATAN(TAN(RADIANS($A1)))>1,$C1=\"Closed\")", "B1");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(ATAN($A1/100))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(ATAN2($A1,100)>0.8,TRUE,FALSE)", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("AND(ATAN2(100,$A1)>0.8,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(ATAN2($A1,100))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(COS(RADIANS($A1))<0,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(COS(RADIANS($A1))<0,$C1=\"Closed\")", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(COS(RADIANS($A1)))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(TAN(RADIANS($A1))<0,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(TAN(RADIANS($A1))>3,$C1=\"Closed\")", "B1");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(TAN(RADIANS($A1)))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(PI()>3,TRUE,FALSE)", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("AND(PI()*$A1>300,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(PI())", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("MOD($A1,2)", "B1", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAggregateScalarFunctionArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(ABS($A1-100),MOD($A1,2))>=25", "B1", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUM(ROUNDUP($A1/100,1),1)>=2", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(ROUNDDOWN($A1/100,1),1)>=2", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(TRUNC($A1/100,1),1)>=2", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(FACT($A1/25),1)>20", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(SQRT($A1),1)>10", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(SQRTPI($A1),1)>18", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(SIGN($A1-100),1)>1", "B4");
        AssertFormulaAggregateContrastLocations("SUM(POWER($A1,2),1)>10000", "B2", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGE(POWER($A1,2),$A1)>5000", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(EXP($A1/100),1)>4", "B4");
        AssertFormulaAggregateContrastLocations("SUM(LN($A1),1)>5.7", "B4");
        AssertFormulaAggregateContrastLocations("SUM(LOG10($A1),1)>3", "B4");
        AssertFormulaAggregateContrastLocations("SUM(LOG($A1),1)>3", "B4");
        AssertFormulaAggregateContrastLocations("SUM(LOG($A1,5),1)>3.8", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(DEGREES($A1/100),1)>58", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(RADIANS($A1),1)>2.5", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(SIN(RADIANS($A1)),1)>1.95", "B1", "B2", "B3");
        AssertFormulaAggregateContrastLocations("SUM(SINH($A1/100),1)>2", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(COSH($A1/100),1)>2.5", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(TANH($A1/100),1)>1.75", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(ASIN(SIN(RADIANS($A1))),1)>2", "B1", "B2", "B3");
        AssertFormulaAggregateContrastLocations("SUM(ACOS(COS(RADIANS($A1))),1)>2.5", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(ATAN($A1/100),1)>1.7", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(ATAN2($A1,100),1)>1.8", "B1", "B3");
        AssertFormulaAggregateContrastLocations("SUM(COS(RADIANS($A1)),1)>1.2", "B1", "B3");
        AssertFormulaAggregateContrastLocations("SUM(TAN(RADIANS($A1)),1)>4", "B1", "B3");
        AssertFormulaAggregateContrastLocations("SUM(PI(),$A1)>103", "B2", "B4");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatScalarFunctionUnsupportedOperands()
    {
        AssertFormulaArithmeticContrastLocations("ABS($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("ROUND($A1)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDUP($A1)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDUP($A1,0,1)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN($A1)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN($A1,0,1)>0");
        AssertFormulaArithmeticContrastLocations("TRUNC()>0");
        AssertFormulaArithmeticContrastLocations("TRUNC($A1,0,1)>0");
        AssertFormulaArithmeticContrastLocations("MOD($A1)>0");
        AssertFormulaArithmeticContrastLocations("MOD($A1,0)>0");
        AssertFormulaArithmeticContrastLocations("ROUND($A1,999999)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDUP($A1,999999)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN($A1,999999)>0");
        AssertFormulaArithmeticContrastLocations("TRUNC($A1,999999)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDUP(\"5\",0)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDUP($A1,\"1\")>0");
        AssertFormulaArithmeticContrastLocations("ROUNDUP($A1&\"x\",0)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDUP(KURT($A1),0)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDUP(1E308*1E308,0)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN(\"5\",0)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN($A1,\"1\")>0");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN($A1&\"x\",0)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN(KURT($A1),0)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN(1E308*1E308,0)>0");
        AssertFormulaArithmeticContrastLocations("TRUNC(\"5\",0)>0");
        AssertFormulaArithmeticContrastLocations("TRUNC($A1,\"1\")>0");
        AssertFormulaArithmeticContrastLocations("TRUNC($A1&\"x\",0)>0");
        AssertFormulaArithmeticContrastLocations("TRUNC(KURT($A1),0)>0");
        AssertFormulaArithmeticContrastLocations("TRUNC(1E308*1E308,0)>0");
        AssertFormulaArithmeticContrastLocations("FACT()>0");
        AssertFormulaArithmeticContrastLocations("FACT($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("FACT(\"5\")>0");
        AssertFormulaArithmeticContrastLocations("FACT($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("FACT(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("FACT(-1)>0");
        AssertFormulaArithmeticContrastLocations("FACT(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("FACT(EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("FACT(171)>0");
        AssertFormulaArithmeticContrastLocations("ABS(\"5\")>0");
        AssertFormulaArithmeticContrastLocations("ABS($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("SQRT()>0");
        AssertFormulaArithmeticContrastLocations("SQRT($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("SQRT(-$A1)>0");
        AssertFormulaArithmeticContrastLocations("SQRT(\"5\")>0");
        AssertFormulaArithmeticContrastLocations("SQRT($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("SQRTPI()>0");
        AssertFormulaArithmeticContrastLocations("SQRTPI($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("SQRTPI(-$A1)>0");
        AssertFormulaArithmeticContrastLocations("SQRTPI(\"5\")>0");
        AssertFormulaArithmeticContrastLocations("SQRTPI($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("SQRTPI(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("SQRTPI(1E308)>0");
        AssertFormulaArithmeticContrastLocations("SIGN()>0");
        AssertFormulaArithmeticContrastLocations("SIGN($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("SIGN(\"5\")>0");
        AssertFormulaArithmeticContrastLocations("SIGN($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("SIGN(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("SIGN(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("POWER($A1)>0");
        AssertFormulaArithmeticContrastLocations("POWER($A1,2,3)>0");
        AssertFormulaArithmeticContrastLocations("POWER(\"5\",2)>0");
        AssertFormulaArithmeticContrastLocations("POWER($A1,\"2\")>0");
        AssertFormulaArithmeticContrastLocations("POWER($A1&\"x\",2)>0");
        AssertFormulaArithmeticContrastLocations("POWER(KURT($A1),2)>0");
        AssertFormulaArithmeticContrastLocations("POWER(1E308,2)>0");
        AssertFormulaArithmeticContrastLocations("POWER(0,-1)>0");
        AssertFormulaArithmeticContrastLocations("POWER(-$A1,0.5)>0");
        AssertFormulaArithmeticContrastLocations("EXP()>0");
        AssertFormulaArithmeticContrastLocations("EXP($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("EXP(\"5\")>0");
        AssertFormulaArithmeticContrastLocations("EXP($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("EXP(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("EXP(1000)>0");
        AssertFormulaArithmeticContrastLocations("LN()>0");
        AssertFormulaArithmeticContrastLocations("LN($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("LN(0)>0");
        AssertFormulaArithmeticContrastLocations("LN(-$A1)>0");
        AssertFormulaArithmeticContrastLocations("LN(\"5\")>0");
        AssertFormulaArithmeticContrastLocations("LN($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("LN(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("LN(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("LN(EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("LOG10()>0");
        AssertFormulaArithmeticContrastLocations("LOG10($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("LOG10(0)>0");
        AssertFormulaArithmeticContrastLocations("LOG10(-$A1)>0");
        AssertFormulaArithmeticContrastLocations("LOG10(\"5\")>0");
        AssertFormulaArithmeticContrastLocations("LOG10($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("LOG10(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("LOG10(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("LOG10(EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("LOG()>0");
        AssertFormulaArithmeticContrastLocations("LOG($A1,10,2)>0");
        AssertFormulaArithmeticContrastLocations("LOG(0)>0");
        AssertFormulaArithmeticContrastLocations("LOG(-$A1)>0");
        AssertFormulaArithmeticContrastLocations("LOG($A1,0)>0");
        AssertFormulaArithmeticContrastLocations("LOG($A1,-2)>0");
        AssertFormulaArithmeticContrastLocations("LOG($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("LOG(\"5\")>0");
        AssertFormulaArithmeticContrastLocations("LOG($A1,\"10\")>0");
        AssertFormulaArithmeticContrastLocations("LOG($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("LOG(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("LOG($A1,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("LOG(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("LOG(EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("LOG($A1,EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("DEGREES()>0");
        AssertFormulaArithmeticContrastLocations("DEGREES($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("DEGREES(\"5\")>0");
        AssertFormulaArithmeticContrastLocations("DEGREES($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("DEGREES(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("DEGREES(1E308)>0");
        AssertFormulaArithmeticContrastLocations("RADIANS()>0");
        AssertFormulaArithmeticContrastLocations("RADIANS($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("RADIANS(\"5\")>0");
        AssertFormulaArithmeticContrastLocations("RADIANS($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("RADIANS(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("RADIANS(1E308)>0");
        AssertFormulaArithmeticContrastLocations("SIN()>0");
        AssertFormulaArithmeticContrastLocations("SIN($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("SIN(\"5\")>0");
        AssertFormulaArithmeticContrastLocations("SIN($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("SIN(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("SIN(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("SINH()>0");
        AssertFormulaArithmeticContrastLocations("SINH($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("SINH(\"5\")>0");
        AssertFormulaArithmeticContrastLocations("SINH($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("SINH(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("SINH(1E308)>0");
        AssertFormulaArithmeticContrastLocations("SINH(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("COSH()>0");
        AssertFormulaArithmeticContrastLocations("COSH($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("COSH(\"5\")>0");
        AssertFormulaArithmeticContrastLocations("COSH($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("COSH(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("COSH(1E308)>0");
        AssertFormulaArithmeticContrastLocations("COSH(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("TANH()>0");
        AssertFormulaArithmeticContrastLocations("TANH($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("TANH(\"5\")>0");
        AssertFormulaArithmeticContrastLocations("TANH($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("TANH(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("TANH(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ASIN()>0");
        AssertFormulaArithmeticContrastLocations("ASIN($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("ASIN(\"0.5\")>0");
        AssertFormulaArithmeticContrastLocations("ASIN($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("ASIN(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("ASIN(2)>0");
        AssertFormulaArithmeticContrastLocations("ASIN(-2)>0");
        AssertFormulaArithmeticContrastLocations("ASIN(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ACOS()>0");
        AssertFormulaArithmeticContrastLocations("ACOS($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("ACOS(\"0.5\")>0");
        AssertFormulaArithmeticContrastLocations("ACOS($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("ACOS(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("ACOS(2)>0");
        AssertFormulaArithmeticContrastLocations("ACOS(-2)>0");
        AssertFormulaArithmeticContrastLocations("ACOS(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ATAN()>0");
        AssertFormulaArithmeticContrastLocations("ATAN($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("ATAN(\"5\")>0");
        AssertFormulaArithmeticContrastLocations("ATAN($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("ATAN(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("ATAN(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ATAN2()>0");
        AssertFormulaArithmeticContrastLocations("ATAN2($A1)>0");
        AssertFormulaArithmeticContrastLocations("ATAN2($A1,1,2)>0");
        AssertFormulaArithmeticContrastLocations("ATAN2(\"5\",$A1)>0");
        AssertFormulaArithmeticContrastLocations("ATAN2($A1,\"5\")>0");
        AssertFormulaArithmeticContrastLocations("ATAN2($A1&\"x\",1)>0");
        AssertFormulaArithmeticContrastLocations("ATAN2(KURT($A1),1)>0");
        AssertFormulaArithmeticContrastLocations("ATAN2($A1,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("ATAN2(1E308*1E308,$A1)>0");
        AssertFormulaArithmeticContrastLocations("ATAN2($A1,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ATAN2(0,0)>0");
        AssertFormulaArithmeticContrastLocations("ATAN2($A1-$A1,0)>0");
        AssertFormulaArithmeticContrastLocations("COS()>0");
        AssertFormulaArithmeticContrastLocations("COS($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("COS(\"5\")>0");
        AssertFormulaArithmeticContrastLocations("COS($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("COS(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("COS(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("TAN()>0");
        AssertFormulaArithmeticContrastLocations("TAN($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("TAN(\"5\")>0");
        AssertFormulaArithmeticContrastLocations("TAN($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("TAN(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("TAN(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("PI(1)>0");
        AssertFormulaArithmeticContrastLocations("PI($A1)>0");
        AssertFormulaArithmeticContrastLocations("KURT($A1)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTextFunctionOperands()
    {
        AssertFormulaTextFunctionContrastLocations("LEN($C1)>4", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("UPPER($C1)=\"OPEN\"", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("LOWER($C1)=\"closed\"", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("LEFT($C1,1)=\"C\"", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("RIGHT(LOWER($C1),4)=\"open\"", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("MID($C1,2,3)=\"los\"", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("MID(LOWER($C1),1,4)=\"open\"", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("MID($C1,99,3)=\"\"", "B1", "B2", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("MID($C1,1,0)=\"\"", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTrimTextFunctionOperand()
    {
        AssertFormulaPaddedTextFunctionContrastLocations("TRIM($C1)=\"Open\"", "B1", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTextFunctionsInWrappers()
    {
        AssertFormulaTextFunctionContrastLocations("AND(UPPER($C1)=\"OPEN\",$A1>=100)", "B4");
        AssertFormulaTextFunctionContrastLocations("AND(MID($C1,1,1)=\"O\",$A1>=100)", "B4");
        AssertFormulaTextFunctionContrastLocations("IF($A1>=100,LEN($C1),FALSE)", "B2", "B4");
        AssertFormulaTextFunctionContrastLocations("IF($A1>=100,MID($C1,1,1)=\"O\",FALSE)", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTextFunctionPredicates()
    {
        AssertFormulaTextFunctionContrastLocations("ISTEXT(LEFT($C1,1))", "B1", "B2", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("ISTEXT(MID($C1,2,2))", "B1", "B2", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("ISNUMBER(LEN($C1))", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTextFunctionArithmeticAndAggregateArguments()
    {
        AssertFormulaTextFunctionContrastLocations("LEN($C1)+1>5", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("LEN(MID($C1,2,3))=3", "B1", "B2", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("COUNTA(LEFT($C1,1))>0", "B1", "B2", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("COUNTA(MID($C1,1,1))>0", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTextSearchFunctionOperands()
    {
        AssertFormulaTextFunctionContrastLocations("FIND(\"los\",$C1)>1", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("FIND(\"e\",$C1,4)>0", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("SEARCH(\"open\",$C1)=1", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("SEARCH(\"E\",LOWER($C1))>0", "B1", "B2", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("EXACT($C1,\"Open\")", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTextSearchFunctionsInWrappers()
    {
        AssertFormulaTextFunctionContrastLocations("AND(SEARCH(\"open\",$C1),$A1>=100)", "B4");
        AssertFormulaTextFunctionContrastLocations("IF(SEARCH(\"open\",$C1),TRUE,FALSE)", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTextSearchFunctionPredicatesArithmeticAndAggregateArguments()
    {
        AssertFormulaTextFunctionContrastLocations("ISNUMBER(SEARCH(\"open\",$C1))", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("SEARCH(\"e\",$C1)+1>4", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("SUM(SEARCH(\"o\",LOWER($C1)))>0", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatTextFunctionUnsupportedOperands()
    {
        AssertFormulaTextFunctionContrastLocations("LEN($C1,1)>0");
        AssertFormulaTextFunctionContrastLocations("UPPER()=\"OPEN\"");
        AssertFormulaTextFunctionContrastLocations("LEFT($C1,-1)=\"\"");
        AssertFormulaTextFunctionContrastLocations("LEFT($C1,999999)=\"Closed\"");
        AssertFormulaTextFunctionContrastLocations("LEFT($C1,1.5)=\"C\"");
        AssertFormulaTextFunctionContrastLocations("MID($C1,0,1)=\"\"");
        AssertFormulaTextFunctionContrastLocations("MID($C1,1.5,1)=\"\"");
        AssertFormulaTextFunctionContrastLocations("MID($C1,999999,1)=\"\"");
        AssertFormulaTextFunctionContrastLocations("MID($C1,1,-1)=\"\"");
        AssertFormulaTextFunctionContrastLocations("MID($C1,1,1.5)=\"C\"");
        AssertFormulaTextFunctionContrastLocations("MID($C1,1,999999)=\"Closed\"");
        AssertFormulaTextFunctionContrastLocations("MID($A1,1,1)=\"7\"");
        AssertFormulaTextFunctionContrastLocations("MID($C1&\"x\",1,1)=\"O\"");
        AssertFormulaTextFunctionContrastLocations("MID($C1,1)=\"C\"");
        AssertFormulaTextFunctionContrastLocations("LEN($A1)>0");
        AssertFormulaTextFunctionContrastLocations("CONCAT($C1,\"x\")=\"Openx\"");
        AssertFormulaTextFunctionContrastLocations("LEFT($C1&\"x\",1)=\"O\"");
        AssertFormulaTextFunctionContrastLocations("LEN(A0)>0");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatTextSearchFunctionUnsupportedOperands()
    {
        AssertFormulaTextFunctionContrastLocations("FIND(\"x\",$C1)>0");
        AssertFormulaTextFunctionContrastLocations("SEARCH(\"o\",$C1,0)>0");
        AssertFormulaTextFunctionContrastLocations("SEARCH(\"o\",$C1,1.5)>0");
        AssertFormulaTextFunctionContrastLocations("SEARCH(\"o\",$C1,999999)>0");
        AssertFormulaTextFunctionContrastLocations("SEARCH(\"\",$C1)>0");
        AssertFormulaTextFunctionContrastLocations("FIND(\"o\")>0");
        AssertFormulaTextFunctionContrastLocations("EXACT($C1)>0");
        AssertFormulaTextFunctionContrastLocations("SEARCH(\"o\",$A1)>0");
        AssertFormulaTextFunctionContrastLocations("SEARCH(\"o\",$C1&\"x\")>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatValueFunctionOperands()
    {
        AssertFormulaValueFunctionContrastLocations("VALUE($C1)=99.5", "B1");
        AssertFormulaValueFunctionContrastLocations("VALUE($C1)>=1000", "B2");
        AssertFormulaValueFunctionContrastLocations("VALUE($C1)=0.5", "B3");
        AssertFormulaValueFunctionContrastLocations("VALUE($C1)<0", "B4");
        AssertFormulaValueFunctionContrastLocations("VALUE(\"1,234.5\")>1000", "B1", "B2", "B3", "B4", "B5", "B6", "B7");
        AssertFormulaValueFunctionContrastLocations("VALUE(\"50%\")=0.5", "B1", "B2", "B3", "B4", "B5", "B6", "B7");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatValueFunctionWrappersPredicatesAndAggregates()
    {
        AssertFormulaValueFunctionContrastLocations("IF(VALUE($C1)>0,TRUE,FALSE)", "B1", "B2", "B3");
        AssertFormulaValueFunctionContrastLocations("AND(VALUE($C1)>0,$A1)", "B1", "B2");
        AssertFormulaValueFunctionContrastLocations("ISNUMBER(VALUE($C1))", "B1", "B2", "B3", "B4");
        AssertFormulaValueFunctionContrastLocations("VALUE($C1)+1=100.5", "B1");
        AssertFormulaValueFunctionContrastLocations("SUM(VALUE($C1),1)>1000", "B2");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatValueFunctionUnsupportedOperands()
    {
        AssertFormulaValueFunctionContrastLocations("VALUE()>0");
        AssertFormulaValueFunctionContrastLocations("VALUE($C1,1)>0");
        AssertFormulaValueFunctionContrastLocations("VALUE($A1)>0");
        AssertFormulaValueFunctionContrastLocations("VALUE(42)>0");
        AssertFormulaValueFunctionContrastLocations("VALUE(\"Open\")>0");
        AssertFormulaValueFunctionContrastLocations("VALUE(\"\")>0");
        AssertFormulaValueFunctionContrastLocations("VALUE(\"1E309\")>0");
        AssertFormulaValueFunctionContrastLocations("VALUE(\"50%%\")>0");
        AssertFormulaValueFunctionContrastLocations("VALUE($C1&\"x\")>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDateFunctionOperands()
    {
        AssertFormulaDateFunctionContrastLocations("YEAR($A1)=2023", "B1", "B2", "B4");
        AssertFormulaDateFunctionContrastLocations("MONTH($A1)=3", "B1", "B2", "B3");
        AssertFormulaDateFunctionContrastLocations("DAY($A1)>=16", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("DATE(2023,3,15)=$A1", "B1");
        AssertFormulaDateFunctionContrastLocations("YEAR(DATE(2023,3,15))=2023", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("TODAY()<=NOW()", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("NOW()-TODAY()>=0", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("YEAR(TODAY())>=1900", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("MONTH(NOW())>=1", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("DAY(TODAY())>=1", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDateFunctionWrappers()
    {
        AssertFormulaDateFunctionContrastLocations("IF(DAY($A1)>=16,TRUE,FALSE)", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("IF(TODAY()<=NOW(),TRUE,FALSE)", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("AND(YEAR($A1)=2023,$C1=\"Open\")", "B4");
        AssertFormulaDateFunctionContrastLocations("AND(ISNUMBER(TODAY()),NOW()>=TODAY())", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISNUMBER(YEAR($A1))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISNUMBER(TODAY())", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("YEAR($A1)-2023", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAggregateDateFunctionArguments()
    {
        AssertFormulaDateFunctionContrastLocations("SUM(DAY($A1),MONTH($A1))>=19", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("SUM(DAY(TODAY()),MONTH(TODAY()))>=2", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatDateFunctionUnsupportedOperands()
    {
        AssertFormulaDateFunctionContrastLocations("DATE(2023,2,30)=$A1");
        AssertFormulaDateFunctionContrastLocations("DATE(2023,1.5,1)=$A1");
        AssertFormulaDateFunctionContrastLocations("DATE(2023,3)=$A1");
        AssertFormulaDateFunctionContrastLocations("DATE(10000,1,1)=$A1");
        AssertFormulaDateFunctionContrastLocations("YEAR(\"2023-03-15\")=2023");
        AssertFormulaDateFunctionContrastLocations("YEAR($A1,1)=2023");
        AssertFormulaDateFunctionContrastLocations("YEAR(1E308)>0");
        AssertFormulaDateFunctionContrastLocations("TODAY(1)>0");
        AssertFormulaDateFunctionContrastLocations("NOW(1)>0");
        AssertFormulaDateFunctionContrastLocations("YEAR(A0)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatRowColumnFunctionOperands()
    {
        AssertFormulaRowColumnFunctionContrastLocations("ROW()=2", "B2", "C2", "D2");
        AssertFormulaRowColumnFunctionContrastLocations("COLUMN()=3", "C1", "C2");
        AssertFormulaRowColumnFunctionContrastLocations("ROW(A2)=3", "B2", "C2", "D2");
        AssertFormulaRowColumnFunctionContrastLocations("COLUMN(C1)=4", "C1", "C2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatRowColumnFunctionWrappersPredicatesAndAggregates()
    {
        AssertFormulaRowColumnFunctionContrastLocations("MOD(ROW(),2)=0", "B2", "C2", "D2");
        AssertFormulaRowColumnFunctionContrastLocations("AND(COLUMN()=3,ROW()=1)", "C1");
        AssertFormulaRowColumnFunctionContrastLocations("IF(ROW()=2,COLUMN()=4,FALSE)", "D2");
        AssertFormulaRowColumnFunctionContrastLocations("ISNUMBER(ROW())", "B1", "C1", "D1", "B2", "C2", "D2");
        AssertFormulaRowColumnFunctionContrastLocations("SUM(ROW(),COLUMN())=5", "D1", "C2");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatRowColumnFunctionUnsupportedOperands()
    {
        AssertFormulaRowColumnFunctionContrastLocations("ROW(1)>0");
        AssertFormulaRowColumnFunctionContrastLocations("COLUMN(\"A1\")>0");
        AssertFormulaRowColumnFunctionContrastLocations("ROW($A1,$B1)>0");
        AssertFormulaRowColumnFunctionContrastLocations("ROW($A1:$A2)>0");
        AssertFormulaRowColumnFunctionContrastLocations("ROW(A0)>0");
        AssertFormulaRowColumnFunctionContrastLocations("COLUMN(Missing!A1)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatArithmeticComparison()
    {
        AssertFormulaArithmeticContrastLocations("($A1+25-50)*2/5>=40", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatArithmeticLogicalWrapper()
    {
        AssertFormulaArithmeticContrastLocations("AND($A1+25>=125,$C1=\"Open\")", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatArithmeticIfBranch()
    {
        AssertFormulaArithmeticContrastLocations("IF($A1>=100,$A1-100,FALSE)", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatArithmeticPredicatesAndTruthiness()
    {
        AssertFormulaArithmeticContrastLocations("$A1-75", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER($A1+1)", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAggregateArithmeticOperands()
    {
        AssertFormulaArithmeticContrastLocations("SUM($A1:$A3)/2>=125", "B1", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatUnaryArithmeticOperands()
    {
        AssertFormulaArithmeticContrastLocations("-$A1<-100", "B4");
        AssertFormulaArithmeticContrastLocations("$A1%>=1", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("IF(-$A1<-100,TRUE,FALSE)", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatPowerArithmeticOperands()
    {
        AssertFormulaArithmeticContrastLocations("$A1^2>10000", "B4");
        AssertFormulaArithmeticContrastLocations("($A1-70)^2>=900", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND($A1%>=1,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER($A1^2)", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAggregateUnaryPowerOperands()
    {
        AssertFormulaArithmeticContrastLocations("SUM($A1:$A3)%>2", "B1", "B2");
        AssertFormulaArithmeticContrastLocations("SUM($A1:$A3)^2>70000", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatArithmeticReferenceShifting()
    {
        AssertFormulaArithmeticContrastLocations("$A1+$A2>=175", "B1", "B2", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatArithmeticDateAndBooleanOperands()
    {
        AssertFormulaDateBooleanArithmeticContrastLocations("$A1+$C1>45001", "B3");
        AssertFormulaDateBooleanArithmeticContrastLocations("$A1+TRUE>45001", "B2", "B3");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatArithmeticUnsupportedOperands()
    {
        AssertFormulaArithmeticContrastLocations("$A1/0>0");
        AssertFormulaArithmeticContrastLocations("\"5\"+$A1>80");
        AssertFormulaArithmeticContrastLocations("1E308*1E308>0");
        AssertFormulaArithmeticContrastLocations("KURT($A1)+1>0");
        AssertFormulaArithmeticContrastLocations("$A1&1>0");
        AssertFormulaArithmeticContrastLocations("A0+1>0");
        AssertFormulaArithmeticContrastLocations("(($A1-$A1)^-1)>0");
        AssertFormulaArithmeticContrastLocations("-\"5\">0");
        AssertFormulaArithmeticContrastLocations("KURT($A1)^2>0");
        AssertFormulaArithmeticContrastLocations("$A1^\"2\">0");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatBooleanFunctionsWithArguments()
    {
        AssertFormulaBooleanContrastLocations("=TRUE(1)");
        AssertFormulaBooleanContrastLocations("NOT(FALSE($A1))");
        AssertFormulaBooleanContrastLocations("AND(TRUE(1),$A1>=100)");
        AssertFormulaIfContrastLocations("IF($A1>=100,TRUE(1),FALSE())");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatIsRefReferences()
    {
        AssertFormulaReferencePredicateContrastLocations("ISREF($A1)", "B1", "B2", "B3", "B4", "B5");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatIsFormulaReferences()
    {
        AssertFormulaReferencePredicateContrastLocations("ISFORMULA($A1)", "B1", "B4");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatIsRefIsFormulaLiteralOperands()
    {
        AssertFormulaReferencePredicateContrastLocations("ISREF(42)");
        AssertFormulaReferencePredicateContrastLocations("ISFORMULA(42)");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatNestedIsRefIsFormulaPredicates()
    {
        AssertFormulaReferencePredicateContrastLocations("AND(ISFORMULA($A1),$C1)", "B1");
        AssertFormulaReferencePredicateContrastLocations("XOR(ISREF($A1),$C1)", "B3", "B4", "B5");
        AssertFormulaReferencePredicateContrastLocations("IF(ISFORMULA($A1),TRUE,FALSE)", "B1", "B4");
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
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, "AND($A1>=100,KURT($A1)>0)");

        FindLowContrastCellTextIssues(workbook).Should().BeEmpty();
    }

    [Fact]
    public void FindIssues_DoesNotMatchUnsupportedFormulaConditionalFormatInsideIfWrapper()
    {
        AssertFormulaIfContrastLocations("IF(KURT($A1)>0,TRUE,FALSE)");
        AssertFormulaIfContrastLocations("IF($A1>=100,KURT($A1),FALSE)");
    }

    [Fact]
    public void FindIssues_DoesNotMatchUnsupportedFormulaConditionalFormatInsideXorWrapper()
    {
        AssertFormulaXorContrastLocations("XOR($A1>=100,KURT($A1)>0)");
        AssertFormulaXorContrastLocations("XOR()");
    }

    [Fact]
    public void FindIssues_DoesNotMatchUnsupportedFormulaConditionalFormatInsideIsPredicate()
    {
        AssertFormulaPredicateContrastLocations("ISNUMBER(KURT($A1))");
        AssertFormulaParityContrastLocations("ISEVEN(KURT($A1))");
        AssertFormulaParityContrastLocations("ISODD(KURT($A1))");
        AssertFormulaReferencePredicateContrastLocations("ISREF(KURT($A1))");
        AssertFormulaReferencePredicateContrastLocations("ISFORMULA(KURT($A1))");
        AssertFormulaPredicateContrastLocations("KURT($A1)>0");
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

    private static Workbook CreateFormulaAggregateContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 4, 2);

        SetFormulaAggregateContrastRow(sheet, 1, 75, "Closed", "Below closed", new TextValue("East"));
        SetFormulaAggregateContrastRow(sheet, 2, 100, "Closed", "Threshold closed", new NumberValue(12));
        SetFormulaAggregateContrastRow(sheet, 3, 75, "Open", "Below open", BlankValue.Instance);
        SetFormulaAggregateContrastRow(sheet, 4, 125, "Open", "Escalated open", new TextValue("West"));

        return workbook;
    }

    private static void SetFormulaAggregateContrastRow(
        Sheet sheet,
        uint row,
        double amount,
        string status,
        string label,
        ScalarValue optionalValue)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(amount));
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), new TextValue(status));

        if (optionalValue is not BlankValue)
            sheet.SetCell(new CellAddress(sheet.Id, row, 4), optionalValue);
    }

    private static Workbook CreateFormulaPaddedTextFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 3, 2);

        SetFormulaPaddedTextFunctionContrastRow(sheet, 1, " Open ", "Open padded");
        SetFormulaPaddedTextFunctionContrastRow(sheet, 2, "Closed ", "Closed padded");
        SetFormulaPaddedTextFunctionContrastRow(sheet, 3, "Open", "Open plain");

        return workbook;
    }

    private static void SetFormulaPaddedTextFunctionContrastRow(
        Sheet sheet,
        uint row,
        string status,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), new TextValue(status));
    }

    private static Workbook CreateFormulaValueFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 7, 2);

        SetFormulaValueFunctionContrastRow(sheet, 1, new TextValue("99.5"), flag: true, "Decimal text");
        SetFormulaValueFunctionContrastRow(sheet, 2, new TextValue("1,234.5"), flag: true, "Thousands text");
        SetFormulaValueFunctionContrastRow(sheet, 3, new TextValue("50%"), flag: false, "Percent text");
        SetFormulaValueFunctionContrastRow(sheet, 4, new TextValue(" -12.25 "), flag: true, "Negative text");
        SetFormulaValueFunctionContrastRow(sheet, 5, new TextValue("Open"), flag: true, "Invalid text");
        SetFormulaValueFunctionContrastRow(sheet, 6, new TextValue(string.Empty), flag: true, "Empty text");
        SetFormulaValueFunctionContrastRow(sheet, 7, new NumberValue(75), flag: true, "Numeric source");

        return workbook;
    }

    private static void SetFormulaValueFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue source,
        bool flag,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), new BoolValue(flag));
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), source);
    }

    private static Workbook CreateFormulaDateFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 4, 2);

        SetFormulaDateFunctionContrastRow(sheet, 1, new DateTime(2023, 3, 15), "Closed", "March midpoint");
        SetFormulaDateFunctionContrastRow(sheet, 2, new DateTime(2023, 3, 16), "Closed", "March second half");
        SetFormulaDateFunctionContrastRow(sheet, 3, new DateTime(2024, 3, 20), "Open", "Next March");
        SetFormulaDateFunctionContrastRow(sheet, 4, new DateTime(2023, 4, 20), "Open", "April second half");

        return workbook;
    }

    private static void SetFormulaDateFunctionContrastRow(
        Sheet sheet,
        uint row,
        DateTime date,
        string status,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), DateTimeValue.FromDateTime(date));
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), new TextValue(status));
    }

    private static Workbook CreateFormulaDateBooleanArithmeticContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 3, 2);

        SetFormulaDateBooleanArithmeticContrastRow(sheet, 1, dateSerial: 45000, flag: true, "Current closed");
        SetFormulaDateBooleanArithmeticContrastRow(sheet, 2, dateSerial: 45001, flag: false, "Next inactive");
        SetFormulaDateBooleanArithmeticContrastRow(sheet, 3, dateSerial: 45001, flag: true, "Next active");

        return workbook;
    }

    private static void SetFormulaDateBooleanArithmeticContrastRow(
        Sheet sheet,
        uint row,
        double dateSerial,
        bool flag,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), new DateTimeValue(dateSerial));
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), new BoolValue(flag));
    }

    private static Workbook CreateFormulaRowColumnFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 2, 4);

        for (uint row = 1; row <= 2; row++)
        {
            for (uint col = 2; col <= 4; col++)
            {
                sheet.SetCell(new CellAddress(sheet.Id, row, col), new TextValue($"Label {row}:{col}"));
            }
        }

        return workbook;
    }

    private static Workbook CreateFormulaReferencePredicateContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 5, 2);

        SetFormulaReferencePredicateContrastRow(sheet, 1, Cell.FromFormula("1+1"), flag: true, "Formula source");
        SetFormulaReferencePredicateContrastRow(sheet, 2, Cell.FromValue(new NumberValue(42)), flag: true, "Number source");
        SetFormulaReferencePredicateContrastRow(sheet, 3, source: null, flag: false, "Blank source");
        SetFormulaReferencePredicateContrastRow(sheet, 4, Cell.FromFormula("A2*2"), flag: false, "Second formula source");
        SetFormulaReferencePredicateContrastRow(sheet, 5, Cell.FromValue(new TextValue("Revenue")), flag: false, "Text source");

        return workbook;
    }

    private static void SetFormulaReferencePredicateContrastRow(
        Sheet sheet,
        uint row,
        Cell? source,
        bool flag,
        string label)
    {
        if (source is not null)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), source);

        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), new BoolValue(flag));
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

    private static void AssertFormulaAggregateContrastLocations(string formulaText, params string[] expectedLocations)
    {
        var workbook = CreateFormulaAggregateContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaArithmeticContrastLocations(string formulaText, params string[] expectedLocations)
    {
        var workbook = CreateFormulaAggregateContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaTextFunctionContrastLocations(string formulaText, params string[] expectedLocations)
    {
        var workbook = CreateFormulaAggregateContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaPaddedTextFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaPaddedTextFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaValueFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaValueFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaDateFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaDateFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaDateBooleanArithmeticContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaDateBooleanArithmeticContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaRowColumnFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaRowColumnFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
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

    private static void AssertFormulaReferencePredicateContrastLocations(string formulaText, params string[] expectedLocations)
    {
        var workbook = CreateFormulaReferencePredicateContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaXorContrastLocations(string formulaText, params string[] expectedLocations)
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
