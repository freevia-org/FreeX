using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace FreeX.App.Avalonia;

/// <summary>The persistent accessibility container for the rendered worksheet viewport.</summary>
internal sealed class WorksheetGridHost : ContentControl
{
    protected override Type StyleKeyOverride => typeof(ContentControl);

    protected override AutomationPeer OnCreateAutomationPeer() => new WorksheetGridAutomationPeer(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ContentProperty &&
            ControlAutomationPeer.FromElement(this) is WorksheetGridAutomationPeer peer)
        {
            peer.NotifyViewportChanged();
        }
    }

    private sealed class WorksheetGridAutomationPeer(WorksheetGridHost owner)
        : ControlAutomationPeer(owner), ISelectionProvider
    {
        public bool CanSelectMultiple => true;
        public bool IsSelectionRequired => false;

        public IReadOnlyList<AutomationPeer> GetSelection() =>
            owner.GetVisualDescendants().OfType<WorksheetCellBorder>()
                .Where(cell => cell.IsSelected)
                .Select(CreatePeerForElement)
                .ToArray();

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataGrid;

        internal void NotifyViewportChanged()
        {
            InvalidateChildren();
            RaisePropertyChangedEvent(SelectionPatternIdentifiers.SelectionProperty, null, null);
        }
    }
}

/// <summary>
/// A rendered worksheet cell is an accessible item, rather than a decorative border. Its visual
/// text/selection layers are implementation details; only a live inline editor is an accessible child.
/// </summary>
internal sealed class WorksheetCellBorder : Border
{
    public string DisplayValue { get; set; } = "";
    public bool IsSelected { get; set; }

    protected override AutomationPeer OnCreateAutomationPeer() => new WorksheetCellAutomationPeer(this);

    private sealed class WorksheetCellAutomationPeer(WorksheetCellBorder owner)
        : ControlAutomationPeer(owner), IValueProvider, ISelectionItemProvider
    {
        public bool IsReadOnly => true;
        public string Value => owner.DisplayValue;
        public bool IsSelected => owner.IsSelected;
        public ISelectionProvider? SelectionContainer =>
            owner.GetVisualAncestors().OfType<WorksheetGridHost>().FirstOrDefault() is { } grid
                ? CreatePeerForElement(grid).GetProvider<ISelectionProvider>()
                : null;

        // As on the WPF host, cell mutation belongs to the worksheet's editing/selection routes.
        public void SetValue(string? value) => throw new InvalidOperationException("Use the worksheet editor to change a cell value.");
        public void AddToSelection() => throw new InvalidOperationException("Use worksheet navigation to change the selection.");
        public void RemoveFromSelection() => throw new InvalidOperationException("Use worksheet navigation to change the selection.");
        public void Select() => throw new InvalidOperationException("Use worksheet navigation to change the selection.");

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataItem;
        protected override string GetClassNameCore() => "GridViewCell";

        protected override IReadOnlyList<AutomationPeer> GetChildrenCore() =>
            owner.GetVisualDescendants().OfType<TextBox>()
                .Where(editor => editor.IsEffectivelyVisible)
                .Select(CreatePeerForElement)
                .ToArray();
    }
}
