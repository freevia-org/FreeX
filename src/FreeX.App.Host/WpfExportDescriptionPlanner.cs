using FreeX.App.Services;

namespace FreeX.App.Host;

internal static class WpfExportDescriptionPlanner
{
    public static string PdfFallbackMessage =>
        ExportDescriptionPlanner.PdfFallbackMessage(WpfExportPlannerTextResolver.Instance);

    public static string DescribeOptions(ExportOptions options) =>
        ExportDescriptionPlanner.DescribeOptions(options, WpfExportPlannerTextResolver.Instance);

    public static string DescribeOptions(ExportOptions options, ExportFormat format) =>
        ExportDescriptionPlanner.DescribeOptions(options, format, WpfExportPlannerTextResolver.Instance);

    public static string DescribeRequest(ExportRequest request) =>
        ExportDescriptionPlanner.DescribeRequest(request, WpfExportPlannerTextResolver.Instance);
}
