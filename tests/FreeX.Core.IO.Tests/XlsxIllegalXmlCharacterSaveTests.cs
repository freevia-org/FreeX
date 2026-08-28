using FluentAssertions;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Model text can legitimately hold characters XML 1.0 cannot represent: C0 control codes and lone
/// UTF-16 surrogates arrive by pasting from another application or by importing a file, and nothing in
/// the editor rejects them. Every writer that drops such text straight into an
/// <see cref="System.Xml.Linq.XElement"/> (or an <c>XmlWriter</c>) makes serialization throw
/// <see cref="ArgumentException"/>, which aborts the WHOLE workbook save with no file written -- the
/// user loses the save, not the character. Dropping the character, as Excel does with the same input,
/// is the only outcome that keeps the document.
/// <para>
/// Every case here goes through the real file-adapter Save gesture and reloads the result, so it fails
/// on the crash rather than on a substring assertion against XML that was never actually written.
/// </para>
/// </summary>
public sealed class XlsxIllegalXmlCharacterSaveTests
{
    private const string Control = "\u0001";
    private const string LoneHighSurrogate = "\ud83d";

    // -- The chart part: title, axis titles, name and alt text are all rebuilt from ChartModel --

    [Fact]
    public void SaveAs_WithControlCharacterInChartTitle_SucceedsAndReloads()
    {
        var workbook = NewWorkbook(out var sheet);
        AddChart(sheet, c => c.Title = "Quarterly" + Control + " Revenue");

        var reloaded = SaveAndReload(workbook).GetSheetAt(0);

        reloaded.Charts.Should().ContainSingle();
        reloaded.Charts[0].Title.Should().Be("Quarterly Revenue");
    }

    [Fact]
    public void SaveAs_WithLoneSurrogateInChartTitle_SucceedsAndReloads()
    {
        var workbook = NewWorkbook(out var sheet);
        AddChart(sheet, c => c.Title = "Sales" + LoneHighSurrogate + " 2026");

        var reloaded = SaveAndReload(workbook).GetSheetAt(0);

        reloaded.Charts[0].Title.Should().Be("Sales 2026");
    }

    // A valid surrogate PAIR is a real character (an emoji), not a defect: it must survive untouched.
    [Fact]
    public void SaveAs_WithEmojiInChartTitle_PreservesTheEmoji()
    {
        var workbook = NewWorkbook(out var sheet);
        AddChart(sheet, c => c.Title = "Revenue \U0001F4C8");

        var reloaded = SaveAndReload(workbook).GetSheetAt(0);

        reloaded.Charts[0].Title.Should().Be("Revenue \U0001F4C8");
    }

    [Fact]
    public void SaveAs_WithControlCharacterInChartAxisTitles_SucceedsAndReloads()
    {
        var workbook = NewWorkbook(out var sheet);
        AddChart(sheet, c =>
        {
            c.XAxisTitle = "Month" + Control;
            c.YAxisTitle = "Amount" + Control;
        });

        var chart = SaveAndReload(workbook).GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        chart.XAxisTitle.Should().Be("Month");
        chart.YAxisTitle.Should().Be("Amount");
    }

    // Chart name and alt text land in the DRAWING part, not the chart part -- a separate writer.
    [Fact]
    public void SaveAs_WithControlCharacterInChartNameAndAltText_SucceedsAndReloads()
    {
        var workbook = NewWorkbook(out var sheet);
        AddChart(sheet, c =>
        {
            c.Name = "Chart" + Control + " 1";
            c.AltTextTitle = "Alt" + Control;
            c.AltTextDescription = "Description" + Control;
        });

        var chart = SaveAndReload(workbook).GetSheetAt(0).Charts.Should().ContainSingle().Subject;

        chart.Name.Should().Be("Chart 1");
        chart.AltTextTitle.Should().Be("Alt");
        chart.AltTextDescription.Should().Be("Description");
    }

    // -- The other drawing objects sharing that part --

    [Fact]
    public void SaveAs_WithControlCharacterInTextBoxText_SucceedsAndReloads()
    {
        var workbook = NewWorkbook(out var sheet);
        sheet.TextBoxes.Add(new TextBoxModel
        {
            Name = "TextBox 1",
            Anchor = new CellAddress(sheet.Id, 8, 2),
            Text = "Note" + Control + " text",
        });

        var reloaded = SaveAndReload(workbook).GetSheetAt(0);

        reloaded.TextBoxes.Should().ContainSingle().Which.Text.Should().Be("Note text");
    }

    [Fact]
    public void SaveAs_WithControlCharacterInShapeText_SucceedsAndReloads()
    {
        var workbook = NewWorkbook(out var sheet);
        sheet.DrawingShapes.Add(new DrawingShapeModel
        {
            Name = "Rectangle 1",
            Anchor = new CellAddress(sheet.Id, 8, 2),
            Kind = DrawingShapeKind.Rectangle,
            Width = 200,
            Height = 100,
            ShapeText = "Label" + Control,
        });

        var reloaded = SaveAndReload(workbook).GetSheetAt(0);

        reloaded.DrawingShapes.Should().ContainSingle().Which.ShapeText.Should().Be("Label");
    }

    // -- Surfaces ClosedXML writes, which validate inside its own streaming writer --

    [Fact]
    public void SaveAs_WithControlCharacterInCommentAndAuthor_SucceedsAndReloads()
    {
        var workbook = NewWorkbook(out var sheet);
        var address = new CellAddress(sheet.Id, 1, 1);
        sheet.Comments[address] = "Check" + Control + " this";
        sheet.CommentAuthors[address] = "Ada" + Control;

        var reloaded = SaveAndReload(workbook).GetSheetAt(0);

        // Keyed by the RELOADED sheet's id -- CellAddress carries the SheetId, which is minted fresh on load.
        reloaded.Comments.Should()
            .ContainKey(new CellAddress(reloaded.Id, 1, 1))
            .WhoseValue.Should().Be("Check this");
    }

    [Fact]
    public void SaveAs_WithControlCharacterInDataValidationMessages_SucceedsAndReloads()
    {
        var workbook = NewWorkbook(out var sheet);
        sheet.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 2, 1)),
            Type = DvType.WholeNumber,
            Formula1 = "1",
            Formula2 = "10",
            ErrorTitle = "Bad" + Control,
            ErrorMessage = "Out of range" + Control,
            PromptTitle = "Hint" + Control,
            PromptMessage = "Enter a number" + Control,
        });

        var reloaded = SaveAndReload(workbook).GetSheetAt(0);

        var validation = reloaded.DataValidations.Should().ContainSingle().Subject;
        validation.ErrorTitle.Should().Be("Bad");
        validation.ErrorMessage.Should().Be("Out of range");
        validation.PromptTitle.Should().Be("Hint");
        validation.PromptMessage.Should().Be("Enter a number");
    }

    [Fact]
    public void SaveAs_WithControlCharacterInHeaderAndFooter_SucceedsAndReloads()
    {
        var workbook = NewWorkbook(out var sheet);
        sheet.PageHeader = new WorksheetHeaderFooter("Left" + Control, "Center" + Control, "Right" + Control);
        sheet.PageFooter = new WorksheetHeaderFooter("Foot" + Control, "", "");

        var reloaded = SaveAndReload(workbook).GetSheetAt(0);

        reloaded.PageHeader.Left.Should().Be("Left");
        reloaded.PageHeader.Center.Should().Be("Center");
        reloaded.PageHeader.Right.Should().Be("Right");
        reloaded.PageFooter.Left.Should().Be("Foot");
    }

    // -- The theme part: written straight to its own zip entry, bypassing OpcXml like the chart part --

    /// <summary>
    /// The theme name reaches three attributes of the theme part (a:theme/@name and the generated
    /// a:clrScheme/@name and a:fontScheme/@name), and <c>WorkbookTheme.WithName</c> takes arbitrary
    /// text -- a .fxl import carries it as JSON, which can legally hold C0 control codes and lone surrogates.
    /// <c>XlsxWorkbookThemeWriter</c> creates its package entry directly, so it misses OpcXml's
    /// sanitize and one such character aborted the ENTIRE workbook save.
    /// </summary>
    [Fact]
    public void SaveAs_WithControlCharacterInThemeName_SucceedsAndReloads()
    {
        var workbook = NewWorkbook(out _);
        workbook.Theme = workbook.Theme.WithName("Corporate" + Control + " Blue");

        var reloaded = SaveAndReload(workbook);

        reloaded.Theme.Name.Should().Be("Corporate Blue");
    }

    [Fact]
    public void SaveAs_WithLoneSurrogateInThemeFontName_SucceedsAndReloads()
    {
        var workbook = NewWorkbook(out _);
        workbook.Theme = workbook.Theme.WithFonts("Calibri" + LoneHighSurrogate, "Calibri");

        SaveAndReload(workbook).Should().NotBeNull();
    }

    // -- The other spreadsheet formats that hand-roll their own XML --

    [Fact]
    public void SaveAs_SpreadsheetMlXml_WithControlCharacterInCellText_Succeeds()
    {
        var workbook = NewWorkbook(out var sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("Total" + Control));

        SaveTo(new SpreadsheetXmlFileAdapter(), workbook).Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SaveAs_Ods_WithControlCharacterInCellText_Succeeds()
    {
        var workbook = NewWorkbook(out var sheet);
        sheet.SetCell(new CellAddress(sheet.Id, 6, 1), new TextValue("Total" + Control));

        SaveTo(new OdsFileAdapter(), workbook).Length.Should().BeGreaterThan(0);
    }

    private static Workbook NewWorkbook(out Sheet sheet)
    {
        var workbook = new Workbook("Book");
        sheet = workbook.AddSheet("Sheet1");
        for (uint row = 1; row <= 4; row++)
        {
            sheet.SetCell(new CellAddress(sheet.Id, row, 1), new TextValue("r" + row));
            sheet.SetCell(new CellAddress(sheet.Id, row, 2), new NumberValue(row));
        }

        return workbook;
    }

    private static void AddChart(Sheet sheet, Action<ChartModel> configure)
    {
        var chart = new ChartModel
        {
            Name = "Chart 1",
            Type = ChartType.Column,
            DataRange = new GridRange(new CellAddress(sheet.Id, 1, 1), new CellAddress(sheet.Id, 4, 2)),
        };
        configure(chart);
        sheet.Charts.Add(chart);
    }

    private static Workbook SaveAndReload(Workbook workbook)
    {
        var adapter = new XlsxFileAdapter();
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        return adapter.Load(saved);
    }

    private static byte[] SaveTo(IFileAdapter adapter, Workbook workbook)
    {
        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        return saved.ToArray();
    }
}
