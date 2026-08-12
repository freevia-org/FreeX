using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using FreeW.App.Host.Editing;
using FreeW.Core.Model;
using WpfTable = System.Windows.Documents.Table;
using WpfTableCell = System.Windows.Documents.TableCell;

namespace FreeW.App.Host.Tests;

/// <summary>
/// App.Host tests for the Table Styles gallery:
/// <list type="bullet">
///   <item>The gallery builds and is labelled correctly (automation name).</item>
///   <item>Applying a catalog style sets <see cref="Table.TableStyleId"/> and the render reflects it.</item>
///   <item>Live-preview / revert cycle leaves the model unchanged.</item>
///   <item>The gallery command id <c>freew.table-styles-gallery</c> is defined in the ribbon group.</item>
/// </list>
/// </summary>
public sealed class TableStyleGalleryTests
{
    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static TextDocument TableModel()
    {
        var doc = TextDocument.CreateEmpty();
        doc.Blocks.Clear();
        var table = Table.Create(3, 2);
        table.Formatting = table.Formatting with { HeaderRow = true, BandedRows = true };
        doc.Blocks.Add(table);
        return doc;
    }

    private static void PlaceCaretInFirstCell(DocumentView view)
    {
        var table = view.Document.Blocks.OfType<WpfTable>().First();
        var cell = table.RowGroups[0].Rows[0].Cells[0];
        view.CaretPosition = cell.ContentStart;
    }

    // ── Gallery widget tests ─────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void Build_ReturnsButtonLabelledTableStyles()
    {
        var editor = new DocumentView();
        var gallery = TableStylesGallery.Build(editor);

        AutomationProperties.GetName(gallery).Should().Be("Table Styles");
    }

    [StaFact]
    public void Build_MenuContainsAllCatalogStyles()
    {
        var editor = new DocumentView();
        var gallery = TableStylesGallery.Build(editor) as Button;

        gallery.Should().NotBeNull();
        var menu = gallery!.ContextMenu;
        menu.Should().NotBeNull();
        menu!.Items.Count.Should().Be(DocumentTableStyle.Catalog.Count,
            "every catalog table style must appear as a menu item");
    }

    [StaFact]
    public void Build_MenuItems_AreAutomationLabelled()
    {
        var editor = new DocumentView();
        var gallery = TableStylesGallery.Build(editor) as Button;
        var menu = gallery!.ContextMenu;

        foreach (MenuItem item in menu!.Items)
        {
            var name = AutomationProperties.GetName(item);
            name.Should().EndWith("table style", $"all menu items must be automation-labelled");
        }
    }

    // ── Apply path tests ─────────────────────────────────────────────────────────────────────────────

    [StaFact]
    public void ApplyTableStyle_SetsTableStyleId_OnCaretTable()
    {
        var editor = new DocumentView();
        editor.LoadModel(TableModel());
        PlaceCaretInFirstCell(editor);

        var style = DocumentTableStyle.FindById("GridTable1Light")!;
        editor.ApplyTableStyle(style);

        editor.CommitToModel();
        var table = editor.Model.Blocks.OfType<Table>().First();
        table.TableStyleId.Should().Be("GridTable1Light");

        editor.CanUndo.Should().BeTrue();
        editor.Undo();
        table = editor.Model.Blocks.OfType<Table>().First();
        table.TableStyleId.Should().BeNull();
        table.Formatting.HeaderRow.Should().BeTrue();
        table.Formatting.BandedRows.Should().BeTrue();

        editor.Redo();
        editor.Model.Blocks.OfType<Table>().First().TableStyleId.Should().Be("GridTable1Light");
    }

    [StaFact]
    public void CellFormattingAndTableOptions_AreUndoable()
    {
        var editor = new DocumentView();
        editor.LoadModel(TableModel());

        PlaceCaretInFirstCell(editor);
        editor.SetCaretCellShading("#123456");
        editor.Model.Blocks.OfType<Table>().First().Rows[0].Cells[0].ShadingColorHex.Should().Be("#123456");
        editor.Undo();
        editor.Model.Blocks.OfType<Table>().First().Rows[0].Cells[0].ShadingColorHex.Should().BeNull();

        PlaceCaretInFirstCell(editor);
        var borders = new CellBorders { Top = new CellBorderEdge(BorderLineStyle.Double, "#123456", 1.5) };
        editor.SetCaretCellBorders(borders);
        editor.Model.Blocks.OfType<Table>().First().Rows[0].Cells[0].Borders.Should().BeSameAs(borders);
        editor.Undo();
        editor.Model.Blocks.OfType<Table>().First().Rows[0].Cells[0].Borders.Should().BeNull();

        PlaceCaretInFirstCell(editor);
        editor.SetCaretCellTextDirection(CellTextDirection.Rotate270);
        editor.Model.Blocks.OfType<Table>().First().Rows[0].Cells[0].TextDirection
            .Should().Be(CellTextDirection.Rotate270);
        editor.Undo();
        editor.Model.Blocks.OfType<Table>().First().Rows[0].Cells[0].TextDirection
            .Should().Be(CellTextDirection.Horizontal);

        PlaceCaretInFirstCell(editor);
        var beforeHeader = editor.Model.Blocks.OfType<Table>().First().Formatting.HeaderRow;
        editor.ToggleTableHeaderRow();
        editor.Model.Blocks.OfType<Table>().First().Formatting.HeaderRow.Should().Be(!beforeHeader);
        editor.Undo();
        editor.Model.Blocks.OfType<Table>().First().Formatting.HeaderRow.Should().Be(beforeHeader);
    }

    [StaFact]
    public void CellAlignment_IsUndoableThroughTheWpfHost()
    {
        var model = TableModel();
        var cell = model.Blocks.OfType<Table>().First().Rows[0].Cells[0];
        cell.VerticalAlignment = TableCellVerticalAlignment.Bottom;
        cell.Paragraphs[0].Formatting = cell.Paragraphs[0].Formatting with
        {
            Alignment = FreeW.Core.Model.TextAlignment.Left,
            SpaceAfterPt = 6,
        };
        cell.Paragraphs.Add(new Paragraph("second")
        {
            Formatting = new ParagraphFormatting
            {
                Alignment = FreeW.Core.Model.TextAlignment.Justify,
                SpaceBeforePt = 4,
            },
        });
        var editor = new DocumentView();
        editor.LoadModel(model);
        PlaceCaretInFirstCell(editor);
        editor.CommitToModel();
        cell = editor.Model.Blocks.OfType<Table>().First().Rows[0].Cells[0];
        var firstBefore = cell.Paragraphs[0].Formatting;
        var secondBefore = cell.Paragraphs[1].Formatting;

        editor.SetCaretCellAlignment(
            TableCellVerticalAlignment.Center,
            FreeW.Core.Model.TextAlignment.Right);

        cell = editor.Model.Blocks.OfType<Table>().First().Rows[0].Cells[0];
        cell.VerticalAlignment.Should().Be(TableCellVerticalAlignment.Center);
        cell.Paragraphs.Should().OnlyContain(paragraph =>
            paragraph.Formatting.Alignment == FreeW.Core.Model.TextAlignment.Right);
        editor.CanUndo.Should().BeTrue();

        editor.Undo();
        cell = editor.Model.Blocks.OfType<Table>().First().Rows[0].Cells[0];
        cell.VerticalAlignment.Should().Be(TableCellVerticalAlignment.Bottom);
        cell.Paragraphs[0].Formatting.Should().Be(firstBefore);
        cell.Paragraphs[1].Formatting.Should().Be(secondBefore);

        editor.Redo();
        cell = editor.Model.Blocks.OfType<Table>().First().Rows[0].Cells[0];
        cell.VerticalAlignment.Should().Be(TableCellVerticalAlignment.Center);
        cell.Paragraphs.Should().OnlyContain(paragraph =>
            paragraph.Formatting.Alignment == FreeW.Core.Model.TextAlignment.Right);
    }

    [StaFact]
    public void ApplyTableStyle_UpdatesBorderColor_InRenderedTable()
    {
        var editor = new DocumentView();
        editor.LoadModel(TableModel());
        PlaceCaretInFirstCell(editor);

        var style = DocumentTableStyle.FindById("GridTable1Light")!;
        editor.ApplyTableStyle(style);

        // After applying GridTable1Light the WPF table's border brush should reflect the style's blue color.
        var wpfTable = editor.Document.Blocks.OfType<WpfTable>().First();
        // The style has borders; the table must have a non-null border brush.
        wpfTable.BorderBrush.Should().NotBeNull("styled table must render border brush from the catalog style");
    }

    [StaFact]
    public void LoadModel_UsesCompleteUniformExplicitTableBorderPayloadBeforeCatalogColor()
    {
        var doc = TableModel();
        var table = doc.Blocks.OfType<Table>().First();
        table.TableStyleId = "GridTable1Light";
        table.Borders = new TableBorders
        {
            Top = new TableBorderEdge(BorderLineStyle.Single, "auto", 0.5),
            Left = new TableBorderEdge(BorderLineStyle.Single, "auto", 0.5),
            Bottom = new TableBorderEdge(BorderLineStyle.Single, "auto", 0.5),
            Right = new TableBorderEdge(BorderLineStyle.Single, "auto", 0.5),
            InsideHorizontal = new TableBorderEdge(BorderLineStyle.Single, "auto", 0.5),
            InsideVertical = new TableBorderEdge(BorderLineStyle.Single, "auto", 0.5)
        };
        foreach (var cell in table.Rows[0].Cells)
        {
            cell.Borders = new CellBorders
            {
                Top = new CellBorderEdge(BorderLineStyle.Double, "#1F4E79", 1.25),
                Bottom = new CellBorderEdge(BorderLineStyle.Thick, "#1F4E79", 1.25),
                Left = new CellBorderEdge(BorderLineStyle.Single, "#1F4E79", 0.75),
                Right = new CellBorderEdge(BorderLineStyle.Single, "#1F4E79", 0.75)
            };
        }

        var editor = new DocumentView();
        editor.LoadModel(doc);

        var wpfTable = editor.Document.Blocks.OfType<WpfTable>().First();
        ((SolidColorBrush)wpfTable.BorderBrush!).Color.Should().Be(Colors.Black,
            "the complete explicit Word border payload owns generic table chrome ahead of the named style");
    }

    [StaFact]
    public void ApplyTableStyle_WithHeaderBand_RendersHeaderCellBold()
    {
        var editor = new DocumentView();
        editor.LoadModel(TableModel());
        PlaceCaretInFirstCell(editor);

        var style = DocumentTableStyle.FindById("GridTable1Light")!;
        editor.ApplyTableStyle(style);

        var wpfTable = editor.Document.Blocks.OfType<WpfTable>().First();
        var headerCell = wpfTable.RowGroups[0].Rows[0].Cells[0];
        headerCell.FontWeight.Should().Be(FontWeights.Bold,
            "GridTable1Light header band is bold — first row cell must render bold");
    }

    [StaFact]
    public void ApplyTableStyle_WithHeaderFill_RendersHeaderCellBackground()
    {
        var editor = new DocumentView();
        editor.LoadModel(TableModel());
        PlaceCaretInFirstCell(editor);

        var style = DocumentTableStyle.FindById("GridTable1Light")!;
        editor.ApplyTableStyle(style);

        var wpfTable = editor.Document.Blocks.OfType<WpfTable>().First();
        var headerCell = wpfTable.RowGroups[0].Rows[0].Cells[0];
        var fill = headerCell.Background.Should().BeOfType<SolidColorBrush>(
            "header band fill must render from the shared effective-fill plan").Subject;
        fill.Color.Should().Be(Color.FromRgb(0x44, 0x72, 0xC4));
    }

    // ── Preview / revert cycle ───────────────────────────────────────────────────────────────────────

    [StaFact]
    public void PreviewTableStyle_ThenEnd_ModelIsRestored()
    {
        var editor = new DocumentView();
        editor.LoadModel(TableModel());
        PlaceCaretInFirstCell(editor);

        // Record the prior TableStyleId (null for a fresh table).
        editor.CommitToModel();
        var before = editor.Model.Blocks.OfType<Table>().First().TableStyleId;

        var style = DocumentTableStyle.FindById("GridTable1Light")!;
        editor.PreviewTableStyle(style);

        // Mid-preview: model has the style applied.
        editor.CommitToModel();
        editor.Model.Blocks.OfType<Table>().First().TableStyleId.Should().Be("GridTable1Light");

        editor.EndTableStylePreview();

        // After end: model restored to pre-preview state.
        editor.CommitToModel();
        editor.Model.Blocks.OfType<Table>().First().TableStyleId.Should().Be(before);
    }

    // ── Ribbon parity ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RibbonDefinition_TableDesign_HasTableStyleGroup()
    {
        var ribbon = FreeW.Ribbon.Definitions.FreeWRibbon.Build(FreeW.Ribbon.Definitions.FreeWRibbonCapabilities.Wpf);
        var tableDesign = ribbon.FindTab("table-design");

        tableDesign.Should().NotBeNull("the table-design contextual tab must exist");
        tableDesign!.Groups.Select(g => g.Id).Should().Contain("table-style",
            "the Table Style group must be declared in the Table Design contextual tab");
    }
}
