using Avalonia;
using Avalonia.Controls.Presenters;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Free.Shared.Shell;
using Free.Shared.Shell.Avalonia;
using FreeW.App.Presentation;

namespace FreeW.App.Avalonia;

internal sealed class LegalNoticesDialog : AvaloniaLegalNoticesDialog
{
    public LegalNoticesDialog()
        : this(FreeWLegalNoticeProvider.GetDocuments(typeof(LegalNoticesDialog).Assembly))
    {
    }

    internal LegalNoticesDialog(IReadOnlyList<LegalNoticeDocument> notices)
        : base(FreeWLegalNoticesPresentation.Create(notices))
    {
        RegisterWpfTrailingEdgeCorrection();
    }

    internal LegalNoticesDialog(IReadOnlyList<(string Title, string Text)> notices)
        : base(FreeWLegalNoticesPresentation.Create(notices))
    {
        RegisterWpfTrailingEdgeCorrection();
    }

    private void RegisterWpfTrailingEdgeCorrection()
    {
        Opened += (_, _) => Dispatcher.UIThread.Post(AlignSelectedDocumentPane, DispatcherPriority.Render);
    }

    private void AlignSelectedDocumentPane()
    {
        var selectedPane = this.GetVisualDescendants()
            .OfType<ContentPresenter>()
            .SingleOrDefault(presenter => presenter.Name == "PART_SelectedContentHost");
        if (selectedPane is null || selectedPane.Margin.Right >= 1)
            return;

        selectedPane.Margin = new Thickness(
            selectedPane.Margin.Left,
            selectedPane.Margin.Top,
            1,
            selectedPane.Margin.Bottom);
    }
}
