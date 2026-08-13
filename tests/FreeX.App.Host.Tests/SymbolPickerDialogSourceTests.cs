using FluentAssertions;

namespace FreeX.App.Host.Tests;

public sealed partial class SymbolPickerDialogSourceTests
{
    private static string ReadSymbolPickerDialogSources() =>
        DialogSourceTestSupport.ReadHostSourcesWithSeparator(
            string.Empty,
            "SymbolPickerDialog.cs",
            "SymbolPickerDialog.Layout.cs");
}
