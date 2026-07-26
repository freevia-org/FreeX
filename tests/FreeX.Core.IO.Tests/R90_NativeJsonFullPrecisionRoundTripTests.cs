using System.Text.Json;
using FluentAssertions;
using FreeX.Core.IO;
using FreeX.Core.Model;
using Xunit;

namespace FreeX.Core.IO.Tests;

/// <summary>
/// Regression coverage for round 90 finding R90-io-workbook-calc-settings-5-1: FullPrecision
/// (precision-as-displayed, XLSX calcPr/@fullPrecision) was not serialized by the native .fxl
/// adapter at all -- WorkbookDto had no FullPrecision property, so a workbook loaded from an
/// Excel-authored .xlsx with "Set precision as displayed" enabled (FullPrecision=false) would
/// silently revert to Workbook's default (true) after a save-as-.fxl/reload round-trip, silently
/// re-enabling full floating-point precision for future recalculations instead of the permanently-
/// rounded values Excel had produced.
///
/// Exercised through the real product entry point: NativeJsonAdapter.Save/Load (the same round-trip
/// every other calc-option field in this class is validated through, e.g.
/// NativeJsonFidelityInventoryTests.NativeJsonAdapter_RoundTrips_WorkbookScalarFields).
/// </summary>
public sealed class R90_NativeJsonFullPrecisionRoundTripTests
{
    private static Workbook RoundTrip(Workbook source)
    {
        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(source, stream);
        stream.Position = 0;
        return adapter.Load(stream);
    }

    [Fact]
    public void RoundTrip_FullPrecisionFalse_PersistsAcrossSaveAndLoad()
    {
        // Precision-as-displayed enabled (Excel File > Options > Advanced > "Set precision as
        // displayed"), as loaded from an XLSX with calcPr/@fullPrecision="0".
        var workbook = new Workbook("PrecisionAsDisplayed");
        workbook.AddSheet("Sheet1");
        workbook.FullPrecision = false;

        using var stream = new MemoryStream();
        var adapter = new NativeJsonAdapter();
        adapter.Save(workbook, stream);

        // The DTO must actually carry the field (not merely have the model happen to match by
        // coincidence) -- assert directly on the serialized JSON.
        using (var document = JsonDocument.Parse(stream.ToArray()))
        {
            document.RootElement.TryGetProperty("FullPrecision", out var property).Should().BeTrue(
                "WorkbookDto must serialize FullPrecision so it is not silently dropped on a native round-trip");
            property.GetBoolean().Should().BeFalse();
        }

        stream.Position = 0;
        var loaded = adapter.Load(stream);
        loaded.FullPrecision.Should().BeFalse(
            "a workbook saved with precision-as-displayed must reload with the same setting, not revert to the full-precision default");
    }

    [Fact]
    public void RoundTrip_FullPrecisionTrue_RemainsTrue()
    {
        // No-regression sibling: the (much more common) full-precision default must still
        // round-trip as true, exactly as it did before this fix (when the field was simply always
        // absent/defaulted).
        var workbook = new Workbook("FullPrecisionDefault");
        workbook.AddSheet("Sheet1");
        workbook.FullPrecision.Should().BeTrue("sanity: Workbook's own default");

        var loaded = RoundTrip(workbook);

        loaded.FullPrecision.Should().BeTrue();
    }
}
