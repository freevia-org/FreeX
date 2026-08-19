using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for external-refs finding F1: <see cref="PasteCommandFactory.CreateInternalPasteCommand"/>
/// used to resolve the copied range's source sheet via <c>workbook.GetSheet(sourceRange.Start.Sheet)</c>
/// against the DESTINATION workbook only. That lookup is correct for an ordinary same-window paste
/// (source and destination share one <see cref="Workbook"/>), but always misses when the copy and
/// paste happen across two different open FreeX windows sharing one process-wide
/// WorkbookClipboardSession (see R143) -- each window's Workbook mints its own independently-GUID'd
/// <see cref="SheetId"/>s, so <c>sourceRange.Start.Sheet</c> can never be found inside the destination
/// Workbook. Every hyperlink-carrying branch gated on "sourceSheet is not null" then silently never
/// fires, so a hyperlink-bearing cell pastes its text/value but loses its hyperlink with no error.
///
/// The fix adds an optional <c>sourceSheetOverride</c> parameter that, when supplied, is used instead
/// of the destination-workbook lookup -- production wiring is
/// <c>WorkbookClipboardSnapshot.SourceSheet</c> (captured at Copy time in the owning window) forwarded
/// by <c>MainWindow.ExecutePaste</c>'s <c>CreatePasteCommand</c> local function
/// (src/FreeX.App.Host/MainWindow.ClipboardCommands.cs) into
/// <see cref="PasteCommandFactory.CreateInternalPasteCommand"/>.
/// </summary>
public sealed class PasteCommandFactory_CrossWorkbookHyperlinkTests
{
    [Fact]
    public void CrossWorkbookPaste_WithSourceSheetOverride_CarriesHyperlink()
    {
        // Models the two-window scenario exactly: sheetA and sheetB belong to two INDEPENDENT
        // Workbook instances (as two open FreeX windows would each have their own), so sheetA.Id
        // can never be found by workbookB.GetSheet(...).
        var workbookA = new Workbook("Book1");
        var sheetA = workbookA.AddSheet("Sheet1");
        var sourceAddress = new CellAddress(sheetA.Id, 1, 1); // A1
        var sourceCell = Cell.FromValue(new TextValue("Click me"));
        sheetA.SetCell(sourceAddress, sourceCell);
        sheetA.Hyperlinks[sourceAddress] = "https://example.com/report";
        sheetA.HyperlinkMetadata[sourceAddress] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            ScreenTip: "Report");

        var workbookB = new Workbook("Book2");
        var sheetB = workbookB.AddSheet("SheetOne");
        var ctx = new TestCommandContext(workbookB);
        var destinationAddress = new CellAddress(sheetB.Id, 3, 3); // C3

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            workbookB,
            sheetB.Id,
            new GridRange(sourceAddress, sourceAddress),
            [(sourceAddress, sourceCell.Clone())],
            new GridRange(destinationAddress, destinationAddress),
            PasteCellsMode.All,
            default,
            sourceAreas: null,
            mergeConditionalFormats: false,
            sourceSheetOverride: sheetA);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue("the plain cell value/formula paste must still succeed");

        sheetB.GetCell(destinationAddress)!.Value.Should().Be(new TextValue("Click me"));
        sheetB.Hyperlinks.Should().ContainKey(destinationAddress,
            "a cross-window paste with the source Sheet supplied via sourceSheetOverride must carry " +
            "the copied cell's hyperlink, exactly like real Excel and exactly like same-window paste " +
            "already does");
        sheetB.Hyperlinks[destinationAddress].Should().Be("https://example.com/report");
        sheetB.HyperlinkMetadata.Should().ContainKey(destinationAddress);
        sheetB.HyperlinkMetadata[destinationAddress].ScreenTip.Should().Be("Report");
    }

    /// <summary>
    /// Sibling no-regression check: a caller that does NOT supply <c>sourceSheetOverride</c> (every
    /// pre-existing call site except the one production fix in MainWindow.ClipboardCommands.cs) must
    /// keep its EXACT prior behavior for a genuine cross-workbook lookup miss -- silently paste the
    /// value with no hyperlink and no error, rather than throwing or behaving differently now that the
    /// parameter exists. This is what proves the fix is additive (a new opt-in seam) and did not change
    /// the default code path for any caller that hasn't been updated to pass the override.
    /// </summary>
    [Fact]
    public void CrossWorkbookPaste_WithoutSourceSheetOverride_StillDropsHyperlinkButPastesValue()
    {
        var workbookA = new Workbook("Book1");
        var sheetA = workbookA.AddSheet("Sheet1");
        var sourceAddress = new CellAddress(sheetA.Id, 1, 1);
        var sourceCell = Cell.FromValue(new TextValue("Click me"));
        sheetA.SetCell(sourceAddress, sourceCell);
        sheetA.Hyperlinks[sourceAddress] = "https://example.com/report";

        var workbookB = new Workbook("Book2");
        var sheetB = workbookB.AddSheet("SheetOne");
        var ctx = new TestCommandContext(workbookB);
        var destinationAddress = new CellAddress(sheetB.Id, 3, 3);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            workbookB,
            sheetB.Id,
            new GridRange(sourceAddress, sourceAddress),
            [(sourceAddress, sourceCell.Clone())],
            new GridRange(destinationAddress, destinationAddress),
            PasteCellsMode.All,
            default);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue();

        sheetB.GetCell(destinationAddress)!.Value.Should().Be(new TextValue("Click me"));
        sheetB.Hyperlinks.Should().NotContainKey(destinationAddress,
            "without an explicit sourceSheetOverride this remains the pre-existing (unfixed) " +
            "cross-workbook lookup-miss behavior -- callers must opt in by supplying the source Sheet");
    }

    /// <summary>
    /// Sibling no-regression check: the pre-existing SAME-workbook paste path (the common case,
    /// covering every call site that never passes sourceSheetOverride) must be entirely unaffected --
    /// hyperlinks still carry via the ordinary <c>workbook.GetSheet(sourceRange.Start.Sheet)</c> lookup
    /// when source and destination share one Workbook.
    /// </summary>
    [Fact]
    public void SameWorkbookPaste_StillCarriesHyperlinkViaOrdinaryLookup()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        var ctx = new TestCommandContext(workbook);
        var sourceAddress = new CellAddress(sheet.Id, 1, 1);
        var sourceCell = Cell.FromValue(new TextValue("Click me"));
        sheet.SetCell(sourceAddress, sourceCell);
        sheet.Hyperlinks[sourceAddress] = "https://example.com/report";
        var destinationAddress = new CellAddress(sheet.Id, 5, 5);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            workbook,
            sheet.Id,
            new GridRange(sourceAddress, sourceAddress),
            [(sourceAddress, sourceCell.Clone())],
            new GridRange(destinationAddress, destinationAddress),
            PasteCellsMode.All,
            default);

        command.Apply(ctx).Success.Should().BeTrue();

        sheet.Hyperlinks.Should().ContainKey(destinationAddress);
        sheet.Hyperlinks[destinationAddress].Should().Be("https://example.com/report");
    }

    /// <summary>
    /// Guards a near-miss found while building this fix: an EARLIER version of the fix redirected
    /// the single shared <c>sourceSheet</c> local (which also gates whether this method constructs
    /// a PasteDataValidationCommand/PasteMergedRegionsCommand for the paste) to
    /// <c>sourceSheetOverride</c>. That made a cross-window paste's data-validation-carry gate
    /// (unconditional whenever sourceSheet is non-null, per R137) start constructing a
    /// PasteDataValidationCommand carrying <c>sourceRange</c> from the OTHER window's workbook --
    /// and that command's own <c>Apply()</c> independently re-resolves
    /// <c>ctx.GetSheet(sourceRange.Start.Sheet)</c> against the DESTINATION context, which throws
    /// (the destination workbook has no sheet with that Id). That turned today's silent "merged
    /// regions/comments/CF/validation/pictures are dropped" cross-window behavior into an outright
    /// paste FAILURE -- worse than the original bug. This test supplies sourceSheetOverride (so the
    /// hyperlink fix is active) on a source sheet that ALSO has a merged region overlapping the
    /// copied cell, and asserts the paste still succeeds -- proving the fix does not widen the
    /// "sourceSheet is not null" gates that feed those Apply()-time-lookup commands.
    /// </summary>
    [Fact]
    public void CrossWorkbookPaste_WithSourceSheetOverride_AndSourceMergedRegion_StillSucceeds()
    {
        var workbookA = new Workbook("Book1");
        var sheetA = workbookA.AddSheet("Sheet1");
        var sourceAddress = new CellAddress(sheetA.Id, 1, 1);
        var sourceCell = Cell.FromValue(new TextValue("Click me"));
        sheetA.SetCell(sourceAddress, sourceCell);
        sheetA.Hyperlinks[sourceAddress] = "https://example.com/report";
        sheetA.AddMergedRegion(new GridRange(sourceAddress, new CellAddress(sheetA.Id, 1, 2)));

        var workbookB = new Workbook("Book2");
        var sheetB = workbookB.AddSheet("SheetOne");
        var ctx = new TestCommandContext(workbookB);
        var destinationAddress = new CellAddress(sheetB.Id, 3, 3);

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            workbookB,
            sheetB.Id,
            new GridRange(sourceAddress, sourceAddress),
            [(sourceAddress, sourceCell.Clone())],
            new GridRange(destinationAddress, destinationAddress),
            PasteCellsMode.All,
            default,
            sourceAreas: null,
            mergeConditionalFormats: false,
            sourceSheetOverride: sheetA);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(
            "the hyperlink-carry fix must not turn the pre-existing (unfixed) cross-window merged-" +
            "region/data-validation carry -- which lazily re-resolves sourceRange's sheet from the " +
            "DESTINATION context at its own Apply() time -- into an outright paste failure: " +
            outcome.ErrorMessage ?? "");
        sheetB.Hyperlinks.Should().ContainKey(destinationAddress,
            "the hyperlink fix itself must still be in effect alongside the merged-region no-crash guarantee");
    }
}
