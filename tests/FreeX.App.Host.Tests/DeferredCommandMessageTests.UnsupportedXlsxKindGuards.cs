using FluentAssertions;
using FreeX.Core.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class DeferredCommandMessageTests
{
    [Fact]
    public void UnsupportedXlsxFeatureKinds_DoNotIncludePrinterSettings()
    {
        Enum.GetNames<XlsxUnsupportedFeatureKind>().Should().NotContain("PrinterSettings",
            "printer settings are retained and should not trigger unsupported-feature warnings");
    }

    [Fact]
    public void UnsupportedXlsxFeatureKinds_DoNotIncludeSupportedMetadataPassFeatures()
    {
        var unsupportedKindNames = Enum.GetNames<XlsxUnsupportedFeatureKind>();

        unsupportedKindNames.Should().NotContain([
            "PivotTables",
            "Slicers",
            "Timelines",
            "ExternalLinks",
            "Sparklines",
            "StructuredTables"
        ], "these XLSX features now load/save or retain native metadata and should not trigger stale unsupported-feature warnings");
    }
}
