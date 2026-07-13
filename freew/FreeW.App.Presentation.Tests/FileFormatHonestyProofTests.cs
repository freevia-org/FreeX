using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class FileFormatHonestyProofTests
{
    [Fact]
    public void BuildDefaultRows_CoversUserFacingFormatTruthDeterministically()
    {
        var rows = FileFormatHonestyProof.BuildDefaultRows(includeXpsExport: true);

        rows.Should().Contain(row =>
            row.Area == FileFormatHonestyProofArea.NativeOoxml &&
            row.FormatName == "Word Document" &&
            row.PrimaryExtension == ".docx" &&
            row.CapabilityKind == DocumentFormatCapabilityKind.OpenSave &&
            row.CanOpen &&
            row.CanSave &&
            !row.CanExport &&
            row.UserFacingTruth.Contains("not compatibility exports", StringComparison.Ordinal));

        rows.Should().Contain(row =>
            row.Area == FileFormatHonestyProofArea.MacroPreservation &&
            row.FormatName == "Word Macro-Enabled Document" &&
            row.PrimaryExtension == ".docm" &&
            row.UserFacingTruth.Contains("preserve existing VBA project bytes", StringComparison.Ordinal) &&
            row.Evidence.Contains("does not inspect or execute macros", StringComparison.Ordinal));

        rows.Should().Contain(row =>
            row.Area == FileFormatHonestyProofArea.MacroPreservation &&
            row.FormatName == "Word Template" &&
            row.PrimaryExtension == ".dotx" &&
            row.UserFacingTruth.Contains("VBA project bytes are dropped", StringComparison.Ordinal) &&
            row.Evidence.Contains("drops macro parts", StringComparison.Ordinal));

        rows.Where(row => row.Area == FileFormatHonestyProofArea.TemplateSemantics)
            .Select(row => row.PrimaryExtension)
            .Should()
            .Contain(new[] { ".dotx", ".dotm", ".ott", ".dot" });

        rows.Should().Contain(row =>
            row.Area == FileFormatHonestyProofArea.TemplateSemantics &&
            row.FormatName == "OpenDocument Text Template" &&
            row.PrimaryExtension == ".ott" &&
            row.OpensAsTemplate &&
            row.Evidence.Contains("new unsaved document", StringComparison.Ordinal) &&
            row.Evidence.Contains("unsupported ODF constructs", StringComparison.OrdinalIgnoreCase));

        rows.Should().Contain(row =>
            row.Area == FileFormatHonestyProofArea.ImportOnly &&
            row.FormatName == "PDF Document" &&
            row.PrimaryExtension == ".pdf" &&
            row.CapabilityKind == DocumentFormatCapabilityKind.ImportOnly &&
            row.CanOpen &&
            !row.CanSave &&
            !row.CanExport);

        rows.Should().Contain(row =>
            row.Area == FileFormatHonestyProofArea.ExportOnly &&
            row.FormatName == "PDF Document" &&
            row.PrimaryExtension == ".pdf" &&
            row.CapabilityKind == DocumentFormatCapabilityKind.ExportOnly &&
            !row.CanOpen &&
            !row.CanSave &&
            row.CanExport);

        rows.Should().Contain(row =>
            row.Area == FileFormatHonestyProofArea.FeatureLoss &&
            row.FormatName == "Rich Text Format" &&
            row.Evidence.Contains("Advanced Word features may be simplified", StringComparison.Ordinal));
        rows.Should().Contain(row =>
            row.Area == FileFormatHonestyProofArea.FeatureLoss &&
            row.FormatName == "OpenDocument Text" &&
            row.Evidence.Contains("Unsupported ODF constructs", StringComparison.Ordinal));
        rows.Should().Contain(row =>
            row.Area == FileFormatHonestyProofArea.FeatureLoss &&
            row.FormatName == "Word 97-2003 Document" &&
            row.IsLegacy &&
            row.Evidence.Contains("Compatibility format", StringComparison.Ordinal));
        rows.Should().Contain(row =>
            row.Area == FileFormatHonestyProofArea.FeatureLoss &&
            row.FormatName == "Plain text" &&
            row.Evidence.Contains("Text-only format", StringComparison.Ordinal));
    }
}
