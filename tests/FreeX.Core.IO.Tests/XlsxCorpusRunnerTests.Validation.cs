using FreeX.Core.IO;
using FreeX.Core.Model;
using FluentAssertions;
using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;

namespace FreeX.Core.IO.Tests;

public partial class XlsxCorpusRunnerTests
{
    [Fact]
    public void GeneratedCorpusRows_IncludeDataValidationMessages()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-pass")
            .Where(row => row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains("data-validation"))
            .ToArray();

        rows.Should().NotBeEmpty("validation prompt/error message metadata should be covered by deterministic generated fixtures");
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreate(row.Id));

        rows.Select(row => XlsxCorpusFixtureFactory.Create(row.Id))
            .SelectMany(workbook => workbook.Sheets)
            .SelectMany(sheet => sheet.DataValidations)
            .Should().Contain(validation =>
                !string.IsNullOrWhiteSpace(validation.ErrorTitle) &&
                !string.IsNullOrWhiteSpace(validation.ErrorMessage) &&
                !string.IsNullOrWhiteSpace(validation.PromptTitle) &&
                !string.IsNullOrWhiteSpace(validation.PromptMessage));
    }

    [Fact]
    public void GeneratedCorpusRows_IncludeMultiAreaDataValidationCoverage()
    {
        var rows = ReadManifestRows()
            .Where(row => row.SourceType == "generated")
            .Where(row => row.ExpectedStatus == "supported-pass")
            .Where(row => row.FeatureTags.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Contains("multi-area-validation"))
            .ToArray();

        rows.Should().ContainSingle("Excel data-validation sqref can cover discontiguous ranges and should not narrow to the first range");
        rows.Should().OnlyContain(row => XlsxCorpusFixtureFactory.CanCreate(row.Id));

        rows.Select(row => XlsxCorpusFixtureFactory.Create(row.Id))
            .SelectMany(workbook => workbook.Sheets)
            .SelectMany(sheet => sheet.DataValidations)
            .Should().Contain(validation => validation.AdditionalRanges.Count > 0);
    }


    [Fact]
    public void GeneratedDvCountPackage_RetainsSemanticDataValidationRulesAfterModelEdit()
    {
        using var package = XlsxCorpusFixtureFactory.CreateKnownGapRetentionPackage("generated-dv-count-package-003");
        var before = CaptureDataValidationPackageSummary(package);
        var expectedRules = new[]
        {
            new DataValidationRuleXmlSummary("list", "", "B2:B10", "A,B,C", ""),
            new DataValidationRuleXmlSummary("whole", "between", "C2:C10", "1", "100"),
            new DataValidationRuleXmlSummary("decimal", "greaterThan", "D2:D10", "0", ""),
            new DataValidationRuleXmlSummary("date", "greaterThanOrEqual", "E2:E10", "DATE(2026,1,1)", ""),
            new DataValidationRuleXmlSummary("time", "between", "F2:F10", "TIME(8,0,0)", "TIME(18,0,0)"),
            new DataValidationRuleXmlSummary("textLength", "lessThanOrEqual", "G2:G10", "50", ""),
            new DataValidationRuleXmlSummary("custom", "", "H2:H10", "LEN(H2)>0", ""),
            new DataValidationRuleXmlSummary("list", "", "I2:I10", "Yes,No", ""),
            new DataValidationRuleXmlSummary("whole", "greaterThan", "J2:J10", "0", ""),
            new DataValidationRuleXmlSummary("decimal", "lessThan", "K2:K10", "1000", "")
        };

        before.CountAttribute.Should().Be("10", "generated-dv-count-package-003 declares ten dataValidation records");
        before.Rules.Should().Equal(expectedRules, "the fixture should exercise list, numeric, date/time, text-length, and custom validation semantics");

        package.Position = 0;
        var adapter = new XlsxFileAdapter();
        var workbook = adapter.Load(package);
        workbook.GetSheetAt(0).SetCell(new CellAddress(workbook.GetSheetAt(0).Id, 12, 1), new TextValue("freex-dv-semantic-edit"));

        using var saved = new MemoryStream();
        adapter.Save(workbook, saved);
        saved.Position = 0;
        AssertPackageHealth(saved, "generated-dv-count-package-003");

        var after = CaptureDataValidationPackageSummary(saved);
        after.Should().BeEquivalentTo(
            before,
            options => options.WithStrictOrdering(),
            "data-validation rule type/operator/formula/sqref semantics should survive ordinary model edits");
    }

}
