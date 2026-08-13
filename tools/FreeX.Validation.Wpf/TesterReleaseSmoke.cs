using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Free.Shared.AppServices;
using Free.Shared.Ribbon;
using FreeX.App.Host;
using FreeX.App.UI;
using FreeX.Core.Model;
using FreeX.Ribbon.Definitions;

namespace FreeX.Validation.Wpf;

internal sealed record TesterReleaseSmokeReport(
    bool Success,
    int ActionableRibbonCommandCount,
    int RibbonHandlerCount,
    bool BorderPixelSnapPassed,
    IReadOnlyList<string> Errors);

internal static class TesterReleaseSmoke
{
    internal const string CommandLineSwitch = "--tester-release-smoke";

    internal static bool TryRun(IReadOnlyList<string> startupArgs, out int exitCode)
    {
        var switchIndex = -1;
        for (var i = 0; i < startupArgs.Count; i++)
        {
            if (string.Equals(startupArgs[i], CommandLineSwitch, StringComparison.OrdinalIgnoreCase))
            {
                switchIndex = i;
                break;
            }
        }

        if (switchIndex < 0)
        {
            exitCode = 0;
            return false;
        }

        var reportPath = switchIndex + 1 < startupArgs.Count
            ? startupArgs[switchIndex + 1]
            : "tester-release-smoke.json";
        var report = Validate();
        var fullReportPath = Path.GetFullPath(reportPath);
        JsonArtifactIO.Write(
            fullReportPath,
            report,
            JsonArtifactIO.CreateSerializerOptions(camelCase: false));
        exitCode = report.Success ? 0 : 1;
        return true;
    }

    internal static TesterReleaseSmokeReport Validate()
    {
        var errors = new List<string>();
        var actionableCommandIds = EnumerateActionableCommandIds().ToArray();
        var missingHandlers = actionableCommandIds
            .Where(id => !FreeXRibbonHandlerMap.Handlers.ContainsKey(id))
            .ToArray();
        if (missingHandlers.Length > 0)
            errors.Add($"Ribbon commands without handlers: {string.Join(", ", missingHandlers)}");

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        var unresolvedMethods = FreeXRibbonHandlerMap.Handlers
            .Where(pair => typeof(MainWindow).GetMethod(pair.Value, flags) is null)
            .Select(pair => $"{pair.Key} -> {pair.Value}")
            .OrderBy(value => value, StringComparer.Ordinal)
            .ToArray();
        if (unresolvedMethods.Length > 0)
            errors.Add($"Ribbon handlers without MainWindow methods: {string.Join(", ", unresolvedMethods)}");

        var borderPixelSnapPassed = ValidateBorderPixelSnapping(errors);
        return new(
            errors.Count == 0,
            actionableCommandIds.Length,
            FreeXRibbonHandlerMap.Handlers.Count,
            borderPixelSnapPassed,
            errors);
    }

    private static IEnumerable<string> EnumerateActionableCommandIds()
    {
        var definition = FreeXRibbon.Build();
        var comboIds = definition.Tabs
            .SelectMany(tab => tab.Groups)
            .SelectMany(group => group.Controls)
            .OfType<RibbonComboBox>()
            .Select(combo => combo.CommandId.Value)
            .ToHashSet(StringComparer.Ordinal);
        return definition.Tabs
            .SelectMany(tab => tab.Groups)
            .SelectMany(group => group.Controls)
            .SelectMany(EnumerateCommandIds)
            .Where(id => !comboIds.Contains(id))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(id => id, StringComparer.Ordinal);
    }

    private static IEnumerable<string> EnumerateCommandIds(RibbonControl control)
    {
        if (!string.IsNullOrEmpty(control.CommandId.Value))
            yield return control.CommandId.Value;
        var menu = control switch
        {
            RibbonSplitButton split => split.Menu,
            RibbonDropdown dropdown => dropdown.Menu,
            _ => null,
        };
        if (menu is null)
            yield break;
        foreach (var id in EnumerateMenuCommandIds(menu.Items))
            yield return id;
    }

    private static IEnumerable<string> EnumerateMenuCommandIds(IReadOnlyList<RibbonMenuItem> items)
    {
        foreach (var item in items)
        {
            if (item.CommandId is { } id && !string.IsNullOrEmpty(id.Value))
                yield return id.Value;
            foreach (var childId in EnumerateMenuCommandIds(item.Children))
                yield return childId;
        }
    }

    private static bool ValidateBorderPixelSnapping(List<string> errors)
    {
        try
        {
            var drawBorderEdge = typeof(GridView).GetMethod(
                "DrawBorderEdge",
                BindingFlags.NonPublic | BindingFlags.Static)
                ?? throw new MissingMethodException(nameof(GridView), "DrawBorderEdge");
            var border = new CellBorder(BorderStyle.Thin, CellColor.Black);
            foreach (var scale in new[] { 1.0, 1.25, 1.5 })
            {
                var visual = new DrawingVisual();
                using (var context = visual.RenderOpen())
                {
                    foreach (var y in new[] { 10.0, 15.5 })
                        drawBorderEdge.Invoke(
                            null,
                            [context, border, new Point(10, y), new Point(90, y), null, null, scale]);
                }

                var bitmap = new RenderTargetBitmap(
                    (int)Math.Ceiling(100 * scale),
                    (int)Math.Ceiling(40 * scale),
                    96 * scale,
                    96 * scale,
                    PixelFormats.Pbgra32);
                bitmap.Render(visual);
                var x = (int)Math.Round(50 * scale);
                var counts = new[] { 10.0, 15.5 }
                    .Select(y => CountPaintedRowsNear(bitmap, x, (int)Math.Round(y * scale)))
                    .ToArray();
                if (counts.Any(count => count != 1))
                {
                    errors.Add(
                        $"Thin borders were not one device pixel at scale {scale}: {string.Join(", ", counts)}");
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            errors.Add($"Border pixel-snap validation failed: {ex}");
            return false;
        }
    }

    private static int CountPaintedRowsNear(RenderTargetBitmap bitmap, int x, int centerY, int radius = 3) =>
        Enumerable.Range(centerY - radius, radius * 2 + 1)
            .Count(y => y >= 0 && y < bitmap.PixelHeight && IsPaintedPixel(bitmap, x, y));

    private static bool IsPaintedPixel(RenderTargetBitmap bitmap, int x, int y)
    {
        var pixels = new byte[4];
        bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixels, 4, 0);
        return pixels[3] > 10;
    }
}
