using System.Text;
using System.Text.Json;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed partial class NativeJsonSchemaTests
{
    [Fact]
    public void Save_OmitsDefaultNativeJsonCellFields()
    {
        var workbook = new Workbook("CompactCells");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(42));
        sheet.SetFormula(new CellAddress(sheet.Id, 2, 1), "A1*2");

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var cells = document.RootElement.GetProperty("Sheets")[0].GetProperty("Cells");
        var literal = cells.EnumerateArray().Single(cell => cell.GetProperty("Address").GetString() == "A1");
        literal.GetProperty("Value").GetString().Should().Be("42");
        literal.GetProperty("ValueType").GetString().Should().Be("n");
        literal.TryGetProperty("Formula", out _).Should().BeFalse();
        literal.TryGetProperty("IgnoreFormulaError", out _).Should().BeFalse();

        var formula = cells.EnumerateArray().Single(cell => cell.GetProperty("Address").GetString() == "A2");
        formula.GetProperty("Formula").GetString().Should().Be("A1*2");
        formula.TryGetProperty("Value", out _).Should().BeFalse();
        formula.TryGetProperty("ValueType", out _).Should().BeFalse();
        formula.TryGetProperty("IgnoreFormulaError", out _).Should().BeFalse();

        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0);
        loaded.GetCell(1, 1)!.Value.Should().Be(new NumberValue(42));
        loaded.GetCell(2, 1)!.FormulaText.Should().Be("A1*2");
        loaded.GetCell(2, 1)!.IgnoreFormulaError.Should().BeFalse();
    }

    [Fact]
    public void Load_DropsMalformedNativeJsonHeaderFooterPicturePayloads()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "HeaderFooterPictures",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "PageHeaderPictures": {
                    "Left": {
                      "ImageBase64": "not-base64!",
                      "ContentType": "image/png",
                      "FileName": "broken.png",
                      "Width": 120,
                      "Height": 48
                    },
                    "Center": {
                      "ImageBase64": "AQIDBA==",
                      "ContentType": "image/png",
                      "FileName": "logo.png",
                      "Width": 144,
                      "Height": 64
                    }
                  }
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var sheet = new NativeJsonAdapter().Load(stream).GetSheetAt(0);

        sheet.PageHeaderPictures.Left.Should().BeNull();
        sheet.PageHeaderPictures.Center.Should().NotBeNull();
        sheet.PageHeaderPictures.Center!.ImageBytes.Should().Equal([1, 2, 3, 4]);
        sheet.PageHeaderPictures.Center.ContentType.Should().Be("image/png");
        sheet.PageHeaderPictures.Center.FileName.Should().Be("logo.png");
        sheet.PageHeaderPictures.Center.Width.Should().Be(144);
        sheet.PageHeaderPictures.Center.Height.Should().Be(64);
    }

    [Fact]
    public void Load_DropsMalformedNativeJsonWorksheetBackgroundPayloads()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "BackgroundImage",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "BackgroundImage": {
                    "ImageBase64": "not-base64!",
                    "ContentType": "image/png",
                    "FileName": "background.png"
                  }
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var sheet = new NativeJsonAdapter().Load(stream).GetSheetAt(0);

        sheet.BackgroundImage.Should().BeNull();
    }

    [Fact]
    public void Save_WritesNonFiniteNativeJsonNumbersAsTextCells()
    {
        var workbook = new Workbook("NonFinite");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), new NumberValue(double.NaN));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), new NumberValue(double.PositiveInfinity));
        sheet.SetCell(new CellAddress(sheet.Id, 1, 3), new NumberValue(double.NegativeInfinity));

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var cells = document.RootElement.GetProperty("Sheets")[0].GetProperty("Cells");
        cells[0].GetProperty("Value").GetString().Should().Be("NaN");
        cells[0].GetProperty("ValueType").GetString().Should().Be("t");
        cells[1].GetProperty("Value").GetString().Should().Be("Infinity");
        cells[1].GetProperty("ValueType").GetString().Should().Be("t");
        cells[2].GetProperty("Value").GetString().Should().Be("-Infinity");
        cells[2].GetProperty("ValueType").GetString().Should().Be("t");

        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0);
        loaded.GetCell(1, 1)!.Value.Should().Be(new TextValue("NaN"));
        loaded.GetCell(1, 2)!.Value.Should().Be(new TextValue("Infinity"));
        loaded.GetCell(1, 3)!.Value.Should().Be(new TextValue("-Infinity"));
    }

    [Fact]
    public void Save_RoundTripsNativeJsonDateTimeValues()
    {
        var workbook = new Workbook("DateTimes");
        var sheet = workbook.AddSheet("Sheet1");
        var dateTime = DateTimeValue.FromDateTime(new DateTime(2026, 5, 31, 8, 15, 30));
        var timeOnly = new DateTimeValue(new TimeSpan(9, 30, 0).TotalDays);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 1), dateTime);
        sheet.SetCell(new CellAddress(sheet.Id, 1, 2), timeOnly);

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var cells = document.RootElement.GetProperty("Sheets")[0].GetProperty("Cells");
        cells[0].GetProperty("ValueType").GetString().Should().Be("d");
        cells[1].GetProperty("ValueType").GetString().Should().Be("d");

        stream.Position = 0;
        var loaded = adapter.Load(stream).GetSheetAt(0);
        loaded.GetCell(1, 1)!.Value.Should().Be(dateTime);
        loaded.GetCell(1, 2)!.Value.Should().Be(timeOnly);
    }

    [Fact]
    public void Load_AcceptsTrimmedFallbackCellAddresses()
    {
        const string json = """
            {
              "Name": "AddressFallback",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "Cells": [
                    { "Address": " a1 ", "Value": "42", "ValueType": "n" },
                    { "Address": " b2 ", "Value": "fallback", "ValueType": "s" }
                  ],
                  "StyleOnlyCells": [
                    { "Address": " c3 ", "StyleId": 0 }
                  ]
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var sheet = new NativeJsonAdapter().Load(stream).GetSheetAt(0);

        sheet.GetCell(1, 1)!.Value.Should().Be(new NumberValue(42));
        sheet.GetCell(2, 2)!.Value.Should().Be(new TextValue("fallback"));
        sheet.GetStyleOnly(3, 3).Should().Be(StyleId.Default);
    }

    [Fact]
    public void Load_TreatsNullSheetCellCollectionsAsEmpty()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "NullCells",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "Cells": null,
                  "StyleOnlyCells": null
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Sheet1");
        sheet.GetCell(1, 1).Should().BeNull();
        sheet.HasStyleOnlyCells.Should().BeFalse();
    }

    [Fact]
    public void Load_HonorsEmptySheetProtectionPermissions()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "EmptyProtectionPermissions",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "IsProtected": true,
                  "ProtectionPermissions": []
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        var sheet = workbook.GetSheetAt(0);
        sheet.IsProtected.Should().BeTrue();
        sheet.ProtectionPermissions.Should().BeEmpty();
    }

    [Theory]
    [InlineData("""{ "Name": "LegacyWithoutSheets" }""")]
    [InlineData("""{ "Name": "LegacyWithNoValidSheets", "Sheets": [ { "Name": "" }, null ] }""")]
    public void Load_AddsDefaultSheetWhenNativeJsonHasNoValidSheets(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        workbook.Sheets.Should().ContainSingle();
        workbook.GetSheetAt(0).Name.Should().Be("Sheet1");
    }

    [Fact]
    public void Load_NormalizesInvalidBlankDuplicateAndLongNativeJsonSheetNames()
    {
        const string json = """
            {
              "Name": "MalformedSheetNames",
              "Sheets": [
                { "Name": "'Bad:/?*[]Name'" },
                { "Name": "bad:/?*[]name" },
                { "Name": "   " },
                { "Name": "''" },
                { "Name": "ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890" }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        workbook.Sheets.Select(sheet => sheet.Name).Should().Equal(
            "Bad______Name",
            "bad______name (1)",
            "Sheet3",
            "Sheet",
            "ABCDEFGHIJKLMNOPQRSTUVWXYZ12345");
        workbook.Sheets.Select(sheet => sheet.Name).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Load_ResolvesMetadataReferencesToNormalizedNativeJsonSheetNames()
    {
        const string json = """
            {
              "Name": "MalformedSheetReferences",
              "Sheets": [
                {
                  "Name": "'Bad:/?*[]Name'",
                  "Cells": [
                    { "Address": "A1", "Value": "42", "ValueType": "n" }
                  ]
                }
              ],
              "NamedRanges": [
                { "Name": "Input", "SheetName": "'Bad:/?*[]Name'", "Range": "A1:A1" }
              ],
              "WatchedCells": [
                { "SheetName": "'Bad:/?*[]Name'", "Address": "A1" }
              ],
              "Scenarios": [
                {
                  "Name": "Scenario 1",
                  "ChangingCells": [
                    { "SheetName": "'Bad:/?*[]Name'", "Address": "A1", "Value": "99", "ValueType": "n" }
                  ]
                }
              ],
              "CustomViews": [
                {
                  "Name": "View 1",
                  "Sheets": [
                    { "SheetName": "'Bad:/?*[]Name'", "ZoomPercent": 125 }
                  ]
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        var sheet = workbook.GetSheetAt(0);
        sheet.Name.Should().Be("Bad______Name");
        workbook.NamedRanges["Input"].Should().Be(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1)));
        workbook.WatchedCells.Should().ContainSingle().Which.Should().Be(new CellAddress(sheet.Id, 1, 1));
        workbook.Scenarios.Should().ContainSingle()
            .Which.ChangingCells.Should().ContainSingle()
            .Which.Address.Should().Be(new CellAddress(sheet.Id, 1, 1));
        workbook.CustomViews.Should().ContainSingle()
            .Which.Sheets.Should().ContainSingle()
            .Which.SheetName.Should().Be("Bad______Name");
    }

    [Fact]
    public void Save_SkipsOutOfBoundsNativeJsonCellAddresses()
    {
        var workbook = new Workbook("InvalidAddresses");
        var sheet = workbook.AddSheet("Sheet1");
        var valid = new CellAddress(sheet.Id, 1, 1);
        var invalidRow = new CellAddress(sheet.Id, CellAddress.MaxRow + 1, 1);
        var invalidColumn = new CellAddress(sheet.Id, 1, CellAddress.MaxCol + 1);

        sheet.SetCell(valid, new TextValue("kept"));
        sheet.SetCell(invalidRow, new TextValue("dropped"));
        sheet.Comments[valid] = "kept";
        sheet.Comments[invalidRow] = "dropped";
        sheet.ThreadedComments[valid] = new ThreadedComment("kept");
        sheet.ThreadedComments[invalidColumn] = new ThreadedComment("dropped");
        sheet.Hyperlinks[valid] = "https://example.invalid/kept";
        sheet.Hyperlinks[invalidColumn] = "https://example.invalid/dropped";
        var styleId = workbook.RegisterStyle(new CellStyle { Bold = true });
        sheet.SetStyleOnly(valid.Row, valid.Col + 1, styleId);
        sheet.SetStyleOnly(invalidRow.Row, invalidRow.Col + 1, styleId);
        workbook.WatchedCells.Add(valid);
        workbook.WatchedCells.Add(invalidRow);
        workbook.Scenarios.Add(new WorkbookScenario(
            "Mixed",
            [
                new ScenarioCellValue(valid, new TextValue("kept")),
                new ScenarioCellValue(invalidColumn, new TextValue("dropped"))
            ]));

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        using var document = JsonDocument.Parse(stream.ToArray());

        var root = document.RootElement;
        root.GetProperty("WatchedCells").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Address").GetString().Should().Be("A1");
        root.GetProperty("Scenarios").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("ChangingCells").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Address").GetString().Should().Be("A1");

        var sheetJson = root.GetProperty("Sheets").EnumerateArray().Single();
        sheetJson.GetProperty("Cells").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Address").GetString().Should().Be("A1");
        sheetJson.GetProperty("Comments").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Address").GetString().Should().Be("A1");
        sheetJson.GetProperty("ThreadedComments").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Address").GetString().Should().Be("A1");
        sheetJson.GetProperty("Hyperlinks").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Address").GetString().Should().Be("A1");
        sheetJson.GetProperty("StyleOnlyCells").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Address").GetString().Should().Be("B1");
    }

    [Fact]
    public void Save_DropsNullNativeJsonThreadedCommentReplyEntries()
    {
        var workbook = new Workbook("NullThreadedCommentReplies");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ThreadedComments[new CellAddress(sheet.Id, 1, 1)] = new ThreadedComment("Parent")
        {
            Replies =
            [
                null!,
                new CommentReply("Kept", "Reviewer")
            ]
        };

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var replies = document.RootElement
            .GetProperty("Sheets").EnumerateArray().Single()
            .GetProperty("ThreadedComments").EnumerateArray().Single()
            .GetProperty("Replies").EnumerateArray()
            .ToList();

        replies.Should().ContainSingle();
        replies[0].GetProperty("Text").GetString().Should().Be("Kept");
        replies[0].GetProperty("Author").GetString().Should().Be("Reviewer");
    }

    [Fact]
    public void Load_DropsNullNativeJsonThreadedCommentReplyEntries()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "NullThreadedCommentReplies",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "ThreadedComments": [
                    {
                      "Address": "A1",
                      "Text": "Parent",
                      "Replies": [
                        null,
                        { "Author": "Nobody" },
                        { "Text": "Kept", "Author": "Reviewer" }
                      ]
                    }
                  ]
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var comment = new NativeJsonAdapter().Load(stream).GetSheetAt(0).ThreadedComments
            .Should().ContainSingle().Subject.Value;

        comment.Replies.Should().ContainSingle().Which.Should().Be(new CommentReply("Kept", "Reviewer"));
    }

    [Fact]
    public void Load_DropsNullNativeJsonCommentAndHyperlinkEntries()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "NullCommentHyperlinkEntries",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "Comments": [
                    null,
                    { "Address": "B2", "Text": "kept comment" }
                  ],
                  "ThreadedComments": [
                    null,
                    { "Address": "C3", "Text": "kept threaded" }
                  ],
                  "Hyperlinks": [
                    null,
                    { "Address": "D4", "Target": "https://example.invalid/kept" }
                  ]
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var sheet = new NativeJsonAdapter().Load(stream).GetSheetAt(0);

        sheet.Comments.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<CellAddress, string>(
                new CellAddress(sheet.Id, 2, 2),
                "kept comment"));
        sheet.ThreadedComments.Should().ContainSingle()
            .Which.Key.Should().Be(new CellAddress(sheet.Id, 3, 3));
        sheet.ThreadedComments[new CellAddress(sheet.Id, 3, 3)].Text.Should().Be("kept threaded");
        sheet.Hyperlinks.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<CellAddress, string>(
                new CellAddress(sheet.Id, 4, 4),
                "https://example.invalid/kept"));
    }

    [Fact]
    public void Save_SkipsOutOfBoundsNativeJsonRanges()
    {
        var workbook = new Workbook("InvalidRanges");
        var sheet = workbook.AddSheet("Sheet1");
        var validRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, 2));
        var invalidRowRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, CellAddress.MaxRow + 1, 2));
        var invalidColumnRange = new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 2, CellAddress.MaxCol + 1));

        workbook.DefineNamedRange("ValidRange", validRange);
        workbook.NamedRanges["InvalidRange"] = invalidRowRange;
        sheet.AddMergedRegion(validRange);
        sheet.AddMergedRegion(invalidRowRange);
        sheet.AllowEditRanges.Add(validRange);
        sheet.AllowEditRanges.Add(invalidColumnRange);

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);
        using var document = JsonDocument.Parse(stream.ToArray());

        var root = document.RootElement;
        var namedRange = root.GetProperty("NamedRanges").EnumerateArray()
            .Should().ContainSingle().Subject;
        namedRange.GetProperty("Name").GetString().Should().Be("ValidRange");
        namedRange.GetProperty("Range").GetString().Should().Be("A1:B2");

        var sheetJson = root.GetProperty("Sheets").EnumerateArray().Single();
        sheetJson.GetProperty("MergedRegions").EnumerateArray()
            .Should().ContainSingle().Which.GetString().Should().Be("A1:B2");
        sheetJson.GetProperty("AllowEditRanges").EnumerateArray()
            .Should().ContainSingle().Which.GetString().Should().Be("A1:B2");
    }

    [Fact]
    public void Save_TreatsNullNativeJsonWorksheetDataListsAsEmpty()
    {
        var workbook = new Workbook("NullWorksheetDataLists");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DataConsolidation = new WorksheetDataConsolidationModel
        {
            Function = "sum",
            References = null!
        };
        sheet.SortState = new WorksheetSortStateModel
        {
            Reference = "A1:B2",
            Conditions = null!
        };
        sheet.AutoFilter = new WorksheetAutoFilterModel("A1:B3", null);
        sheet.AutoFilter.FilterColumns.Add(null!);
        sheet.AutoFilter.FilterColumns.Add(new WorksheetAutoFilterColumnModel(
            0,
            null!,
            IncludeBlank: false,
            CustomFilters: [null!, new WorksheetAutoFilterCustomFilterModel("equal", "A")],
            CustomFiltersAnd: false,
            CustomFiltersAndRaw: null,
            NativeCustomFiltersAttributes: null,
            Top10: null,
            DynamicFilter: null,
            ColorFilter: null,
            IconFilter: null,
            DateGroups: [null!, new WorksheetAutoFilterDateGroupItemModel(Year: 2026, DateTimeGrouping: "year")],
            NativeFiltersAttributes: null,
            NativeFilterXmls: null!));

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var sheetJson = document.RootElement.GetProperty("Sheets").EnumerateArray().Single();
        sheetJson.GetProperty("DataConsolidation").GetProperty("References")
            .EnumerateArray().Should().BeEmpty();
        sheetJson.GetProperty("SortState").GetProperty("Conditions")
            .EnumerateArray().Should().BeEmpty();
        var filterColumn = sheetJson.GetProperty("AutoFilter").GetProperty("FilterColumns")
            .EnumerateArray().Should().ContainSingle().Subject;
        filterColumn.GetProperty("Values").EnumerateArray().Should().BeEmpty();
        filterColumn.GetProperty("CustomFilters").EnumerateArray().Should().ContainSingle();
        filterColumn.GetProperty("DateGroups").EnumerateArray().Should().ContainSingle();
        filterColumn.GetProperty("NativeFilterXmls").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public void Load_DropsNullNativeJsonWorksheetAutoFilterChildEntries()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "NullWorksheetAutoFilterChildren",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "AutoFilter": {
                    "Reference": "A1:B3",
                    "FilterColumns": [
                      null,
                      {
                        "ColumnId": 0,
                        "Values": [ null, "A" ],
                        "CustomFilters": [
                          null,
                          { "Operator": "equal", "Value": "A" }
                        ],
                        "DateGroups": [
                          null,
                          { "Year": 2026, "DateTimeGrouping": "year" }
                        ]
                      }
                    ]
                  }
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var filterColumn = new NativeJsonAdapter().Load(stream).GetSheetAt(0).AutoFilter!.FilterColumns
            .Should().ContainSingle().Subject;

        filterColumn.ColumnId.Should().Be(0);
        filterColumn.Values.Should().ContainSingle().Which.Should().Be("A");
        var customFilter = filterColumn.CustomFilters.Should().ContainSingle().Subject;
        customFilter.Operator.Should().Be("equal");
        customFilter.Value.Should().Be("A");
        var dateGroup = filterColumn.DateGroups.Should().ContainSingle().Subject;
        dateGroup.Year.Should().Be(2026);
        dateGroup.DateTimeGrouping.Should().Be("year");
    }

    [Fact]
    public void Save_DropsNullNativeJsonWorksheetCustomPropertyEntries()
    {
        var workbook = new Workbook("NullWorksheetCustomProperties");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.CustomProperties.Add(null!);
        sheet.CustomProperties.Add(new WorksheetCustomProperty("", 1));
        sheet.CustomProperties.Add(new WorksheetCustomProperty("MissingId", 0));
        sheet.CustomProperties.Add(new WorksheetCustomProperty("ModeledProperty", 7));

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var propertyJson = document.RootElement
            .GetProperty("Sheets").EnumerateArray().Single()
            .GetProperty("CustomProperties").EnumerateArray()
            .Should().ContainSingle().Subject;

        propertyJson.GetProperty("Name").GetString().Should().Be("ModeledProperty");
        propertyJson.GetProperty("Id").GetInt32().Should().Be(7);
    }

    [Fact]
    public void Load_DropsNullNativeJsonWorksheetCustomPropertyEntries()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "NullWorksheetCustomProperties",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "CustomProperties": [
                    null,
                    { "Name": "ModeledProperty", "Id": 7 },
                    { "Name": "MissingId" },
                    { "Id": 8 }
                  ]
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var property = new NativeJsonAdapter().Load(stream).GetSheetAt(0).CustomProperties
            .Should().ContainSingle().Subject;

        property.Name.Should().Be("ModeledProperty");
        property.Id.Should().Be(7);
    }

    [Fact]
    public void Load_DropsNullNativeJsonWorksheetDimensionEntries()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "NullWorksheetDimensions",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "RowHeights": [
                    null,
                    { "Index": 2, "Value": 24.5 }
                  ],
                  "ColumnWidths": [
                    null,
                    { "Index": 3, "Value": 14.25 }
                  ],
                  "RowOutlineLevels": [
                    null,
                    { "Index": 4, "Value": 2 }
                  ],
                  "ColOutlineLevels": [
                    null,
                    { "Index": 5, "Value": 3 }
                  ]
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var sheet = new NativeJsonAdapter().Load(stream).GetSheetAt(0);

        sheet.RowHeights.Should().Contain(new KeyValuePair<uint, double>(2, 24.5));
        sheet.ColumnWidths.Should().Contain(new KeyValuePair<uint, double>(3, 14.25));
        sheet.RowOutlineLevels.Should().Contain(new KeyValuePair<uint, int>(4, 2));
        sheet.ColOutlineLevels.Should().Contain(new KeyValuePair<uint, int>(5, 3));
    }

    [Fact]
    public void Save_DropsNullNativeJsonDataValidationEntries()
    {
        var workbook = new Workbook("NullDataValidationEntries");
        var sheet = workbook.AddSheet("Sheet1");
        var validation = new DataValidation
        {
            AppliesTo = GridRange.Parse("A1:A2", sheet.Id),
            NativeAttributes = new Dictionary<string, string>
            {
                [""] = "dropped",
                ["imeMode"] = "noControl",
                ["nullAttr"] = null!
            },
            NativeChildXmls = [null!, " ", "<x:ext />"],
            NativeContainerAttributes = new Dictionary<string, string>
            {
                [" "] = "dropped",
                ["disablePrompts"] = "1",
                ["nullAttr"] = null!
            },
            NativeContainerChildXmls = [null!, "", "<x:container />"]
        };

        sheet.DataValidations.Add(null!);
        sheet.DataValidations.Add(validation);

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        document.RootElement
            .GetProperty("Sheets").EnumerateArray().Single()
            .GetProperty("DataValidations").EnumerateArray()
            .Should().ContainSingle();
        var validationJson = document.RootElement
            .GetProperty("Sheets").EnumerateArray().Single()
            .GetProperty("DataValidations").EnumerateArray().Single();
        validationJson.GetProperty("NativeAttributes").EnumerateObject()
            .Should().ContainSingle().Which.Should().Match<JsonProperty>(
                property => property.Name == "imeMode" && property.Value.GetString() == "noControl");
        validationJson.GetProperty("NativeChildXmls").EnumerateArray()
            .Should().ContainSingle().Which.GetString().Should().Be("<x:ext />");
        validationJson.GetProperty("NativeContainerAttributes").EnumerateObject()
            .Should().ContainSingle().Which.Should().Match<JsonProperty>(
                property => property.Name == "disablePrompts" && property.Value.GetString() == "1");
        validationJson.GetProperty("NativeContainerChildXmls").EnumerateArray()
            .Should().ContainSingle().Which.GetString().Should().Be("<x:container />");
    }

    [Fact]
    public void Load_DropsNullNativeJsonDataValidationNativeChildXmlEntries()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "NullDataValidationNativeChildXmls",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "DataValidations": [
                    {
                      "AppliesTo": "A1:A2",
                      "NativeAttributes": {
                        "": "dropped",
                        "imeMode": "noControl",
                        "nullAttr": null
                      },
                      "NativeChildXmls": [ null, " ", "<x:ext />" ],
                      "NativeContainerAttributes": {
                        " ": "dropped",
                        "disablePrompts": "1",
                        "nullAttr": null
                      },
                      "NativeContainerChildXmls": [ null, "", "<x:container />" ]
                    }
                  ]
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var validation = new NativeJsonAdapter().Load(stream).GetSheetAt(0).DataValidations
            .Should().ContainSingle().Subject;

        validation.NativeChildXmls.Should().ContainSingle().Which.Should().Be("<x:ext />");
        validation.NativeAttributes.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, string>("imeMode", "noControl"));
        validation.NativeContainerAttributes.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, string>("disablePrompts", "1"));
        validation.NativeContainerChildXmls.Should().ContainSingle().Which.Should().Be("<x:container />");
    }

    [Fact]
    public void Save_DropsNullNativeJsonConditionalFormatChildEntries()
    {
        var workbook = new Workbook("NullConditionalFormatChildEntries");
        var sheet = workbook.AddSheet("Sheet1");
        var format = new ConditionalFormat
        {
            AppliesTo = GridRange.Parse("A1:A3", sheet.Id),
            RuleType = CfRuleType.IconSet,
            IconSetStyle = "3Arrows",
            NativeAttributes = new Dictionary<string, string>
            {
                [""] = "dropped",
                ["priority"] = "7",
                ["nullAttr"] = null!
            },
            NativeChildXmls = [null!, " ", "<x:ext />"],
            NativePayloadAttributes = new Dictionary<string, string>
            {
                [" "] = "dropped",
                ["x14:axisPosition"] = "middle",
                ["nullAttr"] = null!
            },
            NativePayloadChildXmls = [null!, "", "<x:payload />"],
            NativeContainerAttributes = new Dictionary<string, string>
            {
                [""] = "dropped",
                ["pivot"] = "1",
                ["nullAttr"] = null!
            },
            NativeContainerChildXmls = [null!, " ", "<x:container />"]
        };
        format.IconSetThresholds.Add(null!);
        format.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Number, "5"));
        format.IconOverrides.Add(null!);
        format.IconOverrides.Add(new CfIconOverride("  3Arrows  ", 1));
        sheet.ConditionalFormats.Add(format);

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var formatJson = document.RootElement.GetProperty("Sheets").EnumerateArray().Single()
            .GetProperty("ConditionalFormats").EnumerateArray().Single();
        formatJson.GetProperty("IconSetThresholds").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Value").GetString().Should().Be("5");
        formatJson.GetProperty("IconOverrides").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("IconSet").GetString().Should().Be("3Arrows");
        formatJson.GetProperty("NativeAttributes").EnumerateObject()
            .Should().ContainSingle().Which.Should().Match<JsonProperty>(
                property => property.Name == "priority" && property.Value.GetString() == "7");
        formatJson.GetProperty("NativeChildXmls").EnumerateArray()
            .Should().ContainSingle().Which.GetString().Should().Be("<x:ext />");
        formatJson.GetProperty("NativePayloadAttributes").EnumerateObject()
            .Should().ContainSingle().Which.Should().Match<JsonProperty>(
                property => property.Name == "x14:axisPosition" && property.Value.GetString() == "middle");
        formatJson.GetProperty("NativePayloadChildXmls").EnumerateArray()
            .Should().ContainSingle().Which.GetString().Should().Be("<x:payload />");
        formatJson.GetProperty("NativeContainerAttributes").EnumerateObject()
            .Should().ContainSingle().Which.Should().Match<JsonProperty>(
                property => property.Name == "pivot" && property.Value.GetString() == "1");
        formatJson.GetProperty("NativeContainerChildXmls").EnumerateArray()
            .Should().ContainSingle().Which.GetString().Should().Be("<x:container />");
    }

    [Fact]
    public void RoundTrips_NativeJsonAboveAverageEqualAverageAndStdDev()
    {
        var workbook = new Workbook("AboveAverageStdDev");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.ConditionalFormats.Add(new ConditionalFormat
        {
            AppliesTo = GridRange.Parse("A1:A9", sheet.Id),
            RuleType = CfRuleType.AboveAverage,
            AboveAverage = false,
            EqualAverage = true,
            StdDevCount = 2
        });

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);
        stream.Position = 0;

        var format = adapter.Load(stream).GetSheetAt(0).ConditionalFormats.Should().ContainSingle().Subject;
        format.AboveAverage.Should().BeFalse();
        format.EqualAverage.Should().BeTrue();
        format.StdDevCount.Should().Be(2);
    }

    [Fact]
    public void Load_DropsNullNativeJsonConditionalFormatNativeChildXmlEntries()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "NullConditionalFormatNativeChildXmls",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "ConditionalFormats": [
                    {
                      "AppliesTo": "A1:A3",
                      "RuleType": 6,
                      "IconSetStyle": "3Arrows",
                      "NativeAttributes": {
                        "": "dropped",
                        "priority": "7",
                        "nullAttr": null
                      },
                      "NativeChildXmls": [ null, " ", "<x:ext />" ],
                      "NativePayloadAttributes": {
                        " ": "dropped",
                        "x14:axisPosition": "middle",
                        "nullAttr": null
                      },
                      "NativePayloadChildXmls": [ null, "", "<x:payload />" ],
                      "NativeContainerAttributes": {
                        "": "dropped",
                        "pivot": "1",
                        "nullAttr": null
                      },
                      "NativeContainerChildXmls": [ null, " ", "<x:container />" ]
                    }
                  ]
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var format = new NativeJsonAdapter().Load(stream).GetSheetAt(0).ConditionalFormats
            .Should().ContainSingle().Subject;

        format.NativeChildXmls.Should().ContainSingle().Which.Should().Be("<x:ext />");
        format.NativeAttributes.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, string>("priority", "7"));
        format.NativePayloadAttributes.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, string>("x14:axisPosition", "middle"));
        format.NativePayloadChildXmls.Should().ContainSingle().Which.Should().Be("<x:payload />");
        format.NativeContainerAttributes.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, string>("pivot", "1"));
        format.NativeContainerChildXmls.Should().ContainSingle().Which.Should().Be("<x:container />");
    }

    [Fact]
    public void Save_DropsNullNativeJsonDrawingAndSparklineEntries()
    {
        var workbook = new Workbook("NullDrawingEntries");
        var sheet = workbook.AddSheet("Sheet1");

        sheet.Pictures.Add(null!);
        sheet.Pictures.Add(new PictureModel
        {
            Anchor = new CellAddress(sheet.Id, 2, 2),
            SourceRowCount = 2,
            SourceColumnCount = 2,
            Cells =
            {
                null!,
                new PictureCellSnapshot(1, 1, "kept")
            }
        });
        sheet.TextBoxes.Add(null!);
        sheet.DrawingShapes.Add(null!);
        sheet.Sparklines.Add(null!);

        using var stream = new MemoryStream();
        new NativeJsonAdapter().Save(workbook, stream);

        using var document = JsonDocument.Parse(stream.ToArray());
        var sheetJson = document.RootElement.GetProperty("Sheets").EnumerateArray().Single();
        var pictureJson = sheetJson.GetProperty("Pictures").EnumerateArray().Should().ContainSingle().Subject;
        pictureJson.GetProperty("Cells").EnumerateArray()
            .Should().ContainSingle().Which.GetProperty("Text").GetString().Should().Be("kept");
        sheetJson.GetProperty("TextBoxes").EnumerateArray().Should().BeEmpty();
        sheetJson.GetProperty("DrawingShapes").EnumerateArray().Should().BeEmpty();
        sheetJson.GetProperty("Sparklines").EnumerateArray().Should().BeEmpty();
    }

    [Fact]
    public void Load_DropsMalformedNativeJsonPictureEntries()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "MalformedPictures",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "Pictures": [
                    null,
                    {
                      "Name": "Kept",
                      "Anchor": "B2",
                      "Kind": 1,
                      "ImageBase64": "AQIDBA==",
                      "ContentType": "image/png",
                      "Width": 144,
                      "Height": 96,
                      "Cells": [
                        null,
                        { "RowOffset": 1, "ColumnOffset": 2, "Text": "snapshot" }
                      ]
                    },
                    { "Name": "BadAnchor", "Anchor": "not-an-address" },
                    { "Name": "BadLinkedRange", "Anchor": "C3", "LinkedSourceRange": "not-a-range" },
                    { "Name": "BadImage", "Anchor": "D4", "ImageBase64": "not-base64!" }
                  ]
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var picture = new NativeJsonAdapter().Load(stream).GetSheetAt(0).Pictures
            .Should().ContainSingle().Subject;

        picture.Name.Should().Be("Kept");
        picture.Anchor.ToA1().Should().Be("B2");
        picture.Kind.Should().Be(PictureKind.Image);
        picture.ImageBytes.Should().Equal([1, 2, 3, 4]);
        picture.ContentType.Should().Be("image/png");
        picture.Width.Should().Be(144);
        picture.Height.Should().Be(96);
        picture.Cells.Should().ContainSingle()
            .Which.Should().Be(new PictureCellSnapshot(1, 2, "snapshot"));
    }

    [Fact]
    public void Load_DropsMalformedNativeJsonTextBoxAndShapeEntries()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "MalformedDrawingObjects",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "TextBoxes": [
                    null,
                    { "Name": "Kept Text", "Anchor": "C3", "Text": "review", "Width": 180, "Height": 80 },
                    { "Name": "Bad Text", "Anchor": "not-an-address", "Text": "dropped" }
                  ],
                  "DrawingShapes": [
                    null,
                    { "Name": "Kept Shape", "Anchor": "D4", "Kind": 1, "Width": 120, "Height": 70 },
                    { "Name": "Bad Shape", "Anchor": "not-an-address", "Kind": 0 }
                  ]
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var sheet = new NativeJsonAdapter().Load(stream).GetSheetAt(0);

        var textBox = sheet.TextBoxes.Should().ContainSingle().Subject;
        textBox.Name.Should().Be("Kept Text");
        textBox.Anchor.ToA1().Should().Be("C3");
        textBox.Text.Should().Be("review");
        textBox.Width.Should().Be(180);
        textBox.Height.Should().Be(80);

        var shape = sheet.DrawingShapes.Should().ContainSingle().Subject;
        shape.Name.Should().Be("Kept Shape");
        shape.Anchor.ToA1().Should().Be("D4");
        shape.Kind.Should().Be(DrawingShapeKind.Ellipse);
        shape.Width.Should().Be(120);
        shape.Height.Should().Be(70);
    }

    [Fact]
    public void Load_DropsMalformedNativeJsonSparklineEntries()
    {
        const string json = """
            {
              "FileFormat": "FreeX.NativeJsonWorkbook",
              "SchemaVersion": 1,
              "MinimumReaderVersion": 1,
              "Name": "MalformedSparklines",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "Sparklines": [
                    null,
                    { "DataRange": "A1:C1", "Location": "D1", "Kind": 1 },
                    { "DataRange": "not-a-range", "Location": "D2", "Kind": 0 },
                    { "DataRange": "A3:C3", "Location": "not-an-address", "Kind": 0 },
                    { "DataRange": "A4:C4", "Location": "D4", "Kind": 99 }
                  ]
                }
              ]
            }
            """;
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var sparkline = new NativeJsonAdapter().Load(stream).GetSheetAt(0).Sparklines
            .Should().ContainSingle().Subject;

        sparkline.DataRange.Start.ToA1().Should().Be("A1");
        sparkline.DataRange.End.ToA1().Should().Be("C1");
        sparkline.Location.ToA1().Should().Be("D1");
        sparkline.Kind.Should().Be(SparklineKind.Column);
    }

    [Theory]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    public void Load_TreatsNonFiniteNativeJsonNumbersAsText(string value)
    {
        var json = $$"""
            {
              "Name": "NonFinite",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "Cells": [
                    { "Address": "A1", "Value": "{{value}}", "ValueType": "n" }
                  ]
                }
              ]
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        workbook.GetSheetAt(0).GetCell(1, 1)!.Value.Should().Be(new TextValue(value));
    }

    [Theory]
    [InlineData("TRUE", true)]
    [InlineData("false", false)]
    public void Load_ParsesNativeJsonBooleanCellsCaseInsensitively(string value, bool expected)
    {
        var json = $$"""
            {
              "Name": "BooleanCell",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "Cells": [
                    { "Address": "A1", "Value": "{{value}}", "ValueType": "b" }
                  ]
                }
              ]
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        workbook.GetSheetAt(0).GetCell(1, 1)!.Value.Should().Be(new BoolValue(expected));
    }

    [Fact]
    public void Load_TreatsMalformedNativeJsonBooleanCellsAsText()
    {
        const string json = """
            {
              "Name": "BooleanCell",
              "Sheets": [
                {
                  "Name": "Sheet1",
                  "Cells": [
                    { "Address": "A1", "Value": "not-bool", "ValueType": "b" }
                  ]
                }
              ]
            }
            """;

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        var workbook = new NativeJsonAdapter().Load(stream);

        workbook.GetSheetAt(0).GetCell(1, 1)!.Value.Should().Be(new TextValue("not-bool"));
    }
}
