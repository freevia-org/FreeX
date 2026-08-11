using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;

namespace FreeW.App.Avalonia.Editing;

/// <summary>
/// UI Automation peer for <see cref="DocumentView"/>, FreeW-Avalonia's custom document-editing
/// surface.
///
/// <para>
/// <b>What the WPF twin gets, and why.</b> <c>FreeW.App.Host.Editing.DocumentView</c> derives from
/// WPF's <c>RichTextBox</c> (a <c>FlowDocument</c>-backed <c>TextBoxBase</c>) and contains no
/// automation code at all — <c>TextBoxBase.OnCreateAutomationPeer()</c> returns a
/// <c>System.Windows.Automation.Peers.TextAutomationPeer</c> for free, which implements the full
/// UIA <c>ITextProvider</c>/<c>ITextRangeProvider</c> contract: the document's text is exposed as
/// navigable <c>TextPatternRange</c>s (by character/word/line/paragraph/document), the caret and
/// selection are reported as a range with live "text selection changed" automation events, and
/// per-run formatting is queryable via <c>TextPatternRangeAttribute</c> on any sub-range.
/// </para>
///
/// <para>
/// <b>What Avalonia 12.0.4 actually offers (verified by reflecting the shipped
/// <c>Avalonia.Controls.dll</c>/<c>Avalonia.Base.dll</c>, not assumed).</b>
/// <c>Avalonia.Automation.Provider</c> defines exactly: <c>IExpandCollapseProvider</c>,
/// <c>IInvokeProvider</c>, <c>IRangeValueProvider</c>, <c>IScrollProvider</c>,
/// <c>ISelectionProvider</c>/<c>ISelectionItemProvider</c>, <c>IToggleProvider</c>, and
/// <c>IValueProvider</c>. There is <b>no</b> <c>ITextProvider</c>, <c>ITextRangeProvider</c>, or
/// <c>ICaretProvider</c> equivalent, and no automation event dedicated to "text selection changed".
/// Text-range navigation and true per-run/paragraph structural queries via UI Automation are
/// therefore not expressible on this platform version — that is a framework limit, not something
/// this peer works around.
/// </para>
///
/// <para>
/// <b>What this peer does instead — the closest available equivalents.</b>
/// <list type="bullet">
/// <item><description>
/// Reports <see cref="AutomationControlType.Document"/> (the same control type WPF's
/// <c>TextAutomationPeer</c> would report for a <c>RichTextBox</c>) and implements
/// <see cref="IValueProvider"/> — the only pattern Avalonia has for exposing bulk text content —
/// with <see cref="Value"/> returning the full document plain text
/// (<see cref="DocumentView.PlainText"/>, itself <c>TextDocument.PlainText</c>, which already
/// joins every paragraph/table cell's runs). This covers "document text exposure".
/// </description></item>
/// <item><description>
/// Reports caret position and selection via the automation ItemStatus property (a free-text
/// status field) — see <see cref="DocumentView.AutomationSelectionStatus"/> for the exact format
/// (body block/offset, or table row/col/paragraph/offset while the caret is in a cell, plus the
/// selected text when there is a selection). Built from the same <c>CellCaretInfo</c>/
/// <see cref="DocumentView.SelectedText"/> the ribbon and Find/Replace already use, not a
/// reimplementation. Reporting caret position (not just selected text) matters because most
/// caret moves — arrow keys, clicks — never produce a selection, so a selection-only status
/// would silently never change for the majority of navigation.
/// </description></item>
/// <item><description>
/// Raises change notifications: <see cref="NotifySelectionChanged"/> fires an
/// ItemStatus-changed automation event on every <see cref="DocumentView.CaretMoved"/> (which
/// already fires from every caret-move/click/selection/table-navigation call site in
/// DocumentView), and <see cref="NotifyValueChanged"/> fires a Value-changed automation event on
/// every <see cref="DocumentView.DocumentChanged"/> (raised on every committed edit, undo/redo,
/// and external load/mutation). Both are de-duplicated by DocumentView so no-op moves (e.g.
/// re-clicking the same position) don't spam assistive tech.
/// </description></item>
/// <item><description>
/// <see cref="IsReadOnly"/> is <see langword="true"/> and <see cref="SetValue"/> throws: automation
/// clients can read the document's text, but cannot replace it wholesale — edits must go through
/// <see cref="DocumentView"/>'s command bus (undo/redo, track-changes, etc. all depend on that),
/// which a raw <c>ValuePattern.SetValue</c> call would bypass.
/// </description></item>
/// </list>
/// </para>
///
/// <para>
/// <b>Explicitly NOT provided</b> (because Avalonia has no pattern for it): per-character/word/
/// line/paragraph <c>TextPatternRange</c> navigation, run-level formatting attribute queries via
/// automation, and a dedicated caret-position/text-selection-changed automation event. A screen
/// reader driving FreeW-Avalonia therefore gets "here is the whole document's text, and here is
/// what's currently selected, updated live" rather than WPF's fully range-addressable navigation.
/// </para>
/// </summary>
internal sealed class DocumentViewAutomationPeer : ControlAutomationPeer, IValueProvider
{
    private readonly DocumentView _owner;

    public DocumentViewAutomationPeer(DocumentView owner)
        : base(owner)
    {
        _owner = owner;
    }

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Document;

    protected override string GetClassNameCore() => nameof(DocumentView);

    protected override string? GetItemStatusCore() => _owner.AutomationSelectionStatus();

    // IValueProvider: the closest Avalonia equivalent to WPF ITextProvider's document-text exposure
    // (Avalonia 12.0.4 has no ITextProvider/ITextRangeProvider). Read-only: see class remarks.
    public bool IsReadOnly => true;

    public string? Value => _owner.PlainText;

    public void SetValue(string? value) =>
        throw new System.NotSupportedException(
            "DocumentView text is read-only via UI Automation; edits must go through the document's command bus, not raw text replacement.");

    /// <summary>Raises the automation Value-changed event. Called by <see cref="DocumentView"/> on every <see cref="DocumentView.DocumentChanged"/>.</summary>
    internal void NotifyValueChanged(string? oldValue, string? newValue) =>
        RaisePropertyChangedEvent(ValuePatternIdentifiers.ValueProperty, oldValue, newValue);

    /// <summary>
    /// Raises the automation ItemStatus-changed event — the closest available substitute for a
    /// caret/selection-changed notification (Avalonia has no ICaretProvider or dedicated
    /// selection-changed automation event). Called by <see cref="DocumentView"/> on every
    /// <see cref="DocumentView.CaretMoved"/>.
    /// </summary>
    internal void NotifySelectionChanged(string? oldStatus, string? newStatus) =>
        RaisePropertyChangedEvent(AutomationElementIdentifiers.ItemStatusProperty, oldStatus, newStatus);
}
