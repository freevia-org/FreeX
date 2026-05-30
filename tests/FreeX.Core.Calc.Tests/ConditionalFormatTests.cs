using FreeX.Core.Calc;
using FreeX.Core.Commands;
using FreeX.Core.Model;
using FluentAssertions;

namespace FreeX.Core.Calc.Tests;

public class ConditionalFormatTests
{
    // ─── helpers ──────────────────────────────────────────────────────────────

    private static (Workbook workbook, Sheet sheet) MakeWorkbook()
    {
        var wb = new Workbook("test");
        var sh = wb.AddSheet("Sheet1");
        return (wb, sh);
    }

    private static ViewportModel GetViewport(Workbook wb, Sheet sheet)
    {
        var svc = new ViewportService();
        return svc.GetViewport(wb, sheet.Id, new ViewportRequest(1, 1, 500, 500));
    }

    private static DisplayCell GetCell(ViewportModel vp, uint row, uint col) =>
        vp.Cells.Single(c => c.Row == row && c.Col == col);

    [Fact]
    public void ConditionalFormatAggregates_DoNotEnumerateEveryCellInLargeAppliesToRanges()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.Calc", "ViewportService.ConditionalFormats.cs"));

        source.Should().NotContain(
            "cf.AppliesTo.AllCells()",
            "viewport refreshes for full-column or full-sheet conditional formats should scan sparse used cells instead of every address");
    }

    // ─── tests ────────────────────────────────────────────────────────────────

    [Fact]
    public void ConditionalFormatAggregates_OnlyAllocateRankAndCountCachesForRulesThatNeedThem()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.Calc", "ViewportConditionalFormatEvaluator.cs"));

        source.Should().Contain(
            "cf.RuleType == CfRuleType.Top10 ? [] : null",
            "only top/bottom rules need a ranked-value cache while precomputing conditional-format aggregates");
        source.Should().Contain(
            "CfRuleType.DuplicateValues or CfRuleType.UniqueValues",
            "only duplicate/unique rules need display-value occurrence counts");
        source.Should().NotContain(
            "var rankedValues = new List<(CellAddress Address, double Value)>();",
            "color scales, icon sets, and above-average rules should avoid unused ranking-list allocations");
        source.Should().NotContain(
            "var valueCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);",
            "non-duplicate aggregate rules should avoid unused value-count dictionary allocations");
    }

    [Fact]
    public void ConditionalFormatTopBottomRanking_SortsInPlaceWithoutLinqPipelines()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.Calc", "ViewportConditionalFormatEvaluator.cs"));
        var resolveTopBottom = source[
            source.IndexOf("private static IReadOnlySet<CellAddress>? ResolveTopBottomMatches", StringComparison.Ordinal)..
            source.IndexOf("private static IEnumerable<(CellAddress Address, ScalarValue Value)> EnumerateAggregateValues", StringComparison.Ordinal)];

        resolveTopBottom.Should().Contain("rankedValues.Sort(");
        resolveTopBottom.Should().Contain("new HashSet<CellAddress>(take)");
        resolveTopBottom.Should().Contain("left.Index.CompareTo(right.Index)");
        resolveTopBottom.Should().NotContain(".OrderBy(");
        resolveTopBottom.Should().NotContain(".OrderByDescending(");
        resolveTopBottom.Should().NotContain(".Take(");
        resolveTopBottom.Should().NotContain(".Select(");
        resolveTopBottom.Should().NotContain(".ToHashSet(");
    }

    [Fact]
    public void ConditionalFormatContext_NoRulesReusesStaticEmptyContext()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.Calc", "ViewportConditionalFormatEvaluator.cs"));
        var buildContext = source[
            source.IndexOf("public static CfEvaluationContext BuildContext", StringComparison.Ordinal)..
            source.IndexOf("var rulesByPriority", StringComparison.Ordinal)];

        source.Should().Contain("private static readonly CfEvaluationContext EmptyContext");
        buildContext.Should().Contain("return EmptyContext;");
        buildContext.Should().NotContain("new CfEvaluationContext(");
        buildContext.Should().NotContain("new Dictionary<ConditionalFormat, CfAggregateCache>");
        buildContext.Should().NotContain("new Dictionary<ConditionalFormat, CfFormulaCache>");
    }

    [Fact]
    public void ConditionalFormatContext_NonEmptyRulesAvoidsLinqArrayPipelines()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.Calc", "ViewportConditionalFormatEvaluator.cs"));
        var buildContext = source[
            source.IndexOf("public static CfEvaluationContext BuildContext", StringComparison.Ordinal)..
            source.IndexOf("public static CellStyle? Evaluate", StringComparison.Ordinal)];

        buildContext.Should().Contain("CopyRulesByPriority(sheet.ConditionalFormats)");
        buildContext.Should().Contain("CopyIconRulesByPriority(rulesByPriority)");
        buildContext.Should().NotContain(".OrderBy(");
        buildContext.Should().NotContain(".Where(");
        buildContext.Should().NotContain(".ToArray(");
        source.Should().Contain("left.Index.CompareTo(right.Index)");
    }

    [Fact]
    public void ConditionalFormatEvaluation_DoesNotRunLinqRangeFiltersPerDisplayedCell()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.Calc", "ViewportService.ConditionalFormats.cs"));

        source.Should().NotContain(
            ".Where(cf => cf.AppliesTo.Contains(addr))",
            "conditional-format rules should be ordered once per viewport and checked with allocation-free loops per cell");
        source.Should().NotContain(
            ".Where(cf => cf.RuleType == CfRuleType.IconSet && cf.AppliesTo.Contains(addr))",
            "icon-set lookup runs for each displayed cell and should reuse preordered icon rules");
    }

    [Fact]
    public void FormulaConditionalFormatEvaluation_DoesNotSerializeShiftedFormulaPerDisplayedCell()
    {
        var source = File.ReadAllText(FindWorkspaceFile(
            "src", "FreeX.Core.Calc", "ViewportService.ConditionalFormatFormulas.cs"));

        source.Should().NotContain(
            "FormulaSerializer.Serialize",
            "viewport formula conditional formats should evaluate cached shifted ASTs instead of serializing formula text per cell");
        source.Should().NotContain(
            "Evaluate(\"=\" + formulaText",
            "serializing shifted formulas back to text makes FormulaEvaluator parse the same rule again per visible cell");
    }

    [Fact]
    public void ColorScale_LargeSparseRange_UsesOccupiedCellsForAggregates()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 1_000_000, 1), Cell.FromValue(new NumberValue(100)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, CellAddress.MaxRow, 1)),
            Priority = 1,
            RuleType = CfRuleType.ColorScale,
            MinColor = new RgbColor(0, 255, 0),
            MaxColor = new RgbColor(255, 0, 0),
            UseThreeColorScale = false
        });

        var viewport = GetViewport(wb, sheet);

        GetCell(viewport, 1, 1).Style!.FillColor.Should().Be(new CellColor(0, 255, 0));
    }

    [Fact]
    public void CellValue_GreaterThan_AppliesFormatToMatchingCells()
    {
        // Arrange
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(5)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(2)));

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        var cf = new ConditionalFormat
        {
            AppliesTo   = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 1)),
            Priority    = 1,
            RuleType    = CfRuleType.CellValue,
            Operator    = CfOperator.GreaterThan,
            Value1      = "3",
            FormatIfTrue = redStyle
        };
        sheet.ConditionalFormats.Add(cf);

        // Act
        var vp = GetViewport(wb, sheet);

        // Assert
        var a1 = GetCell(vp, 1, 1);
        var a2 = GetCell(vp, 2, 1);

        a1.Style!.FillColor.Should().Be(new CellColor(255, 0, 0), "A1=5 > 3, so red fill should apply");
        a2.Style!.FillColor.Should().NotBe(new CellColor(255, 0, 0), "A2=2 is not > 3, so red fill should NOT apply");
    }

    [Fact]
    public void ConditionalFormatContext_PreservesInsertionOrderForEqualPriorities()
    {
        var (wb, sheet) = MakeWorkbook();
        var address = new CellAddress(sheet.Id, 1, 1);
        var range = new GridRange(address, address);

        sheet.SetCell(address, Cell.FromValue(new NumberValue(5)));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(10, 20, 30) }
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = range,
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "0",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(200, 210, 220) }
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(
            new CellColor(10, 20, 30),
            "same-priority conditional formats should keep insertion order");
    }

    [Fact]
    public void CellValue_Between_AppliesOnlyWhenInRange()
    {
        // Arrange
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(5)));

        var boldStyle = new CellStyle { Bold = true };
        var cf = new ConditionalFormat
        {
            AppliesTo    = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1)),
            Priority     = 1,
            RuleType     = CfRuleType.CellValue,
            Operator     = CfOperator.Between,
            Value1       = "1",
            Value2       = "10",
            FormatIfTrue = boldStyle
        };
        sheet.ConditionalFormats.Add(cf);

        // Act
        var vp = GetViewport(wb, sheet);

        // Assert
        var a1 = GetCell(vp, 1, 1);
        a1.Style!.Bold.Should().BeTrue("A1=5 is between 1 and 10");
    }

    [Fact]
    public void ColorScale_InterpolatesColorForMidRangeValue()
    {
        // Arrange
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(0)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(100)));

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 3, 1)),
            Priority  = 1,
            RuleType  = CfRuleType.ColorScale,
            MinColor  = new RgbColor(0, 255, 0),    // green
            MaxColor  = new RgbColor(255, 0, 0),    // red
            UseThreeColorScale = false
        };
        sheet.ConditionalFormats.Add(cf);

        // Act
        var vp = GetViewport(wb, sheet);

        // Assert: mid-range cell (50 out of 0–100) should have roughly yellow (~128, ~128, 0)
        var a2 = GetCell(vp, 2, 1);
        a2.Style!.FillColor.Should().NotBeNull("color scale should set a fill");
        var fill = a2.Style!.FillColor!.Value;
        // Interpolation: R = 0 + 0.5*(255-0) = 127, G = 255 + 0.5*(0-255) = 127, B = 0
        fill.R.Should().BeCloseTo(127, 2, "R interpolated from 0→255 at t=0.5");
        fill.G.Should().BeCloseTo(127, 2, "G interpolated from 255→0 at t=0.5");
    }

    [Fact]
    public void IconSet_AttachesTrafficLightDisplayIconsByValueBand()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(90)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1"
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3TrafficLights1", 0, 3, true));
        GetCell(vp, 2, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3TrafficLights1", 1, 3, true));
        GetCell(vp, 3, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3TrafficLights1", 2, 3, true));
    }

    [Fact]
    public void IconSet_RespectsReverseAndIconsOnlyDisplay()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(90)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1",
            IconSetReverse = true,
            IconSetShowValue = false
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3TrafficLights1", 2, 3, false));
        GetCell(vp, 1, 1).DisplayText.Should().BeEmpty();
        GetCell(vp, 2, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3TrafficLights1", 0, 3, false));
        GetCell(vp, 2, 1).DisplayText.Should().BeEmpty();
    }

    [Fact]
    public void IconSet_ResolvesFourAndFiveIconBandsFromExplicitThresholds()
    {
        var (wb, sheet) = MakeWorkbook();
        var fourBandValues = new[] { 0, 50, 85, 100 };
        var fiveBandValues = new[] { 10, 50, 88, 93, 100 };
        for (uint row = 1; row <= 4; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), Cell.FromValue(new NumberValue(fourBandValues[row - 1])));
        for (uint row = 1; row <= 5; row++)
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), Cell.FromValue(new NumberValue(fiveBandValues[row - 1])));

        var fourBandRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 1)),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "4Arrows"
        };
        fourBandRule.IconSetThresholds.AddRange([
            new CfThresholdModel(CfThresholdType.Percent, "10"),
            new CfThresholdModel(CfThresholdType.Percent, "80"),
            new CfThresholdModel(CfThresholdType.Percent, "90")
        ]);
        sheet.ConditionalFormats.Add(fourBandRule);

        var fiveBandRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 2), new CellAddress(sheet.Id, 5, 2)),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "5Arrows"
        };
        fiveBandRule.IconSetThresholds.AddRange([
            new CfThresholdModel(CfThresholdType.Number, "15"),
            new CfThresholdModel(CfThresholdType.Number, "85"),
            new CfThresholdModel(CfThresholdType.Number, "90"),
            new CfThresholdModel(CfThresholdType.Number, "95")
        ]);
        sheet.ConditionalFormats.Add(fiveBandRule);

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("4Arrows", 0, 4, true));
        GetCell(vp, 2, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("4Arrows", 1, 4, true));
        GetCell(vp, 3, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("4Arrows", 2, 4, true));
        GetCell(vp, 4, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("4Arrows", 3, 4, true));
        GetCell(vp, 1, 2).ConditionalIcon.Should().Be(new ConditionalFormatIcon("5Arrows", 0, 5, true));
        GetCell(vp, 2, 2).ConditionalIcon.Should().Be(new ConditionalFormatIcon("5Arrows", 1, 5, true));
        GetCell(vp, 3, 2).ConditionalIcon.Should().Be(new ConditionalFormatIcon("5Arrows", 2, 5, true));
        GetCell(vp, 4, 2).ConditionalIcon.Should().Be(new ConditionalFormatIcon("5Arrows", 3, 5, true));
        GetCell(vp, 5, 2).ConditionalIcon.Should().Be(new ConditionalFormatIcon("5Arrows", 4, 5, true));
    }

    [Fact]
    public void IconSet_WithPerThresholdOverrides_AppliesCustomIconForEachBucket()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(50)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(90)));

        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3TrafficLights1"
        };
        cf.IconSetThresholds.AddRange([
            new CfThresholdModel(CfThresholdType.Percent, "40"),
            new CfThresholdModel(CfThresholdType.Percent, "70")
        ]);
        cf.IconOverrides.AddRange([
            new CfIconOverride("3Arrows", 0),
            new CfIconOverride("3Arrows", 1),
            new CfIconOverride("3Arrows", 2)
        ]);
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        // Values 10, 50, 90 with thresholds at 40% (42) and 70% (66) of range [10,90]
        // → buckets 0, 1, 2 respectively
        GetCell(vp, 1, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3Arrows", 0, 3, true));
        GetCell(vp, 2, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3Arrows", 1, 3, true));
        GetCell(vp, 3, 1).ConditionalIcon.Should().Be(new ConditionalFormatIcon("3Arrows", 2, 3, true));
    }

    [Fact]
    public void Top10_HighlightsTopRankedValues()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(30)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(20)));

        var green = new CellStyle { FillColor = new CellColor(198, 239, 206) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.Top10,
            TopBottomRank = 2,
            AboveAverage = true,
            FormatIfTrue = green
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().NotBe(new CellColor(198, 239, 206));
        GetCell(vp, 2, 1).Style!.FillColor.Should().Be(new CellColor(198, 239, 206));
        GetCell(vp, 3, 1).Style!.FillColor.Should().Be(new CellColor(198, 239, 206));
    }

    [Fact]
    public void Top10_PreservesInsertionOrderWhenValuesTieAtRankBoundary()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new NumberValue(5)));

        var green = new CellStyle { FillColor = new CellColor(198, 239, 206) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.Top10,
            TopBottomRank = 1,
            AboveAverage = true,
            FormatIfTrue = green
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(new CellColor(198, 239, 206));
        GetCell(vp, 2, 1).Style!.FillColor.Should().NotBe(
            new CellColor(198, 239, 206),
            "the in-place sort should preserve the previous stable ordering for tied values");
        GetCell(vp, 3, 1).Style!.FillColor.Should().NotBe(new CellColor(198, 239, 206));
    }

    [Fact]
    public void DuplicateValues_HighlightsOnlyRepeatedDisplayValues()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("North")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new TextValue("South")));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(new TextValue("north")));

        var yellow = new CellStyle { FillColor = new CellColor(255, 235, 132) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.DuplicateValues,
            FormatIfTrue = yellow
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(new CellColor(255, 235, 132));
        GetCell(vp, 2, 1).Style!.FillColor.Should().NotBe(new CellColor(255, 235, 132));
        GetCell(vp, 3, 1).Style!.FillColor.Should().Be(new CellColor(255, 235, 132));
    }

    [Fact]
    public void TextBlankAndErrorRules_RenderVisibleStyles()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new TextValue("urgent follow-up")));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(BlankValue.Instance));
        sheet.SetCell(new CellAddress(sheet.Id, 3, 1), Cell.FromValue(ErrorValue.Value));

        var blue = new CellStyle { FillColor = new CellColor(189, 215, 238) };
        var red = new CellStyle { FillColor = new CellColor(255, 199, 206) };
        var gray = new CellStyle { FillColor = new CellColor(217, 217, 217) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 1,
            RuleType = CfRuleType.ContainsText,
            TextRuleText = "urgent",
            FormatIfTrue = blue
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 2,
            RuleType = CfRuleType.Blanks,
            FormatIfTrue = gray
        });
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 3,
            RuleType = CfRuleType.Errors,
            FormatIfTrue = red
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(new CellColor(189, 215, 238));
        GetCell(vp, 2, 1).Style!.FillColor.Should().Be(new CellColor(217, 217, 217));
        GetCell(vp, 3, 1).Style!.FillColor.Should().Be(new CellColor(255, 199, 206));
    }

    [Fact]
    public void DateOccurring_HighlightsDatesInSelectedPeriod()
    {
        var (wb, sheet) = MakeWorkbook();
        var today = DateTime.Today;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(today.AddDays(-3)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), DateTimeValue.FromDateTime(today.AddDays(-8)));

        var green = new CellStyle { FillColor = new CellColor(198, 239, 206) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.DateOccurring,
            DateOccurringPeriod = "last7Days",
            FormatIfTrue = green
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(new CellColor(198, 239, 206));
        GetCell(vp, 2, 1).Style!.FillColor.Should().NotBe(new CellColor(198, 239, 206));
    }

    [Fact]
    public void DuplicateValues_UsesDateValuesInsteadOfTreatingDatesAsBlank()
    {
        var (wb, sheet) = MakeWorkbook();
        var today = DateTime.Today;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), DateTimeValue.FromDateTime(today));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), DateTimeValue.FromDateTime(today.AddDays(1)));

        var yellow = new CellStyle { FillColor = new CellColor(255, 235, 132) };
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1,
            RuleType = CfRuleType.DuplicateValues,
            FormatIfTrue = yellow
        });

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().NotBe(new CellColor(255, 235, 132));
        GetCell(vp, 2, 1).Style!.FillColor.Should().NotBe(new CellColor(255, 235, 132));
    }

    [Fact]
    public void MergeStyles_CfBoldOverridesBaseStyle()
    {
        // Arrange
        var (wb, sheet) = MakeWorkbook();
        var baseStyle = new CellStyle { Bold = false, FillColor = new CellColor(200, 200, 200) };
        var styleId   = wb.RegisterStyle(baseStyle);

        var cell = Cell.FromValue(new NumberValue(99));
        cell.StyleId = styleId;
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), cell);

        var boldStyle = new CellStyle { Bold = true };
        var cf = new ConditionalFormat
        {
            AppliesTo    = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1)),
            Priority     = 1,
            RuleType     = CfRuleType.CellValue,
            Operator     = CfOperator.GreaterThan,
            Value1       = "0",
            FormatIfTrue = boldStyle
        };
        sheet.ConditionalFormats.Add(cf);

        // Act
        var vp = GetViewport(wb, sheet);

        // Assert
        var a1 = GetCell(vp, 1, 1);
        a1.Style!.Bold.Should().BeTrue("CF bold overrides base non-bold");
        // Base fill should be preserved (CF style has no fill)
        a1.Style!.FillColor.Should().Be(new CellColor(200, 200, 200), "base fill preserved when CF has none");
    }

    [Fact]
    public void ApplyConditionalFormatCommand_Revert_RemovesRule()
    {
        // Arrange
        var (wb, sheet) = MakeWorkbook();

        var cf = new ConditionalFormat
        {
            AppliesTo    = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 1)),
            Priority     = 1,
            RuleType     = CfRuleType.CellValue,
            Operator     = CfOperator.GreaterThan,
            Value1       = "0",
            FormatIfTrue = new CellStyle { Bold = true }
        };

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        var cmd = new ApplyConditionalFormatCommand(sheet.Id, cf);

        // Apply
        bus.Execute(wb.Id, cmd);
        sheet.ConditionalFormats.Should().HaveCount(1);

        // Undo (revert)
        bus.Undo(wb.Id);
        sheet.ConditionalFormats.Should().BeEmpty("revert should remove the rule");
    }

    // ─── Formula CF rule tests ────────────────────────────────────────────────

    [Fact]
    public void Formula_Rule_AppliesWhenFormulaIsTrue()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(2)));

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        var cf = new ConditionalFormat
        {
            AppliesTo    = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 1)),
            Priority     = 1,
            RuleType     = CfRuleType.Formula,
            FormulaText  = "A1>5",   // relative — for row 2 this shifts to A2>5
            FormatIfTrue = redStyle
        };
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        var a1 = GetCell(vp, 1, 1);
        var a2 = GetCell(vp, 2, 1);

        a1.Style!.FillColor.Should().Be(new CellColor(255, 0, 0), "A1=10 > 5, formula true");
        a2.Style!.FillColor.Should().NotBe(new CellColor(255, 0, 0), "A2=2, shifted formula A2>5 is false");
    }

    [Fact]
    public void Formula_Rule_AbsoluteRef_SameForAllCells()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(10)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(3)));
        // Threshold cell
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), Cell.FromValue(new NumberValue(5)));

        var redStyle = new CellStyle { FillColor = new CellColor(255, 0, 0) };
        var cf = new ConditionalFormat
        {
            AppliesTo    = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 1, 2)),
            Priority     = 1,
            RuleType     = CfRuleType.Formula,
            // Absolute reference — same condition for all cells in range
            FormulaText  = "$A$1>5",
            FormatIfTrue = redStyle
        };
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        // Both cells should be red because $A$1=10 > 5 is always true
        var a1 = GetCell(vp, 1, 1);
        var b1 = GetCell(vp, 1, 2);
        a1.Style!.FillColor.Should().Be(new CellColor(255, 0, 0));
        b1.Style!.FillColor.Should().Be(new CellColor(255, 0, 0));
    }

    [Fact]
    public void Formula_Rule_ShiftsRelativeRefsWhileKeepingAbsoluteRefs()
    {
        var (wb, sheet) = MakeWorkbook();
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), Cell.FromValue(new NumberValue(6)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), Cell.FromValue(new NumberValue(4)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 1), Cell.FromValue(new NumberValue(8)));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), Cell.FromValue(new NumberValue(9)));
        sheet.SetCell(new CellAddress(sheet.Id, 4, 4), Cell.FromValue(new NumberValue(5)));

        var greenStyle = new CellStyle { FillColor = new CellColor(0, 255, 0) };
        var cf = new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, 1),
                new CellAddress(sheet.Id, 2, 2)),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "A1>$D$4",
            FormatIfTrue = greenStyle
        };
        sheet.ConditionalFormats.Add(cf);

        var vp = GetViewport(wb, sheet);

        GetCell(vp, 1, 1).Style!.FillColor.Should().Be(new CellColor(0, 255, 0), "A1=6 is greater than $D$4=5");
        GetCell(vp, 1, 2).Style!.FillColor.Should().NotBe(new CellColor(0, 255, 0), "B1=4 is not greater than $D$4=5");
        GetCell(vp, 2, 1).Style!.FillColor.Should().Be(new CellColor(0, 255, 0), "A2=8 is greater than $D$4=5");
        GetCell(vp, 2, 2).Style!.FillColor.Should().Be(new CellColor(0, 255, 0), "B2=9 is greater than $D$4=5");
    }

    [Fact]
    public void Formula_Rule_ShiftPastSheetBounds_DoesNotMatch()
    {
        var (wb, sheet) = MakeWorkbook();
        var greenStyle = new CellStyle { FillColor = new CellColor(0, 255, 0) };

        sheet.SetCell(new CellAddress(sheet.Id, 1, CellAddress.MaxCol - 1), Cell.FromValue(new NumberValue(1)));
        sheet.SetCell(new CellAddress(sheet.Id, 1, CellAddress.MaxCol), Cell.FromValue(new NumberValue(1)));
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(
                new CellAddress(sheet.Id, 1, CellAddress.MaxCol - 1),
                new CellAddress(sheet.Id, 1, CellAddress.MaxCol)),
            Priority = 1,
            RuleType = CfRuleType.Formula,
            FormulaText = "XFD1=1",
            FormatIfTrue = greenStyle
        });

        var svc = new ViewportService();
        var vp = svc.GetViewport(wb, sheet.Id, new ViewportRequest(1, CellAddress.MaxCol - 1, 500, 500));

        GetCell(vp, 1, CellAddress.MaxCol - 1).Style!.FillColor.Should().Be(new CellColor(0, 255, 0));
        GetCell(vp, 1, CellAddress.MaxCol).Style!.FillColor.Should().NotBe(
            new CellColor(0, 255, 0),
            "shifting XFD1 one column right should become an invalid reference, not an XFE1 lookup");
    }

    // ─── ReplaceAllConditionalFormatsCommand tests ────────────────────────────

    [Fact]
    public void ReplaceAllCF_Commit_ReplacesAllRules()
    {
        var (wb, sheet) = MakeWorkbook();

        var oldRule = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            Priority = 1, RuleType = CfRuleType.CellValue,
            FormatIfTrue = new CellStyle { Bold = true }
        };
        sheet.ConditionalFormats.Add(oldRule);

        var newRule1 = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 2, 1), new CellAddress(sheet.Id, 2, 1)),
            Priority = 1, RuleType = CfRuleType.Formula,
            FormulaText = "A2>0", FormatIfTrue = new CellStyle { FillColor = new CellColor(0, 255, 0) }
        };
        var newRule2 = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 3, 1), new CellAddress(sheet.Id, 3, 1)),
            Priority = 2, RuleType = CfRuleType.CellValue,
            FormatIfTrue = new CellStyle { Italic = true }
        };

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        var cmd = new ReplaceAllConditionalFormatsCommand(sheet.Id, [newRule1, newRule2]);

        bus.Execute(wb.Id, cmd);

        sheet.ConditionalFormats.Should().HaveCount(2);
        sheet.ConditionalFormats.Should().NotContain(r => r.Id == oldRule.Id, "old rule replaced");
        sheet.ConditionalFormats.Should().ContainSingle(r => r.Id == newRule1.Id);
    }

    [Fact]
    public void ReplaceAllCF_Undo_RestoresOriginalRules()
    {
        var (wb, sheet) = MakeWorkbook();

        var original = new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            Priority = 1, RuleType = CfRuleType.CellValue,
            FormatIfTrue = new CellStyle { Bold = true }
        };
        sheet.ConditionalFormats.Add(original);

        var bus = new CommandBus(wbId => new TestCommandContext(wb));
        var cmd = new ReplaceAllConditionalFormatsCommand(sheet.Id, []); // replace with empty

        bus.Execute(wb.Id, cmd);
        sheet.ConditionalFormats.Should().BeEmpty();

        bus.Undo(wb.Id);
        sheet.ConditionalFormats.Should().HaveCount(1);
        sheet.ConditionalFormats[0].Id.Should().Be(original.Id, "undo restores the original rule");
    }

    // ─── minimal test helpers ─────────────────────────────────────────────────

    private sealed class TestCommandContext(Workbook wb) : ICommandContext
    {
        public Workbook Workbook => wb;
        public Sheet GetSheet(SheetId id) => wb.GetSheet(id)!;
    }

    private static string FindWorkspaceFile(params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate workspace file.", Path.Combine(parts));
    }
}
