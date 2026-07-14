using FluentAssertions;
using FreeX.Core.Model;

namespace FreeX.App.Services.Tests;

public sealed class ExportOptionsDialogSurfacePlannerTests
{
    [Fact]
    public void CaptureSize_MatchesSharedDialogVisualEvidenceContract()
    {
        ExportOptionsDialogSurfacePlanner.CaptureWidth.Should().Be(430);
        ExportOptionsDialogSurfacePlanner.CaptureHeight.Should().Be(552);
    }

    [Fact]
    public void CreateFormatAvailability_EnablesPdfOnlyControlsForPdf()
    {
        ExportOptionsDialogSurfacePlanner.CreateFormatAvailability(ExportFileFormat.Pdf)
            .Should()
            .Be(new ExportOptionsDialogFormatAvailability(
                PdfBookmarksEnabled: true,
                PdfInitialViewEnabled: true,
                PdfOpenModeEnabled: true,
                PdfLanguageEnabled: true,
                PdfBitmapTextEnabled: true,
                MinimumSizeEnabled: true));
    }

    [Fact]
    public void CreateFormatAvailability_DisablesPdfOnlyControlsForXps()
    {
        ExportOptionsDialogSurfacePlanner.CreateFormatAvailability(ExportFileFormat.Xps)
            .Should()
            .Be(new ExportOptionsDialogFormatAvailability(
                PdfBookmarksEnabled: false,
                PdfInitialViewEnabled: false,
                PdfOpenModeEnabled: false,
                PdfLanguageEnabled: false,
                PdfBitmapTextEnabled: false,
                MinimumSizeEnabled: false));
    }

    [Theory]
    [InlineData("from must be less than to", "4", ExportOptionsDialogFocusTarget.ToPage)]
    [InlineData("whole numbers only", "2", ExportOptionsDialogFocusTarget.ToPage)]
    [InlineData("whole numbers only", "0", ExportOptionsDialogFocusTarget.FromPage)]
    [InlineData("whole numbers only", "x", ExportOptionsDialogFocusTarget.FromPage)]
    public void ResolveInvalidPageRangeFocusTarget_SelectsExpectedField(
        string error,
        string fromPageText,
        ExportOptionsDialogFocusTarget expected)
    {
        ExportOptionsDialogSurfacePlanner.ResolveInvalidPageRangeFocusTarget(
                error,
                fromPageText,
                fromLessThanToError: "from must be less than to")
            .Should()
            .Be(expected);
    }
}
