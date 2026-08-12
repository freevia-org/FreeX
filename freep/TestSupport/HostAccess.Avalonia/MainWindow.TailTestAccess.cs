using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaRectangle = Avalonia.Controls.Shapes.Rectangle;
using Free.Shared.AppServices;
using Free.Shared.AppServices.Printing;
#if FREEP_WINDOWS_CAPTURE
using Free.Shared.AppServices.Windows;
#endif
using Free.Shared.Drawing;
using Free.Shared.IO;
using Free.Shared.Pdf.Skia;
using Free.Shared.Ribbon;
using Free.Shared.Ribbon.Avalonia;
using Free.Shared.Ribbon.KeyTips;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using Free.Shared.Theme;
using Free.Shared.Theme.Avalonia;
using FreeP.App.Avalonia.Backstage;
using FreeP.App.Avalonia.Printing;
using FreeP.App.Compositor;
using FreeP.App.Recording;
#if FREEP_WINDOWS_CAPTURE
using FreeP.App.Recording.Windows;
#endif
using FreeP.App.Rendering.Avalonia;
using FreeP.Core.IO;
using FreeP.Core.Model;
using System.Linq;

namespace FreeP.App.Avalonia;

public sealed partial class MainWindow
{
    internal Task<bool> TrySavePresentationFileAsyncForTests(string path) =>
        TrySavePresentationFileAsync(path);

    internal ContextMenu BuildSlidePaneContextMenuForTests(int slideIndex) =>
        BuildSlidePaneContextMenu(slideIndex);

    internal ContextMenu BuildSlidePaneSectionContextMenuForTests(SlidePaneEntry entry) =>
        BuildSlidePaneSectionContextMenu(entry);

    internal bool ToggleSlidePaneSectionForTests(int sectionIndex)
    {
        if (sectionIndex < 0 || sectionIndex >= _presentation.Sections.Count)
            return false;

        ToggleSlidePaneSection(SlidePanePlanner.GetSectionIdentity(_presentation.Sections[sectionIndex], sectionIndex));
        return true;
    }

    internal bool TryApplySlideSectionActionForTests(
        SlideSectionActionKind kind,
        int slideIndex = -1,
        int sectionIndex = -1,
        string? promptedName = null)
    {
        var command = kind switch
        {
            SlideSectionActionKind.AddSection => FreePContextMenuCommand.AddSection,
            SlideSectionActionKind.RenameSection => FreePContextMenuCommand.RenameSection,
            SlideSectionActionKind.RemoveSection => FreePContextMenuCommand.RemoveSection,
            SlideSectionActionKind.RemoveAllSections => FreePContextMenuCommand.RemoveAllSections,
            _ => default,
        };
        var execution = _workareaSession.BuildSlidePaneContextCommandRoute(
                command,
                slideIndex,
                sectionIndex)
            .SectionExecution;
        return execution is not null &&
            _workareaSession.ExecuteSlidePaneSectionAction(execution, promptedName);
    }

    internal SlidePaneDropVisualPlan PreviewSlidePaneDragForTests(
        int sourceSlideIndex,
        double startPointerY,
        double pointerYWithinItem,
        double pointerYWithinPane)
    {
        _workareaSession.BeginSlidePaneDrag(sourceSlideIndex, startPointerY);
        var update = _workareaSession.UpdateSlidePaneDrag(
            pointerYWithinItem,
            pointerYWithinPane,
            SlidePanePlanner.DefaultSlideItemHeight);
        if (update.State.IsDragging)
            ShowSlidePaneInsertionIndicator(update.DropVisualPlan);
        else
            HideSlidePaneInsertionIndicator();

        return update.DropVisualPlan;
    }

    internal bool CompleteSlidePaneDragForTests()
    {
        var applied = _workareaSession.CompleteSlidePaneDrag(out var shouldReleaseCapture);
        HideSlidePaneInsertionIndicator();
        return shouldReleaseCapture && applied;
    }

    internal bool ClickSlidePaneNewSlideAffordanceForTests()
    {
        var before = _presentation.Slides.Count;
        var applied = InsertSlideFromSlidePaneAffordance();
        return applied && _presentation.Slides.Count == before + 1;
    }

    internal Task OpenCustomShowDialogAsyncForTests() =>
        OpenCustomShowDialogAsync();

}
