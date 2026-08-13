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
    internal ContextMenu BuildChartContextMenuForTests(ChartSubtargetHit hit) =>
        BuildDomainContextMenu(_domainContextMenuSession.BuildChart(hit));

    internal ContextMenu? BuildTableContextMenuForTests(uint shapeId)
    {
        var plan = _domainContextMenuSession.BuildTable(shapeId);
        return plan is null ? null : BuildDomainContextMenu(plan);
    }

    internal bool ActivateTableCellEditForTests(uint shapeId, int row, int col)
    {
        _textEditor?.ActivateCellEdit(shapeId, row, col);
        return _textEditor?.IsCellEditActive == true;
    }

    internal bool IsTableCellEditActiveForTests => _textEditor?.IsCellEditActive == true;

    internal Task ApplyPictureBulletFromFileAsyncForTests() => ApplyPictureBulletFromFileAsync();

    internal Task<HyperlinkDialogApplyPlan> OpenHyperlinkDialogAsyncForTests() =>
        OpenHyperlinkDialogAsync();

    internal FindReplaceWorkflowPlan SetFindReplaceDialogInputForTests(
        string? query,
        string? replacement = null,
        bool matchCase = false,
        bool wholeWord = false)
    {
        var dialog = _findReplaceDialog ?? throw new InvalidOperationException("Find/Replace is not open.");
        LastFindReplaceWorkflowPlan = dialog.SetInputForTests(query, replacement, matchCase, wholeWord);
        return LastFindReplaceWorkflowPlan;
    }

    internal FindReplaceWorkflowPlan NavigateFindReplaceDialogForTests(int direction)
    {
        var dialog = _findReplaceDialog ?? throw new InvalidOperationException("Find/Replace is not open.");
        LastFindReplaceWorkflowPlan = dialog.NavigateForTests(direction);
        return LastFindReplaceWorkflowPlan;
    }

    internal FindReplaceWorkflowPlan ReplaceAllFindReplaceDialogForTests()
    {
        var dialog = _findReplaceDialog ?? throw new InvalidOperationException("Find/Replace is not open.");
        LastFindReplaceWorkflowPlan = dialog.ReplaceAllForTests();
        return LastFindReplaceWorkflowPlan;
    }

    internal Task<bool> FileNewAsyncForTests() => FileNewAsync();

    internal Task<PrintSubmissionResult> ExecutePrintForTests(
        PresentationPrintRequest? request = null,
        CancellationToken cancellationToken = default) =>
        ExecutePrintWorkflowAsync(request, cancellationToken);

}
