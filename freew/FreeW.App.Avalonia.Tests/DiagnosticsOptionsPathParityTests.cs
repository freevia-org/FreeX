using System.IO;

namespace FreeW.App.Avalonia.Tests;

/// <summary>
/// Round 169 follow-up. Both FreeW shells built the "Options path:" line of Copy Diagnostics with
/// <c>AppStoragePathPlanner.GetOptionsFilePathLabelOrFallback(PlatformApplicationDataPathProvider.LocalInstance)</c>,
/// which names <c>%LOCALAPPDATA%\FreeW\options.json</c>. That is wrong twice over: FreeW's options
/// live in <c>settings.json</c> (the planner's <c>OptionsFileName</c> is FreeX's <c>options.json</c>)
/// under <c>%APPDATA%</c> (see <c>R169_SettingsPathParityTests</c>). Support reports were pointing at
/// a path that has never existed on any FreeW install.
///
/// <para>
/// Both shells now read the live store instead, so the label cannot drift from the file the window
/// actually loads and saves -- including when an override path or a test store is in play. This is a
/// source contract rather than a behavioural test because Copy Diagnostics ends in a modal
/// <c>FreeWInfoDialog</c> that headless has no way to answer, and the assertion is about which
/// expression feeds the label -- exactly what <c>SisterAppFrameHelperTests</c> pins for the rest of
/// this window's frame.
/// </para>
/// </summary>
public sealed class DiagnosticsOptionsPathParityTests
{
    [Theory]
    [InlineData("FreeW.App.Host", "MainWindow.cs")]
    [InlineData("FreeW.App.Avalonia", "MainWindow.HelpCommands.cs")]
    public void Both_shells_label_diagnostics_with_the_live_options_store_path(
        string projectFolder,
        string fileName)
    {
        var source = File.ReadAllText(Path.Combine(
            TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx"),
            "freew",
            projectFolder,
            fileName));

        source.Should().Contain(
            "var optionsPath = _optionsStore.StorePath;",
            "the diagnostics label must name the settings file this window actually uses");
        source.Should().NotContain(
            "GetOptionsFilePathLabelOrFallback",
            "that planner resolves FreeX's options.json under the wrong root for FreeW");
    }
}
