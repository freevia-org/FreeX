using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit.Sdk;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxSemanticComparisonTests
{
    [Fact]
    public void Compare_CatchesUsedCellValueDifferenceAndReportsUsefulPath()
    {
        var expected = CreateSingleValueWorkbook(42);
        var actual = CreateSingleValueWorkbook(43);

        var result = XlsxSemanticWorkbookComparer.Compare(expected, actual);
        var exception = Assert.Throws<XunitException>(() =>
            XlsxSemanticWorkbookComparer.AssertEquivalent(expected, actual));

        result.AreEquivalent.Should().BeFalse();
        result.Differences.Should().Contain(difference =>
            difference.Contains("Sheets[0 'Data'].Cells[B2].Value") &&
            difference.Contains("Number:42") &&
            difference.Contains("Number:43"));
        exception.Message.Should().Contain("Workbook semantic comparison failed");
        exception.Message.Should().Contain("Sheets[0 'Data'].Cells[B2].Value");
    }

    [Fact]
    public void Compare_CatchesExternalWorkbookLinkDifferenceAndReportsUsefulPath()
    {
        var expected = CreateSingleValueWorkbook(42);
        expected.ExternalLinks.Add(new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "file:///C:/FreeX/Expected.xlsx",
            TargetMode = "External"
        });
        var actual = CreateSingleValueWorkbook(42);
        actual.ExternalLinks.Add(new ExternalLinkModel
        {
            PackagePart = "xl/externalLinks/externalLink1.xml",
            TargetUri = "file:///C:/FreeX/Actual.xlsx",
            TargetMode = "External"
        });

        var result = XlsxSemanticWorkbookComparer.Compare(expected, actual);

        result.AreEquivalent.Should().BeFalse();
        result.Differences.Should().Contain(difference =>
            difference.Contains("Workbook.ExternalLinks[0]") &&
            difference.Contains("Expected.xlsx") &&
            difference.Contains("Actual.xlsx"));
    }

    [Fact]
    public void AssertEquivalent_FreeXCreatedXlsxSaveLoadSaveLoad_PassesForRepresentativeWorkbook()
    {
        var original = CreateRepresentativeFreeXWorkbook();
        var adapter = new XlsxFileAdapter();

        using var firstSave = new MemoryStream();
        adapter.Save(original, firstSave);
        firstSave.Position = 0;

        var loadedOnce = adapter.Load(firstSave);
        using var secondSave = new MemoryStream();
        adapter.Save(loadedOnce, secondSave);
        secondSave.Position = 0;

        var loadedTwice = adapter.Load(secondSave);

        XlsxSemanticWorkbookComparer.AssertEquivalent(original, loadedTwice);
    }

    [Fact]
    public void AssertEquivalent_GeneratedExternalLinkPackageLoadEditSaveLoad_PassesAgainstMutatedModel()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-external-links-001");
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        loaded.ExternalLinks.Should().ContainSingle(link =>
            link.PackagePart == "xl/externalLinks/externalLink1.xml" &&
            link.TargetUri == "file:///C:/FreeX/ExternalWorkbook.xlsx" &&
            link.TargetMode == "External");

        var sheet = loaded.GetSheetAt(0);
        sheet.SetCell(new CellAddress(sheet.Id, 4, 1), new TextValue("FreeX external-link semantic edit"));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        var reloaded = adapter.Load(saved);

        XlsxSemanticWorkbookComparer.AssertEquivalent(loaded, reloaded);
    }

    [Fact]
    public void AssertEquivalent_PackageAuthoredXlsxLoadEditSaveLoad_PassesAgainstMutatedModel()
    {
        using var package = CreatePackageAuthoredWorkbook();
        var adapter = new XlsxFileAdapter();
        var loaded = adapter.Load(package);
        var input = loaded.GetSheet("Input")!;
        var editedAddress = new CellAddress(input.Id, 4, 1);
        input.SetCell(editedAddress, new TextValue("FreeX model edit"));

        using var saved = new MemoryStream();
        adapter.Save(loaded, saved);
        saved.Position = 0;

        var reloaded = adapter.Load(saved);

        XlsxSemanticWorkbookComparer.AssertEquivalent(loaded, reloaded);
    }

    private static Workbook CreateSingleValueWorkbook(double value)
    {
        var workbook = new Workbook("ComparisonProbe");
        var sheet = workbook.AddSheet("Data");
        var styleId = workbook.RegisterStyle(new CellStyle
        {
            NumberFormat = "0.00",
            FillColor = new CellColor(221, 235, 247)
        });
        sheet.SetCell(new CellAddress(sheet.Id, 2, 2), new Cell
        {
            Value = new NumberValue(value),
            StyleId = styleId
        });
        return workbook;
    }

    private static Workbook CreateRepresentativeFreeXWorkbook()
    {
        var workbook = new Workbook("SemanticRoundTrip")
        {
            CalculationMode = WorkbookCalculationMode.Manual,
            FullCalculationOnLoad = true,
            ForceFullCalculation = true
        };

        var data = workbook.AddSheet("Data");
        var hidden = workbook.AddSheet("Hidden");
        hidden.IsHidden = true;
        hidden.TabColor = new CellColor(255, 192, 0);
        hidden.SetCell(new CellAddress(hidden.Id, 1, 1), new TextValue("hidden payload"));

        var headerStyle = workbook.RegisterStyle(new CellStyle
        {
            Bold = true,
            FontColor = CellColor.White,
            FillColor = new CellColor(31, 78, 121),
            HorizontalAlignment = HorizontalAlignment.Center,
            BorderBottom = new CellBorder(BorderStyle.Thin, CellColor.Black)
        });
        var moneyStyle = workbook.RegisterStyle(new CellStyle
        {
            NumberFormat = "$#,##0.00",
            FillColor = new CellColor(226, 239, 218),
            BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(112, 173, 71)),
            HorizontalAlignment = HorizontalAlignment.Right
        });
        var hyperlinkStyle = workbook.RegisterStyle(new CellStyle
        {
            Underline = true,
            FontColor = new CellColor(5, 99, 193)
        });

        data.SetCell(new CellAddress(data.Id, 1, 1), new Cell { Value = new TextValue("Revenue"), StyleId = headerStyle });
        data.SetCell(new CellAddress(data.Id, 1, 2), new Cell { Value = new TextValue("Amount"), StyleId = headerStyle });
        data.SetCell(new CellAddress(data.Id, 2, 1), new TextValue("North"));
        data.SetCell(new CellAddress(data.Id, 2, 2), new Cell { Value = new NumberValue(1250.5), StyleId = moneyStyle });
        data.SetCell(new CellAddress(data.Id, 3, 1), new TextValue("South"));
        data.SetCell(new CellAddress(data.Id, 3, 2), new Cell { Value = new NumberValue(980.25), StyleId = moneyStyle });
        data.SetCell(new CellAddress(data.Id, 4, 1), new TextValue("Total"));
        data.SetCell(new CellAddress(data.Id, 4, 2), new Cell { FormulaText = "SUM(B2:B3)", StyleId = moneyStyle });
        data.SetCell(new CellAddress(data.Id, 2, 3), new TextValue("Open"));
        data.SetCell(new CellAddress(data.Id, 3, 3), new Cell { Value = new TextValue("Closed"), StyleId = hyperlinkStyle });
        data.SetStyleOnly(6, 2, moneyStyle);
        data.SetCell(new CellAddress(data.Id, 7, 1), new TextValue("Merged note"));
        data.AddMergedRegion(new GridRange(new CellAddress(data.Id, 7, 1), new CellAddress(data.Id, 7, 3)));

        var commentAddress = new CellAddress(data.Id, 1, 1);
        data.Comments[commentAddress] = "Executive summary";
        var linkAddress = new CellAddress(data.Id, 3, 3);
        data.Hyperlinks[linkAddress] = "https://example.com/status";
        data.HyperlinkMetadata[linkAddress] = new HyperlinkMetadata(
            HyperlinkTargetKind.ExistingFileOrWebPage,
            "Open status details",
            "");

        data.RowHeights[1] = 28;
        data.ColumnWidths[1] = 18;
        data.ColumnWidths[2] = 15;
        data.HiddenRows.Add(8);
        data.HiddenCols.Add(5);
        data.RowOutlineLevels[2] = 1;
        data.ColOutlineLevels[2] = 1;
        data.FrozenRows = 1;
        data.FrozenCols = 1;
        data.PrintArea = new GridRange(new CellAddress(data.Id, 1, 1), new CellAddress(data.Id, 5, 3));
        data.PageOrientation = WorksheetPageOrientation.Landscape;
        data.PaperSize = WorksheetPaperSize.Letter;
        data.PageMargins = new WorksheetPageMargins(0.7, 0.7, 0.75, 0.75);
        data.PrintGridlines = true;
        data.PrintHeadings = true;
        data.ScaleToFit = new WorksheetScaleToFit(null, 1, 1);
        data.FitToPage = true;
        data.PrintTitleRows = new WorksheetRepeatRange(1, 1);
        data.PageHeader = new WorksheetHeaderFooter("Left header", "Center header", "Right header");
        data.PageFooter = new WorksheetHeaderFooter("Left footer", "Center footer", "Right footer");
        data.CenterHorizontallyOnPage = true;
        data.PageOrder = WorksheetPageOrder.OverThenDown;
        data.FirstPageNumber = 3;
        data.PrintBlackAndWhite = true;
        data.PrintQualityDpi = 300;
        data.PrintQualityVerticalDpi = 300;
        data.PrintComments = WorksheetPrintComments.AtEnd;
        data.RowPageBreaks.Add(5);
        data.ColumnPageBreaks.Add(3);

        data.DataValidations.Add(new DataValidation
        {
            AppliesTo = new GridRange(new CellAddress(data.Id, 2, 3), new CellAddress(data.Id, 5, 3)),
            Type = DvType.List,
            Formula1 = "Open,Closed",
            PromptTitle = "Status",
            PromptMessage = "Choose a status.",
            ErrorTitle = "Invalid status",
            ErrorMessage = "Use Open or Closed."
        });
        data.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = new GridRange(new CellAddress(data.Id, 2, 2), new CellAddress(data.Id, 5, 2)),
            Priority = 1,
            RuleType = CfRuleType.CellValue,
            Operator = CfOperator.GreaterThan,
            Value1 = "1000",
            FormatIfTrue = new CellStyle
            {
                FillColor = new CellColor(198, 239, 206),
                FontColor = new CellColor(0, 97, 0)
            }
        });

        workbook.DefineNamedRange(
            "SalesData",
            new GridRange(new CellAddress(data.Id, 1, 1), new CellAddress(data.Id, 5, 3)));

        return workbook;
    }

    private static MemoryStream CreatePackageAuthoredWorkbook()
    {
        var stream = new MemoryStream();
        using (var workbook = new XLWorkbook())
        {
            var input = workbook.Worksheets.Add("Input");
            input.Cell("A1").Value = "Package-authored";
            input.Cell("B2").Value = 12;
            input.Cell("C2").FormulaA1 = "B2*2";
            input.Cell("A1").Style.Font.Bold = true;
            input.Cell("A1").Style.Fill.BackgroundColor = XLColor.FromArgb(31, 78, 121);
            input.Cell("A1").Style.Font.FontColor = XLColor.White;
            input.Row(2).Height = 21;
            input.Column(2).Width = 16;
            input.Range("A1:C1").Merge();
            input.Cell("A1").CreateComment().AddText("Created before FreeX load");
            input.Cell("C3").Value = "docs";
            input.Cell("C3").SetHyperlink(new XLHyperlink("https://example.com/package")
            {
                Tooltip = "Package link"
            });
            workbook.DefinedNames.Add("PackageInput", "'Input'!A1:C3");

            var hidden = workbook.Worksheets.Add("Hidden");
            hidden.Cell("A1").Value = "hidden";
            hidden.Visibility = XLWorksheetVisibility.Hidden;

            workbook.SaveAs(stream);
        }

        stream.Position = 0;
        return stream;
    }
}
