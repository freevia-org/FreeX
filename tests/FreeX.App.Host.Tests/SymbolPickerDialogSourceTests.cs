using FluentAssertions;
using System.IO;

namespace FreeX.App.Host.Tests;

public sealed partial class SymbolPickerDialogSourceTests
{
    private static string ReadSymbolPickerDialogSources() =>
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SymbolPickerDialog.cs")) +
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SymbolPickerDialog.Layout.cs")) +
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SymbolPickerDialog.Catalog.cs")) +
        File.ReadAllText(WorkspaceFileLocator.Find("src", "FreeX.App.Host", "SymbolPickerSelectionPlanner.cs"));
}
