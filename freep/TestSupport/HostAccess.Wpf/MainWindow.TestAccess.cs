using System.Windows;
using FreeP.App.Compositor;
using FreeP.Core.Model;

namespace FreeP.App.Host;

public sealed partial class MainWindow
{
    internal PresentationPrintBackstagePlan? LastFilePrintBackstagePlanForTests =>
        _fileSession.LastPrintBackstagePlan;

    internal void ShowBackstageForTests() => ShowBackstage();

    internal bool ActivateBackstageEntryForTests(string label)
    {
        _backstage.Show(label);
        return _backstage.CurrentPaneContent is not null;
    }

    internal UIElement? CurrentBackstagePaneContentForTests => _backstage.CurrentPaneContent;

    internal bool ApplyBackstagePrintCustomRangeForTests(string rangeText) =>
        _backstage.ApplyCustomPrintRangeForTests(rangeText);

    /// <summary>
    /// Forwards to the private native in-place OLE entry point wired to
    /// <see cref="SlideCanvas.AttachEditing"/>, so end-to-end tests can drive the exact call site
    /// that composes <c>onPayloadUpdated</c> for <c>WpfOleInPlaceHost.TryShow</c> instead of only
    /// exercising the composition helper in isolation.
    /// </summary>
    internal bool TryOpenOleInPlaceForTests(SlideShape shape) => TryOpenOleInPlace(shape);

    /// <summary>
    /// Forwards to the private external-activation entry point wired to
    /// <see cref="SlideCanvas.AttachEditing"/>, so end-to-end tests can drive the exact call site
    /// that composes <c>onPayloadUpdated</c> for a slide-level embedded object opened in its own
    /// application -- the route taken whenever in-place activation is unavailable.
    /// </summary>
    internal bool TryActivateOleExternallyForTests(OleObjectInfo? oleObject) =>
        TryActivateOleExternally(oleObject);
}
