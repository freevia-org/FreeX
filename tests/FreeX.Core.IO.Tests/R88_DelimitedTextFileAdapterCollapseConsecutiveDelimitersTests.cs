using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// R88-io-text-import-wizard-5-2: the Get Data wizard's "Treat consecutive delimiters as one" checkbox
/// was honored by the live PREVIEW (built via <c>ImportDataPlanner.PreviewText</c> ->
/// <c>TextToColumnsPlanner.Split</c>) but silently dropped by the actual import, which re-parsed the same
/// text through <c>DelimitedTextFileAdapter</c> -&gt; <c>DelimitedTextWorkbookReader.Load</c>, a method
/// with no parameter at all for collapsing consecutive delimiters. Every individual delimiter became a
/// field boundary, so a whitespace-aligned file misaligned against what the preview grid showed. Fixed by
/// threading a <c>collapseConsecutiveDelimiters</c> flag through <see cref="DelimitedTextFileAdapter"/>
/// into the reader's tokenizer, mirroring <c>TextToColumnsSplitter</c>'s "treat consecutive delimiters as
/// one" semantics (skip over immediately-following delimiter chars after a field boundary).
/// </summary>
public sealed class R88_DelimitedTextFileAdapterCollapseConsecutiveDelimitersTests
{
    [Fact]
    public void Load_WithCollapseConsecutiveDelimitersEnabled_TreatsDelimiterRunsAsOneSeparator()
    {
        // Two spaces between "A" and "B" must collapse to a single field boundary, matching the
        // preview's TextToColumnsPlanner.Split(..., treatConsecutiveDelimitersAsOne: true) behavior.
        var adapter = new DelimitedTextFileAdapter(
            ".csv", "Text", ' ', allowSeparatorDirective: true, collapseConsecutiveDelimiters: true);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("Name     Age   City\r\nJohn        30    NYC\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("Name"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(new TextValue("Age"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new TextValue("City"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 4)).Should().Be(BlankValue.Instance,
            "the run of spaces must collapse to one separator, not leave empty columns between the words");

        sheet.GetValue(new CellAddress(sheet.Id, 2, 1)).Should().Be(new TextValue("John"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 2)).Should().Be(new NumberValue(30));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 3)).Should().Be(new TextValue("NYC"));
        sheet.GetValue(new CellAddress(sheet.Id, 2, 4)).Should().Be(BlankValue.Instance);
    }

    // No-regression sibling: every pre-existing call site (none of which pass the new parameter) must
    // keep the old behavior -- every individual delimiter is its own field boundary, so a run of
    // delimiters produces empty fields in between exactly as before this option existed.
    [Fact]
    public void Load_WithoutCollapseConsecutiveDelimiters_KeepsEveryDelimiterAsItsOwnBoundary_NoRegression()
    {
        var adapter = new DelimitedTextFileAdapter(".csv", "Text", ' ');
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("A  B\r\n"));

        var workbook = adapter.Load(stream);
        var sheet = workbook.Sheets.Single();

        sheet.GetValue(new CellAddress(sheet.Id, 1, 1)).Should().Be(new TextValue("A"));
        sheet.GetValue(new CellAddress(sheet.Id, 1, 2)).Should().Be(BlankValue.Instance,
            "without the option, the empty field between the two consecutive spaces must remain");
        sheet.GetValue(new CellAddress(sheet.Id, 1, 3)).Should().Be(new TextValue("B"));
    }
}
