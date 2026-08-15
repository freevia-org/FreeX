using FluentAssertions;
using FreeX.App.Presentation.Charts;
using FreeX.App.Presentation.Charts.Editing;
using FreeX.Core.Model;

namespace FreeX.App.Presentation.Tests.Charts;

/// <summary>
/// R137-app-select-data-source-multiarea: the same multi-area truncation shape that was fixed in
/// <c>XlsxChartSeriesRangeReader.TryParseFormulaRange</c> (R137-io-chart-series-multiarea) also
/// existed in this planner's own, independent <c>TryParseRangeReference</c>, which backs the
/// "Select Data Source" dialog's Legend Entries / Axis Labels preview lists in BOTH shells
/// (WPF <c>SelectDataSourceDialog</c> and Avalonia <c>ShowSelectDataSourceDialogAsync</c>).
/// <para>
/// It located the sheet-name boundary with <c>LastIndexOf('!')</c> and split the remainder on ':'.
/// For a union such as <c>Sheet1!$A$1:$A$5,Sheet1!$C$1:$D$5</c> that lands on the LAST area's
/// separator, so the tail parses cleanly and the whole formula "parses" as the final area alone --
/// every earlier area silently discarded, and <c>SheetName</c> left holding the garbage prefix
/// "Sheet1!$A$1:$A$5,Sheet1", which is then re-emitted verbatim into each previewed series range.
/// The prefix-less spelling (<c>$A$1:$A$5,$C$1:$C$5</c>) already returned null via the 3-way ':'
/// split, so the two spellings disagreed; the fix makes both take the fallback path.
/// </para>
/// <para>
/// Reachability (why this is a real defect, not a theoretical one): the dialog's chart-data-range
/// text box is free-text and its <c>TextChanged</c> handler re-runs <c>InferPreviewEntries</c> on
/// every keystroke, so a typed or pasted union reaches this parser directly. It is NOT reachable
/// from chart series formulas -- see the reachability note at the bottom of this file.
/// </para>
/// </summary>
public sealed class R137_SelectDataSourcePlannerMultiAreaTests
{
    // ---- The bug: a union must not parse as its last area -------------------------------------

    [Fact]
    public void InferPreviewEntries_TwoAreasOnOneSheet_UsesUnparseableFallbackInsteadOfLastAreaOnly()
    {
        const string union = "Sheet1!$A$1:$A$5,Sheet1!$C$1:$D$5";

        var preview = SelectDataSourcePlanner.InferPreviewEntries(union, firstColumnIsCategories: false);

        // Pre-fix this produced TWO series (columns C and D of the last area only), with column A's
        // area dropped entirely. The fallback path produces exactly one series echoing the raw text.
        preview.Series.Should().HaveCount(1,
            "a discontiguous union is not a single rectangle and must take the 'cannot parse' " +
            "fallback rather than being previewed as its last area alone");
        preview.Series[0].Name.Should().Be("Series 1");
        preview.Series[0].ValuesRangeText.Should().Be(union,
            "the fallback echoes the user's own text back rather than inventing a truncated range");
        preview.Categories.Should().ContainSingle()
            .Which.Label.Should().Be(SelectDataSourcePlanner.CategoryLabelsFallback);
        preview.CategoryRangeText.Should().BeEmpty();
    }

    [Fact]
    public void InferPreviewEntries_TwoAreasOnOneSheet_NeverLeaksGarbageSheetPrefixIntoPreview()
    {
        // The sharper symptom: pre-fix, SheetName was everything before the LAST '!', i.e.
        // "Sheet1!$A$1:$A$5,Sheet1" -- which FormatRangeReference then pasted in front of every
        // inferred series range, so the Legend Entries list showed entries containing a whole
        // embedded range as their "sheet name".
        var preview = SelectDataSourcePlanner.InferPreviewEntries(
            "Sheet1!$A$1:$A$5,Sheet1!$C$1:$D$5",
            firstColumnIsCategories: false);

        preview.Series.Should().OnlyContain(series => !series.ValuesRangeText.Contains(",", StringComparison.Ordinal)
                                                      || series.ValuesRangeText == "Sheet1!$A$1:$A$5,Sheet1!$C$1:$D$5",
            "the only entry allowed to contain a ',' is the verbatim fallback echo of the input");
        preview.Series.Should().NotContain(series => series.ValuesRangeText == "Sheet1!$A$1:$A$5,Sheet1!$C$2:$C$5",
            "a preview range built from the garbage 'Sheet1!$A$1:$A$5,Sheet1' sheet prefix must never appear");
    }

    [Fact]
    public void InferPreviewEntries_AreasOnDifferentSheets_UsesUnparseableFallback()
    {
        // The last area spans two columns on purpose. With a SINGLE-column last area the pre-fix
        // truncation was invisible to a text assertion: FormatRangeReference re-concatenated the
        // garbage sheet prefix with the surviving area and happened to reproduce the input string
        // character for character. A multi-column last area splits into one entry per column, so the
        // dropped first area actually shows up in the assertion.
        const string union = "Sheet1!$A$1:$A$5,Sheet2!$C$1:$D$5";

        var preview = SelectDataSourcePlanner.InferPreviewEntries(union, firstColumnIsCategories: false);

        preview.Series.Should().ContainSingle(
            "pre-fix this previewed Sheet2's C and D columns as the whole chart, with Sheet1's area gone")
            .Which.ValuesRangeText.Should().Be(union);
    }

    [Fact]
    public void InferPreviewEntries_MoreThanTwoAreas_UsesUnparseableFallback()
    {
        const string union = "Sheet1!$A$1:$A$5,Sheet1!$C$1:$C$5,Sheet1!$E$1:$F$5";

        var preview = SelectDataSourcePlanner.InferPreviewEntries(union, firstColumnIsCategories: false);

        preview.Series.Should().ContainSingle()
            .Which.ValuesRangeText.Should().Be(union);
    }

    [Fact]
    public void InferPreviewEntries_UnionWithSwitchRowColumn_AlsoUsesUnparseableFallback()
    {
        // The transposed branch reads the same ParsedRange, so it truncated identically pre-fix.
        const string union = "Sheet1!$A$1:$B$5,Sheet1!$D$1:$E$5";

        var preview = SelectDataSourcePlanner.InferPreviewEntries(
            union,
            firstColumnIsCategories: true,
            switchRowColumn: true);

        preview.Series.Should().ContainSingle()
            .Which.ValuesRangeText.Should().Be(union);
        preview.CategoryRangeText.Should().BeEmpty();
    }

    [Fact]
    public void InferPreviewEntries_PrefixlessUnion_KeepsItsPreExistingFallbackBehaviour()
    {
        // This spelling already fell out to the fallback (no '!' -> the ':' split yields 3 parts),
        // and must keep doing so -- it is the behaviour the sheet-prefixed spelling now matches.
        const string union = "$A$1:$A$5,$C$1:$C$5";

        var preview = SelectDataSourcePlanner.InferPreviewEntries(union, firstColumnIsCategories: false);

        preview.Series.Should().ContainSingle()
            .Which.ValuesRangeText.Should().Be(union);
    }

    // ---- The preview agreed with nothing: OK-time validation rejects unions too ----------------

    [Theory]
    [InlineData("Sheet1!$A$1:$A$5,Sheet1!$C$1:$D$5")]
    [InlineData("Sheet1!$A$1:$A$5,Sheet2!$C$1:$C$5")]
    [InlineData("$A$1:$A$5,$C$1:$C$5")]
    public void ChartInputParser_RejectsEveryUnionSpelling_SoAPreviewOfOneAreaDescribedAnUnappliableRange(string union)
    {
        // Confirms the severity boundary AND the direction of the fix: the dialog's OK handler
        // (SelectDataSourceDialog.ValidateInputs / ShowSelectChartDataDialog) runs the union through
        // ChartInputParser, which rejects it, so no truncated range was ever committed to the chart.
        // The damage was confined to the preview lists -- but that also means returning null (the
        // "cannot parse" path) is the spelling that actually agrees with validation.
        var sheetId = new SheetId(Guid.NewGuid());

        ChartInputParser.TryParseDataRange(union, sheetId, name => name == "Sheet2" ? new SheetId(Guid.NewGuid()) : null, out _)
            .Should().BeFalse();
    }

    // ---- Sibling no-regression checks -----------------------------------------------------------

    [Fact]
    public void InferPreviewEntries_QuotedSheetNameContainingComma_StillParsesAsASingleArea()
    {
        // A comma INSIDE a quoted sheet name is not a union separator; the new check uses the
        // quote-aware WorkbookRangeTextCodec.SplitReferences rather than a naive IndexOf(',').
        var preview = SelectDataSourcePlanner.InferPreviewEntries(
            "'Budget, Q1'!$A$1:$C$5",
            firstColumnIsCategories: false);

        preview.Series.Should().HaveCount(3, "columns A, B and C of a normally parsed single area");
        preview.Series[0].ValuesRangeText.Should().Be("Budget, Q1!$A$1:$A$5");
    }

    [Fact]
    public void InferPreviewEntries_OrdinarySingleAreaWithSheetPrefix_StillParsesSuccessfully()
    {
        var preview = SelectDataSourcePlanner.InferPreviewEntries(
            "Sheet1!$A$1:$D$4",
            firstColumnIsCategories: true);

        preview.Series.Should().HaveCount(3);
        preview.Series[0].ValuesRangeText.Should().Be("Sheet1!$B$2:$B$4");
        preview.CategoryRangeText.Should().Be("Sheet1!$A$2:$A$4");
    }

    [Fact]
    public void InferPreviewEntries_TrailingCommaOnASingleArea_FallsBackForTheSamePreExistingReason()
    {
        // Not a union: SplitReferences drops the empty trailing segment, so the new check does not
        // fire here. "Sheet1!$A$1:$C$3," still reaches the fallback -- but via the pre-existing
        // cell-ref parse ("C3," is not a cell address), exactly as it did before this fix. Asserted
        // so a future rewrite of the union check cannot quietly start attributing this to itself.
        WorkbookRangeTextCodec.SplitReferences("Sheet1!$A$1:$C$3,").Should().ContainSingle();

        var preview = SelectDataSourcePlanner.InferPreviewEntries(
            "Sheet1!$A$1:$C$3,",
            firstColumnIsCategories: false);

        preview.Series.Should().ContainSingle()
            .Which.ValuesRangeText.Should().Be("Sheet1!$A$1:$C$3,");
    }

    // ---- Reachability note (why no ChartModel-side test exists here) ---------------------------
    //
    // TryParseRangeReference is only ever fed the dialog's chart-data-range TEXT BOX contents:
    //
    //   * WPF     - SelectDataSourceDialog.Actions.cs RefreshPreviewLists() passes _rangeBox.Text;
    //               _rangeBox is seeded in the ctor from the `sourceRangeText` argument.
    //   * Avalonia- MainWindow.ChartTabs.cs RefreshLists() passes rangeBox.Text, seeded from the
    //               `initialRange` argument.
    //
    // Both shells seed that argument from FormatRangeReference(chart.DataRange) -- a single
    // rectangular GridRange -- in SelectChartDataSourceBtn_Click / ShowSelectChartDataDialog, and
    // the in-dialog range picker writes back one formatted GridRange too. Neither shell's
    // "Edit Series" button opens a per-series editor (both are stubs that merely move the ListBox
    // selection; FreeX has no per-series range storage the dialog could edit), and nothing in the
    // presentation or shell layers reads ChartModel.VerbatimSeriesFormulas at all -- it is consumed
    // only by FreeX.Core.IO's writer and by FreeX.Core.Commands' shift/clone helpers. So a
    // multi-area ValFormula preserved by the R137 XlsxChartSeriesRangeReader fix never flows into
    // this planner; the reachable source of union text is the user typing or pasting one into the
    // range box, which the TextChanged handler feeds straight into InferPreviewEntries.
}
