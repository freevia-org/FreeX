using System.Text;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// r319: closes the last item this review program was carrying.
///
/// <para>r298 measured it and left it open: .prn writes fixed-width, so a cell's column is its
/// POSITION, but the reader re-separated on whitespace runs and discarded position -- a row whose
/// leading columns were empty came back shifted left, and a value saved in B2 loaded into A2. It was
/// left open because inferring columns changes how every .prn imports, not only files FreeX wrote.
/// </para>
///
/// <para>What makes it safe now is the narrowness of the change: fields are still cut on whitespace
/// exactly as before, so no file re-tokenizes. Only the column INDEX comes from position, and only
/// when the file gives evidence of a grid -- more than one column, and some line indented past the
/// first. A file with no empty leading column takes the old path unchanged, which is the case where
/// a change could only introduce a difference rather than remove one.</para>
/// </summary>
public sealed class R319_PrnReadPreservesColumnPositionsTests
{
    private static Workbook Load(string content)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        return new PrnFileAdapter().Load(stream);
    }

    private static string? TextAt(Sheet sheet, uint row, uint col) =>
        sheet.GetCell(new CellAddress(sheet.Id, row, col))?.Value switch
        {
            TextValue text => text.Value,
            NumberValue number => number.Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            null => null,
            var other => other.ToString(),
        };

    [Fact]
    public void ALeadingEmptyColumnIsPreservedInsteadOfShiftingTheRowLeft()
    {
        // "Beta" sits under the second column on both lines; the second row's first column is empty.
        var sheet = Load("Alpha Beta\r\n      Beta\r\n").Sheets[0];

        TextAt(sheet, 1, 1).Should().Be("Alpha");
        TextAt(sheet, 1, 2).Should().Be("Beta");
        TextAt(sheet, 2, 1).Should().BeNull("the second row's first column is empty in the file");
        TextAt(sheet, 2, 2).Should().Be("Beta", "it was written under the second column");
    }

    [Fact]
    public void AFileWithNoEmptyLeadingColumnReadsExactlyAsBefore()
    {
        var sheet = Load("Alpha Beta\r\nGamma Delta\r\n").Sheets[0];

        TextAt(sheet, 1, 1).Should().Be("Alpha");
        TextAt(sheet, 1, 2).Should().Be("Beta");
        TextAt(sheet, 2, 1).Should().Be("Gamma");
        TextAt(sheet, 2, 2).Should().Be("Delta");
    }

    /// <summary>
    /// The property r298 said .prn could not have: what was saved is what loads back.
    /// </summary>
    [Fact]
    public void SavingASheetWithAnEmptyLeadingColumnAndReloadingKeepsTheShape()
    {
        var workbook = new Workbook("Book1");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new TextValue("Alpha"));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new TextValue("Beta"));
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new TextValue("Gamma"));

        using var stream = new MemoryStream();
        new PrnFileAdapter().Save(workbook, stream);
        stream.Position = 0;
        var reloaded = new PrnFileAdapter().Load(stream).Sheets[0];

        TextAt(reloaded, 1, 1).Should().Be("Alpha");
        TextAt(reloaded, 1, 2).Should().Be("Beta");
        TextAt(reloaded, 2, 1).Should().BeNull("A2 was empty when this was saved");
        TextAt(reloaded, 2, 2).Should().Be("Gamma", "B2 must come back in B2, not shifted into A2");
    }

    /// <summary>
    /// A line packing two tokens inside one inferred column must not lose one to an overwrite: the
    /// reader falls back to sequential columns for the rest of that line.
    /// </summary>
    [Fact]
    public void TwoTokensInsideOneInferredColumnBothSurvive()
    {
        var sheet = Load("Alpha      Beta\r\n   one two  Beta\r\n").Sheets[0];

        var row2 = new[] { TextAt(sheet, 2, 1), TextAt(sheet, 2, 2), TextAt(sheet, 2, 3) };
        row2.Should().Contain("one").And.Contain("two").And.Contain("Beta",
            "no token may be dropped, whichever columns they land in");
    }
}
