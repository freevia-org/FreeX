namespace FreeW.Core.Model.Tests;

/// <summary>
/// Render-only resolution of an imported <c>w:style w:type="table"</c> definition: band sizes, the
/// <c>w:tblLook</c> gates, and the conditional-format precedence.
/// </summary>
public class ImportedTableStyleTests
{
    private const string Ns = "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"";

    private static ImportedTableStyle Resolve(string tblPr)
    {
        var styles = new Dictionary<string, DocumentStyle>
        {
            ["Bands"] = new()
            {
                Id = "Bands",
                Name = "Bands",
                Type = StyleType.Table,
                PreservedTableStyleXml =
                    $"<w:style {Ns} w:type=\"table\" w:styleId=\"Bands\">{tblPr}"
                    + "<w:tblStylePr w:type=\"firstRow\"><w:tcPr><w:shd w:val=\"clear\" w:fill=\"111111\"/></w:tcPr></w:tblStylePr>"
                    + "<w:tblStylePr w:type=\"band1Horz\"><w:tcPr><w:shd w:val=\"clear\" w:fill=\"AAAAAA\"/></w:tcPr></w:tblStylePr>"
                    + "<w:tblStylePr w:type=\"band2Horz\"><w:tcPr><w:shd w:val=\"clear\" w:fill=\"BBBBBB\"/></w:tcPr></w:tblStylePr>"
                    + "<w:tblStylePr w:type=\"band1Vert\"><w:tcPr><w:shd w:val=\"clear\" w:fill=\"CCCCCC\"/></w:tcPr></w:tblStylePr>"
                    + "</w:style>"
            }
        };
        return ImportedTableStyle.TryResolve(styles, "Bands")!;
    }

    private static string? Fill(ImportedTableStyle style, TableFormatting look, int row, int column = 1) =>
        style.ResolveCell(look, row, rowCount: 6, column, gridSpan: 1, gridColumnCount: 3).FillHex;

    [Fact]
    public void OmittedBandSize_DefaultsToOneRowAndOneColumnPerBand()
    {
        var style = Resolve(tblPr: string.Empty);
        var look = new TableFormatting { HeaderRow = true, BandedRows = true };

        style.RowBandSize.Should().Be(1);
        style.ColumnBandSize.Should().Be(1);
        Fill(style, look, row: 0).Should().Be("#111111");
        Fill(style, look, row: 1).Should().Be("#AAAAAA", "the first body row is band 1");
        Fill(style, look, row: 2).Should().Be("#BBBBBB");
        Fill(style, look, row: 3).Should().Be("#AAAAAA");
    }

    [Fact]
    public void DeclaredBandSize_GroupsRowsPerBand()
    {
        var style = Resolve("<w:tblPr><w:tblStyleRowBandSize w:val=\"2\"/></w:tblPr>");
        var look = new TableFormatting { BandedRows = true };

        new[] { 0, 1, 2, 3, 4 }.Select(row => Fill(style, look, row))
            .Should().Equal("#AAAAAA", "#AAAAAA", "#BBBBBB", "#BBBBBB", "#AAAAAA");
    }

    [Fact]
    public void TblLook_GatesBandsAndOnlyDefinedConditionsPaint()
    {
        var style = Resolve(tblPr: string.Empty);

        Fill(style, new TableFormatting { BandedRows = false }, row: 1)
            .Should().BeNull("w:tblLook noHBand suppresses horizontal bands");
        Fill(style, new TableFormatting { BandedColumns = true }, row: 1, column: 0).Should().Be("#CCCCCC");
        Fill(style, new TableFormatting { BandedColumns = true }, row: 1, column: 1)
            .Should().BeNull("the style defines no band2Vert formatting");
        Fill(style, new TableFormatting { BandedRows = true, BandedColumns = true }, row: 0, column: 0)
            .Should().Be("#AAAAAA", "horizontal bands outrank vertical bands");
    }
}
