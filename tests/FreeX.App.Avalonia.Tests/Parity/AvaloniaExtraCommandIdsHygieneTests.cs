using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

using FreeX.App.Presentation.PageLayout;
using FreeX.Ribbon.Definitions;

using Xunit;

namespace FreeX.App.Avalonia.Tests.Parity;

/// <summary>
/// Proves the Avalonia endpoint dictionaries use only command ids emitted by the shared ribbon definition.
/// The endpoint mappings remain renderer-owned; a second command-id inventory does not.
/// </summary>
public sealed class CanonicalEndpointCommandIdsHygieneTests
{
    [Fact]
    public void LiteralEndpointKeys_AreCanonicalIdsFromTheSharedDefinition()
    {
        var keys = ExtractRawCanonicalKeysFromSource();
        var nonCanonical = keys
            .Where(key => !FreeXRibbonCommandCatalog.TryGet(key, out _))
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(nonCanonical);
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
                keys.Add(key);
            }
        }

        foreach (var descriptor in PageLayoutRibbonActionPlanner.RibbonActionDescriptors)
        {
            keys.Add(descriptor.CommandId);
        }

        return keys;
    }

}
