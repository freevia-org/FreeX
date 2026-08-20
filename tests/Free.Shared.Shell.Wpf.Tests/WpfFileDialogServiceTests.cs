using System.IO;
using Microsoft.Win32;

namespace Free.Shared.Shell.Wpf.Tests;

/// <summary>
/// Covers <see cref="Free.Shared.Shell.WpfFileDialogService.ApplyInitialDirectory"/>, the helper both
/// <c>ShowOpenDialog</c> and <c>ShowSaveDialog</c> use to seed a native dialog's starting folder. The
/// dialogs themselves can't be driven headlessly (ShowDialog blocks on real user input), so this exercises
/// the property-assignment logic directly against real <see cref="OpenFileDialog"/>/<see cref="SaveFileDialog"/>
/// instances, which can be constructed without showing UI.
/// </summary>
public sealed class WpfFileDialogServiceTests
{
    [Fact]
    public void ApplyInitialDirectory_OnSaveDialog_SeedsStartingFolder_WhenDirectoryExists()
    {
        var dialog = new SaveFileDialog();
        var existingDirectory = Path.GetTempPath();

        Free.Shared.Shell.WpfFileDialogService.ApplyInitialDirectory(dialog, existingDirectory);

        dialog.InitialDirectory.Should().Be(existingDirectory);
    }

    [Fact]
    public void ApplyInitialDirectory_OnOpenDialog_SeedsStartingFolder_WhenDirectoryExists()
    {
        var dialog = new OpenFileDialog();
        var existingDirectory = Path.GetTempPath();

        Free.Shared.Shell.WpfFileDialogService.ApplyInitialDirectory(dialog, existingDirectory);

        dialog.InitialDirectory.Should().Be(existingDirectory);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ApplyInitialDirectory_LeavesDialogDefault_WhenDirectoryIsBlank(string? initialDirectory)
    {
        var dialog = new SaveFileDialog();

        Free.Shared.Shell.WpfFileDialogService.ApplyInitialDirectory(dialog, initialDirectory);

        dialog.InitialDirectory.Should().BeEmpty();
    }

    [Fact]
    public void ApplyInitialDirectory_LeavesDialogDefault_WhenDirectoryDoesNotExist()
    {
        var dialog = new SaveFileDialog();
        var missingDirectory = Path.Combine(Path.GetTempPath(), "freex-r152-f4-" + Guid.NewGuid().ToString("N"));

        Free.Shared.Shell.WpfFileDialogService.ApplyInitialDirectory(dialog, missingDirectory);

        dialog.InitialDirectory.Should().BeEmpty();
    }
}
