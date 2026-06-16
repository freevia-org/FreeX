namespace Free.Shared.Ribbon;

/// <summary>Fluent, type-safe construction of a <see cref="RibbonDefinition"/>.</summary>
public sealed class RibbonDefinitionBuilder
{
    private readonly List<RibbonTab> _tabs = new();

    public RibbonDefinitionBuilder Tab(string id, string header, string? keyTip, Action<RibbonTabBuilder> configure)
        => AddTab(id, header, keyTip, context: null, configure);

    public RibbonDefinitionBuilder ContextualTab(string id, string header, RibbonTabContext context, Action<RibbonTabBuilder> configure)
        => AddTab(id, header, keyTip: null, context, configure);

    private RibbonDefinitionBuilder AddTab(string id, string header, string? keyTip, RibbonTabContext? context, Action<RibbonTabBuilder> configure)
    {
        var builder = new RibbonTabBuilder(id, header, keyTip, context);
        configure(builder);
        _tabs.Add(builder.Build());
        return this;
    }

    public RibbonDefinition Build() => new(_tabs.ToArray());
}

public sealed class RibbonTabBuilder
{
    private readonly string _id;
    private readonly string _header;
    private readonly string? _keyTip;
    private readonly RibbonTabContext? _context;
    private readonly List<RibbonGroup> _groups = new();

    internal RibbonTabBuilder(string id, string header, string? keyTip, RibbonTabContext? context)
    {
        _id = id;
        _header = header;
        _keyTip = keyTip;
        _context = context;
    }

    public RibbonTabBuilder Group(string id, string header, string? keyTip, int priority, Action<RibbonGroupBuilder> configure)
    {
        var builder = new RibbonGroupBuilder(id, header, keyTip, priority);
        configure(builder);
        _groups.Add(builder.Build());
        return this;
    }

    internal RibbonTab Build() => new(_id, _header, _keyTip, _context, _groups.ToArray());
}

public sealed class RibbonGroupBuilder
{
    private readonly string _id;
    private readonly string _header;
    private readonly string? _keyTip;
    private readonly int _priority;
    private readonly List<RibbonControl> _controls = new();
    private RibbonGroupSizing _sizing = RibbonGroupSizing.Default;

    internal RibbonGroupBuilder(string id, string header, string? keyTip, int priority)
    {
        _id = id;
        _header = header;
        _keyTip = keyTip;
        _priority = priority;
    }

    public RibbonGroupBuilder Button(string commandId, string label, Func<RibbonButton, RibbonButton>? configure = null)
        => Add(new RibbonButton(commandId, label), configure);

    public RibbonGroupBuilder Toggle(string commandId, string label, Func<RibbonToggleButton, RibbonToggleButton>? configure = null)
        => Add(new RibbonToggleButton(commandId, label), configure);

    public RibbonGroupBuilder CheckBox(string commandId, string label, Func<RibbonCheckBox, RibbonCheckBox>? configure = null)
        => Add(new RibbonCheckBox(commandId, label), configure);

    public RibbonGroupBuilder ComboBox(string commandId, string label, Func<RibbonComboBox, RibbonComboBox>? configure = null)
        => Add(new RibbonComboBox(commandId, label), configure);

    public RibbonGroupBuilder SplitButton(string commandId, string label, RibbonMenu menu, Func<RibbonSplitButton, RibbonSplitButton>? configure = null)
        => Add(new RibbonSplitButton(commandId, label, menu), configure);

    public RibbonGroupBuilder Dropdown(string commandId, string label, RibbonMenu menu, Func<RibbonDropdown, RibbonDropdown>? configure = null)
        => Add(new RibbonDropdown(commandId, label, menu), configure);

    public RibbonGroupBuilder Gallery(string commandId, string label, Func<RibbonGallery, RibbonGallery>? configure = null)
        => Add(new RibbonGallery(commandId, label), configure);

    public RibbonGroupBuilder Separator()
    {
        _controls.Add(new RibbonSeparator());
        return this;
    }

    public RibbonGroupBuilder Sizing(RibbonGroupSizing sizing)
    {
        _sizing = sizing;
        return this;
    }

    private RibbonGroupBuilder Add<T>(T control, Func<T, T>? configure) where T : RibbonControl
    {
        _controls.Add(configure is null ? control : configure(control));
        return this;
    }

    internal RibbonGroup Build() => new(_id, _header, _keyTip, _priority, _controls.ToArray(), _sizing);
}
