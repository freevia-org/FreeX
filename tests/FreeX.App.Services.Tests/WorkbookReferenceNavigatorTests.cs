using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class WorkbookReferenceNavigatorTests
{
    [Fact]
    public void TryParseAddress_AcceptsA1ReferenceOnCurrentSheet()
    {
        var sheetId = SheetId.New();

        WorkbookReferenceNavigator.TryParseAddress("B5", sheetId, out var address).Should().BeTrue();

        address.Should().Be(new CellAddress(sheetId, 5, 2));
    }

    [Fact]
    public void TryParseAddress_AcceptsExcelAbsoluteA1Reference()
    {
        var sheetId = SheetId.New();

        WorkbookReferenceNavigator.TryParseAddress("$B$5", sheetId, out var address).Should().BeTrue();

        address.Should().Be(new CellAddress(sheetId, 5, 2));
    }

    [Theory]
    [InlineData("Sheet1!$B$5")]
    [InlineData("$B$5:$C$6")]
    [InlineData("'[Book.xlsx]Sheet 1'!$B$5")]
    public void TryParseAddress_RejectsQualifiedOrRangeInput(string input)
    {
        WorkbookReferenceNavigator.TryParseAddress(input, SheetId.New(), out _).Should().BeFalse();
    }

    [Fact]
    public void TryParseAddress_AcceptsAbsoluteR1C1Reference()
    {
        var sheetId = SheetId.New();

        WorkbookReferenceNavigator.TryParseAddress("R5C2", sheetId, out var address).Should().BeTrue();

        address.Should().Be(new CellAddress(sheetId, 5, 2));
    }

    [Theory]
    [InlineData("")]
    [InlineData("NotACell")]
    [InlineData("A0")]
    [InlineData("R0C1")]
    public void TryParseAddress_RejectsInvalidReference(string input)
    {
        WorkbookReferenceNavigator.TryParseAddress(input, SheetId.New(), out _).Should().BeFalse();
    }

    [Fact]
    public void TryParseReference_ResolvesDefinedNameToRangeStart()
    {
        var sheetId = SheetId.New();
        var names = new Dictionary<string, GridRange>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sales_Total"] = new(
                new CellAddress(sheetId, 10, 2),
                new CellAddress(sheetId, 12, 4))
        };

        WorkbookReferenceNavigator.TryParseReference("sales_total", sheetId, names, out var address).Should().BeTrue();

        address.Should().Be(new CellAddress(sheetId, 10, 2));
    }

    [Fact]
    public void TryParseReferenceRange_ResolvesDefinedNameToFullRange()
    {
        var sheetId = SheetId.New();
        var namedRange = new GridRange(
            new CellAddress(sheetId, 10, 2),
            new CellAddress(sheetId, 12, 4));
        var names = new Dictionary<string, GridRange>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sales_Total"] = namedRange
        };

        WorkbookReferenceNavigator.TryParseReferenceRange("sales_total", sheetId, names, out var parsed).Should().BeTrue();

        parsed.Should().Be(namedRange);
    }

    [Fact]
    public void TryParseReferenceRange_AcceptsTypedCurrentSheetRange()
    {
        var sheetId = SheetId.New();

        WorkbookReferenceNavigator.TryParseReferenceRange("A1:C3", sheetId, definedNames: null, out var range).Should().BeTrue();

        range.Should().Be(new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)));
    }

    [Fact]
    public void TryParseReferenceRange_AcceptsExcelAbsoluteA1Range()
    {
        var sheetId = SheetId.New();

        WorkbookReferenceNavigator.TryParseReferenceRange("$A$1:$C$3", sheetId, definedNames: null, out var range).Should().BeTrue();

        range.Should().Be(new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)));
    }

    [Fact]
    public void TryParseReferenceRange_AcceptsSheetQualifiedRange()
    {
        var currentSheetId = SheetId.New();
        var dataSheetId = SheetId.New();

        WorkbookReferenceNavigator.TryParseReferenceRange(
            "'Data Sheet'!B2:C4",
            currentSheetId,
            sheetName => string.Equals(sheetName, "Data Sheet", StringComparison.OrdinalIgnoreCase) ? dataSheetId : null,
            definedNames: null,
            out var range).Should().BeTrue();

        range.Should().Be(new GridRange(new CellAddress(dataSheetId, 2, 2), new CellAddress(dataSheetId, 4, 3)));
    }

    [Fact]
    public void TryParseReferenceRange_ResolvesSheetQualifiedDefinedName()
    {
        // R62-commands-name-box-6-3: "Sheet2!Rate" is legal defined-name syntax in Excel's Name Box
        // (mirroring formulas, e.g. "=Sheet2!Rate") and must resolve the same as the bare name
        // "Rate" once the sheet prefix is stripped, instead of failing to match any key because the
        // un-stripped "Sheet2!Rate" text is used verbatim for the dictionary lookup.
        var currentSheetId = SheetId.New();
        var dataSheetId = SheetId.New();
        var namedRange = new GridRange(
            new CellAddress(dataSheetId, 2, 2),
            new CellAddress(dataSheetId, 2, 2));
        var names = new Dictionary<string, GridRange>(StringComparer.OrdinalIgnoreCase)
        {
            ["Rate"] = namedRange
        };

        WorkbookReferenceNavigator.TryParseReferenceRange(
            "Sheet2!Rate",
            currentSheetId,
            sheetName => string.Equals(sheetName, "Sheet2", StringComparison.OrdinalIgnoreCase) ? dataSheetId : null,
            names,
            out var range).Should().BeTrue();

        range.Should().Be(namedRange);
    }

    [Fact]
    public void TryParseReferenceRange_UnqualifiedDefinedNameStillResolvesFromCurrentSheet()
    {
        // Sibling no-regression case: a plain (unqualified) name lookup must be completely
        // unaffected by the sheet-prefix-stripping fix above.
        var currentSheetId = SheetId.New();
        var namedRange = new GridRange(
            new CellAddress(currentSheetId, 10, 2),
            new CellAddress(currentSheetId, 12, 4));
        var names = new Dictionary<string, GridRange>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sales_Total"] = namedRange
        };

        WorkbookReferenceNavigator.TryParseReferenceRange(
            "Sales_Total",
            currentSheetId,
            static _ => null,
            names,
            out var range).Should().BeTrue();

        range.Should().Be(namedRange);
    }

    [Fact]
    public void TryParseReferenceRange_ScopedNameLookupUsesQualifierSheet_NotActiveSheet()
    {
        // R64-meta-2: "Sheet2!Rate" strips the sheet prefix and resolves the RESOLVED qualifier
        // sheet (Sheet2) into the scoped-name lookup, not the caller's active sheet (Sheet1) --
        // otherwise a name scoped to Sheet2 would fail (or resolve the wrong sheet's Rate) because
        // the lookup ran against Sheet1 instead of the sheet the user explicitly qualified.
        var activeSheetId = SheetId.New();
        var dataSheetId = SheetId.New();
        var sheet2Rate = new GridRange(new CellAddress(dataSheetId, 2, 2), new CellAddress(dataSheetId, 2, 2));
        var scoped = new Dictionary<(SheetId Sheet, string Name), GridRange>
        {
            [(dataSheetId, "Rate")] = sheet2Rate
        };

        WorkbookReferenceNavigator.TryParseReferenceRange(
            "Sheet2!Rate",
            activeSheetId,
            sheetName => string.Equals(sheetName, "Sheet2", StringComparison.OrdinalIgnoreCase) ? dataSheetId : null,
            definedNames: null,
            resolveScopedName: (name, sheetId) => scoped.TryGetValue((sheetId, name), out var found) ? found : null,
            out var range).Should().BeTrue();

        range.Should().Be(sheet2Rate);
    }

    [Fact]
    public void TryParseReferenceRange_UnqualifiedScopedNameResolvesAgainstActiveSheet()
    {
        // Sibling no-regression case: without a sheet prefix, the scoped-name lookup still uses the
        // caller's active/default sheet, exactly as before this fix.
        var activeSheetId = SheetId.New();
        var otherSheetId = SheetId.New();
        var activeSheetRate = new GridRange(new CellAddress(activeSheetId, 3, 3), new CellAddress(activeSheetId, 3, 3));
        var scoped = new Dictionary<(SheetId Sheet, string Name), GridRange>
        {
            [(activeSheetId, "Rate")] = activeSheetRate,
            [(otherSheetId, "Rate")] = new GridRange(new CellAddress(otherSheetId, 9, 9), new CellAddress(otherSheetId, 9, 9))
        };

        WorkbookReferenceNavigator.TryParseReferenceRange(
            "Rate",
            activeSheetId,
            static _ => null,
            definedNames: null,
            resolveScopedName: (name, sheetId) => scoped.TryGetValue((sheetId, name), out var found) ? found : null,
            out var range).Should().BeTrue();

        range.Should().Be(activeSheetRate);
    }

    [Fact]
    public void TryParseReferenceRange_FallsBackToGlobalDefinedNameWhenNoScopedNameMatches()
    {
        // Sibling no-regression case: a workbook-global defined name (not present in the
        // sheet-scoped lookup at all) must still resolve via the definedNames dictionary fallback.
        var activeSheetId = SheetId.New();
        var namedRange = new GridRange(new CellAddress(activeSheetId, 10, 2), new CellAddress(activeSheetId, 12, 4));
        var names = new Dictionary<string, GridRange>(StringComparer.OrdinalIgnoreCase)
        {
            ["Sales_Total"] = namedRange
        };

        WorkbookReferenceNavigator.TryParseReferenceRange(
            "Sales_Total",
            activeSheetId,
            static _ => null,
            names,
            resolveScopedName: static (_, _) => null,
            out var range).Should().BeTrue();

        range.Should().Be(namedRange);
    }

    [Fact]
    public void TryParseReferenceRanges_ParsesDisjointSingleCellAreas()
    {
        // R78-render-selection-namebox-5-1: typing "A1,C3" into the Name Box must select a two-area
        // (disjoint) selection -- A1 and C3 -- exactly like Ctrl+clicking both cells in real Excel.
        var sheetId = SheetId.New();

        WorkbookReferenceNavigator.TryParseReferenceRanges("A1,C3", sheetId, definedNames: null, out var ranges)
            .Should().BeTrue();

        ranges.Should().Equal(
            new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 1, 1)),
            new GridRange(new CellAddress(sheetId, 3, 3), new CellAddress(sheetId, 3, 3)));
    }

    [Fact]
    public void TryParseReferenceRanges_ParsesMixedSingleCellAndRangeAreas()
    {
        var sheetId = SheetId.New();

        WorkbookReferenceNavigator.TryParseReferenceRanges("A1:B2,D4", sheetId, definedNames: null, out var ranges)
            .Should().BeTrue();

        ranges.Should().Equal(
            new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 2, 2)),
            new GridRange(new CellAddress(sheetId, 4, 4), new CellAddress(sheetId, 4, 4)));
    }

    [Fact]
    public void TryParseReferenceRanges_SingleAreaWithNoCommaMatchesSingularOverload()
    {
        // No-regression sibling: a plain single-area reference (no top-level comma) must still parse
        // identically to the singular TryParseReferenceRange overload.
        var sheetId = SheetId.New();

        WorkbookReferenceNavigator.TryParseReferenceRanges("A1:C3", sheetId, definedNames: null, out var ranges)
            .Should().BeTrue();

        ranges.Should().Equal(new GridRange(new CellAddress(sheetId, 1, 1), new CellAddress(sheetId, 3, 3)));
    }

    [Fact]
    public void TryParseReferenceRanges_FailsWhenAnyAreaIsInvalid()
    {
        var sheetId = SheetId.New();

        WorkbookReferenceNavigator.TryParseReferenceRanges("A1,NotACell", sheetId, definedNames: null, out var ranges)
            .Should().BeFalse();

        ranges.Should().BeEmpty();
    }

    [Fact]
    public void BuildReferenceChoices_PutsDefaultThenRecentThenSortedNamesWithoutDuplicates()
    {
        var choices = WorkbookReferenceNavigator.BuildReferenceChoices(
            "B5",
            ["B5", "D10"],
            ["zName", "Alpha"]);

        choices.Should().Equal("B5", "D10", "Alpha", "zName");
    }
}
