using System.IO;

namespace FreeX.App.Avalonia.Tests;

public sealed class AvaloniaSheetDialogSizeSourceTests
{
    [Fact]
    public void RenameAndUnhideSheetDialogs_UseWpfLogicalCaptureSizes()
    {
        var source = File.ReadAllText(RepoFile("src", "FreeX.App.Avalonia", "MainWindow.cs"));

        var renameDialog = ExtractMethodSource(
            source,
            "private async Task<string?> ShowRenameSheetDialogAsync(string currentName)",
            "private async Task ShowFindDialogAsync()");
        renameDialog.Should().Contain("Width = 340,");
        renameDialog.Should().Contain("Height = 150,");
        renameDialog.Should().Contain("MinWidth = 340,");
        renameDialog.Should().Contain("MinHeight = 150,");
        renameDialog.Should().Contain("MaxWidth = 340,");
        renameDialog.Should().Contain("MaxHeight = 150,");
        renameDialog.Should().Contain("CanResize = false,");
        renameDialog.Should().Contain("MinWidth = 72,");

        var unhideDialog = ExtractMethodSource(
            source,
            "private async Task<WorkbookHiddenSheet?> ShowUnhideSheetDialogAsync(IReadOnlyList<WorkbookHiddenSheet> hiddenSheets)",
            "private async Task<string?> ShowRenameSheetDialogAsync(string currentName)");
        unhideDialog.Should().Contain("Width = 340,");
        unhideDialog.Should().Contain("Height = 160,");
        unhideDialog.Should().Contain("MinWidth = 340,");
        unhideDialog.Should().Contain("MinHeight = 160,");
        unhideDialog.Should().Contain("MaxWidth = 340,");
        unhideDialog.Should().Contain("MaxHeight = 160,");
        unhideDialog.Should().Contain("CanResize = false,");
        unhideDialog.Should().Contain("ApplyDialogButtonChrome(okButton, width: 72, isDefault: true);");
        unhideDialog.Should().Contain("ApplyDialogButtonChrome(cancelButton, width: 72);");
    }

    private static string ExtractMethodSource(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"source should contain {startMarker}");

        var end = source.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"source should contain {endMarker} after {startMarker}");

        return source[start..end];
    }

    private static string RepoFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(new[] { directory.FullName }.Concat(parts).ToArray());
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException("Could not locate repository file.", Path.Combine(parts));
    }
}
