namespace FreeW.Core.Model.Tests;

/// <summary>
/// Core-Model coverage for the <see cref="DocumentTableStyle"/> catalog: non-empty, each style coherent,
/// round-trip id field on <see cref="Table"/>, and the <see cref="DocumentTableStyle.ResolveCellStyle"/>
/// helper returns the expected fill/bold for the common positions.
/// </summary>
public class DocumentTableStyleTests
{
    private sealed class CommandContext(TextDocument document) : IDocumentCommandContext
    {
        public TextDocument Document => document;
    }

    [Fact]
    public void ApplyTableStyleCommand_UndoRedoRestoresStyleAndFormatting()
    {
        var document = TextDocument.CreateEmpty();
        var table = Table.Create(2, 2);
        table.Formatting = table.Formatting with { HeaderRow = true, BandedRows = true, Borders = false };
        document.Blocks.Add(table);
        var originalFormatting = table.Formatting;
        var style = DocumentTableStyle.FindById("GridTable1Light")!;
        var bus = new DocumentCommandBus(new CommandContext(document));

        bus.Execute(new ApplyTableStyleCommand(1, style));
        table.TableStyleId.Should().Be(style.WordStyleId);
        table.Formatting.Borders.Should().Be(style.Borders);

        bus.Undo().Should().BeTrue();
        table.TableStyleId.Should().BeNull();
        table.Formatting.Should().Be(originalFormatting);

        bus.Redo().Should().BeTrue();
        table.TableStyleId.Should().Be(style.WordStyleId);
        table.Formatting.HeaderRow.Should().BeTrue();
        table.Formatting.BandedRows.Should().BeTrue();
    }

    [Fact]
    public void Catalog_IsNonEmpty()
    {
        DocumentTableStyle.Catalog.Should().NotBeEmpty();
    }

    [Fact]
    public void Catalog_EachStyle_HasNonEmptyNameAndWordStyleId()
    {
        foreach (var style in DocumentTableStyle.Catalog)
        {
            style.Name.Should().NotBeNullOrWhiteSpace($"{style.WordStyleId} must have a display name");
            style.WordStyleId.Should().NotBeNullOrWhiteSpace($"style '{style.Name}' must have a WordStyleId");
        }
    }

    [Fact]
    public void Catalog_WordStyleIds_AreDistinct()
    {
        DocumentTableStyle.Catalog
            .Select(s => s.WordStyleId)
            .Should().OnlyHaveUniqueItems("every catalog entry must have a unique WordStyleId");
    }

    [Fact]
    public void FindById_ReturnsMatchingEntry_CaseInsensitive()
    {
        var grid = DocumentTableStyle.FindById("TableGrid");
        grid.Should().NotBeNull();
        grid!.WordStyleId.Should().Be("TableGrid");

        DocumentTableStyle.FindById("tablegrid").Should().BeSameAs(grid);
        DocumentTableStyle.FindById("NoSuchStyle").Should().BeNull();
    }

    [Fact]
    public void FindByName_ReturnsMatchingEntry_CaseInsensitive()
    {
        var style = DocumentTableStyle.FindByName("Table Grid");
        style.Should().NotBeNull();
        style!.Name.Should().Be("Table Grid");

        DocumentTableStyle.FindByName("table grid").Should().BeSameAs(style);
        DocumentTableStyle.FindByName("No Such Style").Should().BeNull();
    }

    [Fact]
    public void TableGrid_HasBordersAndNoFills()
    {
        var style = DocumentTableStyle.FindById("TableGrid")!;

        style.Borders.Should().BeTrue();
        style.HeaderBand.Should().BeNull();
        style.BandedRowOdd.Should().BeNull();
        style.BandedRowEven.Should().BeNull();
    }

    [Fact]
    public void GridTable1Light_HasBlueBordersAndHeaderFill()
    {
        var style = DocumentTableStyle.FindById("GridTable1Light")!;

        style.Borders.Should().BeTrue();
        style.BorderColorHex.Should().Be("4472C4");
        style.HeaderBand.Should().NotBeNull();
        style.HeaderBand!.FillHex.Should().Be("4472C4");
        style.HeaderBand.Bold.Should().BeTrue();
        style.BandedRowOdd.Should().NotBeNull();
    }

    [Fact]
    public void Table_TableStyleId_DefaultsToNull()
    {
        var table = Table.Create(2, 2);
        table.TableStyleId.Should().BeNull();
    }

    [Fact]
    public void Table_TableStyleId_CanBeSet()
    {
        var table = Table.Create(2, 2);
        table.TableStyleId = "GridTable1Light";
        table.TableStyleId.Should().Be("GridTable1Light");
    }

    [Fact]
    public void ResolveCellStyle_HeaderRow_ReturnsHeaderBandFill()
    {
        var style = DocumentTableStyle.FindById("GridTable1Light")!;
        var fmt = TableFormatting.Default with { HeaderRow = true, BandedRows = true };

        var (fillHex, bold) = style.ResolveCellStyle(rowIndex: 0, totalRows: 4, isFirstCol: false, isLastCol: false, fmt);

        fillHex.Should().Be("4472C4");
        bold.Should().BeTrue();
    }

    [Fact]
    public void ResolveCellStyle_BandedBodyRows_AltFillsAlternate()
    {
        var style = DocumentTableStyle.FindById("GridTable2")!;
        var fmt = TableFormatting.Default with { HeaderRow = true, BandedRows = true };

        // First body row (rowIndex=1 with header): bodyIndex=0, odd band.
        var (fill1, _) = style.ResolveCellStyle(rowIndex: 1, totalRows: 5, isFirstCol: false, isLastCol: false, fmt);
        // Second body row (rowIndex=2): bodyIndex=1, even band.
        var (fill2, _) = style.ResolveCellStyle(rowIndex: 2, totalRows: 5, isFirstCol: false, isLastCol: false, fmt);

        fill1.Should().Be(style.BandedRowOdd?.FillHex);
        fill2.Should().Be(style.BandedRowEven?.FillHex);
        fill1.Should().NotBe(fill2, "odd and even banding fills should differ");
    }

    [Fact]
    public void TableBanding_BodyRowZero_IsFirstBandWithOrWithoutHeader()
    {
        TableBanding.IsBandedBodyRow(rowIndex: 0, hasHeaderRow: false).Should().BeTrue();
        TableBanding.IsBandedBodyRow(rowIndex: 1, hasHeaderRow: false).Should().BeFalse();
        TableBanding.IsBandedBodyRow(rowIndex: 0, hasHeaderRow: true).Should().BeFalse();
        TableBanding.IsBandedBodyRow(rowIndex: 1, hasHeaderRow: true).Should().BeTrue();
        TableBanding.IsBandedBodyRow(rowIndex: 2, hasHeaderRow: true).Should().BeFalse();
    }

    [Fact]
    public void ResolveCellStyle_PlainTableGrid_ReturnsNoFillNoBold()
    {
        var style = DocumentTableStyle.FindById("TableGrid")!;
        var fmt = TableFormatting.Default;

        var (fill, bold) = style.ResolveCellStyle(rowIndex: 0, totalRows: 3, isFirstCol: false, isLastCol: false, fmt);

        fill.Should().BeNull();
        bold.Should().BeFalse();
    }
}
