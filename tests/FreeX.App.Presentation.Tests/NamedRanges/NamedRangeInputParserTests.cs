using FluentAssertions;
using FreeX.App.Presentation.NamedRanges;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.NamedRanges;

public sealed class NamedRangeInputParserTests
{
    [Fact]
    public void TryParseRange_ParsesUnqualifiedRangeOnFirstSheet()
    {
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("Sheet1");

        NamedRangeInputParser.TryParseRange(workbook, " A1:B2 ", out var range).Should().BeTrue();

        range.Start.Should().Be(new CellAddress(sheet.Id, 1, 1));
        range.End.Should().Be(new CellAddress(sheet.Id, 2, 2));
    }

    [Fact]
    public void TryParseRange_ParsesQuotedSheetQualifiedRange()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");
        var sheet = workbook.AddSheet("Sales FY26");

        NamedRangeInputParser.TryParseRange(workbook, "'Sales FY26'!C3:D4", out var range).Should().BeTrue();

        range.Start.Should().Be(new CellAddress(sheet.Id, 3, 3));
        range.End.Should().Be(new CellAddress(sheet.Id, 4, 4));
    }

    [Fact]
    public void TryParseRange_RejectsUnknownSheetQualifiedRange()
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");

        NamedRangeInputParser.TryParseRange(workbook, "Missing!C3:D4", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("bad")]
    [InlineData("A1:B2:C3")]
    public void TryParseRange_RejectsBlankOrMalformedText(string input)
    {
        var workbook = new Workbook("Book");
        workbook.AddSheet("Sheet1");

        NamedRangeInputParser.TryParseRange(workbook, input, out _).Should().BeFalse();
    }

    [Fact]
    public void TryParseRange_RejectsWorkbookWithoutSheets()
    {
        NamedRangeInputParser.TryParseRange(new Workbook("Book"), "A1:B2", out _).Should().BeFalse();
    }

    // F2: a sheet-scoped named FORMULA shadows a same-named workbook-global named RANGE (Excel
    // scope precedence is per-NAME, not per-kind). NamedRangeInputParser.TryParseRange must not
    // fall through to the shadowed global range when resolving on the sheet that owns the
    // scoped formula -- this is what backs the New Name/Edit Name "Refers to" box and the chart
    // "Select Data Source" range box (both routed through DefinedNamesSession.TryParseRange).
    [Fact]
    public void TryParseRange_DoesNotResolveGlobalRangeShadowedByLocalScopedFormula()
    {
        var workbook = new Workbook("Book");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        workbook.DefineNamedRange("Foo", new GridRange(
            new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 3, 1)));
        workbook.DefineNamedFormula("Foo", "A1:A2", sheet2.Id);

        NamedRangeInputParser.TryParseRange(workbook, sheet2.Id, "=Foo", out var range).Should().BeFalse();
        range.Should().Be(default(GridRange));
    }

    // Sibling/no-regression: when the sheet's OWN local definition is itself a scoped named
    // RANGE (not a formula), it must still resolve to that sheet's own range and take
    // precedence over the same-named workbook-global range, exactly as before this fix.
    [Fact]
    public void TryParseRange_ResolvesLocalScopedRangeOverShadowedGlobalRange()
    {
        var workbook = new Workbook("Book");
        var sheet1 = workbook.AddSheet("Sheet1");
        var sheet2 = workbook.AddSheet("Sheet2");

        workbook.DefineNamedRange("Foo", new GridRange(
            new CellAddress(sheet1.Id, 1, 1), new CellAddress(sheet1.Id, 3, 1)));
        workbook.DefineNamedRange(
            "Foo",
            new GridRange(new CellAddress(sheet2.Id, 5, 2), new CellAddress(sheet2.Id, 6, 2)),
            metadata: null,
            sheet2.Id);

        NamedRangeInputParser.TryParseRange(workbook, sheet2.Id, "=Foo", out var range).Should().BeTrue();

        range.Start.Should().Be(new CellAddress(sheet2.Id, 5, 2));
        range.End.Should().Be(new CellAddress(sheet2.Id, 6, 2));
    }
}
