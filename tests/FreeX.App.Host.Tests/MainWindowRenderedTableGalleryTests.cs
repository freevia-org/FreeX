using System.Linq;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FluentAssertions;
using static FreeX.App.Host.Tests.DispatcherTestPump;

namespace FreeX.App.Host.Tests;

/// <summary>
/// Functional safety net for the two table-style galleries on the DECLARATIVE ribbon: Home ▸ Format as
/// Table and the Table Design contextual ▸ Table Styles. Both menus are built imperatively
/// (PopulateFormatTableGalleryMenu / PopulateTableDesignStyleGalleryMenu) and attached to the rendered
/// declarative gallery button so its click opens the gallery. Before the host-side attach existed the
/// rendered button had no ContextMenu, so the dropdowns were non-functional even though the stub menu
/// was populated. These tests assert the rendered button owns a populated gallery menu after the ribbon
/// is built.
/// </summary>
public sealed class MainWindowRenderedTableGalleryTests
{
    [Fact]
    public void RenderedFormatAsTableButton_HasPopulatedGalleryContextMenu()
    {
        ReusableFreeXMainWindowSession.Run(window =>
        {
            PumpDispatcher();

            var button = window.FindRenderedRibbonCommandControlForTest("Format as Table") as ButtonBase;
            button.Should().NotBeNull("the declarative ribbon should render a 'Format as Table' button");

            var menu = button!.ContextMenu;
            menu.Should().NotBeNull("the imperatively-built gallery must be attached to the rendered button");
            menu!.Items.OfType<MenuItem>().Should().NotBeEmpty("the gallery must carry table-style options");
        });
    }

    [Fact]
    public void RenderedTableStylesButton_HasPopulatedGalleryContextMenu()
    {
        ReusableFreeXMainWindowSession.Run(window =>
        {
            PumpDispatcher();

            // The Table Design tab is contextual; populate + attach its style gallery the same way the
            // host does when the rendered button is engaged, then assert the rendered button owns it.
            window.PopulateTableDesignStyleGalleryMenuForTest();
            PumpDispatcher();

            var button = window.FindRenderedRibbonCommandControlForTest("Table Styles") as ButtonBase;
            button.Should().NotBeNull("the declarative ribbon should render a 'Table Styles' button");

            var menu = button!.ContextMenu;
            menu.Should().NotBeNull("the imperatively-built gallery must be attached to the rendered button");
            menu!.Items.OfType<MenuItem>().Should().NotBeEmpty("the gallery must carry table-style options");
        });
    }
}
