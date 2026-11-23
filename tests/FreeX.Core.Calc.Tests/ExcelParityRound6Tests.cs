using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Calc.Tests;

/// <summary>
/// Excel-parity round 6. Every expectation here was measured against real Excel through COM
/// (Range.Sort for the sort cases, Validation.Value for data validation, Range.DisplayFormat for
/// conditional formatting) -- see the commit message for the probe description.
/// </summary>
public sealed class ExcelParityRound6Tests
{
    // ───────────────────────────── Sort ─────────────────────────────

    private static List<string> SortTexts(IReadOnlyList<string> items, bool ascending = true, bool caseSensitive = false)
    {
        var (_, sheet, ctx) = TestWorkbookFixture.CreateContext();
        for (var i = 0; i < items.Count; i++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)i + 1, 1), Cell.FromValue(new TextValue(items[i])));

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, (uint)items.Count, 1));
        new SortCommand(sheet.Id, range, [new SortKey(0, ascending)], new SortOptions(caseSensitive)).Apply(ctx)
            .Success.Should().BeTrue();

        return Enumerable.Range(1, items.Count)
            .Select(r => ((TextValue)sheet.GetValue((uint)r, 1)).Value)
            .ToList();
    }

    [Fact]
    public void Sort_PunctuationLeadingKeys_FollowExcelsSymbolOrder()
    {
        // Measured order of the symbol characters (all of them precede digits and letters).
        var excelOrder = new[] { "!a", "#a", "$a", "%a", "&a", "(a", "*a", ".a", "?a", "@a", "_a", "~a", "+a", "=a", "1a", "a" };
        var shuffled = excelOrder.Reverse().ToList();

        SortTexts(shuffled).Should().Equal(excelOrder);
    }

    [Fact]
    public void Sort_HyphenAndApostropheAreIgnoredAtTheFirstLevelThenBreakTies()
    {
        // Excel word sort: "coop" == "co-op" at the primary level; ties order by where the ignored
        // character sits (later in the string first, apostrophe before hyphen, leading hyphen last).
        SortTexts(["-coop", "c-oop", "co-op", "co'op", "coop"])
            .Should().Equal("coop", "co'op", "co-op", "c-oop", "-coop");
    }

    [Fact]
    public void Sort_LeadingApostropheIsFullyIgnored_SoEqualRowsKeepInputOrder()
    {
        SortTexts(["'x", "x"]).Should().Equal("'x", "x");
        SortTexts(["x", "'x"]).Should().Equal("x", "'x");
    }

    [Fact]
    public void Sort_CaseSensitive_PutsLowercaseFirstAndCaseInsensitiveKeepsInputOrder()
    {
        SortTexts(["B", "a", "A", "b"], caseSensitive: true).Should().Equal("a", "A", "b", "B");
        SortTexts(["B", "a", "A", "b"], caseSensitive: false).Should().Equal("a", "A", "B", "b");
        SortTexts(["A", "a"], caseSensitive: false).Should().Equal("A", "a");
    }

    [Fact]
    public void Sort_AccentedLettersStayAfterTheirPlainLetter()
    {
        SortTexts(["résumé", "Resume"]).Should().Equal("Resume", "résumé");
    }

    private static List<ScalarValue> SortValues(ScalarValue?[] cells, bool ascending)
    {
        var (_, sheet, ctx) = TestWorkbookFixture.CreateContext();
        for (var i = 0; i < cells.Length; i++)
        {
            if (cells[i] is { } value)
                sheet.SetCell(new CellAddress(sheet.Id, (uint)i + 1, 1), Cell.FromValue(value));
        }

        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, (uint)cells.Length, 1));
        new SortCommand(sheet.Id, range, [new SortKey(0, ascending)]).Apply(ctx).Success.Should().BeTrue();
        return Enumerable.Range(1, cells.Length).Select(r => sheet.GetValue((uint)r, 1)).ToList();
    }

    [Fact]
    public void Sort_Ascending_OrdersNumbersTextLogicalsErrorsThenBlanks()
    {
        var result = SortValues(
            [null, ErrorValue.NA, new BoolValue(true), new TextValue("b"), new NumberValue(2), ErrorValue.DivByZero, new TextValue("a"), new NumberValue(-1)],
            ascending: true);

        result.Should().Equal(
            new NumberValue(-1), new NumberValue(2), new TextValue("a"), new TextValue("b"),
            new BoolValue(true), ErrorValue.NA, ErrorValue.DivByZero, BlankValue.Instance);
    }

    [Fact]
    public void Sort_Descending_PutsErrorsFirstAndOnlyBlanksLast_ErrorsKeepTheirRelativeOrder()
    {
        var result = SortValues(
            [null, ErrorValue.NA, new BoolValue(true), new TextValue("b"), new NumberValue(2), ErrorValue.DivByZero, new TextValue("a"), new NumberValue(-1)],
            ascending: false);

        result.Should().Equal(
            ErrorValue.NA, ErrorValue.DivByZero, new BoolValue(true), new TextValue("b"),
            new TextValue("a"), new NumberValue(2), new NumberValue(-1), BlankValue.Instance);
    }

    [Fact]
    public void Sort_CustomList_DescendingReversesTheWholeOrderingNonMembersFirst()
    {
        var (_, sheet, ctx) = TestWorkbookFixture.CreateContext();
        string[] items = ["Tue", "Foo", "Sun", "Wed", "Mon"];
        for (var i = 0; i < items.Length; i++)
            sheet.SetCell(new CellAddress(sheet.Id, (uint)i + 1, 1), Cell.FromValue(new TextValue(items[i])));

        CustomSortOrder.TryParse("Sun,Mon,Tue,Wed,Thu,Fri,Sat", out var order).Should().BeTrue();
        var range = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1));
        new SortCommand(sheet.Id, range, [new SortKey(0, false, CustomOrder: order)]).Apply(ctx).Success.Should().BeTrue();

        Enumerable.Range(1, 5).Select(r => ((TextValue)sheet.GetValue((uint)r, 1)).Value)
            .Should().Equal("Foo", "Wed", "Tue", "Mon", "Sun");
    }

    // ───────────────────────── Data validation ─────────────────────────

    private static DataValidation Rule(DvType type, DvOperator op, string? f1, string? f2 = null, bool allowBlank = true) => new()
    {
        Type = type,
        Operator = op,
        Formula1 = f1,
        Formula2 = f2,
        AllowBlank = allowBlank,
    };

    [Theory]
    [InlineData(DvType.WholeNumber, DvOperator.NotEqual, "5", true)]
    [InlineData(DvType.WholeNumber, DvOperator.Equal, "5", false)]
    [InlineData(DvType.Decimal, DvOperator.LessThan, "10", true)]
    [InlineData(DvType.Decimal, DvOperator.GreaterThan, "10", false)]
    [InlineData(DvType.Decimal, DvOperator.GreaterThanOrEqual, "0", true)]
    [InlineData(DvType.TextLength, DvOperator.Equal, "0", true)]
    [InlineData(DvType.TextLength, DvOperator.GreaterThan, "0", false)]
    public void Validate_BlankWithIgnoreBlankOff_IsValidatedAsZeroOrTheEmptyString(DvType type, DvOperator op, string bound, bool valid)
    {
        var dv = Rule(type, op, bound, allowBlank: false);

        (DataValidationService.Validate(dv, BlankValue.Instance) is null).Should().Be(valid);
    }

    [Fact]
    public void Validate_ListAndBlankWithIgnoreBlankOff_StaysInvalid()
    {
        DataValidationService.Validate(Rule(DvType.List, DvOperator.Between, "a,b", allowBlank: false), BlankValue.Instance)
            .Should().NotBeNull();
    }

    [Theory]
    [InlineData(DvOperator.Between, "1", "10", false)]
    [InlineData(DvOperator.NotBetween, "1", "10", true)]
    [InlineData(DvOperator.Equal, "1", null, false)]
    [InlineData(DvOperator.NotEqual, "1", null, true)]
    [InlineData(DvOperator.GreaterThan, "1", null, true)]
    [InlineData(DvOperator.GreaterThanOrEqual, "1", null, true)]
    [InlineData(DvOperator.LessThan, "1", null, false)]
    [InlineData(DvOperator.LessThanOrEqual, "1", null, false)]
    public void Validate_LogicalValueInWholeNumberRule_RanksAboveEveryNumber(DvOperator op, string f1, string? f2, bool valid)
    {
        var dv = Rule(DvType.WholeNumber, op, f1, f2);

        (DataValidationService.Validate(dv, new BoolValue(true)) is null).Should().Be(valid);
        (DataValidationService.Validate(dv, new BoolValue(false)) is null).Should().Be(valid);
    }

    [Fact]
    public void Validate_Time_ComparesTheRawSerialIncludingTheDatePart()
    {
        var between = Rule(DvType.Time, DvOperator.Between, "0.25", "0.75");
        DataValidationService.Validate(between, new NumberValue(0.5)).Should().BeNull();
        DataValidationService.Validate(between, new NumberValue(1.5)).Should().NotBeNull();

        var greater = Rule(DvType.Time, DvOperator.GreaterThan, "0.5");
        DataValidationService.Validate(greater, new NumberValue(5)).Should().BeNull();
    }

    [Fact]
    public void Validate_RangeListSource_StaysCaseInsensitive_WhileInlineListIsCaseSensitive()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new TextValue("Red")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 3), Cell.FromValue(new TextValue("Green")));
        var address = new CellAddress(sheet.Id, 1, 1);
        var rangeRule = Rule(DvType.List, DvOperator.Between, "=$C$1:$C$2");
        rangeRule.AppliesTo = new GridRange(address, address);
        var inlineRule = Rule(DvType.List, DvOperator.Between, "Red,Green");
        inlineRule.AppliesTo = new GridRange(address, address);

        DataValidationService.Validate(rangeRule, new TextValue("red"), sheet, address, workbook).Should().BeNull();
        DataValidationService.Validate(inlineRule, new TextValue("red"), sheet, address, workbook).Should().NotBeNull();
    }

    [Fact]
    public void Validate_CustomFormula_CanUseIndirectBackToTheValidatedCell()
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        var rule = Rule(DvType.Custom, DvOperator.Between, "=INDIRECT(\"A1\")>5");
        rule.AppliesTo = new GridRange(address, address);

        DataValidationService.Validate(rule, new NumberValue(9), sheet, address, workbook).Should().BeNull();
        DataValidationService.Validate(rule, new NumberValue(2), sheet, address, workbook).Should().NotBeNull();
    }

    // ─────────────────────── Conditional formatting ───────────────────────

    private static readonly CellColor Highlight = new(255, 199, 206);

    private static (Workbook Workbook, Sheet Sheet) NewSheet(params ScalarValue?[] column)
    {
        var (workbook, sheet) = TestWorkbookFixture.CreateWorkbook();
        for (var i = 0; i < column.Length; i++)
        {
            if (column[i] is { } value)
                sheet.SetCell(new CellAddress(sheet.Id, (uint)i + 1, 1), Cell.FromValue(value));
        }

        return (workbook, sheet);
    }

    private static ConditionalFormat AddRule(Sheet sheet, uint rows, Action<ConditionalFormat> configure)
    {
        var rule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, rows, 1)),
            Priority = sheet.ConditionalFormats.Count + 1,
            FormatIfTrue = new CellStyle { FillColor = Highlight },
        };
        configure(rule);
        sheet.ConditionalFormats.Add(rule);
        return rule;
    }

    private static bool[] Highlighted(Workbook workbook, Sheet sheet, int rows)
    {
        var session = new ConditionalFormatEvaluationSession(sheet, workbook, sheet.GetOccupiedCellMap());
        return Enumerable.Range(1, rows)
            .Select(r =>
            {
                var address = new CellAddress(sheet.Id, (uint)r, 1);
                return session.EvaluateEffectiveStyle(address, sheet.GetValue((uint)r, 1)).FillColor == Highlight;
            })
            .ToArray();
    }

    [Fact]
    public void CellValue_BlankTakesTheTypeOfTheOperand_AndErrorsNeverMatch()
    {
        var (workbook, sheet) = NewSheet(null, new NumberValue(5), new TextValue("abc"), new BoolValue(true), ErrorValue.NA);
        AddRule(sheet, 5, rule => { rule.RuleType = CfRuleType.CellValue; rule.Operator = CfOperator.Equal; rule.Value1 = "0"; });
        Highlighted(workbook, sheet, 5).Should().Equal(true, false, false, false, false);

        sheet.ConditionalFormats.Clear();
        AddRule(sheet, 5, rule => { rule.RuleType = CfRuleType.CellValue; rule.Operator = CfOperator.NotEqual; rule.Value1 = "0"; });
        Highlighted(workbook, sheet, 5).Should().Equal(false, true, true, true, false);
    }

    [Fact]
    public void CellValue_ComparesAcrossTypesAsNumbersThenTextThenLogicals()
    {
        var (workbook, sheet) = NewSheet(new NumberValue(5), new TextValue("abc"), new BoolValue(true), null);
        AddRule(sheet, 4, rule => { rule.RuleType = CfRuleType.CellValue; rule.Operator = CfOperator.GreaterThan; rule.Value1 = "100"; });

        Highlighted(workbook, sheet, 4).Should().Equal(false, true, true, false);
    }

    [Fact]
    public void CellValue_BetweenAcceptsReversedOperands()
    {
        var (workbook, sheet) = NewSheet(new NumberValue(8), new NumberValue(9), new NumberValue(11));
        AddRule(sheet, 3, rule => { rule.RuleType = CfRuleType.CellValue; rule.Operator = CfOperator.Between; rule.Value1 = "10"; rule.Value2 = "8"; });

        Highlighted(workbook, sheet, 3).Should().Equal(true, true, false);
    }

    [Theory]
    [InlineData(CfRuleType.ContainsText, "", new[] { true, true, true, false })]
    [InlineData(CfRuleType.ContainsText, "*", new[] { true, true, true, false })]
    [InlineData(CfRuleType.ContainsText, "a?p", new[] { true, false, false, false })]
    [InlineData(CfRuleType.NotContainsText, "", new[] { false, false, false, true })]
    [InlineData(CfRuleType.BeginsWith, "", new[] { true, true, true, true })]
    [InlineData(CfRuleType.EndsWith, "5", new[] { false, false, true, false })]
    public void TextRules_FollowTheSearchLeftRightFormulaSemantics(CfRuleType type, string text, bool[] expected)
    {
        // Row 4 is a truly empty cell (SEARCH over an empty cell is #VALUE!; LEFT/RIGHT of it are "").
        var (workbook, sheet) = NewSheet(new TextValue("apple"), new TextValue("banana"), new NumberValue(15), null);
        AddRule(sheet, 4, rule => { rule.RuleType = type; rule.TextRuleText = text; });

        Highlighted(workbook, sheet, 4).Should().Equal(expected);
    }

    [Fact]
    public void TopBottomPercent_KeepsTheFloorOfTheCountWithAMinimumOfOne()
    {
        // 10 numeric cells: Top 25% keeps floor(2.5) = 2, Top 1% keeps 1 (never zero).
        var column = Enumerable.Range(1, 10).Select(i => (ScalarValue)new NumberValue(i)).ToArray();
        var (workbook, sheet) = NewSheet(column);
        AddRule(sheet, 10, rule => { rule.RuleType = CfRuleType.Top10; rule.AboveAverage = true; rule.TopBottomPercent = true; rule.TopBottomRank = 25; });
        Highlighted(workbook, sheet, 10).Count(h => h).Should().Be(2);

        sheet.ConditionalFormats.Clear();
        AddRule(sheet, 10, rule => { rule.RuleType = CfRuleType.Top10; rule.AboveAverage = true; rule.TopBottomPercent = true; rule.TopBottomRank = 1; });
        Highlighted(workbook, sheet, 10).Count(h => h).Should().Be(1);
    }

    [Fact]
    public void StdDevRule_UsesThePopulationStandardDeviation_AndIsDisabledByAnErrorInTheRange()
    {
        // [2,4,4,4,5,5,7,9]: mean 5, population sd 2 -> "1 sd above or equal" starts at 7.
        var column = new double[] { 2, 4, 4, 4, 5, 5, 7, 9 }.Select(v => (ScalarValue)new NumberValue(v)).ToArray();
        var (workbook, sheet) = NewSheet(column);
        AddRule(sheet, 8, rule => { rule.RuleType = CfRuleType.AboveAverage; rule.AboveAverage = true; rule.EqualAverage = true; rule.StdDevCount = 1; });
        Highlighted(workbook, sheet, 8).Should().Equal(false, false, false, false, false, false, true, true);

        sheet.SetCell(new CellAddress(sheet.Id, 9, 1), Cell.FromValue(ErrorValue.NA));
        sheet.ConditionalFormats.Clear();
        AddRule(sheet, 9, rule => { rule.RuleType = CfRuleType.AboveAverage; rule.AboveAverage = true; rule.EqualAverage = true; rule.StdDevCount = 1; });
        Highlighted(workbook, sheet, 9).Should().OnlyContain(h => !h, "the deviation of a range that contains an error is an error");
    }

    [Fact]
    public void AboveAverage_WithoutStdDev_SkipsErrorCells()
    {
        var (workbook, sheet) = NewSheet(new NumberValue(1), new NumberValue(9), ErrorValue.NA);
        AddRule(sheet, 3, rule => { rule.RuleType = CfRuleType.AboveAverage; rule.AboveAverage = true; });

        Highlighted(workbook, sheet, 3).Should().Equal(false, true, false);
    }

    [Fact]
    public void DuplicateValues_TextOneEqualsNumberOne_AndTrailingSpaceIsSignificant()
    {
        var (workbook, sheet) = NewSheet(new NumberValue(1), new TextValue("1"), new TextValue("a"), new TextValue("a "));
        AddRule(sheet, 4, rule => rule.RuleType = CfRuleType.DuplicateValues);

        Highlighted(workbook, sheet, 4).Should().Equal(true, true, false, false);
    }

    [Fact]
    public void DuplicateAndUnique_EmptyStringIsAnOrdinaryValueSharingItsKeyWithEmptyCells()
    {
        // Row 1 is "", row 2 is a truly empty cell, row 3 is a lone value.
        var (workbook, sheet) = NewSheet(new TextValue(""), null, new TextValue("x"));
        AddRule(sheet, 3, rule => rule.RuleType = CfRuleType.DuplicateValues);
        Highlighted(workbook, sheet, 3).Should().Equal(true, false, false);

        sheet.ConditionalFormats.Clear();
        AddRule(sheet, 3, rule => rule.RuleType = CfRuleType.UniqueValues);
        Highlighted(workbook, sheet, 3).Should().Equal(false, false, true);

        // With no other empty cell the "" is unique.
        var (workbook2, sheet2) = NewSheet(new TextValue(""), new TextValue("x"));
        AddRule(sheet2, 2, rule => rule.RuleType = CfRuleType.UniqueValues);
        Highlighted(workbook2, sheet2, 2).Should().Equal(true, true);
    }

    [Fact]
    public void ColorScale_AllEqualValuesPaintTheMaxColor()
    {
        var (workbook, sheet) = NewSheet(new NumberValue(5), new NumberValue(5), new NumberValue(5));
        AddRule(sheet, 3, rule =>
        {
            rule.RuleType = CfRuleType.ColorScale;
            rule.FormatIfTrue = null;
            rule.UseThreeColorScale = false;
            rule.MinColor = new RgbColor(255, 255, 255);
            rule.MaxColor = new RgbColor(255, 0, 0);
        });

        var session = new ConditionalFormatEvaluationSession(sheet, workbook, sheet.GetOccupiedCellMap());
        session.EvaluateEffectiveStyle(new CellAddress(sheet.Id, 1, 1), new NumberValue(5)).FillColor
            .Should().Be(new CellColor(255, 0, 0));
    }

    [Fact]
    public void FormulaRule_IndirectBackToTheFormattedCellIsAPlainRead()
    {
        var (workbook, sheet) = NewSheet(new NumberValue(2), new NumberValue(9));
        AddRule(sheet, 2, rule => { rule.RuleType = CfRuleType.Formula; rule.FormulaText = "INDIRECT(\"A\"&ROW())>5"; });

        Highlighted(workbook, sheet, 2).Should().Equal(false, true);
    }
}
