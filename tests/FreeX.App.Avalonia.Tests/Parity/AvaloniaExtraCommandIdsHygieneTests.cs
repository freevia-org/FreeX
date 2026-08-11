using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using FreeX.App.Avalonia.Ribbon;
using FreeX.App.Presentation.PageLayout;
using FreeX.App.Presentation.Ribbon;

using Xunit;

namespace FreeX.App.Avalonia.Tests.Parity;

/// <summary>
/// Keeps <see cref="FreeXRibbonCommandIdentityCatalog.RawCanonicalAvaloniaIds"/> in lock-step with the actual raw-canonical
/// <c>ExtraCommands</c> keys wired in the Avalonia <c>MainWindow</c> source. The functional parity matrix
/// trusts <c>RawCanonical</c> as the (UI-instantiation-free) record of the shell's raw-canonical bindings;
/// this guard reads the MainWindow partial-class sources and asserts the declared set is exactly the set of
/// non-dotted dictionary keys those files assign — so a future wiring change can never quietly desync the
/// matrix from reality.
/// </summary>
public sealed class RawCanonicalCommandIdsHygieneTests
{
    [Fact]
    public void RawCanonical_MatchesLiteralExtraCommandKeysInSource()
    {
        var keys = ExtractRawCanonicalKeysFromSource();

        var declaredOnly = FreeXRibbonCommandIdentityCatalog.RawCanonicalAvaloniaIds.Except(keys).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var sourceOnly = keys.Except(FreeXRibbonCommandIdentityCatalog.RawCanonicalAvaloniaIds).OrderBy(x => x, StringComparer.Ordinal).ToArray();

        Assert.True(declaredOnly.Length == 0 && sourceOnly.Length == 0,
            "FreeXRibbonCommandIdentityCatalog.RawCanonicalAvaloniaIds has drifted from the MainWindow ExtraCommands keys."
            + Environment.NewLine + "Declared but not in source: " + string.Join(", ", declaredOnly)
            + Environment.NewLine + "In source but not declared: " + string.Join(", ", sourceOnly));
    }

    private static ISet<string> ExtractRawCanonicalKeysFromSource()
    {
        var root = FunctionalParityMatrix.RepoRoot();
        var dir = Path.Combine(root, "src", "FreeX.App.Avalonia");
        var files = new[] { "MainWindow.cs", "MainWindow.ContextualTabs.cs", "MainWindow.HomeBorders.cs" }
            .Select(f => Path.Combine(dir, f));

        // Matches dictionary-initializer keys and the Home-border helper's commands["..."] assignments.
        var keyPattern = new Regex(
            "^\\s*(?:commands)?\\[\"(?<key>(?:[^\"\\\\]|\\\\.)*)\"\\]\\s*=",
            RegexOptions.Compiled);
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var file in files)
        {
            if (!File.Exists(file))
                continue;
            foreach (var line in File.ReadLines(file))
            {
                var m = keyPattern.Match(line);
                if (!m.Success)
                    continue;
                var key = Regex.Unescape(m.Groups["key"].Value);
                if (IsDottedHandlerId(key))
                    continue; // routed through the shared identity catalog, not a raw-canonical wiring.
                keys.Add(key);
            }
        }

        foreach (var descriptor in PageLayoutRibbonActionPlanner.RibbonActionDescriptors)
        {
            if (!IsDottedHandlerId(descriptor.CommandId))
                keys.Add(descriptor.CommandId);
        }

        return keys;
    }

    // Dotted ids ("home.bold", "chartDesign.titles", …) go through the adapter; everything else is a raw
    // canonical id. A dotted id is "<lowerCamelSegment>.<segment>" with no spaces before the first dot.
    private static bool IsDottedHandlerId(string key)
    {
        var dot = key.IndexOf('.');
        if (dot <= 0)
            return false;
        var head = key[..dot];
        return head.Length > 0 && head.All(c => char.IsLetter(c)) && char.IsLower(head[0]);
    }
}
