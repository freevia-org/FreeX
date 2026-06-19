using System.Windows;
using FreeP.App.Host;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Construction smoke tests for the shared-chrome shell: the <see cref="MainWindow"/> composes its title bar,
/// ribbon, backstage and canvas from the shared tier without throwing. STA because the window is a real WPF
/// control. This stands in for launching the GUI: if the shared chrome wires up, the window builds.
/// </summary>
public sealed class ShellConstructionTests
{
    [StaFact]
    public void MainWindow_ConstructsWithSharedChrome()
    {
        var window = new MainWindow();
        try
        {
            window.Should().NotBeNull();
            window.Title.Should().Contain("FreeP");
            window.Content.Should().NotBeNull();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_TitleReflectsApplicationName()
    {
        var window = new MainWindow(new FreePOptions());
        try
        {
            // WindowTitlePlanner composes "<doc> — FreeP"; the untitled deck still ends in the app name.
            window.Title.Should().EndWith("FreeP");
        }
        finally
        {
            window.Close();
        }
    }
}
