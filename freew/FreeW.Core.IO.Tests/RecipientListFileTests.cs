using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using FreeW.Core.IO.RecipientSources;

namespace FreeW.Core.IO.Tests;

/// <summary>
/// Word's Select Recipients &gt; Use an Existing List: Excel workbooks (worksheet / named range / table),
/// delimited text (delimiter + encoding detection) and documents whose first table is the list.
/// </summary>
public sealed class RecipientListFileTests
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string OfficeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    // A hand-built workbook: sheet "People" (a list starting in B2 with shared strings, a number, a
    // date, a boolean, an inline string and a cached formula string), sheet "Other", a named range
    // "VIPs" over part of People, and an Excel table "Staff" on Other.
    private static MemoryStream BuildWorkbook()
    {
        var stream = new MemoryStream();
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml",
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>");
            Add(zip, "_rels/.rels",
                $"<Relationships xmlns=\"{PackageRel}\"><Relationship Id=\"rId1\" Type=\"{OfficeRel}/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Add(zip, "xl/workbook.xml",
                $"<workbook xmlns=\"{Main}\" xmlns:r=\"{OfficeRel}\"><sheets>" +
                "<sheet name=\"People\" sheetId=\"1\" r:id=\"rId1\"/>" +
                "<sheet name=\"Other Data\" sheetId=\"2\" r:id=\"rId2\"/></sheets>" +
                "<definedNames>" +
                "<definedName name=\"VIPs\">People!$B$2:$C$3</definedName>" +
                "<definedName name=\"_xlnm.Print_Area\" localSheetId=\"0\">People!$A$1:$F$9</definedName>" +
                "<definedName name=\"Broken\">#REF!</definedName>" +
                "</definedNames></workbook>");
            Add(zip, "xl/_rels/workbook.xml.rels",
                $"<Relationships xmlns=\"{PackageRel}\">" +
                $"<Relationship Id=\"rId1\" Type=\"{OfficeRel}/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                $"<Relationship Id=\"rId2\" Type=\"{OfficeRel}/worksheet\" Target=\"worksheets/sheet2.xml\"/>" +
                $"<Relationship Id=\"rId3\" Type=\"{OfficeRel}/sharedStrings\" Target=\"sharedStrings.xml\"/>" +
                $"<Relationship Id=\"rId4\" Type=\"{OfficeRel}/styles\" Target=\"styles.xml\"/>" +
                "</Relationships>");
            Add(zip, "xl/sharedStrings.xml",
                $"<sst xmlns=\"{Main}\">" +
                "<si><t>Name</t></si><si><t>City</t></si><si><t>Joined</t></si><si><t>Score</t></si>" +
                "<si><t>Active</t></si><si><t>Note</t></si>" +
                "<si><r><t>Ada </t></r><r><rPr><b/></rPr><t>Lovelace</t></r><rPh><t>ignored</t></rPh></si>" +
                "<si><t>London</t></si><si><t>Grace Hopper</t></si><si><t>New York</t></si>" +
                "<si><t>Role</t></si><si><t>Engineer</t></si>" +
                "</sst>");
            Add(zip, "xl/styles.xml",
                $"<styleSheet xmlns=\"{Main}\"><numFmts count=\"1\">" +
                "<numFmt numFmtId=\"164\" formatCode=\"yyyy\\-mm\\-dd\"/></numFmts>" +
                "<cellXfs count=\"3\"><xf numFmtId=\"0\"/><xf numFmtId=\"14\"/><xf numFmtId=\"164\"/></cellXfs>" +
                "</styleSheet>");
            Add(zip, "xl/worksheets/sheet1.xml",
                $"<worksheet xmlns=\"{Main}\"><sheetData>" +
                "<row r=\"2\">" +
                "<c r=\"B2\" t=\"s\"><v>0</v></c><c r=\"C2\" t=\"s\"><v>1</v></c><c r=\"D2\" t=\"s\"><v>2</v></c>" +
                "<c r=\"E2\" t=\"s\"><v>3</v></c><c r=\"F2\" t=\"s\"><v>4</v></c><c r=\"G2\" t=\"s\"><v>5</v></c></row>" +
                "<row r=\"3\">" +
                "<c r=\"B3\" t=\"s\"><v>6</v></c><c r=\"C3\" t=\"s\"><v>7</v></c><c r=\"D3\" s=\"1\"><v>45306</v></c>" +
                "<c r=\"E3\"><v>0.30000000000000004</v></c><c r=\"F3\" t=\"b\"><v>1</v></c>" +
                "<c r=\"G3\" t=\"inlineStr\"><is><t>inline note</t></is></c></row>" +
                "<row r=\"4\">" +
                "<c r=\"B4\" t=\"s\"><v>8</v></c><c r=\"C4\" t=\"s\"><v>9</v></c><c r=\"D4\" s=\"2\"><v>36526</v></c>" +
                "<c r=\"E4\"><v>42</v></c><c r=\"F4\" t=\"b\"><v>0</v></c>" +
                "<c r=\"G4\" t=\"str\"><f>B4&amp;\"!\"</f><v>Grace Hopper!</v></c></row>" +
                "</sheetData></worksheet>");
            Add(zip, "xl/worksheets/sheet2.xml",
                $"<worksheet xmlns=\"{Main}\"><sheetData>" +
                "<row r=\"1\"><c r=\"A1\" t=\"inlineStr\"><is><t>Title above the table</t></is></c></row>" +
                "<row r=\"3\"><c r=\"A3\" t=\"s\"><v>0</v></c><c r=\"B3\" t=\"s\"><v>10</v></c></row>" +
                "<row r=\"4\"><c r=\"A4\" t=\"s\"><v>8</v></c><c r=\"B4\" t=\"s\"><v>11</v></c></row>" +
                "</sheetData></worksheet>");
            Add(zip, "xl/worksheets/_rels/sheet2.xml.rels",
                $"<Relationships xmlns=\"{PackageRel}\">" +
                $"<Relationship Id=\"rId1\" Type=\"{OfficeRel}/table\" Target=\"../tables/table1.xml\"/>" +
                "</Relationships>");
            Add(zip, "xl/tables/table1.xml",
                $"<table xmlns=\"{Main}\" id=\"1\" name=\"Table1\" displayName=\"Staff\" ref=\"A3:B4\"/>");
        }

        stream.Position = 0;
        return stream;
    }

    private static void Add(ZipArchive zip, string path, string xml)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
        writer.Write("<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + xml);
    }

    private static string Key(RecipientSourceInfo info, string displayName) =>
        info.Tables.Single(table => table.DisplayName == displayName).Key;

    [Fact]
    public void Inspect_Workbook_ListsWorksheetsNamedRangesAndTables()
    {
        using var workbook = BuildWorkbook();

        var info = RecipientListFile.Inspect(workbook, ".xlsx");

        info.Kind.Should().Be(RecipientSourceKind.Workbook);
        info.Tables.Select(table => (table.DisplayName, table.Kind)).Should().Equal(
            ("People$", RecipientSourceTableKind.Worksheet),
            ("Other Data$", RecipientSourceTableKind.Worksheet),
            ("VIPs", RecipientSourceTableKind.NamedRange),
            ("Staff", RecipientSourceTableKind.Table));
        info.Tables.Select(table => table.Key).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void Load_Worksheet_ReadsUsedRange_WithStringsNumbersDatesBooleansAndCachedFormulas()
    {
        using var workbook = BuildWorkbook();
        var info = RecipientListFile.Inspect(workbook, ".xlsm");
        workbook.Position = 0;

        var data = RecipientListFile.Load(workbook, ".xlsm", new RecipientSourceOptions(Key(info, "People$")));

        data.Header.Should().Equal("Name", "City", "Joined", "Score", "Active", "Note");
        data.Count.Should().Be(2);
        var ada = data.Rows[0];
        ada["Name"].Should().Be("Ada Lovelace", "rich-text runs are joined and phonetic text is ignored");
        ada["City"].Should().Be("London");
        ada["Joined"].Should().Be(new DateTime(2024, 1, 15).ToString("d", CultureInfo.CurrentCulture));
        ada["Score"].Should().Be(0.3.ToString("G15", CultureInfo.CurrentCulture), "binary noise is not shown");
        ada["Active"].Should().Be("TRUE");
        ada["Note"].Should().Be("inline note");
        var grace = data.Rows[1];
        grace["Joined"].Should().Be(new DateTime(2000, 1, 1).ToString("d", CultureInfo.CurrentCulture));
        grace["Score"].Should().Be("42");
        grace["Active"].Should().Be("FALSE");
        grace["Note"].Should().Be("Grace Hopper!", "the cached formula result is used");
    }

    [Fact]
    public void Load_WithNoTableKey_ReadsTheFirstWorksheet()
    {
        using var workbook = BuildWorkbook();

        RecipientListFile.Load(workbook, ".xlsx").Header.Should().StartWith("Name");
    }

    [Fact]
    public void Load_NamedRange_ReadsOnlyThatRange()
    {
        using var workbook = BuildWorkbook();
        var info = RecipientListFile.Inspect(workbook, ".xlsx");
        workbook.Position = 0;

        var data = RecipientListFile.Load(workbook, ".xlsx", new RecipientSourceOptions(Key(info, "VIPs")));

        data.Header.Should().Equal("Name", "City");
        data.Rows.Should().ContainSingle().Which["Name"].Should().Be("Ada Lovelace");
    }

    [Fact]
    public void Load_ExcelTable_ReadsTheTableRange_NotTheTitleAboveIt()
    {
        using var workbook = BuildWorkbook();
        var info = RecipientListFile.Inspect(workbook, ".xlsx");
        workbook.Position = 0;

        var data = RecipientListFile.Load(workbook, ".xlsx", new RecipientSourceOptions(Key(info, "Staff")));

        data.Header.Should().Equal("Name", "Role");
        data.Rows.Should().ContainSingle().Which["Role"].Should().Be("Engineer");
    }

    [Fact]
    public void Load_WithoutHeaderRow_NamesFieldsByPosition_AndKeepsFirstRowAsARecord()
    {
        using var workbook = BuildWorkbook();
        var info = RecipientListFile.Inspect(workbook, ".xlsx");
        workbook.Position = 0;

        var data = RecipientListFile.Load(
            workbook,
            ".xlsx",
            new RecipientSourceOptions(Key(info, "Staff"), FirstRowIsHeader: false));

        data.Header.Should().Equal("F1", "F2");
        data.Count.Should().Be(2);
        data.Rows[0]["F1"].Should().Be("Name");
    }

    [Fact]
    public void Load_UnknownTableKey_ReportsNoTable()
    {
        using var workbook = BuildWorkbook();

        var act = () => RecipientListFile.Load(workbook, ".xlsx", new RecipientSourceOptions("sheet:Missing"));

        act.Should().Throw<RecipientSourceException>().Which.Error.Should().Be(RecipientSourceError.NoTable);
    }

    [Fact]
    public void Workbook_ThatIsNotAZip_IsReportedUnreadable()
    {
        using var notAZip = new MemoryStream(Encoding.ASCII.GetBytes("Name,City\nAda,London\n"));

        var act = () => RecipientListFile.Inspect(notAZip, ".xlsx");

        act.Should().Throw<RecipientSourceException>().Which.Error.Should().Be(RecipientSourceError.Unreadable);
    }

    [Theory]
    [InlineData(".xls")]
    [InlineData(".mdb")]
    [InlineData(".accdb")]
    [InlineData(".odc")]
    [InlineData("")]
    public void UnsupportedSources_AreRefused_NotApproximated(string extension)
    {
        RecipientListFile.KindForPath("list" + extension).Should().BeNull();
        using var stream = new MemoryStream([1, 2, 3]);

        var act = () => RecipientListFile.Inspect(stream, extension);

        act.Should().Throw<RecipientSourceException>().Which.Error.Should().Be(RecipientSourceError.UnsupportedFormat);
    }

    [Theory]
    [InlineData("list.XLSX", RecipientSourceKind.Workbook)]
    [InlineData("list.xlsm", RecipientSourceKind.Workbook)]
    [InlineData("list.csv", RecipientSourceKind.DelimitedText)]
    [InlineData("list.TSV", RecipientSourceKind.DelimitedText)]
    [InlineData("list.txt", RecipientSourceKind.DelimitedText)]
    [InlineData("list.docx", RecipientSourceKind.Document)]
    [InlineData("list.rtf", RecipientSourceKind.Document)]
    public void KindForPath_ClassifiesSupportedExtensions(string path, RecipientSourceKind expected)
    {
        RecipientListFile.KindForPath(path).Should().Be(expected);
    }

    public static TheoryData<byte[], string, string> EncodedSamples()
    {
        const string text = "Name;City\nZoë;Köln\nŁukasz;Łódź\n";
        const string western = "Name;City\nZoë;Köln\n";
        return new TheoryData<byte[], string, string>
        {
            { [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(text)], "Łódź", "utf-8 with BOM" },
            { Encoding.UTF8.GetBytes(text), "Łódź", "utf-8 without BOM" },
            { [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)], "Łódź", "utf-16 LE with BOM" },
            { [.. Encoding.BigEndianUnicode.GetPreamble(), .. Encoding.BigEndianUnicode.GetBytes(text)], "Łódź", "utf-16 BE with BOM" },
            { Encoding.Unicode.GetBytes(western), "Köln", "utf-16 LE without BOM" },
            { Encoding.Latin1.GetBytes(western), "Köln", "8-bit Western text" },
        };
    }

    [Theory]
    [MemberData(nameof(EncodedSamples))]
    public void DelimitedText_DetectsEncodingAndDelimiter(byte[] bytes, string expectedLastCity, string because)
    {
        using var inspect = new MemoryStream(bytes);
        var info = RecipientListFile.Inspect(inspect, ".txt");
        using var load = new MemoryStream(bytes);

        var data = RecipientListFile.Load(load, ".txt");

        info.Kind.Should().Be(RecipientSourceKind.DelimitedText);
        info.Delimiter.Should().Be(';', because);
        info.EncodingName.Should().NotBeNullOrEmpty();
        data.Header.Should().Equal(["Name", "City"], because);
        data.Rows[0]["Name"].Should().Be("Zoë", because);
        data.Rows[^1]["City"].Should().Be(expectedLastCity, because);
    }

    [Fact]
    public void DelimitedText_TsvDefaultsToTab_AndAnExplicitDelimiterWins()
    {
        using var single = new MemoryStream(Encoding.UTF8.GetBytes("Name\nAda\n"));
        RecipientListFile.Inspect(single, ".tsv").Delimiter.Should().Be('\t');

        using var semicolons = new MemoryStream(Encoding.UTF8.GetBytes("Name;City\nAda;London\n"));
        var data = RecipientListFile.Load(semicolons, ".csv", new RecipientSourceOptions(Delimiter: ','));
        data.Header.Should().Equal("Name;City");
    }

    [Fact]
    public void EmptySource_IsReportedEmpty()
    {
        using var empty = new MemoryStream(Encoding.UTF8.GetBytes("\r\n\r\n"));

        var act = () => RecipientListFile.Load(empty, ".csv");

        act.Should().Throw<RecipientSourceException>().Which.Error.Should().Be(RecipientSourceError.Empty);
    }

    private static TextDocument DocumentWithRecipientTable()
    {
        var document = TextDocument.CreateEmpty();
        document.Blocks.Clear();
        document.Blocks.Add(new Paragraph("Our customers"));
        var table = new Table();
        foreach (var cells in new[] { new[] { "Name", "City" }, ["Ada", "London"], ["Grace", "New York"] })
        {
            var row = new TableRow();
            foreach (var cell in cells)
                row.Cells.Add(new TableCell(cell));
            table.Rows.Add(row);
        }
        document.Blocks.Add(table);
        // RTF has no table boundary of its own: adjacent tables read back as one, so separate them.
        document.Blocks.Add(new Paragraph("Another table follows"));
        var second = new Table();
        var secondRow = new TableRow();
        secondRow.Cells.Add(new TableCell("not the list"));
        second.Rows.Add(secondRow);
        document.Blocks.Add(second);
        return document;
    }

    [Theory]
    [InlineData(".docx")]
    [InlineData(".rtf")]
    public void Document_FirstTableIsTheRecipientList(string extension)
    {
        using var stream = new MemoryStream();
        if (extension == ".rtf")
            RtfWriter.Write(DocumentWithRecipientTable(), stream);
        else
            DocxWriter.Write(DocumentWithRecipientTable(), stream);
        stream.Position = 0;

        RecipientListFile.Inspect(stream, extension).Kind.Should().Be(RecipientSourceKind.Document);
        stream.Position = 0;
        var data = RecipientListFile.Load(stream, extension);

        data.Header.Should().Equal("Name", "City");
        data.Rows.Select(row => row["Name"]).Should().Equal("Ada", "Grace");
        data.Rows[1]["City"].Should().Be("New York");
    }

    [Fact]
    public void Document_WithoutATable_ReportsNoTable()
    {
        var document = TextDocument.CreateEmpty();
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        stream.Position = 0;

        var act = () => RecipientListFile.Load(stream, ".docx");

        act.Should().Throw<RecipientSourceException>().Which.Error.Should().Be(RecipientSourceError.NoTable);
    }

    [Theory]
    [InlineData(".csv", ',')]
    [InlineData(".tsv", '\t')]
    [InlineData(".txt", '\t')]
    public void Save_WritesDelimitedTextThatLoadsBackUnchanged(string extension, char delimiter)
    {
        var data = new MergeData(
            ["Name", "Address"],
            [["Zoë Łódź", "1 Main St, \"Flat 2\""], ["Grace", "line one\r\nline two"]]);
        using var stream = new MemoryStream();

        RecipientListFile.Save(stream, extension, data);

        var bytes = stream.ToArray();
        bytes.Take(3).Should().Equal(0xEF, 0xBB, 0xBF);
        using var inspect = new MemoryStream(bytes);
        RecipientListFile.Inspect(inspect, extension).Delimiter.Should().Be(delimiter);
        using var load = new MemoryStream(bytes);
        var reloaded = RecipientListFile.Load(load, extension);
        reloaded.Header.Should().Equal(data.Header);
        reloaded.Rows[0]["Name"].Should().Be("Zoë Łódź");
        reloaded.Rows[0]["Address"].Should().Be("1 Main St, \"Flat 2\"");
        reloaded.Rows[1]["Address"].Should().Be("line one\r\nline two");
    }

    [Fact]
    public void Save_ToAnUnsupportedType_IsRefused()
    {
        using var stream = new MemoryStream();

        var act = () => RecipientListFile.Save(stream, ".xlsx", new MergeData(["Name"], []));

        act.Should().Throw<RecipientSourceException>().Which.Error.Should().Be(RecipientSourceError.UnsupportedFormat);
    }

    [Theory]
    [InlineData("yyyy\\-mm\\-dd", true)]
    [InlineData("d/m/yy h:mm", true)]
    [InlineData("#,##0.00", false)]
    [InlineData("0.0 \"days\"", false)]
    [InlineData("[Red]0.00", false)]
    [InlineData("General", false)]
    [InlineData("[h]:mm", true)]
    public void IsDateFormatCode_RecognisesDatePlaceholdersOutsideLiterals(string code, bool expected)
    {
        XlsxRecipientReader.IsDateFormatCode(code).Should().Be(expected);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1)]
    [InlineData(2958466)]
    [InlineData(double.PositiveInfinity)]
    public void TryFromSerialDate_RejectsValuesOutsideTheExcelDateRange(double serial)
    {
        XlsxRecipientReader.TryFromSerialDate(serial, date1904: false, out _).Should().BeFalse();
    }

    [Fact]
    public void TryFromSerialDate_HandlesBothDateSystems_AndTheFictitiousLeapDay()
    {
        XlsxRecipientReader.TryFromSerialDate(1, false, out var first).Should().BeTrue();
        first.Should().Be(new DateTime(1900, 1, 1));
        XlsxRecipientReader.TryFromSerialDate(59, false, out var beforeLeap).Should().BeTrue();
        beforeLeap.Should().Be(new DateTime(1900, 2, 28));
        XlsxRecipientReader.TryFromSerialDate(61, false, out var afterLeap).Should().BeTrue();
        afterLeap.Should().Be(new DateTime(1900, 3, 1));
        XlsxRecipientReader.TryFromSerialDate(45306.5, false, out var withTime).Should().BeTrue();
        withTime.Should().Be(new DateTime(2024, 1, 15, 12, 0, 0));
        XlsxRecipientReader.TryFromSerialDate(0, true, out var mac).Should().BeTrue();
        mac.Should().Be(new DateTime(1904, 1, 1));
    }
}
