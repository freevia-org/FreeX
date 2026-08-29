using System.IO;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Round 169 follow-up, same class of bug as <see cref="DiagnosticsOptionsPathParityTests"/> but on
/// the status-bar / backstage "data folder" label. <c>ResolveDataFolderLabel()</c>'s parameterless
/// overload defaults to <c>PlatformApplicationDataPathProvider.LocalInstance</c>, so three of the
/// four sister shells reported <c>%LOCALAPPDATA%\{Product}</c> while their options actually live
/// under <c>%APPDATA%</c> -- FreeW's WPF host, and both FreeP shells. Only FreeW.App.Avalonia was
/// already passing the live store path, and it is the model the rest now follow.
///
/// <para>
/// Pinned as a source contract for the same reason as the diagnostics twin: the label is built deep
/// inside window construction and backstage port wiring, and what is under test is which expression
/// feeds it. The behavioural half is covered where a shell can be constructed headless --
/// <c>FreeP.App.Avalonia.Tests.MainWindowHeadlessTests</c> asserts the rendered status text ends
/// with the label derived from that window's own store.
/// </para>
/// </summary>
public sealed class DataFolderLabelParityTests
{
    [Theory]
    [InlineData("freew", "FreeW.App.Host", "MainWindow.cs")]
    [InlineData("freew", "FreeW.App.Avalonia", "MainWindow.cs")]
    [InlineData("freep", "FreeP.App.Host", "MainWindow.cs")]
    [InlineData("freep", "FreeP.App.Avalonia", "MainWindow.cs")]
    public void Every_shell_derives_the_data_folder_label_from_its_live_options_store(
        string appFolder,
        string projectFolder,
        string fileName)
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            appFolder,
            projectFolder,
            fileName));

        source.Should().Contain(
            "ResolveDataFolderLabel(_optionsStore.StorePath)",
            "the data-folder label must name the folder this window actually reads and writes");
        source.Should().NotContain(
            "ResolveDataFolderLabel()",
            "the parameterless overload resolves %LOCALAPPDATA%, which none of these apps store options in");
    }
}
