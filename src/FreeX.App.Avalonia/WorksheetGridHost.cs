using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using FreeX.Core.Model;

namespace FreeX.App.Avalonia;

internal sealed record WorksheetAutomationCell(
    string Name, string Value, string HelpText, bool IsSelected, bool IsActive, Rect Bounds);

/// <summary>A persistent accessibility surface over the worksheet's replaceable visual tree.</summary>
internal sealed class WorksheetGridHost : ContentControl
{
    private readonly Dictionary<(uint Row, uint Column), WorksheetCellAutomationPeer> _cellPeers = [];
    private readonly Dictionary<(uint Row, uint Column), WorksheetCellBorder> _borders = [];
    private IReadOnlyList<(uint Row, uint Column)> _coordinates = [];
    private Func<uint, uint, WorksheetAutomationCell>? _readCell;
    private Control? _surface;
    private bool _bordersDirty = true;
    private bool _publishPending;
    private bool _publishQueued;
    private bool _closed;
    private Matrix? _publishedTransform;

    public WorksheetGridHost() => LayoutUpdated += (_, _) => PublishChanges();

    protected override Type StyleKeyOverride => typeof(ContentControl);
    internal int CachedCellPeerCount => _cellPeers.Count;
    internal WorksheetAutomationCell ReadAutomationCell(uint row, uint column) => ReadCell((row, column));

    internal void UpdateAutomationViewport(
        Control surface,
        IReadOnlyList<(uint Row, uint Column)> coordinates,
        Func<uint, uint, WorksheetAutomationCell> readCell)
    {
        if (_closed)
            return;
        _surface = surface;
        _coordinates = coordinates;
        _readCell = readCell;
        var visible = coordinates.ToHashSet();
        foreach (var key in _cellPeers.Keys.ToArray())
        {
            if (!visible.Contains(key) && !ReadCell(key).IsActive)
                _cellPeers.Remove(key);
        }
        QueuePublication();
    }

    internal void ClearAutomationViewport()
    {
        _surface = null;
        _readCell = null;
        _coordinates = [];
        _borders.Clear();
        foreach (var peer in _cellPeers.Values)
            peer.PublishChanges();
        _cellPeers.Clear();
        QueuePublication();
    }

    internal void CloseAutomationViewport()
    {
        _closed = true;
        ClearAutomationViewport();
    }

    internal AutomationPeer GetCellPeer(uint row, uint column)
    {
        var key = (row, column);
        if (_closed)
            return new WorksheetCellAutomationPeer(this, key);
        if (!_cellPeers.TryGetValue(key, out var peer))
            _cellPeers[key] = peer = new WorksheetCellAutomationPeer(this, key);
        return peer;
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new WorksheetGridAutomationPeer(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == ContentProperty)
            QueuePublication();
    }

    private WorksheetAutomationCell ReadCell((uint Row, uint Column) key) =>
        _readCell?.Invoke(key.Row, key.Column) ?? new(
            $"{CellAddress.NumberToColumnName(key.Column)}{key.Row}", "", "", false, false, default);

    private Matrix? SurfaceTransform() => _surface is not null && TopLevel.GetTopLevel(_surface) is { } root
        ? _surface.TransformToVisual(root)
        : null;

    private Rect CellBounds((uint Row, uint Column) key)
    {
        var bounds = ReadCell(key).Bounds;
        return bounds.Width > 0 && bounds.Height > 0 && SurfaceTransform() is { } transform
            ? bounds.TransformToAABB(transform)
            : default;
    }

    private WorksheetCellBorder? BorderFor((uint Row, uint Column) key)
    {
        if (_bordersDirty)
        {
            _borders.Clear();
            if (_surface is not null)
            {
                foreach (var border in _surface.GetVisualDescendants().OfType<WorksheetCellBorder>())
                {
                    if (ReferenceEquals(border.AutomationHost, this))
                        _borders[border.AutomationCoordinate] = border;
                }
            }
            _bordersDirty = false;
        }
        return _borders.GetValueOrDefault(key);
    }

    private void QueuePublication()
    {
        _bordersDirty = true;
        _publishPending = true;
        if (_publishQueued)
            return;
        _publishQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _publishQueued = false;
            PublishChanges();
        }, DispatcherPriority.Loaded);
    }

    private void PublishChanges()
    {
        var transform = SurfaceTransform();
        if (!_publishPending && transform == _publishedTransform)
            return;
        _publishPending = false;
        _publishedTransform = transform;
        foreach (var peer in _cellPeers.Values.ToArray())
            peer.PublishChanges();
        if (ControlAutomationPeer.FromElement(this) is WorksheetGridAutomationPeer grid)
            grid.NotifyViewportChanged();
    }

    private sealed class WorksheetGridAutomationPeer(WorksheetGridHost owner)
        : ControlAutomationPeer(owner), ISelectionProvider
    {
        private AutomationPeer[] _publishedSelection = [];
        public bool CanSelectMultiple => true;
        public bool IsSelectionRequired => false;

        public IReadOnlyList<AutomationPeer> GetSelection() => owner._coordinates
            .Where(key => owner.ReadCell(key).IsSelected)
            .Select(key => owner.GetCellPeer(key.Row, key.Column)).ToArray();

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataGrid;
        protected override IReadOnlyList<AutomationPeer> GetChildrenCore() => owner._closed ? [] : owner._coordinates
            .Select(key => owner.GetCellPeer(key.Row, key.Column))
            .Concat(base.GetChildrenCore() ?? []).ToArray();

        internal void NotifyViewportChanged()
        {
            InvalidateChildren();
            var selection = GetSelection().ToArray();
            if (!_publishedSelection.SequenceEqual(selection))
                RaisePropertyChangedEvent(SelectionPatternIdentifiers.SelectionProperty, _publishedSelection, selection);
            _publishedSelection = selection;
        }
    }

    // A control peer cannot follow replacement controls: its Owner and subscriptions are immutable.
    // Coordinate peers instead resolve every property from the host's current viewport, as on WPF.
    private sealed class WorksheetCellAutomationPeer : AutomationPeer, IValueProvider, ISelectionItemProvider
    {
        private readonly WorksheetGridHost _host;
        private readonly (uint Row, uint Column) _key;
        private WorksheetAutomationCell _published;
        private Rect _publishedBounds;
        private IReadOnlyList<AutomationPeer> _publishedChildren = [];

        internal WorksheetCellAutomationPeer(WorksheetGridHost host, (uint Row, uint Column) key)
        {
            _host = host;
            _key = key;
            _published = State;
            _publishedBounds = host.CellBounds(key);
        }

        private WorksheetAutomationCell State => _host.ReadCell(_key);
        private WorksheetCellBorder? Border => _host.BorderFor(_key);
        public bool IsReadOnly => true;
        public string Value => State.Value;
        public bool IsSelected => State.IsSelected;
        public ISelectionProvider? SelectionContainer =>
            ControlAutomationPeer.CreatePeerForElement(_host).GetProvider<ISelectionProvider>();

        public void SetValue(string? value) => throw new InvalidOperationException("Use the worksheet editor to change a cell value.");
        public void AddToSelection() => throw new InvalidOperationException("Use worksheet navigation to change the selection.");
        public void RemoveFromSelection() => throw new InvalidOperationException("Use worksheet navigation to change the selection.");
        public void Select() => throw new InvalidOperationException("Use worksheet navigation to change the selection.");

        protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.DataItem;
        protected override string GetClassNameCore() => "GridViewCell";
        protected override string GetLocalizedControlTypeCore() => "cell";
        protected override string GetAutomationIdCore() => $"Cell_{CellAddress.NumberToColumnName(_key.Column)}{_key.Row}";
        protected override string GetNameCore() => State.Name;
        protected override string GetHelpTextCore() => State.HelpText;
        protected override Rect GetBoundingRectangleCore() => _host.CellBounds(_key);
        protected override AutomationPeer GetParentCore() => ControlAutomationPeer.CreatePeerForElement(_host);
        protected override bool TrySetParent(AutomationPeer? parent) => parent is null || ReferenceEquals(parent, GetParentCore());
        protected override bool HasKeyboardFocusCore() => State.IsActive && Border?.IsFocused == true;
        protected override bool IsContentElementCore() => true;
        protected override bool IsControlElementCore() => true;
        protected override bool IsEnabledCore() => _host._readCell is not null && _host.IsEffectivelyEnabled;
        protected override bool IsKeyboardFocusableCore() => true;
        protected override bool IsOffscreenCore() => !_host.IsEffectivelyVisible || GetBoundingRectangleCore().Size == default;
        protected override void SetFocusCore() => Border?.Focus();
        protected override void BringIntoViewCore() => Border?.BringIntoView();
        protected override string GetAcceleratorKeyCore() => "";
        protected override string GetAccessKeyCore() => "";
        protected override AutomationPeer? GetLabeledByCore() => null;
        protected override bool ShowContextMenuCore()
        {
            if (Border is not { ContextMenu: { } menu } border)
                return false;
            menu.Open(border);
            return true;
        }

        protected override IReadOnlyList<AutomationPeer> GetOrCreateChildrenCore() =>
            Border?.GetVisualDescendants().OfType<TextBox>()
                .Where(editor => editor.IsEffectivelyVisible)
                .Select(ControlAutomationPeer.CreatePeerForElement).ToArray() ?? [];

        internal void PublishChanges()
        {
            var state = State;
            var bounds = GetBoundingRectangleCore();
            Notify(AutomationElementIdentifiers.NameProperty, _published.Name, state.Name);
            Notify(AutomationElementIdentifiers.HelpTextProperty, _published.HelpText, state.HelpText);
            Notify(ValuePatternIdentifiers.ValueProperty, _published.Value, state.Value);
            Notify(SelectionItemPatternIdentifiers.IsSelectedProperty, _published.IsSelected, state.IsSelected);
            Notify(AutomationElementIdentifiers.BoundingRectangleProperty, _publishedBounds, bounds);
            var children = GetOrCreateChildrenCore();
            if (!_publishedChildren.SequenceEqual(children))
                RaiseChildrenChangedEvent();
            _publishedChildren = children;
            _published = state;
            _publishedBounds = bounds;
        }

        private void Notify<T>(AutomationProperty property, T before, T after)
        {
            if (!EqualityComparer<T>.Default.Equals(before, after))
                RaisePropertyChangedEvent(property, before, after);
        }
    }
}

/// <summary>Retains worksheet overlays in the visual accessibility tree without duplicating cells.</summary>
internal sealed class WorksheetGridSurface : Grid
{
    protected override Type StyleKeyOverride => typeof(Grid);
    protected override AutomationPeer OnCreateAutomationPeer() => new WorksheetGridSurfaceAutomationPeer(this);

    private sealed class WorksheetGridSurfaceAutomationPeer(WorksheetGridSurface owner) : ControlAutomationPeer(owner)
    {
        protected override IReadOnlyList<AutomationPeer> GetChildrenCore() =>
            owner.GetVisualChildren().OfType<Control>()
                .Where(child => child is not WorksheetCellBorder && child.IsVisible)
                .Select(CreatePeerForElement).ToArray();
    }
}

internal sealed class WorksheetCellBorder : Border
{
    internal WorksheetGridHost? AutomationHost { get; private set; }
    internal (uint Row, uint Column) AutomationCoordinate { get; private set; }

    internal void SetAutomationOwner(WorksheetGridHost host, uint row, uint column)
    {
        AutomationHost = host;
        AutomationCoordinate = (row, column);
        var state = host.ReadAutomationCell(row, column);
        AutomationProperties.SetAutomationId(this, $"Cell_{CellAddress.NumberToColumnName(column)}{row}");
        AutomationProperties.SetName(this, state.Name);
        AutomationProperties.SetHelpText(this, state.HelpText);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => AutomationHost is { } host
        ? host.GetCellPeer(AutomationCoordinate.Row, AutomationCoordinate.Column)
        : base.OnCreateAutomationPeer();
}

internal sealed class WorksheetInlineCellEditor : TextBox
{
    protected override Type StyleKeyOverride => typeof(TextBox);
    protected override AutomationPeer OnCreateAutomationPeer() => new WorksheetInlineCellEditorAutomationPeer(this);

    private sealed class WorksheetInlineCellEditorAutomationPeer(WorksheetInlineCellEditor owner)
        : TextBoxAutomationPeer(owner)
    {
        protected override AutomationPeer? GetParentCore() =>
            owner.GetVisualAncestors().OfType<WorksheetCellBorder>().FirstOrDefault() is { } border
                ? CreatePeerForElement(border)
                : base.GetParentCore();
    }
}
