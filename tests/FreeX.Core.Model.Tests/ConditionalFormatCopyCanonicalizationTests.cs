using FluentAssertions;

namespace FreeX.Core.Model.Tests;

public sealed class ConditionalFormatCopyCanonicalizationTests
{
    [Fact]
    public void SheetClone_PreservesAdvancedFieldsWhileRemappingIdentityRangesAndFormula()
    {
        var workbook = new Workbook("Book");
        var sourceSheet = workbook.AddSheet("Source");
        var source = new ConditionalFormat
        {
            AppliesTo = Range(sourceSheet.Id, 1, 1, 4, 2),
            AdditionalRanges = [Range(sourceSheet.Id, 6, 3, 8, 4)],
            RuleType = CfRuleType.DataBar,
            FormulaText = "Source!A1>0",
            FormatIfTrue = new CellStyle { Bold = true },
            MinColorSource = new CfColorStopSource(1, 0.1),
            MidColorSource = new CfColorStopSource(2, 0.2),
            MaxColorSource = new CfColorStopSource(3, 0.3),
            DataBarColorSource = new CfColorStopSource(4, 0.4),
            DataBarBorderColor = new RgbColor(10, 20, 30),
            DataBarNegativeFillSameAsPositive = true,
            DataBarNegativeBorderSameAsPositive = true,
            DataBarDirection = "rightToLeft"
        };
        source.IconSetThresholds.Add(new CfThresholdModel(CfThresholdType.Percent, "25"));
        source.IconOverrides.Add(new CfIconOverride("3Arrows", 2));
        sourceSheet.ConditionalFormats.Add(source);
        var copyId = SheetId.New();

        var copy = sourceSheet.Clone(copyId, "Copy");

        var clone = copy.ConditionalFormats.Should().ContainSingle().Subject;
        clone.Id.Should().NotBe(source.Id);
        clone.AppliesTo.Should().Be(Range(copyId, 1, 1, 4, 2));
        clone.AdditionalRanges.Should().Equal(Range(copyId, 6, 3, 8, 4));
        clone.FormulaText.Should().Be("Copy!A1>0");
        clone.MinColorSource.Should().Be(source.MinColorSource);
        clone.MidColorSource.Should().Be(source.MidColorSource);
        clone.MaxColorSource.Should().Be(source.MaxColorSource);
        clone.DataBarColorSource.Should().Be(source.DataBarColorSource);
        clone.DataBarBorderColor.Should().Be(source.DataBarBorderColor);
        clone.DataBarNegativeFillSameAsPositive.Should().BeTrue();
        clone.DataBarNegativeBorderSameAsPositive.Should().BeTrue();
        clone.DataBarDirection.Should().Be("rightToLeft");
        clone.FormatIfTrue.Should().NotBeSameAs(source.FormatIfTrue);
        clone.IconSetThresholds.Should().NotBeSameAs(source.IconSetThresholds);
        clone.IconOverrides.Should().NotBeSameAs(source.IconOverrides);
        clone.AdditionalRanges.Should().NotBeSameAs(source.AdditionalRanges);
    }

    [Fact]
    public void CopyPaths_DelegateFieldOwnershipToConditionalFormatClone()
    {
        var sheetClone = ModelSourceTestSupport.ReadModelSource("Sheet.Clone.cs");
        var paste = ModelSourceTestSupport.ReadCommandsSource("PasteConditionalFormatsCommand.cs");
        var xlsx = TestWorkspaceFileLocator.ReadAllText(
            "src", "FreeX.Core.IO", "XlsxFileAdapter.ConditionalFormats.cs");

        sheetClone.Should().Contain("cf.Clone(Guid.NewGuid())");
        sheetClone.Should().NotContain("var clonedFormat = new ConditionalFormat");
        paste.Should().Contain("source.Clone(Guid.NewGuid())");
        paste.Should().NotContain("var clone = new ConditionalFormat");
        xlsx.Should().Contain("private static ConditionalFormat RemapConditionalFormat");
        xlsx.Should().Contain("var format = source.Clone();");
    }

    private static GridRange Range(
        SheetId sheetId,
        uint startRow,
        uint startColumn,
        uint endRow,
        uint endColumn) =>
        new(
            new CellAddress(sheetId, startRow, startColumn),
            new CellAddress(sheetId, endRow, endColumn));
}
