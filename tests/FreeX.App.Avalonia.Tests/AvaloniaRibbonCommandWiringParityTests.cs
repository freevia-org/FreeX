using System.Reflection;
using System.Text.RegularExpressions;
using Avalonia.Headless;
using Free.Shared.Ribbon;
using FreeX.App.Avalonia.Ribbon;
using FreeX.Ribbon.Definitions;

namespace FreeX.App.Avalonia.Tests;

/// <summary>
/// WPF/Avalonia ribbon command-wiring parity: every canonical command id the WPF host wires to a handler
/// (<c>FreeXRibbonHandlerMap.g.cs</c>) must also be a live (enabled) command in the macOS/Avalonia ribbon.
/// </summary>
[Collection("AvaloniaHeadless")]
public sealed class AvaloniaRibbonCommandWiringParityTests
{
    private static readonly HeadlessUnitTestSession Session =
        HeadlessUnitTestSession.GetOrStartForAssembly(typeof(RibbonHeadlessApp).Assembly);

    private static string Root => TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeX.slnx");

    private static IReadOnlySet<string> WpfWiredCommandIds()
    {
        var text = File.ReadAllText(Path.Combine(Root, "src", "FreeX.App.Host", "Ribbon", "FreeXRibbonHandlerMap.g.cs"));
        var constants = typeof(FreeXRibbonCommandIds)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in Regex.Matches(text, @"^\s*\[(?:FreeXRibbonCommandIds\.(?<c>\w+)|""(?<s>(?:[^""\\]|\\.)*)"")\]\s*=\s*new\(", RegexOptions.Multiline))
        {
            ids.Add(match.Groups["c"].Success
                ? constants[match.Groups["c"].Value]
                : Regex.Unescape(match.Groups["s"].Value));
        }

        return ids;
    }

    internal static IReadOnlySet<string> AvaloniaRegisteredIds(MainWindow window)
    {
        var field = typeof(MainWindow).GetField("_ribbonCommandRegistry", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var registry = (IRibbonCommandRegistry)field.GetValue(window)!;
        return FreeXRibbonCommandCatalog.All
            .Where(id => registry.TryGet(id, out _))
            .Select(id => id.Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public async Task EveryWpfWiredRibbonCommand_IsRegisteredInAvaloniaRibbon()
    {
        string[] missing = [];
        WpfWiredCommandIds().Count.Should().BeGreaterThan(500, "the WPF handler map must parse");
        await Session.Dispatch(() =>
        {
            var window = new MainWindow([]);
            try
            {
                var registered = AvaloniaRegisteredIds(window);
                missing = WpfWiredCommandIds()
                    .Where(id => FreeXRibbonCommandCatalog.TryGet(id, out _) && !registered.Contains(id))
                    .Order(StringComparer.Ordinal)
                    .ToArray();
            }
            finally
            {
                window.AllowCloseWithoutDirtyPromptForParityCapture();
                window.Close();
            }
        }, CancellationToken.None);

        missing.Should().BeEmpty("WPF wires these ribbon commands but the Avalonia ribbon leaves them disabled");
    }
}
