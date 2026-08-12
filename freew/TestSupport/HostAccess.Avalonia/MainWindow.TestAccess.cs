using Avalonia.Controls;
using Avalonia.Input;
using Free.Shared.Ribbon;
using FreeW.App.Avalonia.Ribbon;
using FreeW.App.Presentation.Ribbon;

namespace FreeW.App.Avalonia;

public sealed partial class MainWindow
{
    internal bool IsReadAloudActiveForTest => _readAloudSession?.IsActive == true;
    internal void ToggleReadAloudForTest() => ToggleReadAloud();
    internal bool RibbonKeyTipsVisibleForTest => _ribbonKeyTipsVisible;
    internal Control? RibbonControlForTest => _ribbonControl;
    internal IRibbonCommandRegistry? RibbonRegistryForTests => _ribbonRegistry;
    internal bool HasWindowIconForTests => Icon is not null;
    internal Border TitleBarForTests => _titleBar;
    internal IReadOnlyList<Button> QuickAccessButtonsForTests => _quickAccessButtons;
    internal IReadOnlyList<Control> StatusViewControlsForTests =>
        [_readModeSwitch, _printLayoutSwitch, _webLayoutSwitch, _draftSwitch, _pagedEditSwitch];
    internal string PageStatusForTests => _pageStatus.Text ?? string.Empty;
    internal string SectionStatusForTests => _sectionStatus.Text ?? string.Empty;
    internal string CountsStatusForTests => _status.Text ?? string.Empty;
    internal string PrintStatusForTests => _status.Text ?? string.Empty;
    internal MailMergeEngine MailMergeForTests => _mailMerge!;
    internal Task ExecuteFinishMergePlanForTests(MailMergeFinishPlan plan) => ExecuteFinishMergePlanAsync(plan);
    internal string DataFolderStatusForTests => _dataFolderStatus.Text ?? string.Empty;
    internal Slider ZoomSliderForTests => _zoomSlider;
    internal string ZoomLabelForTests => _zoomLabel.Text ?? string.Empty;
    internal void ApplyZoomForTests(double scale) => ApplyZoom(scale);
    internal void RaiseKeyDownForTest(KeyEventArgs args) => MainWindow_KeyDown(this, args);
    internal bool IsCloseDecisionPendingForTests => _closeCoordinator.IsClosePending;
    internal NotesPane NotesPaneForTest => _notesPane;
    internal ThesaurusPane ThesaurusPaneForTest => _thesaurusPane;
}
