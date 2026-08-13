using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;

using FreeX.Ribbon.Definitions;

namespace FreeX.App.Avalonia.Tests.Parity;

internal static partial class FreeXRibbonCommandSourceScanner
{
    [GeneratedRegex("FreeXRibbonCommandIds\\.(?<name>[A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex TypedCommandPattern();

    public static void AddTypedCommandIds(string source, ISet<string> ids)
    {
        foreach (Match match in TypedCommandPattern().Matches(source))
        {
            var field = typeof(FreeXRibbonCommandIds).GetField(
                match.Groups["name"].Value,
                BindingFlags.Public | BindingFlags.Static);
            if (field?.GetRawConstantValue() is string commandId)
                ids.Add(FreeXRibbonCommandCatalog.GetRequired(commandId).Value);
        }
    }
}
