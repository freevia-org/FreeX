using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Reflection;
using System.Xml.Linq;
using ClosedXML.Excel;
using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class XlsxFileAdapterPerformanceTests
{
    private const int DenseSheetCount = 8;
    private const int DenseRowsPerSheet = 80;
    private const int DenseColumnsPerSheet = 24;
    private const int StyleOnlySaveSheetCount = 2;
    private const int StyleOnlySaveRowsPerSheet = 600;
    private const int StyleOnlySaveColumnsPerSheet = 72;
    private const int StyleOnlySaveRunWidth = 8;
    private const int WorksheetNativeMetadataSheetCount = 8;
    private const int WorksheetNativeMetadataRowsPerSheet = 40;
    private const int WorksheetReplayMetadataSheetCount = 8;
    private const int WorksheetReplayMetadataRowsPerSheet = 40;
    private const int AdvancedConditionalFormatRulesPerSheet = 40;
    private const int WorksheetSingleXmlCellsPerSheet = 40;
    private const int IgnoredErrorStyleOnlyRows = 800;
    private const int IgnoredErrorStyleOnlyValueColumns = 30;
    private const int IgnoredErrorStyleOnlyStyleColumns = 10;
    private const int IgnoredErrorStyleOnlyIgnoredRanges = 800;
    private const int IgnoredErrorSaveRows = 300;
    private const int IgnoredErrorSaveColumns = 40;

    private static byte[] CreateDenseXlsxPackage()
    {
        using var workbook = new XLWorkbook();
        for (var sheetIndex = 1; sheetIndex <= DenseSheetCount; sheetIndex++)
        {
            var sheet = workbook.Worksheets.Add($"Sheet {sheetIndex}");
            for (var row = 1; row <= DenseRowsPerSheet; row++)
            {
                for (var col = 1; col <= DenseColumnsPerSheet; col++)
                {
                    var cell = sheet.Cell(row, col);
                    cell.Value = row * col + sheetIndex;
                    if ((row + col) % 17 == 0)
                    {
                        cell.Style.Font.Bold = true;
                        cell.Style.Fill.BackgroundColor = XLColor.FromArgb(255, 242, 204);
                    }
                }
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static byte[] CreateIgnoredErrorAndStyleOnlyMetadataPackage()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Metadata");
        for (var row = 1; row <= IgnoredErrorStyleOnlyRows; row++)
        {
            for (var col = 1; col <= IgnoredErrorStyleOnlyValueColumns; col++)
                sheet.Cell(row, col).Value = row * col;

            for (var col = IgnoredErrorStyleOnlyValueColumns + 2;
                 col < IgnoredErrorStyleOnlyValueColumns + 2 + IgnoredErrorStyleOnlyStyleColumns;
                 col++)
            {
                var styleOnlyCell = sheet.Cell(row, col);
                styleOnlyCell.Style.Fill.BackgroundColor = XLColor.FromArgb(221, 235, 247);
                styleOnlyCell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            }
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Update, leaveOpen: true);
            var worksheetEntry = archive.GetEntry("xl/worksheets/sheet1.xml")!;
            XDocument worksheetXml;
            using (var worksheetStream = worksheetEntry.Open())
                worksheetXml = XDocument.Load(worksheetStream);
            XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
            worksheetXml.Root!.Element(ns + "ignoredErrors")?.Remove();

            var ignoredErrors = new XElement(ns + "ignoredErrors");
            for (var rangeIndex = 1; rangeIndex <= IgnoredErrorStyleOnlyIgnoredRanges; rangeIndex++)
            {
                ignoredErrors.Add(new XElement(
                    ns + "ignoredError",
                    new XAttribute("sqref", $"A{rangeIndex}:AD{rangeIndex + 999}"),
                    new XAttribute("numberStoredAsText", "1")));
            }

            worksheetXml.Root.Add(ignoredErrors);
            ReplaceZipEntryXml(archive, worksheetEntry.FullName, worksheetXml);
        }

        return stream.ToArray();
    }

    private static void AssertIgnoredErrorAndStyleOnlyMetadata(Workbook workbook)
    {
        workbook.SheetCount.Should().Be(1);
        var sheet = workbook.Sheets[0];
        sheet.EnumerateCells().Count(pair => pair.Cell.IgnoreFormulaError)
            .Should().Be(IgnoredErrorStyleOnlyRows * IgnoredErrorStyleOnlyValueColumns);
        sheet.GetStyleOnlyEntries().Count()
            .Should().Be(IgnoredErrorStyleOnlyRows * IgnoredErrorStyleOnlyStyleColumns);
    }

    private static void ReplaceZipEntryXml(ZipArchive archive, string entryName, XDocument document)
    {
        archive.GetEntry(entryName)?.Delete();
        var entry = archive.CreateEntry(entryName);
        using var stream = entry.Open();
        document.Save(stream, System.Xml.Linq.SaveOptions.DisableFormatting);
    }

    private static Workbook CreateDenseModelWorkbook()
    {
        var workbook = new Workbook("Dense IO");
        for (var sheetIndex = 1; sheetIndex <= DenseSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"Sheet {sheetIndex}");
            for (uint row = 1; row <= DenseRowsPerSheet; row++)
            {
                for (uint col = 1; col <= DenseColumnsPerSheet; col++)
                {
                    sheet.SetCell(
                        new CellAddress(sheet.Id, row, col),
                        new NumberValue(row * col + sheetIndex));
                }
            }
        }

        return workbook;
    }

    private static Workbook CreateDrawingPicturesWorkbook(int pictureCount)
    {
        var workbook = new Workbook("Drawing Pictures IO");
        var sheet = workbook.AddSheet("Sheet1");
        var imageBytes = MinimalPngBytes();
        for (var index = 0; index < pictureCount; index++)
        {
            var row = (uint)(1 + index / 18);
            var column = (uint)(1 + index % 18);
            sheet.Pictures.Add(new PictureModel
            {
                Name = $"Picture {index + 1}",
                Anchor = new CellAddress(sheet.Id, row, column),
                Kind = PictureKind.Image,
                ImageBytes = imageBytes,
                ContentType = "image/png",
                Width = 72,
                Height = 48,
                AltText = $"Drawing picture {index + 1}"
            });
        }

        return workbook;
    }

    private static byte[] MinimalPngBytes() =>
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
        0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
        0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
        0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
        0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
        0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
        0x42, 0x60, 0x82
    ];

    private static Workbook CreateIgnoredErrorsSaveWorkbook()
    {
        var workbook = new Workbook("Ignored Errors Save IO");
        var sheet = workbook.AddSheet("Data");
        for (uint row = 1; row <= IgnoredErrorSaveRows; row++)
        {
            for (uint col = 1; col <= IgnoredErrorSaveColumns; col++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, col),
                    new TextValue($"{row:D4}{col:D2}"));
                sheet.GetCell(row, col)!.IgnoreFormulaError = true;
            }
        }

        return workbook;
    }

    private static MemoryStream CreateWritablePackageStream(byte[] package)
    {
        var stream = new MemoryStream(package.Length * 2);
        stream.Write(package, 0, package.Length);
        stream.Position = 0;
        return stream;
    }

    private static void InvokeSavePostProcessing(Workbook workbook, Stream stream)
    {
        var method = typeof(XlsxFileAdapter).GetMethod(
            "ApplyPackagePostProcessing",
            BindingFlags.NonPublic | BindingFlags.Static);
        method.Should().NotBeNull();
        method!.Invoke(null, [workbook, stream, null]);
    }

    private static void MeasureExternalStage(string path, string stage, Action action)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            action();
            stopwatch.Stop();
            var allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            Console.WriteLine(
                "PERF XLSX_LOAD_EXTERNAL_STAGE " +
                $"stage={stage} file=\"{Path.GetFileName(path)}\" bytes={new FileInfo(path).Length:N0} " +
                $"elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F2} allocated_bytes={allocatedBytes:N0}");
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            Console.WriteLine(
                "PERF XLSX_LOAD_EXTERNAL_STAGE_FAILED " +
                $"stage={stage} file=\"{Path.GetFileName(path)}\" bytes={new FileInfo(path).Length:N0} " +
                $"elapsed_ms={stopwatch.Elapsed.TotalMilliseconds:F2} error=\"{ex.GetType().Name}: {ex.Message}\"");
        }
    }

    private static Workbook CreateStyleOnlyModelWorkbook()
    {
        var workbook = new Workbook("Style-only IO");
        var styleIds = new[]
        {
            workbook.RegisterStyle(new CellStyle
            {
                FillColor = new CellColor(221, 235, 247),
                BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(91, 155, 213))
            }),
            workbook.RegisterStyle(new CellStyle
            {
                FillColor = new CellColor(226, 239, 218),
                BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(112, 173, 71))
            }),
            workbook.RegisterStyle(new CellStyle
            {
                FillColor = new CellColor(252, 228, 214),
                BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(237, 125, 49))
            })
        };

        for (var sheetIndex = 1; sheetIndex <= StyleOnlySaveSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"Styled blanks {sheetIndex}");
            for (uint row = 1; row <= StyleOnlySaveRowsPerSheet; row++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, 1),
                    new NumberValue(row + (uint)sheetIndex));

                for (uint col = 3; col < 3 + StyleOnlySaveColumnsPerSheet; col++)
                {
                    var runIndex = (col - 3) / StyleOnlySaveRunWidth;
                    var styleIndex = (int)((runIndex + row + (uint)sheetIndex) % (uint)styleIds.Length);
                    sheet.SetStyleOnly(row, col, styleIds[styleIndex]);
                }
            }
        }

        return workbook;
    }

    private static Workbook CreateWorksheetNativeMetadataWorkbook()
    {
        var workbook = new Workbook("Worksheet native metadata IO");
        for (var sheetIndex = 1; sheetIndex <= WorksheetNativeMetadataSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"Metadata {sheetIndex}");
            for (uint row = 1; row <= WorksheetNativeMetadataRowsPerSheet; row++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, 1),
                    new TextValue($"R{row}"));
            }

            sheet.IsProtected = true;
            sheet.ProtectionMetadata = MakeBag(
                "sheetProtection",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["algorithmName"] = "SHA-512",
                    ["hashValue"] = $"hash{sheetIndex}",
                    ["saltValue"] = $"salt{sheetIndex}",
                    ["spinCount"] = "100000",
                    ["objects"] = "1",
                    ["scenarios"] = "1"
                },
                [$"<fx:sheetProtectionNative xmlns:fx=\"urn:freex:test\" id=\"{sheetIndex}\" />"]);
            sheet.PrintOptionsMetadata = MakeBag(
                "printOptions",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["gridLinesSet"] = "1",
                    ["customAttr"] = $"print-{sheetIndex}"
                },
                [$"<fx:printOptionsNative xmlns:fx=\"urn:freex:test\" id=\"{sheetIndex}\" />"]);
            sheet.DimensionMetadata = MakeBag(
                "dimension",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["nativeDimensionAttr"] = $"dimension-{sheetIndex}"
                });
            sheet.SheetPropertiesMetadata = MakeBag(
                "sheetPr",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["filterMode"] = "1",
                    ["customSheetPrAttr"] = $"sheetPr-{sheetIndex}"
                },
                [$"<fx:sheetPrNative xmlns:fx=\"urn:freex:test\" id=\"{sheetIndex}\" />"]);
            sheet.PrimaryViewMetadata = MakeBag(
                "sheetView",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["showZeros"] = "0",
                    ["rightToLeft"] = "1",
                    ["customViewAttr"] = $"view-{sheetIndex}"
                },
                [$"<pivotSelection xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" pane=\"topLeft\" />"]);
            sheet.PageMargins = new WorksheetPageMargins(0.7, 0.75, 0.8, 0.85);
            sheet.PageMarginsMetadata = MakeBag(
                "pageMargins",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customAttr"] = $"margins-{sheetIndex}"
                },
                [$"<fx:pageMarginsNative xmlns:fx=\"urn:freex:test\" id=\"{sheetIndex}\" />"]);
            sheet.RowPageBreaks.Add(20);
            sheet.RowPageBreaksMetadata = new WorksheetPageBreaksMetadataModel
            {
                NativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["manualBreakCount"] = "1"
                },
                BreakNativeAttributes = new Dictionary<uint, Dictionary<string, string>>
                {
                    [20] = new(StringComparer.Ordinal)
                    {
                        ["pt"] = "1",
                        ["customAttr"] = $"row-break-{sheetIndex}"
                    }
                }
            };
            sheet.ColumnPageBreaks.Add(5);
            sheet.ColumnPageBreaksMetadata = new WorksheetPageBreaksMetadataModel
            {
                NativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["manualBreakCount"] = "1"
                },
                BreakNativeAttributes = new Dictionary<uint, Dictionary<string, string>>
                {
                    [5] = new(StringComparer.Ordinal)
                    {
                        ["pt"] = "1",
                        ["customAttr"] = $"column-break-{sheetIndex}"
                    }
                }
            };
            sheet.PageHeader = new WorksheetHeaderFooter("L", "C", "R");
            sheet.PageFooter = new WorksheetHeaderFooter("FL", "FC", "FR");
            sheet.HeaderFooterMetadata = MakeBag(
                "headerFooter",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["nativeHeaderFooterAttr"] = $"header-footer-{sheetIndex}"
                },
                [$"<fx:headerFooterNative xmlns:fx=\"urn:freex:test\" id=\"{sheetIndex}\" />"]);
        }

        return workbook;
    }

    private static Workbook CreateWorksheetAutoFilterNativeMetadataWorkbook()
    {
        var workbook = CreateWorksheetNativeMetadataWorkbook();
        foreach (var sheet in workbook.Sheets)
        {
            sheet.AutoFilter = new WorksheetAutoFilterModel(
                $"A1:B{WorksheetNativeMetadataRowsPerSheet}",
                null)
            {
                NativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customAutoFilterAttr"] = $"auto-filter-{sheet.Name}"
                }
            };
            sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
                0,
                [$"R{WorksheetNativeMetadataRowsPerSheet / 2}", $"R{WorksheetNativeMetadataRowsPerSheet}"],
                IncludeBlank: false));
            sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
                1,
                [],
                IncludeBlank: true,
                CustomFilters: [new WorksheetAutoFilterCustomFilterModel("greaterThanOrEqual", "10")],
                CustomFiltersAnd: false,
                NativeCustomFiltersAttributes: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customFiltersAttr"] = $"custom-filters-{sheet.Name}"
                },
                NativeFilterXmls: [],
                NativeAttributes: new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customFilterColumnAttr"] = $"filter-column-{sheet.Name}"
                }));
        }

        return workbook;
    }

    private static Workbook CreateDataValidationNativeMetadataWorkbook()
    {
        var workbook = new Workbook("Data validation native metadata IO");
        for (var sheetIndex = 1; sheetIndex <= WorksheetNativeMetadataSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"DV Metadata {sheetIndex}");
            for (uint row = 1; row <= WorksheetNativeMetadataRowsPerSheet; row++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, 1),
                    new NumberValue(row));
                sheet.DataValidations.Add(new DataValidation
                {
                    AppliesTo = new GridRange(
                        new CellAddress(sheet.Id, row, 1),
                        new CellAddress(sheet.Id, row, 1)),
                    Type = DvType.WholeNumber,
                    Operator = DvOperator.Between,
                    Formula1 = "1",
                    Formula2 = "100",
                    NativeAttributes = new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["imeMode"] = "noControl",
                        ["customDvAttr"] = $"dv-{sheetIndex}-{row}"
                    },
                    NativeChildXmls =
                    [
                        $"<extLst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><ext uri=\"{{FREEX-DV-{sheetIndex}-{row}}}\" /></extLst>"
                    ],
                    NativeContainerAttributes = row == 1
                        ? new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            ["disablePrompts"] = "0",
                            ["customDvContainerAttr"] = $"container-{sheetIndex}"
                        }
                        : null
                });
            }
        }

        return workbook;
    }

    private static Workbook CreateAdvancedConditionalFormattingWorkbook()
    {
        var workbook = new Workbook("Advanced conditional formatting IO");
        for (var sheetIndex = 1; sheetIndex <= WorksheetNativeMetadataSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"CF Metadata {sheetIndex}");
            for (uint row = 1; row <= WorksheetNativeMetadataRowsPerSheet; row++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, 1),
                    new NumberValue(row + sheetIndex));
            }

            for (uint row = 1; row <= AdvancedConditionalFormatRulesPerSheet; row++)
            {
                sheet.ConditionalFormats.Add(new ConditionalFormat
                {
                    AppliesTo = new GridRange(
                        new CellAddress(sheet.Id, row, 1),
                        new CellAddress(sheet.Id, row, 1)),
                    Priority = (int)row,
                    RuleType = CfRuleType.DataBar,
                    DataBarGradient = false,
                    DataBarBorder = true,
                    DataBarAxisPosition = "middle",
                    DataBarAxisColor = new RgbColor(0, 0, 0),
                    DataBarNegativeFillColor = new RgbColor(156, 0, 6),
                    DataBarNegativeBorderColor = new RgbColor(156, 0, 6),
                    NativePayloadChildXmls =
                    [
                        $"<x14:customPayload xmlns:x14=\"http://schemas.microsoft.com/office/spreadsheetml/2009/9/main\" id=\"{sheetIndex}-{row}\" />"
                    ],
                    FormatIfTrue = new CellStyle
                    {
                        FillColor = new CellColor(198, 239, 206),
                        FontColor = new CellColor(0, 97, 0),
                        BorderBottom = new CellBorder(BorderStyle.Thin, new CellColor(0, 97, 0))
                    }
                });
            }
        }

        return workbook;
    }

    private static Workbook CreateWorksheetSingleXmlCellsPostProcessingWorkbook()
    {
        var workbook = new Workbook("Worksheet singleXmlCells IO");
        for (var sheetIndex = 1; sheetIndex <= WorksheetNativeMetadataSheetCount; sheetIndex++)
        {
            var sheet = workbook.AddSheet($"SingleXml {sheetIndex}");
            for (uint row = 1; row <= WorksheetNativeMetadataRowsPerSheet; row++)
            {
                sheet.SetCell(
                    new CellAddress(sheet.Id, row, 1),
                    new TextValue($"R{row}"));
            }

            sheet.SmartTags = new WorksheetSmartTagsModel
            {
                NativeXml = "<smartTags xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    $"<cellSmartTags r=\"A{sheetIndex}\"><cellSmartTag type=\"{sheetIndex}\" deleted=\"0\">" +
                    $"<cellSmartTagPr key=\"place\" val=\"City{sheetIndex}\" /></cellSmartTag></cellSmartTags></smartTags>"
            };
            sheet.SingleXmlCells = new WorksheetSingleXmlCellsModel
            {
                NativeAttributes =
                {
                    ["nativeSingleXmlCellsAttr"] = $"single-xml-{sheetIndex}"
                }
            };
            for (var cellIndex = 1; cellIndex <= WorksheetSingleXmlCellsPerSheet; cellIndex++)
            {
                sheet.SingleXmlCells.Cells.Add(new WorksheetSingleXmlCellModel
                {
                    Id = cellIndex,
                    Reference = $"A{cellIndex}",
                    XmlCellPropertyId = 1000 + cellIndex,
                    NativeAttributes =
                    {
                        ["nativeSingleXmlCellAttr"] = $"single-cell-{sheetIndex}-{cellIndex}"
                    }
                });
            }
        }

        return workbook;
    }

    private static byte[] CreateWorksheetReplayMetadataSourcePackage()
    {
        using var workbook = new XLWorkbook();
        for (var sheetIndex = 1; sheetIndex <= WorksheetReplayMetadataSheetCount; sheetIndex++)
        {
            var sheet = workbook.Worksheets.Add($"Replay {sheetIndex}");
            for (var row = 1; row <= WorksheetReplayMetadataRowsPerSheet; row++)
                sheet.Cell(row, 1).Value = $"R{row}";
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    private static void ApplyWorksheetReplayMetadata(Workbook workbook)
    {
        for (var i = 0; i < workbook.Sheets.Count; i++)
        {
            var sheet = workbook.Sheets[i];
            var sheetIndex = i + 1;
            sheet.SmartTags = new WorksheetSmartTagsModel
            {
                NativeXml = "<smartTags xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
                    $"<cellSmartTags r=\"A{sheetIndex}\"><cellSmartTag type=\"{sheetIndex}\" deleted=\"0\">" +
                    $"<cellSmartTagPr key=\"place\" val=\"City{sheetIndex}\" /></cellSmartTag></cellSmartTags></smartTags>"
            };
            sheet.SortState = new WorksheetSortStateModel
            {
                Reference = $"A1:A{WorksheetReplayMetadataRowsPerSheet}",
                CaseSensitive = true,
                Conditions =
                [
                    new WorksheetSortConditionModel
                    {
                        Reference = $"A1:A{WorksheetReplayMetadataRowsPerSheet}",
                        Descending = sheetIndex % 2 == 0,
                        SortBy = "value"
                    }
                ]
            };
            sheet.AdditionalViews = new WorksheetAdditionalViewsModel
            {
                NativeAttributes = { ["customSheetViewsAttr"] = $"views-{sheetIndex}" },
                Views =
                [
                    new WorksheetAdditionalViewModel
                    {
                        WorkbookViewId = (sheetIndex + 1).ToString(CultureInfo.InvariantCulture),
                        NativeAttributes = { ["customViewAttr"] = $"view-{sheetIndex}" }
                    }
                ]
            };
            sheet.DataConsolidation = new WorksheetDataConsolidationModel
            {
                Function = "sum",
                LeftLabels = true,
                TopLabels = true,
                Link = sheetIndex % 2 == 0,
                NativeAttributes = { ["customDataConsolidationFlag"] = $"data-{sheetIndex}" },
                References =
                [
                    new WorksheetDataConsolidationReferenceModel
                    {
                        Reference = "A1:A2",
                        Sheet = sheet.Name,
                        NativeAttributes = { ["customDataRefFlag"] = $"ref-{sheetIndex}" }
                    }
                ]
            };
            sheet.UsePrinterDefaults = false;
            sheet.PrintCopies = 2 + sheetIndex;
            sheet.PrintQualityVerticalDpi = 300 + sheetIndex;
            sheet.PageSetupMetadata = MakeBag(
                "pageSetup",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["customPageSetupAttr"] = $"page-setup-{sheetIndex}"
                },
                [$"<fx:nativePageSetupChild xmlns:fx=\"urn:freex:test\" id=\"{sheetIndex}\" />"]);
        }
    }

    private static NativeXmlPreserveBag MakeBag(
        string key,
        Dictionary<string, string>? attrs = null,
        IReadOnlyList<string>? children = null)
    {
        var wrapper = new XElement("e");
        foreach (var (name, value) in attrs ?? [])
            wrapper.SetAttributeValue(XName.Get(name), value);
        foreach (var childXml in children ?? [])
            wrapper.Add(XElement.Parse(childXml, System.Xml.Linq.LoadOptions.PreserveWhitespace));

        var bag = new NativeXmlPreserveBag();
        bag.Set(key, wrapper.ToString(System.Xml.Linq.SaveOptions.DisableFormatting));
        return bag;
    }

    private static string[] ResolveExternalWorkbookPaths()
    {
        var configured = Environment.GetEnvironmentVariable("FREEX_IO_BENCHMARK_PATHS");
        if (string.IsNullOrWhiteSpace(configured))
            return [];

        var limit = 3;
        if (int.TryParse(Environment.GetEnvironmentVariable("FREEX_IO_BENCHMARK_LIMIT"), out var configuredLimit))
            limit = Math.Clamp(configuredLimit, 1, 20);

        return configured
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .SelectMany(EnumerateWorkbookPaths)
            .Where(path => !Path.GetFileName(path).StartsWith("~$", StringComparison.Ordinal))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new FileInfo(path))
            .Where(file => file.Exists)
            .OrderByDescending(file => file.Length)
            .Take(limit)
            .Select(file => file.FullName)
            .ToArray();
    }

    private static IEnumerable<string> EnumerateWorkbookPaths(string path)
    {
        if (Directory.Exists(path))
        {
            return Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(IsSupportedBenchmarkWorkbook);
        }

        return File.Exists(path) && IsSupportedBenchmarkWorkbook(path)
            ? [path]
            : [];
    }

    private static bool IsSupportedBenchmarkWorkbook(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xlsm", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xltx", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".xltm", StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRepoFile(params string[] relativeParts) => TestWorkspaceFiles.FindRepoFile(relativeParts);
}
