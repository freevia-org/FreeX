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
    public void FindIssues_ConditionalRgbColorsOverrideThemedBaseColors()
    {
        var workbook = new Workbook("Accessibility")
        {
            Theme = WorkbookTheme.Office
                .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(10, 10, 10))
                .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(250, 250, 250))
        };
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 2, 2);
        var baseStyleId = workbook.RegisterStyle(new CellStyle
        {
            FontColor = new CellColor(1, 2, 3),
            FontThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
            FillColor = new CellColor(4, 5, 6),
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1)
        });
        var cell = Cell.FromValue(new TextValue("At risk"));
        cell.StyleId = baseStyleId;
        sheet.SetCell(address, cell);
        var conditionalFont = new CellColor(245, 245, 245);
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(address, address),
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "risk",
            FormatIfTrue = new CellStyle
            {
                FontColor = conditionalFont,
                DxfFontColor = conditionalFont,
                FillColor = new CellColor(250, 250, 250)
            }
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(issue =>
                issue.Kind == AccessibilityIssueKind.LowContrastCellText &&
                issue.Location == "B2");
    }

    [Fact]
    public void FindIssues_ConditionalThemeColorsReplaceThemedBaseColors()
    {
        var workbook = new Workbook("Accessibility")
        {
            Theme = WorkbookTheme.Office
                .WithColor(WorkbookThemeColorSlot.Accent1, new CellColor(10, 10, 10))
                .WithColor(WorkbookThemeColorSlot.Accent2, new CellColor(250, 250, 250))
                .WithColor(WorkbookThemeColorSlot.Accent3, new CellColor(235, 235, 235))
                .WithColor(WorkbookThemeColorSlot.Accent4, new CellColor(245, 245, 245))
        };
        var sheet = workbook.AddSheet("Sales");
        var address = new CellAddress(sheet.Id, 2, 2);
        var baseStyleId = workbook.RegisterStyle(new CellStyle
        {
            FontThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent2),
            FillThemeColor = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent1)
        });
        var cell = Cell.FromValue(new TextValue("At risk"));
        cell.StyleId = baseStyleId;
        sheet.SetCell(address, cell);
        var conditionalFontTheme = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent3, 0.1);
        var conditionalFillTheme = new WorkbookThemeColorReference(WorkbookThemeColorSlot.Accent4, -0.05);
        var conditionalFontFallback = new CellColor(7, 8, 9);
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(address, address),
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "risk",
            FormatIfTrue = new CellStyle
            {
                FontColor = conditionalFontFallback,
                DxfFontColor = conditionalFontFallback,
                FontThemeColor = conditionalFontTheme,
                FillColor = new CellColor(11, 12, 13),
                FillThemeColor = conditionalFillTheme
            }
        });

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().ContainSingle(issue =>
                issue.Kind == AccessibilityIssueKind.LowContrastCellText &&
                issue.Location == "B2");
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
    public void FindIssues_DoesNotCoerceNumericTextForAboveAverageConditionalFormat()
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

        // Canonical Calc aggregate rules admit numeric/date values, not numeric-looking TextValue cells.
        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText);
    }

    [Fact]
    public void FindIssues_DoesNotCoerceNumericTextForBelowAverageConditionalFormat()
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

        AccessibilityCheckerService.FindIssues(workbook)
            .Should().NotContain(issue => issue.Kind == AccessibilityIssueKind.LowContrastCellText);
    }

    [Fact]
    public void FindIssues_DoesNotCoerceNumericTextForTopRankedConditionalFormat()
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

        issues.Should().BeEmpty();
    }

    [Fact]
    public void FindIssues_DoesNotCoerceNumericTextForBottomPercentConditionalFormat()
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

        issues.Should().BeEmpty();
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
        AssertFormulaParityContrastLocations("ISEVEN($A1)", "B1", "B3", "B5", "B6", "B8");
        AssertFormulaParityContrastLocations("ISODD($A1)", "B2", "B4", "B7");
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
        AssertFormulaParityContrastLocations("OR(ISODD($A1),$C1)", "B1", "B2", "B4", "B5", "B7");
        AssertFormulaParityContrastLocations("NOT(ISODD($A1))", "B1", "B3", "B5", "B6", "B8");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatIsEvenIsOddNonNumericOperandSemantics()
    {
        AssertFormulaParityContrastLocations("ISEVEN($D1)", "B3", "B5", "B6", "B7", "B8");
        AssertFormulaParityContrastLocations("ISODD($D1)", "B2");
        AssertFormulaParityContrastLocations("ISEVEN(\"2\")", "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9");
        AssertFormulaParityContrastLocations("ISODD(TRUE)", "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9");
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
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatIfErrorAndIfNaWrappers()
    {
        AssertFormulaPredicateContrastLocations("IFERROR($A1,TRUE)", "B2", "B3", "B5", "B6", "B7");
        AssertFormulaPredicateContrastLocations("IFNA($A1,TRUE)", "B2", "B3", "B5", "B7");
        AssertFormulaIfContrastLocations("IFERROR(#VALUE!,$A1>=100)", "B2", "B4");
        AssertFormulaIfContrastLocations("IFNA(#N/A,$C1=\"Open\")", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatIfErrorAndIfNaNestedWrappers()
    {
        AssertFormulaIfContrastLocations("AND(IFERROR($A1>=100,FALSE),$C1=\"Open\")", "B4");
        AssertFormulaIfContrastLocations("OR(IFNA(#N/A,$A1>=100),$C1=\"Open\")", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatIfsWrappers()
    {
        AssertFormulaIfContrastLocations("IFS($A1>=125,TRUE,$A1>=100,$C1=\"Closed\",TRUE,FALSE)", "B2", "B4");
        AssertFormulaIfContrastLocations("AND(IFS($A1>=125,TRUE,$A1>=100,TRUE,TRUE,FALSE),$C1=\"Open\")", "B4");
        AssertFormulaIfContrastLocations("IFNA(IFS($A1>200,TRUE),$C1=\"Open\")", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatSwitchWrappers()
    {
        AssertFormulaIfContrastLocations("SWITCH(TRUE,$A1>=125,TRUE,$C1=\"Open\",TRUE,FALSE)", "B3", "B4");
        AssertFormulaIfContrastLocations("SWITCH($C1,\"Open\",$A1>=100,\"Closed\",$A1>=100,FALSE)", "B2", "B4");
        AssertFormulaIfContrastLocations("SWITCH($C1,\"Open\",FALSE,$A1>=100)", "B2");
        AssertFormulaIfContrastLocations("AND(SWITCH(TRUE,$A1>=125,TRUE,$C1=\"Open\",TRUE,FALSE),$A1>=100)", "B4");
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
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatConditionalAggregates()
    {
        AssertFormulaAggregateContrastLocations("SUMIF($C$1:$C$4,$C1,$A$1:$A$4)>175", "B3", "B4");
        AssertFormulaAggregateContrastLocations("COUNTIF($C$1:$C$4,$C1)=2", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGEIF($C$1:$C$4,$C1,$A$1:$A$4)>90", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUMIFS($A$1:$A$4,$C$1:$C$4,$C1,$A$1:$A$4,\">100\")>0", "B3", "B4");
        AssertFormulaAggregateContrastLocations("COUNTIFS($C$1:$C$4,$C1,$A$1:$A$4,\">100\")=1", "B3", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGEIFS($A$1:$A$4,$C$1:$C$4,$C1,$A$1:$A$4,\">=100\")>110", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatConditionalAggregateRangeExpansionAndWildcards()
    {
        AssertFormulaAggregateContrastLocations("SUMIF($C$1:$C$4,\"O*\",$A$1)>175", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGEIF($C$1:$C$4,\"<>C*\",$A$1)>90", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("COUNTIF($C$1:$C$4,\"<>O*\")=2", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_PropagatesFormulaConditionalFormatConditionalAggregateErrorsAndFailsClosedForErrorComparisons()
    {
        AssertFormulaAggregateContrastLocations("ISNA(SUMIF(NA(),\"Open\",$A$1:$A$1))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISNA(COUNTIF(NA(),\"Open\"))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISERROR(AVERAGEIF($C$1:$C$4,\"Missing\",$A$1:$A$4))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISNA(SUMIFS(NA(),$C$1:$C$4,\"Open\"))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISNA(COUNTIFS(NA(),\"Open\"))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISNA(AVERAGEIFS(NA(),$C$1:$C$4,\"Open\"))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISERROR(SUMIFS($A$1:$A$4,$C$1:$C$3,\"Open\"))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUMIF(NA(),\"Open\",$A$1:$A$1)>0");
        AssertFormulaAggregateContrastLocations("AVERAGEIF($C$1:$C$4,\"Missing\",$A$1:$A$4)>0");
        AssertFormulaAggregateContrastLocations("SUMIFS($A$1:$A$4,$C$1:$C$3,\"Open\")>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatMaxMinIfsCriteriaAggregates()
    {
        AssertFormulaAggregateContrastLocations("MAXIFS($A$1:$A$4,$C$1:$C$4,$C1)>100", "B3", "B4");
        AssertFormulaAggregateContrastLocations("MINIFS($A$1:$A$4,$C$1:$C$4,$C1)=75", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("MAXIFS($A1:$A3,$C1:$C3,\"Open\")=125", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("MINIFS($A$1:$A$4,$C$1:$C$4,\"Missing\")=0", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatMaxMinIfsWrappersAndErrors()
    {
        AssertFormulaAggregateContrastLocations("IF(MAXIFS($A$1:$A$4,$C$1:$C$4,$C1)=125,TRUE,FALSE)", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUM(MAXIFS($A$1:$A$4,$C$1:$C$4,\"Open\"),MINIFS($A$1:$A$4,$C$1:$C$4,\"Open\"))=200", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISERROR(MAXIFS($A$1:$A$4,$C$1:$C$3,\"Open\"))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("MAXIFS(NA(),$C$1:$C$4,\"Open\")>0");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatMaxMinIfsUnsupportedArityAndComparisons()
    {
        AssertFormulaAggregateContrastLocations("MAXIFS($A$1:$A$4,$C$1:$C$4)>0");
        AssertFormulaAggregateContrastLocations("MINIFS($A$1:$A$4,$C$1:$C$4,\"Open\",$A$1:$A$3,\">0\")>0");
        AssertFormulaAggregateContrastLocations("MAXIFS($A$1:$A$4,$C$1:$C$4,\"Open\")>$A$1:$A$2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatSubtotalFunctionNumbers()
    {
        AssertFormulaAggregateContrastLocations("SUBTOTAL(9,$A1:$A3)=250", "B1");
        AssertFormulaAggregateContrastLocations("SUBTOTAL(4,$A1:$A3)=125", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUBTOTAL(5,$A1:$A3)=75", "B1", "B2", "B3");
        AssertFormulaAggregateContrastLocations("SUBTOTAL(2,$A$1:$A$4)=4", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatSubtotalWrappersPredicatesAndErrors()
    {
        AssertFormulaAggregateContrastLocations("IF(SUBTOTAL(9,$A1:$A3)>250,TRUE,FALSE)", "B2");
        AssertFormulaAggregateContrastLocations("ISNUMBER(SUBTOTAL(9,$A1:$A3))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISERROR(SUBTOTAL(1,NA()))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUBTOTAL(9,NA())>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatSubtotalHiddenRowsAndNestedAggregates()
    {
        var workbook = CreateFormulaAggregateContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        sheet.FilterHiddenRows.Add(2);
        sheet.HiddenRows.Add(4);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, "SUBTOTAL(9,$A$1:$A$4)=275");

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal("B1", "B2", "B3", "B4");

        workbook = CreateFormulaAggregateContrastWorkbook(out sheet, out firstLabel, out lastLabel);
        sheet.FilterHiddenRows.Add(2);
        sheet.HiddenRows.Add(4);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, "SUBTOTAL(109,$A$1:$A$4)=150");

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal("B1", "B2", "B3", "B4");

        workbook = CreateFormulaAggregateContrastWorkbook(out sheet, out firstLabel, out lastLabel);
        sheet.SetCell(
            new CellAddress(sheet.Id, 2, 1),
            new Cell { Value = new NumberValue(100), FormulaText = "SUBTOTAL(9,$A$1:$A$1)" });
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, "SUBTOTAL(9,$A$1:$A$4)=275");

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal("B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAggregateFunctionNumbers()
    {
        AssertFormulaAggregateContrastLocations("AGGREGATE(9,4,$A1:$A3)=250", "B1");
        AssertFormulaAggregateContrastLocations("AGGREGATE(4,4,$A1:$A3)=125", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("AGGREGATE(14,4,$A$1:$A$4,2)=100", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("AGGREGATE(16,4,$A$1:$A$4,0.5)=87.5", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAggregateWrappersPredicatesAndOptions()
    {
        AssertFormulaAggregateContrastLocations("IF(AGGREGATE(9,4,$A1:$A3)>250,TRUE,FALSE)", "B2");
        AssertFormulaAggregateContrastLocations("AGGREGATE(9,6,$A$1:$A$4,NA())=375", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("AGGREGATE(9,6,\"25\",$A$1:$A$1)=100");
        AssertFormulaAggregateContrastLocations("ISERROR(AGGREGATE(9,4,$A$1:$A$4,NA()))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISERROR(AGGREGATE(9,6,$A$1:$A$4,NA()))");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAggregateHiddenRowsAndNestedAggregates()
    {
        var workbook = CreateFormulaAggregateContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        sheet.FilterHiddenRows.Add(2);
        sheet.HiddenRows.Add(4);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, "AGGREGATE(9,5,$A$1:$A$4)=150");

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal("B1", "B2", "B3", "B4");

        workbook = CreateFormulaAggregateContrastWorkbook(out sheet, out firstLabel, out lastLabel);
        sheet.SetCell(
            new CellAddress(sheet.Id, 2, 1),
            new Cell { Value = new NumberValue(100), FormulaText = "AGGREGATE(9,4,$A$1:$A$1)" });
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, "AGGREGATE(9,0,$A$1:$A$4)=275");

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal("B1", "B2", "B3", "B4");

        workbook = CreateFormulaAggregateContrastWorkbook(out sheet, out firstLabel, out lastLabel);
        sheet.SetCell(
            new CellAddress(sheet.Id, 2, 1),
            new Cell { Value = new NumberValue(100), FormulaText = "AGGREGATE(9,4,$A$1:$A$1)" });
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, "AGGREGATE(9,4,$A$1:$A$4)=375");

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal("B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatAggregateUnsupportedFunctionShapesOptionsAndComparisons()
    {
        AssertFormulaAggregateContrastLocations("AGGREGATE(20,4,$A$1:$A$4)>0");
        AssertFormulaAggregateContrastLocations("AGGREGATE(9,8,$A$1:$A$4)>0");
        AssertFormulaAggregateContrastLocations("AGGREGATE(14,4,$A$1:$A$4)>0");
        AssertFormulaAggregateContrastLocations("AGGREGATE(9,4,$A$1:$A$4)>$A$1:$A$2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDatabaseAggregateSubset()
    {
        AssertFormulaDatabaseAggregateContrastLocations("DSUM($F$1:$I$5,\"Amount\",$K$1:$K$2)=200", "B1", "B2", "B3", "B4");
        AssertFormulaDatabaseAggregateContrastLocations("DSUM($F$1:$I$5,2,$M$1:$M$2)>200", "B1", "B2", "B3", "B4");
        AssertFormulaDatabaseAggregateContrastLocations("DCOUNT($F$1:$I$5,\"Amount\",$K$1:$K$2)=2", "B1", "B2", "B3", "B4");
        AssertFormulaDatabaseAggregateContrastLocations("DCOUNTA($F$1:$I$5,\"Region\",$K$1:$K$2)=2", "B1", "B2", "B3", "B4");
        AssertFormulaDatabaseAggregateContrastLocations("DGET($F$1:$I$5,\"Region\",$N$1:$N$2)=\"East\"", "B1", "B2", "B3", "B4");
        AssertFormulaDatabaseAggregateContrastLocations("DMAX($F$1:$I$5,\"Amount\",$K$1:$K$2)=125", "B1", "B2", "B3", "B4");
        AssertFormulaDatabaseAggregateContrastLocations("DMIN($F$1:$I$5,\"Amount\",$K$1:$K$2)=75", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDatabaseAggregateShiftedReferences()
    {
        AssertFormulaDatabaseAggregateContrastLocations("DCOUNT($F$1:$I$5,$L1,$K$1:$K$2)>0", "B1", "B3");
        AssertFormulaDatabaseAggregateContrastLocations("ISNUMBER(DGET($F$1:$I$5,$L1,$N$1:$N$2))", "B1", "B3");
        AssertFormulaDatabaseAggregateContrastLocations("DSUM($F$1:$I$5,\"Amount\",$J$1:$J2)>300", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_PropagatesFormulaConditionalFormatDatabaseAggregateErrorsAndRejectsUnsupportedShapes()
    {
        AssertFormulaDatabaseAggregateContrastLocations("ISNA(DSUM(NA(),\"Amount\",$K$1:$K$2))", "B1", "B2", "B3", "B4");
        AssertFormulaDatabaseAggregateContrastLocations("ISERROR(DMAX($F$1:$I$5,\"Missing\",$K$1:$K$2))", "B1", "B2", "B3", "B4");
        AssertFormulaDatabaseAggregateContrastLocations("ISERROR(DGET($F$1:$I$5,\"Amount\",$K$1:$K$2))", "B1", "B2", "B3", "B4");
        AssertFormulaDatabaseAggregateContrastLocations("ISERROR(DGET($F$1:$I$5,\"Amount\",$O$1:$O$2))", "B1", "B2", "B3", "B4");
        AssertFormulaDatabaseAggregateContrastLocations("DSUM($F$1:$I$5,\"Amount\",$K$1)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDatabaseAggregateExtendedFunctions()
    {
        AssertFormulaDatabaseAggregateContrastLocations("DAVERAGE($F$1:$I$5,\"Amount\",$K$1:$K$2)=100", "B1", "B2", "B3", "B4");
        AssertFormulaDatabaseAggregateContrastLocations("DPRODUCT($F$1:$I$5,\"Units\",$K$1:$K$2)=12", "B1", "B2", "B3", "B4");
        AssertFormulaDatabaseAggregateContrastLocations("DSTDEV($F$1:$I$5,\"Amount\",$K$1:$K$2)>35", "B1", "B2", "B3", "B4");
        AssertFormulaDatabaseAggregateContrastLocations("DSTDEVP($F$1:$I$5,\"Amount\",$K$1:$K$2)=25", "B1", "B2", "B3", "B4");
        AssertFormulaDatabaseAggregateContrastLocations("DVAR($F$1:$I$5,\"Amount\",$K$1:$K$2)>1200", "B1", "B2", "B3", "B4");
        AssertFormulaDatabaseAggregateContrastLocations("DVARP($F$1:$I$5,\"Amount\",$K$1:$K$2)=625", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialCashFlowFunctions()
    {
        AssertFormulaFinancialCashFlowFunctionContrastLocations("AND($A1<0,NPV(0,\"25\",TRUE,$A1,$C1:$D1)>225)", "B1");
        AssertFormulaFinancialCashFlowFunctionContrastLocations("AND($A1<0,IRR($A1:$D1)>0.1)", "B1");
        AssertFormulaFinancialCashFlowFunctionContrastLocations("AND($A1<0,MIRR($A1:$D1,$H1,$I1)>0.12)", "B1");
        AssertFormulaFinancialCashFlowFunctionContrastLocations("AND($A1<0,XNPV(0.1,$A1:$D1,$E1:$G1)>100)", "B1");
        AssertFormulaFinancialCashFlowFunctionContrastLocations("AND($A1<0,XIRR($A1:$D1,$E1:$G1)>0.1)", "B1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialCashFlowWrappersAndErrors()
    {
        AssertFormulaFinancialCashFlowFunctionContrastLocations("AND($A1<0,NPV(0,N($A1:$D1))>0)", "B1");
        AssertFormulaFinancialCashFlowFunctionContrastLocations("ISNA(IRR($A1:$D1))", "B4");
        AssertFormulaFinancialCashFlowFunctionContrastLocations("ISERROR(MIRR($A1:$D1,$H1,$I1))", "B3", "B4");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatFinancialCashFlowUnsupportedShapesOrComparisons()
    {
        AssertFormulaFinancialCashFlowFunctionContrastLocations("NPV(0)>0");
        AssertFormulaFinancialCashFlowFunctionContrastLocations("IRR()>0");
        AssertFormulaFinancialCashFlowFunctionContrastLocations("MIRR($A1:$D1,$H1)>0");
        AssertFormulaFinancialCashFlowFunctionContrastLocations("XNPV($H1:$I1,$A1:$D1,$E1:$G1)>0");
        AssertFormulaFinancialCashFlowFunctionContrastLocations("XIRR($A1:$D1,$E1:$F1)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAValueAggregates()
    {
        AssertFormulaAggregateContrastLocations("AVERAGEA($D1:$D3)=6", "B1", "B2");
        AssertFormulaAggregateContrastLocations("MINA($D1:$D3)=0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("MAXA($D1:$D3)=12", "B1", "B2");
        AssertFormulaAggregateContrastLocations("AVERAGEA($E1:$E3)=0.5", "B1", "B2");
        AssertFormulaAggregateContrastLocations("MINA($E1:$E3)=0", "B1", "B2");
        AssertFormulaAggregateContrastLocations("MAXA($E1:$E3)=1", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGEA(\"25\",$A1,125)=75", "B1", "B3");
        AssertFormulaAggregateContrastLocations("AVERAGEA(\"\",$D5:$D6)=0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("MINA($D5:$D6)=0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("MAXA($D5:$D6)=0", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAValueAggregateWrappersPredicatesAndNestedAggregates()
    {
        AssertFormulaAggregateContrastLocations("AND(AVERAGEA($D1:$D3)=6,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaAggregateContrastLocations("IF(MINA($D1:$D3)=0,TRUE,FALSE)", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISNUMBER(MAXA($E1:$E3))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUM(AVERAGEA($D1:$D3),94)=100", "B1", "B2");
        AssertFormulaAggregateContrastLocations("AVERAGEA(SUM($A1:$A2),$A1^2)>5000", "B2", "B4");
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
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatSumProductScalarComparisons()
    {
        AssertFormulaAggregateContrastLocations("SUMPRODUCT(3,4)=12", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUMPRODUCT(\"5\",2)=0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUMPRODUCT(TRUE,2)=0");
        AssertFormulaAggregateContrastLocations("SUMPRODUCT(DATE(2023,1,2),1)=44928", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatSumProductRanges()
    {
        AssertFormulaAggregateContrastLocations("SUMPRODUCT($A1:$A3)>250", "B2");
        AssertFormulaAggregateContrastLocations("SUMPRODUCT($A1:$A3,$A2:$A4)>16000", "B1", "B2");
        AssertFormulaAggregateContrastLocations("SUMPRODUCT($D1:$D3)=12", "B1", "B2");
        AssertFormulaAggregateContrastLocations("SUMPRODUCT($D1:$D3,$A1:$A3)=1200", "B1", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatSumProductWrappersPredicatesAndNestedAggregates()
    {
        AssertFormulaAggregateContrastLocations("AND(SUMPRODUCT($A1:$A3,$A2:$A4)>16000,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaAggregateContrastLocations("IF(SUMPRODUCT($A1:$A3)>250,TRUE,FALSE)", "B2");
        AssertFormulaAggregateContrastLocations("ISNUMBER(SUMPRODUCT($A1:$A3,$A2:$A4))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUM(SUMPRODUCT($A1:$A3,$A2:$A4),1)>16000", "B1", "B2");
        AssertFormulaAggregateContrastLocations("SUMPRODUCT(SUM($A1:$A2),$A1^2)>1500000", "B2", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatSumProductOperandCoercionAndErrorSemantics()
    {
        AssertFormulaAggregateContrastLocations("SUMPRODUCT()>0");
        AssertFormulaAggregateContrastLocations("SUMPRODUCT($A1:$A2,$A1:$A3)>0");
        AssertFormulaAggregateContrastLocations("SUMPRODUCT($A1:$B1,$A1:$A2)>0");
        AssertFormulaAggregateContrastLocations("SUMPRODUCT($A1:$A20000)>0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUMPRODUCT(A0,$A1)>0");
        AssertFormulaAggregateContrastLocations("SUMPRODUCT(NA(),1)>0");
        AssertFormulaAggregateContrastLocations("SUMPRODUCT(KURT($A1),$A1)>0");
        AssertFormulaAggregateContrastLocations("SUMPRODUCT($A1,KURT($A1))>0");
        AssertFormulaAggregateContrastLocations("SUMPRODUCT($A1/0,1)>0");
        AssertFormulaAggregateContrastLocations("SUMPRODUCT(1E308,1E308)>0");
        AssertFormulaAggregateContrastLocations("SUMPRODUCT($A1:$A3,2)>0");

        var workbook = new Workbook("Accessibility");
        var sheet = workbook.AddSheet("Sales");
        var label = new CellAddress(sheet.Id, 1, 2);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(1E308));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new NumberValue(1E308));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), ErrorValue.Value);
        sheet.SetCell(label, new TextValue("Overflow source"));
        AddFormulaContrastRule(sheet, label, label, "SUMPRODUCT($A$1:$A$2)>0");
        AddFormulaContrastRule(sheet, label, label, "SUMPRODUCT($C$1)=0");

        FindLowContrastCellTextIssues(workbook).Should().BeEmpty();
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatPairwiseAggregateScalarComparisons()
    {
        AssertFormulaAggregateContrastLocations("SUMXMY2(3,4)=1", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUMX2MY2(3,4)=-7", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUMX2PY2(3,4)=25", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUMXMY2(TRUE,\"3\")=4", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUMXMY2(DATE(2023,1,2),DATE(2023,1,1))=1", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatPairwiseAggregateRangeComparisons()
    {
        AssertFormulaAggregateContrastLocations("SUMXMY2($A1:$A3,$A2:$A4)>3000", "B1", "B2");
        AssertFormulaAggregateContrastLocations("SUMX2MY2($A1:$A3,$A2:$A4)<0", "B1", "B2", "B3");
        AssertFormulaAggregateContrastLocations("SUMX2PY2($A1:$A3,$A2:$A4)>30000", "B1", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatPairwiseAggregateWrappersAndPredicates()
    {
        AssertFormulaAggregateContrastLocations("AND(SUMXMY2($A1:$A3,$A2:$A4)>3000,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaAggregateContrastLocations("IF(SUMX2PY2($A1:$A3,$A2:$A4)>30000,TRUE,FALSE)", "B1", "B2");
        AssertFormulaAggregateContrastLocations("ISNUMBER(SUMX2MY2($A1:$A3,$A2:$A4))", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatPairwiseAggregateNesting()
    {
        AssertFormulaAggregateContrastLocations("SUM(SUMXMY2($A1:$A3,$A2:$A4),1)>3000", "B1", "B2");
        AssertFormulaAggregateContrastLocations("SUMXMY2(SUM($A1:$A2),$A1^2)>30000000", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatPairwiseAggregateSkipsReferencedNonNumericPairs()
    {
        AssertFormulaAggregateContrastLocations("SUMXMY2($D1:$D3,$A1:$A3)=7744", "B1", "B2");
        AssertFormulaAggregateContrastLocations("SUMX2PY2($D1:$D3,$E1:$E3)=0", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatPairwiseAggregateUnsupportedOperands()
    {
        AssertFormulaAggregateContrastLocations("SUMXMY2($A1)>0");
        AssertFormulaAggregateContrastLocations("SUMX2MY2($A1,$A2,$A3)>0");
        AssertFormulaAggregateContrastLocations("SUMX2PY2()>0");
        AssertFormulaAggregateContrastLocations("SUMXMY2(\"n/a\",1)>0");
        AssertFormulaAggregateContrastLocations("SUMXMY2(\"1E309\",1)>0");
        AssertFormulaAggregateContrastLocations("SUMX2PY2(1E308,1)>0");
        AssertFormulaAggregateContrastLocations("SUMXMY2(1E308*1E308,1)>0");
        AssertFormulaAggregateContrastLocations("SUMXMY2($A1:$A2,$A1:$A3)>0");
        AssertFormulaAggregateContrastLocations("SUMXMY2($A1:$A20000,$A1:$A20000)>0");
        AssertFormulaAggregateContrastLocations("SUMXMY2(A0,$A1)>0");
        AssertFormulaAggregateContrastLocations("SUMXMY2(KURT($A1),$A1)>0");
        AssertFormulaAggregateContrastLocations("SUMXMY2($A1,KURT($A1))>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatRegressionAggregateComparisons()
    {
        AssertFormulaAggregateContrastLocations("CORREL($A$1:$A$4,$A$1:$A$4)>0.99", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("PEARSON($A$1:$A$4,$A$1:$A$4)>0.99", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("COVARIANCE.P($A$1:$A$4,$A$1:$A$4)=429.6875", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("COVARIANCE.S($A$1:$A$4,$A$1:$A$4)>572", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("RSQ($A$1:$A$4,$A$1:$A$4)>0.99", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SLOPE($A$1:$A$4,$A$1:$A$4)=1", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("INTERCEPT($A$1:$A$4,$A$1:$A$4)=0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("FORECAST(150,$A$1:$A$4,$A$1:$A$4)=150", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("FORECAST.LINEAR(150,$A$1:$A$4,$A$1:$A$4)=150", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("STEYX($A$1:$A$4,$A$1:$A$4)=0", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatRegressionShiftedRanges()
    {
        AssertFormulaAggregateContrastLocations("CORREL($A1:$A3,$A2:$A4)<-0.8", "B1", "B2");
        AssertFormulaAggregateContrastLocations("COVARIANCE.P($A1:$A3,$A2:$A4)<-200", "B1", "B2");
        AssertFormulaAggregateContrastLocations("COVARIANCE.S($A1:$A3,$A2:$A4)<-300", "B1", "B2");
        AssertFormulaAggregateContrastLocations("SLOPE($A1:$A3,$A2:$A4)<0", "B1", "B2");
        AssertFormulaAggregateContrastLocations("FORECAST($A1,$A1:$A3,$A2:$A4)>90", "B1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatRegressionWrappersPredicatesAndScalarOperands()
    {
        AssertFormulaAggregateContrastLocations("AND(CORREL($A1:$A3,$A2:$A4)<-0.8,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaAggregateContrastLocations("IF(RSQ($A$1:$A$4,$A$1:$A$4)>0.99,TRUE,FALSE)", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISNUMBER(STEYX($A$1:$A$4,$A$1:$A$4))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISERROR(SLOPE($A$1:$A$4,$D$1:$D$4))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISNA(CORREL($A$1:$A$3,$A$1:$A$4))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISERROR(FORECAST(\"n/a\",$A$1:$A$4,$A$1:$A$4))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("COVARIANCE.P(3,4)=0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("COVARIANCE.P(SUM($A1:$A2),$A1^2)=0", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatRegressionArityShapeAndComparisonSemantics()
    {
        AssertFormulaAggregateContrastLocations("CORREL($A$1:$A$4)>0");
        AssertFormulaAggregateContrastLocations("FORECAST($A1,$A$1:$A$4)>0");
        AssertFormulaAggregateContrastLocations("FORECAST($A$1:$A$2,$A$1:$A$4,$A$1:$A$4)>0");
        AssertFormulaAggregateContrastLocations("PEARSON($A1:$A20000,$A1:$A20000)>0", "B1", "B2", "B3");
        AssertFormulaAggregateContrastLocations("CORREL(\"n/a\",1)>0");
        AssertFormulaAggregateContrastLocations("CORREL($A$1:$A$3,$A$1:$A$4)>0");
        AssertFormulaAggregateContrastLocations("SLOPE($A$1:$A$4,$A$1:$A$4)>$A$1:$A$2");
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
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAValueStdDevAndVarianceAggregates()
    {
        AssertFormulaAggregateContrastLocations("STDEVA($D1:$D3)>8", "B1", "B2");
        AssertFormulaAggregateContrastLocations("STDEVPA($E1:$E3)=0.5", "B1", "B2");
        AssertFormulaAggregateContrastLocations("VARA($D1:$D3)=72", "B1", "B2");
        AssertFormulaAggregateContrastLocations("VARPA($E1:$E3)=0.25", "B1", "B2");
        AssertFormulaAggregateContrastLocations("VARA(\"5\",TRUE,\"\")=7", "B1", "B2", "B3", "B4");
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
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatShapeAndTrimAggregates()
    {
        AssertFormulaAggregateContrastLocations("KURT($A$1:$A$4)<-1", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SKEW($A1:$A3)>1", "B1");
        AssertFormulaAggregateContrastLocations("SKEW.P($A1:$A3)>0.4", "B1");
        AssertFormulaAggregateContrastLocations("TRIMMEAN($A$1:$A$4,0.5)=87.5", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("TRIMMEAN($A1:$A3,0)=100", "B2", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatShapeAndTrimWrappersPredicatesAndErrors()
    {
        AssertFormulaAggregateContrastLocations("AND(SKEW($A1:$A3)>1,$C1=\"Closed\")", "B1");
        AssertFormulaAggregateContrastLocations("IF(TRIMMEAN($A$1:$A$4,0.5)=87.5,TRUE,FALSE)", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISNUMBER(KURT($A$1:$A$4))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISERROR(KURT($A1:$A3))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("ISERROR(TRIMMEAN($A$1:$A$4,-0.1))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUM(SKEW.P($A1:$A3),1)>1.5", "B1");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatShapeAndTrimShapeAndInvalidDataSemantics()
    {
        AssertFormulaAggregateContrastLocations("KURT()>0");
        AssertFormulaAggregateContrastLocations("TRIMMEAN($A$1:$A$4)>0");
        AssertFormulaAggregateContrastLocations("TRIMMEAN($A$1:$A$4,0,1)>0");
        AssertFormulaAggregateContrastLocations("KURT($A1:$A3)>0");
        AssertFormulaAggregateContrastLocations("SKEW($A1:$A2)>0");
        AssertFormulaAggregateContrastLocations("SKEW.P($A1)>0");
        AssertFormulaAggregateContrastLocations("TRIMMEAN($D3:$D5,0)>0");
        AssertFormulaAggregateContrastLocations("SKEW($A1:$A20000)>0", "B1");
        AssertFormulaAggregateContrastLocations("TRIMMEAN($A1:$A20000,0)>0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("TRIMMEAN($A$1:$A$4,1.1)>0");
        AssertFormulaAggregateContrastLocations("TRIMMEAN($A$1:$A$4,$A$1:$A$2)>0");
        AssertFormulaAggregateContrastLocations("KURT(\"n/a\",$A1,$A2,$A3)>0");
        AssertFormulaAggregateContrastLocations("SKEW(\"n/a\",$A1,$A2)>0");
        AssertFormulaAggregateContrastLocations("SKEW.P(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("KURT(1E308,0,0,0)>0");
        AssertFormulaAggregateContrastLocations("KURT(A0,$A1,$A2,$A3)>0");
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
    public void FindIssues_UsesCanonicalFormulaConditionalFormatAggregateForOversizedRangeOrInvalidDirectText()
    {
        AssertFormulaAggregateContrastLocations("SUM($A1:$A20000)>0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUM(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("MEDIAN()>0");
        AssertFormulaAggregateContrastLocations("MEDIAN($A1:$A20000)>0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("MEDIAN(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("MEDIAN($D3:$D5)>0");
        AssertFormulaAggregateContrastLocations("MEDIAN($A1/0)>0");
        AssertFormulaAggregateContrastLocations("MEDIAN(1E308,1E308)>0");
        AssertFormulaAggregateContrastLocations("MEDIAN(A0)>0");
        AssertFormulaAggregateContrastLocations("DEVSQ()>0");
        AssertFormulaAggregateContrastLocations("DEVSQ($A1:$A20000)>0", "B1", "B2", "B3");
        AssertFormulaAggregateContrastLocations("DEVSQ(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("DEVSQ($D3:$D5)>0");
        AssertFormulaAggregateContrastLocations("DEVSQ($A1/0)>0");
        AssertFormulaAggregateContrastLocations("DEVSQ(1E308,0)>0");
        AssertFormulaAggregateContrastLocations("DEVSQ(A0)>0");
        AssertFormulaAggregateContrastLocations("AVEDEV()>0");
        AssertFormulaAggregateContrastLocations("AVEDEV($A1:$A20000)>0", "B1", "B2", "B3");
        AssertFormulaAggregateContrastLocations("AVEDEV(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("AVEDEV($D3:$D5)>0");
        AssertFormulaAggregateContrastLocations("AVEDEV($A1/0)>0");
        AssertFormulaAggregateContrastLocations("AVEDEV(1E308,-1E308)>0");
        AssertFormulaAggregateContrastLocations("AVEDEV(A0)>0");
        AssertFormulaAggregateContrastLocations("GEOMEAN()>0");
        AssertFormulaAggregateContrastLocations("GEOMEAN($A1:$A20000)>0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("GEOMEAN(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("GEOMEAN($D3:$D5)>0");
        AssertFormulaAggregateContrastLocations("GEOMEAN(0,$A1)>0");
        AssertFormulaAggregateContrastLocations("GEOMEAN(-1,$A1)>0");
        AssertFormulaAggregateContrastLocations("GEOMEAN($A1/0)>0");
        AssertFormulaAggregateContrastLocations("GEOMEAN(1E308,1E308)>0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("GEOMEAN(A0)>0");
        AssertFormulaAggregateContrastLocations("HARMEAN()>0");
        AssertFormulaAggregateContrastLocations("HARMEAN($A1:$A20000)>0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("HARMEAN(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("HARMEAN($D3:$D5)>0");
        AssertFormulaAggregateContrastLocations("HARMEAN(0,$A1)>0");
        AssertFormulaAggregateContrastLocations("HARMEAN(-1,$A1)>0");
        AssertFormulaAggregateContrastLocations("HARMEAN($A1/0)>0");
        AssertFormulaAggregateContrastLocations("HARMEAN(1E308*1E308)>0");
        AssertFormulaAggregateContrastLocations("HARMEAN(A0)>0");
        AssertFormulaAggregateContrastLocations("STDEV()>0");
        AssertFormulaAggregateContrastLocations("STDEV($A1)>0");
        AssertFormulaAggregateContrastLocations("STDEV($A1:$A20000)>0", "B1", "B2", "B3");
        AssertFormulaAggregateContrastLocations("STDEV(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("STDEV($D1:$D3)>0");
        AssertFormulaAggregateContrastLocations("STDEV($A1/0)>0");
        AssertFormulaAggregateContrastLocations("STDEV(1E308,0)>0");
        AssertFormulaAggregateContrastLocations("STDEV(A0)>0");
        AssertFormulaAggregateContrastLocations("STDEVP()>0");
        AssertFormulaAggregateContrastLocations("STDEVP($A1:$A20000)>0", "B1", "B2", "B3");
        AssertFormulaAggregateContrastLocations("STDEVP(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("STDEV.P($D3:$D5)>0");
        AssertFormulaAggregateContrastLocations("STDEV.P($A1/0)>0");
        AssertFormulaAggregateContrastLocations("STDEV.P(1E308,0)>0");
        AssertFormulaAggregateContrastLocations("STDEV.P(A0)>0");
        AssertFormulaAggregateContrastLocations("STDEVA()>0");
        AssertFormulaAggregateContrastLocations("STDEVA($A1)>0");
        AssertFormulaAggregateContrastLocations("STDEVA(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("STDEVPA($A1/0)>0");
        AssertFormulaAggregateContrastLocations("VAR()>0");
        AssertFormulaAggregateContrastLocations("VAR($A1)>0");
        AssertFormulaAggregateContrastLocations("VAR($A1:$A20000)>0", "B1", "B2", "B3");
        AssertFormulaAggregateContrastLocations("VAR(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("VAR($D1:$D3)>0");
        AssertFormulaAggregateContrastLocations("VAR($A1/0)>0");
        AssertFormulaAggregateContrastLocations("VAR(1E308,0)>0");
        AssertFormulaAggregateContrastLocations("VAR(A0)>0");
        AssertFormulaAggregateContrastLocations("VARP()>0");
        AssertFormulaAggregateContrastLocations("VARP($A1:$A20000)>0", "B1", "B2", "B3");
        AssertFormulaAggregateContrastLocations("VARP(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("VAR.P($D3:$D5)>0");
        AssertFormulaAggregateContrastLocations("VAR.P($A1/0)>0");
        AssertFormulaAggregateContrastLocations("VAR.P(1E308,0)>0");
        AssertFormulaAggregateContrastLocations("VAR.P(A0)>0");
        AssertFormulaAggregateContrastLocations("VARA()>0");
        AssertFormulaAggregateContrastLocations("VARA($A1)>0");
        AssertFormulaAggregateContrastLocations("VARA(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("VARPA($A1/0)>0");
        AssertFormulaAggregateContrastLocations("AVERAGEA(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("MINA(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("MAXA(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("AVERAGEA($D5:$D6)=0");
        AssertFormulaAggregateContrastLocations("AVERAGEA($A1/0)>0");
        AssertFormulaAggregateContrastLocations("MINA($A1/0)>0");
        AssertFormulaAggregateContrastLocations("MAXA(KURT($A1))>0");
        AssertFormulaAggregateContrastLocations("MAXA(NA())>0");
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
        AssertFormulaAggregateContrastLocations("COUNTBLANK($D1,$A1+1)=1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAggregateArgumentReferenceShifting()
    {
        AssertFormulaAggregateContrastLocations("SUM($A1+25,$A2)>175", "B1", "B2", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatStatisticalSelectionAndRankFunctions()
    {
        AssertFormulaStatisticalSelectionContrastLocations("$A1=LARGE($A$1:$A$4,1)", "B4");
        AssertFormulaStatisticalSelectionContrastLocations("$A1=SMALL($A$1:$A$4,1)", "B1", "B3");
        AssertFormulaStatisticalSelectionContrastLocations("RANK($A1,$A$1:$A$4)=2", "B2");
        AssertFormulaStatisticalSelectionContrastLocations("RANK.EQ($A1,$A$1:$A$4,1)=1", "B1", "B3");
        AssertFormulaStatisticalSelectionContrastLocations("RANK.AVG($A1,$A$1:$A$4)=3.5", "B1", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatPercentileQuartileAndPercentRankFunctions()
    {
        AssertFormulaStatisticalSelectionContrastLocations("$A1>PERCENTILE($A$1:$A$4,0.5)", "B2", "B4");
        AssertFormulaStatisticalSelectionContrastLocations("$A1=PERCENTILE.INC($A$1:$A$4,0)", "B1", "B3");
        AssertFormulaStatisticalSelectionContrastLocations("$A1>PERCENTILE.EXC($A$1:$A$4,0.5)", "B2", "B4");
        AssertFormulaStatisticalSelectionContrastLocations("$A1>=QUARTILE($A$1:$A$4,3)", "B4");
        AssertFormulaStatisticalSelectionContrastLocations("$A1=QUARTILE.INC($A$1:$A$4,1)", "B1", "B3");
        AssertFormulaStatisticalSelectionContrastLocations("$A1>QUARTILE.EXC($A$1:$A$4,2)", "B2", "B4");
        AssertFormulaStatisticalSelectionContrastLocations("PERCENTRANK($A$1:$A$4,$A1)=0", "B1", "B3");
        AssertFormulaStatisticalSelectionContrastLocations("PERCENTRANK.INC($A$1:$A$4,$A1)>0.5", "B2", "B4");
        AssertFormulaStatisticalSelectionContrastLocations("PERCENTRANK.EXC($A$1:$A$4,$A1)=0.2", "B1", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatModeProbAndPercentOfFunctions()
    {
        AssertFormulaStatisticalSelectionContrastLocations("$A1=MODE($A$1:$A$4)", "B1", "B3");
        AssertFormulaStatisticalSelectionContrastLocations("$A1=MODE.SNGL($A$1:$A$4)", "B1", "B3");
        AssertFormulaStatisticalSelectionContrastLocations("PROB($A$1:$A$4,$F$1:$F$4,$A1)=0.5", "B1", "B3");
        AssertFormulaStatisticalSelectionContrastLocations("PERCENTOF($A1,$A$1:$A$4)>0.25", "B2", "B4");
    }

    [Fact]
    public void FindIssues_PropagatesFormulaConditionalFormatStatisticalSelectionErrorsAndFailsClosedForErrorComparisons()
    {
        AssertFormulaStatisticalSelectionContrastLocations("ISNA(LARGE(NA(),1))", FormulaStatisticalSelectionAllLocations);
        AssertFormulaStatisticalSelectionContrastLocations("ISNA(RANK(5,$A$1:$A$4,NA()))", FormulaStatisticalSelectionAllLocations);
        AssertFormulaStatisticalSelectionContrastLocations("ISERROR(PERCENTILE($A$1:$A$4,2))", FormulaStatisticalSelectionAllLocations);
        AssertFormulaStatisticalSelectionContrastLocations("ISNA(PERCENTRANK($A$1:$A$4,1000))", FormulaStatisticalSelectionAllLocations);
        AssertFormulaStatisticalSelectionContrastLocations("ISNA(PROB($A$1:$A$4,$F$1:$F$3,$A1))", FormulaStatisticalSelectionAllLocations);
        AssertFormulaStatisticalSelectionContrastLocations("ISERROR(PERCENTOF($A1,$D$3:$D$3))", FormulaStatisticalSelectionAllLocations);
        AssertFormulaStatisticalSelectionContrastLocations("LARGE(NA(),1)>0");
        AssertFormulaStatisticalSelectionContrastLocations("PERCENTILE($A$1:$A$4,2)>0");
        AssertFormulaStatisticalSelectionContrastLocations("PROB($A$1:$A$4,$F$1:$F$3,$A1)>0");
        AssertFormulaStatisticalSelectionContrastLocations("PERCENTOF($A1,$D$3:$D$3)>0");
        AssertFormulaStatisticalSelectionContrastLocations("LARGE($A$1:$A$4,$G$1:$G$2)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatStatisticalTestFunctions()
    {
        AssertFormulaStatisticalTestContrastLocations("Z.TEST($D$1:$D$4,$A1,2)<0.2", "B1", "B2");
        AssertFormulaStatisticalTestContrastLocations("ZTEST($D$1:$D$4,$A1)<0.25", "B1", "B2");
        AssertFormulaStatisticalTestContrastLocations("AND($C1,T.TEST($D$1:$D$4,$E$1:$E$4,2,2)<0.6)", "B1", "B2", "B4");
        AssertFormulaStatisticalTestContrastLocations("AND($C1,TTEST($D$1:$D$4,$E$1:$E$4,2,3)<0.6)", "B1", "B2", "B4");
        AssertFormulaStatisticalTestContrastLocations("F.TEST($D$1:$D$4,$E$1:$E$4)>0.8", FormulaStatisticalTestAllLocations);
        AssertFormulaStatisticalTestContrastLocations("FTEST($D$1:$D$4,$E$1:$E$4)>0.8", FormulaStatisticalTestAllLocations);
        AssertFormulaStatisticalTestContrastLocations("CHISQ.TEST($H$1:$I$2,$K$1:$L$2)<0.05", FormulaStatisticalTestAllLocations);
        AssertFormulaStatisticalTestContrastLocations("CHITEST($H$1:$I$2,$K$1:$L$2)<0.05", FormulaStatisticalTestAllLocations);
    }

    [Fact]
    public void FindIssues_PropagatesFormulaConditionalFormatStatisticalTestErrorsAndLeavesFrequencyUnsupported()
    {
        AssertFormulaStatisticalTestContrastLocations("ISERROR(Z.TEST($D$1:$D$4,$A1,0))", FormulaStatisticalTestAllLocations);
        AssertFormulaStatisticalTestContrastLocations("ISNA(T.TEST($D$1:$D$3,$E$1:$E$4,2,1))", FormulaStatisticalTestAllLocations);
        AssertFormulaStatisticalTestContrastLocations("ISERROR(F.TEST($D$1:$D$4,$D$1:$D$1))", FormulaStatisticalTestAllLocations);
        AssertFormulaStatisticalTestContrastLocations("ISNA(CHISQ.TEST($H$1:$I$2,$K$1:$K$2))", FormulaStatisticalTestAllLocations);
        AssertFormulaStatisticalTestContrastLocations("FREQUENCY($D$1:$D$4,$A$1:$A$4)>0");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatAggregateArgumentCoercionAndErrorSemantics()
    {
        AssertFormulaAggregateContrastLocations("SUM($D1&\"x\")>0");
        AssertFormulaAggregateContrastLocations("COUNTA($D1&\"x\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUM($A1/0)>0");
        AssertFormulaAggregateContrastLocations("SUM(1E308*1E308)>0");
        AssertFormulaAggregateContrastLocations("SUM(KURT($A1)+1)>0");
        AssertFormulaAggregateContrastLocations("SUM(\"n/a\"+$A1)>0");
        AssertFormulaAggregateContrastLocations("SUM(SUM($A1:$A20000))>0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUMSQ($A1:$A20000)>0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUMSQ(\"n/a\",$A1)>0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUMSQ(1E308)>0");
        AssertFormulaAggregateContrastLocations("SUMSQ(KURT($A1))>0");
        AssertFormulaAggregateContrastLocations("SUMSQ($A1/0)>0");
        AssertFormulaAggregateContrastLocations("DEVSQ(KURT($A1))>0");
        AssertFormulaAggregateContrastLocations("PRODUCT($A1:$A20000)>0", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("PRODUCT(\"n/a\",$A1)>0");
        AssertFormulaAggregateContrastLocations("PRODUCT(1E308,1E308)>0");
        AssertFormulaAggregateContrastLocations("PRODUCT(KURT($A1))>0");
        AssertFormulaAggregateContrastLocations("AVEDEV(KURT($A1))>0");
        AssertFormulaAggregateContrastLocations("GEOMEAN(KURT($A1))>0");
        AssertFormulaAggregateContrastLocations("HARMEAN(KURT($A1))>0");
        AssertFormulaAggregateContrastLocations("VAR(KURT($A1))>0");
        AssertFormulaAggregateContrastLocations("VAR.P(KURT($A1))>0");
        AssertFormulaAggregateContrastLocations("COUNTBLANK()>0");
        AssertFormulaAggregateContrastLocations("COUNTBLANK($D1:$D20000)>0", "B1", "B2", "B3", "B4");
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
        AssertFormulaArithmeticContrastLocations("EVEN($A1/50)>2", "B4");
        AssertFormulaArithmeticContrastLocations("EVEN(-$A1/50)<-2", "B4");
        AssertFormulaArithmeticContrastLocations("EVEN(1.2)=2", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("EVEN(3)=4", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("EVEN(-1.2)=-2", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("EVEN(-3)=-4", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("EVEN(2)=2", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("EVEN(-2)=-2", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("EVEN(0)=0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ROUND($A1/3,0)>=33", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ROUNDUP($A1/100,1)>=1", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ROUNDUP(-$A1/100,1)<=-1", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ROUNDUP($A1,-1)>=100", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN($A1/100,1)>=1", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN(-1.29,1)=-1.2", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN($A1,-1)>=100", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("MROUND($A1,30)>=120", "B4");
        AssertFormulaArithmeticContrastLocations("MROUND(-$A1,-30)<=-120", "B4");
        AssertFormulaArithmeticContrastLocations("MROUND($A1,0)=0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("MROUND(10,3)=9", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("MROUND(10,4)=12", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("MROUND(1.5,1)=2", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("MROUND(-10,-3)=-9", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("MROUND(-1.5,-1)=-2", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("TRUNC($A1/100,1)>=1", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("TRUNC(-1.29,1)=-1.2", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("TRUNC($A1,-1)>=100", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("TRUNC(1.99)=1", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("TRUNC($A1/100)>=1", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("FACT($A1/25)>20", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("FACT(5.9)=120", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FACTDOUBLE($A1/25)>7", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("FACTDOUBLE(7)=105", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FACTDOUBLE(8)=384", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FACTDOUBLE(7.9)=105", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FACTDOUBLE(0)=1", "B1", "B2", "B3", "B4");
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
        AssertFormulaArithmeticContrastLocations("ASINH(0)=0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ASINH($A1/100)>0.88", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ASINH(-$A1/100)<-0.88", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ROUND(ASINH(SINH(1)),2)=1", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ASINH(1E308)>700", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ACOSH(1)=0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ACOSH($A1/50)>1.2", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ROUND(ACOSH(COSH(1)),2)=1", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ACOSH(1E308)>700", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COSH($A1/100)>1.5", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("TANH($A1/100)>0.75", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ATANH(0)=0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ATANH(0.5)>0.54", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ATANH(-0.5)<-0.54", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ATANH($A1/200)>0.54", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ROUND(ATANH(TANH(1)),2)=1", "B1", "B2", "B3", "B4");
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
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCeilingScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("CEILING($A1,30)>=120", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING($A1,0)=0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING(10,3)=12", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING(-10,3)=-9");
        AssertFormulaArithmeticContrastLocations("CEILING(-10,-3)=-12", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING(-1.5,1)=-1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCeilingScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(CEILING($A1,30)>=120,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(CEILING($A1,30)>=90,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(CEILING($A1,30))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISEVEN(CEILING($A1,25))", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCeilingScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(CEILING($A1,30),1)>=121", "B2", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGE(CEILING($A1,30),$A1)>110", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatCeilingScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("CEILING($A1)>0");
        AssertFormulaArithmeticContrastLocations("CEILING($A1,10,1)>0");
        AssertFormulaArithmeticContrastLocations("CEILING(\"10\",3)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING($A1,\"10\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING($A1&\"x\",10)>0");
        AssertFormulaArithmeticContrastLocations("CEILING(KURT($A1),10)>0");
        AssertFormulaArithmeticContrastLocations("CEILING($A1,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("CEILING($A1,-10)>0");
        AssertFormulaArithmeticContrastLocations("CEILING(1E308*1E308,10)>0");
        AssertFormulaArithmeticContrastLocations("CEILING($A1,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("CEILING(1E308,0.1)>0");
        AssertFormulaArithmeticContrastLocations("CEILING(EXP(1000),2)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCeilingMathScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("CEILING.MATH($A1,30)>=120", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH($A1)>=100", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH($A1,0)=0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH(10,3)=12", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH(-10,3)=-9", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH(-10,-3)=-9", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH(-10,3,1)=-12", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH(-1.5,1,1)=-2", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCeilingMathScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(CEILING.MATH($A1,30)>=120,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(CEILING.MATH($A1,30)>=90,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(CEILING.MATH($A1))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISEVEN(CEILING.MATH($A1,25))", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCeilingMathScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(CEILING.MATH($A1,30),1)>=121", "B2", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGE(CEILING.MATH($A1,30),$A1)>110", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatCeilingMathScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("CEILING.MATH()>0");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH($A1,10,1,0)>0");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH(\"10\",3)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH($A1,\"10\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH($A1,10,\"1\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH($A1&\"x\",10)>0");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH(KURT($A1),10)>0");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH($A1,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH($A1,10,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH(1E308*1E308,10)>0");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH($A1,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH($A1,10,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH(1E308,0.1)>0");
        AssertFormulaArithmeticContrastLocations("CEILING.MATH(EXP(1000),2)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatIsoCeilingScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("ISO.CEILING($A1,30)>=120", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ISO.CEILING($A1)>=100", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ISO.CEILING($A1,0)=0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISO.CEILING(10,3)=12", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISO.CEILING(-10,3)=-9", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISO.CEILING(-10,-3)=-9", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISO.CEILING(-1.5,1)=-1", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatIsoCeilingScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(ISO.CEILING($A1,30)>=120,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(ISO.CEILING($A1,30)>=90,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(ISO.CEILING($A1))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISEVEN(ISO.CEILING($A1,25))", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatIsoCeilingScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(ISO.CEILING($A1,30),1)>=121", "B2", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGE(ISO.CEILING($A1,30),$A1)>110", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatIsoCeilingScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("ISO.CEILING()>0");
        AssertFormulaArithmeticContrastLocations("ISO.CEILING($A1,10,1)>0");
        AssertFormulaArithmeticContrastLocations("ISO.CEILING(\"10\",3)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISO.CEILING($A1,\"10\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISO.CEILING($A1&\"x\",10)>0");
        AssertFormulaArithmeticContrastLocations("ISO.CEILING(KURT($A1),10)>0");
        AssertFormulaArithmeticContrastLocations("ISO.CEILING($A1,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("ISO.CEILING(1E308*1E308,10)>0");
        AssertFormulaArithmeticContrastLocations("ISO.CEILING($A1,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ISO.CEILING(1E308,0.1)>0");
        AssertFormulaArithmeticContrastLocations("ISO.CEILING(EXP(1000),2)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCeilingPreciseScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("CEILING.PRECISE($A1,30)>=120", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING.PRECISE($A1)>=100", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING.PRECISE($A1,0)=0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING.PRECISE(10,3)=12", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING.PRECISE(-10,3)=-9", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING.PRECISE(-10,-3)=-9", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING.PRECISE(-1.5,1)=-1", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCeilingPreciseScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(CEILING.PRECISE($A1,30)>=120,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(CEILING.PRECISE($A1,30)>=90,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(CEILING.PRECISE($A1))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISEVEN(CEILING.PRECISE($A1,25))", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCeilingPreciseScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(CEILING.PRECISE($A1,30),1)>=121", "B2", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGE(CEILING.PRECISE($A1,30),$A1)>110", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatCeilingPreciseScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("CEILING.PRECISE()>0");
        AssertFormulaArithmeticContrastLocations("CEILING.PRECISE($A1,10,1)>0");
        AssertFormulaArithmeticContrastLocations("CEILING.PRECISE(\"10\",3)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING.PRECISE($A1,\"10\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CEILING.PRECISE($A1&\"x\",10)>0");
        AssertFormulaArithmeticContrastLocations("CEILING.PRECISE(KURT($A1),10)>0");
        AssertFormulaArithmeticContrastLocations("CEILING.PRECISE($A1,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("CEILING.PRECISE(1E308*1E308,10)>0");
        AssertFormulaArithmeticContrastLocations("CEILING.PRECISE($A1,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("CEILING.PRECISE(1E308,0.1)>0");
        AssertFormulaArithmeticContrastLocations("CEILING.PRECISE(EXP(1000),2)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFloorScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("FLOOR($A1,30)>=90", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR($A1,0)=0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR(10,3)=9", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR(4.9,0.5)=4.5", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR(-10,-3)=-9", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR(-1.5,-1)=-1", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFloorScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(FLOOR($A1,30)>=90,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(FLOOR($A1,30)>=60,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(FLOOR($A1,30))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISEVEN(FLOOR($A1,25))", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFloorScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(FLOOR($A1,30),1)>=91", "B2", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGE(FLOOR($A1,30),$A1)>110", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatFloorScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("FLOOR($A1)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR($A1,10,1)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR(\"10\",3)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR($A1,\"10\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR($A1&\"x\",10)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR(KURT($A1),10)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR($A1,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("FLOOR($A1,-10)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR(-$A1,10)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR(1E308*1E308,10)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR($A1,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR(1E308,0.1)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR(EXP(1000),2)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFloorMathScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH($A1,30)>=90", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH($A1)>=100", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH($A1,0)=0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH(10,3)=9", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH(10,-3)=9", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH(-10,3)=-12", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH(-10,-3)=-12", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH(-10,3,1)=-9", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH(-1.5,1,1)=-1", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFloorMathScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(FLOOR.MATH($A1,30)>=90,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(FLOOR.MATH($A1,30)>=60,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(FLOOR.MATH($A1))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISEVEN(FLOOR.MATH($A1,25))", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFloorMathScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(FLOOR.MATH($A1,30),1)>=91", "B2", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGE(FLOOR.MATH($A1,30),$A1)>110", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatFloorMathScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH()>0");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH($A1,10,1,0)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH(\"10\",3)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH($A1,\"10\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH($A1,10,\"1\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH($A1&\"x\",10)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH(KURT($A1),10)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH($A1,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH($A1,10,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH(1E308*1E308,10)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH($A1,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH($A1,10,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH(1E308,0.1)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR.MATH(EXP(1000),2)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFloorPreciseScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("FLOOR.PRECISE($A1,30)>=90", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.PRECISE($A1)>=100", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.PRECISE($A1,0)=0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.PRECISE(10,3)=9", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.PRECISE(10,-3)=9", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.PRECISE(-10,3)=-12", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.PRECISE(-10,-3)=-12", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.PRECISE(-1.5,1)=-2", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFloorPreciseScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(FLOOR.PRECISE($A1,30)>=90,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(FLOOR.PRECISE($A1,30)>=60,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(FLOOR.PRECISE($A1))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISEVEN(FLOOR.PRECISE($A1,25))", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFloorPreciseScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(FLOOR.PRECISE($A1,30),1)>=91", "B2", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGE(FLOOR.PRECISE($A1,30),$A1)>110", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatFloorPreciseScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("FLOOR.PRECISE()>0");
        AssertFormulaArithmeticContrastLocations("FLOOR.PRECISE($A1,10,1)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR.PRECISE(\"10\",3)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.PRECISE($A1,\"10\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FLOOR.PRECISE($A1&\"x\",10)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR.PRECISE(KURT($A1),10)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR.PRECISE($A1,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("FLOOR.PRECISE(1E308*1E308,10)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR.PRECISE($A1,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR.PRECISE(1E308,0.1)>0");
        AssertFormulaArithmeticContrastLocations("FLOOR.PRECISE(EXP(1000),2)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatQuotientScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("QUOTIENT($A1,25)>=4", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("QUOTIENT(5,2)=2", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("QUOTIENT(-5,2)=-2", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("QUOTIENT(5,-2)=-2", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("QUOTIENT(-5,-2)=2", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatQuotientScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(QUOTIENT($A1,25)>=4,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(QUOTIENT($A1,25)>=4,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(QUOTIENT($A1,25))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISEVEN(QUOTIENT($A1,25))", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatQuotientScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(QUOTIENT($A1,25),1)>=5", "B2", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGE(QUOTIENT($A1,25),$A1)>50", "B2", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatQuotientScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("QUOTIENT($A1)>0");
        AssertFormulaArithmeticContrastLocations("QUOTIENT($A1,2,1)>0");
        AssertFormulaArithmeticContrastLocations("QUOTIENT(\"5\",2)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("QUOTIENT($A1,\"2\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("QUOTIENT($A1&\"x\",2)>0");
        AssertFormulaArithmeticContrastLocations("QUOTIENT(KURT($A1),2)>0");
        AssertFormulaArithmeticContrastLocations("QUOTIENT($A1,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("QUOTIENT($A1,0)>0");
        AssertFormulaArithmeticContrastLocations("QUOTIENT(1E308*1E308,2)>0");
        AssertFormulaArithmeticContrastLocations("QUOTIENT($A1,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("QUOTIENT(1E308,1E-308)>0");
        AssertFormulaArithmeticContrastLocations("QUOTIENT(EXP(1000),2)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCombinScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("COMBIN($A1/25,2)>=6", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("COMBIN(5,2)=10", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COMBIN(6,4)=15", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COMBIN(0,0)=1", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COMBIN(5.9,2.9)=10", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COMBIN(5.9,0.9)=1", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COMBIN(5.9,5.9)=1", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCombinScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(COMBIN($A1/25,2)>=6,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(COMBIN($A1/25,2)>=6,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(COMBIN($A1/25,2))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISEVEN(COMBIN($A1/25,2))", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCombinScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(COMBIN($A1/25,2),1)>=7", "B2", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGE(COMBIN($A1/25,2),$A1)>50", "B2", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatCombinScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("COMBIN()>0");
        AssertFormulaArithmeticContrastLocations("COMBIN($A1)>0");
        AssertFormulaArithmeticContrastLocations("COMBIN($A1,2,1)>0");
        AssertFormulaArithmeticContrastLocations("COMBIN(\"5\",2)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COMBIN($A1,\"2\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COMBIN($A1&\"x\",2)>0");
        AssertFormulaArithmeticContrastLocations("COMBIN(KURT($A1),2)>0");
        AssertFormulaArithmeticContrastLocations("COMBIN($A1,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("COMBIN(-1,0)>0");
        AssertFormulaArithmeticContrastLocations("COMBIN(-0.2,0)>0");
        AssertFormulaArithmeticContrastLocations("COMBIN(5,-1)>0");
        AssertFormulaArithmeticContrastLocations("COMBIN(5,-0.2)>0");
        AssertFormulaArithmeticContrastLocations("COMBIN(2,3)>0");
        AssertFormulaArithmeticContrastLocations("COMBIN($A1/0,2)>0");
        AssertFormulaArithmeticContrastLocations("COMBIN(1E308*1E308,2)>0");
        AssertFormulaArithmeticContrastLocations("COMBIN($A1,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("COMBIN(EXP(1000),2)>0");
        AssertFormulaArithmeticContrastLocations("COMBIN($A1,EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("COMBIN(1E308,2)>0");
        AssertFormulaArithmeticContrastLocations("COMBIN(2000,1000)>0");
        AssertFormulaArithmeticContrastLocations("COMBIN(100000,50000)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCombinaScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("COMBINA($A1/25,2)>=10", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("COMBINA(4,3)=20", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COMBINA(10,3)=220", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COMBINA(1030,1)=1030", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COMBINA(1030,0)=1", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COMBINA(4.9,3.1)=20", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COMBINA(0,0)=1", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCombinaScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(COMBINA($A1/25,2)>=10,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(COMBINA($A1/25,2)>=10,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(COMBINA($A1/25,2))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISODD(COMBINA($A1/25,2))", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCombinaScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(COMBINA($A1/25,2),1)>=11", "B2", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGE(COMBINA($A1/25,2),$A1)>50", "B2", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatCombinaScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("COMBINA()>0");
        AssertFormulaArithmeticContrastLocations("COMBINA($A1)>0");
        AssertFormulaArithmeticContrastLocations("COMBINA($A1,2,1)>0");
        AssertFormulaArithmeticContrastLocations("COMBINA(\"5\",2)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COMBINA($A1,\"2\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COMBINA($A1&\"x\",2)>0");
        AssertFormulaArithmeticContrastLocations("COMBINA(KURT($A1),2)>0");
        AssertFormulaArithmeticContrastLocations("COMBINA($A1,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("COMBINA(-1,0)>0");
        AssertFormulaArithmeticContrastLocations("COMBINA(-0.2,0)>0");
        AssertFormulaArithmeticContrastLocations("COMBINA(5,-1)>0");
        AssertFormulaArithmeticContrastLocations("COMBINA(5,-0.2)>0");
        AssertFormulaArithmeticContrastLocations("COMBINA(0,1)>0");
        AssertFormulaArithmeticContrastLocations("COMBINA($A1/0,2)>0");
        AssertFormulaArithmeticContrastLocations("COMBINA(1E308*1E308,2)>0");
        AssertFormulaArithmeticContrastLocations("COMBINA($A1,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("COMBINA(EXP(1000),2)>0");
        AssertFormulaArithmeticContrastLocations("COMBINA($A1,EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("COMBINA(1E308,2)>0");
        AssertFormulaArithmeticContrastLocations("COMBINA(1030,2)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COMBINA(516,514)>0");
        AssertFormulaArithmeticContrastLocations("COMBINA(2000,1000)>0");
        AssertFormulaArithmeticContrastLocations("COMBINA(100000,50000)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatPermutScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("PERMUT($A1/25,2)>=12", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("PERMUT(5,2)=20", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("PERMUT(6,4)=360", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("PERMUT(0,0)=1", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("PERMUT(5.9,2.9)=20", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("PERMUT(5.9,0.9)=1", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("PERMUT(5.9,5.9)=120", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatPermutScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(PERMUT($A1/25,2)>=12,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(PERMUT($A1/25,2)>=12,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(PERMUT($A1/25,2))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISODD(PERMUT($A1/25,1))", "B1", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatPermutScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(PERMUT($A1/25,2),1)>=13", "B2", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGE(PERMUT($A1/25,2),$A1)>50", "B2", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatPermutScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("PERMUT()>0");
        AssertFormulaArithmeticContrastLocations("PERMUT($A1)>0");
        AssertFormulaArithmeticContrastLocations("PERMUT($A1,2,1)>0");
        AssertFormulaArithmeticContrastLocations("PERMUT(\"5\",2)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("PERMUT($A1,\"2\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("PERMUT($A1&\"x\",2)>0");
        AssertFormulaArithmeticContrastLocations("PERMUT(KURT($A1),2)>0");
        AssertFormulaArithmeticContrastLocations("PERMUT($A1,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("PERMUT(-1,0)>0");
        AssertFormulaArithmeticContrastLocations("PERMUT(-0.2,0)>0");
        AssertFormulaArithmeticContrastLocations("PERMUT(5,-1)>0");
        AssertFormulaArithmeticContrastLocations("PERMUT(5,-0.2)>0");
        AssertFormulaArithmeticContrastLocations("PERMUT(2,3)>0");
        AssertFormulaArithmeticContrastLocations("PERMUT($A1/0,2)>0");
        AssertFormulaArithmeticContrastLocations("PERMUT(1E308*1E308,2)>0");
        AssertFormulaArithmeticContrastLocations("PERMUT($A1,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("PERMUT(EXP(1000),2)>0");
        AssertFormulaArithmeticContrastLocations("PERMUT($A1,EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("PERMUT(1E308,2)>0");
        AssertFormulaArithmeticContrastLocations("PERMUT(2000,1000)>0");
        AssertFormulaArithmeticContrastLocations("PERMUT(100000,50000)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatPermutationAScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA($A1/25,2)>=16", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA(3,2)=9", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA(2,2)=4", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA(3.9,2.1)=9", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA(5.9,0.9)=1", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA(0,0)=1", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatPermutationAScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(PERMUTATIONA($A1/25,2)>=16,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(PERMUTATIONA($A1/25,2)>=16,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(PERMUTATIONA($A1/25,2))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISODD(PERMUTATIONA($A1/25,2))", "B1", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatPermutationAScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(PERMUTATIONA($A1/25,2),1)>=17", "B2", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGE(PERMUTATIONA($A1/25,2),$A1)>50", "B2", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatPermutationAScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA()>0");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA($A1)>0");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA($A1,2,1)>0");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA(\"5\",2)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA($A1,\"2\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA($A1&\"x\",2)>0");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA(KURT($A1),2)>0");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA($A1,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA(-1,0)>0");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA(-0.2,0)>0");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA(5,-1)>0");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA(5,-0.2)>0");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA(0,1)>0");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA($A1/0,2)>0");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA(1E308*1E308,2)>0");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA($A1,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA(EXP(1000),2)>0");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA($A1,EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA(1E308,2)>0");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA(2147483648,1)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA(2,1024)>0");
        AssertFormulaArithmeticContrastLocations("PERMUTATIONA(100000,50000)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatMultinomialScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL($A1,1)>=101", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL(2,3)=10", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL(2.9,3.1)=10", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL(0,0)=1", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL(1,2,3)=60", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL($A1/25,2)>=20", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatMultinomialScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(MULTINOMIAL($A1,1)>=101,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(MULTINOMIAL($A1,1)>=101,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(MULTINOMIAL($A1,1))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISEVEN(MULTINOMIAL($A1,1))", "B1", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatMultinomialScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(MULTINOMIAL(2,3),1)=11", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUM(MULTINOMIAL($A1,1),1)>=102", "B2", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGE(MULTINOMIAL($A1,1),$A1)>100", "B2", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatMultinomialScalarFunctionOperandCoercionAndErrorSemantics()
    {
        var tooManyArguments = $"MULTINOMIAL({string.Join(",", Enumerable.Repeat("1", 256))})>0";

        AssertFormulaArithmeticContrastLocations("MULTINOMIAL()>0");
        AssertFormulaArithmeticContrastLocations(tooManyArguments);
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL(\"5\",2)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL($A1,\"2\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL($A1&\"x\",2)>0");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL(KURT($A1),2)>0");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL($A1,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL(-1,1)>0");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL(-0.2,1)>0");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL(1,-1)>0");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL(1,-0.2)>0");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL($A1/0,1)>0");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL(1E308*1E308,1)>0");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL($A1,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL(EXP(1000),1)>0");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL($A1,EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL(1E308,1)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL(2000,1000)>0");
        AssertFormulaArithmeticContrastLocations("MULTINOMIAL(100000,50000)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatGcdScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("GCD($A1,50)=25", "B1", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("GCD($A1,50)>=50", "B2");
        AssertFormulaArithmeticContrastLocations("GCD(8,12)=4", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("GCD(5.9,2.1)=1", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("GCD(0,0)=0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("GCD(18,24,30)=6", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("GCD($A1,50,25)=25", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("GCD($A1/10,2.9)=2", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatGcdScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(GCD($A1,50)>=50,TRUE,FALSE)", "B2");
        AssertFormulaArithmeticContrastLocations("AND(GCD($A1,50)=25,$C1=\"Open\")", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(GCD($A1,50))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISEVEN(GCD($A1,50))", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatGcdScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(GCD($A1,50),1)>26", "B2");
        AssertFormulaAggregateContrastLocations("AVERAGE(GCD($A1,50),$A1)>60", "B2", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatGcdScalarFunctionOperandCoercionAndErrorSemantics()
    {
        var tooManyArguments = $"GCD({string.Join(",", Enumerable.Repeat("1", 256))})>0";

        AssertFormulaArithmeticContrastLocations("GCD()>0");
        AssertFormulaArithmeticContrastLocations(tooManyArguments);
        AssertFormulaArithmeticContrastLocations("GCD(\"5\",2)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("GCD($A1,\"2\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("GCD($A1&\"x\",2)>0");
        AssertFormulaArithmeticContrastLocations("GCD(KURT($A1),2)>0");
        AssertFormulaArithmeticContrastLocations("GCD($A1,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("GCD(-1,1)>0");
        AssertFormulaArithmeticContrastLocations("GCD(-0.2,1)>0");
        AssertFormulaArithmeticContrastLocations("GCD(1,-1)>0");
        AssertFormulaArithmeticContrastLocations("GCD(1,-0.2)>0");
        AssertFormulaArithmeticContrastLocations("GCD($A1/0,1)>0");
        AssertFormulaArithmeticContrastLocations("GCD(1E308*1E308,1)>0");
        AssertFormulaArithmeticContrastLocations("GCD($A1,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("GCD(EXP(1000),1)>0");
        AssertFormulaArithmeticContrastLocations("GCD($A1,EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("GCD(1E20,1)>0");
        AssertFormulaArithmeticContrastLocations("GCD($A1,1E20)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatLcmScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("LCM($A1,50)=150", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("LCM($A1,50)>=200", "B4");
        AssertFormulaArithmeticContrastLocations("LCM($A1)=75", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("LCM(4,6)=12", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("LCM(5.9,2.1)=10", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("LCM(0,5)=0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("LCM(3,4,5)=60", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("LCM($A1/10,2.9)=10", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatLcmScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(LCM($A1,50)>=200,TRUE,FALSE)", "B4");
        AssertFormulaArithmeticContrastLocations("AND(LCM($A1,50)=150,$C1=\"Open\")", "B3");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(LCM($A1,50))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISEVEN(LCM($A1,50))", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatLcmScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(LCM($A1,50),1)>200", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGE(LCM($A1,50),$A1)>100", "B1", "B3", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatLcmScalarFunctionOperandCoercionAndErrorSemantics()
    {
        var tooManyArguments = $"LCM({string.Join(",", Enumerable.Repeat("1", 256))})>0";

        AssertFormulaArithmeticContrastLocations("LCM()>0");
        AssertFormulaArithmeticContrastLocations(tooManyArguments);
        AssertFormulaArithmeticContrastLocations("LCM(\"5\",2)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("LCM($A1,\"2\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("LCM($A1&\"x\",2)>0");
        AssertFormulaArithmeticContrastLocations("LCM(KURT($A1),2)>0");
        AssertFormulaArithmeticContrastLocations("LCM($A1,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("LCM(-1,1)>0");
        AssertFormulaArithmeticContrastLocations("LCM(-0.2,1)>0");
        AssertFormulaArithmeticContrastLocations("LCM(1,-1)>0");
        AssertFormulaArithmeticContrastLocations("LCM(1,-0.2)>0");
        AssertFormulaArithmeticContrastLocations("LCM($A1/0,1)>0");
        AssertFormulaArithmeticContrastLocations("LCM(1E308*1E308,1)>0");
        AssertFormulaArithmeticContrastLocations("LCM($A1,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("LCM(EXP(1000),1)>0");
        AssertFormulaArithmeticContrastLocations("LCM($A1,EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("LCM(1E20,1)>0");
        AssertFormulaArithmeticContrastLocations("LCM($A1,1E20)>0");
        AssertFormulaArithmeticContrastLocations("LCM(3037000500,3037000501)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatOddScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("ODD($A1/40)>3", "B4");
        AssertFormulaArithmeticContrastLocations("ODD(-$A1/40)<-3", "B4");
        AssertFormulaArithmeticContrastLocations("ODD(1.2)=3", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ODD(2)=3", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ODD(3)=3", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ODD(-1.2)=-3", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ODD(-2)=-3", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ODD(-3)=-3", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ODD(0)=1", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatOddScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(ODD($A1/40)>3,TRUE,FALSE)", "B4");
        AssertFormulaArithmeticContrastLocations("AND(ODD($A1/50)>=3,$C1=\"Open\")", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(ODD($A1/50))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISODD(ODD($A1/50))", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatOddScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(ODD($A1/40),1)>4", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatOddScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("ODD()>0");
        AssertFormulaArithmeticContrastLocations("ODD($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("ODD(\"5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ODD($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("ODD(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("ODD(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ODD(EXP(1000))>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAcothScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("ACOTH($A1/50)>0.54", "B1", "B2", "B3");
        AssertFormulaArithmeticContrastLocations("ACOTH(-$A1/50)<-0.54", "B1", "B2", "B3");
        AssertFormulaArithmeticContrastLocations("ACOTH($A1/100)>1", "B4");
        AssertFormulaArithmeticContrastLocations("ACOTH(2)>0.54", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ACOTH(-2)<-0.54", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ROUND(ACOTH(1/TANH(1)),2)=1", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAcothScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(ACOTH($A1/50)>0.54,TRUE,FALSE)", "B1", "B2", "B3");
        AssertFormulaArithmeticContrastLocations("AND(ACOTH($A1/50)>0.54,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(ACOTH($A1/100))", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAcothScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(ACOTH($A1/50),1)>1.54", "B1", "B2", "B3");
        AssertFormulaAggregateContrastLocations("AVERAGE(ACOTH($A1/50),$A1)>50", "B2", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatAcothScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("ACOTH()>0");
        AssertFormulaArithmeticContrastLocations("ACOTH($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("ACOTH(\"2\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ACOTH($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("ACOTH(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("ACOTH(1)>0");
        AssertFormulaArithmeticContrastLocations("ACOTH(-1)>0");
        AssertFormulaArithmeticContrastLocations("ACOTH(0)>0");
        AssertFormulaArithmeticContrastLocations("ACOTH(0.5)>0");
        AssertFormulaArithmeticContrastLocations("ACOTH(-0.5)>0");
        AssertFormulaArithmeticContrastLocations("ACOTH(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ACOTH(EXP(1000))>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCothScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("COTH($A1/100)>1.5", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("COTH(-$A1/100)<-1.5", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("ABS(COTH(1)-1.3130352854993312)<0.000000000001", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ROUND(ACOTH(COTH(1)),2)=1", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCothScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(COTH($A1/100)>1.5,TRUE,FALSE)", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("AND(COTH($A1/100)>1.5,$C1=\"Closed\")", "B1");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(COTH($A1/100))", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCothScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(COTH($A1/100),1)>2.5", "B1", "B3");
        AssertFormulaAggregateContrastLocations("AVERAGE(COTH($A1/100),$A1)>50", "B2", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatCothScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("COTH()>0");
        AssertFormulaArithmeticContrastLocations("COTH($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("COTH(\"1\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COTH($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("COTH(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("COTH(0)>0");
        AssertFormulaArithmeticContrastLocations("COTH($A1-$A1)>0");
        AssertFormulaArithmeticContrastLocations("COTH(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("COTH(EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("COTH(5E-324)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCschScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("CSCH($A1/100)>1", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("CSCH(-$A1/100)<-1", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("ABS(CSCH(1)-0.8509181282393216)<0.000000000001", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ROUND(ASINH(1/CSCH(1)),2)=1", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCschScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(CSCH($A1/100)>1,TRUE,FALSE)", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("AND(CSCH($A1/100)>1,$C1=\"Closed\")", "B1");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(CSCH($A1/100))", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCschScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(CSCH($A1/100),1)>2", "B1", "B3");
        AssertFormulaAggregateContrastLocations("AVERAGE(CSCH($A1/100),$A1)>50", "B2", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatCschScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("CSCH()>0");
        AssertFormulaArithmeticContrastLocations("CSCH($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("CSCH(\"1\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CSCH($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("CSCH(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("CSCH(0)>0");
        AssertFormulaArithmeticContrastLocations("CSCH($A1-$A1)>0");
        AssertFormulaArithmeticContrastLocations("CSCH(1E308)>0");
        AssertFormulaArithmeticContrastLocations("CSCH(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("CSCH(EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("CSCH(5E-324)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatSechScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("SECH($A1/100)>0.6", "B1", "B2", "B3");
        AssertFormulaArithmeticContrastLocations("ABS(SECH(1)-0.6480542736638854)<0.000000000001", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ABS(SECH(0)-1)<0.000000000001", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ROUND(ACOSH(1/SECH(1)),2)=1", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatSechScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(SECH($A1/100)>0.6,TRUE,FALSE)", "B1", "B2", "B3");
        AssertFormulaArithmeticContrastLocations("AND(SECH($A1/100)>0.6,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(SECH($A1/100))", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatSechScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(SECH($A1/100),1)>1.6", "B1", "B2", "B3");
        AssertFormulaAggregateContrastLocations("AVERAGE(SECH($A1/100),$A1)>50", "B2", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatSechScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("SECH()>0");
        AssertFormulaArithmeticContrastLocations("SECH($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("SECH(\"1\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("SECH($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("SECH(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("SECH(1E308)>0");
        AssertFormulaArithmeticContrastLocations("SECH(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("SECH(EXP(1000))>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAcotScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("ACOT($A1/100)>0.8", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("ACOT(-$A1/100)>2.3", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ABS(ACOT(1)-PI()/4)<0.000000000001", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ACOT(0)=PI()/2", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ABS(ACOT(-1)-3*PI()/4)<0.000000000001", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAcotScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(ACOT($A1/100)>0.8,TRUE,FALSE)", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("AND(ACOT($A1/100)>0.8,$C1=\"Closed\")", "B1");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(ACOT($A1/100))", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAcotScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(ACOT($A1/100),1)>1.8", "B1", "B3");
        AssertFormulaAggregateContrastLocations("AVERAGE(ACOT($A1/100),$A1)>50", "B2", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatAcotScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("ACOT()>0");
        AssertFormulaArithmeticContrastLocations("ACOT($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("ACOT(\"1\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ACOT($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("ACOT(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("ACOT(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ACOT(EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("ACOT(5E-324)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ACOT(-5E-324)>0", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCotScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("COT($A1/100)>1", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("COT(-$A1/100)<-1", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("ABS(COT(1)-0.6420926159343306)<0.000000000001", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ABS(COT(PI()/4)-1)<0.000000000001", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCotScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(COT($A1/100)>1,TRUE,FALSE)", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("AND(COT($A1/100)>1,$C1=\"Closed\")", "B1");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(COT($A1/100))", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCotScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(COT($A1/100),1)>2", "B1", "B3");
        AssertFormulaAggregateContrastLocations("AVERAGE(COT($A1/100),$A1)>50", "B2", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatCotScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("COT()>0");
        AssertFormulaArithmeticContrastLocations("COT($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("COT(\"1\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COT($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("COT(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("COT(0)>0");
        AssertFormulaArithmeticContrastLocations("COT(PI()-PI())>0");
        AssertFormulaArithmeticContrastLocations("COT(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("COT(EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("COT(5E-324)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCscScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("CSC($A1/100)>1.2", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("CSC(-$A1/100)<-1.2", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("ABS(CSC(1)-1.1883951057781212)<0.000000000001", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ABS(CSC(PI()/2)-1)<0.000000000001", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCscScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(CSC($A1/100)>1.2,TRUE,FALSE)", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("AND(CSC($A1/100)>1.2,$C1=\"Closed\")", "B1");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(CSC($A1/100))", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatCscScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(CSC($A1/100),1)>2.2", "B1", "B3");
        AssertFormulaAggregateContrastLocations("AVERAGE(CSC($A1/100),$A1)>50", "B2", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatCscScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("CSC()>0");
        AssertFormulaArithmeticContrastLocations("CSC($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("CSC(\"1\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("CSC($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("CSC(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("CSC(0)>0");
        AssertFormulaArithmeticContrastLocations("CSC(PI()-PI())>0");
        AssertFormulaArithmeticContrastLocations("CSC(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("CSC(EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("CSC(5E-324)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatSecScalarFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("SEC($A1/100)>1.5", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("SEC(RADIANS($A1))<0", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ABS(SEC(1)-1.8508157176809255)<0.000000000001", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ABS(SEC(0)-1)<0.000000000001", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatSecScalarFunctionWrappersAndPredicates()
    {
        AssertFormulaArithmeticContrastLocations("IF(SEC($A1/100)>1.5,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(SEC($A1/100)>1.5,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(SEC($A1/100))", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatSecScalarFunctionAggregateArguments()
    {
        AssertFormulaAggregateContrastLocations("SUM(SEC($A1/100),1)>2.5", "B2", "B4");
        AssertFormulaAggregateContrastLocations("AVERAGE(SEC($A1/100),$A1)>50", "B2", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatSecScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("SEC()>0");
        AssertFormulaArithmeticContrastLocations("SEC($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("SEC(\"1\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("SEC($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("SEC(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("SEC(PI()/2)>0");
        AssertFormulaArithmeticContrastLocations("SEC(RADIANS(90))>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("SEC(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("SEC(EXP(1000))>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatScalarFunctionWrappers()
    {
        AssertFormulaArithmeticContrastLocations("IF(ABS($A1-100)>=25,TRUE,FALSE)", "B1", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("AND(ROUND($A1/3,0)>=33,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(ROUND($A1/3,0))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(EVEN($A1/50)>2,TRUE,FALSE)", "B4");
        AssertFormulaArithmeticContrastLocations("AND(EVEN($A1/50)>=2,$C1=\"Open\")", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(EVEN($A1/50))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(ROUNDUP($A1/100,1)>=1,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(ROUNDUP($A1/100,1)>=1,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(ROUNDUP($A1/100,1))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(ROUNDDOWN($A1/100,1)>=1,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(ROUNDDOWN($A1/100,1)>=1,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(ROUNDDOWN($A1/100,1))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(MROUND($A1,30)>=120,TRUE,FALSE)", "B4");
        AssertFormulaArithmeticContrastLocations("AND(MROUND($A1,30)>=90,$C1=\"Closed\")", "B1", "B2");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(MROUND($A1,30))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(TRUNC($A1/100,1)>=1,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(TRUNC($A1/100,1)>=1,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(TRUNC($A1/100))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(FACT($A1/25)>20,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(FACT($A1/25)>20,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(FACT($A1/25))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(FACTDOUBLE($A1/25)>7,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(FACTDOUBLE($A1/25)>7,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(FACTDOUBLE($A1/25))", "B1", "B2", "B3", "B4");
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
        AssertFormulaArithmeticContrastLocations("IF(ASINH($A1/100)>0.88,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(ASINH($A1/100)>0.88,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(ASINH($A1/100))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(ACOSH($A1/50)>1.2,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(ACOSH($A1/50)>1.2,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(ACOSH($A1/100))", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("IF(COSH($A1/100)>1.5,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(COSH($A1/100)>1.5,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(COSH($A1/100))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(TANH($A1/100)>0.75,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(TANH($A1/100)>0.75,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(TANH($A1/100))", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(ATANH($A1/200)>0.54,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("AND(ATANH($A1/200)>0.54,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(ATANH($A1/200))", "B1", "B2", "B3", "B4");
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
        AssertFormulaAggregateContrastLocations("SUM(EVEN($A1/50),1)>3", "B4");
        AssertFormulaAggregateContrastLocations("SUM(ROUNDUP($A1/100,1),1)>=2", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(ROUNDDOWN($A1/100,1),1)>=2", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(MROUND($A1,30),1)>=121", "B4");
        AssertFormulaAggregateContrastLocations("SUM(TRUNC($A1/100,1),1)>=2", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(FACT($A1/25),1)>20", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(FACTDOUBLE($A1/25),1)>8", "B2", "B4");
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
        AssertFormulaAggregateContrastLocations("SUM(ASINH($A1/100),1)>1.88", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(ACOSH($A1/50),1)>2.2", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(COSH($A1/100),1)>2.5", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(TANH($A1/100),1)>1.75", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(ATANH($A1/200),1)>1.54", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(ASIN(SIN(RADIANS($A1))),1)>2", "B1", "B2", "B3");
        AssertFormulaAggregateContrastLocations("SUM(ACOS(COS(RADIANS($A1))),1)>2.5", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(ATAN($A1/100),1)>1.7", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(ATAN2($A1,100),1)>1.8", "B1", "B3");
        AssertFormulaAggregateContrastLocations("SUM(COS(RADIANS($A1)),1)>1.2", "B1", "B3");
        AssertFormulaAggregateContrastLocations("SUM(TAN(RADIANS($A1)),1)>4", "B1", "B3");
        AssertFormulaAggregateContrastLocations("SUM(PI(),$A1)>103", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatEngineeringScalarFunctions()
    {
        AssertFormulaArithmeticContrastLocations("DELTA($A1,75)", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("DELTA($A1-75)", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("GESTEP($A1)", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("GESTEP($A1,100)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("BITAND($A1,8)>0", "B1", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("BITOR($A1,4)=79", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("BITXOR($A1,5)=120", "B4");
        AssertFormulaArithmeticContrastLocations("BITLSHIFT($A1,1)>200", "B4");
        AssertFormulaArithmeticContrastLocations("BITRSHIFT($A1,2)=18", "B1", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatEngineeringScalarFunctionWrappers()
    {
        AssertFormulaArithmeticContrastLocations("AND(GESTEP($A1,100),$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("IF(DELTA($A1,75),TRUE,FALSE)", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("AND(BITAND($A1,8),$C1=\"Open\")", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("IF(BITXOR($A1,5)=120,TRUE,FALSE)", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(BITLSHIFT($A1,1))", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatMatrixArrayFunctionComparisons()
    {
        AssertFormulaMatrixArrayFunctionContrastLocations("MDETERM($C$1:$D$2)=-2", FormulaMatrixArrayAllLocations);
        AssertFormulaMatrixArrayFunctionContrastLocations("MMULT($I$1:$J$1,$I$2:$I$3)=11", FormulaMatrixArrayAllLocations);
        AssertFormulaMatrixArrayFunctionContrastLocations("MINVERSE($K$1:$K$1)=0.5", FormulaMatrixArrayAllLocations);
        AssertFormulaMatrixArrayFunctionContrastLocations("TRANSPOSE($K$1:$K$1)=2", FormulaMatrixArrayAllLocations);
        AssertFormulaMatrixArrayFunctionContrastLocations("INDEX(MUNIT(2),2,2)=1", FormulaMatrixArrayAllLocations);
        AssertFormulaMatrixArrayFunctionContrastLocations("INDEX(MMULT(MUNIT(2),$C$1:$D$2),2,2)=4", FormulaMatrixArrayAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatMatrixArrayLiteralsWrappersAndErrorPredicates()
    {
        AssertFormulaMatrixArrayFunctionContrastLocations("MMULT({1,2},{3;4})=11", FormulaMatrixArrayAllLocations);
        AssertFormulaMatrixArrayFunctionContrastLocations("MDETERM({1,2;3,4})=-2", FormulaMatrixArrayAllLocations);
        AssertFormulaMatrixArrayFunctionContrastLocations("SUM(MUNIT(3))=3", FormulaMatrixArrayAllLocations);
        AssertFormulaMatrixArrayFunctionContrastLocations("AND(MDETERM($C$1:$D$2)<0,TRANSPOSE({2})=2)", FormulaMatrixArrayAllLocations);
        AssertFormulaMatrixArrayFunctionContrastLocations("IF(MMULT($I$1:$J$1,$I$2:$I$3)=11,TRUE,FALSE)", FormulaMatrixArrayAllLocations);
        AssertFormulaMatrixArrayFunctionContrastLocations("IFERROR(MMULT($C$1:$D$2,$I$1:$J$1),TRUE)", FormulaMatrixArrayAllLocations);
        AssertFormulaMatrixArrayFunctionContrastLocations("ISERROR(MMULT($C$1:$D$2,$I$1:$J$1))", FormulaMatrixArrayAllLocations);
        AssertFormulaMatrixArrayFunctionContrastLocations("ISNA(MMULT($C$1:$D$2,NA()))", FormulaMatrixArrayAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatMatrixArrayDimensionAndSingularErrors()
    {
        AssertFormulaMatrixArrayFunctionContrastLocations("ISERROR(MMULT($C$1:$D$2,$I$1:$J$1))", FormulaMatrixArrayAllLocations);
        AssertFormulaMatrixArrayFunctionContrastLocations("ISERROR(MDETERM($P$1:$R$2))", FormulaMatrixArrayAllLocations);
        AssertFormulaMatrixArrayFunctionContrastLocations("ISERROR(MINVERSE($M$1:$N$2))", FormulaMatrixArrayAllLocations);
        AssertFormulaMatrixArrayFunctionContrastLocations("ISERR(MINVERSE($M$1:$N$2))", FormulaMatrixArrayAllLocations);
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatMatrixArrayArityAndShapeSemantics()
    {
        AssertFormulaMatrixArrayFunctionContrastLocations("MMULT($I$1:$J$1)>0");
        AssertFormulaMatrixArrayFunctionContrastLocations("MDETERM()>0");
        AssertFormulaMatrixArrayFunctionContrastLocations("MINVERSE($C$1:$D$2,1)>0");
        AssertFormulaMatrixArrayFunctionContrastLocations("TRANSPOSE()>0");
        AssertFormulaMatrixArrayFunctionContrastLocations("MMULT($C$1:$D$2,$F$1:$G$2)>0");
        AssertFormulaMatrixArrayFunctionContrastLocations("MINVERSE($C$1:$D$2)>0");
        AssertFormulaMatrixArrayFunctionContrastLocations("TRANSPOSE($C$1:$D$2)>0");
        AssertFormulaMatrixArrayFunctionContrastLocations("MDETERM({1,\"x\";3,4})>0");
        AssertFormulaMatrixArrayFunctionContrastLocations("MUNIT()>0");
        AssertFormulaMatrixArrayFunctionContrastLocations("MUNIT(0)>0");
        AssertFormulaMatrixArrayFunctionContrastLocations("MUNIT(\"x\")>0");
        AssertFormulaMatrixArrayFunctionContrastLocations("SUM(MUNIT(101))>0", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDynamicArrayShaperScalarComparisons()
    {
        AssertFormulaDynamicArrayFunctionContrastLocations(
            "INDEX(SEQUENCE(3,2,10,5),2,1)=20",
            FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations(
            "INDEX(TAKE($P$1:$R$2,1),1,2)=2",
            FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations(
            "INDEX(DROP($P$1:$R$2,1),1,2)=5",
            FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations(
            "INDEX(EXPAND($K$1:$K$1,2,2,9),2,2)=9",
            FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations(
            "INDEX(CHOOSECOLS($P$1:$R$2,3,1),2,1)=6",
            FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations(
            "INDEX(CHOOSEROWS($P$1:$R$2,2),1,3)=6",
            FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations(
            "INDEX(TOCOL($P$1:$R$2),4,1)=4",
            FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations(
            "INDEX(TOROW($P$1:$R$2,0,TRUE),1,4)=5",
            FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations(
            "INDEX(WRAPROWS(TOROW($P$1:$R$2),4),2,2)=6",
            FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations(
            "INDEX(WRAPCOLS(TOCOL($P$1:$R$2),4),2,2)=6",
            FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations(
            "INDEX(HSTACK($C$1:$D$1,$F$1:$F$1),1,3)=5",
            FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations(
            "INDEX(VSTACK($C$1:$D$1,$F$1:$G$1),2,2)=6",
            FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations(
            "INDEX(FILTER($P$1:$R$2,{TRUE;FALSE}),1,3)=3",
            FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations(
            "INDEX(SORT({2,1;4,3},2,-1),1,1)=4",
            FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations(
            "INDEX(SORTBY($P$1:$R$2,{2;1}),1,1)=4",
            FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations(
            "INDEX(UNIQUE({1;2;1}),2,1)=2",
            FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations(
            "INDEX(TRIMRANGE($L$1:$O$3),2,2)=4",
            FormulaMatrixArrayAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDynamicArrayAggregateOperands()
    {
        AssertFormulaDynamicArrayFunctionContrastLocations("SUM(SEQUENCE(3))=6", FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations("SUM(TAKE($P$1:$R$2,1))=6", FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations("SUM(DROP($P$1:$R$2,1))=15", FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations(
            "SUM(TOCOL(UNIQUE(VSTACK({1;2;1},{3}))))=6",
            FormulaMatrixArrayAllLocations);
        AssertFormulaDynamicArrayFunctionContrastLocations(
            "SUM(FILTER($P$1:$R$2,{TRUE;FALSE}))=6",
            FormulaMatrixArrayAllLocations);
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatDynamicArrayShapeAndDeferredFunctionSemantics()
    {
        AssertFormulaDynamicArrayFunctionContrastLocations("SEQUENCE(2)>0");
        AssertFormulaDynamicArrayFunctionContrastLocations("SUM(SEQUENCE(10001))>0", "B1", "B2", "B3", "B4");
        AssertFormulaDynamicArrayFunctionContrastLocations("FILTER($P$1:$R$2,$P$1:$R$2>2)>0");
        AssertFormulaDynamicArrayFunctionContrastLocations("RANDARRAY(1)>0", "B1", "B2", "B3", "B4");
        AssertFormulaDynamicArrayFunctionContrastLocations("BYROW($P$1:$R$2,1)>0");
        AssertFormulaDynamicArrayFunctionContrastLocations("MAP($P$1:$Q$1,1)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatNormalDistributionScalarFunctions()
    {
        AssertFormulaNormalDistributionFunctionContrastLocations("AND($G1,NORMDIST($A1,$C1,$D1,TRUE)>0.8)", "B2", "B4");
        AssertFormulaNormalDistributionFunctionContrastLocations("AND($G1,NORM.DIST($A1,$C1,$D1,FALSE)>0.39)", "B1");
        AssertFormulaNormalDistributionFunctionContrastLocations("AND($G1,NORMINV($E1,$C1,$D1)>0.9)", "B2", "B4");
        AssertFormulaNormalDistributionFunctionContrastLocations("AND($G1,NORM.INV($E1,$C1,$D1)<0)", "B3");
        AssertFormulaNormalDistributionFunctionContrastLocations("AND($G1,NORMSDIST($A1)>0.8)", "B2", "B4");
        AssertFormulaNormalDistributionFunctionContrastLocations("AND($G1,NORM.S.DIST($A1,$F1)>0.45)", "B1");
        AssertFormulaNormalDistributionFunctionContrastLocations("AND($G1,NORMSINV($E1)>0.9)", "B2", "B4");
        AssertFormulaNormalDistributionFunctionContrastLocations("AND($G1,NORM.S.INV($E1)<0)", "B3");
        AssertFormulaNormalDistributionFunctionContrastLocations("AND($G1,PHI($A1)>0.24)", "B1", "B2", "B3");
        AssertFormulaNormalDistributionFunctionContrastLocations("AND($G1,GAUSS($A1)>0.3)", "B2", "B4");
        AssertFormulaNormalDistributionFunctionContrastLocations("AND($G1,STANDARDIZE($A1,$C1,$D1)>1.5)", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatNormalDistributionNestedScalars()
    {
        AssertFormulaNormalDistributionFunctionContrastLocations("AND($G1,NORM.DIST(ABS($A1),$C1,$D1,TRUE)>0.8)", "B2", "B3", "B4");
        AssertFormulaNormalDistributionFunctionContrastLocations("AND($G1,SUM(NORMSINV($E1),STANDARDIZE($A1,$C1,$D1))>1.9)", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatConfidenceFunctions()
    {
        AssertFormulaNormalDistributionFunctionContrastLocations("AND($G1,CONFIDENCE(0.05,$D1,25)>0.3)", "B1", "B2", "B3");
        AssertFormulaNormalDistributionFunctionContrastLocations("AND($G1,CONFIDENCE.NORM(0.05,$D1,25)<0.4)", "B1", "B2", "B3", "B4");
        AssertFormulaNormalDistributionFunctionContrastLocations("AND($G1,CONFIDENCE.T(0.05,$D1,25)>0.39)", "B1", "B2", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatNormalDistributionErrorPredicates()
    {
        AssertFormulaNormalDistributionFunctionContrastLocations("ISERROR(NORM.DIST($A1,$C1,$D1,TRUE))", "B5", "B8", "B9");
        AssertFormulaNormalDistributionFunctionContrastLocations("ISNA(NORM.DIST($A1,$C1,$D1,TRUE))", "B8");
        AssertFormulaNormalDistributionFunctionContrastLocations("ISERR(NORM.DIST($A1,$C1,$D1,TRUE))", "B5", "B9");
        AssertFormulaNormalDistributionFunctionContrastLocations("ISERROR(NORM.INV($E1,$C1,$D1))", "B5", "B6", "B7", "B8", "B9");
        AssertFormulaNormalDistributionFunctionContrastLocations("ISNA(NORM.INV($E1,$C1,$D1))", "B8");
        AssertFormulaNormalDistributionFunctionContrastLocations("ISERROR(NORM.S.DIST($A1,$F1))", "B8", "B9");
        AssertFormulaNormalDistributionFunctionContrastLocations("ISERROR(NORM.S.INV($E1))", "B6", "B7", "B8", "B9");
        AssertFormulaNormalDistributionFunctionContrastLocations("ISERROR(PHI($A1))", "B8", "B9");
        AssertFormulaNormalDistributionFunctionContrastLocations("ISERROR(GAUSS($A1))", "B8", "B9");
        AssertFormulaNormalDistributionFunctionContrastLocations("ISERROR(STANDARDIZE($A1,$C1,$D1))", "B5", "B8", "B9");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatNormalDistributionUnsupportedShapes()
    {
        AssertFormulaNormalDistributionFunctionContrastLocations("NORM.DIST($A$1:$A$2,0,1,TRUE)>0");
        AssertFormulaNormalDistributionFunctionContrastLocations("NORM.INV($E1,$C1)>0");
        AssertFormulaNormalDistributionFunctionContrastLocations("NORM.S.DIST($A1)>0");
        AssertFormulaNormalDistributionFunctionContrastLocations("NORMSDIST($A1,TRUE)>0");
        AssertFormulaNormalDistributionFunctionContrastLocations("PHI($A$1:$A$2)>0");
        AssertFormulaNormalDistributionFunctionContrastLocations("GAUSS(KURT($A1))>0");
        AssertFormulaNormalDistributionFunctionContrastLocations("STANDARDIZE($A1,$C1,$D1,1)>0");
        AssertFormulaNormalDistributionFunctionContrastLocations("NORM.DIST($A1,$C1,$D1,\"TRUE\")>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTDistributionFunctions()
    {
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,TDIST($A1,$C1,2)>0.5)", "B1", "B4");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,T.DIST($A1,$C1,TRUE)>0.9)", "B2", "B3");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,T.DIST($A1,$C1,FALSE)>0.3)", "B1", "B4");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,T.DIST.RT($A1,$C1)>0.3)", "B1", "B4");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,T.DIST.2T($A1,$C1)<0.2)", "B2", "B3");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,TINV($E1,$C1)>0.7)", "B1", "B4");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,T.INV($E1,$C1)>1.5)", "B3");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,T.INV.2T($E1,$C1)>1)", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFDistributionFunctions()
    {
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,FDIST($A1,$C1,$D1)>0.7)", "B1", "B4");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,F.DIST($A1,$C1,$D1,TRUE)>0.7)", "B2", "B3");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,F.DIST($A1,$C1,$D1,FALSE)>0.5)", "B1", "B4");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,F.DIST.RT($A1,$C1,$D1)<0.3)", "B2", "B3");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,FINV($E1,$C1,$D1)>2)", "B4");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,F.INV($E1,$C1,$D1)>2)", "B3");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,F.INV.RT($E1,$C1,$D1)<0.6)", "B2", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatChiSquareDistributionFunctions()
    {
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,CHIDIST($A1,$C1)>0.995)", "B2", "B3");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,CHISQ.DIST($A1,$C1,TRUE)<0.005)", "B2", "B3");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,CHISQ.DIST($A1,$C1,FALSE)>0.05)", "B4");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,CHISQ.DIST.RT($A1,$C1)<0.995)", "B1", "B4");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,CHIINV($E1,$C1)>10)", "B3");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,CHISQ.INV($E1,$C1)>10)", "B2", "B3");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,CHISQ.INV.RT($E1,$C1)>10)", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTFChiSquareDistributionNestedScalars()
    {
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,T.DIST(ABS($A1),$C1,TRUE)>0.9)", "B2", "B3");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,SUM(F.DIST.RT($A1,$C1,$D1),CHISQ.DIST($A1,$C1,TRUE))>0.77)", "B1", "B4");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("AND($G1,T.INV.2T($E1,ABS($C1))>1)", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTFChiSquareDistributionErrorPredicates()
    {
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("ISERROR(T.DIST.RT($A1,$C1))", "B5", "B6", "B8", "B9");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("ISNA(T.DIST.RT($A1,$C1))", "B8");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("ISERR(T.DIST.RT($A1,$C1))", "B5", "B6", "B9");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("ISERROR(T.INV($E1,$C1))", "B6", "B7", "B8", "B9", "B10");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("ISERROR(F.DIST($A1,$C1,$D1,TRUE))", "B5", "B6", "B7", "B8", "B9");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("ISERROR(F.INV($E1,$C1,$D1))", "B6", "B7", "B8", "B9", "B10");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("ISERROR(CHISQ.DIST($A1,$C1,TRUE))", "B5", "B6", "B8", "B9");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("ISERROR(CHISQ.INV($E1,$C1))", "B6", "B8", "B9", "B10");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("ISERROR(CHISQ.INV.RT($E1,$C1))", "B6", "B7", "B8", "B9");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("ISNA(CHISQ.INV.RT($E1,$C1))", "B8");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatTFChiSquareDistributionUnsupportedShapes()
    {
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("T.DIST($A$1:$A$2,$C1,TRUE)>0");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("T.DIST($A1,$C1)>0");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("TDIST($A1,$C1,3)>0");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("T.DIST(KURT($A1),$C1,TRUE)>0");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("T.DIST($A1,$C1,\"TRUE\")>0");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("F.DIST($A$1:$A$2,$C1,$D1,TRUE)>0");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("F.DIST($A1,$C1,$D1)>0");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("F.DIST($A1,$C1,$D1,\"TRUE\")>0");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("CHISQ.DIST($A$1:$A$2,$C1,TRUE)>0");
        AssertFormulaTFChiSquareDistributionFunctionContrastLocations("CHISQ.DIST($A1,$C1)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatContinuousDistributionScalarFunctions()
    {
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,BETA.DIST($A1,$C1,$D1,$F1,$H1,$I1)>0.49)", "B1");
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,BETA.INV($E1,$C1,$D1,$H1,$I1)>0.49)", "B1");
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,BETADIST($A1,$C1,$D1,$H1,$I1)>0.49)", "B1");
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,BETAINV($E1,$C1,$D1,$H1,$I1)>0.49)", "B1");
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,GAMMA($A1)>1.7)", "B1");
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,GAMMA.DIST($A1,$C1,$D1,$F1)>0.02)", "B1");
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,GAMMA.INV($E1,$C1,$D1)>3)", "B1");
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,GAMMADIST($A1,$C1,$D1,$F1)>0.02)", "B1");
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,GAMMAINV($E1,$C1,$D1)>3)", "B1");
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,GAMMALN($A1)>0.5)", "B1");
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,GAMMALN.PRECISE($A1)>0.5)", "B1");
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,LOGNORM.DIST($A1,$J1,$K1,$F1)>0.2)", "B1");
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,LOGNORM.INV($E1,$J1,$K1)>0.9)", "B1");
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,LOGNORMDIST($A1,$J1,$K1)>0.2)", "B1");
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,LOGINV($E1,$J1,$K1)>0.9)", "B1");
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,EXPON.DIST($A1,$L1,$F1)>0.3)", "B1");
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,EXPONDIST($A1,$L1,$F1)>0.3)", "B1");
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,WEIBULL($A1,$C1,$D1,$F1)>0.05)", "B1");
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,WEIBULL.DIST($A1,$C1,$D1,$F1)>0.05)", "B1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatContinuousDistributionNestedScalars()
    {
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,BETA.DIST(ABS($A1),$C1,$D1,$F1,$H1,$I1)>0.49)", "B1");
        AssertFormulaContinuousDistributionFunctionContrastLocations("AND($G1,SUM(GAMMA($A1),LOGNORM.INV($E1,$J1,$K1))>2.7)", "B1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatContinuousDistributionErrors()
    {
        AssertFormulaContinuousDistributionFunctionContrastLocations("ISERROR(BETA.DIST($A1,$C1,$D1,$F1,$H1,$I1))", "B2", "B3", "B4", "B6", "B7");
        AssertFormulaContinuousDistributionFunctionContrastLocations("ISNA(BETA.INV($E1,$C1,$D1,$H1,$I1))", "B6");
        AssertFormulaContinuousDistributionFunctionContrastLocations("ISERROR(GAMMA($A1))", "B3", "B6", "B7");
        AssertFormulaContinuousDistributionFunctionContrastLocations("ISERROR(GAMMA.INV($E1,$C1,$D1))", "B2", "B4", "B5", "B6", "B7");
        AssertFormulaContinuousDistributionFunctionContrastLocations("ISERR(GAMMALN($A1))", "B3", "B7");
        AssertFormulaContinuousDistributionFunctionContrastLocations("ISERROR(LOGNORM.DIST($A1,$J1,$K1,$F1))", "B3", "B4", "B6", "B7");
        AssertFormulaContinuousDistributionFunctionContrastLocations("ISERROR(LOGNORM.INV($E1,$J1,$K1))", "B4", "B5", "B6", "B7");
        AssertFormulaContinuousDistributionFunctionContrastLocations("ISERROR(EXPON.DIST($A1,$L1,$F1))", "B3", "B4", "B6", "B7");
        AssertFormulaContinuousDistributionFunctionContrastLocations("ISERROR(WEIBULL.DIST($A1,$C1,$D1,$F1))", "B2", "B3", "B4", "B6", "B7");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatContinuousDistributionUnsupportedShapes()
    {
        AssertFormulaContinuousDistributionFunctionContrastLocations("BETA.DIST($A$1:$A$2,2,2,TRUE)>0");
        AssertFormulaContinuousDistributionFunctionContrastLocations("GAMMA.INV($E1,$C1)>0");
        AssertFormulaContinuousDistributionFunctionContrastLocations("LOGNORM.DIST($A1,$J1,$K1)>0");
        AssertFormulaContinuousDistributionFunctionContrastLocations("EXPON.DIST($A1,$L1,\"TRUE\")>0");
        AssertFormulaContinuousDistributionFunctionContrastLocations("WEIBULL.DIST($A1,$C1,$D1,$F1,1)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDiscreteStatisticalScalarFunctions()
    {
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("AND($G1,FISHER($A1)>0.5)", "B1");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("AND($G1,FISHERINV($E1)>0.6)", "B2", "B3");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("AND($G1,BINOM.DIST($A1,$C1,$D1,FALSE)>0.25)", "B2", "B4");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("AND($G1,BINOMDIST($A1,$C1,$D1,TRUE)>0.7)", "B3");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("AND($G1,BINOM.DIST.RANGE($C1,$D1,$A1,2)>0.6)", "B1", "B4");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("AND($G1,BINOM.INV($C1,$D1,$E1)>=2)", "B1", "B2", "B3");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("AND($G1,CRITBINOM($C1,$D1,$E1)=0)", "B4");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("AND($G1,HYPGEOM.DIST($A1,$C1,2,$H1,FALSE)>0.3)", "B1", "B4");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("AND($G1,HYPGEOMDIST($A1,$C1,2,$H1)>0.3)", "B1", "B4");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("AND($G1,NEGBINOM.DIST($A1,$C1,$D1,FALSE)>0.05)", "B1", "B2");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("AND($G1,NEGBINOMDIST($A1,$C1,$D1)>0.05)", "B1", "B2");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("AND($G1,POISSON.DIST($A1,$C1,FALSE)>0.14)", "B2", "B3");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("AND($G1,POISSON($A1,$C1,TRUE)<0.3)", "B1", "B2", "B3", "B4");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("AND($G1,SERIESSUM($A1,$I1,$J1,$K1)>10)", "B2", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDiscreteStatisticalNestedScalars()
    {
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("AND($G1,FISHERINV(ABS($E1))>0.6)", "B2", "B3");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations(
            "AND($G1,SUM(BINOM.DIST($A1,$C1,$D1,FALSE),POISSON.DIST($A1,$C1,FALSE))>0.5)",
            "B2",
            "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDiscreteStatisticalErrors()
    {
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("ISERROR(FISHER($A1))", "B2", "B3", "B5", "B8", "B9");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("ISERROR(BINOM.DIST($A1,$C1,$D1,TRUE))", "B5", "B6", "B7", "B8", "B9");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("ISNA(BINOM.DIST($A1,$C1,$D1,TRUE))", "B8");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("ISERR(BINOM.DIST($A1,$C1,$D1,TRUE))", "B5", "B6", "B7", "B9");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("ISERROR(HYPGEOM.DIST($A1,$C1,2,$H1,TRUE))", "B3", "B5", "B6", "B8", "B9");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("ISERROR(NEGBINOM.DIST($A1,$C1,$D1,TRUE))", "B5", "B6", "B7", "B8", "B9");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("ISERROR(POISSON.DIST($A1,$C1,TRUE))", "B5", "B6", "B8", "B9");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("ISERROR(SERIESSUM($A1,$I1,$J1,$K1))", "B8", "B9");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatDiscreteStatisticalShapeAndErrorSemantics()
    {
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("FISHER($A$1:$A$1)>0", "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("BINOM.DIST($A$1:$A$2,$C1,$D1,TRUE)>0");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("BINOM.DIST($A1,$C1,$D1)>0");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("HYPGEOM.DIST($A1,$C1,2,$H1)>0");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("NEGBINOM.DIST($A1,$C1,$D1)>0");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("POISSON.DIST($A1,$C1,\"TRUE\")>0");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("SERIESSUM($A1,$I1,$J1,$K$1:$K$2)>0", "B1", "B2", "B3", "B6", "B7");
        AssertFormulaDiscreteStatisticalFunctionContrastLocations("SERIESSUM($A1,$I1,$J1)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialAnnuityFunctions()
    {
        AssertFormulaFinancialAnnuityFunctionContrastLocations("PMT($A1,$C1,$D1,$G1,$H1)<-180", "B1", "B3", "B6");
        AssertFormulaFinancialAnnuityFunctionContrastLocations("PV($A1,$C1,$E1,$G1,$H1)<-9000", "B1", "B3", "B6");
        AssertFormulaFinancialAnnuityFunctionContrastLocations("FV($A1,12,$F1)>1500", "B1", "B3", "B4", "B6");
        AssertFormulaFinancialAnnuityFunctionContrastLocations("NPER($A1,$F1,$D1,$G1,$H1)>50", "B1", "B3", "B6");
        AssertFormulaFinancialAnnuityFunctionContrastLocations("RATE($C1,$F1,$D1,$G1,$H1)>0.004", "B1", "B3", "B6");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialPaymentBreakdownFunctions()
    {
        AssertFormulaFinancialAnnuityFunctionContrastLocations("IPMT($A1,$I1,$C1,$D1,$G1,$H1)<0", "B1", "B3");
        AssertFormulaFinancialAnnuityFunctionContrastLocations("PPMT($A1,$I1,$C1,$D1,$G1,$H1)<0", "B1", "B2", "B3");
        AssertFormulaFinancialAnnuityFunctionContrastLocations("ISPMT($A1,$I1,$C1,$D1)<0", "B1", "B3", "B4");
        AssertFormulaFinancialAnnuityFunctionContrastLocations("CUMIPMT($A1,$C1,$D1,1,$I1,$H1)<0", "B1", "B3");
        AssertFormulaFinancialAnnuityFunctionContrastLocations("CUMPRINC($A1,$C1,$D1,1,$I1,$H1)<0", "B1", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialDefaultsWrappersAndPredicates()
    {
        AssertFormulaFinancialAnnuityFunctionContrastLocations("IF(PMT($A1,$C1,$D1)<-180,TRUE,FALSE)", "B1", "B3", "B4", "B6");
        AssertFormulaFinancialAnnuityFunctionContrastLocations("AND(RATE($C1,$F1,$D1)>0.004,$H1=0)", "B1", "B6");
        AssertFormulaFinancialAnnuityFunctionContrastLocations("ISNUMBER(PV($A1,$C1,$E1))", "B1", "B2", "B3", "B4", "B6");
    }

    [Fact]
    public void FindIssues_PropagatesFormulaConditionalFormatFinancialErrorsAndRejectsRanges()
    {
        AssertFormulaFinancialAnnuityFunctionContrastLocations("ISNA(PMT($A1,$C1,$D1))", "B5");
        AssertFormulaFinancialAnnuityFunctionContrastLocations("ISERROR(IPMT($A1,$I1,$C1,$D1,$G1,$H1))", "B4", "B5", "B6");
        AssertFormulaFinancialAnnuityFunctionContrastLocations("ISERROR(CUMIPMT($A1,$C1,$D1,1,$I1,$H1))", "B2", "B4", "B5", "B6");
        AssertFormulaFinancialAnnuityFunctionContrastLocations("ISNA(CUMPRINC($A1,$C1,$D1,1,$I1,$H1))", "B5");
        AssertFormulaFinancialAnnuityFunctionContrastLocations("PMT($A$1:$A$2,$C1,$D1)<0");
        AssertFormulaFinancialAnnuityFunctionContrastLocations("ISERROR(PMT($A$1:$A$1,$C1,$D1))", "B5");
        AssertFormulaFinancialAnnuityFunctionContrastLocations("CUMIPMT($A$1:$A$2,$C1,$D1,1,$I1,$H1)<0");
        AssertFormulaFinancialAnnuityFunctionContrastLocations("CUMPRINC($A1,$C1,$D1,1,$I1)<0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatConvertScalarComparisons()
    {
        AssertFormulaConvertFunctionContrastLocations("CONVERT($A1,$C1,$D1)=1000", "B1");
        AssertFormulaConvertFunctionContrastLocations("CONVERT($A1,$C1,$D1)=100", "B2");
        AssertFormulaConvertFunctionContrastLocations("CONVERT($A1,$C1,$D1)=20", "B3");
        AssertFormulaConvertFunctionContrastLocations("CONVERT($A1,$C1,$D1)=2048", "B4");
        AssertFormulaConvertFunctionContrastLocations("CONVERT($A1,$C1,$D1)<1", "B5");
        AssertFormulaConvertFunctionContrastLocations("CONVERT($A1,$C1,$D1)>200", "B1", "B4", "B6");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatConvertWrappersPredicatesAndAggregates()
    {
        AssertFormulaConvertFunctionContrastLocations("AND(CONVERT($A1,$C1,$D1)>=100,$E1)", "B1", "B2", "B4");
        AssertFormulaConvertFunctionContrastLocations("IF(CONVERT($A1,$C1,$D1)>100,TRUE,FALSE)", "B1", "B4", "B6");
        AssertFormulaConvertFunctionContrastLocations("ISNUMBER(CONVERT($A1,$C1,$D1))", FormulaConvertNumericLocations);
        AssertFormulaConvertFunctionContrastLocations("CONVERT($A1,LOWER(\"M\"),$D1)=100", "B2");
        AssertFormulaConvertFunctionContrastLocations("CONVERT($A1,$C1,$D1)+1=101", "B2");
        AssertFormulaConvertFunctionContrastLocations("SUM(CONVERT($A1,$C1,$D1),1)>2000", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatConvertInvalidUnitsAndErrors()
    {
        AssertFormulaConvertFunctionContrastLocations("ISERROR(CONVERT($A1,$C1,$D1))", "B7", "B8", "B9", "B10", "B11");
        AssertFormulaConvertFunctionContrastLocations("ISNA(CONVERT($A1,$C1,$D1))", "B7", "B8", "B10");
        AssertFormulaConvertFunctionContrastLocations("ISERR(CONVERT($A1,$C1,$D1))", "B9", "B11");
        AssertFormulaConvertFunctionContrastLocations("CONVERT($A1,$C1,$D1)>0", "B1", "B2", "B3", "B4", "B6");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatConvertWrongArity()
    {
        AssertFormulaConvertFunctionContrastLocations("CONVERT($A1,$C1)>0");
        AssertFormulaConvertFunctionContrastLocations("CONVERT($A1,$C1,$D1,1)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatComplexScalarComparisons()
    {
        AssertFormulaComplexFunctionContrastLocations("IMREAL($A1)=3", "B1");
        AssertFormulaComplexFunctionContrastLocations("IMREAL($A1)=5", "B2");
        AssertFormulaComplexFunctionContrastLocations("IMREAL($A1)=7", "B5");
        AssertFormulaComplexFunctionContrastLocations("IMAGINARY($A1)=1", "B3");
        AssertFormulaComplexFunctionContrastLocations("IMAGINARY($A1)=-1", "B6");
        AssertFormulaComplexFunctionContrastLocations("IMABS($A1)=5", "B1");
        AssertFormulaComplexFunctionContrastLocations("IMABS($A1)=13", "B2");
        AssertFormulaComplexFunctionContrastLocations(
            "ABS(IMARGUMENT($A1)-0.927295218001612)<0.000000000001",
            "B1");
        AssertFormulaComplexFunctionContrastLocations("IMARGUMENT($A1)>0.9", "B1", "B3");
        AssertFormulaComplexFunctionContrastLocations("IMARGUMENT($A1)<-1", "B2", "B6");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatComplexTextComparisonsAndSuffixes()
    {
        AssertFormulaComplexFunctionContrastLocations("COMPLEX($C1,$D1,$E1)=$A1", "B1", "B2");
        AssertFormulaComplexFunctionContrastLocations("COMPLEX($C1,$D1,$E1)=\"1234+0.5i\"", "B4");
        AssertFormulaComplexFunctionContrastLocations("COMPLEX($C1,$D1,$E1)=\"-j\"", "B5");
        AssertFormulaComplexFunctionContrastLocations("COMPLEX($C1,$D1,$E1)=\"3\"", "B6");
        AssertFormulaComplexFunctionContrastLocations("IMCONJUGATE($A1)=\"3-4i\"", "B1");
        AssertFormulaComplexFunctionContrastLocations("IMCONJUGATE($A1)=\"5+12j\"", "B2");
        AssertFormulaComplexFunctionContrastLocations("IMCONJUGATE($A1)=\"j\"", "B6");
        AssertFormulaComplexFunctionContrastLocations("IMSQRT($A1)=\"2+i\"", "B1");
        AssertFormulaComplexFunctionContrastLocations("IMSQRT($A1)=\"3-2j\"", "B2");
        AssertFormulaComplexFunctionContrastLocations(
            "IMEXP($A1)=\"-13.1287830814622-15.200784463068i\"",
            "B1");
        AssertFormulaComplexFunctionContrastLocations(
            "IMLN($A1)=\"1.6094379124341+0.927295218001612i\"",
            "B1");
        AssertFormulaComplexFunctionContrastLocations(
            "IMLOG10($A1)=\"1.11394335230684-0.510732572130908j\"",
            "B2");
        AssertFormulaComplexFunctionContrastLocations(
            "IMLOG2($A1)=\"2.32192809488736+1.33780421245098i\"",
            "B1");
        AssertFormulaComplexFunctionContrastLocations("IMLOG2($A1)=\"2.8073549220576\"", "B5");
        AssertFormulaComplexFunctionContrastLocations(
            "EXACT(IMCONJUGATE(COMPLEX($C1,$D1,$E1)),\"5+12j\")",
            "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatComplexTrigTextComparisons()
    {
        AssertFormulaComplexFunctionContrastLocations("IMSIN($A1)=\"1.1752011936438i\"", "B3");
        AssertFormulaComplexFunctionContrastLocations("IMCOS($A1)=\"1.54308063481524\"", "B3", "B6");
        AssertFormulaComplexFunctionContrastLocations("IMTAN($A1)=\"0.761594155955765i\"", "B3");
        AssertFormulaComplexFunctionContrastLocations("IMSEC($A1)=\"0.648054273663885\"", "B3", "B6");
        AssertFormulaComplexFunctionContrastLocations("IMCSC($A1)=\"-0.850918128239322i\"", "B3");
        AssertFormulaComplexFunctionContrastLocations("IMCOT($A1)=\"-1.31303528549933i\"", "B3");
        AssertFormulaComplexFunctionContrastLocations("IMSINH($A1)=\"0.841470984807897i\"", "B3");
        AssertFormulaComplexFunctionContrastLocations("IMCOSH($A1)=\"0.54030230586814\"", "B3", "B6");
        AssertFormulaComplexFunctionContrastLocations("IMSECH($A1)=\"1.85081571768093\"", "B3", "B6");
        AssertFormulaComplexFunctionContrastLocations("IMCSCH($A1)=\"-1.18839510577812i\"", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatComplexTrigWrappersPredicatesAndSuffixes()
    {
        AssertFormulaComplexFunctionContrastLocations("AND(ISTEXT(IMSIN($A1)),RIGHT(IMSIN($A1),1)=\"j\")", "B2", "B6");
        AssertFormulaComplexFunctionContrastLocations("IF(IMSECH($A1)=\"1.85081571768093\",TRUE,FALSE)", "B3", "B6");
        AssertFormulaComplexFunctionContrastLocations("ISERROR(IMCSC(0))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("IMSIN(COMPLEX(0,1,LOWER(\"J\")))=\"1.1752011936438j\"", FormulaComplexAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatComplexTrigErrors()
    {
        AssertFormulaComplexFunctionContrastLocations("ISERROR(IMTAN(1.5707963267948966))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISERROR(IMCSC(0))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISERROR(IMCOT(0))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISERROR(IMCSCH(0))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISERROR(IMCOS(COMPLEX(1,1E308)))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISNA(IMSEC(NA()))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("IMCSC(0)=\"0\"");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatComplexArithmeticComparisons()
    {
        AssertFormulaComplexFunctionContrastLocations("IMSUM($A1,\"1+i\")=\"4+5i\"", "B1");
        AssertFormulaComplexFunctionContrastLocations("IMSUM($A1,\"1+i\")=\"6-11i\"");
        AssertFormulaComplexFunctionContrastLocations("IMSUB($A1,\"1+i\")=\"2+3i\"", "B1");
        AssertFormulaComplexFunctionContrastLocations("IMSUB($A1,\"1+i\")=\"4-13j\"");
        AssertFormulaComplexFunctionContrastLocations("IMPRODUCT($A1,\"1+i\")=\"-1+7i\"", "B1");
        AssertFormulaComplexFunctionContrastLocations("IMDIV($A1,\"1+i\")=\"3.5+0.5i\"", "B1");
        AssertFormulaComplexFunctionContrastLocations("IMREAL(IMPOWER($A1,2))=-7", "B1");
        AssertFormulaComplexFunctionContrastLocations("IMAGINARY(IMPOWER($A1,2))=24", "B1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatComplexWrappersPredicatesAndAggregates()
    {
        AssertFormulaComplexFunctionContrastLocations("AND(IMABS($A1)>6,$F1)", "B2");
        AssertFormulaComplexFunctionContrastLocations("AND(IMARGUMENT($A1)>0,$F1)", "B1");
        AssertFormulaComplexFunctionContrastLocations("IF(IMAGINARY($A1)<0,TRUE,FALSE)", "B2", "B6");
        AssertFormulaComplexFunctionContrastLocations("ISNUMBER(IMREAL($A1))", "B1", "B2", "B3", "B4", "B5", "B6");
        AssertFormulaComplexFunctionContrastLocations("ISTEXT(COMPLEX($C1,$D1,$E1))", "B1", "B2", "B4", "B5", "B6");
        AssertFormulaComplexFunctionContrastLocations("ISTEXT(IMLN($A1))", "B1", "B2", "B3", "B4", "B5", "B6");
        AssertFormulaComplexFunctionContrastLocations("COMPLEX(0,1,LOWER(\"J\"))=\"j\"", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("IF(EXACT(IMSQRT($A1),\"3-2j\"),TRUE,FALSE)", "B2");
        AssertFormulaComplexFunctionContrastLocations("IMREAL($A1)+IMAGINARY($A1)=7", "B1", "B5");
        AssertFormulaComplexFunctionContrastLocations("SUM(IMABS($A1),1)>13", "B2");
        AssertFormulaComplexFunctionContrastLocations("SUM(IMARGUMENT($A1),1)>2", "B3");
        AssertFormulaComplexFunctionContrastLocations("ABS(IMABS($A1)-5)<0.000000000001", "B1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatComplexArithmeticWrappersPredicatesAndAggregates()
    {
        AssertFormulaComplexFunctionContrastLocations("AND(IMABS(IMSUM($A1,\"1+i\"))>10,$F1)");
        AssertFormulaComplexFunctionContrastLocations("IF(IMREAL(IMDIV($A1,\"1+i\"))>0,TRUE,FALSE)", "B1", "B3", "B4", "B5");
        AssertFormulaComplexFunctionContrastLocations("ISTEXT(IMPOWER($A1,2))", "B1", "B2", "B3", "B4", "B5", "B6");
        AssertFormulaComplexFunctionContrastLocations("SUM(IMABS(IMPRODUCT($A1,\"1+i\")),1)>14");
        AssertFormulaComplexFunctionContrastLocations("ABS(IMAGINARY(IMPOWER($A1,2))-24)<0.000000000001", "B1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatComplexArithmeticRangeFlattening()
    {
        AssertFormulaComplexFunctionContrastLocations("IMSUM($A$1:$A$3)=\"8-7i\"");
        AssertFormulaComplexFunctionContrastLocations("AND(IMPRODUCT($A$1:$A$3)=\"16+63i\",$F1)");
        AssertFormulaComplexFunctionContrastLocations("IMSUM($A$1:$A$2,\"1+i\")=\"9-7i\"");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatComplexErrors()
    {
        AssertFormulaComplexFunctionContrastLocations("ISERROR(IMREAL($A1))", "B7", "B8", "B9");
        AssertFormulaComplexFunctionContrastLocations("ISNA(IMREAL($A1))", "B8");
        AssertFormulaComplexFunctionContrastLocations("ISERR(IMREAL($A1))", "B7", "B9");
        AssertFormulaComplexFunctionContrastLocations("ISERROR(IMLN($A1))", "B7", "B8", "B9");
        AssertFormulaComplexFunctionContrastLocations("ISERROR(COMPLEX($C1,$D1,$E1))", "B3", "B7", "B8", "B9");
        AssertFormulaComplexFunctionContrastLocations("ISNA(COMPLEX($C1,$D1,$E1))", "B8");
        AssertFormulaComplexFunctionContrastLocations("ISERR(COMPLEX($C1,$D1,$E1))", "B3", "B7", "B9");
        AssertFormulaComplexFunctionContrastLocations("ISERROR(COMPLEX(1,2,\"x\"))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISERROR(COMPLEX(1E309,0))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISERROR(COMPLEX(\"Open\",2))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISERROR(IMARGUMENT(0))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISERR(IMARGUMENT(0))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISERROR(IMLN(0))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISERR(IMLOG10(COMPLEX(0,0)))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISERROR(IMLOG2(\"0\"))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISERROR(IMEXP(\"1000\"))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISERROR(IMSQRT(\"1E309i\"))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISERROR(IMREAL(\"not complex\"))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISERROR(IMREAL(\"1,234\"))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISERROR(IMAGINARY(\"1E309i\"))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISNA(IMSQRT(NA()))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISNA(COMPLEX(NA(),2))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISNA(COMPLEX(1,NA()))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISNA(COMPLEX(1,2,NA()))", FormulaComplexAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatComplexArithmeticErrors()
    {
        AssertFormulaComplexFunctionContrastLocations("ISERROR(IMDIV($A1,\"0\"))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISNA(IMDIV($A1,\"0\"))", "B8");
        AssertFormulaComplexFunctionContrastLocations("ISERR(IMDIV($A1,\"0\"))", "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B9");
        AssertFormulaComplexFunctionContrastLocations("ISERROR(IMPOWER(0,-1))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISERROR(IMPOWER(\"1+i\",\"Open\"))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISNA(IMPOWER(\"not complex\",NA()))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISNA(IMSUM($A$8:$A$9))", FormulaComplexAllLocations);
        AssertFormulaComplexFunctionContrastLocations("ISERR(IMPRODUCT($A$7:$A$8))", FormulaComplexAllLocations);
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatComplexUnsupportedShapesOrErrorComparisons()
    {
        AssertFormulaComplexFunctionContrastLocations("COMPLEX($C1)>0");
        AssertFormulaComplexFunctionContrastLocations("COMPLEX($C1,$D1,$E1,1)>0");
        AssertFormulaComplexFunctionContrastLocations("IMARGUMENT()>0");
        AssertFormulaComplexFunctionContrastLocations("IMARGUMENT($A1,1)>0");
        AssertFormulaComplexFunctionContrastLocations("IMSQRT()>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMSQRT($A1,1)>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMEXP()>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMEXP($A1,1)>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMLN()>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMLN($A1,1)>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMLOG10()>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMLOG10($A1,1)>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMLOG2()>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMLOG2($A1,1)>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMREAL()>0");
        AssertFormulaComplexFunctionContrastLocations("IMREAL($A1,1)>0");
        AssertFormulaComplexFunctionContrastLocations("IMAGINARY()>0");
        AssertFormulaComplexFunctionContrastLocations("IMABS($A1,1)>0");
        AssertFormulaComplexFunctionContrastLocations("IMCONJUGATE()>\"\"");
        AssertFormulaComplexFunctionContrastLocations("COMPLEX(1,2,\"x\")=\"1+2i\"");
        AssertFormulaComplexFunctionContrastLocations("IMREAL(\"not complex\")>0");
        AssertFormulaComplexFunctionContrastLocations("IMREAL(NA())>0");
        AssertFormulaComplexFunctionContrastLocations("IMSIN()>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMCOS($A1,1)>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMCSCH($A1,1)>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMSUM()>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMPRODUCT()>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMSUB($A1)>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMSUB($A1,1,2)>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMDIV($A1)>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMPOWER($A1)>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMPOWER($A1,$A$1:$A$2)>\"\"");
        AssertFormulaComplexFunctionContrastLocations("IMDIV($A1,\"0\")>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatErrorFunctionComparisons()
    {
        AssertFormulaArithmeticContrastLocations("ERF($A1/100)", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ERF($A1/100)>0.8", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ERFC($A1/100)<0.2", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ERF($A1/100,1)>0.1", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("ERF(0,$A1/100)>0.8", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ERF.PRECISE($A1/100)>0.9", "B4");
        AssertFormulaArithmeticContrastLocations("ERFC.PRECISE($A1/100)<0.1", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatErrorFunctionWrappersPredicatesAndAggregates()
    {
        AssertFormulaArithmeticContrastLocations("AND(ERF($A1/100)>0.8,$C1=\"Open\")", "B4");
        AssertFormulaArithmeticContrastLocations("IF(ERFC($A1/100)<0.2,TRUE,FALSE)", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("ISNUMBER(ERF.PRECISE($A1/100))", "B1", "B2", "B3", "B4");
        AssertFormulaAggregateContrastLocations("SUM(ERF($A1/100),1)>1.8", "B2", "B4");
        AssertFormulaAggregateContrastLocations("SUM(ERFC.PRECISE($A1/100),1)<1.2", "B2", "B4");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatErrorFunctionUnsupportedOperands()
    {
        AssertFormulaArithmeticContrastLocations("ERF()>0");
        AssertFormulaArithmeticContrastLocations("ERF($A1,1,2)>0");
        AssertFormulaArithmeticContrastLocations("ERF.PRECISE($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("ERFC()>0");
        AssertFormulaArithmeticContrastLocations("ERFC($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("ERFC.PRECISE($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("ERF(\"x\")>0");
        AssertFormulaArithmeticContrastLocations("ERF(0,\"x\")>0");
        AssertFormulaArithmeticContrastLocations("ERFC(\"x\")>0");
        AssertFormulaArithmeticContrastLocations("ERF.PRECISE(\"x\")>0");
        AssertFormulaArithmeticContrastLocations("ERFC.PRECISE(\"x\")>0");
        AssertFormulaArithmeticContrastLocations("ERF($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("ERF(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("ERF(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ERF(0,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ERFC(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ERF(NA())>0");
        AssertFormulaArithmeticContrastLocations("ERF(0,NA())>0");
        AssertFormulaArithmeticContrastLocations("ERFC(NA())>0");
        AssertFormulaArithmeticContrastLocations("ERF.PRECISE(NA())>0");
        AssertFormulaArithmeticContrastLocations("ERFC.PRECISE(NA())>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatBitShiftNegativeDirection()
    {
        AssertFormulaArithmeticContrastLocations("BITLSHIFT($A1,-2)=18", "B1", "B3");
        AssertFormulaArithmeticContrastLocations("BITRSHIFT($A1,-1)>200", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatBaseConversionToDecimalFunctions()
    {
        AssertFormulaBaseConversionFunctionContrastLocations("BIN2DEC($A1)=10", "B1", "B4");
        AssertFormulaBaseConversionFunctionContrastLocations("HEX2DEC($C1)=255", "B4");
        AssertFormulaBaseConversionFunctionContrastLocations("OCT2DEC($D1)=15", "B2");
        AssertFormulaBaseConversionFunctionContrastLocations("BIN2DEC($A1)=-1", "B3");
        AssertFormulaBaseConversionFunctionContrastLocations("HEX2DEC($C1)=-1", "B3");
        AssertFormulaBaseConversionFunctionContrastLocations("OCT2DEC($D1)=-1", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDecimalToBaseFunctions()
    {
        AssertFormulaBaseConversionFunctionContrastLocations("DEC2BIN($E1)=\"1010\"", "B1");
        AssertFormulaBaseConversionFunctionContrastLocations("DEC2BIN($E1,$F1)=\"00001010\"", "B1");
        AssertFormulaBaseConversionFunctionContrastLocations("DEC2BIN($E1)=\"1111111111\"", "B3");
        AssertFormulaBaseConversionFunctionContrastLocations("DEC2BIN($E1,$F1)=\"0000\"", "B5");
        AssertFormulaBaseConversionFunctionContrastLocations("DEC2HEX($E1,$F1)=\"001F\"", "B4");
        AssertFormulaBaseConversionFunctionContrastLocations("DEC2HEX($E1)=\"FFFFFFFFFF\"", "B3");
        AssertFormulaBaseConversionFunctionContrastLocations("DEC2OCT($E1,$F1)=\"0017\"", "B2");
        AssertFormulaBaseConversionFunctionContrastLocations("DEC2OCT($E1)=\"7777777777\"", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatBaseToBaseFunctions()
    {
        AssertFormulaBaseConversionFunctionContrastLocations("BIN2HEX($A1)=\"A\"", "B1", "B4");
        AssertFormulaBaseConversionFunctionContrastLocations("BIN2OCT($A1)=\"12\"", "B1", "B4");
        AssertFormulaBaseConversionFunctionContrastLocations("HEX2BIN($C1)=\"1111\"", "B2");
        AssertFormulaBaseConversionFunctionContrastLocations("HEX2OCT($C1)=\"17\"", "B2");
        AssertFormulaBaseConversionFunctionContrastLocations("OCT2BIN($D1)=\"1111\"", "B2");
        AssertFormulaBaseConversionFunctionContrastLocations("OCT2HEX($D1)=\"FF\"", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatBaseToBaseOptionalPlacesAndCoercion()
    {
        AssertFormulaBaseConversionFunctionContrastLocations("EXACT(BIN2HEX($A1,$F1),\"0000000A\")", "B1");
        AssertFormulaBaseConversionFunctionContrastLocations("EXACT(BIN2OCT($A1,$F1),\"0012\")", "B4");
        AssertFormulaBaseConversionFunctionContrastLocations("EXACT(HEX2BIN($C1,$F1),\"00001010\")", "B1");
        AssertFormulaBaseConversionFunctionContrastLocations("EXACT(HEX2OCT($C1,$F1),\"0017\")", "B2");
        AssertFormulaBaseConversionFunctionContrastLocations("EXACT(OCT2BIN($D1,$F1),\"00001010\")", "B1");
        AssertFormulaBaseConversionFunctionContrastLocations("EXACT(OCT2HEX($D1,$F1),\"000F\")", "B2");
        AssertFormulaBaseConversionFunctionContrastLocations("EXACT(BIN2HEX($A1,$H1),\"A\")", "B1", "B4");
        AssertFormulaBaseConversionFunctionContrastLocations("EXACT(BIN2HEX(\"101\",\"4.9\"),\"0005\")", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("EXACT(HEX2BIN(\"1\",TRUE),\"1\")", FormulaBaseConversionAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatBaseToBaseNegativeTwosComplement()
    {
        AssertFormulaBaseConversionFunctionContrastLocations("EXACT(BIN2HEX($A1,$F1),\"FFFFFFFFFF\")", "B3");
        AssertFormulaBaseConversionFunctionContrastLocations("EXACT(BIN2OCT($A1,$F1),\"7777777777\")", "B3");
        AssertFormulaBaseConversionFunctionContrastLocations("EXACT(HEX2BIN($C1,$F1),\"1111111111\")", "B3");
        AssertFormulaBaseConversionFunctionContrastLocations("EXACT(HEX2OCT($C1,$F1),\"7777777777\")", "B3");
        AssertFormulaBaseConversionFunctionContrastLocations("EXACT(OCT2BIN($D1,$F1),\"1111111111\")", "B3");
        AssertFormulaBaseConversionFunctionContrastLocations("EXACT(OCT2HEX($D1,$F1),\"FFFFFFFFFF\")", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatBaseFunctionOperands()
    {
        AssertFormulaBaseConversionFunctionContrastLocations("BASE($E1,2)=\"1010\"", "B1");
        AssertFormulaBaseConversionFunctionContrastLocations("BASE($E1,2,$F1)=\"00001010\"", "B1");
        AssertFormulaBaseConversionFunctionContrastLocations("BASE($E1,16,$F1)=\"001F\"", "B4");
        AssertFormulaBaseConversionFunctionContrastLocations("BASE($E1,2,$F1)=\"0000\"", "B5");
        AssertFormulaBaseConversionFunctionContrastLocations("BASE(45745,36)=\"ZAP\"", FormulaBaseConversionAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatBaseFunctionTruncationAndPadding()
    {
        AssertFormulaBaseConversionFunctionContrastLocations("BASE($E1,2,1)=\"1111\"", "B2");
        AssertFormulaBaseConversionFunctionContrastLocations("BASE(15,2.9,8.9)=\"00001111\"", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("BASE(35,36.9)=\"Z\"", FormulaBaseConversionAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDecimalFunctionOperands()
    {
        AssertFormulaBaseConversionFunctionContrastLocations("DECIMAL($A1,2)=10", "B1", "B4");
        AssertFormulaBaseConversionFunctionContrastLocations("DECIMAL($A1,2)=15", "B2");
        AssertFormulaBaseConversionFunctionContrastLocations("DECIMAL($C1,16)=255", "B4");
        AssertFormulaBaseConversionFunctionContrastLocations("DECIMAL(111,2)=7", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("DECIMAL(\"zap\",36)=45745", FormulaBaseConversionAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDecimalFunctionTruncatesRadix()
    {
        AssertFormulaBaseConversionFunctionContrastLocations("DECIMAL($A1,2.9)=10", "B1", "B4");
        AssertFormulaBaseConversionFunctionContrastLocations("DECIMAL(\"11\",2.9)=3", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("DECIMAL(\"Z\",36.9)=35", FormulaBaseConversionAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatBaseDecimalWrappersPredicatesTextComparisonsAndAggregates()
    {
        AssertFormulaBaseConversionFunctionContrastLocations("IF(BASE($E1,2)=\"1010\",TRUE,FALSE)", "B1");
        AssertFormulaBaseConversionFunctionContrastLocations("EXACT(BASE($E1,16,$F1),\"001F\")", "B4");
        AssertFormulaBaseConversionFunctionContrastLocations("ISTEXT(BASE($E1,16))", "B1", "B2", "B4", "B5", "B6");
        AssertFormulaBaseConversionFunctionContrastLocations("IF(DECIMAL($A1,2)>10,TRUE,FALSE)", "B2", "B3");
        AssertFormulaBaseConversionFunctionContrastLocations("AND(DECIMAL($C1,16)>10,$G1=\"Closed\")", "B2", "B4");
        AssertFormulaBaseConversionFunctionContrastLocations("ISNUMBER(DECIMAL($A1,2))", "B1", "B2", "B3", "B4", "B5");
        AssertFormulaBaseConversionFunctionContrastLocations("DECIMAL($A1,2)+1=11", "B1", "B4");
        AssertFormulaBaseConversionFunctionContrastLocations("SUM(DECIMAL($A1,2),1)>100", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatBaseDecimalErrorPredicates()
    {
        AssertFormulaBaseConversionFunctionContrastLocations("ISERROR(BASE(-1,2))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISERROR(BASE(7,1))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISERROR(BASE(7,37))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISERROR(BASE(7,2,-1))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISERROR(BASE(7,2,256))", FormulaBaseConversionAllLocations);
        // 9.01E15 is unambiguously >= 2^53 (BASE's overflow limit) even after Excel's 15-significant-
        // digit literal cap; the exact-2^53 literal 9007199254740992 (16 sig digits) caps to
        // 9007199254740990, which is BELOW the limit and so no longer errors -- matching real Excel.
        AssertFormulaBaseConversionFunctionContrastLocations("ISERROR(BASE(9010000000000000,2))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISERROR(DECIMAL(\"\",16))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISERROR(DECIMAL(\"2\",2))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISERROR(DECIMAL(\"FF\",1))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISERROR(DECIMAL(\"FF\",37))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISERROR(DECIMAL(\"ZZZZZZZZZZZ\",36))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISERROR(BASE($E1,2))", "B3", "B7");
        AssertFormulaBaseConversionFunctionContrastLocations("ISERROR(DECIMAL($A1,2))", "B6", "B7");
        AssertFormulaBaseConversionFunctionContrastLocations("ISNA(BASE(NA(),2))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISNA(BASE(7,NA()))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISNA(BASE(7,2,NA()))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISNA(DECIMAL(NA(),16))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISNA(DECIMAL(\"FF\",NA()))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISNA(BASE($E1,2))", "B7");
        AssertFormulaBaseConversionFunctionContrastLocations("ISNA(DECIMAL($A1,2))", "B7");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatBaseDecimalUnsupportedOperandsOrErrorComparisons()
    {
        AssertFormulaBaseConversionFunctionContrastLocations("BASE($E1)=\"1010\"");
        AssertFormulaBaseConversionFunctionContrastLocations("BASE($E1,2,$F1,1)=\"1010\"");
        AssertFormulaBaseConversionFunctionContrastLocations("DECIMAL($A1)>0");
        AssertFormulaBaseConversionFunctionContrastLocations("DECIMAL($A1,2,1)>0");
        AssertFormulaBaseConversionFunctionContrastLocations("BASE(-1,2)=\"\"");
        AssertFormulaBaseConversionFunctionContrastLocations("BASE(7,1)=\"111\"");
        AssertFormulaBaseConversionFunctionContrastLocations("BASE(7,2,-1)=\"111\"");
        AssertFormulaBaseConversionFunctionContrastLocations("BASE(NA(),2)=\"\"");
        AssertFormulaBaseConversionFunctionContrastLocations("DECIMAL(\"2\",2)>0");
        AssertFormulaBaseConversionFunctionContrastLocations("DECIMAL(\"\",16)>0");
        AssertFormulaBaseConversionFunctionContrastLocations("DECIMAL(NA(),16)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatBaseConversionWrappersPredicatesAndTextComparisons()
    {
        AssertFormulaBaseConversionFunctionContrastLocations("IF(BIN2DEC($A1)=10,TRUE,FALSE)", "B1", "B4");
        AssertFormulaBaseConversionFunctionContrastLocations("AND(HEX2DEC($C1)>10,$G1=\"Closed\")", "B2", "B4");
        AssertFormulaBaseConversionFunctionContrastLocations("ISNUMBER(OCT2DEC($D1))", "B1", "B2", "B3", "B4", "B5");
        AssertFormulaBaseConversionFunctionContrastLocations("ISTEXT(DEC2BIN($E1))", "B1", "B2", "B3", "B4", "B5");
        AssertFormulaBaseConversionFunctionContrastLocations("EXACT(DEC2HEX($E1,$F1),\"001F\")", "B4");
        AssertFormulaBaseConversionFunctionContrastLocations("AND(EXACT(DEC2BIN($E1,$F1),\"00001010\"),$G1=\"Open\")", "B1");
        AssertFormulaBaseConversionFunctionContrastLocations("IF(EXACT(DEC2OCT($E1,$F1),\"0017\"),TRUE,FALSE)", "B2");
        AssertFormulaBaseConversionFunctionContrastLocations("AND(EXACT(BIN2HEX($A1,$F1),\"0000000A\"),$G1=\"Open\")", "B1");
        AssertFormulaBaseConversionFunctionContrastLocations("IF(EXACT(HEX2OCT($C1),\"377\"),TRUE,FALSE)", "B4");
        AssertFormulaBaseConversionFunctionContrastLocations("ISTEXT(OCT2BIN($D1))", "B1", "B2", "B3", "B4", "B5");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatBaseConversionUnsupportedAndErrorDomainCases()
    {
        AssertFormulaBaseConversionFunctionContrastLocations("BIN2DEC()>0");
        AssertFormulaBaseConversionFunctionContrastLocations("DEC2BIN($E1,$F1,1)=\"0\"");
        AssertFormulaBaseConversionFunctionContrastLocations("BIN2HEX()=\"0\"");
        AssertFormulaBaseConversionFunctionContrastLocations("OCT2HEX($D1,$F1,1)=\"0\"");
        AssertFormulaBaseConversionFunctionContrastLocations("BIN2DEC(\"102\")>0");
        AssertFormulaBaseConversionFunctionContrastLocations("HEX2DEC(\"10000000000\")>0");
        AssertFormulaBaseConversionFunctionContrastLocations("OCT2DEC(\"8\")>0");
        AssertFormulaBaseConversionFunctionContrastLocations("BIN2HEX(\"102\")=\"2\"");
        AssertFormulaBaseConversionFunctionContrastLocations("HEX2BIN(\"F\",2)=\"1111\"");
        AssertFormulaBaseConversionFunctionContrastLocations("OCT2HEX(\"17\",-1)=\"F\"");
        AssertFormulaBaseConversionFunctionContrastLocations("HEX2OCT(\"10000000000\")=\"0\"");
        AssertFormulaBaseConversionFunctionContrastLocations("DEC2BIN(512)=\"1000000000\"");
        AssertFormulaBaseConversionFunctionContrastLocations("DEC2BIN(10,2)=\"10\"");
        AssertFormulaBaseConversionFunctionContrastLocations("DEC2HEX(255,-1)=\"FF\"");
        AssertFormulaBaseConversionFunctionContrastLocations("DEC2OCT(64,1)=\"100\"");
        AssertFormulaBaseConversionFunctionContrastLocations("DEC2HEX(549755813888)=\"80000000000\"");
        AssertFormulaBaseConversionFunctionContrastLocations("DEC2OCT(536870912)=\"4000000000\"");
        AssertFormulaBaseConversionFunctionContrastLocations("ISERROR(BIN2DEC(\"102\"))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISERROR(BIN2HEX(\"102\"))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISERROR(HEX2BIN(\"F\",2))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISERROR(OCT2HEX(TRUE))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISERROR(BIN2OCT($H1))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISNA(DEC2BIN(NA()))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISNA(DEC2BIN(-1,NA()))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISNA(HEX2BIN(NA()))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISNA(BIN2HEX(\"102\",NA()))", FormulaBaseConversionAllLocations);
        AssertFormulaBaseConversionFunctionContrastLocations("ISNA(OCT2HEX(\"17\",NA()))", FormulaBaseConversionAllLocations);
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatScalarFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("ABS($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("ROUND($A1)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDUP($A1)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDUP($A1,0,1)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN($A1)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN($A1,0,1)>0");
        AssertFormulaArithmeticContrastLocations("TRUNC()>0");
        AssertFormulaArithmeticContrastLocations("TRUNC($A1,0,1)>0");
        AssertFormulaArithmeticContrastLocations("MROUND($A1)>0");
        AssertFormulaArithmeticContrastLocations("MROUND($A1,10,1)>0");
        AssertFormulaArithmeticContrastLocations("MROUND(\"10\",3)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("MROUND($A1,\"10\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("MROUND($A1&\"x\",10)>0");
        AssertFormulaArithmeticContrastLocations("MROUND(KURT($A1),10)>0");
        AssertFormulaArithmeticContrastLocations("MROUND($A1,-10)>0");
        AssertFormulaArithmeticContrastLocations("MROUND(1E308*1E308,10)>0");
        AssertFormulaArithmeticContrastLocations("MROUND($A1,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("MROUND(1E308,0.1)>0");
        AssertFormulaArithmeticContrastLocations("MOD($A1)>0");
        AssertFormulaArithmeticContrastLocations("MOD($A1,0)>0");
        AssertFormulaArithmeticContrastLocations("ROUND($A1,999999)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ROUNDUP($A1,999999)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN($A1,999999)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("TRUNC($A1,999999)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ROUNDUP(\"5\",0)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ROUNDUP($A1,\"1\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ROUNDUP($A1&\"x\",0)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDUP(KURT($A1),0)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDUP(1E308*1E308,0)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN(\"5\",0)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN($A1,\"1\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN($A1&\"x\",0)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN(KURT($A1),0)>0");
        AssertFormulaArithmeticContrastLocations("ROUNDDOWN(1E308*1E308,0)>0");
        AssertFormulaArithmeticContrastLocations("TRUNC(\"5\",0)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("TRUNC($A1,\"1\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("TRUNC($A1&\"x\",0)>0");
        AssertFormulaArithmeticContrastLocations("TRUNC(KURT($A1),0)>0");
        AssertFormulaArithmeticContrastLocations("TRUNC(1E308*1E308,0)>0");
        AssertFormulaArithmeticContrastLocations("FACT()>0");
        AssertFormulaArithmeticContrastLocations("FACT($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("FACT(\"5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FACT($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("FACT(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("FACT(-1)>0");
        AssertFormulaArithmeticContrastLocations("FACT(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("FACT(EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("FACT(171)>0");
        AssertFormulaArithmeticContrastLocations("FACTDOUBLE()>0");
        AssertFormulaArithmeticContrastLocations("FACTDOUBLE($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("FACTDOUBLE(\"5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("FACTDOUBLE($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("FACTDOUBLE(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("FACTDOUBLE(-1)>0");
        AssertFormulaArithmeticContrastLocations("FACTDOUBLE(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("FACTDOUBLE(EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("FACTDOUBLE(301)>0");
        AssertFormulaArithmeticContrastLocations("ABS(\"5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ABS($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("EVEN()>0");
        AssertFormulaArithmeticContrastLocations("EVEN($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("EVEN(\"5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("EVEN($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("EVEN(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("EVEN(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("EVEN(EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("SQRT()>0");
        AssertFormulaArithmeticContrastLocations("SQRT($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("SQRT(-$A1)>0");
        AssertFormulaArithmeticContrastLocations("SQRT(\"5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("SQRT($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("SQRTPI()>0");
        AssertFormulaArithmeticContrastLocations("SQRTPI($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("SQRTPI(-$A1)>0");
        AssertFormulaArithmeticContrastLocations("SQRTPI(\"5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("SQRTPI($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("SQRTPI(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("SQRTPI(1E308)>0");
        AssertFormulaArithmeticContrastLocations("SIGN()>0");
        AssertFormulaArithmeticContrastLocations("SIGN($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("SIGN(\"5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("SIGN($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("SIGN(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("SIGN(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("POWER($A1)>0");
        AssertFormulaArithmeticContrastLocations("POWER($A1,2,3)>0");
        AssertFormulaArithmeticContrastLocations("POWER(\"5\",2)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("POWER($A1,\"2\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("POWER($A1&\"x\",2)>0");
        AssertFormulaArithmeticContrastLocations("POWER(KURT($A1),2)>0");
        AssertFormulaArithmeticContrastLocations("POWER(1E308,2)>0");
        AssertFormulaArithmeticContrastLocations("POWER(0,-1)>0");
        AssertFormulaArithmeticContrastLocations("POWER(-$A1,0.5)>0");
        AssertFormulaArithmeticContrastLocations("EXP()>0");
        AssertFormulaArithmeticContrastLocations("EXP($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("EXP(\"5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("EXP($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("EXP(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("EXP(1000)>0");
        AssertFormulaArithmeticContrastLocations("LN()>0");
        AssertFormulaArithmeticContrastLocations("LN($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("LN(0)>0");
        AssertFormulaArithmeticContrastLocations("LN(-$A1)>0");
        AssertFormulaArithmeticContrastLocations("LN(\"5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("LN($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("LN(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("LN(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("LN(EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("LOG10()>0");
        AssertFormulaArithmeticContrastLocations("LOG10($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("LOG10(0)>0");
        AssertFormulaArithmeticContrastLocations("LOG10(-$A1)>0");
        AssertFormulaArithmeticContrastLocations("LOG10(\"5\")>0", "B1", "B2", "B3", "B4");
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
        AssertFormulaArithmeticContrastLocations("LOG(\"5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("LOG($A1,\"10\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("LOG($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("LOG(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("LOG($A1,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("LOG(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("LOG(EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("LOG($A1,EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("DEGREES()>0");
        AssertFormulaArithmeticContrastLocations("DEGREES($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("DEGREES(\"5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("DEGREES($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("DEGREES(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("DEGREES(1E308)>0");
        AssertFormulaArithmeticContrastLocations("RADIANS()>0");
        AssertFormulaArithmeticContrastLocations("RADIANS($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("RADIANS(\"5\")>0", "B1", "B2", "B3", "B4");
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
        AssertFormulaArithmeticContrastLocations("SINH(\"5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("SINH($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("SINH(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("SINH(1E308)>0");
        AssertFormulaArithmeticContrastLocations("SINH(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ASINH()>0");
        AssertFormulaArithmeticContrastLocations("ASINH($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("ASINH(\"5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ASINH($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("ASINH(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("ASINH(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ASINH(EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("ACOSH()>0");
        AssertFormulaArithmeticContrastLocations("ACOSH($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("ACOSH(\"5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ACOSH($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("ACOSH(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("ACOSH(0)>0");
        AssertFormulaArithmeticContrastLocations("ACOSH(-1)>0");
        AssertFormulaArithmeticContrastLocations("ACOSH(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ACOSH(EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("COSH()>0");
        AssertFormulaArithmeticContrastLocations("COSH($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("COSH(\"5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("COSH($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("COSH(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("COSH(1E308)>0");
        AssertFormulaArithmeticContrastLocations("COSH(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("TANH()>0");
        AssertFormulaArithmeticContrastLocations("TANH($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("TANH(\"5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("TANH($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("TANH(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("TANH(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ATANH()>0");
        AssertFormulaArithmeticContrastLocations("ATANH($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("ATANH(\"0.5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ATANH($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("ATANH(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("ATANH(1)>0");
        AssertFormulaArithmeticContrastLocations("ATANH(-1)>0");
        AssertFormulaArithmeticContrastLocations("ATANH(2)>0");
        AssertFormulaArithmeticContrastLocations("ATANH(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ATANH(EXP(1000))>0");
        AssertFormulaArithmeticContrastLocations("ASIN()>0");
        AssertFormulaArithmeticContrastLocations("ASIN($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("ASIN(\"0.5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ASIN($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("ASIN(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("ASIN(2)>0");
        AssertFormulaArithmeticContrastLocations("ASIN(-2)>0");
        AssertFormulaArithmeticContrastLocations("ASIN(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ACOS()>0");
        AssertFormulaArithmeticContrastLocations("ACOS($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("ACOS(\"0.5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ACOS($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("ACOS(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("ACOS(2)>0");
        AssertFormulaArithmeticContrastLocations("ACOS(-2)>0");
        AssertFormulaArithmeticContrastLocations("ACOS(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ATAN()>0");
        AssertFormulaArithmeticContrastLocations("ATAN($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("ATAN(\"5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ATAN($A1&\"x\")>0");
        AssertFormulaArithmeticContrastLocations("ATAN(KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("ATAN(1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ATAN2()>0");
        AssertFormulaArithmeticContrastLocations("ATAN2($A1)>0");
        AssertFormulaArithmeticContrastLocations("ATAN2($A1,1,2)>0");
        AssertFormulaArithmeticContrastLocations("ATAN2(\"5\",$A1)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ATAN2($A1,\"5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("ATAN2($A1&\"x\",1)>0");
        AssertFormulaArithmeticContrastLocations("ATAN2(KURT($A1),1)>0");
        AssertFormulaArithmeticContrastLocations("ATAN2($A1,KURT($A1))>0");
        AssertFormulaArithmeticContrastLocations("ATAN2(1E308*1E308,$A1)>0");
        AssertFormulaArithmeticContrastLocations("ATAN2($A1,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("ATAN2(0,0)>0");
        AssertFormulaArithmeticContrastLocations("ATAN2($A1-$A1,0)>0");
        AssertFormulaArithmeticContrastLocations("COS()>0");
        AssertFormulaArithmeticContrastLocations("COS($A1,1)>0");
        AssertFormulaArithmeticContrastLocations("COS(\"5\")>0", "B1", "B2", "B3", "B4");
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
        AssertFormulaArithmeticContrastLocations("DELTA()>0");
        AssertFormulaArithmeticContrastLocations("DELTA($A1,75,0)>0");
        AssertFormulaArithmeticContrastLocations("DELTA(\"75\",75)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("DELTA(1E308*1E308,0)>0");
        AssertFormulaArithmeticContrastLocations("GESTEP()>0");
        AssertFormulaArithmeticContrastLocations("GESTEP($A1,100,0)>0");
        AssertFormulaArithmeticContrastLocations("GESTEP(\"100\",100)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("GESTEP($A1,1E308*1E308)>0");
        AssertFormulaArithmeticContrastLocations("BITAND($A1)>0");
        AssertFormulaArithmeticContrastLocations("BITAND($A1,1,2)>0");
        AssertFormulaArithmeticContrastLocations("BITAND(1.5,1)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("BITAND($A1,1.5)>0", "B1", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("BITAND(1E308*1E308,1)>0");
        AssertFormulaArithmeticContrastLocations("BITOR(-1,1)>0");
        AssertFormulaArithmeticContrastLocations("BITOR(281474976710656,1)>0");
        AssertFormulaArithmeticContrastLocations("BITXOR(1,281474976710656)>0");
        AssertFormulaArithmeticContrastLocations("BITLSHIFT($A1)>0");
        AssertFormulaArithmeticContrastLocations("BITLSHIFT(281474976710655,1)>0");
        AssertFormulaArithmeticContrastLocations("BITLSHIFT(1,54)>0");
        AssertFormulaArithmeticContrastLocations("BITLSHIFT(1,1.5)>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("BITRSHIFT(1,54)>0");
        AssertFormulaArithmeticContrastLocations("BITRSHIFT(1,-54)>0");
        AssertFormulaArithmeticContrastLocations("BITRSHIFT(KURT($A1),1)>0");
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
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTextNumberFormattingFunctionOperands()
    {
        AssertFormulaTextFunctionContrastLocations("TEXT($A1,\"0\")=\"100\"", "B2");
        AssertFormulaTextFunctionContrastLocations("TEXT($A1/100,\"0.0%\")=\"75.0%\"", "B1", "B3");
        AssertFormulaTextFunctionContrastLocations("TEXT(45292,\"m/d/yyyy\")=\"1/1/2024\"", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("FIXED($A1/10,1,TRUE)=\"7.5\"", "B1", "B3");
        AssertFormulaTextFunctionContrastLocations("FIXED(-1234.56,1,FALSE)=\"-1,234.6\"", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("DOLLAR($A1,0)=\"$100\"", "B2");
        AssertFormulaTextFunctionContrastLocations("DOLLAR(-1234.56,1)=\"($1,234.6)\"", FormulaTextFunctionAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTextScalarConcatAndReplaceOperands()
    {
        AssertFormulaTextFunctionContrastLocations("CONCAT($C1,\"x\")=\"Openx\"", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("CONCAT(LEFT($C1,1),RIGHTB($C1,1))=\"Cd\"", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("CONCATENATE($C1,\"!\")=\"Closed!\"", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("TEXTJOIN(\"-\",TRUE,$C1,$D1)=\"Closed-East\"", "B1");
        AssertFormulaTextFunctionContrastLocations("SUBSTITUTE($C1,\"Closed\",\"Done\")=\"Done\"", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("SUBSTITUTE(\"open open\",\"open\",\"shut\",2)=\"open shut\"", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("REPLACE($C1,1,1,\"X\")=\"Xlosed\"", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("REPLACEB($C1,1,1,\"X\")=\"Xlosed\"", "B1", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTextScalarByteOperands()
    {
        AssertFormulaTextFunctionContrastLocations("LENB($C1)=6", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("LEFTB($C1,1)=\"C\"", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("LEFTB($C1)=\"O\"", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("RIGHTB($C1,1)=\"n\"", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("MIDB($C1,2,3)=\"los\"", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("FINDB(\"los\",$C1)>1", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("FINDB(\"\",$C1,2)=2", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("SEARCHB(\"op*\",$C1)=1", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTextScalarRangeOperands()
    {
        AssertFormulaTextFunctionContrastLocations("CONCAT($C$3:$C$4)=\"OpenOpen\"", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("COUNTA(CONCATENATE($C$1:$C$2,\"!\"))=2", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("TEXTJOIN(\"\",TRUE,$C$3:$C$4)=\"OpenOpen\"", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("SUM(LENB($C$3:$C$4))=8", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("SUM(FINDB(\"o\",$C$1:$C$2))=6", FormulaTextFunctionAllLocations);
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
        AssertFormulaTextFunctionContrastLocations("AND(TEXT($A1,\"0\")=\"125\",$C1=\"Open\")", "B4");
        AssertFormulaTextFunctionContrastLocations("IF($A1>=100,LEN($C1),FALSE)", "B2", "B4");
        AssertFormulaTextFunctionContrastLocations("IF($A1>=100,MID($C1,1,1)=\"O\",FALSE)", "B4");
        AssertFormulaTextFunctionContrastLocations("IF(EXACT(FIXED($A1,0),\"75\"),TRUE,FALSE)", "B1", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTextFunctionPredicates()
    {
        AssertFormulaTextFunctionContrastLocations("ISTEXT(LEFT($C1,1))", "B1", "B2", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("ISTEXT(MID($C1,2,2))", "B1", "B2", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("ISTEXT(DOLLAR($A1,0))", "B1", "B2", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("ISNUMBER(LEN($C1))", "B1", "B2", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTextFunctionArithmeticAndAggregateArguments()
    {
        AssertFormulaTextFunctionContrastLocations("LEN($C1)+1>5", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("LEN(MID($C1,2,3))=3", "B1", "B2", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("COUNTA(LEFT($C1,1))>0", "B1", "B2", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("COUNTA(MID($C1,1,1))>0", "B1", "B2", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("COUNTA(TEXT($A$1:$A$2,\"0\"))=2", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("TEXT($A1+$A2,\"0\")=\"175\"", "B1", "B2");
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
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTextScalarCharCodeOperands()
    {
        AssertFormulaTextFunctionContrastLocations("CHAR($A1)=\"K\"", "B1", "B3");
        AssertFormulaTextFunctionContrastLocations("CHAR(65.9)=\"A\"", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("CHAR(128)=\"\u20AC\"", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("CODE($C1)=67", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("CODE(42)=52", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("CODE(TRUE)=84", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("CODE(\"\u20AC\")=128", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("CODE(\"\u0100\")=63", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("CODE(CHAR(128))=128", FormulaTextFunctionAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTextScalarProperReptCleanTOperands()
    {
        AssertFormulaTextFunctionContrastLocations("PROPER($C1)=\"Open\"", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("PROPER(\"2-way street\")=\"2-Way Street\"", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("PROPER(42)=\"42\"", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("REPT(LEFT($C1,1),2)=\"CC\"", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("REPT(\"x\",0.9)=\"\"", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("REPT(\"ab\",3.9)=\"ababab\"", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("CLEAN(CHAR(1))=\"\"", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("CLEAN(TRUE)=\"TRUE\"", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("T($D1)=\"East\"", "B1");
        AssertFormulaTextFunctionContrastLocations("T($D1)=\"\"", "B2", "B3");
        AssertFormulaTextFunctionContrastLocations("T(42)=\"\"", FormulaTextFunctionAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatModernTextExtractAndSplitOperands()
    {
        AssertFormulaTextFunctionContrastLocations("TEXTBEFORE($C1,\"s\")=\"Clo\"", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("TEXTAFTER($C1,\"O\")=\"pen\"", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("TEXTBEFORE($C1,\"z\",,,,\"missing\")=\"missing\"", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("TEXTSPLIT($C1,\"-\")=\"Closed\"", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("TEXTSPLIT(\"Closed\",\"-\")=\"Closed\"", FormulaTextFunctionAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatModernTextRepresentationOperands()
    {
        AssertFormulaTextFunctionContrastLocations("ASC($C1)=$C1", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("DBCS(UNICHAR(65313))=UNICHAR(65313)", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("JIS(UNICHAR(65313))=UNICHAR(65313)", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("BAHTTEXT($A1)=BAHTTEXT(75)", "B1", "B3");
        AssertFormulaTextFunctionContrastLocations("VALUETOTEXT($D1,0)=\"East\"", "B1");
        AssertFormulaTextFunctionContrastLocations("ARRAYTOTEXT($C$1:$C$2,0)=\"Closed, Closed\"", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("PHONETIC($C1:$D1)=\"Closed\"", "B1", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatRegexTextOperands()
    {
        AssertFormulaTextFunctionContrastLocations("REGEXTEST($C1,\"^O\")", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("REGEXTEST($C1,\"^o\",1)", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("REGEXEXTRACT($C1,\"[A-Z][a-z]+\")=\"Closed\"", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("REGEXREPLACE($C1,\"[aeiou]\",\"*\")=\"Cl*s*d\"", "B1", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatXmlAndUrlTextOperands()
    {
        AssertFormulaTextFunctionContrastLocations("ENCODEURL(\"a b\")=\"a%20b\"", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ENCODEURL($D1)=\"West\"", "B4");
        AssertFormulaTextFunctionContrastLocations("FILTERXML(\"<root><item>Closed</item></root>\",\"/root/item\")=\"Closed\"", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("FILTERXML(\"<root><item>A</item><item>B</item></root>\",\"/root/item[2]\")=\"B\"", FormulaTextFunctionAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatModernTextErrorPredicates()
    {
        AssertFormulaTextFunctionContrastLocations("ISERROR(TEXTBEFORE($C1,\"z\"))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(TEXTSPLIT($C1,))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(BAHTTEXT(\"Open\"))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISNA(REGEXEXTRACT($C1,\"z\"))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(REGEXTEST($C1,\"[\"))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(FILTERXML(\"<root>\",\"/root\"))", FormulaTextFunctionAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTextScalarsInWrappersPredicatesAndAggregates()
    {
        AssertFormulaTextFunctionContrastLocations("AND(PROPER($C1)=\"Open\",$A1>=100)", "B4");
        AssertFormulaTextFunctionContrastLocations("IF(T($D1)=\"\",TRUE,FALSE)", "B2", "B3");
        AssertFormulaTextFunctionContrastLocations("ISTEXT(CHAR($A1))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISNUMBER(CODE($C1))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("CODE(CHAR($A1))+1=76", "B1", "B3");
        AssertFormulaTextFunctionContrastLocations("SUM(CODE($C1),1)=68", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("SUM(CODE($C$1:$C$2))=134", FormulaTextFunctionAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTextScalarErrorPredicates()
    {
        AssertFormulaTextFunctionContrastLocations("ISERROR(CHAR(0))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(CHAR(256.9))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(CHAR(\"x\"))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(CODE(\"\"))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(REPT(\"x\",-0.5))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(REPT(\"x\",32768))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(REPT(\"\uD83D\uDE00\",32768))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISTEXT(REPT(\"\uD83D\uDE00\",32767))");
        AssertFormulaTextFunctionContrastLocations("ISNA(CODE(NA()))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISNA(PROPER(NA()))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISNA(REPT(\"x\",NA()))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISNA(CLEAN(NA()))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISNA(T(NA()))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISNA(CONCAT(NA(),\"x\"))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISNA(TEXTJOIN(\"\",TRUE,NA()))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISNA(TEXT(NA(),\"0\"))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(FIXED(\"Open\"))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(FIXED(1,32768))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(DOLLAR(\"Open\"))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(DOLLAR(1,32768))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(SUBSTITUTE($C1,\"C\",\"x\",0))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(REPLACE($C1,99,1,\"x\"))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(REPLACEB($C1,99,1,\"x\"))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISNA(LENB(NA()))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(LEFTB($C1,-1))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(RIGHTB($C1,-1))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(MIDB($C1,0,1))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(FINDB(\"x\",$C1))", FormulaTextFunctionAllLocations);
        AssertFormulaTextFunctionContrastLocations("ISERROR(SEARCHB(\"z\",$C1))", FormulaTextFunctionAllLocations);
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatTextFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaTextFunctionContrastLocations("LEN($C1,1)>0");
        AssertFormulaTextFunctionContrastLocations("UPPER()=\"OPEN\"");
        AssertFormulaTextFunctionContrastLocations("LEFT($C1,-1)=\"\"");
        AssertFormulaTextFunctionContrastLocations("LEFT($C1,999999)=\"Closed\"", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("LEFT($C1,1.5)=\"C\"", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("MID($C1,0,1)=\"\"");
        AssertFormulaTextFunctionContrastLocations("MID($C1,1.5,1)=\"\"");
        AssertFormulaTextFunctionContrastLocations("MID($C1,999999,1)=\"\"", "B1", "B2", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("MID($C1,1,-1)=\"\"");
        AssertFormulaTextFunctionContrastLocations("MID($C1,1,1.5)=\"C\"", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("MID($C1,1,999999)=\"Closed\"", "B1", "B2");
        AssertFormulaTextFunctionContrastLocations("MID($A1,1,1)=\"7\"", "B1", "B3");
        AssertFormulaTextFunctionContrastLocations("MID($C1&\"x\",1,1)=\"O\"", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("MID($C1,1)=\"C\"");
        AssertFormulaTextFunctionContrastLocations("LEN($A1)>0", "B1", "B2", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("LEFT($C1&\"x\",1)=\"O\"", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("LEN(A0)>0");
        AssertFormulaTextFunctionContrastLocations("CHAR()>0");
        AssertFormulaTextFunctionContrastLocations("CHAR($A1,1)>0");
        AssertFormulaTextFunctionContrastLocations("CHAR(0)>0");
        AssertFormulaTextFunctionContrastLocations("CHAR(256)>0");
        AssertFormulaTextFunctionContrastLocations("CHAR(FALSE)>0");
        AssertFormulaTextFunctionContrastLocations("CODE()>0");
        AssertFormulaTextFunctionContrastLocations("CODE($C1,1)>0");
        AssertFormulaTextFunctionContrastLocations("CODE(\"\")>0");
        AssertFormulaTextFunctionContrastLocations("PROPER()>0");
        AssertFormulaTextFunctionContrastLocations("PROPER($C1,1)>0");
        AssertFormulaTextFunctionContrastLocations("REPT(\"x\")=\"x\"");
        AssertFormulaTextFunctionContrastLocations("REPT(\"x\",1,1)=\"x\"");
        AssertFormulaTextFunctionContrastLocations("REPT(\"x\",-1)=\"\"");
        AssertFormulaTextFunctionContrastLocations("REPT(\"x\",1E308*1E308)=\"\"");
        AssertFormulaTextFunctionContrastLocations("CLEAN()>0");
        AssertFormulaTextFunctionContrastLocations("CLEAN($C1,1)>0");
        AssertFormulaTextFunctionContrastLocations("T()>0");
        AssertFormulaTextFunctionContrastLocations("T($C1,1)>0");
        AssertFormulaTextFunctionContrastLocations("TEXT($A1)>0");
        AssertFormulaTextFunctionContrastLocations("TEXT($A1,\"0\",1)>0");
        AssertFormulaTextFunctionContrastLocations("TEXT($A$1:$A$2,$C$1:$D$1)=\"75\"");
        AssertFormulaTextFunctionContrastLocations("FIXED()>0");
        AssertFormulaTextFunctionContrastLocations("FIXED($A1,0,FALSE,0)>0");
        AssertFormulaTextFunctionContrastLocations("FIXED($A$1:$A$2,$A$3:$A$4,TRUE)=\"75\"");
        AssertFormulaTextFunctionContrastLocations("DOLLAR()>0");
        AssertFormulaTextFunctionContrastLocations("DOLLAR($A1,0,0)>0");
        AssertFormulaTextFunctionContrastLocations("DOLLAR($A$1:$A$2,$A$3:$A$4)=\"$75\"");
        AssertFormulaTextFunctionContrastLocations("DOLLAR($C1)>0");
        AssertFormulaTextFunctionContrastLocations("TEXTBEFORE($C1,\"s\",0)=\"\"");
        AssertFormulaTextFunctionContrastLocations("TEXTSPLIT($C1,\"e\")=\"Clos\"");
        AssertFormulaTextFunctionContrastLocations("ASC(UNICHAR(65313))=\"A\"");
        AssertFormulaTextFunctionContrastLocations("DBCS($C1)=$C1", "B1", "B2", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("JIS($C1)=$C1", "B1", "B2", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("REGEXEXTRACT($C1,\"[a-z]\",1)=\"l\"");
        AssertFormulaTextFunctionContrastLocations("FILTERXML(\"<root><item>A</item><item>B</item></root>\",\"/root/item\")=\"A\"");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatTextSearchFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaTextFunctionContrastLocations("FIND(\"x\",$C1)>0");
        AssertFormulaTextFunctionContrastLocations("SEARCH(\"o\",$C1,0)>0");
        AssertFormulaTextFunctionContrastLocations("SEARCH(\"o\",$C1,1.5)>0", "B1", "B2", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("SEARCH(\"o\",$C1,999999)>0");
        AssertFormulaTextFunctionContrastLocations("SEARCH(\"\",$C1)>0", "B1", "B2", "B3", "B4");
        AssertFormulaTextFunctionContrastLocations("FIND(\"o\")>0");
        AssertFormulaTextFunctionContrastLocations("EXACT($C1)>0");
        AssertFormulaTextFunctionContrastLocations("SEARCH(\"o\",$A1)>0");
        AssertFormulaTextFunctionContrastLocations("SEARCH(\"o\",$C1&\"x\")>0", "B1", "B2", "B3", "B4");
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
        AssertFormulaValueFunctionContrastLocations("IF(VALUE($C1)>0,TRUE,FALSE)", "B1", "B2", "B3", "B7");
        AssertFormulaValueFunctionContrastLocations("AND(VALUE($C1)>0,$A1)", "B1", "B2", "B7");
        AssertFormulaValueFunctionContrastLocations("ISNUMBER(VALUE($C1))", "B1", "B2", "B3", "B4", "B7");
        AssertFormulaValueFunctionContrastLocations("VALUE($C1)+1=100.5", "B1");
        AssertFormulaValueFunctionContrastLocations("SUM(VALUE($C1),1)>1000", "B2");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatValueFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaValueFunctionContrastLocations("VALUE()>0");
        AssertFormulaValueFunctionContrastLocations("VALUE($C1,1)>0");
        AssertFormulaValueFunctionContrastLocations("VALUE($A1)>0");
        AssertFormulaValueFunctionContrastLocations("VALUE(42)>0", "B1", "B2", "B3", "B4", "B5", "B6", "B7");
        AssertFormulaValueFunctionContrastLocations("VALUE(\"Open\")>0");
        AssertFormulaValueFunctionContrastLocations("VALUE(\"\")>0");
        AssertFormulaValueFunctionContrastLocations("VALUE(\"1E309\")>0", "B1", "B2", "B3", "B4", "B5", "B6", "B7");
        AssertFormulaValueFunctionContrastLocations("VALUE(\"50%%\")>0", "B1", "B2", "B3", "B4", "B5", "B6", "B7");
        AssertFormulaValueFunctionContrastLocations("VALUE($C1&\"x\")>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatNumberValueFunctionOperands()
    {
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE($C1)=1234.56", "B1");
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE($C1,$D1,$E1)=1234.56", "B1", "B2");
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE(\"1;25\",\";\")=1.25", FormulaNumberValueAllLocations);
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE(\"1 234.5\",\".\",\" \")=1234.5", FormulaNumberValueAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatNumberValueFunctionPercentAccountingAndWhitespace()
    {
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE($C1)=0.5", "B3");
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE($C1)<0", "B4");
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE($C1)=1234", "B5");
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE(\"10%%\")>0", FormulaNumberValueAllLocations);
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE(\" ( 1,234.5%) \")<0", FormulaNumberValueAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatNumberValueFunctionWrappersPredicatesAndAggregates()
    {
        AssertFormulaNumberValueFunctionContrastLocations("IF(NUMBERVALUE($C1,$D1,$E1)>0,TRUE,FALSE)", "B1", "B2", "B3", "B5", "B6");
        AssertFormulaNumberValueFunctionContrastLocations("AND(NUMBERVALUE($C1,$D1,$E1)>0,$A1)", "B1", "B2", "B5", "B6");
        AssertFormulaNumberValueFunctionContrastLocations("ISNUMBER(NUMBERVALUE($C1,$D1,$E1))", "B1", "B2", "B3", "B4", "B5", "B6");
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE($C1)+1=76", "B6");
        AssertFormulaNumberValueFunctionContrastLocations("SUM(NUMBERVALUE($C1,$D1,$E1),1)>1235", "B1", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatNumberValueFunctionErrorPredicates()
    {
        AssertFormulaNumberValueFunctionContrastLocations("ISERROR(NUMBERVALUE(\"Open\"))", FormulaNumberValueAllLocations);
        AssertFormulaNumberValueFunctionContrastLocations("ISERROR(NUMBERVALUE(\"1234\",\".\",\".\"))", FormulaNumberValueAllLocations);
        AssertFormulaNumberValueFunctionContrastLocations("ISERROR(NUMBERVALUE($C1,$D1,$E1))", "B7", "B8");
        AssertFormulaNumberValueFunctionContrastLocations("ISNA(NUMBERVALUE(NA()))", FormulaNumberValueAllLocations);
        AssertFormulaNumberValueFunctionContrastLocations("ISNA(NUMBERVALUE(\"123\",NA(),\",\"))", FormulaNumberValueAllLocations);
        AssertFormulaNumberValueFunctionContrastLocations("ISNA(NUMBERVALUE(\"123\",\".\",NA()))", FormulaNumberValueAllLocations);
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatNumberValueFunctionUnsupportedOperandsOrErrorComparisons()
    {
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE()>0");
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE($C1,$D1,$E1,1)>0");
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE(\"Open\")>0");
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE(\"1.234,56\",\".\",\",\")>0");
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE(\"1234\",\".\",\".\")>0");
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE(\"1234\",\"\",\",\")>0");
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE(\"1234\",\".\",\"\")>0");
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE(\"1E309\")>0");
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE(NA())>0");
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE(\"123\",NA(),\",\")>0");
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE(\"123\",\".\",NA())>0");
        AssertFormulaNumberValueFunctionContrastLocations("NUMBERVALUE($C1:$C2)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDateValueFunctionOperands()
    {
        AssertFormulaDateValueTimeValueFunctionContrastLocations("DATEVALUE($A1)=1", "B1");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("DATEVALUE($A1)=60", "B2", "B3");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("DATEVALUE(\"3/1/1900\")=61", FormulaDateValueTimeValueAllLocations);
        AssertFormulaDateValueTimeValueFunctionContrastLocations("DATEVALUE($A1)=45292", "B4", "B5");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("DATEVALUE($A1)=45306", "B6");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("DATEVALUE($A1)=45293", "B7");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTimeValueFunctionOperands()
    {
        AssertFormulaDateValueTimeValueFunctionContrastLocations("TIMEVALUE($C1)=0", "B1", "B8");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("TIMEVALUE($C1)=0.5", "B2", "B6");
        AssertFormulaDateValueTimeValueFunctionContrastLocations(
            "ABS(TIMEVALUE($C1)-0.999988425925926)<0.000000000001",
            "B3",
            "B5");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("TIMEVALUE($C1)=0.25", "B4", "B7");
        AssertFormulaDateValueTimeValueFunctionContrastLocations(
            "ABS(TIMEVALUE(\"1900-02-29 23:59:59\")-0.999988425925926)<0.000000000001",
            FormulaDateValueTimeValueAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDateValueTimeValueWrappersPredicatesAndAggregates()
    {
        AssertFormulaDateValueTimeValueFunctionContrastLocations("IF(DATEVALUE($A1)>60,TRUE,FALSE)", "B4", "B5", "B6", "B7");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("AND(DATEVALUE($A1)>60,$D1)", "B4", "B5", "B7");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("ISNUMBER(DATEVALUE($A1))", "B1", "B2", "B3", "B4", "B5", "B6", "B7");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("DATEVALUE($A1)+1=45294", "B7");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("SUM(DATEVALUE($A1),1)=45307", "B6");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("IF(TIMEVALUE($C1)>=0.5,TRUE,FALSE)", "B2", "B3", "B5", "B6");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("AND(ISNUMBER(TIMEVALUE($C1)),TIMEVALUE($C1)<0.5)", "B1", "B4", "B7", "B8");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("TIMEVALUE($C1)*24=6", "B4", "B7");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("SUM(TIMEVALUE($C1),0.75)=1", "B4", "B7");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatDateValueTimeValueErrorPredicates()
    {
        AssertFormulaDateValueTimeValueFunctionContrastLocations("ISERROR(DATEVALUE(\"12:00 PM\"))", FormulaDateValueTimeValueAllLocations);
        AssertFormulaDateValueTimeValueFunctionContrastLocations("ISERROR(TIMEVALUE(\"2024-01-02\"))");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("ISERROR(DATEVALUE(\"Open\"))", FormulaDateValueTimeValueAllLocations);
        AssertFormulaDateValueTimeValueFunctionContrastLocations("ISNA(TIMEVALUE(NA()))", FormulaDateValueTimeValueAllLocations);
        AssertFormulaDateValueTimeValueFunctionContrastLocations("ISNA(DATEVALUE(NA()))", FormulaDateValueTimeValueAllLocations);
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatDateValueTimeValueUnsupportedOperands()
    {
        AssertFormulaDateValueTimeValueFunctionContrastLocations("DATEVALUE()>0");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("DATEVALUE($A1,1)>0");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("DATEVALUE($D1)>0");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("DATEVALUE(42)>0");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("DATEVALUE(\"12:00 PM\")>0");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("DATEVALUE(\"23:59:59\")>0");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("DATEVALUE(\"Open\")>0");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("DATEVALUE(\"\")>0");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("DATEVALUE(#VALUE!)>0");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("DATEVALUE($A1&\"x\")>0");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("TIMEVALUE()>0");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("TIMEVALUE($C1,1)>0");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("TIMEVALUE($D1)>0");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("TIMEVALUE(0.5)>0");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("TIMEVALUE(\"1/2/2024\")>0");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("TIMEVALUE(\"2024-01-02\")>0");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("TIMEVALUE(\"2/29/1900\")>0");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("TIMEVALUE(\"Open\")>0");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("TIMEVALUE(\"\")>0");
        AssertFormulaDateValueTimeValueFunctionContrastLocations("TIMEVALUE(#N/A)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatArabicFunctionOperands()
    {
        AssertFormulaArabicRomanFunctionContrastLocations("ARABIC($C1)=12", "B1");
        AssertFormulaArabicRomanFunctionContrastLocations("ARABIC($C1)=-4", "B2");
        AssertFormulaArabicRomanFunctionContrastLocations("ARABIC($C1)=0", "B3");
        AssertFormulaArabicRomanFunctionContrastLocations("ARABIC($C1)=1999", "B4");
        AssertFormulaArabicRomanFunctionContrastLocations("ARABIC($C1)=99", "B5");
        AssertFormulaArabicRomanFunctionContrastLocations("ARABIC(\"  xm  \")=990", FormulaArabicRomanAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatArabicFunctionWrappersPredicatesAndAggregates()
    {
        AssertFormulaArabicRomanFunctionContrastLocations("ARABIC($C1)+1=13", "B1");
        AssertFormulaArabicRomanFunctionContrastLocations("SUM(ARABIC($C1),1)=100", "B5");
        AssertFormulaArabicRomanFunctionContrastLocations("ISNUMBER(ARABIC($C1))", "B1", "B2", "B3", "B4", "B5");
        AssertFormulaArabicRomanFunctionContrastLocations("IF(ARABIC($C1),TRUE,FALSE)", "B1", "B2", "B4", "B5");
        AssertFormulaArabicRomanFunctionContrastLocations("AND(ARABIC($C1)>0,$A1)", "B1", "B4", "B5");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatArabicFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArabicRomanFunctionContrastLocations("ARABIC()>0");
        AssertFormulaArabicRomanFunctionContrastLocations("ARABIC($C1,1)>0");
        AssertFormulaArabicRomanFunctionContrastLocations("ARABIC($A1)>0");
        AssertFormulaArabicRomanFunctionContrastLocations("ARABIC(42)>0");
        AssertFormulaArabicRomanFunctionContrastLocations("ARABIC(TRUE)>0");
        AssertFormulaArabicRomanFunctionContrastLocations("ARABIC(\"IIV\")>0");
        AssertFormulaArabicRomanFunctionContrastLocations("ARABIC(\"-   \")>0");
        AssertFormulaArabicRomanFunctionContrastLocations("ARABIC(\"MMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMMM\")>0");
        AssertFormulaArabicRomanFunctionContrastLocations("ARABIC($C1&\"X\")>0", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatRomanFunctionOperands()
    {
        AssertFormulaArabicRomanFunctionContrastLocations("ROMAN($D1)=\"XII\"", "B1");
        AssertFormulaArabicRomanFunctionContrastLocations("ROMAN($D1,TRUE)=\"XLIX\"", "B2");
        AssertFormulaArabicRomanFunctionContrastLocations("ROMAN($D1,FALSE)=\"IL\"", "B2");
        AssertFormulaArabicRomanFunctionContrastLocations("ROMAN($D1)=\"\"", "B3");
        AssertFormulaArabicRomanFunctionContrastLocations("ROMAN($D1,4)=\"IM\"", "B4");
        AssertFormulaArabicRomanFunctionContrastLocations("ROMAN(944,2)=\"CMXLIV\"", FormulaArabicRomanAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatRomanFunctionWrappersPredicatesAndTextComparisons()
    {
        AssertFormulaArabicRomanFunctionContrastLocations("EXACT(ROMAN($D1,4),\"IM\")", "B4");
        AssertFormulaArabicRomanFunctionContrastLocations("ISTEXT(ROMAN($D1))", "B1", "B2", "B3", "B4", "B5", "B7");
        AssertFormulaArabicRomanFunctionContrastLocations("AND(EXACT(ROMAN($D1),\"XII\"),$A1)", "B1");
        AssertFormulaArabicRomanFunctionContrastLocations("IF(EXACT(ROMAN($D1,4),\"IL\"),TRUE,FALSE)", "B2");
        AssertFormulaArabicRomanFunctionContrastLocations("ROMAN(SUM($D1,0),4)=\"CCLV\"", "B7");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatRomanFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArabicRomanFunctionContrastLocations("ROMAN()=\"\"");
        AssertFormulaArabicRomanFunctionContrastLocations("ROMAN($D1,1,1)=\"\"");
        AssertFormulaArabicRomanFunctionContrastLocations("ROMAN($C1)=\"XII\"");
        AssertFormulaArabicRomanFunctionContrastLocations("ROMAN($D1,5)=\"XII\"");
        AssertFormulaArabicRomanFunctionContrastLocations("ROMAN($D1,-1)=\"XII\"");
        AssertFormulaArabicRomanFunctionContrastLocations("ROMAN($D1,\"4\")=\"XII\"", "B1");
        AssertFormulaArabicRomanFunctionContrastLocations("ROMAN(4000)=\"MMMM\"");
        AssertFormulaArabicRomanFunctionContrastLocations("ROMAN(-1)=\"\"");
        AssertFormulaArabicRomanFunctionContrastLocations("ROMAN(1E308*1E308)=\"\"");
        AssertFormulaArabicRomanFunctionContrastLocations("ROMAN(A0)=\"\"");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatUnicharFunctionOperands()
    {
        AssertFormulaUnicodeFunctionContrastLocations("UNICHAR($A1)=\"A\"", "B1");
        AssertFormulaUnicodeFunctionContrastLocations("UNICHAR($A1)=\"\u2603\"", "B2");
        AssertFormulaUnicodeFunctionContrastLocations("EXACT(UNICHAR($A1),UNICHAR(128512))", "B3");
        AssertFormulaUnicodeFunctionContrastLocations("UNICODE(UNICHAR($A1))=$A1", "B2", "B3");
        AssertFormulaUnicodeFunctionContrastLocations("UNICHAR(65.9)=\"A\"", FormulaUnicodeAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatUnicodeFunctionOperands()
    {
        AssertFormulaUnicodeFunctionContrastLocations("UNICODE($C1)=65", "B1");
        AssertFormulaUnicodeFunctionContrastLocations("UNICODE($C1)=9731", "B2");
        AssertFormulaUnicodeFunctionContrastLocations("UNICODE($C1)=128512", "B3");
        AssertFormulaUnicodeFunctionContrastLocations("UNICODE($C1)=90", "B4");
        AssertFormulaUnicodeFunctionContrastLocations("UNICODE(65)=54", FormulaUnicodeAllLocations);
        AssertFormulaUnicodeFunctionContrastLocations("UNICODE(TRUE)=84", FormulaUnicodeAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatUnicodeFunctionsInWrappersPredicatesAndTextComparisons()
    {
        AssertFormulaUnicodeFunctionContrastLocations("IF(UNICODE($C1)=128512,TRUE,FALSE)", "B3");
        AssertFormulaUnicodeFunctionContrastLocations("AND(UNICODE($C1)>64,$D1)", "B1", "B2", "B3", "B4", "B7");
        AssertFormulaUnicodeFunctionContrastLocations("ISNUMBER(UNICODE($C1))", "B1", "B2", "B3", "B4", "B6", "B7");
        AssertFormulaUnicodeFunctionContrastLocations("ISTEXT(UNICHAR($A1))", "B1", "B2", "B3");
        AssertFormulaUnicodeFunctionContrastLocations("EXACT(UNICHAR($A1),LEFT($C1,2))", "B3");
        AssertFormulaUnicodeFunctionContrastLocations("SUM(UNICODE($C1),1)=9732", "B2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatUnicodeFunctionErrorPredicates()
    {
        AssertFormulaUnicodeFunctionContrastLocations("ISERROR(UNICHAR(0))", FormulaUnicodeAllLocations);
        AssertFormulaUnicodeFunctionContrastLocations("ISERROR(UNICHAR(\"x\"))", FormulaUnicodeAllLocations);
        AssertFormulaUnicodeFunctionContrastLocations("ISERROR(UNICODE(\"\"))", FormulaUnicodeAllLocations);
        AssertFormulaUnicodeFunctionContrastLocations("ISERROR(UNICODE($C$8))", FormulaUnicodeAllLocations);
        AssertFormulaUnicodeFunctionContrastLocations("ISERROR(UNICODE($C$9))", FormulaUnicodeAllLocations);
        AssertFormulaUnicodeFunctionContrastLocations("ISNA(UNICODE(NA()))", FormulaUnicodeAllLocations);
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatUnicodeFunctionUnsupportedOrErrorDomainOperands()
    {
        AssertFormulaUnicodeFunctionContrastLocations("UNICHAR()>0");
        AssertFormulaUnicodeFunctionContrastLocations("UNICHAR($A1,1)=\"A\"");
        AssertFormulaUnicodeFunctionContrastLocations("UNICHAR(0)=\"\"");
        AssertFormulaUnicodeFunctionContrastLocations("UNICHAR(1114112)=\"\"");
        AssertFormulaUnicodeFunctionContrastLocations("UNICHAR(55296)=\"\"");
        AssertFormulaUnicodeFunctionContrastLocations("UNICHAR(\"x\")=\"\"");
        AssertFormulaUnicodeFunctionContrastLocations("UNICHAR(1E308*1E308)=\"\"");
        AssertFormulaUnicodeFunctionContrastLocations("UNICODE()>0");
        AssertFormulaUnicodeFunctionContrastLocations("UNICODE($C1,1)>0");
        AssertFormulaUnicodeFunctionContrastLocations("UNICODE(\"\")>0");
        AssertFormulaUnicodeFunctionContrastLocations("UNICODE(UNICHAR(0))>0");
        AssertFormulaUnicodeFunctionContrastLocations("UNICODE($C$8)>0");
        AssertFormulaUnicodeFunctionContrastLocations("UNICODE($C$9)>0");
        AssertFormulaUnicodeFunctionContrastLocations("UNICODE(NA())>0");
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
        AssertFormulaDateFunctionContrastLocations("WEEKDAY($A1)=4", "B1", "B3");
        AssertFormulaDateFunctionContrastLocations("WEEKDAY($A1,2)=4", "B2", "B4");
        AssertFormulaDateFunctionContrastLocations("WEEKDAY($A1,14)=1", "B2", "B4");
        AssertFormulaDateFunctionContrastLocations("WEEKDAY(0)=7", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("WEEKNUM($A1)=11", "B1", "B2");
        AssertFormulaDateFunctionContrastLocations("WEEKNUM($A1,2)=12", "B1", "B2", "B3");
        AssertFormulaDateFunctionContrastLocations("WEEKNUM($A1,21)=12", "B3");
        AssertFormulaDateFunctionContrastLocations("WEEKNUM(0)=0", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISOWEEKNUM($A1)=11", "B1", "B2");
        AssertFormulaDateFunctionContrastLocations("ISOWEEKNUM(DATE(2021,1,1))=53", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("WEEKNUM(DATE(2021,1,1),21)=53", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("EDATE($A1,1)>=DATE(2023,4,16)", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("EDATE(DATE(2023,1,31),1)=DATE(2023,2,28)", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("EDATE($A1,-1)<DATE(2023,3,1)", "B1", "B2");
        AssertFormulaDateFunctionContrastLocations("EDATE($A1,1.9)=EDATE($A1,1)", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("EOMONTH($A1,0)=DATE(2023,3,31)", "B1", "B2");
        AssertFormulaDateFunctionContrastLocations("EOMONTH($A1,-1)=DATE(2023,2,28)", "B1", "B2");
        AssertFormulaDateFunctionContrastLocations("DAYS($A1,DATE(2023,3,15))>0", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("DAYS(DATE(2023,3,16),$A1)=1", "B1");
        AssertFormulaDateFunctionContrastLocations("HOUR($D1)=12", "B2");
        AssertFormulaDateFunctionContrastLocations("MINUTE($D1)=5", "B3");
        AssertFormulaDateFunctionContrastLocations("SECOND($D1)=59", "B4");
        AssertFormulaDateFunctionContrastLocations("HOUR(1.25)=6", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("MINUTE(1.5242592592592593)=34", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("SECOND(1.5242592592592593)=56", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("HOUR($A1)=0", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("MINUTE(45000)=0", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("SECOND(2958465)=0", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("HOUR(TIME(12,5,59))=12", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("MINUTE(TIME(12,5,59))=5", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("SECOND(TIME(12,5,59))=59", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("TIME(12,5,59)>0.5", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ABS(TIME(25,0,0)-TIME(1,0,0))<0.000000000001", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("TIME(48,0,1)<TIME(0,0,2)", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("TIME(1.9,2.9,3.9)=TIME(1,2,3)", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("DAYS360(DATE(2023,3,15),$A1)>30", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("DAYS360(DATE(2023,2,28),DATE(2023,3,31))=30");
        AssertFormulaDateFunctionContrastLocations("DAYS360(DATE(1900,2,28),DATE(1900,3,31))=33", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("DAYS360(DATE(1900,1,30),DATE(1900,2,28))=28", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("DAYS360(DATE(2023,1,31),DATE(2023,2,28))=28", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("DAYS360(DATE(2023,4,30),DATE(2023,5,31))=30", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("DAYS360(DATE(2023,1,31),DATE(2023,2,28),TRUE)=28", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("DAYS360(DATE(2023,1,31),DATE(2023,3,31),1)=60", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("WORKDAY($A1,1)>DATE(2023,3,16)", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("WORKDAY($A1,2)=DATE(2023,3,20)", "B2");
        AssertFormulaDateFunctionContrastLocations("WORKDAY($A1,-1)<DATE(2023,3,16)", "B1", "B2");
        AssertFormulaDateFunctionContrastLocations("WORKDAY(DATE(2023,3,17),1)=DATE(2023,3,20)", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("WORKDAY(DATE(2023,3,20),-1)=DATE(2023,3,17)", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("WORKDAY(DATE(2023,3,16),1,$E$1:$E$1)=DATE(2023,3,20)", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("WORKDAY(DATE(2023,3,16),1,DATE(2023,3,17))=DATE(2023,3,20)", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS(DATE(2023,3,13),$A1)=3", "B1");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS(DATE(2023,3,17),DATE(2023,3,19))=1", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS($A1,DATE(2023,3,13))<=-4", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS(DATE(2023,3,16),DATE(2023,3,20),$E$1:$E$2)=1", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS(DATE(2023,3,16),DATE(2023,3,20),DATE(2023,3,17))=2", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("WORKDAY.INTL($A1,1)>DATE(2023,3,16)", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("WORKDAY.INTL($A1,2)=DATE(2023,3,20)", "B2");
        AssertFormulaDateFunctionContrastLocations("WORKDAY.INTL($A1,2,11)=DATE(2023,3,17)", "B1");
        AssertFormulaDateFunctionContrastLocations("WORKDAY.INTL(DATE(2023,3,17),1,\"0000001\")=DATE(2023,3,18)", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("WORKDAY.INTL(DATE(2023,3,16),1,1,$E$1:$E$1)=DATE(2023,3,20)", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("WORKDAY.INTL(DATE(2023,3,16),1,1,DATE(2023,3,17))=DATE(2023,3,20)", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS.INTL(DATE(2023,3,13),$A1)=3", "B1");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS.INTL(DATE(2023,3,17),DATE(2023,3,19),11)=2", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS.INTL(DATE(2023,3,17),DATE(2023,3,19),\"0000001\")=2", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS.INTL($A1,DATE(2023,3,13))<=-4", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS.INTL(DATE(2023,3,16),DATE(2023,3,20),1,$E$1:$E$2)=1", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS.INTL(DATE(2023,3,16),DATE(2023,3,20),1,DATE(2023,3,17))=2", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC(DATE(2023,3,15),$A1)>0.09", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC(DATE(2023,3,15),$A1,0)>0.09", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC(DATE(2023,3,15),$A1,1)>1", "B3");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC(DATE(2023,3,15),$A1,2)>1", "B3");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC(DATE(2023,3,15),$A1,3)>0.09", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC(DATE(2023,3,15),$A1,4)>0.09", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC(DATE(2023,2,28),DATE(2023,3,31))=0.08333333333333333", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC(DATE(1900,2,28),DATE(1900,3,31))=0.09166666666666666", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC(DATE(1900,1,30),DATE(1900,2,28))=0.07777777777777778", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC($A1,DATE(2023,3,15),2)<0");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC(DATE(2023,3,15),$A1,1.9)>1", "B3");
        AssertFormulaDateFunctionContrastLocations("DATEDIF($A1,DATE(2024,4,20),\"D\")>365", "B1", "B2", "B4");
        AssertFormulaDateFunctionContrastLocations("DATEDIF($A1,DATE(2024,4,20),\"M\")>=12", "B1", "B2", "B4");
        AssertFormulaDateFunctionContrastLocations("DATEDIF($A1,DATE(2024,4,20),\"Y\")=1", "B1", "B2", "B4");
        AssertFormulaDateFunctionContrastLocations("DATEDIF($A1,DATE(2024,4,20),\"YM\")=1", "B1", "B2", "B3");
        AssertFormulaDateFunctionContrastLocations("DATEDIF($A1,DATE(2024,4,20),\"YD\")>30", "B1", "B2", "B3");
        AssertFormulaDateFunctionContrastLocations("DATEDIF($A1,DATE(2024,4,20),\"MD\")=0", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("DATEDIF($A1+TIME(23,0,0),$A1+1+TIME(1,0,0),\"D\")=1", "B1", "B2", "B3", "B4");
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
        AssertFormulaDateFunctionContrastLocations("IF(WEEKDAY($A1,2)>=4,TRUE,FALSE)", "B2", "B4");
        AssertFormulaDateFunctionContrastLocations("AND(ISNUMBER(WEEKNUM($A1)),WEEKNUM($A1,21)>=12)", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISNUMBER(ISOWEEKNUM($A1))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("YEAR($A1)-2023", "B3");
        AssertFormulaDateFunctionContrastLocations("WEEKNUM($A1)+WEEKDAY($A1)=15", "B1");
        AssertFormulaDateFunctionContrastLocations("IF(EDATE($A1,1)>=DATE(2023,4,16),TRUE,FALSE)", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("AND(ISNUMBER(DAYS($A1,DATE(2023,3,15))),DAYS($A1,DATE(2023,3,15))>=36)", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISNUMBER(EOMONTH($A1,0))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("EDATE($A1,1)-DATE(2023,4,15)>0", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("IF(HOUR($D1)>=12,TRUE,FALSE)", "B2", "B4");
        AssertFormulaDateFunctionContrastLocations("AND(ISNUMBER(MINUTE($D1)),SECOND($D1)>=7)", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISNUMBER(HOUR($D1))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("HOUR($D1)", "B1", "B2", "B4");
        AssertFormulaDateFunctionContrastLocations("HOUR($D1)+MINUTE($D1)=46", "B2");
        AssertFormulaDateFunctionContrastLocations("IF(TIME(HOUR($D1),MINUTE($D1),SECOND($D1))>=0.5,TRUE,FALSE)", "B2", "B4");
        AssertFormulaDateFunctionContrastLocations("AND(ISNUMBER(TIME(12,0,0)),TIME(12,0,0))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("TIME(1,0,0)*24=1", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("IF(DAYS360(DATE(2023,3,15),$A1)>=30,TRUE,FALSE)", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("AND(ISNUMBER(DAYS360(DATE(2023,3,15),$A1)),DAYS360(DATE(2023,3,15),$A1)>=30)", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("DAYS360(DATE(2023,3,15),$A1)+1>31", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("IF(WORKDAY($A1,1)>DATE(2023,3,16),TRUE,FALSE)", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("AND(ISNUMBER(WORKDAY($A1,1)),WORKDAY($A1,2)=DATE(2023,3,20))", "B2");
        AssertFormulaDateFunctionContrastLocations("WORKDAY($A1,1)-$A1=1", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("IF(NETWORKDAYS(DATE(2023,3,13),$A1)>=4,TRUE,FALSE)", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("AND(ISNUMBER(NETWORKDAYS(DATE(2023,3,13),$A1)),NETWORKDAYS(DATE(2023,3,13),$A1)=3)", "B1");
        AssertFormulaDateFunctionContrastLocations("IF(WORKDAY.INTL($A1,1)>DATE(2023,3,16),TRUE,FALSE)", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("AND(ISNUMBER(WORKDAY.INTL($A1,1)),WORKDAY.INTL($A1,2)=DATE(2023,3,20))", "B2");
        AssertFormulaDateFunctionContrastLocations("WORKDAY.INTL($A1,1)-$A1=1", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("IF(NETWORKDAYS.INTL(DATE(2023,3,13),$A1)>=4,TRUE,FALSE)", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("AND(ISNUMBER(NETWORKDAYS.INTL(DATE(2023,3,13),$A1)),NETWORKDAYS.INTL(DATE(2023,3,13),$A1)=3)", "B1");
        AssertFormulaDateFunctionContrastLocations("IF(DATEDIF($A1,DATE(2024,4,20),\"M\")>=12,TRUE,FALSE)", "B1", "B2", "B4");
        AssertFormulaDateFunctionContrastLocations("AND(ISNUMBER(DATEDIF($A1,DATE(2024,4,20),\"Y\")),DATEDIF($A1,DATE(2024,4,20),\"Y\")=1)", "B1", "B2", "B4");
        AssertFormulaDateFunctionContrastLocations("DATEDIF($A1,DATE(2024,4,20),\"YM\")", "B1", "B2", "B3");
        AssertFormulaDateFunctionContrastLocations("DATEDIF($A1,DATE(2024,4,20),\"MD\")+1=1", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("IF(YEARFRAC(DATE(2023,3,15),$A1,1)>0.09,TRUE,FALSE)", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("AND(ISNUMBER(YEARFRAC(DATE(2023,3,15),$A1)),YEARFRAC(DATE(2023,3,15),$A1)>=1)", "B3");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC(DATE(2023,3,15),$A1)", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC(DATE(2023,3,15),$A1)*360>30", "B3", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatAggregateDateFunctionArguments()
    {
        AssertFormulaDateFunctionContrastLocations("SUM(DAY($A1),MONTH($A1))>=19", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("SUM(DAY(TODAY()),MONTH(TODAY()))>=2", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("SUM(WEEKDAY($A1),WEEKNUM($A1))=15", "B1");
        AssertFormulaDateFunctionContrastLocations("SUM(DAYS($A1,DATE(2023,3,15)),1)>=37", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("SUM(EOMONTH($A1,0),1)=45017", "B1", "B2");
        AssertFormulaDateFunctionContrastLocations("SUM(HOUR($D1),MINUTE($D1),SECOND($D1))=141", "B4");
        AssertFormulaDateFunctionContrastLocations("SUM(TIME(12,0,0),0.5)=1", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("AVERAGE(TIME(6,0,0),TIME(18,0,0))=0.5", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("SUM(DAYS360(DATE(2023,3,15),$A1),1)>31", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("AVERAGE(DAYS360(DATE(2023,3,15),$A1),1)>15", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("SUM(WORKDAY($A1,1),1)>DATE(2023,3,17)", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("AVERAGE(WORKDAY($A1,1),DATE(2023,3,16))>DATE(2023,3,16)", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("SUM(NETWORKDAYS(DATE(2023,3,13),$A1),1)>4", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("AVERAGE(NETWORKDAYS(DATE(2023,3,13),$A1),1)>2", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("SUM(WORKDAY.INTL($A1,1),1)>DATE(2023,3,17)", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("AVERAGE(WORKDAY.INTL($A1,1),DATE(2023,3,16))>DATE(2023,3,16)", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("SUM(NETWORKDAYS.INTL(DATE(2023,3,13),$A1),1)>4", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("AVERAGE(NETWORKDAYS.INTL(DATE(2023,3,13),$A1),1)>2", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("SUM(DATEDIF($A1,DATE(2024,4,20),\"D\"),1)>366", "B1", "B2", "B4");
        AssertFormulaDateFunctionContrastLocations("AVERAGE(DATEDIF($A1,DATE(2024,4,20),\"YD\"),1)>15", "B1", "B2", "B3");
        AssertFormulaDateFunctionContrastLocations("SUM(YEARFRAC(DATE(2023,3,15),$A1,2),1)>2", "B3");
        AssertFormulaDateFunctionContrastLocations("AVERAGE(YEARFRAC(DATE(2023,3,15),$A1,3),1)>0.54", "B3", "B4");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatDateFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaDateFunctionContrastLocations("DATE(2023,2,30)=$A1");
        AssertFormulaDateFunctionContrastLocations("DATE(2023,1.5,1)=$A1");
        AssertFormulaDateFunctionContrastLocations("DATE(2023,3)=$A1");
        AssertFormulaDateFunctionContrastLocations("DATE(10000,1,1)=$A1");
        AssertFormulaDateFunctionContrastLocations("YEAR(\"2023-03-15\")=2023", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("YEAR($A1,1)=2023");
        AssertFormulaDateFunctionContrastLocations("YEAR(1E308)>0");
        AssertFormulaDateFunctionContrastLocations("TODAY(1)>0");
        AssertFormulaDateFunctionContrastLocations("NOW(1)>0");
        AssertFormulaDateFunctionContrastLocations("YEAR(A0)>0");
        AssertFormulaDateFunctionContrastLocations("WEEKDAY(\"2023-03-15\")=4", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("WEEKDAY($A1,99)>0");
        AssertFormulaDateFunctionContrastLocations("WEEKDAY($A1,1,1)>0");
        AssertFormulaDateFunctionContrastLocations("WEEKDAY(-1)>0");
        AssertFormulaDateFunctionContrastLocations("WEEKDAY(2958466)>0");
        AssertFormulaDateFunctionContrastLocations("WEEKNUM(\"2023-03-15\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("WEEKNUM($A1,3)>0");
        AssertFormulaDateFunctionContrastLocations("WEEKNUM($A1,1,1)>0");
        AssertFormulaDateFunctionContrastLocations("WEEKNUM(2958466)>0");
        AssertFormulaDateFunctionContrastLocations("ISOWEEKNUM(\"2021-01-01\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISOWEEKNUM($A1,1)>0");
        AssertFormulaDateFunctionContrastLocations("ISOWEEKNUM(2958466)>0");
        AssertFormulaDateFunctionContrastLocations("EDATE($A1)>0");
        AssertFormulaDateFunctionContrastLocations("EDATE($A1,1,1)>0");
        AssertFormulaDateFunctionContrastLocations("EDATE(\"2023-03-15\",1)>0", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("EDATE($A1,1E308)>0");
        AssertFormulaDateFunctionContrastLocations("EDATE($A1,2147483648)>0");
        AssertFormulaDateFunctionContrastLocations("EDATE(2958466,1)>0");
        AssertFormulaDateFunctionContrastLocations("EDATE(A0,1)>0");
        AssertFormulaDateFunctionContrastLocations("EOMONTH($A1)>0");
        AssertFormulaDateFunctionContrastLocations("EOMONTH($A1,1,1)>0");
        AssertFormulaDateFunctionContrastLocations("EOMONTH(\"2023-03-15\",1)>0", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("EOMONTH($A1,2147483647)>0");
        AssertFormulaDateFunctionContrastLocations("EOMONTH(2958466,1)>0");
        AssertFormulaDateFunctionContrastLocations("DAYS($A1)>0");
        AssertFormulaDateFunctionContrastLocations("DAYS($A1,DATE(2023,3,15),1)>0");
        AssertFormulaDateFunctionContrastLocations("DAYS(\"2023-03-16\",DATE(2023,3,15))>0", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("DAYS($A1,2958466)>0");
        AssertFormulaDateFunctionContrastLocations("DAYS360($A1)>0");
        AssertFormulaDateFunctionContrastLocations("DAYS360($A1,DATE(2023,3,15),0,1)>0");
        AssertFormulaDateFunctionContrastLocations("DAYS360(\"2023-03-16\",DATE(2023,3,15))>0");
        AssertFormulaDateFunctionContrastLocations("DAYS360($A1,2958466)>0");
        AssertFormulaDateFunctionContrastLocations("DAYS360($A1,DATE(2023,3,15),\"TRUE\")>0");
        AssertFormulaDateFunctionContrastLocations("DAYS360($A1,DATE(2023,3,15),1E308*1E308)>0");
        AssertFormulaDateFunctionContrastLocations("DAYS360(A0,DATE(2023,3,15))>0");
        AssertFormulaDateFunctionContrastLocations("WORKDAY($A1)>0");
        AssertFormulaDateFunctionContrastLocations("WORKDAY($A1,1,DATE(2023,3,17),1)>0");
        AssertFormulaDateFunctionContrastLocations("WORKDAY($A1,$C1)>0");
        AssertFormulaDateFunctionContrastLocations("WORKDAY(A0,1)>0");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS($A1)>0");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS($A1,DATE(2023,3,20),DATE(2023,3,17),1)>0");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS($A1,$C1)>0");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS(A0,DATE(2023,3,20))>0");
        AssertFormulaDateFunctionContrastLocations("WORKDAY.INTL($A1)>0");
        AssertFormulaDateFunctionContrastLocations("WORKDAY.INTL($A1,1,1,DATE(2023,3,17),1)>0");
        AssertFormulaDateFunctionContrastLocations("WORKDAY.INTL($A1,$C1)>0");
        AssertFormulaDateFunctionContrastLocations("WORKDAY.INTL($A1,1,$E$1:$E$1)>0");
        AssertFormulaDateFunctionContrastLocations("WORKDAY.INTL(A0,1)>0");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS.INTL($A1)>0");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS.INTL($A1,DATE(2023,3,20),1,DATE(2023,3,17),1)>0");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS.INTL($A1,$C1)>0");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS.INTL($A1,DATE(2023,3,20),$E$1:$E$1)>0");
        AssertFormulaDateFunctionContrastLocations("NETWORKDAYS.INTL(A0,DATE(2023,3,20))>0");
        AssertFormulaDateFunctionContrastLocations("DATEDIF($A1,DATE(2024,4,20))>0");
        AssertFormulaDateFunctionContrastLocations("DATEDIF($A1,DATE(2024,4,20),\"D\",1)>0");
        AssertFormulaDateFunctionContrastLocations("DATEDIF(\"2023-03-15\",DATE(2024,4,20),\"D\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("DATEDIF($A1,1E308,\"D\")>0");
        AssertFormulaDateFunctionContrastLocations("DATEDIF(DATE(2024,4,20),$A1,\"D\")>0");
        AssertFormulaDateFunctionContrastLocations("DATEDIF($A1,DATE(2024,4,20),1)>0");
        AssertFormulaDateFunctionContrastLocations("DATEDIF($A1,DATE(2024,4,20),\"Q\")>0");
        AssertFormulaDateFunctionContrastLocations("DATEDIF(A0,DATE(2024,4,20),\"D\")>0");
        AssertFormulaDateFunctionContrastLocations("DATEDIF(DATE(2020,2,29),DATE(2021,3,1),\"YD\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC($A1)>0");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC($A1,DATE(2023,3,15),0,1)>0");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC(\"2023-03-15\",DATE(2023,3,15))>0");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC($A1,2958466)>0");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC($A1,DATE(2023,3,15),\"0\")>0", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC($A1,DATE(2023,3,15),1E308*1E308)>0");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC($A1,DATE(2023,3,15),5)>0");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC($A1,DATE(2023,3,15),-1)>0");
        AssertFormulaDateFunctionContrastLocations("YEARFRAC(A0,DATE(2023,3,15))>0");
        AssertFormulaDateFunctionContrastLocations("HOUR(\"0.5\")>0", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("MINUTE(-1)>0");
        AssertFormulaDateFunctionContrastLocations("SECOND(2958465.1)>0");
        AssertFormulaDateFunctionContrastLocations("HOUR($D1,1)>0");
        AssertFormulaDateFunctionContrastLocations("MINUTE(A0)>0");
        AssertFormulaDateFunctionContrastLocations("SECOND(1E308)>0");
        AssertFormulaDateFunctionContrastLocations("TIME(1,2)>0");
        AssertFormulaDateFunctionContrastLocations("TIME(1,2,3,4)>0");
        AssertFormulaDateFunctionContrastLocations("TIME(\"1\",2,3)>0", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("TIME(-1,0,0)>0");
        AssertFormulaDateFunctionContrastLocations("TIME(32768,0,0)>0");
        AssertFormulaDateFunctionContrastLocations("TIME(1E308*1E308,0,0)>0");
        AssertFormulaDateFunctionContrastLocations("TIME(A0,0,0)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatWorkdayNetworkdaysErrors()
    {
        AssertFormulaDateFunctionContrastLocations("ISERROR(WORKDAY(-1,1))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISERROR(WORKDAY(DATE(2023,3,15),1E308))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISERROR(WORKDAY(2958465,1))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISERROR(NETWORKDAYS(DATE(2023,3,15),-1))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISERROR(NETWORKDAYS(DATE(2023,3,15),DATE(2023,3,16),-1))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISNA(WORKDAY(DATE(2023,3,15),1,NA()))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISNA(WORKDAY(DATE(2023,3,15),1,$E$4:$E$4))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISNA(NETWORKDAYS(DATE(2023,3,15),DATE(2023,3,16),NA()))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISNA(NETWORKDAYS(DATE(2023,3,15),DATE(2023,3,16),$E$4:$E$4))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISERROR(WORKDAY.INTL(-1,1))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISERROR(WORKDAY.INTL(DATE(2023,3,15),1E308))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISERROR(WORKDAY.INTL(2958465,1))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISERROR(WORKDAY.INTL(DATE(2023,3,15),1,0))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISERROR(WORKDAY.INTL(DATE(2023,3,15),1,\"1111111\"))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISERROR(WORKDAY.INTL(DATE(2023,3,15),1,1,-1))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISERROR(NETWORKDAYS.INTL(DATE(2023,3,15),-1))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISERROR(NETWORKDAYS.INTL(DATE(2023,3,15),DATE(2023,3,16),8))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISERROR(NETWORKDAYS.INTL(DATE(2023,3,15),DATE(2023,3,16),\"1234567\"))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISERROR(NETWORKDAYS.INTL(DATE(2023,3,15),DATE(2023,3,16),1,-1))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISNA(WORKDAY.INTL(-1,1,NA()))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISNA(WORKDAY.INTL(DATE(2023,3,15),1,NA()))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISNA(WORKDAY.INTL(DATE(2023,3,15),1,1,NA()))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISNA(WORKDAY.INTL(DATE(2023,3,15),1,1,$E$4:$E$4))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISNA(NETWORKDAYS.INTL(-1,DATE(2023,3,16),NA()))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISNA(NETWORKDAYS.INTL(DATE(2023,3,15),DATE(2023,3,16),NA()))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISNA(NETWORKDAYS.INTL(DATE(2023,3,15),DATE(2023,3,16),1,NA()))", "B1", "B2", "B3", "B4");
        AssertFormulaDateFunctionContrastLocations("ISNA(NETWORKDAYS.INTL(DATE(2023,3,15),DATE(2023,3,16),1,$E$4:$E$4))", "B1", "B2", "B3", "B4");
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
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatReferenceDimensionFunctions()
    {
        AssertFormulaRowColumnFunctionContrastLocations("ROWS($A$1:$C$2)=2", "B1", "C1", "D1", "B2", "C2", "D2");
        AssertFormulaRowColumnFunctionContrastLocations("COLUMNS($A$1:$C$2)=3", "B1", "C1", "D1", "B2", "C2", "D2");
        AssertFormulaRowColumnFunctionContrastLocations("AREAS($A$1:$C$2)=1", "B1", "C1", "D1", "B2", "C2", "D2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatReferenceDimensionFullRowAndColumnFunctions()
    {
        AssertFormulaRowColumnFunctionContrastLocations("ROWS($A:$A)=1048576", "B1", "C1", "D1", "B2", "C2", "D2");
        AssertFormulaRowColumnFunctionContrastLocations("COLUMNS($A:$C)=3", "B1", "C1", "D1", "B2", "C2", "D2");
        AssertFormulaRowColumnFunctionContrastLocations("ROWS($1:$3)=3", "B1", "C1", "D1", "B2", "C2", "D2");
        AssertFormulaRowColumnFunctionContrastLocations("COLUMNS($1:$1)=16384", "B1", "C1", "D1", "B2", "C2", "D2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatReferenceDimensionFunctionWrappers()
    {
        AssertFormulaRowColumnFunctionContrastLocations("IF(ROWS($A$1:$A$2)=2,TRUE,FALSE)", "B1", "C1", "D1", "B2", "C2", "D2");
        AssertFormulaRowColumnFunctionContrastLocations("AND(COLUMNS($A$1:$C$1)=3,AREAS($A$1:$C$1))", "B1", "C1", "D1", "B2", "C2", "D2");
        AssertFormulaRowColumnFunctionContrastLocations("ISNUMBER(ROWS($A$1:$A$2))", "B1", "C1", "D1", "B2", "C2", "D2");
        AssertFormulaRowColumnFunctionContrastLocations("ROWS($A$1:$A$2)+COLUMNS($A$1:$C$1)=5", "B1", "C1", "D1", "B2", "C2", "D2");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatReferenceDimensionFunctionOperandCoercionAndErrorSemantics()
    {
        AssertFormulaRowColumnFunctionContrastLocations("ROWS(1)>0", "B1", "C1", "D1", "B2", "C2", "D2");
        AssertFormulaRowColumnFunctionContrastLocations("COLUMNS(\"A1\")>0", "B1", "C1", "D1", "B2", "C2", "D2");
        AssertFormulaRowColumnFunctionContrastLocations("AREAS($A$1,$B$1)>0");
        AssertFormulaRowColumnFunctionContrastLocations("ROWS(Missing!$A$1:$A$2)>0");
        AssertFormulaRowColumnFunctionContrastLocations("COLUMNS(A0:A1)>0");
        AssertFormulaRowColumnFunctionContrastLocations("ROWS(SEQUENCE(2))>0", "B1", "C1", "D1", "B2", "C2", "D2");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatLookupReferenceExactBasics()
    {
        AssertFormulaLookupReferenceFunctionContrastLocations("CHOOSE($A1,FALSE,TRUE,FALSE)", "B2", "B4");
        AssertFormulaLookupReferenceFunctionContrastLocations("MATCH($C1,$F$1:$F$4,0)=2", "B2", "B4");
        AssertFormulaLookupReferenceFunctionContrastLocations("XMATCH($C1,$F$1:$F$4)=2", "B2", "B4");
        AssertFormulaLookupReferenceFunctionContrastLocations("INDEX($G$1:$G$4,MATCH($C1,$F$1:$F$4,0))=20", "B2", "B4");
        AssertFormulaLookupReferenceFunctionContrastLocations("VLOOKUP($C1,$F$1:$G$4,2,FALSE)=20", "B2", "B4");
        AssertFormulaLookupReferenceFunctionContrastLocations("HLOOKUP($C1,$H$1:$K$2,2,FALSE)=20", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatLookupReferenceApproximateAndErrors()
    {
        AssertFormulaLookupReferenceFunctionContrastLocations("MATCH($D1,$M$1:$M$4,1)=2", "B3");
        AssertFormulaLookupReferenceFunctionContrastLocations("VLOOKUP($D1,$M$1:$N$4,2,TRUE)=\"Band3\"", "B4");
        AssertFormulaLookupReferenceFunctionContrastLocations("HLOOKUP($D1,$P$1:$S$2,2,TRUE)=\"Band3\"", "B4");
        AssertFormulaLookupReferenceFunctionContrastLocations("ISNA(MATCH($C1,$F$1:$F$4,0))", "B5");
        AssertFormulaLookupReferenceFunctionContrastLocations("ISNA(VLOOKUP($C1,$F$1:$G$4,2,FALSE))", "B5");
        AssertFormulaLookupReferenceFunctionContrastLocations("ISERROR(INDEX($G$1:$G$4,99))", FormulaLookupReferenceAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatXLookupExactAndDefaults()
    {
        AssertFormulaLookupReferenceFunctionContrastLocations("XLOOKUP($C1,$F$1:$F$4,$G$1:$G$4)=20", "B2", "B4");
        AssertFormulaLookupReferenceFunctionContrastLocations("XLOOKUP($C1,$H$1:$K$1,$H$2:$K$2)=20", "B2", "B4");
        AssertFormulaLookupReferenceFunctionContrastLocations("XLOOKUP($C1,$F$1:$F$4,$G$1:$G$4,99)=99", "B5");
        AssertFormulaLookupReferenceFunctionContrastLocations("ISNA(XLOOKUP($C1,$F$1:$F$4,$G$1:$G$4))", "B5");
        AssertFormulaLookupReferenceFunctionContrastLocations("IFERROR(XLOOKUP($C1,$F$1:$F$4,$G$1:$G$4)=20,$C1=\"Missing\")", "B2", "B4", "B5");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatXLookupApproximateAndShiftedReferences()
    {
        AssertFormulaLookupReferenceFunctionContrastLocations("XLOOKUP($D1,$M$1:$M$4,$N$1:$N$4,\"Missing\",-1)=\"Band3\"", "B4");
        AssertFormulaLookupReferenceFunctionContrastLocations("XLOOKUP($D1,$M$1:$M$4,$N$1:$N$4,\"Missing\",-1)=\"Missing\"", "B1");
        AssertFormulaLookupReferenceFunctionContrastLocations("XLOOKUP(\"Beta\",$F1:$F4,$G1:$G4)=20", "B1", "B2");
        AssertFormulaLookupReferenceFunctionContrastLocations("AND(XLOOKUP($C1,$F$1:$F$4,$G$1:$G$4,0)=20,$C1=\"Beta\")", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatLookupVectorFunction()
    {
        AssertFormulaLookupReferenceFunctionContrastLocations("LOOKUP($D1,$M$1:$M$4,$N$1:$N$4)=\"Band2\"", "B3");
        AssertFormulaLookupReferenceFunctionContrastLocations("LOOKUP($D1,$M$1:$N$4)=\"Band3\"", "B4");
        AssertFormulaLookupReferenceFunctionContrastLocations("ISNA(LOOKUP($D1,$M$1:$M$4,$N$1:$N$4))", "B1");
        AssertFormulaLookupReferenceFunctionContrastLocations("IFERROR(LOOKUP($D1,$M$1:$M$4,$N$1:$N$4)=\"Missing\",$D1<10)", "B1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatOffsetFunction()
    {
        AssertFormulaLookupReferenceFunctionContrastLocations("OFFSET($G$1,MATCH($C1,$F$1:$F$4,0)-1,0)=20", "B2", "B4");
        AssertFormulaLookupReferenceFunctionContrastLocations("XLOOKUP($C1,OFFSET($F$1,0,0,4,1),OFFSET($G$1,0,0,4,1))=20", "B2", "B4");
        AssertFormulaLookupReferenceFunctionContrastLocations("OFFSET($F1,1,1)=20", "B1");
        AssertFormulaLookupReferenceFunctionContrastLocations("ISERROR(OFFSET($G$1,-1,0))", FormulaLookupReferenceAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatIndirectFunction()
    {
        AssertFormulaLookupReferenceFunctionContrastLocations("INDIRECT(\"$G$2\")=20", FormulaLookupReferenceAllLocations);
        AssertFormulaLookupReferenceFunctionContrastLocations("INDIRECT(CONCAT(\"$G\",$A1))=20", "B2", "B4");
        AssertFormulaLookupReferenceFunctionContrastLocations("SUM(INDIRECT(\"$G$1:$G$2\"))=30", FormulaLookupReferenceAllLocations);
        AssertFormulaLookupReferenceFunctionContrastLocations("XLOOKUP($C1,INDIRECT(\"$F$1:$F$4\"),INDIRECT(\"$G$1:$G$4\"))=20", "B2", "B4");
        AssertFormulaLookupReferenceFunctionContrastLocations("INDIRECT(\"RC[5]\",FALSE)=20", "B2");
        AssertFormulaLookupReferenceFunctionContrastLocations("INDIRECT(\"'Quoted Sheet'!$A$1\")=20", FormulaLookupReferenceAllLocations);
        AssertFormulaLookupReferenceFunctionContrastLocations("ISERROR(INDIRECT(\"Missing!$A$1\"))", FormulaLookupReferenceAllLocations);
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatLookupReferenceShapeAndErrorSemantics()
    {
        AssertFormulaLookupReferenceFunctionContrastLocations("MATCH($C1,$F$1:$G$4,0)=1");
        AssertFormulaLookupReferenceFunctionContrastLocations("XMATCH($C1,$F$1:$F$4,0,2)=2", "B2", "B4");
        AssertFormulaLookupReferenceFunctionContrastLocations("VLOOKUP($C1,$F$1:$G$4,3,FALSE)=20");
        AssertFormulaLookupReferenceFunctionContrastLocations("INDEX($G$1:$G$4,0,0)=20");
        AssertFormulaLookupReferenceFunctionContrastLocations("MATCH($D1,$U$1:$U$4,1)>0", "B2", "B3", "B4", "B5");
        AssertFormulaLookupReferenceFunctionContrastLocations("XLOOKUP($C1,$F$1:$G$4,$G$1:$G$4)=20");
        AssertFormulaLookupReferenceFunctionContrastLocations("XLOOKUP($C1,$F$1:$F$4,$F$1:$G$4)=20");
        AssertFormulaLookupReferenceFunctionContrastLocations("XLOOKUP($C1,$F$1:$F$4,$G$1:$G$4,\"Missing\",2)=20", "B2", "B4");
        AssertFormulaLookupReferenceFunctionContrastLocations("XLOOKUP($C1,$F$1:$F$4,$G$1:$G$4,\"Missing\",0,2)=20", "B2", "B4");
        AssertFormulaLookupReferenceFunctionContrastLocations("LOOKUP($C1,$F$1:$G$4,$G$1:$G$4)=20");
        AssertFormulaLookupReferenceFunctionContrastLocations("LOOKUP($D1,$U$1:$U$4,$N$1:$N$4)=\"Band3\"", "B3", "B4");
        AssertFormulaLookupReferenceFunctionContrastLocations("OFFSET($A:$A,0,0)>0");
        AssertFormulaLookupReferenceFunctionContrastLocations("OFFSET($G$1,0,0,1,2)=20");
        AssertFormulaLookupReferenceFunctionContrastLocations("INDIRECT(\"$G$1:$H$2\")=20");
        AssertFormulaLookupReferenceFunctionContrastLocations("INDIRECT(\"$A:$A\")>0");
        AssertFormulaLookupReferenceFunctionContrastLocations("INDIRECT(\"$A$1:$XFD$1048576\")>0");
        AssertFormulaLookupReferenceFunctionContrastLocations("INDIRECT(\"[Book.xlsx]Sales!$G$2\")=20");
        AssertFormulaLookupReferenceFunctionContrastLocations("INDIRECT(\"$G$2\",\"FALSE\")=20");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatTypeFunctionComparisons()
    {
        AssertFormulaInfoScalarFunctionContrastLocations("TYPE($A1)=1", "B1", "B4", "B5");
        AssertFormulaInfoScalarFunctionContrastLocations("TYPE($A1)=2", "B2", "B15");
        AssertFormulaInfoScalarFunctionContrastLocations("TYPE($A1)=4", "B3", "B16");
        AssertFormulaInfoScalarFunctionContrastLocations(
            "TYPE($A1)=16",
            "B6",
            "B7",
            "B8",
            "B9",
            "B10",
            "B11",
            "B12",
            "B13",
            "B14");
        AssertFormulaInfoScalarFunctionContrastLocations("TYPE($A$1:$A$5)=64", FormulaInfoScalarAllLocations);
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatNFunctionConversionsWrappersAndPredicates()
    {
        AssertFormulaInfoScalarFunctionContrastLocations("N($A1)>0", "B1", "B3", "B4");
        AssertFormulaInfoScalarFunctionContrastLocations("N($A1)=0", "B2", "B5", "B15", "B16");
        AssertFormulaInfoScalarFunctionContrastLocations("AND(TYPE($A1)=1,N($A1)>0)", "B1", "B4");
        AssertFormulaInfoScalarFunctionContrastLocations("IF(TYPE($A1)=2,N($A1)=0,FALSE)", "B2", "B15");
        AssertFormulaInfoScalarFunctionContrastLocations(
            "ISNUMBER(N($A1))",
            "B1",
            "B2",
            "B3",
            "B4",
            "B5",
            "B15",
            "B16");
        AssertFormulaInfoScalarFunctionContrastLocations(
            "ISERROR(N($A1))",
            "B6",
            "B7",
            "B8",
            "B9",
            "B10",
            "B11",
            "B12",
            "B13",
            "B14");
        AssertFormulaInfoScalarFunctionContrastLocations("ISNA(N($A1))", "B6");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatErrorTypeFunctionCodesAndPredicates()
    {
        AssertFormulaInfoScalarFunctionContrastLocations("ERROR.TYPE($A1)=7", "B6");
        AssertFormulaInfoScalarFunctionContrastLocations("ERROR.TYPE($A1)=2", "B7");
        AssertFormulaInfoScalarFunctionContrastLocations(
            "ERROR.TYPE($A1)>=8",
            "B8",
            "B9",
            "B10",
            "B11",
            "B12",
            "B13",
            "B14");
        AssertFormulaInfoScalarFunctionContrastLocations(
            "ISNA(ERROR.TYPE($A1))",
            "B1",
            "B2",
            "B3",
            "B4",
            "B5",
            "B15",
            "B16");
        AssertFormulaInfoScalarFunctionContrastLocations(
            "AND(ISNUMBER(ERROR.TYPE($A1)),ERROR.TYPE($A1)>=8)",
            "B8",
            "B9",
            "B10",
            "B11",
            "B12",
            "B13",
            "B14");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatInfoScalarFunctionRangeAggregateCases()
    {
        AssertFormulaInfoScalarFunctionContrastLocations("SUM(N($A$1:$A$5))=45043", FormulaInfoScalarAllLocations);
        AssertFormulaInfoScalarFunctionContrastLocations("SUMPRODUCT(N($A$1:$A$5),$C$1:$C$5)=180045", FormulaInfoScalarAllLocations);
        AssertFormulaInfoScalarFunctionContrastLocations("SUM(ERROR.TYPE($A$6:$A$14))=86", FormulaInfoScalarAllLocations);
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatInfoScalarFunctionUnsupportedOrErrorCases()
    {
        AssertFormulaInfoScalarFunctionContrastLocations("TYPE()=1");
        AssertFormulaInfoScalarFunctionContrastLocations("TYPE($A1,1)=1");
        AssertFormulaInfoScalarFunctionContrastLocations("N()=0");
        AssertFormulaInfoScalarFunctionContrastLocations("N($A1,1)=0");
        AssertFormulaInfoScalarFunctionContrastLocations("ERROR.TYPE()=7");
        AssertFormulaInfoScalarFunctionContrastLocations("ERROR.TYPE($A1,1)=7");
        AssertFormulaInfoScalarFunctionContrastLocations("N($A$1:$A$5)=0");
        AssertFormulaInfoScalarFunctionContrastLocations("ERROR.TYPE($A$6:$A$7)=7");
        AssertFormulaInfoScalarFunctionContrastLocations("SUM(ERROR.TYPE($A$1:$A$7))=9");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatInfoReferenceScalarFunctions()
    {
        AssertFormulaInfoReferenceParityContrastLocations(
            "ADDRESS(2,3,4,TRUE,\"Sales\")=\"Sales!C2\"",
            FormulaInfoReferenceParityAllLocations);
        AssertFormulaInfoReferenceParityContrastLocations(
            "ADDRESS(2,3,4,FALSE,\"Data Set\")=\"'Data Set'!R[2]C[3]\"",
            FormulaInfoReferenceParityAllLocations);
        AssertFormulaInfoReferenceParityContrastLocations("CELL(\"address\",$A1)=\"$A$1\"", "B1");
        AssertFormulaInfoReferenceParityContrastLocations("CELL(\"type\",$A1)=\"l\"", "B2");
        AssertFormulaInfoReferenceParityContrastLocations("CELL(\"contents\",$A1)=2", "B3");
        AssertFormulaInfoReferenceParityContrastLocations("CELL(\"format\",$A1)=\"F0\"", "B5");
        AssertFormulaInfoReferenceParityContrastLocations("CELL(\"protect\",$A1)=0", "B5");
        AssertFormulaInfoReferenceParityContrastLocations("CELL(\"prefix\",$A1)=\"^\"");
        AssertFormulaInfoReferenceParityContrastLocations("FORMULATEXT($A1)=\"=SUM(1,1)\"", "B3");
        AssertFormulaInfoReferenceParityContrastLocations("HYPERLINK($C1,$D1)=\"Friendly\"", "B4");
        AssertFormulaInfoReferenceParityContrastLocations("HYPERLINK($C1)=\"https://example.com/friendly\"", "B4");
        AssertFormulaInfoReferenceParityContrastLocations("SHEET()=1", FormulaInfoReferenceParityAllLocations);
        AssertFormulaInfoReferenceParityContrastLocations("SHEET(\"Data Set\")=2", FormulaInfoReferenceParityAllLocations);
        AssertFormulaInfoReferenceParityContrastLocations("SHEETS()=2", FormulaInfoReferenceParityAllLocations);
        AssertFormulaInfoReferenceParityContrastLocations("SHEETS($A1)=1", FormulaInfoReferenceParityAllLocations);
        AssertFormulaInfoReferenceParityContrastLocations("INFO(\"numfile\")=2", FormulaInfoReferenceParityAllLocations);
        AssertFormulaInfoReferenceParityContrastLocations("INFO(\"recalc\")=\"Manual\"", FormulaInfoReferenceParityAllLocations);
        AssertFormulaInfoReferenceParityContrastLocations(
            "GETPIVOTDATA(\"Sum of Amount\",$E$1)=30",
            FormulaInfoReferenceParityAllLocations);
        AssertFormulaInfoReferenceParityContrastLocations(
            "GETPIVOTDATA(\"Sum of Amount\",$E$1,\"Region\",\"East\")=20",
            FormulaInfoReferenceParityAllLocations);
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatInfoReferenceScalarShapeAndErrorSemantics()
    {
        AssertFormulaInfoReferenceParityContrastLocations("INFO(\"directory\")<>\"\"", "B1", "B2", "B3", "B4", "B5");
        AssertFormulaInfoReferenceParityContrastLocations("INFO(\"system\")=\"pcdos\"", "B1", "B2", "B3", "B4", "B5");
        AssertFormulaInfoReferenceParityContrastLocations("CELL(\"type\",1)=\"v\"");
        AssertFormulaInfoReferenceParityContrastLocations("FORMULATEXT(42)<>\"\"");
        AssertFormulaInfoReferenceParityContrastLocations("GETPIVOTDATA(\"Missing\",$E$1)>0");
        AssertFormulaInfoReferenceParityContrastLocations(
            "GETPIVOTDATA(\"Sum of Amount\",$E$1,\"Region\",\"East\",\"Region\",\"West\")>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialDepreciationFunctions()
    {
        AssertFormulaFinancialDepreciationFunctionContrastLocations("SLN($A1,$C1,$D1)>170", "B1", "B2", "B8");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("SYD($A1,$C1,$D1,$E1)>200", "B1", "B2", "B8");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("DB($A1,$C1,$D1,$E1,$I1)>300", "B1", "B2");
        // DDB corrected to Excel's fractional-period behavior (see AccessibilityCheckerService.Contrast.cs
        // FormulaFinancialDdbScalar): B8's row no longer crosses the >300 threshold, so it is no longer a
        // low-contrast cell. Previously pinned the buggy integer-only DDB output.
        AssertFormulaFinancialDepreciationFunctionContrastLocations("DDB($A1,$C1,$D1,$E1)>300", "B1");
        // VDB corrected to Excel's carry-forward book value (r46, see BuiltInFunctions.Financial.Depreciation
        // VdbScalar): for the "Later period" row (start_period=1) VDB now depletes book value across the
        // periods before start_period, so VDB(1000,100,5,1,2)=240 (was the buggy 400 that used the full
        // undepreciated cost). 240 no longer crosses >300, so B2 is no longer a low-contrast cell.
        AssertFormulaFinancialDepreciationFunctionContrastLocations("VDB($A1,$C1,$D1,$F1,$G1)>300", "B1", "B8");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("AMORDEGRC($A1,DATE(2020,1,1),DATE(2020,12,31),$C1,$E1,$K1,0)>140", "B1", "B2", "B8");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("AMORLINC($A1,DATE(2020,1,1),DATE(2020,12,31),$C1,$E1,$K1)>=100", "B1", "B2", "B8");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialRateFunctions()
    {
        AssertFormulaFinancialDepreciationFunctionContrastLocations("EFFECT($K1,$L1)>0.1", "B1", "B2", "B8");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("NOMINAL(EFFECT($K1,$L1),$L1)>0.19", "B2");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("RRI($E1,$M1,$N1)>0.06", "B1", "B2", "B8");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("PDURATION($K1,$M1,$N1)>7", "B1");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialOptionalArgumentsWrappersAndAggregates()
    {
        // DDB corrected to Excel's fractional-period behavior: B8's row no longer crosses the >=300
        // threshold (was pinning the buggy integer-only DDB output).
        AssertFormulaFinancialDepreciationFunctionContrastLocations("DDB($A1,$C1,$D1,$E1,$H1)>=300", "B1");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("DB($A1,$C1,$D1,$E1,$I1)>180", "B1", "B2", "B8");
        // VDB corrected to carry book value forward (r46): the "Later period" row (start_period=1) now
        // yields VDB(1000,100,5,1,2,2,FALSE)=240 (was the buggy 400), which no longer clears >250, so the
        // AND(...) predicate is false for B2 and it is no longer a low-contrast cell.
        AssertFormulaFinancialDepreciationFunctionContrastLocations("AND($O1,VDB($A1,$C1,$D1,$F1,$G1,$H1,$J1)>250)", "B1", "B8");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("SUM(SLN($A1,$C1,$D1),SYD($A1,$C1,$D1,$E1))>400", "B1", "B2", "B8");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("IF(EFFECT($K1,$L1)>0.1,TRUE,FALSE)", "B1", "B2", "B8");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("AMORLINC($A1,DATE(2020,1,1),DATE(2020,12,31),$C1,$E1,$K1)>100", "B2", "B8");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialErrorPredicates()
    {
        AssertFormulaFinancialDepreciationFunctionContrastLocations("ISERROR(SLN($A1,$C1,$D1))", "B5", "B6", "B7");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("ISNA(SLN($A1,$C1,$D1))", "B5");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("ISERROR(DB($A1,$C1,$D1,$E1,$I1))", "B5", "B6", "B7");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("ISERROR(VDB($A1,$C1,$D1,$F1,$G1,$H1,$J1))", "B5", "B6", "B7");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("ISERROR(AMORDEGRC($A1,DATE(2020,1,1),DATE(2020,12,31),$C1,$E1,$K1,0))", "B5", "B6", "B7");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("ISNA(AMORLINC($A1,DATE(2020,1,1),DATE(2020,12,31),$C1,$E1,$K1))", "B5");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("ISERROR(EFFECT($K1,$L1))", "B5", "B6", "B7");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("ISNA(EFFECT($K1,$L1))", "B5");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("ISERROR(RRI($E1,$M1,$N1))", "B5", "B6", "B7");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("ISERROR(PDURATION($K1,$M1,$N1))", "B5", "B6", "B7");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatFinancialUnsupportedShapesArityAndErrorComparisons()
    {
        AssertFormulaFinancialDepreciationFunctionContrastLocations("SLN($A$1:$A$2,$C1,$D1)>0");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("SYD($A1,$C1,$D1)>0");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("DB($A1,$C1,$D1,$E1,$I1,1)>0");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("DDB($A1,$C1,$D1)>0");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("VDB($A1,$C1,$D1,$F1)>0");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("VDB($A1,$C1,$D1,$F1,$G1,$H1,$J1,1)>0");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("VDB($A1,$C1,$D1,$F1,$G1,$H1,$J$1:$J$2)>0");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("AMORDEGRC($A$1:$A$2,DATE(2020,1,1),DATE(2020,12,31),$C1,$E1,$K1)>0");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("AMORLINC($A1,DATE(2020,1,1),DATE(2020,12,31),$C1,$E1)>0");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("AMORLINC($A1,DATE(2020,1,1),DATE(2020,12,31),$C1,$E1,$K1,$J$1:$J$2)>0");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("EFFECT($K$1:$K$2,$L1)>0");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("NOMINAL($K1)>0");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("RRI($E1,$M1)>0");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("PDURATION($K1,$M1,$N1,1)>0");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("SLN($A$6,$C$6,$D$6)>0");
        AssertFormulaFinancialDepreciationFunctionContrastLocations("EFFECT($K$6,$L$6)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialCouponFunctions()
    {
        AssertFormulaFinancialCouponFunctionContrastLocations("COUPDAYBS($A1,$C1,$D1,$E1)>10", "B2", "B3", "B8");
        AssertFormulaFinancialCouponFunctionContrastLocations("COUPDAYS($A1,$C1,$D1)>=180", "B1", "B2", "B4");
        AssertFormulaFinancialCouponFunctionContrastLocations("COUPDAYSNC($A1,$C1,$D1,$E1)>100", "B1", "B2", "B4");
        AssertFormulaFinancialCouponFunctionContrastLocations("COUPNCD($A1,$C1,$D1,$E1)=DATE(2020,7,1)", "B2");
        AssertFormulaFinancialCouponFunctionContrastLocations("COUPNUM($A1,$C1,$D1,$E1)>=2", "B2", "B3", "B8");
        AssertFormulaFinancialCouponFunctionContrastLocations("COUPPCD($A1,$C1,$D1,$E1)=DATE(2020,7,1)", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialCouponWrappersAndErrors()
    {
        AssertFormulaFinancialCouponFunctionContrastLocations("SUM(COUPDAYBS($A1,$C1,$D1,$E1),COUPDAYSNC($A1,$C1,$D1,$E1))>=180", "B1", "B2", "B4");
        AssertFormulaFinancialCouponFunctionContrastLocations("IF(COUPNUM($A1,$C1,$D1,$E1)>1,TRUE,FALSE)", "B2", "B3", "B8");
        AssertFormulaFinancialCouponFunctionContrastLocations("AND(ISNUMBER(COUPNCD($A1,$C1,$D1,$E1)),COUPPCD($A1,$C1,$D1,$E1)<=DATE(2020,7,1))", "B1", "B2", "B3", "B4", "B8");
        AssertFormulaFinancialCouponFunctionContrastLocations("ISNA(COUPDAYBS($A1,$C1,$D1,$E1))", "B5");
        AssertFormulaFinancialCouponFunctionContrastLocations("ISERROR(COUPNUM($A1,$C1,$D1,$E1))", "B5", "B6", "B7");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatFinancialCouponUnsupportedShapesArityAndComparisons()
    {
        AssertFormulaFinancialCouponFunctionContrastLocations("COUPDAYS($A$1:$A$2,$C1,$D1)>0");
        AssertFormulaFinancialCouponFunctionContrastLocations("COUPNCD($A1,$C1,$D$1:$D$2)>0");
        AssertFormulaFinancialCouponFunctionContrastLocations("COUPPCD($A1,$C1)>0");
        AssertFormulaFinancialCouponFunctionContrastLocations("COUPNUM($A1,$C1,$D1,$E1,$F1)>0");
        AssertFormulaFinancialCouponFunctionContrastLocations("COUPDAYS($A$7,$C$7,$D$7,$E$7)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialBillDiscountFunctions()
    {
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("DISC($A1,$C1,$E1,$G1,$K1)>0.02", "B1", "B3", "B6");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("INTRATE($A1,$C1,$F1,$G1,$K1)>0.05", "B1", "B3", "B6");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("RECEIVED($A1,$C1,$F1,$H1,$K1)>98", "B2", "B3");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("TBILLPRICE($A1,$L1,$H1)<99", "B1");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("TBILLYIELD($A1,$L1,$E1)>0.05", "B1", "B3", "B6");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("TBILLEQ($A1,$L1,$H1)>0.04", "B1", "B3");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("PRICEDISC($A1,$C1,$H1,$G1,$K1)<96", "B1");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("PRICEMAT($A1,$C1,$D1,$I1,$J1,$K1)>100.5", "B3");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialBillDiscountWrappersDefaultsAndErrors()
    {
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("IF(DISC($A1,$C1,$E1,$G1)>0.02,TRUE,FALSE)", "B1", "B3", "B6");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("AND($M1,TBILLPRICE($A1,$L1,$H1)<99)", "B1");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("ISNUMBER(TBILLEQ($A1,$L1,$H1))", "B1", "B2", "B3");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("ISNA(DISC($A1,$C1,$E1,$G1,$K1))", "B4");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("ISERROR(RECEIVED($A1,$C1,$F1,$H1,$K1))", "B4", "B5", "B6");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("ISERROR(PRICEMAT($A1,$C1,$D1,$I1,$J1,$K1))", "B4", "B5");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatFinancialBillDiscountUnsupportedShapesArityAndComparisons()
    {
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("DISC($A$1:$A$2,$C1,$E1,$G1)>0");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("ISERROR(DISC($A$1:$A$2,$C1,$E1,$G1))");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("INTRATE($A1,$C1,$F1)>0");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("RECEIVED($A1,$C1,$F1,$H1,$K1,$M1)>0");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("TBILLPRICE($A1,$L$1:$L$2,$H1)>0");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("TBILLYIELD($A1,$L1)>0");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("TBILLEQ($A$5,$L$5,$H$5)>0");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("PRICEDISC($A1,$C1,$H1)>0");
        AssertFormulaFinancialBillDiscountFunctionContrastLocations("PRICEMAT($A1,$C1,$D1,$I1)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialBondYieldFunctions()
    {
        AssertFormulaFinancialBondYieldFunctionContrastLocations("PRICE($A1,$C1,$D1,$E1,$G1,$H1)>105", "B1", "B2", "B8");
        // R30-financial-coupon-2: PRICE/YIELD now return the Excel CLEAN price (accrued
        // interest subtracted), so YIELD for this bond no longer exceeds 0.08 at B4.
        AssertFormulaFinancialBondYieldFunctionContrastLocations("YIELD($A1,$C1,$D1,$F1,$G1,$H1)>0.08", "B1", "B2", "B8");
        AssertFormulaFinancialBondYieldFunctionContrastLocations("DURATION($A1,$C1,$D1,$E1,$H1)>4", "B1", "B2", "B8");
        AssertFormulaFinancialBondYieldFunctionContrastLocations("MDURATION($A1,$C1,$D1,$E1,$H1)>4", "B1", "B2", "B8");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialBondYieldWrappersAndOptionalBasis()
    {
        AssertFormulaFinancialBondYieldFunctionContrastLocations("PRICE($A1,$C1,$D1,$E1,$G1,$H1,$I1)>105", "B1", "B2", "B8");
        AssertFormulaFinancialBondYieldFunctionContrastLocations("SUM(PRICE($A1,$C1,$D1,$E1,$G1,$H1),DURATION($A1,$C1,$D1,$E1,$H1))>117", "B1", "B2", "B8");
        AssertFormulaFinancialBondYieldFunctionContrastLocations("YIELDDISC($A1,$J1,$K1,$G1,$I1)>0.04", "B1", "B2", "B4", "B8");
        AssertFormulaFinancialBondYieldFunctionContrastLocations("YIELDMAT($A1,$J1,$A1,$D1,$K1,$I1)>0.05", "B1", "B2", "B4", "B8");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialBondYieldErrorPredicates()
    {
        AssertFormulaFinancialBondYieldFunctionContrastLocations("ISNUMBER(DURATION($A1,$C1,$D1,$E1,$H1))", "B1", "B2", "B3", "B4", "B8");
        AssertFormulaFinancialBondYieldFunctionContrastLocations("ISNA(PRICE($A1,$C1,$D1,$E1,$G1,$H1))", "B5");
        AssertFormulaFinancialBondYieldFunctionContrastLocations("ISERROR(YIELD($A1,$C1,$D1,$F1,$G1,$H1))", "B5", "B6", "B7");
        AssertFormulaFinancialBondYieldFunctionContrastLocations("ISERROR(YIELDDISC($A1,$J1,$K1,$G1,$I1))", "B5", "B6", "B7");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatFinancialBondYieldUnsupportedShapesArityAndComparisons()
    {
        AssertFormulaFinancialBondYieldFunctionContrastLocations("PRICE($A$1:$A$2,$C1,$D1,$E1,$G1,$H1)>0");
        AssertFormulaFinancialBondYieldFunctionContrastLocations("YIELD($A1,$C1,$D1,$F1,$G1)>0");
        AssertFormulaFinancialBondYieldFunctionContrastLocations("DURATION($A1,$C1,$D1,$E1,$H1,$I1,$J1)>0");
        AssertFormulaFinancialBondYieldFunctionContrastLocations("MDURATION($A1,$C1,$D1,$E1,$H$1:$H$2)>0");
        AssertFormulaFinancialBondYieldFunctionContrastLocations("YIELDDISC($A1,$J1,$K1,$G1,$I1,$L1)>0");
        AssertFormulaFinancialBondYieldFunctionContrastLocations("YIELDMAT($A1,$J1,$A1,$D1,$K$1:$K$2)>0");
        AssertFormulaFinancialBondYieldFunctionContrastLocations("PRICE($A$6,$C$6,$D$6,$E$6,$G$6,$H$6)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialOddCouponFunctions()
    {
        // R79: ODDFPRICE/ODDFYIELD now include the redemption principal (Excel-correct), so B2's value newly triggers the low-contrast CF
        AssertFormulaFinancialOddCouponFunctionContrastLocations("ODDFPRICE($A1,$C1,$D1,$E1,$F1,$G1,$I1,$J1,$K1)>99", "B1", "B2", "B4", "B8");
        // R79: ODDFYIELD now includes the redemption principal (Excel-correct) via OddFirstPrice, so B2's value newly triggers the low-contrast CF
        AssertFormulaFinancialOddCouponFunctionContrastLocations("ODDFYIELD($A1,$C1,$D1,$E1,$F1,$H1,$I1,$J1,$K1)>0.08", "B2", "B4");
        AssertFormulaFinancialOddCouponFunctionContrastLocations("ODDLPRICE($A1,$L1,$D1,$F1,$G1,$I1,$J1,$K1)>100", "B2", "B4");
        AssertFormulaFinancialOddCouponFunctionContrastLocations("ODDLYIELD($A1,$L1,$D1,$F1,$H1,$I1,$J1,$K1)>0.09", "B2", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialOddCouponWrappersDefaultsAndOptionalArguments()
    {
        // R79: ODDFPRICE now includes the redemption principal (Excel-correct), so B2's value newly triggers the low-contrast CF
        AssertFormulaFinancialOddCouponFunctionContrastLocations("IF(ODDFPRICE($A1,$C1,$D1,$E1,$F1,$G1,$I1,$J1)>99,TRUE,FALSE)", "B1", "B2", "B4", "B8");
        // R79: ODDFPRICE now includes the redemption principal (Excel-correct), pushing this SUM over 200 for B2 too
        AssertFormulaFinancialOddCouponFunctionContrastLocations("SUM(ODDFPRICE($A1,$C1,$D1,$E1,$F1,$G1,$I1,$J1,$K1),ODDLPRICE($A1,$L1,$D1,$F1,$G1,$I1,$J1,$K1))>200", "B2", "B4");
        AssertFormulaFinancialOddCouponFunctionContrastLocations("AND($M1,ODDLYIELD($A1,$L1,$D1,$F1,$H1,$I1,$J1)>0.09)", "B4");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialOddCouponErrorPredicates()
    {
        AssertFormulaFinancialOddCouponFunctionContrastLocations("ISNUMBER(ODDLYIELD($A1,$L1,$D1,$F1,$H1,$I1,$J1,$K1))", "B1", "B2", "B3", "B4", "B8");
        AssertFormulaFinancialOddCouponFunctionContrastLocations("ISNA(ODDFPRICE($A1,$C1,$D1,$E1,$F1,$G1,$I1,$J1,$K1))", "B5");
        AssertFormulaFinancialOddCouponFunctionContrastLocations("ISERROR(ODDFYIELD($A1,$C1,$D1,$E1,$F1,$H1,$I1,$J1,$K1))", "B5", "B6", "B7", "B9");
        AssertFormulaFinancialOddCouponFunctionContrastLocations("ISERR(ODDLPRICE($A1,$L1,$D1,$F1,$G1,$I1,$J1,$K1))", "B6", "B7", "B9");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatFinancialOddCouponUnsupportedShapesArityAndComparisons()
    {
        AssertFormulaFinancialOddCouponFunctionContrastLocations("ODDFPRICE($A$1:$A$2,$C1,$D1,$E1,$F1,$G1,$I1,$J1,$K1)>0");
        AssertFormulaFinancialOddCouponFunctionContrastLocations("ISERROR(ODDFPRICE($A$1:$A$2,$C1,$D1,$E1,$F1,$G1,$I1,$J1,$K1))");
        AssertFormulaFinancialOddCouponFunctionContrastLocations("ODDFYIELD($A1,$C1,$D1,$E1,$F1,$H1,$I1)>0");
        AssertFormulaFinancialOddCouponFunctionContrastLocations("ODDLPRICE($A1,$L1,$D1,$F1,$G1,$I1,$J1,$K1,$M1)>0");
        AssertFormulaFinancialOddCouponFunctionContrastLocations("ODDLYIELD($A1,$L1,$D1,$F1,$H1,$I1,$J$1:$J$2,$K1)>0");
        AssertFormulaFinancialOddCouponFunctionContrastLocations("ODDFPRICE($A$6,$C$6,$D$6,$E$6,$F$6,$G$6,$I$6,$J$6)>0");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialAccrualScheduleAndDollarFunctions()
    {
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("ACCRINT($A1,$C1,$D1,$E1,$F1,$G1,$H1)>=50", "B1", "B2");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("ACCRINTM($A1,$D1,$E1,$F1,$H1)>45", "B1", "B2");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("FVSCHEDULE($I1,$J1:$K1)>150", "B2", "B8");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("DOLLARDE($L1,$N1)>2", "B2", "B8");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("DOLLARFR($M1,$N1)>2", "B2", "B8");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialAccrualScheduleWrappersDefaultsAndOptionalArguments()
    {
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("IF(ACCRINT($A1,$C1,$D1,$E1,$F1,$G1)>45,TRUE,FALSE)", "B1", "B2");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("AND(ISNUMBER(ACCRINTM($A1,$D1,$E1)),FVSCHEDULE($I1,$J1:$K1)>100)", "B1", "B2", "B8");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("ACCRINT($A1,$C1,$D1,$E1,$F1,$G1,$H1,FALSE)<25", "B3", "B4");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("SUM(DOLLARDE($L1,$N1),DOLLARFR($M1,$N1))>6", "B8");
    }

    [Fact]
    public void FindIssues_FlagsLowContrastCellText_FromFormulaConditionalFormatFinancialAccrualScheduleErrorPredicates()
    {
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("ISNA(ACCRINT($A1,$C1,$D1,$E1,$F1,$G1,$H1))", "B5");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("ISERROR(ACCRINTM($A1,$D1,$E1,$F1,$H1))", "B5", "B6", "B7");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("ISERROR(FVSCHEDULE($I1,$J1:$K1))", "B5", "B6", "B7");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("ISERROR(DOLLARDE($L1,$N1))", "B5", "B6", "B7");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("ISERROR(DOLLARFR($M1,$N1))", "B5", "B6", "B7");
    }

    [Fact]
    public void FindIssues_DoesNotMatchFormulaConditionalFormatFinancialAccrualScheduleUnsupportedShapesArityAndComparisons()
    {
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("ACCRINT($A$1:$A$2,$C1,$D1,$E1,$F1,$G1)>0");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("ACCRINT($A1,$C1,$D1,$E1,$F1)>0");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("ACCRINTM($A1,$D1)>0");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("FVSCHEDULE($I$1:$I$2,$J1:$K1)>0");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("FVSCHEDULE($I1,$J1:$K1,0)>0");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("DOLLARDE($L$1:$L$2,$N1)>0");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("DOLLARFR($M1)>0");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("ACCRINT($A$6,$C$6,$D$6,$E$6,$F$6,$G$6)>0");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("FVSCHEDULE($I$6,$J$6:$K$6)>0");
        AssertFormulaFinancialAccrualScheduleFunctionContrastLocations("DOLLARDE($L$6,$N$6)>0");
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
    public void FindIssues_UsesCanonicalFormulaConditionalFormatArithmeticOperandCoercionAndErrorSemantics()
    {
        AssertFormulaArithmeticContrastLocations("$A1/0>0");
        AssertFormulaArithmeticContrastLocations("\"5\"+$A1>80", "B2", "B4");
        AssertFormulaArithmeticContrastLocations("1E308*1E308>0");
        AssertFormulaArithmeticContrastLocations("KURT($A1)+1>0");
        AssertFormulaArithmeticContrastLocations("$A1&1>0", "B1", "B2", "B3", "B4");
        AssertFormulaArithmeticContrastLocations("A0+1>0");
        AssertFormulaArithmeticContrastLocations("(($A1-$A1)^-1)>0");
        AssertFormulaArithmeticContrastLocations("-\"5\">0");
        AssertFormulaArithmeticContrastLocations("KURT($A1)^2>0");
        AssertFormulaArithmeticContrastLocations("$A1^\"2\">0", "B1", "B2", "B3", "B4");
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
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, "AND($A1>=100,UNKNOWNFUNC($A1)>0)");

        FindLowContrastCellTextIssues(workbook).Should().BeEmpty();
    }

    [Fact]
    public void FindIssues_DoesNotMatchUnsupportedFormulaConditionalFormatInsideIfWrapper()
    {
        AssertFormulaIfContrastLocations("IF(UNKNOWNFUNC($A1)>0,TRUE,FALSE)");
        AssertFormulaIfContrastLocations("IF($A1>=100,UNKNOWNFUNC($A1),FALSE)");
    }

    [Fact]
    public void FindIssues_UsesCanonicalFormulaConditionalFormatInsideSelectorWrappers()
    {
        AssertFormulaIfContrastLocations("IFERROR(UNKNOWNFUNC($A1),TRUE)", "B1", "B2", "B3", "B4");
        AssertFormulaIfContrastLocations("IFNA($A1>=100,TRUE,FALSE)");
        AssertFormulaIfContrastLocations("IFS($A1>=100,TRUE,UNKNOWNFUNC($A1)>0,TRUE)", "B2", "B4");
        AssertFormulaIfContrastLocations("IFS($A1>=100)");
        AssertFormulaIfContrastLocations("SWITCH($C1,\"Open\",TRUE,UNKNOWNFUNC($A1),FALSE)", "B3", "B4");
        AssertFormulaIfContrastLocations("SWITCH($C1,\"Open\")");
        AssertFormulaIfContrastLocations("IFERROR($A1,0)>0", "B1", "B2", "B3", "B4");
        AssertFormulaIfContrastLocations("IFS($A1>=100,1,TRUE,0)>0", "B2", "B4");
        AssertFormulaIfContrastLocations("SWITCH($C1,\"Open\",1,0)>0", "B3", "B4");
    }

    [Fact]
    public void FindIssues_DoesNotMatchUnsupportedFormulaConditionalFormatInsideXorWrapper()
    {
        AssertFormulaXorContrastLocations("XOR($A1>=100,UNKNOWNFUNC($A1)>0)");
        AssertFormulaXorContrastLocations("XOR()");
    }

    [Fact]
    public void FindIssues_DoesNotMatchUnsupportedFormulaConditionalFormatInsideIsPredicate()
    {
        AssertFormulaPredicateContrastLocations("ISNUMBER(UNKNOWNFUNC($A1))");
        AssertFormulaParityContrastLocations("ISEVEN(UNKNOWNFUNC($A1))");
        AssertFormulaParityContrastLocations("ISODD(UNKNOWNFUNC($A1))");
        AssertFormulaReferencePredicateContrastLocations("ISREF(UNKNOWNFUNC($A1))");
        AssertFormulaReferencePredicateContrastLocations("ISFORMULA(UNKNOWNFUNC($A1))");
        AssertFormulaPredicateContrastLocations("UNKNOWNFUNC($A1)>0");
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
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new BoolValue(true));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new BoolValue(false));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 5), new BoolValue(true));

        return workbook;
    }

    private static Workbook CreateFormulaMatrixArrayFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 4, 2);

        for (uint row = 1; row <= 4; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue($"Matrix row {row}"));

        SetMatrixArrayCell(sheet, 1, 3, 1);
        SetMatrixArrayCell(sheet, 1, 4, 2);
        SetMatrixArrayCell(sheet, 2, 3, 3);
        SetMatrixArrayCell(sheet, 2, 4, 4);

        SetMatrixArrayCell(sheet, 1, 6, 5);
        SetMatrixArrayCell(sheet, 1, 7, 6);
        SetMatrixArrayCell(sheet, 2, 6, 7);
        SetMatrixArrayCell(sheet, 2, 7, 8);

        SetMatrixArrayCell(sheet, 1, 9, 1);
        SetMatrixArrayCell(sheet, 1, 10, 2);
        SetMatrixArrayCell(sheet, 2, 9, 3);
        SetMatrixArrayCell(sheet, 3, 9, 4);

        SetMatrixArrayCell(sheet, 1, 11, 2);

        SetMatrixArrayCell(sheet, 1, 13, 1);
        SetMatrixArrayCell(sheet, 1, 14, 2);
        SetMatrixArrayCell(sheet, 2, 13, 2);
        SetMatrixArrayCell(sheet, 2, 14, 4);

        SetMatrixArrayCell(sheet, 1, 16, 1);
        SetMatrixArrayCell(sheet, 1, 17, 2);
        SetMatrixArrayCell(sheet, 1, 18, 3);
        SetMatrixArrayCell(sheet, 2, 16, 4);
        SetMatrixArrayCell(sheet, 2, 17, 5);
        SetMatrixArrayCell(sheet, 2, 18, 6);

        return workbook;
    }

    private static void SetMatrixArrayCell(Sheet sheet, uint row, uint col, double value) =>
        sheet.SetCell(new CellAddress(sheet.Id, row, col), new NumberValue(value));

    private static Workbook CreateFormulaStatisticalSelectionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = CreateFormulaAggregateContrastWorkbook(out sheet, out firstLabel, out lastLabel);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 6), new NumberValue(0.25));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 6), new NumberValue(0.25));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 6), new NumberValue(0.25));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 6), new NumberValue(0.25));

        return workbook;
    }

    private static Workbook CreateFormulaStatisticalTestContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 4, 2);

        SetFormulaStatisticalTestContrastRow(sheet, 1, 11, active: true, "Low hypothesis", 10, 11);
        SetFormulaStatisticalTestContrastRow(sheet, 2, 12, active: true, "Near low hypothesis", 12, 13);
        SetFormulaStatisticalTestContrastRow(sheet, 3, 13, active: false, "Mean hypothesis", 14, 15);
        SetFormulaStatisticalTestContrastRow(sheet, 4, 14, active: true, "High hypothesis", 16, 18);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 8), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 9), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 8), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 9), new NumberValue(40));

        sheet.SetCell(new CellAddress(sheet.Id, 1, 11), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 12), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 11), new NumberValue(15));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 12), new NumberValue(45));

        return workbook;
    }

    private static void SetFormulaStatisticalTestContrastRow(
        Sheet sheet,
        uint row,
        double hypothesis,
        bool active,
        string label,
        double firstSample,
        double secondSample)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(hypothesis));
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), new BoolValue(active));
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), new NumberValue(firstSample));
        sheet.SetCell(new CellAddress(sheet.Id, row, 5), new NumberValue(secondSample));
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

    private static Workbook CreateFormulaDatabaseAggregateContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 4, 2);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Closed summary"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Open summary"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 2), new TextValue("Indexed field"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 2), new TextValue("Invalid field"));

        sheet.SetCell(new CellAddress(sheet.Id, 1, 6), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 7), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 8), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 9), new TextValue("Units"));
        SetFormulaDatabaseAggregateDataRow(sheet, 2, "Closed", 75, "East", 1);
        SetFormulaDatabaseAggregateDataRow(sheet, 3, "Closed", 100, "West", 2);
        SetFormulaDatabaseAggregateDataRow(sheet, 4, "Open", 75, "East", 3);
        SetFormulaDatabaseAggregateDataRow(sheet, 5, "Open", 125, "West", 4);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 10), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 10), new TextValue("Closed"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 10), new TextValue("Open"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 10), new TextValue("Missing"));
        sheet.SetCell(new CellAddress(sheet.Id, 5, 10), new TextValue("Open"));

        sheet.SetCell(new CellAddress(sheet.Id, 1, 11), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 11), new TextValue("Open"));

        sheet.SetCell(new CellAddress(sheet.Id, 1, 12), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 12), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 12), new NumberValue(4));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 12), new NumberValue(9));

        sheet.SetCell(new CellAddress(sheet.Id, 1, 13), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 13), new TextValue(">75"));

        sheet.SetCell(new CellAddress(sheet.Id, 1, 14), new TextValue("Units"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 14), new NumberValue(1));

        sheet.SetCell(new CellAddress(sheet.Id, 1, 15), new TextValue("Status"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 15), new TextValue("Missing"));

        return workbook;
    }

    private static void SetFormulaDatabaseAggregateDataRow(
        Sheet sheet,
        uint row,
        string status,
        double amount,
        string region,
        double units)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 6), new TextValue(status));
        sheet.SetCell(new CellAddress(sheet.Id, row, 7), new NumberValue(amount));
        sheet.SetCell(new CellAddress(sheet.Id, row, 8), new TextValue(region));
        sheet.SetCell(new CellAddress(sheet.Id, row, 9), new NumberValue(units));
    }

    private static Workbook CreateFormulaFinancialCashFlowFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 4, 2);

        SetFormulaFinancialCashFlowFunctionContrastRow(
            sheet,
            1,
            new NumberValue(-1000),
            new NumberValue(600),
            new NumberValue(600),
            "Strong return");
        SetFormulaFinancialCashFlowFunctionContrastRow(
            sheet,
            2,
            new NumberValue(-1000),
            new NumberValue(400),
            new NumberValue(400),
            "Weak return");
        SetFormulaFinancialCashFlowFunctionContrastRow(
            sheet,
            3,
            new NumberValue(100),
            new NumberValue(200),
            new NumberValue(300),
            "Positive only");
        SetFormulaFinancialCashFlowFunctionContrastRow(
            sheet,
            4,
            new NumberValue(-1000),
            ErrorValue.NA,
            new NumberValue(1100),
            "NA cash flow");

        return workbook;
    }

    private static void SetFormulaFinancialCashFlowFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue firstCashFlow,
        ScalarValue secondCashFlow,
        ScalarValue thirdCashFlow,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), firstCashFlow);
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), secondCashFlow);
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), thirdCashFlow);
        sheet.SetCell(new CellAddress(sheet.Id, row, 5), new NumberValue(43831));
        sheet.SetCell(new CellAddress(sheet.Id, row, 6), new NumberValue(44016));
        sheet.SetCell(new CellAddress(sheet.Id, row, 7), new NumberValue(44197));
        sheet.SetCell(new CellAddress(sheet.Id, row, 8), new NumberValue(0.1));
        sheet.SetCell(new CellAddress(sheet.Id, row, 9), new NumberValue(0.12));
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

    private static Workbook CreateFormulaNumberValueFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 8, 2);

        SetFormulaNumberValueFunctionContrastRow(sheet, 1, new TextValue("1,234.56"), ".", ",", flag: true, "Default separators");
        SetFormulaNumberValueFunctionContrastRow(sheet, 2, new TextValue("1.234,56"), ",", ".", flag: true, "Localized separators");
        SetFormulaNumberValueFunctionContrastRow(sheet, 3, new TextValue("50%"), ".", ",", flag: false, "Percent text");
        SetFormulaNumberValueFunctionContrastRow(sheet, 4, new TextValue(" (1,234.5%) "), ".", ",", flag: true, "Accounting percent");
        SetFormulaNumberValueFunctionContrastRow(sheet, 5, new TextValue("1\t234"), ".", ",", flag: true, "Ascii spacing");
        SetFormulaNumberValueFunctionContrastRow(sheet, 6, new NumberValue(75), ".", ",", flag: true, "Numeric source");
        SetFormulaNumberValueFunctionContrastRow(sheet, 7, new TextValue("Open"), ".", ",", flag: true, "Invalid source");
        SetFormulaNumberValueFunctionContrastRow(sheet, 8, new TextValue("1.234,56"), ".", ".", flag: true, "Invalid separators");

        return workbook;
    }

    private static void SetFormulaNumberValueFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue source,
        string decimalSeparator,
        string groupSeparator,
        bool flag,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), new BoolValue(flag));
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), source);
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), new TextValue(decimalSeparator));
        sheet.SetCell(new CellAddress(sheet.Id, row, 5), new TextValue(groupSeparator));
    }

    private static Workbook CreateFormulaDateValueTimeValueFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 10, 2);

        SetFormulaDateValueTimeValueFunctionContrastRow(sheet, 1, "1/1/1900", "00:00:00", flag: true, "First Excel date");
        SetFormulaDateValueTimeValueFunctionContrastRow(sheet, 2, "2/29/1900", "12:00:00", flag: true, "Fake leap date");
        SetFormulaDateValueTimeValueFunctionContrastRow(sheet, 3, "1900-02-29 23:59:59", "23:59:59", flag: false, "Fake leap date-time");
        SetFormulaDateValueTimeValueFunctionContrastRow(sheet, 4, "January 2024", "1/2/2024 6:00 AM", flag: true, "Month year text");
        SetFormulaDateValueTimeValueFunctionContrastRow(sheet, 5, "Jan-2024", "1900-02-29 23:59:59", flag: true, "Short month text");
        SetFormulaDateValueTimeValueFunctionContrastRow(sheet, 6, "2024-01-15", "12:00 PM", flag: false, "ISO date text");
        SetFormulaDateValueTimeValueFunctionContrastRow(sheet, 7, "1/2/2024 6:00 AM", "2/29/1900 6:00 AM", flag: true, "Date-time text");
        SetFormulaDateValueTimeValueFunctionContrastRow(sheet, 8, "12:00 PM", "2024-01-02", flag: true, "Wrong component text");
        SetFormulaDateValueTimeValueFunctionContrastRow(sheet, 9, "Open", string.Empty, flag: true, "Invalid text");
        SetFormulaDateValueTimeValueFunctionContrastRow(sheet, 10, new NumberValue(45293), ErrorValue.NA, flag: true, "Non-text source");

        return workbook;
    }

    private static void SetFormulaDateValueTimeValueFunctionContrastRow(
        Sheet sheet,
        uint row,
        string dateText,
        string timeText,
        bool flag,
        string label) =>
        SetFormulaDateValueTimeValueFunctionContrastRow(
            sheet,
            row,
            new TextValue(dateText),
            new TextValue(timeText),
            flag,
            label);

    private static void SetFormulaDateValueTimeValueFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue dateSource,
        ScalarValue timeSource,
        bool flag,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), dateSource);
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), timeSource);
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), new BoolValue(flag));
    }

    private static Workbook CreateFormulaBaseConversionFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 7, 2);

        SetFormulaBaseConversionFunctionContrastRow(
            sheet,
            1,
            new TextValue("1010"),
            new TextValue("A"),
            new TextValue("12"),
            new NumberValue(10),
            new NumberValue(8),
            "Open",
            "Decimal ten");
        SetFormulaBaseConversionFunctionContrastRow(
            sheet,
            2,
            new TextValue("1111"),
            new TextValue("F"),
            new TextValue("17"),
            new NumberValue(15),
            new NumberValue(4),
            "Closed",
            "Decimal fifteen");
        SetFormulaBaseConversionFunctionContrastRow(
            sheet,
            3,
            new TextValue("1111111111"),
            new TextValue("FFFFFFFFFF"),
            new TextValue("7777777777"),
            new NumberValue(-1),
            new NumberValue(4),
            "Open",
            "Negative one");
        SetFormulaBaseConversionFunctionContrastRow(
            sheet,
            4,
            new NumberValue(1010),
            new TextValue("ff"),
            new TextValue("377"),
            new NumberValue(31.9),
            new NumberValue(4.9),
            "Closed",
            "Truncated decimal");
        SetFormulaBaseConversionFunctionContrastRow(
            sheet,
            5,
            new TextValue("0"),
            new TextValue("0"),
            new TextValue("0"),
            new NumberValue(0),
            new NumberValue(4),
            "Open",
            "Zero");
        SetFormulaBaseConversionFunctionContrastRow(
            sheet,
            6,
            new TextValue("102"),
            new TextValue("10000000000"),
            new TextValue("8"),
            new NumberValue(512),
            new NumberValue(2),
            "Open",
            "Invalid domains");
        SetFormulaBaseConversionFunctionContrastRow(
            sheet,
            7,
            ErrorValue.NA,
            ErrorValue.Value,
            ErrorValue.NA,
            ErrorValue.NA,
            new NumberValue(4),
            "Closed",
            "Error source");

        return workbook;
    }

    private static void SetFormulaBaseConversionFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue binarySource,
        ScalarValue hexSource,
        ScalarValue octalSource,
        ScalarValue decimalSource,
        ScalarValue places,
        string status,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), binarySource);
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), hexSource);
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), octalSource);
        sheet.SetCell(new CellAddress(sheet.Id, row, 5), decimalSource);
        sheet.SetCell(new CellAddress(sheet.Id, row, 6), places);
        sheet.SetCell(new CellAddress(sheet.Id, row, 7), new TextValue(status));
    }

    private static Workbook CreateFormulaNormalDistributionFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 9, 2);

        SetFormulaNormalDistributionFunctionContrastRow(
            sheet,
            1,
            new NumberValue(0),
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(0.5),
            new BoolValue(true),
            active: true,
            "Center");
        SetFormulaNormalDistributionFunctionContrastRow(
            sheet,
            2,
            new NumberValue(1),
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(0.841344746068543),
            new BoolValue(false),
            active: true,
            "Positive one");
        SetFormulaNormalDistributionFunctionContrastRow(
            sheet,
            3,
            new NumberValue(-1),
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(0.158655253931457),
            new BoolValue(true),
            active: true,
            "Negative one");
        SetFormulaNormalDistributionFunctionContrastRow(
            sheet,
            4,
            new NumberValue(2),
            new NumberValue(1),
            new NumberValue(0.5),
            new NumberValue(0.977249868051821),
            new BoolValue(false),
            active: true,
            "Shifted scaled");
        SetFormulaNormalDistributionFunctionContrastRow(
            sheet,
            5,
            new NumberValue(1),
            new NumberValue(0),
            new NumberValue(0),
            new NumberValue(0.5),
            new BoolValue(true),
            active: false,
            "Zero stdev");
        SetFormulaNormalDistributionFunctionContrastRow(
            sheet,
            6,
            new NumberValue(0),
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(0),
            new BoolValue(true),
            active: false,
            "Zero probability");
        SetFormulaNormalDistributionFunctionContrastRow(
            sheet,
            7,
            new NumberValue(0),
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(1),
            new BoolValue(true),
            active: false,
            "One probability");
        SetFormulaNormalDistributionFunctionContrastRow(
            sheet,
            8,
            ErrorValue.NA,
            new NumberValue(0),
            new NumberValue(1),
            ErrorValue.NA,
            new BoolValue(true),
            active: false,
            "NA source");
        SetFormulaNormalDistributionFunctionContrastRow(
            sheet,
            9,
            new TextValue("Open"),
            new NumberValue(0),
            new NumberValue(1),
            new TextValue("Open"),
            new BoolValue(true),
            active: false,
            "Value source");

        return workbook;
    }

    private static void SetFormulaNormalDistributionFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue x,
        ScalarValue mean,
        ScalarValue stdev,
        ScalarValue probability,
        ScalarValue cumulative,
        bool active,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), x);
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), mean);
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), stdev);
        sheet.SetCell(new CellAddress(sheet.Id, row, 5), probability);
        sheet.SetCell(new CellAddress(sheet.Id, row, 6), cumulative);
        sheet.SetCell(new CellAddress(sheet.Id, row, 7), new BoolValue(active));
    }

    private static Workbook CreateFormulaTFChiSquareDistributionFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 10, 2);

        SetFormulaTFChiSquareDistributionFunctionContrastRow(
            sheet,
            1,
            new NumberValue(0.5),
            new NumberValue(5),
            new NumberValue(10),
            new NumberValue(0.5),
            new BoolValue(true),
            active: true,
            "Moderate");
        SetFormulaTFChiSquareDistributionFunctionContrastRow(
            sheet,
            2,
            new NumberValue(1.5),
            new NumberValue(10),
            new NumberValue(12),
            new NumberValue(0.8),
            new BoolValue(false),
            active: true,
            "Upper");
        SetFormulaTFChiSquareDistributionFunctionContrastRow(
            sheet,
            3,
            new NumberValue(2.5),
            new NumberValue(20),
            new NumberValue(15),
            new NumberValue(0.95),
            new BoolValue(true),
            active: true,
            "Tail");
        SetFormulaTFChiSquareDistributionFunctionContrastRow(
            sheet,
            4,
            new NumberValue(0.1),
            new NumberValue(3),
            new NumberValue(5),
            new NumberValue(0.2),
            new BoolValue(false),
            active: true,
            "Near zero");
        SetFormulaTFChiSquareDistributionFunctionContrastRow(
            sheet,
            5,
            new NumberValue(-1),
            new NumberValue(5),
            new NumberValue(10),
            new NumberValue(0.5),
            new BoolValue(true),
            active: false,
            "Negative x");
        SetFormulaTFChiSquareDistributionFunctionContrastRow(
            sheet,
            6,
            new NumberValue(0.5),
            new NumberValue(0),
            new NumberValue(10),
            new NumberValue(0.5),
            new BoolValue(true),
            active: false,
            "Zero df");
        SetFormulaTFChiSquareDistributionFunctionContrastRow(
            sheet,
            7,
            new NumberValue(0.5),
            new NumberValue(5),
            new NumberValue(0),
            new NumberValue(0),
            new BoolValue(true),
            active: false,
            "Zero probability");
        SetFormulaTFChiSquareDistributionFunctionContrastRow(
            sheet,
            8,
            ErrorValue.NA,
            new NumberValue(5),
            new NumberValue(10),
            ErrorValue.NA,
            new BoolValue(true),
            active: false,
            "NA source");
        SetFormulaTFChiSquareDistributionFunctionContrastRow(
            sheet,
            9,
            new TextValue("Open"),
            new TextValue("Open"),
            new NumberValue(10),
            new TextValue("Open"),
            new BoolValue(true),
            active: false,
            "Value source");
        SetFormulaTFChiSquareDistributionFunctionContrastRow(
            sheet,
            10,
            new NumberValue(0.5),
            new NumberValue(5),
            new NumberValue(10),
            new NumberValue(1),
            new BoolValue(true),
            active: false,
            "One probability");

        return workbook;
    }

    private static void SetFormulaTFChiSquareDistributionFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue x,
        ScalarValue df1,
        ScalarValue df2,
        ScalarValue probability,
        ScalarValue cumulative,
        bool active,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), x);
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), df1);
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), df2);
        sheet.SetCell(new CellAddress(sheet.Id, row, 5), probability);
        sheet.SetCell(new CellAddress(sheet.Id, row, 6), cumulative);
        sheet.SetCell(new CellAddress(sheet.Id, row, 7), new BoolValue(active));
    }

    private static Workbook CreateFormulaContinuousDistributionFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 7, 2);

        SetFormulaContinuousDistributionFunctionContrastRow(
            sheet,
            1,
            new NumberValue(0.5),
            new NumberValue(2),
            new NumberValue(2),
            new NumberValue(0.5),
            new BoolValue(true),
            active: true,
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(1),
            "Reference hit");
        SetFormulaContinuousDistributionFunctionContrastRow(
            sheet,
            2,
            new NumberValue(0.5),
            new NumberValue(0),
            new NumberValue(2),
            new NumberValue(0.5),
            new BoolValue(true),
            active: false,
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(1),
            "Zero alpha");
        SetFormulaContinuousDistributionFunctionContrastRow(
            sheet,
            3,
            new NumberValue(-1),
            new NumberValue(2),
            new NumberValue(2),
            new NumberValue(0.5),
            new BoolValue(true),
            active: false,
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(1),
            "Negative x");
        SetFormulaContinuousDistributionFunctionContrastRow(
            sheet,
            4,
            new NumberValue(0.5),
            new NumberValue(2),
            new NumberValue(0),
            new NumberValue(0.5),
            new BoolValue(true),
            active: false,
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(0),
            new NumberValue(0),
            new NumberValue(0),
            "Zero scale");
        SetFormulaContinuousDistributionFunctionContrastRow(
            sheet,
            5,
            new NumberValue(0.5),
            new NumberValue(2),
            new NumberValue(2),
            new NumberValue(1),
            new BoolValue(true),
            active: false,
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(1),
            "One probability");
        SetFormulaContinuousDistributionFunctionContrastRow(
            sheet,
            6,
            ErrorValue.NA,
            new NumberValue(2),
            new NumberValue(2),
            ErrorValue.NA,
            new BoolValue(true),
            active: false,
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(1),
            "NA source");
        SetFormulaContinuousDistributionFunctionContrastRow(
            sheet,
            7,
            new TextValue("Open"),
            new NumberValue(2),
            new NumberValue(2),
            new TextValue("Open"),
            new BoolValue(true),
            active: false,
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(1),
            "Value source");

        return workbook;
    }

    private static void SetFormulaContinuousDistributionFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue x,
        ScalarValue alpha,
        ScalarValue beta,
        ScalarValue probability,
        ScalarValue cumulative,
        bool active,
        ScalarValue lower,
        ScalarValue upper,
        ScalarValue mean,
        ScalarValue stdev,
        ScalarValue lambda,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), x);
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), alpha);
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), beta);
        sheet.SetCell(new CellAddress(sheet.Id, row, 5), probability);
        sheet.SetCell(new CellAddress(sheet.Id, row, 6), cumulative);
        sheet.SetCell(new CellAddress(sheet.Id, row, 7), new BoolValue(active));
        sheet.SetCell(new CellAddress(sheet.Id, row, 8), lower);
        sheet.SetCell(new CellAddress(sheet.Id, row, 9), upper);
        sheet.SetCell(new CellAddress(sheet.Id, row, 10), mean);
        sheet.SetCell(new CellAddress(sheet.Id, row, 11), stdev);
        sheet.SetCell(new CellAddress(sheet.Id, row, 12), lambda);
    }

    private static Workbook CreateFormulaDiscreteStatisticalFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 9, 2);

        SetFormulaDiscreteStatisticalFunctionContrastRow(
            sheet,
            1,
            new NumberValue(0.5),
            new NumberValue(4),
            new NumberValue(0.5),
            new NumberValue(0.5),
            new BoolValue(true),
            active: true,
            new NumberValue(10),
            new NumberValue(2),
            new NumberValue(1),
            new NumberValue(3),
            "Half success");
        SetFormulaDiscreteStatisticalFunctionContrastRow(
            sheet,
            2,
            new NumberValue(2),
            new NumberValue(4),
            new NumberValue(0.5),
            new NumberValue(0.7),
            new BoolValue(false),
            active: true,
            new NumberValue(10),
            new NumberValue(2),
            new NumberValue(1),
            new NumberValue(3),
            "Two successes");
        SetFormulaDiscreteStatisticalFunctionContrastRow(
            sheet,
            3,
            new NumberValue(3),
            new NumberValue(5),
            new NumberValue(0.25),
            new NumberValue(0.8),
            new BoolValue(true),
            active: true,
            new NumberValue(12),
            new NumberValue(3),
            new NumberValue(1),
            new NumberValue(2),
            "Three successes");
        SetFormulaDiscreteStatisticalFunctionContrastRow(
            sheet,
            4,
            new NumberValue(0),
            new NumberValue(3),
            new NumberValue(0.2),
            new NumberValue(0.2),
            new BoolValue(false),
            active: true,
            new NumberValue(8),
            new NumberValue(1),
            new NumberValue(1),
            new NumberValue(4),
            "Zero successes");
        SetFormulaDiscreteStatisticalFunctionContrastRow(
            sheet,
            5,
            new NumberValue(-1),
            new NumberValue(4),
            new NumberValue(0.5),
            new NumberValue(0.5),
            new BoolValue(true),
            active: false,
            new NumberValue(10),
            new NumberValue(2),
            new NumberValue(1),
            new NumberValue(3),
            "Negative source");
        SetFormulaDiscreteStatisticalFunctionContrastRow(
            sheet,
            6,
            new NumberValue(0.5),
            new NumberValue(-1),
            new NumberValue(0.5),
            new NumberValue(0.5),
            new BoolValue(true),
            active: false,
            new NumberValue(10),
            new NumberValue(2),
            new NumberValue(1),
            new NumberValue(3),
            "Negative count");
        SetFormulaDiscreteStatisticalFunctionContrastRow(
            sheet,
            7,
            new NumberValue(0.5),
            new NumberValue(4),
            new NumberValue(-0.1),
            new NumberValue(0.5),
            new BoolValue(true),
            active: false,
            new NumberValue(10),
            new NumberValue(2),
            new NumberValue(1),
            new NumberValue(3),
            "Invalid probability");
        SetFormulaDiscreteStatisticalFunctionContrastRow(
            sheet,
            8,
            ErrorValue.NA,
            new NumberValue(4),
            new NumberValue(0.5),
            ErrorValue.NA,
            new BoolValue(true),
            active: false,
            new NumberValue(10),
            new NumberValue(2),
            new NumberValue(1),
            new NumberValue(3),
            "NA source");
        SetFormulaDiscreteStatisticalFunctionContrastRow(
            sheet,
            9,
            new TextValue("Open"),
            new NumberValue(4),
            new NumberValue(0.5),
            new TextValue("Open"),
            new BoolValue(true),
            active: false,
            new NumberValue(10),
            new NumberValue(2),
            new NumberValue(1),
            new NumberValue(3),
            "Value source");

        return workbook;
    }

    private static void SetFormulaDiscreteStatisticalFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue x,
        ScalarValue count,
        ScalarValue probability,
        ScalarValue alpha,
        ScalarValue cumulative,
        bool active,
        ScalarValue populationSize,
        ScalarValue seriesN,
        ScalarValue seriesM,
        ScalarValue coefficient,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), x);
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), count);
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), probability);
        sheet.SetCell(new CellAddress(sheet.Id, row, 5), alpha);
        sheet.SetCell(new CellAddress(sheet.Id, row, 6), cumulative);
        sheet.SetCell(new CellAddress(sheet.Id, row, 7), new BoolValue(active));
        sheet.SetCell(new CellAddress(sheet.Id, row, 8), populationSize);
        sheet.SetCell(new CellAddress(sheet.Id, row, 9), seriesN);
        sheet.SetCell(new CellAddress(sheet.Id, row, 10), seriesM);
        sheet.SetCell(new CellAddress(sheet.Id, row, 11), coefficient);
    }

    private static Workbook CreateFormulaFinancialAnnuityFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 6, 2);

        SetFormulaFinancialAnnuityFunctionContrastRow(
            sheet,
            1,
            new NumberValue(0.05 / 12),
            new NumberValue(60),
            new NumberValue(10000),
            new NumberValue(188.71),
            new NumberValue(-188.71),
            new NumberValue(0),
            new NumberValue(0),
            new NumberValue(1),
            "Standard loan");
        SetFormulaFinancialAnnuityFunctionContrastRow(
            sheet,
            2,
            new NumberValue(0),
            new NumberValue(10),
            new NumberValue(1000),
            new NumberValue(100),
            new NumberValue(-100),
            new NumberValue(0),
            new NumberValue(0),
            new NumberValue(2),
            "Zero rate");
        SetFormulaFinancialAnnuityFunctionContrastRow(
            sheet,
            3,
            new NumberValue(0.05 / 12),
            new NumberValue(60),
            new NumberValue(10000),
            new NumberValue(195),
            new NumberValue(-195),
            new NumberValue(500),
            new NumberValue(1),
            new NumberValue(2),
            "Beginning period");
        SetFormulaFinancialAnnuityFunctionContrastRow(
            sheet,
            4,
            new NumberValue(0.05 / 12),
            new NumberValue(60),
            new NumberValue(10000),
            new NumberValue(188.71),
            new NumberValue(-188.71),
            new NumberValue(0),
            new NumberValue(2),
            new NumberValue(1),
            "Invalid type");
        SetFormulaFinancialAnnuityFunctionContrastRow(
            sheet,
            5,
            ErrorValue.NA,
            ErrorValue.NA,
            new NumberValue(10000),
            new NumberValue(188.71),
            new NumberValue(-188.71),
            new NumberValue(0),
            new NumberValue(0),
            new NumberValue(1),
            "NA source");
        SetFormulaFinancialAnnuityFunctionContrastRow(
            sheet,
            6,
            new NumberValue(0.05 / 12),
            new NumberValue(60),
            new NumberValue(10000),
            new NumberValue(188.71),
            new NumberValue(-188.71),
            new NumberValue(0),
            new NumberValue(0),
            new NumberValue(61),
            "Invalid period");

        return workbook;
    }

    private static void SetFormulaFinancialAnnuityFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue rate,
        ScalarValue nper,
        ScalarValue presentValue,
        ScalarValue positivePayment,
        ScalarValue outgoingPayment,
        ScalarValue futureValue,
        ScalarValue paymentType,
        ScalarValue period,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), rate);
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), nper);
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), presentValue);
        sheet.SetCell(new CellAddress(sheet.Id, row, 5), positivePayment);
        sheet.SetCell(new CellAddress(sheet.Id, row, 6), outgoingPayment);
        sheet.SetCell(new CellAddress(sheet.Id, row, 7), futureValue);
        sheet.SetCell(new CellAddress(sheet.Id, row, 8), paymentType);
        sheet.SetCell(new CellAddress(sheet.Id, row, 9), period);
    }

    private static Workbook CreateFormulaConvertFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 11, 2);

        SetFormulaConvertFunctionContrastRow(sheet, 1, new NumberValue(1), "kg", "g", flag: true, "Kilogram to grams");
        SetFormulaConvertFunctionContrastRow(sheet, 2, new NumberValue(1), "m", "cm", flag: true, "Meter to centimeters");
        SetFormulaConvertFunctionContrastRow(sheet, 3, new NumberValue(2), "dam", "m", flag: false, "Deka meter prefix");
        SetFormulaConvertFunctionContrastRow(sheet, 4, new NumberValue(2), "kibyte", "byte", flag: true, "Binary byte prefix");
        SetFormulaConvertFunctionContrastRow(sheet, 5, new NumberValue(32), "F", "C", flag: true, "Freezing point");
        SetFormulaConvertFunctionContrastRow(sheet, 6, new NumberValue(100), "C", "F", flag: false, "Boiling point");
        SetFormulaConvertFunctionContrastRow(sheet, 7, new NumberValue(1), "kg", "m", flag: true, "Category mismatch");
        SetFormulaConvertFunctionContrastRow(sheet, 8, new NumberValue(1), "foo", "g", flag: true, "Unknown unit");
        SetFormulaConvertFunctionContrastRow(sheet, 9, ErrorValue.Value, "m", "cm", flag: true, "Value error source");
        SetFormulaConvertFunctionContrastRow(sheet, 10, ErrorValue.NA, "m", "cm", flag: true, "NA error source");
        SetFormulaConvertFunctionContrastRow(sheet, 11, new NumberValue(1E308), "Yg", "yg", flag: true, "Nonfinite result");

        return workbook;
    }

    private static void SetFormulaConvertFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue number,
        string fromUnit,
        string toUnit,
        bool flag,
        string label) =>
        SetFormulaConvertFunctionContrastRow(
            sheet,
            row,
            number,
            new TextValue(fromUnit),
            new TextValue(toUnit),
            flag,
            label);

    private static void SetFormulaConvertFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue number,
        ScalarValue fromUnit,
        ScalarValue toUnit,
        bool flag,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), number);
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), fromUnit);
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), toUnit);
        sheet.SetCell(new CellAddress(sheet.Id, row, 5), new BoolValue(flag));
    }

    private static Workbook CreateFormulaComplexFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 9, 2);

        SetFormulaComplexFunctionContrastRow(
            sheet,
            1,
            new TextValue("3+4i"),
            new NumberValue(3),
            new NumberValue(4),
            new TextValue("i"),
            flag: true,
            "Three four i");
        SetFormulaComplexFunctionContrastRow(
            sheet,
            2,
            new TextValue("5-12j"),
            new NumberValue(5),
            new NumberValue(-12),
            new TextValue("j"),
            flag: true,
            "Five minus twelve j");
        SetFormulaComplexFunctionContrastRow(
            sheet,
            3,
            new TextValue("i"),
            new NumberValue(0),
            new NumberValue(1),
            BlankValue.Instance,
            flag: false,
            "Default suffix");
        SetFormulaComplexFunctionContrastRow(
            sheet,
            4,
            new TextValue("4"),
            new TextValue("1,234"),
            new TextValue("50%"),
            new TextValue("i"),
            flag: true,
            "Coerced constructor text");
        SetFormulaComplexFunctionContrastRow(
            sheet,
            5,
            new NumberValue(7),
            new NumberValue(0),
            new NumberValue(-1),
            new TextValue("j"),
            flag: false,
            "Numeric source");
        SetFormulaComplexFunctionContrastRow(
            sheet,
            6,
            new TextValue("0-j"),
            new NumberValue(3),
            new NumberValue(0),
            new TextValue("j"),
            flag: true,
            "Zero imaginary formatting");
        SetFormulaComplexFunctionContrastRow(
            sheet,
            7,
            new TextValue("not complex"),
            new NumberValue(1),
            new NumberValue(2),
            new TextValue("x"),
            flag: true,
            "Invalid text and suffix");
        SetFormulaComplexFunctionContrastRow(
            sheet,
            8,
            ErrorValue.NA,
            ErrorValue.NA,
            new NumberValue(2),
            new TextValue("i"),
            flag: true,
            "NA source");
        SetFormulaComplexFunctionContrastRow(
            sheet,
            9,
            new BoolValue(true),
            new TextValue("Open"),
            new NumberValue(2),
            new TextValue("i"),
            flag: true,
            "Value error source");

        return workbook;
    }

    private static void SetFormulaComplexFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue complexSource,
        ScalarValue realSource,
        ScalarValue imaginarySource,
        ScalarValue suffixSource,
        bool flag,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), complexSource);
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), realSource);
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), imaginarySource);
        sheet.SetCell(new CellAddress(sheet.Id, row, 5), suffixSource);
        sheet.SetCell(new CellAddress(sheet.Id, row, 6), new BoolValue(flag));
    }

    private static Workbook CreateFormulaArabicRomanFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 7, 2);

        SetFormulaArabicRomanFunctionContrastRow(sheet, 1, 12.9, "XII", true, "Roman twelve");
        SetFormulaArabicRomanFunctionContrastRow(sheet, 2, 49, " -iv ", true, "Negative four");
        SetFormulaArabicRomanFunctionContrastRow(sheet, 3, 0, string.Empty, true, "Empty roman");
        SetFormulaArabicRomanFunctionContrastRow(sheet, 4, 999, "  MIM  ", true, "Permissive high");
        SetFormulaArabicRomanFunctionContrastRow(sheet, 5, 99, "IC", true, "Permissive compact");
        SetFormulaArabicRomanFunctionContrastRow(sheet, 6, 4000, "IIV", true, "Invalid roman");
        SetFormulaArabicRomanFunctionContrastRow(sheet, 7, 255.8, new NumberValue(75), true, "Numeric roman source");

        return workbook;
    }

    private static void SetFormulaArabicRomanFunctionContrastRow(
        Sheet sheet,
        uint row,
        double number,
        string romanText,
        bool flag,
        string label) =>
        SetFormulaArabicRomanFunctionContrastRow(sheet, row, number, new TextValue(romanText), flag, label);

    private static void SetFormulaArabicRomanFunctionContrastRow(
        Sheet sheet,
        uint row,
        double number,
        ScalarValue romanSource,
        bool flag,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), new BoolValue(flag));
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), romanSource);
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), new NumberValue(number));
    }

    private static Workbook CreateFormulaUnicodeFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 9, 2);

        SetFormulaUnicodeFunctionContrastRow(sheet, 1, 65.9, new TextValue("Apple"), "ASCII text");
        SetFormulaUnicodeFunctionContrastRow(sheet, 2, 9731, new TextValue("\u2603snow"), "BMP text");
        SetFormulaUnicodeFunctionContrastRow(sheet, 3, 128512, new TextValue(char.ConvertFromUtf32(128512) + " grin"), "Supplementary text");
        SetFormulaUnicodeFunctionContrastRow(sheet, 4, 55296, new TextValue("ZA"), "First code point text");
        SetFormulaUnicodeFunctionContrastRow(sheet, 5, 0, new TextValue(string.Empty), "Empty text");
        SetFormulaUnicodeFunctionContrastRow(sheet, 6, 1114112, new NumberValue(65), "Numeric text coercion");
        SetFormulaUnicodeFunctionContrastRow(sheet, 7, -1, new BoolValue(true), "Boolean text coercion");
        SetFormulaUnicodeFunctionContrastRow(sheet, 8, 55296, new TextValue("\uD800A"), "Unpaired high surrogate");
        SetFormulaUnicodeFunctionContrastRow(sheet, 9, 56320, new TextValue("\uDC00"), "Unpaired low surrogate");

        return workbook;
    }

    private static void SetFormulaUnicodeFunctionContrastRow(
        Sheet sheet,
        uint row,
        double codePoint,
        ScalarValue unicodeSource,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(codePoint));
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), unicodeSource);
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), new BoolValue(true));
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

        SetFormulaDateFunctionContrastRow(sheet, 1, new DateTime(2023, 3, 15), "Closed", "March midpoint", 45000.25);
        SetFormulaDateFunctionContrastRow(sheet, 2, new DateTime(2023, 3, 16), "Closed", "March second half", 1.5242592592592593);
        SetFormulaDateFunctionContrastRow(sheet, 3, new DateTime(2024, 3, 20), "Open", "Next March", 2.0035532407407406);
        SetFormulaDateFunctionContrastRow(sheet, 4, new DateTime(2023, 4, 20), "Open", "April second half", 3.999988425925926);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), DateTimeValue.FromDateTime(new DateTime(2023, 3, 17)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), DateTimeValue.FromDateTime(new DateTime(2023, 3, 20)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 5), new TextValue("ignored"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 5), ErrorValue.NA);

        return workbook;
    }

    private static void SetFormulaDateFunctionContrastRow(
        Sheet sheet,
        uint row,
        DateTime date,
        string status,
        string label,
        double timeSerial)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), DateTimeValue.FromDateTime(date));
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), new TextValue(status));
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), new NumberValue(timeSerial));
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

    private static Workbook CreateFormulaLookupReferenceFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 5, 2);

        SetFormulaLookupReferenceFunctionContrastRow(sheet, 1, selector: 1, key: "Alpha", amount: 5, label: "Alpha row");
        SetFormulaLookupReferenceFunctionContrastRow(sheet, 2, selector: 2, key: "Beta", amount: 15, label: "Beta row");
        SetFormulaLookupReferenceFunctionContrastRow(sheet, 3, selector: 3, key: "Gamma", amount: 25, label: "Gamma row");
        SetFormulaLookupReferenceFunctionContrastRow(sheet, 4, selector: 2, key: "Beta", amount: 35, label: "Repeated beta");
        SetFormulaLookupReferenceFunctionContrastRow(sheet, 5, selector: 99, key: "Missing", amount: 45, label: "Missing row");

        SetFormulaLookupReferenceTableRow(sheet, 1, key: "Alpha", result: 10);
        SetFormulaLookupReferenceTableRow(sheet, 2, key: "Beta", result: 20);
        SetFormulaLookupReferenceTableRow(sheet, 3, key: "Gamma", result: 30);
        SetFormulaLookupReferenceTableRow(sheet, 4, key: "Delta", result: 40);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 8), new TextValue("Alpha"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 9), new TextValue("Beta"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 10), new TextValue("Gamma"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 11), new TextValue("Delta"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 8), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 9), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 10), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 11), new NumberValue(40));

        SetFormulaLookupReferenceApproximateRow(sheet, 1, threshold: 10, band: "Band1");
        SetFormulaLookupReferenceApproximateRow(sheet, 2, threshold: 20, band: "Band2");
        SetFormulaLookupReferenceApproximateRow(sheet, 3, threshold: 30, band: "Band3");
        SetFormulaLookupReferenceApproximateRow(sheet, 4, threshold: 40, band: "Band4");

        sheet.SetCell(new CellAddress(sheet.Id, 1, 16), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 17), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 18), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 19), new NumberValue(40));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 16), new TextValue("Band1"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 17), new TextValue("Band2"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 18), new TextValue("Band3"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 19), new TextValue("Band4"));

        sheet.SetCell(new CellAddress(sheet.Id, 1, 21), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 21), new NumberValue(30));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 21), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 21), new NumberValue(40));

        var quotedSheet = workbook.AddSheet("Quoted Sheet");
        quotedSheet.SetCell(new CellAddress(quotedSheet.Id, 1, 1), new NumberValue(20));

        return workbook;
    }

    private static void SetFormulaLookupReferenceFunctionContrastRow(
        Sheet sheet,
        uint row,
        double selector,
        string key,
        double amount,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), new NumberValue(selector));
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), new TextValue(key));
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), new NumberValue(amount));
    }

    private static void SetFormulaLookupReferenceTableRow(
        Sheet sheet,
        uint row,
        string key,
        double result)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 6), new TextValue(key));
        sheet.SetCell(new CellAddress(sheet.Id, row, 7), new NumberValue(result));
    }

    private static void SetFormulaLookupReferenceApproximateRow(
        Sheet sheet,
        uint row,
        double threshold,
        string band)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 13), new NumberValue(threshold));
        sheet.SetCell(new CellAddress(sheet.Id, row, 14), new TextValue(band));
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

    private static Workbook CreateFormulaInfoScalarFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 16, 2);

        SetFormulaInfoScalarFunctionContrastRow(sheet, 1, new NumberValue(42), "Number", 1);
        SetFormulaInfoScalarFunctionContrastRow(sheet, 2, new TextValue("hello"), "Text", 2);
        SetFormulaInfoScalarFunctionContrastRow(sheet, 3, new BoolValue(true), "TRUE", 3);
        SetFormulaInfoScalarFunctionContrastRow(sheet, 4, new DateTimeValue(45000), "Date serial", 4);
        SetFormulaInfoScalarFunctionContrastRow(sheet, 5, null, "Blank", 5);
        SetFormulaInfoScalarFunctionContrastRow(sheet, 6, ErrorValue.NA, "NA error", 1);
        SetFormulaInfoScalarFunctionContrastRow(sheet, 7, ErrorValue.DivByZero, "Division error", 1);
        SetFormulaInfoScalarFunctionContrastRow(sheet, 8, ErrorValue.Spill, "Spill error", 1);
        SetFormulaInfoScalarFunctionContrastRow(sheet, 9, new ErrorValue("#CONNECT!"), "Connect error", 1);
        SetFormulaInfoScalarFunctionContrastRow(sheet, 10, new ErrorValue("#BLOCKED!"), "Blocked error", 1);
        SetFormulaInfoScalarFunctionContrastRow(sheet, 11, new ErrorValue("#UNKNOWN!"), "Unknown error", 1);
        SetFormulaInfoScalarFunctionContrastRow(sheet, 12, new ErrorValue("#FIELD!"), "Field error", 1);
        SetFormulaInfoScalarFunctionContrastRow(sheet, 13, ErrorValue.Calc, "Calc error", 1);
        SetFormulaInfoScalarFunctionContrastRow(sheet, 14, new ErrorValue("#GETTING_DATA"), "Getting data error", 1);
        SetFormulaInfoScalarFunctionContrastRow(sheet, 15, new TextValue("123"), "Numeric text", 1);
        SetFormulaInfoScalarFunctionContrastRow(sheet, 16, new BoolValue(false), "FALSE", 1);

        return workbook;
    }

    private static void SetFormulaInfoScalarFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue? source,
        string label,
        double weight)
    {
        if (source is not null)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), source);

        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), new NumberValue(weight));
    }

    private static Workbook CreateFormulaInfoReferenceParityContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility")
        {
            CalculationMode = WorkbookCalculationMode.Manual
        };
        sheet = workbook.AddSheet("Sales");
        workbook.AddSheet("Data Set");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 5, 2);

        sheet.ColumnWidths[1] = 12.4;
        var centeredUnlocked = CellStyle.Default.Clone();
        centeredUnlocked.NumberFormat = "0";
        centeredUnlocked.HorizontalAlignment = HorizontalAlignment.Center;
        centeredUnlocked.Locked = false;
        var centeredUnlockedStyleId = workbook.RegisterStyle(centeredUnlocked);

        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), new TextValue("hello"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), new Cell
        {
            FormulaText = "SUM(1,1)",
            Value = new NumberValue(2)
        });
        sheet.SetCell(new CellAddress(sheet.Id, 5, 1), new Cell
        {
            Value = new NumberValue(7),
            StyleId = centeredUnlockedStyleId
        });

        for (uint row = 1; row <= 5; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue($"Info row {row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 3), new TextValue($"https://example.com/{row}"));
            sheet.SetCell(new CellAddress(sheet.Id, row, 4), new TextValue($"Link {row}"));
        }

        sheet.SetCell(new CellAddress(sheet.Id, 4, 3), new TextValue("https://example.com/friendly"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), new TextValue("Friendly"));

        sheet.SetCell(new CellAddress(sheet.Id, 1, 5), new TextValue("Row Labels"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 6), new TextValue("Sum of Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 5), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 6), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 5), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 6), new NumberValue(20));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 5), new TextValue("Grand Total"));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 6), new NumberValue(30));

        sheet.SetCell(new CellAddress(sheet.Id, 1, 8), new TextValue("Region"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 9), new TextValue("Amount"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 8), new TextValue("West"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 9), new NumberValue(10));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 8), new TextValue("East"));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 9), new NumberValue(20));

        var pivot = new PivotTableModel
        {
            Name = "SalesPivot",
            SourceRange = new GridRange(new CellAddress(sheet.Id, 1, 8), new CellAddress(sheet.Id, 3, 9)),
            TargetRange = new GridRange(new CellAddress(sheet.Id, 1, 5), new CellAddress(sheet.Id, 4, 6))
        };
        pivot.RowFields.Add(new PivotFieldModel(0));
        pivot.DataFields.Add(new PivotDataFieldModel(1, "Sum of Amount", "sum"));
        sheet.PivotTables.Add(pivot);

        return workbook;
    }

    private static Workbook CreateFormulaFinancialDepreciationFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 8, 2);

        SetFormulaFinancialDepreciationFunctionContrastRow(
            sheet,
            1,
            new NumberValue(1000),
            new NumberValue(100),
            new NumberValue(5),
            new NumberValue(1),
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(2),
            new NumberValue(12),
            new BoolValue(false),
            new NumberValue(0.1),
            new NumberValue(12),
            new NumberValue(100),
            new NumberValue(200),
            active: true,
            "Base annual");
        SetFormulaFinancialDepreciationFunctionContrastRow(
            sheet,
            2,
            new NumberValue(1000),
            new NumberValue(100),
            new NumberValue(5),
            new NumberValue(2),
            new NumberValue(1),
            new NumberValue(2),
            new NumberValue(2),
            new NumberValue(6),
            new BoolValue(false),
            new NumberValue(0.2),
            new NumberValue(4),
            new NumberValue(100),
            new NumberValue(121),
            active: true,
            "Later period");
        SetFormulaFinancialDepreciationFunctionContrastRow(
            sheet,
            3,
            new NumberValue(500),
            new NumberValue(50),
            new NumberValue(4),
            new NumberValue(3),
            new NumberValue(2),
            new NumberValue(3),
            new NumberValue(1.5),
            new NumberValue(12),
            new BoolValue(true),
            new NumberValue(0.05),
            new NumberValue(2),
            new NumberValue(100),
            new NumberValue(110),
            active: true,
            "Short life");
        SetFormulaFinancialDepreciationFunctionContrastRow(
            sheet,
            4,
            new NumberValue(300),
            new NumberValue(0),
            new NumberValue(3),
            new NumberValue(3),
            new NumberValue(0),
            new NumberValue(2),
            new NumberValue(2),
            new NumberValue(12),
            new BoolValue(false),
            new NumberValue(0.01),
            new NumberValue(1),
            new NumberValue(100),
            new NumberValue(105),
            active: false,
            "Inactive small");
        SetFormulaFinancialDepreciationFunctionContrastRow(
            sheet,
            5,
            ErrorValue.NA,
            new NumberValue(100),
            new NumberValue(5),
            new NumberValue(1),
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(2),
            new NumberValue(12),
            new BoolValue(false),
            ErrorValue.NA,
            new NumberValue(12),
            ErrorValue.NA,
            new NumberValue(200),
            active: false,
            "NA source");
        SetFormulaFinancialDepreciationFunctionContrastRow(
            sheet,
            6,
            new NumberValue(1000),
            new NumberValue(100),
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(0),
            new NumberValue(0),
            new BoolValue(false),
            new NumberValue(0),
            new NumberValue(0),
            new NumberValue(0),
            new NumberValue(200),
            active: false,
            "Invalid domain");
        SetFormulaFinancialDepreciationFunctionContrastRow(
            sheet,
            7,
            new TextValue("Open"),
            new NumberValue(100),
            new NumberValue(5),
            new NumberValue(1),
            new NumberValue(0),
            new NumberValue(1),
            new NumberValue(2),
            new NumberValue(12),
            new TextValue("Open"),
            new TextValue("Open"),
            new NumberValue(12),
            new TextValue("Open"),
            new NumberValue(200),
            active: false,
            "Value source");
        SetFormulaFinancialDepreciationFunctionContrastRow(
            sheet,
            8,
            new NumberValue(1000),
            new NumberValue(100),
            new NumberValue(5),
            new NumberValue(1.9),
            new NumberValue(0.5),
            new NumberValue(1.5),
            new NumberValue(1.5),
            new NumberValue(6.9),
            new BoolValue(true),
            new NumberValue(0.12),
            new NumberValue(12.9),
            new NumberValue(100),
            new NumberValue(150),
            active: true,
            "Fractional optional");

        return workbook;
    }

    private static void SetFormulaFinancialDepreciationFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue cost,
        ScalarValue salvage,
        ScalarValue life,
        ScalarValue period,
        ScalarValue startPeriod,
        ScalarValue endPeriod,
        ScalarValue factor,
        ScalarValue month,
        ScalarValue noSwitch,
        ScalarValue rate,
        ScalarValue npery,
        ScalarValue presentValue,
        ScalarValue futureValue,
        bool active,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), cost);
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), salvage);
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), life);
        sheet.SetCell(new CellAddress(sheet.Id, row, 5), period);
        sheet.SetCell(new CellAddress(sheet.Id, row, 6), startPeriod);
        sheet.SetCell(new CellAddress(sheet.Id, row, 7), endPeriod);
        sheet.SetCell(new CellAddress(sheet.Id, row, 8), factor);
        sheet.SetCell(new CellAddress(sheet.Id, row, 9), month);
        sheet.SetCell(new CellAddress(sheet.Id, row, 10), noSwitch);
        sheet.SetCell(new CellAddress(sheet.Id, row, 11), rate);
        sheet.SetCell(new CellAddress(sheet.Id, row, 12), npery);
        sheet.SetCell(new CellAddress(sheet.Id, row, 13), presentValue);
        sheet.SetCell(new CellAddress(sheet.Id, row, 14), futureValue);
        sheet.SetCell(new CellAddress(sheet.Id, row, 15), new BoolValue(active));
    }

    private static Workbook CreateFormulaFinancialCouponFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 8, 2);

        SetFormulaFinancialCouponFunctionContrastRow(
            sheet,
            1,
            new NumberValue(43831),
            new NumberValue(44197),
            new NumberValue(1),
            new NumberValue(0),
            "Annual start");
        SetFormulaFinancialCouponFunctionContrastRow(
            sheet,
            2,
            new NumberValue(43845),
            new NumberValue(44197),
            new NumberValue(2),
            new NumberValue(0),
            "Semiannual partial");
        SetFormulaFinancialCouponFunctionContrastRow(
            sheet,
            3,
            new NumberValue(43921),
            new NumberValue(44197),
            new NumberValue(4),
            new NumberValue(1),
            "Quarter actual");
        SetFormulaFinancialCouponFunctionContrastRow(
            sheet,
            4,
            new NumberValue(44016),
            new NumberValue(44197),
            new NumberValue(2),
            new NumberValue(4),
            "European basis");
        SetFormulaFinancialCouponFunctionContrastRow(
            sheet,
            5,
            ErrorValue.NA,
            new NumberValue(44197),
            new NumberValue(2),
            new NumberValue(0),
            "NA settlement");
        SetFormulaFinancialCouponFunctionContrastRow(
            sheet,
            6,
            new TextValue("Open"),
            new NumberValue(44197),
            new NumberValue(2),
            new NumberValue(0),
            "Value settlement");
        SetFormulaFinancialCouponFunctionContrastRow(
            sheet,
            7,
            new NumberValue(44197),
            new NumberValue(43831),
            new NumberValue(2),
            new NumberValue(0),
            "Invalid order");
        SetFormulaFinancialCouponFunctionContrastRow(
            sheet,
            8,
            new NumberValue(43845),
            new NumberValue(44562),
            new NumberValue(4),
            new NumberValue(1),
            "Long quarterly");

        return workbook;
    }

    private static void SetFormulaFinancialCouponFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue settlement,
        ScalarValue maturity,
        ScalarValue frequency,
        ScalarValue basis,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), settlement);
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), maturity);
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), frequency);
        sheet.SetCell(new CellAddress(sheet.Id, row, 5), basis);
    }

    private static Workbook CreateFormulaFinancialBillDiscountFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 6, 2);

        SetFormulaFinancialBillDiscountFunctionContrastRow(
            sheet,
            1,
            new NumberValue(43831),
            new NumberValue(44197),
            new NumberValue(43831),
            new NumberValue(97),
            new NumberValue(90),
            new NumberValue(100),
            new NumberValue(0.05),
            new NumberValue(0.05),
            new NumberValue(0.05),
            new NumberValue(0),
            new NumberValue(43921),
            active: true,
            "Strong discount");
        SetFormulaFinancialBillDiscountFunctionContrastRow(
            sheet,
            2,
            new NumberValue(43831),
            new NumberValue(44197),
            new NumberValue(43831),
            new NumberValue(99),
            new NumberValue(99),
            new NumberValue(100),
            new NumberValue(0.01),
            new NumberValue(0.02),
            new NumberValue(0.10),
            new NumberValue(1),
            new NumberValue(43921),
            active: true,
            "Small discount");
        SetFormulaFinancialBillDiscountFunctionContrastRow(
            sheet,
            3,
            new NumberValue(43845),
            new NumberValue(44228),
            new NumberValue(43831),
            new NumberValue(95),
            new NumberValue(95),
            new NumberValue(110),
            new NumberValue(0.04),
            new NumberValue(0.08),
            new NumberValue(0.06),
            new NumberValue(4),
            new NumberValue(43935),
            active: true,
            "Premium redemption");
        SetFormulaFinancialBillDiscountFunctionContrastRow(
            sheet,
            4,
            ErrorValue.NA,
            new NumberValue(44197),
            new NumberValue(43831),
            new NumberValue(97),
            new NumberValue(90),
            new NumberValue(100),
            new NumberValue(0.05),
            new NumberValue(0.05),
            new NumberValue(0.05),
            new NumberValue(0),
            new NumberValue(43921),
            active: true,
            "NA settlement");
        SetFormulaFinancialBillDiscountFunctionContrastRow(
            sheet,
            5,
            new NumberValue(44197),
            new NumberValue(43831),
            new NumberValue(43831),
            new NumberValue(97),
            new NumberValue(90),
            new NumberValue(100),
            new NumberValue(0.05),
            new NumberValue(0.05),
            new NumberValue(0.05),
            new NumberValue(0),
            new NumberValue(43921),
            active: true,
            "Invalid order");
        SetFormulaFinancialBillDiscountFunctionContrastRow(
            sheet,
            6,
            new NumberValue(43831),
            new NumberValue(44197),
            new NumberValue(43831),
            new NumberValue(97),
            new NumberValue(90),
            new NumberValue(100),
            new TextValue("Open"),
            new NumberValue(0.05),
            new NumberValue(0.05),
            new NumberValue(0),
            new NumberValue(43921),
            active: true,
            "Invalid discount");

        return workbook;
    }

    private static void SetFormulaFinancialBillDiscountFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue settlement,
        ScalarValue maturity,
        ScalarValue issue,
        ScalarValue price,
        ScalarValue investment,
        ScalarValue redemption,
        ScalarValue discount,
        ScalarValue rate,
        ScalarValue yieldRate,
        ScalarValue basis,
        ScalarValue billMaturity,
        bool active,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), settlement);
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), maturity);
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), issue);
        sheet.SetCell(new CellAddress(sheet.Id, row, 5), price);
        sheet.SetCell(new CellAddress(sheet.Id, row, 6), investment);
        sheet.SetCell(new CellAddress(sheet.Id, row, 7), redemption);
        sheet.SetCell(new CellAddress(sheet.Id, row, 8), discount);
        sheet.SetCell(new CellAddress(sheet.Id, row, 9), rate);
        sheet.SetCell(new CellAddress(sheet.Id, row, 10), yieldRate);
        sheet.SetCell(new CellAddress(sheet.Id, row, 11), basis);
        sheet.SetCell(new CellAddress(sheet.Id, row, 12), billMaturity);
        sheet.SetCell(new CellAddress(sheet.Id, row, 13), new BoolValue(active));
    }

    private static Workbook CreateFormulaFinancialBondYieldFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 8, 2);

        SetFormulaFinancialBondYieldFunctionContrastRow(
            sheet,
            1,
            new NumberValue(43831),
            new NumberValue(45658),
            new NumberValue(0.08),
            new NumberValue(0.05),
            new NumberValue(99),
            new NumberValue(100),
            new NumberValue(2),
            new NumberValue(0),
            new NumberValue(44197),
            new NumberValue(95),
            "Premium semiannual");
        SetFormulaFinancialBondYieldFunctionContrastRow(
            sheet,
            2,
            new NumberValue(43845),
            new NumberValue(45672),
            new NumberValue(0.07),
            new NumberValue(0.06),
            new NumberValue(101),
            new NumberValue(110),
            new NumberValue(4),
            new NumberValue(1),
            new NumberValue(44228),
            new NumberValue(101),
            "Quarter actual");
        SetFormulaFinancialBondYieldFunctionContrastRow(
            sheet,
            3,
            new NumberValue(43831),
            new NumberValue(44197),
            new NumberValue(0.03),
            new NumberValue(0.08),
            new NumberValue(105),
            new NumberValue(100),
            new NumberValue(1),
            new NumberValue(0),
            new NumberValue(44197),
            new NumberValue(105),
            "Discount below threshold");
        SetFormulaFinancialBondYieldFunctionContrastRow(
            sheet,
            4,
            new NumberValue(44016),
            new NumberValue(44197),
            new NumberValue(0.08),
            new NumberValue(0.05),
            new NumberValue(100),
            new NumberValue(100),
            new NumberValue(2),
            new NumberValue(0),
            new NumberValue(44197),
            new NumberValue(98),
            "Short remaining");
        SetFormulaFinancialBondYieldFunctionContrastRow(
            sheet,
            5,
            ErrorValue.NA,
            new NumberValue(45658),
            new NumberValue(0.08),
            new NumberValue(0.05),
            new NumberValue(99),
            new NumberValue(100),
            new NumberValue(2),
            new NumberValue(0),
            new NumberValue(44197),
            new NumberValue(95),
            "NA settlement");
        SetFormulaFinancialBondYieldFunctionContrastRow(
            sheet,
            6,
            new NumberValue(43831),
            new NumberValue(43831),
            new NumberValue(0.08),
            new NumberValue(0.05),
            new NumberValue(99),
            new NumberValue(100),
            new NumberValue(2),
            new NumberValue(0),
            new NumberValue(43831),
            new NumberValue(95),
            "Invalid order");
        SetFormulaFinancialBondYieldFunctionContrastRow(
            sheet,
            7,
            new TextValue("Open"),
            new NumberValue(45658),
            new NumberValue(0.08),
            new NumberValue(0.05),
            new NumberValue(99),
            new NumberValue(100),
            new NumberValue(2),
            new NumberValue(0),
            new NumberValue(44197),
            new NumberValue(95),
            "Value settlement");
        SetFormulaFinancialBondYieldFunctionContrastRow(
            sheet,
            8,
            new NumberValue(43831),
            new NumberValue(45658),
            new NumberValue(0.08),
            new NumberValue(0.05),
            new NumberValue(99),
            new NumberValue(100),
            new NumberValue(2.9),
            new NumberValue(0.9),
            new NumberValue(44197),
            new NumberValue(95),
            "Fractional optional");

        return workbook;
    }

    private static void SetFormulaFinancialBondYieldFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue settlement,
        ScalarValue maturity,
        ScalarValue couponRate,
        ScalarValue yield,
        ScalarValue price,
        ScalarValue redemption,
        ScalarValue frequency,
        ScalarValue basis,
        ScalarValue shortMaturity,
        ScalarValue discountPrice,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), settlement);
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), maturity);
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), couponRate);
        sheet.SetCell(new CellAddress(sheet.Id, row, 5), yield);
        sheet.SetCell(new CellAddress(sheet.Id, row, 6), price);
        sheet.SetCell(new CellAddress(sheet.Id, row, 7), redemption);
        sheet.SetCell(new CellAddress(sheet.Id, row, 8), frequency);
        sheet.SetCell(new CellAddress(sheet.Id, row, 9), basis);
        sheet.SetCell(new CellAddress(sheet.Id, row, 10), shortMaturity);
        sheet.SetCell(new CellAddress(sheet.Id, row, 11), discountPrice);
    }

    private static Workbook CreateFormulaFinancialOddCouponFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 9, 2);

        SetFormulaFinancialOddCouponFunctionContrastRow(
            sheet,
            1,
            new NumberValue(43900),
            new NumberValue(44562),
            new NumberValue(43831),
            new NumberValue(44197),
            new NumberValue(0.05),
            new NumberValue(0.05),
            new NumberValue(99),
            new NumberValue(100),
            new NumberValue(2),
            new NumberValue(0),
            new NumberValue(44197),
            active: true,
            "Odd first par");
        SetFormulaFinancialOddCouponFunctionContrastRow(
            sheet,
            2,
            new NumberValue(43910),
            new NumberValue(44592),
            new NumberValue(43840),
            new NumberValue(44228),
            new NumberValue(0.06),
            new NumberValue(0.07),
            new NumberValue(101),
            new NumberValue(110),
            new NumberValue(4),
            new NumberValue(1),
            new NumberValue(44228),
            active: false,
            "Quarter actual");
        SetFormulaFinancialOddCouponFunctionContrastRow(
            sheet,
            3,
            new NumberValue(43900),
            new NumberValue(44562),
            new NumberValue(43831),
            new NumberValue(44197),
            new NumberValue(0.02),
            new NumberValue(0.10),
            new NumberValue(105),
            new NumberValue(100),
            new NumberValue(2),
            new NumberValue(0),
            new NumberValue(44197),
            active: true,
            "Below threshold");
        SetFormulaFinancialOddCouponFunctionContrastRow(
            sheet,
            4,
            new NumberValue(44000),
            new NumberValue(44562),
            new NumberValue(43831),
            new NumberValue(44197),
            new NumberValue(0.08),
            new NumberValue(0.04),
            new NumberValue(98),
            new NumberValue(100),
            new NumberValue(2),
            new NumberValue(0),
            new NumberValue(44197),
            active: true,
            "High coupon");
        SetFormulaFinancialOddCouponFunctionContrastRow(
            sheet,
            5,
            ErrorValue.NA,
            new NumberValue(44562),
            new NumberValue(43831),
            new NumberValue(44197),
            new NumberValue(0.05),
            new NumberValue(0.05),
            new NumberValue(99),
            new NumberValue(100),
            new NumberValue(2),
            new NumberValue(0),
            new NumberValue(44197),
            active: true,
            "NA settlement");
        SetFormulaFinancialOddCouponFunctionContrastRow(
            sheet,
            6,
            new NumberValue(43900),
            new NumberValue(44562),
            new NumberValue(43831),
            new NumberValue(43890),
            new NumberValue(0.05),
            new NumberValue(0.05),
            new NumberValue(99),
            new NumberValue(100),
            new NumberValue(2),
            new NumberValue(0),
            new NumberValue(43890),
            active: true,
            "Invalid order");
        SetFormulaFinancialOddCouponFunctionContrastRow(
            sheet,
            7,
            new TextValue("Open"),
            new NumberValue(44562),
            new NumberValue(43831),
            new NumberValue(44197),
            new NumberValue(0.05),
            new NumberValue(0.05),
            new NumberValue(99),
            new NumberValue(100),
            new NumberValue(2),
            new NumberValue(0),
            new NumberValue(44197),
            active: true,
            "Value settlement");
        SetFormulaFinancialOddCouponFunctionContrastRow(
            sheet,
            8,
            new NumberValue(43900),
            new NumberValue(44562),
            new NumberValue(43831),
            new NumberValue(44197),
            new NumberValue(0.05),
            new NumberValue(0.05),
            new NumberValue(99),
            new NumberValue(100),
            new NumberValue(2.9),
            new NumberValue(0.9),
            new NumberValue(44197),
            active: true,
            "Fractional optional");
        SetFormulaFinancialOddCouponFunctionContrastRow(
            sheet,
            9,
            new NumberValue(43900),
            new NumberValue(44562),
            new NumberValue(43831),
            new NumberValue(44197),
            new NumberValue(0.05),
            new NumberValue(0.05),
            new NumberValue(99),
            new NumberValue(100),
            new NumberValue(3),
            new NumberValue(0),
            new NumberValue(44197),
            active: true,
            "Invalid frequency");

        return workbook;
    }

    private static void SetFormulaFinancialOddCouponFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue settlement,
        ScalarValue maturity,
        ScalarValue issueOrLastInterest,
        ScalarValue firstCoupon,
        ScalarValue rate,
        ScalarValue yieldRate,
        ScalarValue price,
        ScalarValue redemption,
        ScalarValue frequency,
        ScalarValue basis,
        ScalarValue shortMaturity,
        bool active,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), settlement);
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), maturity);
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), issueOrLastInterest);
        sheet.SetCell(new CellAddress(sheet.Id, row, 5), firstCoupon);
        sheet.SetCell(new CellAddress(sheet.Id, row, 6), rate);
        sheet.SetCell(new CellAddress(sheet.Id, row, 7), yieldRate);
        sheet.SetCell(new CellAddress(sheet.Id, row, 8), price);
        sheet.SetCell(new CellAddress(sheet.Id, row, 9), redemption);
        sheet.SetCell(new CellAddress(sheet.Id, row, 10), frequency);
        sheet.SetCell(new CellAddress(sheet.Id, row, 11), basis);
        sheet.SetCell(new CellAddress(sheet.Id, row, 12), shortMaturity);
        sheet.SetCell(new CellAddress(sheet.Id, row, 13), new BoolValue(active));
    }

    private static Workbook CreateFormulaFinancialAccrualScheduleFunctionContrastWorkbook(
        out Sheet sheet,
        out CellAddress firstLabel,
        out CellAddress lastLabel)
    {
        var workbook = new Workbook("Accessibility");
        sheet = workbook.AddSheet("Sales");
        firstLabel = new CellAddress(sheet.Id, 1, 2);
        lastLabel = new CellAddress(sheet.Id, 8, 2);

        SetFormulaFinancialAccrualScheduleFunctionContrastRow(
            sheet,
            1,
            new NumberValue(43831),
            new NumberValue(43831),
            new NumberValue(44197),
            new NumberValue(0.05),
            new NumberValue(1000),
            new NumberValue(2),
            new NumberValue(0),
            new NumberValue(100),
            new NumberValue(0.10),
            new NumberValue(0.05),
            new NumberValue(1.02),
            new NumberValue(1.125),
            new NumberValue(16),
            "Annual accrual");
        SetFormulaFinancialAccrualScheduleFunctionContrastRow(
            sheet,
            2,
            new NumberValue(43862),
            new NumberValue(43862),
            new NumberValue(44228),
            new NumberValue(0.06),
            new NumberValue(1200),
            new NumberValue(4),
            new NumberValue(3),
            new NumberValue(200),
            new NumberValue(-0.05),
            new NumberValue(0.10),
            new NumberValue(2.16),
            new NumberValue(2.5),
            new NumberValue(16),
            "Actual 365 schedule");
        SetFormulaFinancialAccrualScheduleFunctionContrastRow(
            sheet,
            3,
            new NumberValue(43831),
            new NumberValue(43831),
            new NumberValue(43921),
            new NumberValue(0.02),
            new NumberValue(1000),
            new NumberValue(2),
            new NumberValue(0),
            new NumberValue(50),
            new NumberValue(0.01),
            new NumberValue(0.02),
            new NumberValue(1.01),
            new NumberValue(1.03125),
            new NumberValue(32),
            "Small accrual");
        SetFormulaFinancialAccrualScheduleFunctionContrastRow(
            sheet,
            4,
            new NumberValue(43831),
            new NumberValue(43921),
            new NumberValue(44197),
            new NumberValue(0.03),
            new NumberValue(1000),
            new NumberValue(2),
            new NumberValue(0),
            new NumberValue(80),
            new NumberValue(0.05),
            new NumberValue(0.05),
            new NumberValue(1.04),
            new NumberValue(1.125),
            new NumberValue(32),
            "First interest partial");
        SetFormulaFinancialAccrualScheduleFunctionContrastRow(
            sheet,
            5,
            ErrorValue.NA,
            new NumberValue(43831),
            new NumberValue(44197),
            new NumberValue(0.05),
            new NumberValue(1000),
            new NumberValue(2),
            new NumberValue(0),
            new NumberValue(100),
            ErrorValue.NA,
            new NumberValue(0.05),
            ErrorValue.NA,
            ErrorValue.NA,
            new NumberValue(16),
            "NA inputs");
        SetFormulaFinancialAccrualScheduleFunctionContrastRow(
            sheet,
            6,
            new NumberValue(44197),
            new NumberValue(43831),
            new NumberValue(43831),
            new NumberValue(0.05),
            new NumberValue(1000),
            new NumberValue(2),
            new NumberValue(0),
            new TextValue("Open"),
            new TextValue("Open"),
            new NumberValue(0.05),
            new NumberValue(1.02),
            new NumberValue(1.125),
            new NumberValue(-1),
            "Invalid order");
        SetFormulaFinancialAccrualScheduleFunctionContrastRow(
            sheet,
            7,
            new NumberValue(43831),
            new NumberValue(43831),
            new NumberValue(44197),
            new NumberValue(0.01),
            new NumberValue(1000),
            new NumberValue(2),
            new NumberValue(5),
            new NumberValue(100),
            ErrorValue.Value,
            new NumberValue(0.05),
            new NumberValue(1.02),
            new NumberValue(1.125),
            new NumberValue(0),
            "Invalid options");
        SetFormulaFinancialAccrualScheduleFunctionContrastRow(
            sheet,
            8,
            new NumberValue(43831),
            new NumberValue(43831),
            new NumberValue(44197),
            new NumberValue(0.04),
            new NumberValue(1000),
            new NumberValue(2.9),
            new NumberValue(0.9),
            new NumberValue(150),
            new NumberValue(0.20),
            BlankValue.Instance,
            new NumberValue(3.04),
            new NumberValue(3.125),
            new NumberValue(32),
            "Truncated options");

        return workbook;
    }

    private static void SetFormulaFinancialAccrualScheduleFunctionContrastRow(
        Sheet sheet,
        uint row,
        ScalarValue issue,
        ScalarValue firstInterest,
        ScalarValue settlement,
        ScalarValue rate,
        ScalarValue par,
        ScalarValue frequency,
        ScalarValue basis,
        ScalarValue principal,
        ScalarValue scheduleFirst,
        ScalarValue scheduleSecond,
        ScalarValue fractionalDollar,
        ScalarValue decimalDollar,
        ScalarValue fraction,
        string label)
    {
        sheet.SetCell(new CellAddress(sheet.Id, row, 1), issue);
        sheet.SetCell(new CellAddress(sheet.Id, row, 2), new TextValue(label));
        sheet.SetCell(new CellAddress(sheet.Id, row, 3), firstInterest);
        sheet.SetCell(new CellAddress(sheet.Id, row, 4), settlement);
        sheet.SetCell(new CellAddress(sheet.Id, row, 5), rate);
        sheet.SetCell(new CellAddress(sheet.Id, row, 6), par);
        sheet.SetCell(new CellAddress(sheet.Id, row, 7), frequency);
        sheet.SetCell(new CellAddress(sheet.Id, row, 8), basis);
        sheet.SetCell(new CellAddress(sheet.Id, row, 9), principal);
        sheet.SetCell(new CellAddress(sheet.Id, row, 10), scheduleFirst);
        sheet.SetCell(new CellAddress(sheet.Id, row, 11), scheduleSecond);
        sheet.SetCell(new CellAddress(sheet.Id, row, 12), fractionalDollar);
        sheet.SetCell(new CellAddress(sheet.Id, row, 13), decimalDollar);
        sheet.SetCell(new CellAddress(sheet.Id, row, 14), fraction);
    }

    // Formula-type CF expectations intentionally follow the canonical Formula/Calc contracts:
    // errors and non-scalar arrays fail closed; all other coercion and function semantics are shared.
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

    private static void AssertFormulaDatabaseAggregateContrastLocations(string formulaText, params string[] expectedLocations)
    {
        var workbook = CreateFormulaDatabaseAggregateContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaStatisticalSelectionContrastLocations(string formulaText, params string[] expectedLocations)
    {
        var workbook = CreateFormulaStatisticalSelectionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaStatisticalTestContrastLocations(string formulaText, params string[] expectedLocations)
    {
        var workbook = CreateFormulaStatisticalTestContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaFinancialCashFlowFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaFinancialCashFlowFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
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

    private static void AssertFormulaMatrixArrayFunctionContrastLocations(string formulaText, params string[] expectedLocations)
    {
        var workbook = CreateFormulaMatrixArrayFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaDynamicArrayFunctionContrastLocations(string formulaText, params string[] expectedLocations)
    {
        var workbook = CreateFormulaMatrixArrayFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaFinancialDepreciationFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaFinancialDepreciationFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaFinancialCouponFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaFinancialCouponFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaFinancialBillDiscountFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaFinancialBillDiscountFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaFinancialBondYieldFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaFinancialBondYieldFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaFinancialOddCouponFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaFinancialOddCouponFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaFinancialAccrualScheduleFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaFinancialAccrualScheduleFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
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

    private static void AssertFormulaNumberValueFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaNumberValueFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);

        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaDateValueTimeValueFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaDateValueTimeValueFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaArabicRomanFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaArabicRomanFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaBaseConversionFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaBaseConversionFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);

        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaNormalDistributionFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaNormalDistributionFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);

        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaTFChiSquareDistributionFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaTFChiSquareDistributionFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);

        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaContinuousDistributionFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaContinuousDistributionFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);

        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaDiscreteStatisticalFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaDiscreteStatisticalFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);

        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaFinancialAnnuityFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaFinancialAnnuityFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);

        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaUnicodeFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaUnicodeFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);

        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaConvertFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaConvertFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaComplexFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaComplexFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
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

    private static void AssertFormulaLookupReferenceFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaLookupReferenceFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaInfoScalarFunctionContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaInfoScalarFunctionContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
        AddFormulaContrastRule(sheet, firstLabel, lastLabel, formulaText);

        FindLowContrastCellTextIssues(workbook)
            .Select(issue => issue.Location)
            .Should()
            .Equal(expectedLocations);
    }

    private static void AssertFormulaInfoReferenceParityContrastLocations(
        string formulaText,
        params string[] expectedLocations)
    {
        var workbook = CreateFormulaInfoReferenceParityContrastWorkbook(out var sheet, out var firstLabel, out var lastLabel);
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

    private static string[] FormulaDateValueTimeValueAllLocations =>
        ["B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9", "B10"];

    private static string[] FormulaArabicRomanAllLocations =>
        ["B1", "B2", "B3", "B4", "B5", "B6", "B7"];

    private static string[] FormulaBaseConversionAllLocations =>
        ["B1", "B2", "B3", "B4", "B5", "B6", "B7"];

    private static string[] FormulaConvertNumericLocations =>
        ["B1", "B2", "B3", "B4", "B5", "B6"];

    private static string[] FormulaComplexAllLocations =>
        ["B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9"];

    private static string[] FormulaNumberValueAllLocations =>
        ["B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8"];

    private static string[] FormulaTextFunctionAllLocations =>
        ["B1", "B2", "B3", "B4"];

    private static string[] FormulaUnicodeAllLocations =>
        ["B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9"];

    private static string[] FormulaInfoScalarAllLocations =>
        ["B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8", "B9", "B10", "B11", "B12", "B13", "B14", "B15", "B16"];

    private static string[] FormulaInfoReferenceParityAllLocations =>
        ["B1", "B2", "B3", "B4", "B5"];

    private static string[] FormulaLookupReferenceAllLocations =>
        ["B1", "B2", "B3", "B4", "B5"];

    private static string[] FormulaMatrixArrayAllLocations =>
        ["B1", "B2", "B3", "B4"];

    private static string[] FormulaStatisticalSelectionAllLocations =>
        ["B1", "B2", "B3", "B4"];

    private static string[] FormulaStatisticalTestAllLocations =>
        ["B1", "B2", "B3", "B4"];

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
