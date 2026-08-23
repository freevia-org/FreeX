using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using FluentAssertions;
using Free.Shared.Opc;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxWorksheetTraversalAndLexicalPolicyTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" \t\r\n ", null)]
    [InlineData(" value ", "value")]
    [InlineData("a b", "a b")]
    public void OptionalText_NormalizesOnlyOuterWhitespace(string? value, string? expected) =>
        XlsxXmlNormalizationHelpers.NormalizeOptionalText(value).Should().Be(expected);

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData(" YQ== ", "YQ==")]
    [InlineData("Y Q==", "Y Q==")]
    [InlineData("not-base64", null)]
    [InlineData("YQ=", null)]
    public void Base64_Normalization_PreservesValidatedLexicalPayload(string? value, string? expected) =>
        XlsxXmlNormalizationHelpers.NormalizeBase64BinaryOrNull(value).Should().Be(expected);

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData(" ab1f ", "AB1F")]
    [InlineData("FFFF", "FFFF")]
    [InlineData("abc", null)]
    [InlineData("abcde", null)]
    [InlineData("GGGG", null)]
    public void LegacyPasswordHash_Normalization_RequiresFourHexDigits(string? value, string? expected) =>
        XlsxXmlNormalizationHelpers.NormalizeLegacyPasswordHashOrNull(value).Should().Be(expected);

    [Theory]
    [InlineData(null, null)]
    [InlineData(" \t\r\n ", null)]
    [InlineData(" A1\tB2 \r\n invalid ", "A1 B2 invalid")]
    public void SqrefWhitespacePolicy_PreservesTokensWithoutValidation(string? value, string? expected) =>
        XlsxSqrefParser.NormalizeWhitespaceSeparatedTokens(value).Should().Be(expected);

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData("A1:A1 B2:B3 invalid b2:b3 C4", "A1 B2:B3 C4")]
    [InlineData("A:A 3:5", null)]
    [InlineData("A1\tB2", null)]
    public void SqrefCellRangePolicy_CanonicalizesDeduplicatesAndSkipsInvalidTokens(
        string? value,
        string? expected) =>
        XlsxSqrefParser.NormalizeCellRangeList(value).Should().Be(expected);

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData(" A:A  3:5 A1:B2 ", "A:A 3:5 A1:B2")]
    [InlineData("A1 invalid B2", null)]
    [InlineData("A1\tB2", null)]
    [InlineData("XFE:XFE", null)]
    [InlineData("1048577:1048577", null)]
    public void SqrefSelectionPolicy_PreservesValidTokensAndRejectsTheWholeListOnError(
        string? value,
        string? expected) =>
        XlsxSqrefParser.NormalizeSelectionReferenceList(value).Should().Be(expected);

    [Fact]
    public void WorksheetWriters_UseMappedPathsAndLeaveSkippedWorksheetsByteExact()
    {
        var workbook = new Workbook("Book");
        var first = workbook.AddSheet("First");
        workbook.AddSheet("Second");
        first.CodeName = "InternalFirst";
        first.AllowEditRanges.Add(new GridRange(
            new CellAddress(first.Id, 2, 2),
            new CellAddress(first.Id, 3, 3)));

        using var package = CreatePackage();
        var secondBefore = ReadEntry(package, "xl/worksheets/sheet2.xml");

        XlsxWorksheetCodeNameWriter.Save(package, workbook);
        XlsxAllowEditRangeMapper.Save(package, workbook);

        var firstXml = XDocument.Parse(ReadEntry(package, "xl/custom/sheet-one.xml"));
        var root = firstXml.Root!;
        root.Attribute("future")!.Value.Should().Be("retained");
        root.Element(WorksheetNs + "sheetData").Should().NotBeNull();
        root.Element(WorksheetNs + "sheetPr")!.Attribute("codeName")!.Value.Should().Be("InternalFirst");
        var protectedRange = root.Element(WorksheetNs + "protectedRanges")!
            .Element(WorksheetNs + "protectedRange")!;
        protectedRange.Attribute("name")!.Value.Should().Be("FreeXAllowEditRange1");
        protectedRange.Attribute("sqref")!.Value.Should().Be("B2:C3");
        ReadEntry(package, "xl/worksheets/sheet2.xml").Should().Be(secondBefore);
    }

    [Fact]
    public void WorksheetWriters_MissingWorkbookRelationships_AreByteExactNoOps()
    {
        using var package = CreatePackage(includeWorkbookRelationships: false);
        var before = package.ToArray();
        var workbook = new Workbook("Book");
        var sheet = workbook.AddSheet("First");
        sheet.CodeName = "InternalFirst";
        sheet.AllowEditRanges.Add(new GridRange(
            new CellAddress(sheet.Id, 1, 1),
            new CellAddress(sheet.Id, 1, 1)));

        XlsxWorksheetCodeNameWriter.Save(package, workbook);
        XlsxAllowEditRangeMapper.Save(package, workbook);

        package.ToArray().Should().Equal(before);
        package.CanRead.Should().BeTrue();
    }

    [Fact]
    public void WorksheetWriters_CorruptWorkbookXml_StillThrowsAndLeavesTheStreamOpen()
    {
        using var package = CreatePackage(workbookXml: "<workbook");
        var workbook = new Workbook("Book");
        workbook.AddSheet("First").CodeName = "InternalFirst";

        var act = () => XlsxWorksheetCodeNameWriter.Save(package, workbook);

        act.Should().Throw<XmlException>();
        package.CanRead.Should().BeTrue();
    }

    [Fact]
    public void WorksheetWriters_DuplicateRelationshipIds_StillThrowAndLeaveTheStreamOpen()
    {
        using var package = CreatePackage(
            relationshipsXml:
            """
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Id="rIdFirst" Type="worksheet" Target="custom/sheet-one.xml" />
              <Relationship Id="RIDFIRST" Type="worksheet" Target="worksheets/sheet2.xml" />
            </Relationships>
            """);
        var workbook = new Workbook("Book");
        workbook.AddSheet("First").CodeName = "InternalFirst";

        var act = () => XlsxWorksheetCodeNameWriter.Save(package, workbook);

        act.Should().Throw<ArgumentException>();
        package.CanRead.Should().BeTrue();
    }

    [Fact]
    public void Callers_AdoptSharedTraversalAndLexicalPolicies()
    {
        foreach (var file in new[] { "XlsxAllowEditRangeMapper.cs", "XlsxWorksheetCodeNameWriter.cs" })
        {
            var source = TestWorkspaceFiles.ReadCoreIoSource(file);
            source.Should().Contain("XlsxWorksheetPackageEditTraversal.EditSourceMapped")
                .And.NotContain("workbook.xml.rels")
                .And.NotContain("new ZipArchive");
        }

        foreach (var file in new[] { "XlsxWorksheetIgnoredErrorsNormalizer.cs", "XlsxWorksheetScenarioNormalizer.cs" })
        {
            var source = TestWorkspaceFiles.ReadCoreIoSource(file);
            source.Should().Contain("XlsxSqrefParser.NormalizeCellRangeList")
                .And.NotContain("private static string? NormalizeSqref");
        }

        TestWorkspaceFiles.ReadCoreIoSource("XlsxWorksheetProtectedRangeNormalizer.cs")
            .Should().Contain("XlsxSqrefParser.NormalizeWhitespaceSeparatedTokens");
        TestWorkspaceFiles.ReadCoreIoSource("XlsxWorksheetSheetViewNormalizer.cs")
            .Should().Contain("XlsxSqrefParser.NormalizeSelectionReferenceList")
            .And.NotContain("private static bool IsColumnOnlyReference");

        foreach (var file in new[]
                 {
                     "XlsxWorkbookLeafElementSchemas.cs",
                     "XlsxWorksheetProtectedRangeNormalizer.cs",
                     "XlsxWorksheetProtectionNormalizer.cs"
                 })
        {
            var source = TestWorkspaceFiles.ReadCoreIoSource(file);
            source.Should().Contain("XlsxXmlNormalizationHelpers.NormalizeBase64BinaryOrNull")
                .And.Contain("XlsxXmlNormalizationHelpers.NormalizeLegacyPasswordHashOrNull")
                .And.NotContain("private static string? NormalizeBase64BinaryOrNull")
                .And.NotContain("private static string? NormalizeLegacyPasswordHashOrNull");
        }
    }

    private static MemoryStream CreatePackage(
        bool includeWorkbookRelationships = true,
        string? workbookXml = null,
        string? relationshipsXml = null)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(
                archive,
                "xl/workbook.xml",
                workbookXml ??
                """
                <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main"
                          xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships">
                  <sheets>
                    <sheet name="First" sheetId="1" r:id="rIdFirst" />
                    <sheet name="Second" sheetId="2" r:id="rIdSecond" />
                  </sheets>
                </workbook>
                """);
            if (includeWorkbookRelationships)
            {
                WriteEntry(
                    archive,
                    "xl/_rels/workbook.xml.rels",
                    relationshipsXml ??
                    """
                    <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
                      <Relationship Id="rIdFirst" Type="worksheet" Target="custom/sheet-one.xml" />
                      <Relationship Id="rIdSecond" Type="worksheet" Target="worksheets/sheet2.xml" />
                    </Relationships>
                    """);
            }

            WriteEntry(
                archive,
                "xl/custom/sheet-one.xml",
                """
                <worksheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" future="retained">
                  <sheetData />
                  <protectedRanges><protectedRange name="old" sqref="A1" /></protectedRanges>
                </worksheet>
                """);
            WriteEntry(
                archive,
                "xl/worksheets/sheet2.xml",
                "<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData /></worksheet>");
        }

        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string path, string text)
    {
        var entry = archive.CreateEntry(path);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(text);
    }

    private static string ReadEntry(MemoryStream stream, string path)
    {
        stream.Position = 0;
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        using var reader = new StreamReader(archive.GetEntry(path)!.Open(), Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
