using System.Globalization;
using FluentAssertions;
using FreeX.App.Services;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Integration.Tests;

/// <summary>
/// Round-152 backlog item B3: under a culture whose thousands separator is itself a whitespace
/// character (fr-FR uses U+202F narrow no-break space), typed cell entry
/// (CellEntryParser.CreateCell) and Ctrl+V external-clipboard paste
/// (PasteCommandFactory.CreateExternalTextPasteCommand -> ParseClipboardValue) must agree on the
/// same input text. The paste path already normalized whitespace-variant group separators
/// (r151-remediation, ExcelTextNumberParser.NormalizeGroupSeparatorSpaceVariants), but typed entry
/// did not: double.TryParse happens to treat an ordinary U+0020 as interchangeable with fr-FR's own
/// U+202F, so a space-typed "1 234,56" parsed fine on both paths, but a regular non-breaking space
/// U+00A0 (common when a value is pasted in from elsewhere, autofilled, or copied off a web page and
/// then re-typed) was accepted by paste and rejected as literal text by typed entry -- the two paths
/// disagreed on an identical string. Fixed by sharing the same normalizer on the typed-entry path.
/// </summary>
public sealed class R152_TypedEntryPasteNbspGroupingAgreementTests
{
    private sealed class CurrentCultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CurrentCultureScope(string cultureName)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        }

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }

    private const string NonBreakingSpaceGroupedNumber = "1 234,56"; // regular NBSP, not fr-FR's own U+202F

    [Fact]
    public void FrFrCulture_NonBreakingSpaceGroupedEntry_TypedAndPasted_AgreeOnNumericValue()
    {
        using var _ = new CurrentCultureScope("fr-FR");

        // Typed path: CellEntryParser.CreateCell is what the grid's cell-edit commit calls
        // (FreeX.App.Services.CellEntryCommitPlanner.cs).
        var typedCell = CellEntryParser.CreateCell(
            NonBreakingSpaceGroupedNumber,
            new CellAddress(SheetId.New(), 1, 1),
            useR1C1ReferenceStyle: false);

        // Pasted path: PasteCommandFactory.CreateExternalTextPasteCommand is what a real Ctrl+V of
        // externally-sourced plain text runs through.
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var pasteAddress = new CellAddress(sheet.Id, 1, 1);
        var pasteCommand = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id, pasteAddress, [[NonBreakingSpaceGroupedNumber]]);
        var outcome = pasteCommand.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var pastedValue = sheet.GetValue(pasteAddress);

        // The defect: typed entry landed as TextValue("1 234,56") while paste already landed
        // as NumberValue(1234.56). Both paths must agree.
        typedCell.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(1234.56);
        pastedValue.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(1234.56);
        typedCell.Value.Should().BeOfType<NumberValue>().Subject.Value.Should().Be(
            pastedValue.Should().BeOfType<NumberValue>().Subject.Value);
    }

    [Fact]
    public void FrFrCulture_PlainAsciiSpaceGroupedEntry_TypedAndPasted_StillAgree()
    {
        // Sibling no-regression: the ordinary-space case already worked on both paths before this
        // fix (via .NET's own leniency on the typed side) and must keep working identically.
        using var _ = new CurrentCultureScope("fr-FR");
        const string asciiSpaceGroupedNumber = "1 234,56";

        var typedCell = CellEntryParser.CreateCell(
            asciiSpaceGroupedNumber,
            new CellAddress(SheetId.New(), 1, 1),
            useR1C1ReferenceStyle: false);

        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var pasteAddress = new CellAddress(sheet.Id, 1, 1);
        var pasteCommand = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id, pasteAddress, [[asciiSpaceGroupedNumber]]);
        var outcome = pasteCommand.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        var pastedValue = sheet.GetValue(pasteAddress);

        typedCell.Value.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(1234.56);
        pastedValue.Should().BeOfType<NumberValue>()
            .Which.Value.Should().Be(1234.56);
        typedCell.Value.Should().BeOfType<NumberValue>().Subject.Value.Should().Be(
            pastedValue.Should().BeOfType<NumberValue>().Subject.Value);
    }
}
