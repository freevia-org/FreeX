using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression tests for round-75 io-cf-classic bucket findings:
/// <list type="bullet">
///   <item>R75-io-cf-classic-4-1: a freshly-authored "A Date Occurring" (<see cref="CfRuleType.DateOccurring"/>)
///     rule (FormulaText null -- nothing in FreeX's own rule-creation path populates it) must be saved with
///     a real Excel-evaluable <c>&lt;formula&gt;</c> child synthesized from its <c>timePeriod</c>, mirroring
///     the existing text-rule/blank-or-error synthesis in <see cref="XlsxAdvancedConditionalFormatWriter"/>.
///     Without it, real Excel (which evaluates the formula, not the timePeriod attribute) treats the rule as
///     never-true and the highlighting silently vanishes on reopen.</item>
///   <item>R75-io-cf-classic-4-2: a classic (CellIs/Expression) rule's <c>&lt;conditionalFormatting
///     pivot="1"&gt;</c> container attribute must survive a save (see
///     <see cref="XlsxAdvancedConditionalFormatWriter.HasAdvancedConditionalFormats(Sheet)"/> and its
///     <c>SplitAndRealignClassicRules</c> post-processing pass, which is now the only place that can emit it
///     -- ClosedXML's own classic-rule Save API (<see cref="XlsxConditionalFormatClosedXmlMapper.Save"/>) has
///     no property for it at all).</item>
/// </list>
/// </summary>
public sealed class R75_CfClassicRoundTripTests
{
    private const string Sheet1Path = "xl/worksheets/sheet1.xml";
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static XElement LoadCfRuleElement(Stream xlsxStream, string worksheetPath, string cfRuleType)
    {
        xlsxStream.Position = 0;
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(worksheetPath)!;
        XDocument doc;
        using (var entryStream = entry.Open())
            doc = XDocument.Load(entryStream);

        return doc.Root!
            .Elements(WorksheetNs + "conditionalFormatting")
            .Elements(WorksheetNs + "cfRule")
            .Single(rule => (string?)rule.Attribute("type") == cfRuleType);
    }

    private static XElement LoadConditionalFormattingContainer(Stream xlsxStream, string worksheetPath, string cfRuleType)
    {
        xlsxStream.Position = 0;
        using var archive = new ZipArchive(xlsxStream, ZipArchiveMode.Read, leaveOpen: true);
        var entry = archive.GetEntry(worksheetPath)!;
        XDocument doc;
        using (var entryStream = entry.Open())
            doc = XDocument.Load(entryStream);

        return doc.Root!
            .Elements(WorksheetNs + "conditionalFormatting")
            .Single(container => container.Elements(WorksheetNs + "cfRule")
                .Any(rule => (string?)rule.Attribute("type") == cfRuleType));
    }

    // ── R75-io-cf-classic-4-1: DateOccurring formula synthesis ─────────────────────────────────

    [Fact]
    public void Save_FreshlyAuthoredLast7DaysRule_SynthesizesFormula()
    {
        var workbook = new Workbook("R75DateOccurringLast7Days");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(DateTime.Today.ToOADate()));

        // Mirrors ConditionalFormatRuleBuilder.Build's actual output for a fresh "Date Occurring"
        // rule created through the ribbon/dialog: DateOccurringPeriod is set, FormulaText is left
        // null (nothing in the app populates it for this rule type).
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 5, 1)),
            Priority = 1,
            RuleType = CfRuleType.DateOccurring,
            DateOccurringPeriod = "last7Days",
        });

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);

        var cfRuleElement = LoadCfRuleElement(stream, Sheet1Path, "timePeriod");
        ((string?)cfRuleElement.Attribute("timePeriod")).Should().Be("last7Days");
        var formula = cfRuleElement.Element(WorksheetNs + "formula")?.Value;
        formula.Should().NotBeNullOrWhiteSpace(
            "without a <formula> child, real Excel evaluates the embedded boolean formula (not the " +
            "timePeriod attribute) and treats the rule as never-true, so the highlighting vanishes on " +
            "reopen (R75-io-cf-classic-4-1)");
        formula.Should().Be(
            "AND(TODAY()-FLOOR(A1,1)>=0,TODAY()-FLOOR(A1,1)<=6)",
            "the synthesized formula must match the standard Excel-generated last7Days formula relative " +
            "to the rule's top-left cell");
    }

    [Theory]
    [InlineData("yesterday", "FLOOR(A1,1)=TODAY()-1")]
    [InlineData("today", "FLOOR(A1,1)=TODAY()")]
    [InlineData("tomorrow", "FLOOR(A1,1)=TODAY()+1")]
    [InlineData("last7Days", "AND(TODAY()-FLOOR(A1,1)>=0,TODAY()-FLOOR(A1,1)<=6)")]
    [InlineData("thisWeek", "AND(TODAY()-ROUNDDOWN(A1,0)<=WEEKDAY(TODAY())-1,ROUNDDOWN(A1,0)-TODAY()<=7-WEEKDAY(TODAY()))")]
    [InlineData("lastWeek", "AND(TODAY()-ROUNDDOWN(A1,0)>=(WEEKDAY(TODAY())),TODAY()-ROUNDDOWN(A1,0)<(WEEKDAY(TODAY())+7))")]
    [InlineData("nextWeek", "AND(ROUNDDOWN(A1,0)-TODAY()>(7-WEEKDAY(TODAY())),ROUNDDOWN(A1,0)-TODAY()<(15-WEEKDAY(TODAY())))")]
    [InlineData("thisMonth", "AND(MONTH(A1)=MONTH(TODAY()),YEAR(A1)=YEAR(TODAY()))")]
    [InlineData("lastMonth", "AND(MONTH(A1)=MONTH(EDATE(TODAY(),0-1)),YEAR(A1)=YEAR(EDATE(TODAY(),0-1)))")]
    [InlineData("nextMonth", "AND(MONTH(A1)=MONTH(EDATE(TODAY(),0+1)),YEAR(A1)=YEAR(EDATE(TODAY(),0+1)))")]
    public void Save_EachDateOccurringPeriod_EmitsCorrectSynthesizedFormula(string period, string expectedFormula)
    {
        var workbook = new Workbook("R75DateOccurringEachPeriod");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(DateTime.Today.ToOADate()));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            Priority = 1,
            RuleType = CfRuleType.DateOccurring,
            DateOccurringPeriod = period,
        });

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);

        var cfRuleElement = LoadCfRuleElement(stream, Sheet1Path, "timePeriod");
        var formula = cfRuleElement.Element(WorksheetNs + "formula")?.Value;
        formula.Should().Be(expectedFormula, $"period '{period}' must synthesize Excel's own generated formula");
    }

    [Fact]
    public void RoundTrip_FreshlyAuthoredDateOccurringRule_StillMatchesOnReload()
    {
        var workbook = new Workbook("R75DateOccurringRoundTrip");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, new DateTimeValue(DateTime.Today.ToOADate()));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(addr, addr),
            Priority = 1,
            RuleType = CfRuleType.DateOccurring,
            DateOccurringPeriod = "today",
        });

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var reloadedSheet = adapter.Load(stream).GetSheetAt(0);
        var rule = reloadedSheet!.ConditionalFormats.Should().ContainSingle().Subject;
        rule.RuleType.Should().Be(CfRuleType.DateOccurring);
        rule.DateOccurringPeriod.Should().Be(
            "today",
            "FreeX's own evaluator (ViewportConditionalFormatEvaluator.MatchesDateOccurring) matches " +
            "purely off this attribute, so it -- not the synthesized formula -- is what keeps the rule " +
            "matching after a FreeX round-trip");
    }

    /// <summary>
    /// Sibling no-regression case: a rule that already carries an explicit FormulaText (e.g.
    /// round-tripped from a real Excel file) must not have it overwritten by the synthesized
    /// fallback formula.
    /// </summary>
    [Fact]
    public void Save_DateOccurringRuleWithExplicitFormulaText_NotOverwritten()
    {
        var workbook = new Workbook("R75DateOccurringExplicitFormula");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new DateTimeValue(DateTime.Today.ToOADate()));

        const string explicitFormula = "FLOOR(A1,1)=TODAY()-1";
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 1, 1)),
            Priority = 1,
            RuleType = CfRuleType.DateOccurring,
            DateOccurringPeriod = "yesterday",
            FormulaText = explicitFormula,
        });

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);

        var cfRuleElement = LoadCfRuleElement(stream, Sheet1Path, "timePeriod");
        cfRuleElement.Element(WorksheetNs + "formula")?.Value.Should().Be(
            explicitFormula,
            "a rule that already carries an explicit FormulaText must not have it overwritten by the " +
            "synthesized fallback formula (no regression from R75-io-cf-classic-4-1's fix)");
    }

    // ── R75-io-cf-classic-4-2: classic conditionalFormatting container pivot attribute ─────────

    [Fact]
    public void Save_CellIsRuleWithPivotContainerAttribute_PreservesPivotAttribute()
    {
        var workbook = new Workbook("R75CfClassicPivot");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new NumberValue(42)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(addr, addr),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "10",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) },
            NativeContainerAttributes = new Dictionary<string, string> { ["pivot"] = "1" },
        });

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);

        var container = LoadConditionalFormattingContainer(stream, Sheet1Path, "cellIs");
        ((string?)container.Attribute("pivot")).Should().Be(
            "1",
            "a classic rule's preserved pivot container attribute must survive save even though " +
            "ClosedXML's own classic-rule writer has no API surface to emit it at all " +
            "(R75-io-cf-classic-4-2)");

        // The rule's own condition must remain unaffected by the container-attribute fix.
        var cfRule = container.Element(WorksheetNs + "cfRule")!;
        ((string?)cfRule.Attribute("operator")).Should().Be("greaterThan");
        cfRule.Element(WorksheetNs + "formula")!.Value.Should().Be("10");
    }

    /// <summary>
    /// Direct unit test of the raw-XML capture side of the fix: <c>XlsxFileAdapter.ReadAdvancedConditionalFormats</c>
    /// must capture each classic (cellIs/expression) rule's real <c>&lt;conditionalFormatting&gt;</c>
    /// container's non-sqref attributes (e.g. <c>pivot="1"</c>) in the SAME document order as the
    /// existing <c>classicRulePriorities</c> out-param, so <see cref="XlsxConditionalFormatClosedXmlMapper.Load"/>
    /// can restore them onto <see cref="ConditionalFormat.NativeContainerAttributes"/> -- ClosedXML's own
    /// object model (<c>IXLConditionalFormat</c>) exposes no such attribute at all.
    /// <para>
    /// This calls the private method directly via reflection rather than going through a full
    /// <see cref="XlsxFileAdapter.Save"/>/<see cref="XlsxFileAdapter.Load"/> round-trip: ClosedXML's own
    /// worksheet parser silently excludes a classic rule whose container carries <c>pivot="1"</c> from
    /// its <c>IXLWorksheet.ConditionalFormats</c> enumeration entirely (verified empirically -- opening a
    /// FreeX-saved file with such a rule directly via <c>new XLWorkbook(stream)</c> yields zero
    /// conditional formats for that sheet, with no exception raised), so a full adapter-level round-trip
    /// can never observe this fix's effect for the "pivot" attribute specifically. That ClosedXML
    /// behavior is outside this fix's scope; this test instead verifies the actual capture logic added to
    /// <c>XlsxFileAdapter.ConditionalFormats.cs</c> in isolation.
    /// </para>
    /// </summary>
    [Fact]
    public void ReadAdvancedConditionalFormats_ClassicRuleWithPivotContainerAttribute_CapturesContainerAttributes()
    {
        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        var worksheetXml = XDocument.Parse(
            """
            <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
              <conditionalFormatting sqref="A1:A1" pivot="1">
                <cfRule type="cellIs" dxfId="0" priority="1" operator="greaterThan"><formula>10</formula></cfRule>
              </conditionalFormatting>
              <conditionalFormatting sqref="B1:B1">
                <cfRule type="cellIs" dxfId="0" priority="2" operator="greaterThan"><formula>5</formula></cfRule>
              </conditionalFormatting>
            </worksheet>
            """);

        var (priorities, containerAttributes) = InvokeReadAdvancedConditionalFormats(worksheetXml, ns);

        priorities.Should().Equal(new[] { 1, 2 }, "both classic rules' real file priorities must be captured, in document order");
        containerAttributes.Should().HaveCount(2);
        containerAttributes[0].Should().NotBeNull(
            "the first rule's container carries a preserved pivot=\"1\" attribute that must be captured " +
            "(R75-io-cf-classic-4-2) -- ClosedXML's own object model has no API surface to expose it");
        containerAttributes[0]!.Should().ContainKey("pivot").WhoseValue.Should().Be("1");
        containerAttributes[1].Should().BeNull(
            "the second rule's container has no preserved attributes, so no spurious entry must appear " +
            "(no-regression sibling)");
    }

    private static (IReadOnlyList<int> Priorities, IReadOnlyList<IReadOnlyDictionary<string, string>?> ContainerAttributes)
        InvokeReadAdvancedConditionalFormats(XDocument worksheetXml, XNamespace worksheetNs)
    {
        var method = typeof(XlsxFileAdapter)
            .GetMethods(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            .Single(m => m.Name == "ReadAdvancedConditionalFormats" && m.GetParameters().Length == 7);

        var args = new object?[]
        {
            worksheetXml,
            worksheetNs,
            new List<CellStyle>(),
            WorkbookTheme.Office,
            new WorkbookIndexedColorPalette(),
            null,
            null
        };
        method.Invoke(null, args);

        return (
            (IReadOnlyList<int>)args[5]!,
            (IReadOnlyList<IReadOnlyDictionary<string, string>?>)args[6]!);
    }

    /// <summary>
    /// Sibling no-regression case: a normal CellIs rule with no preserved container attributes
    /// must emit no spurious pivot attribute, and its condition must round-trip exactly as before.
    /// </summary>
    [Fact]
    public void Save_CellIsRuleWithoutContainerAttributes_EmitsNoPivotAttribute()
    {
        var workbook = new Workbook("R75CfClassicNoPivot");
        var sheet = workbook.AddSheet("Sheet1");
        var addr = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(addr, Cell.FromValue(new NumberValue(42)));

        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(addr, addr),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "10",
            FormatIfTrue = new CellStyle { FillColor = new CellColor(255, 0, 0) },
        });

        var adapter = new XlsxFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);

        var container = LoadConditionalFormattingContainer(stream, Sheet1Path, "cellIs");
        container.Attribute("pivot").Should().BeNull(
            "a rule with no preserved container attributes must not gain a spurious pivot attribute");

        stream.Position = 0;
        var reloaded = adapter.Load(stream);
        var reloadedSheet = reloaded.GetSheetAt(0);
        var rule = reloadedSheet!.ConditionalFormats.Should().ContainSingle().Subject;
        rule.Value1.Should().Be("10", "the ordinary CellIs round-trip must be unaffected by the container-attribute fix");
    }
}
