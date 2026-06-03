using FluentAssertions;
using System.IO;
using System.Windows;

namespace FreeX.App.Host.Tests;

public sealed partial class SymbolPickerDialogSourceTests
{
    private static string ReadSymbolPickerDialogSources() =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SymbolPickerDialog.cs")) +
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SymbolPickerDialog.Layout.cs")) +
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SymbolPickerDialog.Catalog.cs")) +
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SymbolPickerSelectionPlanner.cs"));

    private static IEnumerable<T> FindLogicalChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            if (child is T match)
                yield return match;

            foreach (var descendant in FindLogicalChildren<T>(child))
                yield return descendant;
        }
    }
}
