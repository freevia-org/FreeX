using FreeW.App.Presentation.DocumentView;
using FreeW.App.Presentation.Shell;

namespace FreeW.App.Presentation.Tests;

public sealed class FreeWEditorInteractionSessionTests
{
    [Fact]
    public void ReadMode_HidesChromeAndRestoresTheExactPriorVisibility()
    {
        var session = new FreeWEditorInteractionSession();
        var initial = new FreeWEditorChromeVisibility(
            TitleBar: FreeWChromeVisibility.Visible,
            Ribbon: FreeWChromeVisibility.Hidden,
            DataFolder: FreeWChromeVisibility.Collapsed,
            ViewSwitch: FreeWChromeVisibility.Visible,
            Zoom: FreeWChromeVisibility.Collapsed,
            NavigationPane: FreeWChromeVisibility.Visible,
            RevealPane: FreeWChromeVisibility.Collapsed,
            ReviewingPane: FreeWChromeVisibility.Visible);

        var entered = session.ToggleReadMode(initial);

        entered.IsActive.Should().BeTrue();
        entered.Chrome.Should().Be(FreeWEditorChromeVisibility.ReadMode);
        entered.ColumnWidth.Should().Be(FreeWReadModePlanner.DefaultColumnWidth);
        entered.PageColorHex.Should().Be(FreeWReadModePlanner.NoColorHex);

        var exited = session.ToggleReadMode(FreeWEditorChromeVisibility.ReadMode);

        exited.IsActive.Should().BeFalse();
        exited.Chrome.Should().Be(initial);
    }

    [Fact]
    public void ReadModeOptions_NormalizeAndOnlyRequestNativeUpdatesWhileActive()
    {
        var session = new FreeWEditorInteractionSession();

        var dormantColumn = session.UpdateReadModeColumnWidth(" WIDE ");
        var dormantColor = session.UpdateReadModePageColor(" inverse ");

        dormantColumn.Should().Be(new FreeWReadModeColumnPlan(
            FreeWReadModePlanner.WideColumn,
            FreeWReadModePlanner.WideColumnWidth,
            ApplyImmediately: false));
        dormantColor.Should().Be(new FreeWReadModePageColorPlan(
            FreeWReadModePlanner.InverseColor,
            FreeWReadModePlanner.InverseColorHex,
            ApplyImmediately: false));

        var entered = session.ToggleReadMode(FreeWEditorChromeVisibility.ReadMode);
        entered.ColumnWidth.Should().Be(FreeWReadModePlanner.WideColumnWidth);
        entered.PageColorHex.Should().Be(FreeWReadModePlanner.InverseColorHex);

        session.UpdateReadModeColumnWidth("narrow").ApplyImmediately.Should().BeTrue();
        session.UpdateReadModePageColor("sepia").ApplyImmediately.Should().BeTrue();
    }

    [Fact]
    public void StatusDispatch_UsesTheCanonicalEditorStatusPlanner()
    {
        var session = new FreeWEditorInteractionSession();
        var snapshot = new FreeWEditorStatusSnapshot(
            Words: 12,
            CharactersWithSpaces: 42,
            Paragraphs: 3,
            CurrentPage: 2,
            TotalPages: 5,
            SelectionText: "selected words");

        session.BuildStatus(snapshot).Should().Be(FreeWEditorStatusPlanner.Build(snapshot));
    }
}

public sealed class FreeWEditorInteractionSessionSourceOwnershipTests
{
    [Fact]
    public void Hosts_UseThePortableInteractionSessionAndDoNotOwnItsPolicies()
    {
        var wpfMainWindow = ReadSource("freew", "FreeW.App.Host", "MainWindow.cs");
        var avaloniaMainWindow = ReadSource("freew", "FreeW.App.Avalonia", "MainWindow.cs");

        foreach (var source in new[] { wpfMainWindow, avaloniaMainWindow })
        {
            source.Should().Contain("FreeWEditorInteractionSession _editorInteraction");
            source.Should().Contain("FreeWViewSession _viewSession");
            source.Should().Contain("_editorInteraction.ToggleReadMode(");
            source.Should().Contain("_viewSession.PlanDocumentViewChange(");
            source.Should().Contain("_viewSession.BuildDocumentViewChecks(");
            source.Should().Contain("_editorInteraction.BuildStatus(");
            source.Should().NotContain("CurrentDocumentViewSnapshot");
            source.Should().NotContain("FreeWReadModePlanner.Normalize");
            source.Should().NotContain("FreeWEditorStatusPlanner.Build(");
            source.Should().NotContain("private bool _readMode;");
        }
    }

    [Fact]
    public void DocumentViewMode_IsOwnedOnlyByPresentation()
    {
        var sharedMode = ReadSource(
            "freew", "FreeW.App.Presentation", "DocumentView", "DocumentViewMode.cs");
        var wpfDocumentView = ReadSource(
            "freew", "FreeW.App.Host", "Editing", "DocumentView.cs");
        var avaloniaDocumentView = ReadSource(
            "freew", "FreeW.App.Avalonia", "Editing", "DocumentView.cs");

        sharedMode.Should().Contain("public enum DocumentViewMode");
        wpfDocumentView.Should().NotContain("enum DocumentViewMode");
        avaloniaDocumentView.Should().NotContain("enum DocumentViewMode");
    }

    [Fact]
    public void PortableSession_HasNoRendererDependencies()
    {
        var interactionSource = ReadSource(
            "freew", "FreeW.App.Presentation", "Shell", "FreeWEditorInteractionSession.cs");
        var viewSource = ReadSource(
            "freew", "FreeW.App.Presentation", "Shell", "FreeWViewSession.cs");

        foreach (var source in new[] { interactionSource, viewSource })
        {
            source.Should().NotContain("using Avalonia");
            source.Should().NotContain("using System.Windows");
            source.Should().NotContain("System.Windows.Visibility");
            source.Should().NotContain("Avalonia.Controls");
        }
    }

    private static string ReadSource(params string[] parts)
    {
        var root = TestWorkspaceFileLocator.FindDirectoryContainingFileFromBaseDirectory("FreeW.slnx");
        return File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()));
    }
}
