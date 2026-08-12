using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Free.Shared.AppServices.Printing;
using Free.Shared.Drawing;
using FreeP.App.Compositor;
using FreeP.App.Recording;
using FreeP.Core.Model;

namespace FreeP.App.Avalonia;

public sealed partial class MainWindow
{
    private bool _allowCloseWithoutDirtyPromptForValidation;

    internal ValidationAccessAdapter CreateValidationAccessAdapter() => new(this);

    internal sealed class ValidationAccessAdapter
    {
        private readonly MainWindow _owner;

        internal ValidationAccessAdapter(MainWindow owner) => _owner = owner;

        internal bool IsVisible => _owner.IsVisible;
        internal string Title => _owner.Title ?? string.Empty;
        internal bool IsDirty => _owner.IsDirty;
        internal int DirtyGeneration => _owner.DirtyGeneration;
        internal int SlideCount => _owner.SlideCount;
        internal IReadOnlyList<StartupDirtyTraceEntry> StartupDirtyTrace => _owner.StartupDirtyTraceForTests;
        internal LinuxNativeOutputCapabilities NativeOutputCapabilities => _owner._nativeOutputCapabilities;
        internal bool NativeOutputCapabilityDetectionCompleted => _owner._nativeOutputDetectionCompleted;
        internal bool LastPrintPackageIsValid => _owner.LastPrintExecutionDescriptor?.Validation.IsValid == true;
        internal string? LastPrintPackageFailureReason =>
            _owner.LastPrintExecutionDescriptor?.Validation.FailureReason;

        internal void StartWhenOpened(Func<Task> operation)
        {
            ArgumentNullException.ThrowIfNull(operation);
            _owner.Opened += async (_, _) => await operation();
        }

        internal void StartNativeOutputCapabilityDetection() =>
            _owner.StartNativeOutputCapabilityDetection();

        internal Task<PrinterDiscoveryResult> DiscoverPrintersAsync(
            CancellationToken cancellationToken = default) =>
            _owner._printService.DiscoverAsync(cancellationToken);

        internal void InsertSlide() => _owner.Editor.InsertSlide();

        internal async Task<SlideShowWindow.ValidationAccessAdapter> ShowSlideShowAsync()
        {
            var window = new SlideShowWindow(_owner._presentation, 0);
            var opened = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            window.Opened += (_, _) => opened.TrySetResult(true);
            window.Show(_owner);
            await opened.Task.WaitAsync(TimeSpan.FromSeconds(8));
            return window.CreateValidationAccessAdapter();
        }

        internal void AddValidationVideo(byte[] bytes)
        {
            ArgumentNullException.ThrowIfNull(bytes);
            _owner._presentation.Slides[0].Shapes.Add(new SlideShape
            {
                Id = 8801,
                Name = "Physical validation video",
                Kind = SlideShapeKind.Media,
                ExtentCxEmu = 6096000,
                ExtentCyEmu = 3429000,
                Media = new MediaInfo
                {
                    IsVideo = true,
                    ContentType = "video/mp4",
                    Bytes = bytes,
                },
            });
        }

        internal Task<LinuxVideoExportResult> ExecuteVideoExportAsync(
            string outputPath,
            PresentationVideoExportRequest request,
            CancellationToken cancellationToken = default) =>
            _owner.ExecuteVideoExportAsync(outputPath, request, cancellationToken);

        internal Task<PrintSubmissionResult> ExecutePrintAsync(
            PresentationPrintRequest request,
            CancellationToken cancellationToken = default) =>
            _owner.ExecutePrintWorkflowCoreAsync(
                request,
                _owner._fileSession.BuildPrintOutputPackage,
                cancellationToken,
                promptForSelection: false);

        internal void ShowRepresentativeAccessibilityPanes()
        {
            _owner.ShowReviewCommentsPane();
            _owner.ShowSelectionPane();
            _owner.ShowAnimationPane();
        }

        internal IReadOnlyList<ValidationAccessibilityPaneObservation> CaptureAccessibilityPanes()
        {
            var snapshot = _owner._paneAccessibility.BuildSnapshot();
            return
            [
                Observe(
                    _owner._slidePaneList,
                    PresentationPaneAccessibilityPlanner.SlidePaneId,
                    snapshot,
                    $"Items={_owner.SlidePaneItemsForAccessibilityTests.Count}"),
                Observe(
                    _owner._notesBox,
                    PresentationPaneAccessibilityPlanner.NotesPaneId,
                    snapshot,
                    $"Text={(string.IsNullOrEmpty(_owner._notesBox.Text) ? "<empty>" : _owner._notesBox.Text)}"),
                Observe(
                    _owner._reviewCommentsPaneHost,
                    PresentationPaneAccessibilityPlanner.CommentsPaneId,
                    snapshot,
                    $"Items={_owner.CommentsPaneItemsForAccessibilityTests.Count}"),
                Observe(
                    _owner._selectionPane,
                    PresentationPaneAccessibilityPlanner.SelectionPaneId,
                    snapshot,
                    $"Items={_owner.SelectionPaneItemsForAccessibilityTests.Count}"),
                Observe(
                    _owner._animationPaneHost,
                    PresentationPaneAccessibilityPlanner.AnimationPaneId,
                    snapshot,
                    $"Items={_owner.AnimationPaneItemsForAccessibilityTests.Count}"),
            ];
        }

        internal void FocusRepresentativeAccessibilityPanes() => _owner._slidePaneList.Focus();

        internal void CloseWithoutDirtyPrompt()
        {
            _owner._allowCloseWithoutDirtyPromptForValidation = true;
            _owner.Close();
        }

        internal void Shutdown(int exitCode)
        {
            if (global::Avalonia.Application.Current?.ApplicationLifetime
                is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown(exitCode);
            }
        }

        private static ValidationAccessibilityPaneObservation Observe(
            Control control,
            string paneId,
            IReadOnlyList<PresentationPaneAccessibilitySnapshotEntry> snapshot,
            string textValue)
        {
            var state = snapshot.Single(entry => entry.PaneId == paneId);
            return new ValidationAccessibilityPaneObservation(
                paneId,
                AutomationProperties.GetAutomationId(control) ?? string.Empty,
                AutomationProperties.GetName(control) ?? string.Empty,
                AutomationProperties.GetHelpText(control) ?? string.Empty,
                control.GetType().Name,
                AutomationProperties.GetItemStatus(control) ?? state.State,
                textValue,
                control.IsVisible,
                control.Focusable,
                control.IsTabStop,
                control.TabIndex);
        }
    }

    internal sealed record ValidationAccessibilityPaneObservation(
        string PaneId,
        string AutomationId,
        string Name,
        string HelpText,
        string Role,
        string State,
        string Value,
        bool IsVisible,
        bool Focusable,
        bool IsTabStop,
        int TabIndex);
}
