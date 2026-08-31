using System.IO.Compression;
using System.Xml.Linq;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;

namespace FreeX.Core.IO.Tests;

public sealed class XlsxDataValidationXmlCodecTests
{
    private static readonly XNamespace WorksheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace X14Ns = "http://schemas.microsoft.com/office/spreadsheetml/2009/9/main";
    private static readonly XNamespace XmNs = "http://schemas.microsoft.com/office/excel/2006/main";

    [Theory]
    [InlineData(DvType.Any, "none", false)]
    [InlineData(DvType.WholeNumber, "whole", true)]
    [InlineData(DvType.Decimal, "decimal", true)]
    [InlineData(DvType.List, "list", false)]
    [InlineData(DvType.Date, "date", true)]
    [InlineData(DvType.Time, "time", true)]
    [InlineData(DvType.TextLength, "textLength", true)]
    [InlineData(DvType.Custom, "custom", false)]
    [InlineData((DvType)int.MaxValue, "none", false)]
    public void TypeTokensAndOperatorRequirement_AreCanonical(DvType type, string token, bool requiresOperator)
    {
        XlsxDataValidationXmlCodec.FormatType(type).Should().Be(token);
        XlsxDataValidationXmlCodec.RequiresOperator(type).Should().Be(requiresOperator);
    }

    [Theory]
    [InlineData(DvOperator.Between, "between")]
    [InlineData(DvOperator.NotBetween, "notBetween")]
    [InlineData(DvOperator.Equal, "equal")]
    [InlineData(DvOperator.NotEqual, "notEqual")]
    [InlineData(DvOperator.GreaterThan, "greaterThan")]
    [InlineData(DvOperator.LessThan, "lessThan")]
    [InlineData(DvOperator.GreaterThanOrEqual, "greaterThanOrEqual")]
    [InlineData(DvOperator.LessThanOrEqual, "lessThanOrEqual")]
    [InlineData((DvOperator)int.MaxValue, "between")]
    public void OperatorTokens_AreCanonical(DvOperator op, string token) =>
        XlsxDataValidationXmlCodec.FormatOperator(op).Should().Be(token);

    [Theory]
    [InlineData(DvAlertStyle.Stop, "stop")]
    [InlineData(DvAlertStyle.Warning, "warning")]
    [InlineData(DvAlertStyle.Information, "information")]
    [InlineData((DvAlertStyle)int.MaxValue, "stop")]
    public void AlertStyleTokens_AreCanonical(DvAlertStyle style, string token) =>
        XlsxDataValidationXmlCodec.FormatAlertStyle(style).Should().Be(token);

    [Fact]
    public void LegacyAndX14Writers_EmitIdenticalModeledTokens()
    {
        var workbook = new Workbook("DataValidationTokenParity");
        var sheet = workbook.AddSheet("Sheet1");
        sheet.DataValidations.Add(CreateValidation(sheet, "A1", isX14: false));
        sheet.DataValidations.Add(CreateValidation(sheet, "B1", isX14: true));

        using var saved = XlsxPackageTestHelper.SaveWorkbook(workbook);
        using var archive = new ZipArchive(saved, ZipArchiveMode.Read, leaveOpen: true);
        using var worksheetStream = archive.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        var worksheet = XDocument.Load(worksheetStream);

        var legacy = worksheet.Root!
            .Element(WorksheetNs + "dataValidations")!
            .Elements(WorksheetNs + "dataValidation")
            .Single(element => (string?)element.Attribute("sqref") == "A1");
        var x14 = worksheet
            .Descendants(X14Ns + "dataValidation")
            .Single(element => (string?)element.Element(XmNs + "sqref") == "B1");

        foreach (var attributeName in new[] { "type", "operator", "errorStyle" })
        {
            ((string?)x14.Attribute(attributeName)).Should().Be(
                (string?)legacy.Attribute(attributeName),
                $"legacy and x14 writers must share the canonical {attributeName} token");
        }
    }

    [Fact]
    public void Writers_DelegateTokenMappingsToCanonicalCodec()
    {
        var x14Writer = TestWorkspaceFiles.ReadCoreIoSource("XlsxX14DataValidationWriter.cs");
        var nativeWriter = TestWorkspaceFiles.ReadCoreIoSource("XlsxDataValidationNativeMetadataMapper.cs");

        x14Writer.Should().Contain("XlsxDataValidationXmlCodec.");
        nativeWriter.Should().Contain("XlsxDataValidationXmlCodec.");
        x14Writer.Should().NotContain("private static bool ShouldWriteOperator");
        x14Writer.Should().NotContain("private static string ToTypeString");
        x14Writer.Should().NotContain("private static string ToOperatorString");
        x14Writer.Should().NotContain("private static string ToAlertStyleString");
        nativeWriter.Should().NotContain("private static bool ShouldWriteOperator");
        nativeWriter.Should().NotContain("private static string ToDataValidationType");
        nativeWriter.Should().NotContain("private static string ToDataValidationOperator");
        nativeWriter.Should().NotContain("private static string ToDataValidationAlertStyle");
    }

    private static DataValidation CreateValidation(Sheet sheet, string address, bool isX14)
    {
        var cell = CellAddress.Parse(address, sheet.Id);
        return new DataValidation
        {
            AppliesTo = new GridRange(cell, cell),
            Type = DvType.WholeNumber,
            Operator = DvOperator.GreaterThan,
            AlertStyle = DvAlertStyle.Warning,
            Formula1 = "1",
            IsX14 = isX14,
            NativeAttributes = isX14 ? null : new Dictionary<string, string> { ["imeMode"] = "on" },
        };
    }
}
