using System.Windows;
using FreeP.App.Compositor;
using FreeP.App.Host;

namespace FreeP.App.Host.Tests;

/// <summary>
/// Construction smoke tests for the shared-chrome shell: the <see cref="MainWindow"/> composes its title bar,
/// ribbon, backstage and canvas from the shared tier without throwing. STA because the window is a real WPF
/// control. This stands in for launching the GUI: if the shared chrome wires up, the window builds.
///
/// Wave 3A tests: verifies that the Editor (EditingSession) is exposed and functional, and that
/// the slide-pane host seam is present for 3B.
/// </summary>
public sealed class ShellConstructionTests
{
    [StaFact]
    public void MainWindow_ConstructsWithSharedChrome()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Should().NotBeNull();
            window.Title.Should().Contain("FreeP");
            window.Icon.Should().NotBeNull("the WPF host must load the canonical owned FreeP icon");
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
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
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

    // ── Wave 3A: Editor and seams ─────────────────────────────────────────────────

    [StaFact]
    public void MainWindow_Editor_IsNotNull()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.Should().NotBeNull();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_Editor_HasCurrentSlide()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            // CreateEmpty starts with 1 slide — Editor should reflect it.
            window.Editor.CurrentSlide.Should().NotBeNull();
            window.Editor.CurrentSlideIndex.Should().Be(0);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_SlidePaneHost_IsPresent()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            // 3B seam: the pane host container must exist.
            window.SlidePaneHost.Should().NotBeNull();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void MainWindow_SlideCanvas_IsPresent()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            // 3C seam: the canvas must be accessible.
            window.SlideCanvas.Should().NotBeNull();
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void Editor_InsertSlide_IncreasesSlideCount()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var before = window.Editor.Presentation.Slides.Count;
            window.Editor.InsertSlide();
            window.Editor.Presentation.Slides.Count.Should().Be(before + 1);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void Editor_DuplicateCurrentSlide_IncreasesSlideCount()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var before = window.Editor.Presentation.Slides.Count;
            window.Editor.DuplicateCurrentSlide();
            window.Editor.Presentation.Slides.Count.Should().Be(before + 1);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void Editor_DeleteCurrentSlide_DecreasesSlideCount()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            window.Editor.InsertSlide(); // ensure 2 slides
            var before = window.Editor.Presentation.Slides.Count;
            window.Editor.DeleteCurrentSlide();
            window.Editor.Presentation.Slides.Count.Should().Be(before - 1);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void Editor_InsertThenUndo_RestoresPreviousCount()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var before = window.Editor.Presentation.Slides.Count;
            window.Editor.InsertSlide();
            window.Editor.Undo();
            window.Editor.Presentation.Slides.Count.Should().Be(before);
        }
        finally
        {
            window.Close();
        }
    }

    [StaFact]
    public void Editor_InsertDefaultRectangle_AddsShapeToCurrentSlide()
    {
        var window = new MainWindow(new FreePOptions(), messageService: TestUserMessageService.DiscardUnsavedChanges);
        try
        {
            var before = window.Editor.CurrentSlide!.Shapes.Count;
            window.Editor.InsertDefaultRectangle();
            window.Editor.CurrentSlide!.Shapes.Count.Should().Be(before + 1);
        }
        finally
        {
            window.Close();
        }
    }
}
