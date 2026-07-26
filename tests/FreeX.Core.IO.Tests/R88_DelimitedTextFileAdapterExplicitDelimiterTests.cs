using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R88-io-text-import-wizard-5-3: a file-embedded "sep=X" first line must NOT override a delimiter the
/// caller explicitly chose (e.g. the Get Data wizard resolving a non-Detect <c>ImportDelimiterKind</c>).
/// <see cref="DelimitedTextFileAdapter"/> now takes an <c>allowSeparatorDirective</c> flag so a caller
/// that already resolved an explicit delimiter can pass <c>false</c> and keep it.
/// </summary>
public sealed class R88_DelimitedTextFileAdapterExplicitDelimiterTests
{
    [Fact]
    public void Load_WithSeparatorDirectiveDisallowed_IgnoresEmbeddedSepDirective()
    {
        // The file has an embedded "sep=;" line, but the caller (mirroring the Get Data wizard after the
        // user explicitly picked Comma) disallows honoring it.
        var adapter = new DelimitedTextFileAdapter(".csv", "Text", ',', allowSeparatorDirective: false);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("sep=;\r\nA,B,C\r\n1,2,3\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        // The first record is literal data split on the caller's comma, not a directive: 3 columns.
        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("sep=;"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(BlankValue.Instance);
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("A"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new TextValue("B"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new TextValue("C"));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 1)).Should().Be(new NumberValue(1));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 2)).Should().Be(new NumberValue(2));
        sheet.GetValue(new CellAddress(sheet.Id, 3, 3)).Should().Be(new NumberValue(3));
    }

    // No-regression sibling: the default (no flag passed, matching every pre-existing call site --
    // plain File-Open .txt/.tsv/.tab loads) must keep honoring the embedded sep= directive exactly as
    // before, e.g. so a double-clicked CSV-like file with "sep=;" still switches delimiter.
    [Fact]
    public void Load_WithDefaultConstructor_StillHonorsEmbeddedSepDirective()
    {
        var adapter = new DelimitedTextFileAdapter(".csv", "Text", ',');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("sep=;\r\nA;B;C\r\n1;2;3\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("A"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("B"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new TextValue("C"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new NumberValue(1));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(2));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new NumberValue(3));
    }
}
