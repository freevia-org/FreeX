using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// csv-edge-cases-F1: plain CSV/TXT/TSV/TAB Save-As writes the OS ANSI code page (see
/// <see cref="DelimitedTextWorkbookWriter.ResolveAnsiEncoding"/>). Any character outside that code
/// page (CJK, Cyrillic on an en-US machine, emoji, ...) is silently replaced with a literal '?'
/// byte by .NET's default <see cref="System.Text.EncoderReplacementFallback"/> -- permanent data
/// loss once the source workbook is closed, with zero warning. This must now surface as a
/// non-fatal save warning through the same <see cref="IWarningCollectingFileAdapter"/> pipeline
/// <see cref="XlsxFileAdapter"/> already uses for its own partial-data-loss outcomes, reachable via
/// <see cref="FreeX.App.Services.WorkbookSaveService"/> for the real File&gt;Save As "CSV (Comma
/// delimited)" / "Text (Tab delimited)" entries (CsvFileAdapter / DelimitedTextFileAdapter -- see
/// WorkbookFileAdapterCatalog for the .csv/.txt/.tsv/.tab registrations that construct them).
/// </summary>
public sealed class CsvEdgeCasesF1_AnsiEncodingLossWarningTests
{
    [Fact]
    public void CsvSaveWithWarnings_ReportsWarning_WhenTextCannotBeRepresentedInAnsiCodePage()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        // Japanese text has no representation at all in Windows-1252 (en-US's ANSI code page).
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("日本語"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("emoji:\U0001F600"));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        var result = adapter.SaveWithWarnings(workbook, stream);

        result.HasWarnings.Should().BeTrue("the ANSI code page cannot represent the Japanese text or the emoji, so data was silently lost");
        result.Warnings.Should().ContainSingle();

        // The doc comment on SaveWithWarnings promises the on-disk bytes are UNCHANGED from the
        // plain Save() path -- warnings only add a signal, they must never change what gets
        // written. Assert the two entry points agree on the actual bytes, rather than hardcoding
        // a literal that only one of them currently produces (that hid a real divergence before:
        // SaveWithWarnings was silently writing one fewer '?' than Save for the emoji's surrogate
        // pair).
        using var plainStream = new MemoryStream();
        adapter.Save(workbook, plainStream);

        stream.ToArray().Should().Equal(plainStream.ToArray(),
            "SaveWithWarnings must write byte-for-byte the same output as plain Save -- only the warning reporting differs");

        // Pin the actual shape too, so a future change to either path that silently drifts them
        // both together (and so still "agrees") still gets caught. Real Excel/.NET's
        // EncoderReplacementFallback replaces the astral emoji's surrogate PAIR with one '?' per
        // UTF-16 code unit (two '?' bytes), not one '?' for the whole codepoint -- verified
        // directly against System.Text.EncoderReplacementFallback on code page 1252.
        stream.Position = 0;
        using var reader = new StreamReader(stream, DelimitedTextWorkbookWriter.ResolveAnsiEncoding());
        var text = reader.ReadToEnd();
        text.Should().Be("???,emoji:??\r\n");
    }

    /// <summary>
    /// Lone (unpaired) surrogates -- a high surrogate with no following low surrogate, or a low
    /// surrogate with no preceding high surrogate -- are not valid Unicode codepoints on their own.
    /// .NET's encoder treats each as an ordinary single unmappable char (one '?' each), the same as
    /// any other unrepresentable BMP character; it does NOT invoke the surrogate-pair fallback
    /// overload for them. This must produce the same bytes via both entry points too.
    /// </summary>
    [Fact]
    public void SaveWithWarnings_AgreesWithSave_ForLoneSurrogates()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("hi:" + '\uD800' + "X"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("lo:" + '\uDC00' + "Y"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new TextValue("end:" + '\uD800'));

        var adapter = new CsvFileAdapter();
        using var warnStream = new MemoryStream();
        var result = adapter.SaveWithWarnings(workbook, warnStream);

        result.HasWarnings.Should().BeTrue("lone surrogates have no representation in the ANSI code page");

        using var plainStream = new MemoryStream();
        adapter.Save(workbook, plainStream);

        warnStream.ToArray().Should().Equal(plainStream.ToArray(),
            "lone surrogates must fall back identically on both entry points");

        // Pin the shape: one '?' per lone surrogate (not two), matching a single unmappable BMP char.
        warnStream.Position = 0;
        using var reader = new StreamReader(warnStream, DelimitedTextWorkbookWriter.ResolveAnsiEncoding());
        reader.ReadToEnd().Should().Be("hi:?X,lo:?Y,end:?\r\n");
    }

    [Fact]
    public void DelimitedTextFileAdapterSaveWithWarnings_ReportsWarning_ForTsvNonAsciiLoss()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("日本語"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("normal"));

        var adapter = new DelimitedTextFileAdapter(".tsv", "TSV (Tab-separated values)", '\t');
        using var stream = new MemoryStream();
        var result = adapter.SaveWithWarnings(workbook, stream);

        result.HasWarnings.Should().BeTrue();
    }

    /// <summary>
    /// No-regression sibling: text that the resolved ANSI code page CAN represent exactly (plain
    /// ASCII, or accented Western-European text under en-US's Windows-1252) must still report no
    /// warnings -- the fix must only fire on genuine data loss, not on every save.
    /// </summary>
    [Fact]
    public void CsvSaveWithWarnings_ReportsNoWarnings_WhenAllTextIsRepresentableInAnsiCodePage()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("café"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(3.5));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        var result = adapter.SaveWithWarnings(workbook, stream);

        result.Should().BeSameAs(XlsxSaveResult.Clean);
        result.HasWarnings.Should().BeFalse();

        stream.Position = 0;
        using var reader = new StreamReader(stream, DelimitedTextWorkbookWriter.ResolveAnsiEncoding());
        reader.ReadToEnd().Should().Be("café,3.5\r\n");
    }

    /// <summary>
    /// No-regression sibling: the plain (non-warning-collecting) <c>Save</c> entry point that other
    /// callers may still use is untouched -- same lossy-but-silent behaviour, same bytes, as before
    /// this fix. Only the new SaveWithWarnings path adds detection.
    /// </summary>
    [Fact]
    public void CsvSave_StillWritesQuestionMarkFallback_WithoutThrowingOrChangingBytes()
    {
        using var cultureScope = TestCultureScope.CurrentCulture("en-US");
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("日本語"));

        var adapter = new CsvFileAdapter();
        using var stream = new MemoryStream();
        adapter.Save(workbook, stream);

        stream.Position = 0;
        using var reader = new StreamReader(stream, DelimitedTextWorkbookWriter.ResolveAnsiEncoding());
        reader.ReadToEnd().Should().Be("???\r\n");
    }
}
