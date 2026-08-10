using FluentAssertions;

namespace FreeX.App.Services.Tests;

public sealed class PortableCoreDedupOwnershipSourceTests
{
    [Fact]
    public void SisterApps_UseSharedFileStatusTextFactory()
    {
        var shared = Read("shared", "Free.Shared.AppServices", "SisterAppFileTextPlanner.cs");
        var freeW = Read("freew", "FreeW.App.Presentation", "Dialogs", "FreeWFileTextResources.cs");
        var freeP = Read("freep", "FreeP.App.Presentation", "PresentationFileTextResources.cs");

        shared.Should().Contain("CreateStatusText(Func<string, string> getText)");
        freeW.Should().Contain("SisterAppFileTextPlanner.CreateStatusText(Loc.Get)");
        freeP.Should().Contain("SisterAppFileTextPlanner.CreateStatusText(Loc.Get)");
        (freeW + freeP).Should().NotContain("CommandUnavailableFormat: Loc.Get");
    }

    [Fact]
    public void PasswordAdapters_UseSharedSha256Storage()
    {
        var shared = Read("shared", "Free.Shared.IO", "Sha256PasswordStorage.cs");
        var native = Read("shared", "Free.Shared.Opc", "NativePasswordHelper.cs");
        var model = Read("src", "FreeX.Core.Model", "ProtectionPasswordHelper.cs");

        shared.Should().Contain("SHA256.HashData");
        native.Should().Contain("Sha256PasswordStorage.Encode");
        model.Should().Contain("Sha256PasswordStorage.Encode");
        (native + model).Should().NotContain("IsStoredSha256Hash");
        (native + model).Should().NotContain("Convert.FromHexString");
    }

    [Fact]
    public void XlsxExtensionListWrappers_DelegateToOneNormalizer()
    {
        var shared = Read("src", "FreeX.Core.IO", "XlsxExtensionListNormalizer.cs");
        var workbook = Read("src", "FreeX.Core.IO", "XlsxWorkbookExtensionListNormalizer.cs");
        var worksheet = Read("src", "FreeX.Core.IO", "XlsxWorksheetExtensionListNormalizer.cs");

        shared.Should().Contain("private static bool NormalizeUri");
        workbook.Should().Contain("XlsxExtensionListNormalizer.NormalizeRoot");
        worksheet.Should().Contain("XlsxExtensionListNormalizer.NormalizeRoot");
        (workbook + worksheet).Should().NotContain("private static bool NormalizeUri");
    }

    [Fact]
    public void WorksheetMetadataPreserver_UsesSharedElementIdentity()
    {
        var shared = Read("shared", "Free.Shared.Opc", "XlsxNativeXmlMerger.cs");
        var worksheet = Read("src", "FreeX.Core.IO", "XlsxWorksheetMetadataPreserver.MergeHelpers.cs");

        shared.Should().Contain("internal static string GetElementIdentityKey");
        worksheet.Should().Contain("XlsxNativeXmlMerger.GetElementIdentityKey(element)");
        worksheet.Should().NotContain("element.Attribute(\"pane\")");
    }

    [Fact]
    public void FormulaRangeNavigation_UsesWorksheetNavigationPlanner()
    {
        var source = Read(
            "src",
            "FreeX.App.Presentation",
            "FormulaBar",
            "FormulaRangeEntryPlanner.cs");

        source.Should().Contain("ExcelWorksheetNavigationPlanner.FindVerticalDataBoundary");
        source.Should().Contain("ExcelWorksheetNavigationPlanner.FindHorizontalDataBoundary");
        source.Should().Contain("ExcelWorksheetNavigationPlanner.GetCtrlEndCell");
        source.Should().NotContain("private static bool CellHasData");
    }

    [Fact]
    public void XlsxSqrefConsumers_UseOneRangeTokenParser()
    {
        var parser = Read("src", "FreeX.Core.IO", "XlsxSqrefParser.cs");
        var consumers = string.Concat(
            Read("src", "FreeX.Core.IO", "XlsxAllowEditRangeMapper.cs"),
            Read("src", "FreeX.Core.IO", "XlsxDataValidationNativeMetadataMapper.cs"),
            Read("src", "FreeX.Core.IO", "XlsxWorksheetMetadataPreserver.ProtectedRanges.cs"),
            Read("src", "FreeX.Core.IO", "XlsxX14DataValidationReader.cs"));

        parser.Should().Contain("TryParseRangeToken");
        consumers.Should().Contain("XlsxSqrefParser.TryParseRangeToken");
        consumers.Should().NotContain("private static bool TryParseWholeColumnOrRowSqrefRange");
        consumers.Should().NotContain("private static bool IsAsciiDigitsOnly");
    }

    [Fact]
    public void ChartCommandsAndIo_UseModelFormatPresencePolicy()
    {
        var policy = Read("src", "FreeX.Core.Model", "ChartFormatPresence.cs");
        var command = Read("src", "FreeX.Core.Commands", "SetChartLayoutCommand.Support.cs");
        var commandOptions = Read("src", "FreeX.Core.Commands", "SetChartLayoutCommand.ApplyOptions.cs");
        var nativeJson = Read("src", "FreeX.Core.IO", "NativeJsonAdapter.ChartSanitization.cs");
        var xlsxWriter = Read("src", "FreeX.Core.IO", "XlsxChartXmlWriter.SeriesFormatting.cs");

        policy.Should().Contain("HasSeriesFormatting");
        policy.Should().Contain("includeLayoutAndCustomText");
        policy.Should().Contain("includeDeletion");
        (commandOptions + nativeJson + xlsxWriter).Should().Contain("ChartFormatPresence.HasPointDataLabelFormatting");
        command.Should().NotContain("private static bool HasPointDataLabelFormatting");
        nativeJson.Should().NotContain("private static bool HasSeriesFormatting");
        xlsxWriter.Should().NotContain("private static bool HasSeriesDataLabelFormatting");
    }

    [Fact]
    public void LegacyAndSpreadsheetXmlNames_UseOneReferenceParser()
    {
        var parser = Read("src", "FreeX.Core.IO", "WorkbookNamedRangeReferenceParser.cs");
        var legacy = Read("src", "FreeX.Core.IO", "LegacyXlsFileAdapter.cs");
        var spreadsheetXml = Read("src", "FreeX.Core.IO", "SpreadsheetXmlFileAdapter.Names.cs");

        parser.Should().Contain("TrySplitSheetQualifiedReference");
        legacy.Should().Contain("WorkbookNamedRangeReferenceParser.TryParse(workbook, text, out range)");
        spreadsheetXml.Should().Contain("WorkbookNamedRangeReferenceParser.TryParse(workbook, text, out range)");
        (legacy + spreadsheetXml).Should().NotContain("private static bool TryParseA1Part");
        (legacy + spreadsheetXml).Should().NotContain("private static bool TrySplitSheetQualifiedReference");
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(RepositoryFileLocator.Find(parts));
}
