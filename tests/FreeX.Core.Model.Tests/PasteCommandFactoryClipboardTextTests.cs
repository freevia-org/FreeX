using System.Globalization;
using FluentAssertions;
using FreeX.Core.Commands;
using FreeX.Core.Model;

namespace FreeX.Core.Model.Tests;

/// <summary>
/// Regression coverage for group E-clipboard-text:
/// - J11: external plain-text paste must coerce parenthesized negatives, thousands separators,
///   trailing-sign negatives, and TRUE/FALSE the way Excel does, and must honor a leading
///   apostrophe as a text escape, without regressing culture-safe decimal parsing.
/// - J41: a plain (mode All, default options) internal paste must carry legacy notes and threaded
///   comments from the source cells, clearing any stale comment already at the destination.
/// </summary>
public sealed class PasteCommandFactoryClipboardTextTests
{
    /// <summary>
    /// Pins CurrentCulture to en-US (dot decimal, comma thousands separator) for the duration of a
    /// test, so assertions about comma-grouped/parenthesized numeric coercion are deterministic
    /// regardless of the host machine's ambient locale.
    /// </summary>
    private sealed class CurrentCultureScope : IDisposable
    {
        private readonly CultureInfo _previous = CultureInfo.CurrentCulture;

        public CurrentCultureScope(string cultureName)
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
        }

        public void Dispose() => CultureInfo.CurrentCulture = _previous;
    }

    [Theory]
    [InlineData("(123)", -123)]
    [InlineData("(1,234.56)", -1234.56)]
    [InlineData("5-", -5)]
    [InlineData("1,234", 1234)]
    [InlineData("-1,234,567", -1234567)]
    public void ExternalTextPaste_CoercesExcelStyleNegativesAndThousandsSeparators(string text, double expected)
    {
        using var _ = new CurrentCultureScope("en-US");
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [[text]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(new NumberValue(expected));
    }

    [Theory]
    [InlineData("1,2345")]
    [InlineData("1,2")]
    [InlineData("12,34")]
    [InlineData("1,234,5")]
    public void ExternalTextPaste_RejectsMalformedThousandsGroupingAsText(string text)
    {
        using var _ = new CurrentCultureScope("en-US");
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [[text]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(new TextValue(text));
    }

    [Theory]
    [InlineData("TRUE", true)]
    [InlineData("FALSE", false)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void ExternalTextPaste_CoercesTrueFalseTokensToBool(string text, bool expected)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [[text]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(new BoolValue(expected));
    }

    [Theory]
    [InlineData("'123")]
    [InlineData("'TRUE")]
    [InlineData("'(123)")]
    [InlineData("'1,234")]
    public void ExternalTextPaste_LeadingApostropheForcesTextAndStripsTheApostrophe(string text)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 1, 1);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [[text]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(new TextValue(text[1..]));
    }

    [Fact]
    public void ExternalTextPaste_StillCoercesPlainDecimalsAndKeepsPlainTextAsText()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(
            sheet.Id,
            new CellAddress(sheet.Id, 3, 2),
            [["1", "Name"], ["2.5", "West"]]);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(new CellAddress(sheet.Id, 3, 2)).Should().Be(new NumberValue(1));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 3)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 4, 2)).Should().Be(new NumberValue(2.5));
        sheet.GetValue(new CellAddress(sheet.Id, 4, 3)).Should().Be(new TextValue("West"));
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void ExternalTextPaste_StillKeepsNonFiniteNumericTokensAsText(string text)
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var address = new CellAddress(sheet.Id, 3, 2);

        var command = PasteCommandFactory.CreateExternalTextPasteCommand(sheet.Id, address, [[text]]);
        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.GetValue(address).Should().Be(new TextValue(text));
    }

    // ── J41: default paste carries comments/notes and clears stale destination ones ──────────

    [Fact]
    public void InternalPaste_PlainPasteCarriesLegacyNoteFromSourceCell()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 5, 5);
        sheet.SetCell(source, Cell.FromValue(new TextValue("hi")));
        sheet.Comments[source] = "a note";

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sheet.GetCell(source)!)],
            destination,
            PasteCellsMode.All,
            default);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Comments[destination].Should().Be("a note");
    }

    [Fact]
    public void InternalPaste_PlainPasteCarriesThreadedCommentFromSourceCell()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 5, 5);
        sheet.SetCell(source, Cell.FromValue(new TextValue("hi")));
        sheet.ThreadedComments[source] = new ThreadedComment("thread text", "Quinn");

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sheet.GetCell(source)!)],
            destination,
            PasteCellsMode.All,
            default);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.ThreadedComments[destination].Text.Should().Be("thread text");
        sheet.ThreadedComments[destination].Author.Should().Be("Quinn");
    }

    [Fact]
    public void InternalPaste_PlainPasteClearsStaleDestinationCommentWhenSourceHasNone()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 5, 5);
        sheet.SetCell(source, Cell.FromValue(new TextValue("hi")));
        sheet.Comments[destination] = "stale note";

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sheet.GetCell(source)!)],
            destination,
            PasteCellsMode.All,
            default);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Comments.ContainsKey(destination).Should().BeFalse();
    }

    [Fact]
    public void InternalPaste_PlainPasteUndoRestoresStaleDestinationComment()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 5, 5);
        sheet.SetCell(source, Cell.FromValue(new TextValue("hi")));
        sheet.Comments[source] = "new note";
        sheet.Comments[destination] = "stale note";

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sheet.GetCell(source)!)],
            destination,
            PasteCellsMode.All,
            default);

        var outcome = command.Apply(ctx);
        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Comments[destination].Should().Be("new note");

        command.Revert(ctx);

        sheet.Comments[destination].Should().Be("stale note");
    }

    [Fact]
    public void InternalPaste_PasteValuesDoesNotCarryComments()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 5, 5);
        sheet.SetCell(source, Cell.FromValue(new TextValue("hi")));
        sheet.Comments[source] = "a note";

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sheet.GetCell(source)!)],
            destination,
            PasteCellsMode.Values,
            default);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Comments.ContainsKey(destination).Should().BeFalse();
    }

    [Fact]
    public void InternalPaste_PasteSpecialValuesAndNumberFormatsDoesNotCarryComments()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        var destination = new CellAddress(sheet.Id, 5, 5);
        sheet.SetCell(source, Cell.FromValue(new TextValue("hi")));
        sheet.Comments[source] = "a note";

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sheet.GetCell(source)!)],
            destination,
            PasteCellsMode.All,
            new PasteSpecialOptions(ContentKind: PasteSpecialContentKind.ValuesAndNumberFormats));

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Comments.ContainsKey(destination).Should().BeFalse();
    }

    [Fact]
    public void InternalPaste_TiledPasteCarriesCommentsToEveryRepeatedTile()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, Cell.FromValue(new TextValue("hi")));
        sheet.Comments[source] = "tiled note";

        var destinationRange = new GridRange(
            new CellAddress(sheet.Id, 3, 3),
            new CellAddress(sheet.Id, 4, 4));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sheet.GetCell(source)!)],
            destinationRange,
            PasteCellsMode.All,
            default);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Comments[new CellAddress(sheet.Id, 3, 3)].Should().Be("tiled note");
        sheet.Comments[new CellAddress(sheet.Id, 3, 4)].Should().Be("tiled note");
        sheet.Comments[new CellAddress(sheet.Id, 4, 3)].Should().Be("tiled note");
        sheet.Comments[new CellAddress(sheet.Id, 4, 4)].Should().Be("tiled note");
    }

    [Fact]
    public void InternalPaste_TiledPasteClearsStaleCommentsAcrossWholeFootprintWhenSourceHasNone()
    {
        var wb = new Workbook("test");
        var sheet = wb.AddSheet("Sheet1");
        var ctx = new TestCommandContext(wb);
        var source = new CellAddress(sheet.Id, 1, 1);
        sheet.SetCell(source, Cell.FromValue(new TextValue("hi")));

        var staleAddress = new CellAddress(sheet.Id, 4, 4);
        sheet.Comments[staleAddress] = "stale";

        var destinationRange = new GridRange(
            new CellAddress(sheet.Id, 3, 3),
            new CellAddress(sheet.Id, 4, 4));

        var command = PasteCommandFactory.CreateInternalPasteCommand(
            wb,
            sheet.Id,
            new GridRange(source, source),
            [(source, sheet.GetCell(source)!)],
            destinationRange,
            PasteCellsMode.All,
            default);

        var outcome = command.Apply(ctx);

        outcome.Success.Should().BeTrue(outcome.ErrorMessage);
        sheet.Comments.ContainsKey(staleAddress).Should().BeFalse();
    }
}
